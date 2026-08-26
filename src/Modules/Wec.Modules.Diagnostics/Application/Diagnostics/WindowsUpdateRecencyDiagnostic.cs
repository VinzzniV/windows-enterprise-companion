using System.Globalization;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class WindowsUpdateRecencyDiagnostic : IDiagnostic
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public WindowsUpdateRecencyDiagnostic(
        IWmiQueryService wmiQueryService,
        Microsoft.Extensions.Options.IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => DeviceHealthDiagnosticIds.WindowsUpdateRecency;

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> hotfixes = await _wmiQueryService.QueryAsync(
            context,
            CimV2Namespace,
            "SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering",
            cancellationToken);
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (hotfixes.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Installed update history could not be read",
                new Dictionary<string, string> { ["errorMessage"] = hotfixes.Error!.Message },
                ["Verify the Windows Management Instrumentation service and rerun the diagnostics."],
                capturedAtUtc)];
        }

        (DateTimeOffset InstalledOn, string HotFixId)? newest = hotfixes.Value
            .Select(hotfix => (
                InstalledOn: ParseInstalledOn(hotfix.GetString("InstalledOn")),
                HotFixId: hotfix.GetString("HotFixID") ?? "unknown"))
            .Where(entry => entry.InstalledOn is not null)
            .Select(entry => (entry.InstalledOn!.Value, entry.HotFixId))
            .OrderByDescending(entry => entry.Item1)
            .Cast<(DateTimeOffset, string)?>()
            .FirstOrDefault();

        if (newest is null)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "No installed update dates could be read",
                new Dictionary<string, string> { ["hotfixCount"] = hotfixes.Value.Count.ToString(CultureInfo.InvariantCulture) },
                ["Check the update history in Windows Update manually."],
                capturedAtUtc)];
        }

        int daysSinceLastUpdate = (int)(_clock.UtcNow - newest.Value.InstalledOn).TotalDays;
        var evidence = new Dictionary<string, string>
        {
            ["lastInstalledUpdate"] = newest.Value.InstalledOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["hotfix"] = newest.Value.HotFixId,
            ["daysSinceLastUpdate"] = daysSinceLastUpdate.ToString(CultureInfo.InvariantCulture),
        };

        if (daysSinceLastUpdate > _options.MaxDaysSinceLastInstalledUpdate)
        {
            return [BuildResult(
                DiagnosticStatus.Warning,
                $"No updates installed for {daysSinceLastUpdate} days",
                evidence,
                ["Run Windows Update and verify the update service and WSUS/Intune assignment."],
                capturedAtUtc)];
        }

        return [BuildResult(
            DiagnosticStatus.Pass,
            "Windows updates are recent",
            evidence,
            [],
            capturedAtUtc)];
    }

    private static DateTimeOffset? ParseInstalledOn(string? installedOn)
    {
        if (string.IsNullOrWhiteSpace(installedOn))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(installedOn, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset invariantResult))
        {
            return invariantResult;
        }

        return DateTimeOffset.TryParse(installedOn, CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset localizedResult)
            ? localizedResult
            : null;
    }

    private DiagnosticResult BuildResult(
        DiagnosticStatus status,
        string title,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps,
        DateTimeOffset capturedAtUtc) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.System,
        "Windows Update",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
