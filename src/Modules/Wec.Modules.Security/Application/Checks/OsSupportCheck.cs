using System.Globalization;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class OsSupportCheck : ISecurityCheck
{
    private const string CimV2Namespace = @"root\cimv2";

    // Offline snapshot of Microsoft lifecycle dates (Home/Pro; Enterprise/LTSC
    // editions differ). Deliberately static — the check must not require
    // internet access. Update this table together with app releases.
    private static readonly Dictionary<int, (string Name, DateTimeOffset EndOfSupportUtc)> LifecycleByBuild =
        new()
        {
            [19044] = ("Windows 10 21H2", new DateTimeOffset(2023, 6, 13, 0, 0, 0, TimeSpan.Zero)),
            [19045] = ("Windows 10 22H2", new DateTimeOffset(2025, 10, 14, 0, 0, 0, TimeSpan.Zero)),
            [22000] = ("Windows 11 21H2", new DateTimeOffset(2023, 10, 10, 0, 0, 0, TimeSpan.Zero)),
            [22621] = ("Windows 11 22H2", new DateTimeOffset(2024, 10, 8, 0, 0, 0, TimeSpan.Zero)),
            [22631] = ("Windows 11 23H2", new DateTimeOffset(2025, 11, 11, 0, 0, 0, TimeSpan.Zero)),
            [26100] = ("Windows 11 24H2", new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero)),
            [26200] = ("Windows 11 25H2", new DateTimeOffset(2027, 10, 12, 0, 0, 0, TimeSpan.Zero)),
        };

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public OsSupportCheck(IWmiQueryService wmiQueryService, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-OSSUPPORT";

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> operatingSystems = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT Caption, BuildNumber FROM Win32_OperatingSystem",
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (operatingSystems.IsFailure || operatingSystems.Value.Count == 0)
        {
            Error error = operatingSystems.IsFailure
                ? operatingSystems.Error!
                : Error.NotFound("Win32_OperatingSystem returned no instance.");
            return [CheckFindings.NotRun(
                CheckId,
                "Operating system support status could not be determined",
                FindingCategory.OperatingSystem,
                "Operating system",
                "Verify the Windows Management Instrumentation service and retry the scan.",
                error,
                capturedAtUtc)];
        }

        WmiInstance operatingSystem = operatingSystems.Value[0];
        string caption = operatingSystem.GetString("Caption") ?? "Unknown Windows";
        string buildNumberText = operatingSystem.GetString("BuildNumber") ?? string.Empty;

        if (!int.TryParse(buildNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int buildNumber)
            || !LifecycleByBuild.TryGetValue(buildNumber, out (string Name, DateTimeOffset EndOfSupportUtc) lifecycle))
        {
            return [new SecurityFinding(
                $"{CheckId}-UNKNOWN",
                "Operating system support status is unknown",
                $"Build {buildNumberText} of '{caption}' is not in the app's offline lifecycle table. "
                    + "The support status could not be evaluated without internet access.",
                FindingSeverity.Info,
                FindingCategory.OperatingSystem,
                caption,
                new Dictionary<string, string>
                {
                    ["caption"] = caption,
                    ["buildNumber"] = buildNumberText,
                    ["lifecycleTable"] = "offline snapshot, Home/Pro dates",
                },
                "Check the Microsoft lifecycle documentation for this build manually.",
                RequiredPrivilege: null,
                capturedAtUtc)];
        }

        if (_clock.UtcNow <= lifecycle.EndOfSupportUtc)
        {
            return [];
        }

        // MEDIUM, not HIGH: unsupported OS is often rated HIGH, but the mapping did
        // not fix a value — conservative lower value of the plausible range, and the
        // evidence (edition-specific dates) is an approximation.
        return [new SecurityFinding(
            $"{CheckId}-EOL",
            $"{lifecycle.Name} is past its end of support",
            $"Support for {lifecycle.Name} (build {buildNumber}) ended on "
                + $"{lifecycle.EndOfSupportUtc:yyyy-MM-dd}. The system no longer receives security updates "
                + "(Home/Pro lifecycle; Enterprise/LTSC editions may differ).",
            FindingSeverity.Medium,
            FindingCategory.OperatingSystem,
            caption,
            new Dictionary<string, string>
            {
                ["caption"] = caption,
                ["buildNumber"] = buildNumberText,
                ["endOfSupport"] = lifecycle.EndOfSupportUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["lifecycleTable"] = "offline snapshot, Home/Pro dates",
            },
            "Upgrade to a supported Windows version, or verify whether an extended "
                + "support program (ESU, Enterprise/LTSC lifecycle) applies to this edition.",
            RequiredPrivilege: null,
            capturedAtUtc)];
    }
}
