using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class Smb1ProtocolCheck : ISecurityCheck
{
    private const string CimV2Namespace = @"root\cimv2";
    private const int InstallStateEnabled = 1;

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public Smb1ProtocolCheck(IWmiQueryService wmiQueryService, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-SMB1";

    public async Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> feature = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT Name, InstallState FROM Win32_OptionalFeature WHERE Name = 'SMB1Protocol'",
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (feature.IsFailure)
        {
            return [CheckFindings.NotRun(
                CheckId,
                "SMB1 protocol state could not be determined",
                FindingCategory.NetworkServices,
                "SMB1 protocol",
                "Verify the Windows Management Instrumentation service and retry the scan.",
                feature.Error!,
                capturedAtUtc)];
        }

        // Feature absent (removed from the image) means SMB1 cannot be enabled — no finding
        if (feature.Value.Count == 0)
        {
            return [];
        }

        long? installState = feature.Value[0].GetInteger("InstallState");
        if (installState != InstallStateEnabled)
        {
            return [];
        }

        return [new SecurityFinding(
            $"{CheckId}-ENABLED",
            "The insecure SMB1 protocol is enabled",
            "The SMB1Protocol optional feature is installed and enabled. SMB1 lacks integrity and "
                + "encryption protections and is the transport exploited by EternalBlue/WannaCry-class attacks.",
            FindingSeverity.High,
            FindingCategory.NetworkServices,
            "SMB1Protocol optional feature",
            new Dictionary<string, string>
            {
                ["feature"] = "SMB1Protocol",
                ["installState"] = installState?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown",
                ["source"] = $@"{CimV2Namespace}\Win32_OptionalFeature",
            },
            "Disable the SMB1Protocol optional feature unless a legacy device strictly requires it; "
                + "if it does, isolate that device and document the exception.",
            RequiredPrivilege: null,
            capturedAtUtc)];
    }
}
