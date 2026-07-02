using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed partial class FirewallProfilesCheck : ISecurityCheck
{
    private const string FirewallNamespace = @"root\standardcimv2";
    private const string ProfilesQuery = "SELECT Name, Enabled FROM MSFT_NetFirewallProfile";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;
    private readonly ILogger<FirewallProfilesCheck> _logger;

    public FirewallProfilesCheck(
        IWmiQueryService wmiQueryService,
        IClock clock,
        ILogger<FirewallProfilesCheck> logger)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
        _logger = logger;
    }

    public string CheckId => "WEC-SEC-FIREWALL";

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> profiles = await _wmiQueryService.QueryAsync(
            FirewallNamespace,
            ProfilesQuery,
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (profiles.IsFailure)
        {
            _logger.LogWarning(
                "Firewall check could not query profiles: {ErrorCode} {ErrorMessage}",
                profiles.Error!.Code,
                profiles.Error.Message);
            return [CheckNotRunFinding(profiles.Error, capturedAtUtc)];
        }

        var findings = new List<SecurityFinding>();
        foreach (WmiInstance profile in profiles.Value)
        {
            string profileName = profile.GetString("Name") ?? "Unknown";
            if (!IsProfileEnabled(profile))
            {
                findings.Add(DisabledProfileFinding(profileName, capturedAtUtc));
            }
        }

        LogCheckEvaluated(profiles.Value.Count, findings.Count);
        return findings;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Firewall check evaluated {ProfileCount} profiles, {FindingCount} findings")]
    private partial void LogCheckEvaluated(int profileCount, int findingCount);

    // MSFT_NetFirewallProfile.Enabled is a GpoBoolean: 0 = False, 1 = True,
    // 2 = NotConfigured (effective value then comes from policy; treat as enabled
    // rather than raising a false alarm). Some providers surface it as bool.
    private static bool IsProfileEnabled(WmiInstance profile) => profile.GetRawValue("Enabled") switch
    {
        bool enabled => enabled,
        null => true,
        _ => profile.GetInteger("Enabled") != 0,
    };

    private SecurityFinding DisabledProfileFinding(string profileName, DateTimeOffset capturedAtUtc) => new(
        FindingId: $"{CheckId}-DISABLED",
        Title: $"Windows Firewall is disabled for the {profileName} profile",
        Description:
            $"The Windows Firewall profile '{profileName}' is turned off. While this profile is active, "
            + "inbound connections are not filtered by the host firewall.",
        Severity: FindingSeverity.High,
        Category: FindingCategory.Firewall,
        AffectedResource: $"Firewall profile '{profileName}'",
        Evidence: new Dictionary<string, string>
        {
            ["profile"] = profileName,
            ["enabled"] = "false",
            ["source"] = $@"{FirewallNamespace}\MSFT_NetFirewallProfile",
        },
        Recommendation:
            "Enable the Windows Firewall for this profile (Windows Security > Firewall & network protection, "
            + "or via group policy). If a third-party firewall replaces it intentionally, document the exception.",
        RequiredPrivilege: null,
        CapturedAtUtc: capturedAtUtc);

    private SecurityFinding CheckNotRunFinding(Error error, DateTimeOffset capturedAtUtc) => new(
        FindingId: $"{CheckId}-NOT-RUN",
        Title: "Windows Firewall status could not be determined",
        Description:
            "The firewall check could not read the firewall profiles. The firewall state is unknown, "
            + "not necessarily bad — investigate why the query failed.",
        // Conservative: unknown state is reported as INFO, not as an alarm
        Severity: FindingSeverity.Info,
        Category: FindingCategory.Firewall,
        AffectedResource: "Windows Firewall",
        Evidence: new Dictionary<string, string>
        {
            ["errorCode"] = error.Code.ToString(),
            ["errorMessage"] = error.Message,
            ["source"] = $@"{FirewallNamespace}\MSFT_NetFirewallProfile",
        },
        Recommendation: "Verify that the Windows Management Instrumentation service is running and retry the scan.",
        RequiredPrivilege: error.RequiredPrivilege,
        CapturedAtUtc: capturedAtUtc);
}
