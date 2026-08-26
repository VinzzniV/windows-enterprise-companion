using Microsoft.Management.Deployment;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;

namespace Wec.Infrastructure.SoftwareUpdates;

public sealed class ComWingetCatalogClient : IWingetCatalogClient
{
    private const string SourceName = "winget";
    private static readonly Lazy<bool> Initialized = new(InitializeInProcessApi);
    internal sealed record WingetInstallerEligibility(bool Eligible, string? Reason);

    public Task<Result<IReadOnlyList<WingetPackageInfo>>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 256 || limit is < 1 or > 100)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<WingetPackageInfo>>(new Error(
                ErrorCode.InvalidRequest,
                "Enter a Winget package name or id and request between 1 and 100 results.")));
        }

        return RunAsync(() => SearchCore(query.Trim(), limit), cancellationToken);
    }

    public async Task<Result<WingetPackageInfo>> GetExactAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId) || packageId.Length > 256)
        {
            return Result.Failure<WingetPackageInfo>(new Error(
                ErrorCode.InvalidRequest,
                "The Winget package id is invalid."));
        }

        Result<IReadOnlyList<WingetPackageInfo>> result = await RunAsync(
            () => SearchCore(packageId.Trim(), 25, exactId: true), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<WingetPackageInfo>(result.Error!);
        }

        WingetPackageInfo? match = result.Value.FirstOrDefault(package =>
            string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? Result.Failure<WingetPackageInfo>(Error.NotFound(
                $"Winget package '{packageId}' was not found in source '{SourceName}'."))
            : Result.Success(match);
    }

    public Task<Result<int>> CompareVersionsAsync(
        string packageId,
        string leftVersion,
        string rightVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(leftVersion)
            || string.IsNullOrWhiteSpace(rightVersion))
        {
            return Task.FromResult(Result.Failure<int>(new Error(
                ErrorCode.InvalidRequest,
                "The Winget package id and both versions are required.")));
        }

        return RunAsync(() => CompareCore(packageId, leftVersion, rightVersion), cancellationToken);
    }

    private static IReadOnlyList<WingetPackageInfo> SearchCore(string query, int limit, bool exactId = false)
    {
        PackageCatalog catalog = ConnectCatalog();
        FindPackagesOptions options = BuildSearchOptions(query, limit, exactId);

        FindPackagesResult found = catalog.FindPackages(options);
        if (found.Status != FindPackagesResultStatus.Ok)
        {
            throw new InvalidOperationException(
                "The Winget catalog search failed.",
                found.ExtendedErrorCode);
        }

        return [.. found.Matches
            .Select(match => Map(match.CatalogPackage))
            .OrderByDescending(package => string.Equals(package.Id, query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)];
    }

    internal static FindPackagesOptions BuildSearchOptions(string query, int limit, bool exactId)
    {
        _ = Initialized.Value;
        FindPackagesOptions options = new();
        PackageMatchFilter filter = new();
        filter.Field = exactId ? PackageMatchField.Id : PackageMatchField.CatalogDefault;
        filter.Option = exactId
            ? PackageFieldMatchOption.EqualsCaseInsensitive
            : PackageFieldMatchOption.ContainsCaseInsensitive;
        filter.Value = query;
        options.Selectors.Add(filter);
        options.ResultLimit = (uint)limit;
        return options;
    }

    private static int CompareCore(string packageId, string leftVersion, string rightVersion)
    {
        PackageCatalog catalog = ConnectCatalog();
        FindPackagesOptions options = new();
        PackageMatchFilter filter = new();
        filter.Field = PackageMatchField.Id;
        filter.Option = PackageFieldMatchOption.Equals;
        filter.Value = packageId;
        options.Filters.Add(filter);
        options.ResultLimit = 1;

        FindPackagesResult found = catalog.FindPackages(options);
        CatalogPackage? package = found.Matches.Count == 0 ? null : found.Matches[0].CatalogPackage;
        if (found.Status != FindPackagesResultStatus.Ok || package is null)
        {
            throw new InvalidOperationException($"Winget package '{packageId}' is unavailable.");
        }

        PackageVersionInfo? versionInfo = package.AvailableVersions
            .Select(package.GetPackageVersionInfo)
            .FirstOrDefault(version => string.Equals(version.Version, leftVersion, StringComparison.OrdinalIgnoreCase))
            ?? package.DefaultInstallVersion;
        return versionInfo.CompareToVersion(rightVersion) switch
        {
            CompareResult.Greater => 1,
            CompareResult.Lesser => -1,
            CompareResult.Equal => 0,
            _ => StringComparer.OrdinalIgnoreCase.Compare(leftVersion, rightVersion),
        };
    }

    private static PackageCatalog ConnectCatalog()
    {
        _ = Initialized.Value;
        PackageManager manager = new();
        PackageCatalogReference reference = manager.GetPackageCatalogByName(SourceName);
        ConnectResult connection = reference.Connect();
        if (connection.Status != ConnectResultStatus.Ok || connection.PackageCatalog is null)
        {
            throw new InvalidOperationException(
                "The public Winget catalog could not be opened.",
                connection.ExtendedErrorCode);
        }

        return connection.PackageCatalog;
    }

    private static bool InitializeInProcessApi()
    {
        PackageManagerSettings settings = new();
        settings.SetCallerIdentifier("WindowsEnterpriseCompanion");
        return true;
    }

    private static WingetPackageInfo Map(CatalogPackage package)
    {
        PackageVersionInfo version = package.DefaultInstallVersion;
        InstallOptions options = new();
        options.PackageInstallScope = PackageInstallScope.System;
        options.PackageInstallMode = PackageInstallMode.Silent;

        bool hasSystemInstaller = version.HasApplicableInstaller(options);
        PackageInstallerInfo? installer = hasSystemInstaller ? version.GetApplicableInstaller(options) : null;
        string installerType = installer?.InstallerType.ToString() ?? "Unknown";
        WingetInstallerEligibility eligibility = AssessInstaller(
            hasSystemInstaller,
            installer?.InstallerType,
            installer?.Scope,
            installer?.ElevationRequirement);

        return new WingetPackageInfo(
            package.Id,
            package.Name,
            version.Publisher ?? string.Empty,
            version.Version,
            SourceName,
            installerType,
            installer?.Architecture.ToString() ?? "Unknown",
            installer?.Scope.ToString() ?? "Unknown",
            eligibility.Eligible,
            eligibility.Reason);
    }

    internal static WingetInstallerEligibility AssessInstaller(
        bool hasSystemInstaller,
        PackageInstallerType? installerType,
        PackageInstallerScope? scope,
        ElevationRequirement? elevationRequirement)
    {
        bool supportedType = installerType is not null
            && installerType is not (
                PackageInstallerType.MSStore or
                PackageInstallerType.Portable or
                PackageInstallerType.Zip or
                PackageInstallerType.Font or
                PackageInstallerType.Unknown);
        bool supportedScope = scope is not null
            && scope is not PackageInstallerScope.User
            && elevationRequirement is not ElevationRequirement.ElevationProhibited;
        bool eligible = hasSystemInstaller && supportedType && supportedScope;
        string? reason = eligible
            ? null
            : !hasSystemInstaller
                ? "No machine-wide installer is applicable on the WEC host."
                : !supportedType
                    ? $"Installer type '{installerType?.ToString() ?? "Unknown"}' is not supported for opsi/SYSTEM packages."
                    : "The applicable installer is user-scoped or prohibits elevation.";
        return new WingetInstallerEligibility(eligible, reason);
    }

    private static async Task<Result<T>> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        try
        {
            Task<T> operationTask = Task.Run(operation, CancellationToken.None);
            T value = await operationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(value);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<T>(new Error(
                ErrorCode.ConnectionTimeout,
                "The Winget catalog request timed out or was cancelled."));
        }
        catch (Exception exception)
        {
            return Result.Failure<T>(new Error(
                ErrorCode.ServiceUnavailable,
                "The Winget catalog is unavailable. Ensure App Installer and the public winget source are installed.")
            {
                Details = exception.Message,
            });
        }
    }
}
