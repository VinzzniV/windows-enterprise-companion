using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal sealed class RdpAccessCheck : ISecurityCheck
{
    private const string TerminalServerKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string DenyConnectionsValue = "fDenyTSConnections";

    private readonly IRegistryReader _registryReader;
    private readonly IClock _clock;

    public RdpAccessCheck(IRegistryReader registryReader, IClock clock)
    {
        _registryReader = registryReader;
        _clock = clock;
    }

    public string CheckId => "WEC-SEC-RDP";

    public Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (!context.Target.IsLocal)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([CheckFindings.LocalOnly(
                CheckId,
                "Remote Desktop state was not checked on the remote target",
                FindingCategory.NetworkServices,
                "Remote Desktop (RDP)",
                context.Target.DisplayName,
                capturedAtUtc)]);
        }

        Result<object?> denyConnections =
            _registryReader.ReadLocalMachineValue(TerminalServerKey, DenyConnectionsValue);

        if (denyConnections.IsFailure)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([CheckFindings.NotRun(
                CheckId,
                "Remote Desktop state could not be determined",
                FindingCategory.NetworkServices,
                "Remote Desktop (RDP)",
                "Verify registry read permissions and retry the scan.",
                denyConnections.Error!,
                capturedAtUtc)]);
        }

        // Missing value = Windows default (connections denied) — no finding
        bool rdpEnabled = denyConnections.Value is int deny && deny == 0;
        if (!rdpEnabled)
        {
            return Task.FromResult<IReadOnlyList<SecurityFinding>>([]);
        }

        return Task.FromResult<IReadOnlyList<SecurityFinding>>([new SecurityFinding(
            $"{CheckId}-ENABLED",
            "Remote Desktop connections are enabled",
            "Inbound Remote Desktop is allowed on this machine. RDP is a frequent initial access vector "
                + "(brute force, credential stuffing, exposed ports).",
            FindingSeverity.Medium,
            FindingCategory.NetworkServices,
            "Remote Desktop (RDP)",
            new Dictionary<string, string>
            {
                ["registryKey"] = $@"HKLM\{TerminalServerKey}",
                ["fDenyTSConnections"] = "0",
            },
            "Disable Remote Desktop if it is not needed. If it is, restrict it to specific accounts and "
                + "networks, require NLA, and never expose 3389 to the internet.",
            RequiredPrivilege: null,
            capturedAtUtc)]);
    }
}
