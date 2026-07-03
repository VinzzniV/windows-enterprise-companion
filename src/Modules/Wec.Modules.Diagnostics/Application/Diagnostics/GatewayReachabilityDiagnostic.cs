using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class GatewayReachabilityDiagnostic : IDiagnostic
{
    private readonly INetworkInfoProvider _networkInfoProvider;
    private readonly IPingProbe _pingProbe;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public GatewayReachabilityDiagnostic(
        INetworkInfoProvider networkInfoProvider,
        IPingProbe pingProbe,
        IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _networkInfoProvider = networkInfoProvider;
        _pingProbe = pingProbe;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-NET-GATEWAY";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        if (!context.Target.IsLocal)
        {
            return [DiagnosticResults.LocalPerspective(
                DiagnosticId, "Gateway reachability", DiagnosticCategory.Network,
                "Default gateway", context.Target.DisplayName, _clock.UtcNow)];
        }

        Result<IReadOnlyList<NetworkAdapterInfo>> adapters = _networkInfoProvider.GetActiveAdapters();
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (adapters.IsFailure)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Gateway reachability was not tested",
                "Default gateway",
                new Dictionary<string, string>
                {
                    ["errorCode"] = adapters.Error!.Code.ToString(),
                    ["errorMessage"] = adapters.Error.Message,
                },
                ["Run the network configuration diagnostic first."],
                capturedAtUtc)];
        }

        string? gateway = adapters.Value
            .SelectMany(adapter => adapter.GatewayAddresses)
            .FirstOrDefault();

        if (gateway is null)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "No default gateway to test",
                "Default gateway",
                new Dictionary<string, string> { ["defaultGateway"] = "(none)" },
                [
                    "Check the network configuration diagnostic — without a default gateway there is nothing to reach.",
                    "Verify DHCP or the static IP configuration.",
                ],
                capturedAtUtc)];
        }

        Result<PingProbeReply> probe = await _pingProbe.SendAsync(gateway, _options.ProbeTimeout, cancellationToken);
        capturedAtUtc = _clock.UtcNow;

        if (probe.IsFailure)
        {
            // Local error: probe could not run at all (invalid/unusable address, stack error)
            return [BuildResult(
                DiagnosticStatus.Fail,
                "Gateway reachability test could not be executed",
                $"Default gateway {gateway}",
                new Dictionary<string, string>
                {
                    ["defaultGateway"] = gateway,
                    ["errorCode"] = probe.Error!.Code.ToString(),
                    ["errorMessage"] = probe.Error.Message,
                    ["errorDetails"] = probe.Error.Details ?? "—",
                },
                [
                    "Verify the default route (route print) — the gateway address may be invalid.",
                    "Check whether local security software blocks ICMP socket access.",
                ],
                capturedAtUtc)];
        }

        if (probe.Value.Success)
        {
            return [BuildResult(
                DiagnosticStatus.Pass,
                "Default gateway is reachable",
                $"Default gateway {gateway}",
                new Dictionary<string, string>
                {
                    ["defaultGateway"] = gateway,
                    ["roundtripMs"] = probe.Value.RoundtripMilliseconds.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["pingStatus"] = probe.Value.Status,
                },
                [],
                capturedAtUtc)];
        }

        // WARNING, not FAIL (product decision): many gateways/firewalls block ICMP
        // while routing works — a timeout is suspicious, not proof of a broken network
        return [BuildResult(
            DiagnosticStatus.Warning,
            "Default gateway did not answer the ping",
            $"Default gateway {gateway}",
            new Dictionary<string, string>
            {
                ["defaultGateway"] = gateway,
                ["pingStatus"] = probe.Value.Status,
                ["timeout"] = _options.ProbeTimeout.ToString(),
            },
            [
                "ICMP may be blocked by the gateway or a firewall — this alone does not prove an outage.",
                "Test TCP connectivity or DNS resolution next (see the DNS diagnostic).",
                "Verify the default route (route print / Get-NetRoute).",
                "Test another device in the same network to isolate the problem.",
            ],
            capturedAtUtc)];
    }

    private DiagnosticResult BuildResult(
        DiagnosticStatus status,
        string title,
        string affectedResource,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps,
        DateTimeOffset capturedAtUtc) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.Network,
        affectedResource,
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
