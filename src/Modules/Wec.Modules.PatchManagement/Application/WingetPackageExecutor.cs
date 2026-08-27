using Wec.Core.Abstractions;
using Wec.Core.Opsi;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Application;

internal sealed class WingetPackageExecutor
{
    private readonly IWingetManagedPackageRepository _repository;
    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly OpsiSessionConnector _sessionConnector;
    private readonly IRemoteFileUploader _fileUploader;
    private readonly IRemoteCommandExecutor _commandExecutor;
    private readonly IClock _clock;
    private readonly PatchManagementOptions _options;

    public WingetPackageExecutor(
        IWingetManagedPackageRepository repository,
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        OpsiSessionConnector sessionConnector,
        IRemoteFileUploader fileUploader,
        IRemoteCommandExecutor commandExecutor,
        IClock clock,
        PatchManagementOptions options)
    {
        _repository = repository;
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _sessionConnector = sessionConnector;
        _fileUploader = fileUploader;
        _commandExecutor = commandExecutor;
        _clock = clock;
        _options = options;
    }

    public async Task<Result<WingetPackageOperationOutcome>> ExecuteSafelyAsync(
        WingetPackagePreview preview,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(preview, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure<WingetPackageOperationOutcome>(new Error(
                ErrorCode.RemoteCommandFailed,
                $"The Winget package '{preview.OpsiProductId}' could not be generated, installed or verified.")
            {
                Details = exception.Message,
            });
        }
    }

    public string BuildRemoteBuildCommand(WingetPackagePreview preview, string remoteArchive)
    {
        string token = Guid.NewGuid().ToString("N");
        string root = _options.WingetWorkbenchRoot.TrimEnd('/');
        string final = $"{root}/{preview.OpsiProductId}";
        string stage = $"{root}/.{preview.OpsiProductId}.staging-{token}";
        string backup = $"{root}/.{preview.OpsiProductId}.backup-{token}";
        string packageName = $"{preview.OpsiProductId}_{preview.WingetVersion}-{preview.PackageVersion}.opsi";
        string script = string.Join("; ",
            "set -eu",
            $"root={ShellQuote(root)}",
            $"final={ShellQuote(final)}",
            $"stage={ShellQuote(stage)}",
            $"backup={ShellQuote(backup)}",
            $"archive={ShellQuote(remoteArchive)}",
            "had_previous=0",
            "swapped=0",
            "cleanup_temp() { rm -rf -- \"$stage\"; rm -f -- \"$archive\"; }",
            "restore_previous() { if test \"$swapped\" = 1; then rm -rf -- \"$final\"; if test \"$had_previous\" = 1 && test -d \"$backup\"; then mv -- \"$backup\" \"$final\"; fi; fi; }",
            "trap 'status=$?; if test \"$status\" -ne 0; then restore_previous; fi; cleanup_temp; exit \"$status\"' EXIT",
            "mkdir -p -- \"$root\"",
            "rm -rf -- \"$stage\" \"$backup\"",
            "mkdir -- \"$stage\"",
            "tar -xzf \"$archive\" -C \"$stage\"",
            "test -f \"$stage/OPSI/control.toml\"",
            "cd \"$stage\"",
            "opsi-makepackage --no-zsync --no-md5",
            $"test -f \"$stage/{packageName}\"",
            "if test -d \"$final\"; then mv -- \"$final\" \"$backup\"; had_previous=1; fi",
            "mv -- \"$stage\" \"$final\"",
            "swapped=1",
            $"opsi-package-manager --quiet -i \"$final/{packageName}\"",
            "rm -rf -- \"$backup\"",
            "trap - EXIT",
            "cleanup_temp",
            $"printf 'WEC_PACKAGE=%s\\n' {ShellQuote(packageName)}");
        string prefix = _options.UseNonInteractiveSudo ? "sudo -n " : string.Empty;
        return $"{prefix}sh -c {ShellQuote(script)}";
    }

