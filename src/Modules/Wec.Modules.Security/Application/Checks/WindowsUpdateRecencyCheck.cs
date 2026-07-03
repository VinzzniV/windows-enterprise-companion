using System.Globalization;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class WindowsUpdateRecencyCheck : ISecurityCheck
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;
    private readonly SecurityOptions _options;

    public WindowsUpdateRecencyCheck(
        IWmiQueryService wmiQueryService,
        IClock clock,
        IOptions<SecurityOptions> options)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
        _options = options.Value;
    }

    public string CheckId => "WEC-SEC-PATCHLEVEL";

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> hotfixes = await _wmiQueryService.QueryAsync(
            context,
            CimV2Namespace,
            "SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering",
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (hotfixes.IsFailure)
        {
            return [CheckFindings.NotRun(
                CheckId,
                "Installed update history could not be determined",
                FindingCategory.OperatingSystem,
                "Windows Update",
                "Verify the Windows Management Instrumentation service and retry the scan.",
                hotfixes.Error!,
                capturedAtUtc)];
        }

        DateTimeOffset newestInstall = hotfixes.Value
            .Select(hotfix => ParseInstalledOn(hotfix.GetString("InstalledOn")))
            .Where(installedOn => installedOn is not null)
            .Select(installedOn => installedOn!.Value)
            .DefaultIfEmpty()
            .Max();

        if (newestInstall == default)
        {
            return [CheckFindings.NotRun(
                CheckId,
                "No installed update dates could be read",
                FindingCategory.OperatingSystem,
                "Windows Update",
                "Check the update history in Windows Update manually.",
                Error.NotFound("Win32_QuickFixEngineering returned no parseable InstalledOn dates."),
                capturedAtUtc)];
        }

        double daysSinceLastUpdate = (_clock.UtcNow - newestInstall).TotalDays;
        if (daysSinceLastUpdate <= _options.MaxDaysSinceLastInstalledUpdate)
        {
            return [];
        }

        return [new SecurityFinding(
            $"{CheckId}-STALE",
            $"No updates installed for {(int)daysSinceLastUpdate} days",
            "The newest entry in the installed-update history is older than the configured "
                + "threshold. The machine is likely missing recent security updates.",
            FindingSeverity.Medium,
            FindingCategory.OperatingSystem,
            "Windows Update",
            new Dictionary<string, string>
            {
                ["lastInstalledUpdateUtc"] = newestInstall.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["daysSinceLastUpdate"] = ((int)daysSinceLastUpdate).ToString(CultureInfo.InvariantCulture),
                ["threshold"] = _options.MaxDaysSinceLastInstalledUpdate.ToString(CultureInfo.InvariantCulture),
                ["source"] = $@"{CimV2Namespace}\Win32_QuickFixEngineering",
            },
            "Run Windows Update and verify the update service, network access to the update "
                + "source, and any WSUS/Intune assignment.",
            RequiredPrivilege: null,
            capturedAtUtc)];
    }

    // InstalledOn is a localized string in most providers ("7/2/2026" or
    // "02.07.2026"); a few return DMTF or empty values.
    internal static DateTimeOffset? ParseInstalledOn(string? installedOn)
    {
        if (string.IsNullOrWhiteSpace(installedOn))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                installedOn,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset invariantResult))
        {
            return invariantResult;
        }

        if (DateTimeOffset.TryParse(
                installedOn,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset localizedResult))
        {
            return localizedResult;
        }

        return null;
    }
}
