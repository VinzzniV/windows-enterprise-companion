using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class DomainMembershipDiagnostic : IDiagnostic
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public DomainMembershipDiagnostic(IWmiQueryService wmiQueryService, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-DOMAIN";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> computerSystems = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT DNSHostName, PartOfDomain, Domain, Workgroup FROM Win32_ComputerSystem",
            cancellationToken);

        DateTimeOffset capturedAtUtc = _clock.UtcNow;

        if (computerSystems.IsFailure || computerSystems.Value.Count == 0)
        {
            Error error = computerSystems.IsFailure
                ? computerSystems.Error!
                : Error.NotFound("Win32_ComputerSystem returned no instance.");
            return [new DiagnosticResult(
                DiagnosticId,
                "Domain membership could not be determined",
                DiagnosticStatus.NotRun,
                DiagnosticCategory.Domain,
                "Computer system",
                new Dictionary<string, string>
                {
                    ["errorCode"] = error.Code.ToString(),
                    ["errorMessage"] = error.Message,
                },
                ["Verify the Windows Management Instrumentation service and rerun the diagnostics."],
                RequiredPrivilege: null,
                capturedAtUtc)];
        }

        WmiInstance computerSystem = computerSystems.Value[0];
        bool partOfDomain = computerSystem.GetValue<bool?>("PartOfDomain") ?? false;
        string hostName = computerSystem.GetString("DNSHostName") ?? Environment.MachineName;

        // Informational by design: workgroup membership is a valid configuration,
        // not a problem — the result documents which world the machine lives in
        var evidence = new Dictionary<string, string>
        {
            ["hostName"] = hostName,
            ["membership"] = partOfDomain ? "Domain" : "Workgroup",
            [partOfDomain ? "domain" : "workgroup"] = partOfDomain
                ? computerSystem.GetString("Domain") ?? "unknown"
                : computerSystem.GetString("Workgroup") ?? computerSystem.GetString("Domain") ?? "unknown",
        };

        return [new DiagnosticResult(
            DiagnosticId,
            partOfDomain
                ? $"Machine is domain-joined ({evidence["domain"]})"
                : $"Machine is in a workgroup ({evidence["workgroup"]})",
            DiagnosticStatus.Pass,
            DiagnosticCategory.Domain,
            hostName,
            evidence,
            partOfDomain
                ? []
                : ["If this machine should be domain-joined, check the computer account and network reachability of a domain controller."],
            RequiredPrivilege: null,
            capturedAtUtc)];
    }
}