    private async Task<Result<WingetPackageOperationOutcome>> ExecuteAsync(
        WingetPackagePreview preview,
        CancellationToken cancellationToken)
    {
        Result<OpsiSession> session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        if (session.IsFailure)
        {
            return Result.Failure<WingetPackageOperationOutcome>(session.Error!);
        }
        string sshUserName = string.IsNullOrWhiteSpace(_options.SshUserName)
            ? session.Value.Connection.UserName
            : _options.SshUserName.Trim();

        GeneratedWingetOpsiPackage generated = WingetOpsiPackageTemplate.Generate(
            new WingetOpsiPackageTemplateInput(
                preview.OpsiProductId,
                preview.DisplayName,
                preview.WingetId,
                preview.WingetVersion,
                preview.PackageVersion));
        string remoteArchive = $"/tmp/wec-winget-{Guid.NewGuid():N}.tar.gz";
        try
        {
            Result<RemoteFileUploadResult> upload = await _fileUploader.UploadAsync(
                new RemoteFileUploadRequest(
                    generated.ArchivePath,
                    preview.DepotId,
                    sshUserName,
                    remoteArchive,
                    _options.SshConnectTimeout,
                    _options.PackageTransferTimeout,
                    _options.SshIdentityFile),
                cancellationToken).ConfigureAwait(false);
            if (upload.IsFailure)
            {
                return Result.Failure<WingetPackageOperationOutcome>(upload.Error!);
            }

            string command = BuildRemoteBuildCommand(preview, remoteArchive);
            Result<RemoteCommandResult> execution = await _commandExecutor.ExecuteAsync(
                new RemoteCommandRequest(
                    preview.DepotId,
                    sshUserName,
                    command,
                    _options.SshConnectTimeout,
                    _options.PackageCommandTimeout,
                    _options.SshIdentityFile),
                cancellationToken).ConfigureAwait(false);
            if (execution.IsFailure)
            {
                return Result.Failure<WingetPackageOperationOutcome>(execution.Error!);
            }

            Result<IReadOnlyList<OpsiProductOnDepot>> depotProducts = await _opsiClient
                .GetProductsOnDepotsAsync(session.Value.Connection, cancellationToken)
                .ConfigureAwait(false);
            if (depotProducts.IsFailure)
            {
                return Result.Failure<WingetPackageOperationOutcome>(depotProducts.Error!);
            }
            OpsiProductOnDepot? installed = FindDepotProduct(
                depotProducts.Value,
                preview.OpsiProductId,
                preview.DepotId);
            string? installedVersion = FormatVersion(installed);
            if (!string.Equals(installedVersion, preview.TargetDepotVersion, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<WingetPackageOperationOutcome>(new Error(
                    ErrorCode.RemoteCommandFailed,
                    $"The opsi depot reports '{installedVersion ?? "no package"}' instead of '{preview.TargetDepotVersion}'."));
            }

            DateTimeOffset now = _clock.UtcNow;
            WingetManagedPackage? existing = await _repository.FindByProductIdAsync(
                preview.OpsiProductId,
                cancellationToken);
            await _repository.UpsertAsync(new WingetManagedPackage(
                existing?.Id ?? 0,
                preview.OpsiProductId,
                preview.WingetId,
                preview.Source,
                preview.Scope,
                preview.DepotId,
                preview.DisplayName,
                preview.WingetVersion,
                WingetOpsiPackageTemplate.Version,
                preview.WingetVersion,
                "SUCCESS",
                now,
                null,
                existing?.CreatedAtUtc ?? now,
                now), cancellationToken);

            return Result.Success(new WingetPackageOperationOutcome(
                preview.OpsiProductId,
                preview.DepotId,
                true,
                preview.CurrentDepotVersion,
                installedVersion,
                null));
        }
        finally
        {
            File.Delete(generated.ArchivePath);
        }
    }

    private async Task<Result<OpsiSession>> GetSessionAsync(CancellationToken cancellationToken)
    {
        Result<OpsiSession?> ensured = await _sessionConnector.EnsureConnectedAsync(cancellationToken)
            .ConfigureAwait(false);
        if (ensured.IsFailure)
        {
            return Result.Failure<OpsiSession>(ensured.Error!);
        }
        return _sessionState.Current is { } session
            ? Result.Success(session)
            : Result.Failure<OpsiSession>(PatchDashboardService.NotConnected);
    }

    private static OpsiProductOnDepot? FindDepotProduct(
        IReadOnlyList<OpsiProductOnDepot> products,
        string productId,
        string depotId) => products.FirstOrDefault(item =>
            string.Equals(item.ProductId, productId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.DepotId, depotId, StringComparison.OrdinalIgnoreCase));

    private static string? FormatVersion(OpsiProductOnDepot? product) => product is null
        ? null
        : string.IsNullOrWhiteSpace(product.PackageVersion)
            ? product.ProductVersion
            : $"{product.ProductVersion}-{product.PackageVersion}";

    private static string ShellQuote(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
