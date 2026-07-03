using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class DefenderStatusCheck : ISecurityCheck
{
    private const string DefenderNamespace = @"root\Microsoft\Windows\Defender";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;
    private readonly SecurityOptions _options;

    public DefenderStatusCheck(
        IWmiQueryService wmiQueryService,
        IClock clock,
        Microsoft.Extensions.Options.IOptions<SecurityOptions> options)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
        _options = options.Value;
    }

    public string CheckId => "WEC-SEC-DEFENDER";

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> status = await _wmiQueryService.QueryAsync(
            context,
            DefenderNamespace,
            "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge FROM MSFT_MpComputerStatus",
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (status.IsFailure || status.Value.Count == 0)
        {
            Error error = status.IsFailure
                ? status.Error!
                : Error.NotFound("MSFT_MpComputerStatus returned no instance.");
            return [CheckFindings.NotRun(
                CheckId,
                "Microsoft Defender status could not be determined",
                FindingCategory.MalwareProtection,
                "Microsoft Defender",
                "If a third-party antivirus replaces Defender this can be expected; "
                    + "verify that a real-time protection product is active.",
                error,
                capturedAtUtc)];
        }

        WmiInstance defender = status.Value[0];
        var findings = new List<SecurityFinding>();

        if (defender.GetValue<bool?>("AntivirusEnabled") == false)
        {
            findings.Add(new SecurityFinding(
                $"{CheckId}-AV-DISABLED",
                "Microsoft Defender antivirus is disabled",
                "Defender reports its antivirus engine as disabled. Without an active antivirus engine "
                    + "the machine has no on-access malware protection.",
                FindingSeverity.High,
                FindingCategory.MalwareProtection,
                "Microsoft Defender",
                new Dictionary<string, string>
                {
                    ["antivirusEnabled"] = "false",
                    ["source"] = $@"{DefenderNamespace}\MSFT_MpComputerStatus",
                },
                "Enable Microsoft Defender antivirus, or verify that an alternative product provides "
                    + "equivalent protection and document the exception.",
                RequiredPrivilege: null,
                capturedAtUtc));
        }
        else if (defender.GetValue<bool?>("RealTimeProtectionEnabled") == false)
        {
            // Conservative MEDIUM: the engine is on, only on-access scanning is off.
            // The user mapping fixes HIGH for "Defender disabled"; this weaker state
            // gets the lower plausible severity per the agreed rule.
            findings.Add(new SecurityFinding(
                $"{CheckId}-RTP-DISABLED",
                "Microsoft Defender real-time protection is disabled",
                "The Defender engine is enabled but real-time (on-access) protection is turned off. "
                    + "Malware is only detected during manual or scheduled scans.",
                FindingSeverity.Medium,
                FindingCategory.MalwareProtection,
                "Microsoft Defender",
                new Dictionary<string, string>
                {
                    ["antivirusEnabled"] = "true",
                    ["realTimeProtectionEnabled"] = "false",
                    ["source"] = $@"{DefenderNamespace}\MSFT_MpComputerStatus",
                },
                "Re-enable real-time protection in Windows Security > Virus & threat protection.",
                RequiredPrivilege: null,
                capturedAtUtc));
        }

        long? signatureAgeDays = defender.GetInteger("AntivirusSignatureAge");
        int maxSignatureAgeDays = _options.MaxDefenderSignatureAgeDays;
        if (signatureAgeDays is not null && signatureAgeDays > maxSignatureAgeDays)
        {
            findings.Add(new SecurityFinding(
                $"{CheckId}-SIGNATURES-STALE",
                $"Microsoft Defender signatures are {signatureAgeDays} days old",
                "The antivirus definitions have not been updated recently. Detection quality degrades "
                    + "quickly with stale signatures; an update pipeline problem is likely.",
                FindingSeverity.Medium,
                FindingCategory.MalwareProtection,
                "Microsoft Defender",
                new Dictionary<string, string>
                {
                    ["antivirusSignatureAgeDays"] = signatureAgeDays.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["threshold"] = maxSignatureAgeDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["source"] = $@"{DefenderNamespace}\MSFT_MpComputerStatus",
                },
                "Trigger a definition update (Windows Security > Virus & threat protection > "
                    + "Protection updates) and verify the machine can reach the update source.",
                RequiredPrivilege: null,
                capturedAtUtc));
        }

        return findings;
    }
}
