using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class DomainControllerReachabilityDiagnostic : IDiagnostic
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IDnsResolver _dnsResolver;
    private readonly IPingProbe _pingProbe;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public DomainControllerReachabilityDiagnostic(
        IWmiQueryService wmiQueryService,
        IDnsResolver dnsResolver,
        IPingProbe pingProbe,
        Microsoft.Extensions.Options.IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _dnsResolver = dnsResolver;
        _pingProbe = pingProbe;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-DOM-DCREACH";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> computerSystem = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT PartOfDomain, Domain FROM Win32_ComputerSystem",
            cancellationToken);
        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (computerSystem.IsFailure || computerSystem.Value.Count == 0)
        {
            return [BuildResult(
                DiagnosticStatus.NotRun,
                "Domain membership could not be determined",
                new Dictionary<string, string>
                {
                    ["errorCode"] = computerSystem.IsFailure ? computerSystem.Error!.Code.ToString() : "NotFound",
                },
                ["Verify the Windows Management Instrumentation service and rerun the diagnostics."],
                capturedAtUtc)];
        }

        WmiInstance system = computerSystem.Value[0];
        bool partOfDomain = system.GetRawValue("PartOfDomain") as bool? ?? false;
        string? domainName = system.GetString("Domain");

        if (!partOfDomain || string.IsNullOrWhiteSpace(domainName))
        {
            return [BuildResult(
                DiagnosticStatus.Pass,
                "Not domain-joined — domain controller discovery skipped",
                new Dictionary<string, string> { ["membership"] = domainName ?? "WORKGROUP" },
                [],
                capturedAtUtc)];
        }

        // The domain's A records point at its domain controllers (DC locator light)
        Result<IReadOnlyList<string>> dcAddresses = await _dnsResolver.ResolveAsync(domainName, cancellationToken);
        capturedAtUtc = _clock.UtcNow;

        if (dcAddresses.IsFailure || dcAddresses.Value.Count == 0)
        {
            return [BuildResult(
                DiagnosticStatus.Fail,
                $"No domain controller found for '{domainName}'",
                new Dictionary<string, string>
                {
                    ["domain"] = domainName,
                    ["dnsResult"] = dcAddresses.IsFailure ? dcAddresses.Error!.Message : "(no addresses)",
                },
                [
                    "Verify that the configured DNS servers are the domain's DNS servers.",
                    $"Cross-check manually: nslookup {domainName} and nltest /dsgetdc:{domainName}",
                ],
                capturedAtUtc)];
        }

        string firstDcAddress = dcAddresses.Value[0];
        Result<PingProbeReply> ping = await _pingProbe.SendAsync(firstDcAddress, _options.ProbeTimeout, cancellationToken);
        capturedAtUtc = _clock.UtcNow;

        var evidence = new Dictionary<string, string>
        {
            ["domain"] = domainName,
            ["resolvedAddresses"] = string.Join(", ", dcAddresses.Value),
            ["pingTarget"] = firstDcAddress,
        };

        if (ping.IsSuccess && ping.Value.Success)
        {
            evidence["roundtrip"] = $"{ping.Value.RoundtripMilliseconds} ms";
            return [BuildResult(
                DiagnosticStatus.Pass,
                $"Domain controller for '{domainName}' is reachable",
                evidence,
                [],
                capturedAtUtc)];
        }

        evidence["pingResult"] = ping.IsSuccess ? ping.Value.Status : ping.Error!.Message;
        return [BuildResult(
            DiagnosticStatus.Warning,
            $"Domain controller for '{domainName}' did not answer a ping",
            evidence,
            [
                "ICMP may be blocked by policy — verify with: nltest /dsgetdc: and a Kerberos logon test.",
                "If the DC is really down, logons will fall back to cached credentials.",
            ],
            capturedAtUtc)];
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
        DiagnosticCategory.Domain,
        "Domain controller",
        evidence,
        nextSteps,
        RequiredPrivilege: null,
        capturedAtUtc);
}
