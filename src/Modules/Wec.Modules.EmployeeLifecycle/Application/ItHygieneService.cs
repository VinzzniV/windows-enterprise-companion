using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Modules.EmployeeLifecycle.Application;

public enum HygieneStatus
{
    Healthy = 0,
    Warning,
    CleanupCandidate,
    Incomplete,
    Critical,
}

public enum HygieneFindingCode
{
    MissingKaspersky = 0,
    OrphanKaspersky,
    StaleAd,
    StaleKaspersky,
    OutdatedAgent,
    OutdatedKes,
    MissingOpsi,
    OrphanOpsi,
    StaleOpsi,
    MissingNessus,
    StaleNessus,
    NessusCriticalVulnerabilities,
    NessusHighVulnerabilities,
    MissingKasperskyAgent,
    MissingKes,
}

public enum HygieneFindingSeverity
{
    Warning = 0,
    Critical,
}

public sealed record HygieneFinding(
    HygieneFindingCode Code,
    HygieneFindingSeverity Severity,
    string Message);

public sealed record AdDeviceData(
    bool Exists,
    bool? Enabled,
    string? DnsHostName,
    string? OperatingSystem,
    string? Description,
    string? DistinguishedName,
    string? OrganizationalUnit,
    DateTimeOffset? LastLogonDate);

public sealed record KasperskyDeviceData(
    bool Exists,
    DateTimeOffset? LastSeen,
    string? AgentVersion,
    string? KesVersion,
    string? AdministrationGroup);

public sealed record OpsiDeviceData(
    bool Exists,
    string? ClientId,
    string? Description,
    string? DepotId,
    DateTimeOffset? LastSeen,
    string? ClientAgentVersion);

public sealed record NessusDeviceData(
    bool Exists,
    string? AssetId,
    string? IpAddress,
    DateTimeOffset? LastCompletedScanUtc,
    int Critical,
    int High,
    int Medium,
    int Low,
    int Info,
    IReadOnlyList<int> Ports,
    IReadOnlyList<string> ScanSources);

public enum InventorySourceAvailability
{
    Available = 0,
    NotConnected,
    Unavailable,
    Truncated,
    Partial,
}

public sealed record InventorySourceState(
    InventorySourceAvailability Availability,
    string? Error = null);

public sealed record EnvironmentSourceStates(
    InventorySourceState ActiveDirectory,
    InventorySourceState Kaspersky,
    InventorySourceState Opsi,
    InventorySourceState Nessus)
{
    public EnvironmentSourceStates(InventorySourceState activeDirectory, InventorySourceState kaspersky, InventorySourceState opsi)
        : this(activeDirectory, kaspersky, opsi,
            new InventorySourceState(InventorySourceAvailability.NotConnected, "Nessus is not configured.")) { }
}

public sealed record HygieneAssessment(
    HygieneStatus Status,
    IReadOnlyList<HygieneFinding> Findings);

public sealed record HygieneDevice(
    string ComputerName,
    string HostName,
    AdDeviceData ActiveDirectory,
    KasperskyDeviceData Kaspersky,
    OpsiDeviceData Opsi,
    NessusDeviceData Nessus,
    HygieneAssessment Assessment);

public sealed record HygieneSummary(
    int Total,
    int AdComputers,
    int KasperskyComputers,
    int OpsiComputers,
    int NessusComputers,
    int Healthy,
    int Problems,
    int Incomplete,
    int Stale,
    int MissingKaspersky,
    int OrphanKaspersky,
    int MissingOpsi,
    int OrphanOpsi,
    int Outdated,
    int MissingNessus,
    int StaleNessus,
    int NessusCritical,
    int NessusHigh);

public sealed record ItHygieneResult(
    DateTimeOffset AssessedAtUtc,
    string? DomainName,
    EnvironmentSourceStates Sources,
    HygieneSummary Summary,
    IReadOnlyList<HygieneDevice> Devices)
{
    internal ManagementDeviceSnapshot? SourceRecords { get; init; }
}

public sealed record ItHygieneRequest(
    DirectoryInventoryConnection? ActiveDirectory = null,
    KasperskyInventoryConnection? Kaspersky = null,
    string? OperationId = null);

public enum HygieneLoadPhase
{
    LoadingSources = 0,
    Correlating,
    Completed,
    Cancelled,
}

public enum HygieneSourceProgressStatus
{
    Running = 0,
    Available,
    Partial,
    NotConnected,
    Unavailable,
    Truncated,
}

public sealed record HygieneSourceProgress(
    string Source,
    HygieneSourceProgressStatus Status,
    int? ItemCount = null,
    string? Message = null);

[BridgeContract]
public sealed record HygieneLoadProgress(
    string OperationId,
    HygieneLoadPhase Phase,
    DateTimeOffset StartedAtUtc,
    int CompletedSources,
    int TotalSources,
    int PartialDeviceCount,
    HygieneSummary? PartialSummary,
    IReadOnlyList<HygieneSourceProgress> Sources);

internal sealed class ItHygieneService
{
    private readonly HygieneSourceLoader _sourceLoader;
    private readonly IBridgeEventPublisher _events;
    private readonly IClock _clock;
    private readonly ItLifecycleOptions _options;

    public ItHygieneService(
        IAdComputerInventoryProvider activeDirectory,
        IKasperskyInventoryReader kaspersky,
        IOpsiComputerInventoryProvider opsi,
        INessusComputerInventoryProvider nessus,
        IServiceCredentialStore credentials,
        IBridgeEventPublisher events,
        IClock clock,
        IOptions<ItLifecycleOptions> options)
    {
        _sourceLoader = new HygieneSourceLoader(
            activeDirectory,
            kaspersky,
            opsi,
            nessus,
            credentials,
            options.Value);
        _events = events;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<ItHygieneResult>> LoadAsync(
        ItHygieneRequest request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedAtUtc = _clock.UtcNow;
        var progress = new HygieneLoadProgressTracker(
            request.OperationId,
            startedAtUtc,
            _events,
            _options);
        progress.Start();
        HygieneSourceLoad sourceLoad;
        try
        {
            sourceLoad = await _sourceLoader.LoadAsync(request, progress, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress.Cancel();
            throw;
        }
        progress.Correlating();

        DateTimeOffset now = _clock.UtcNow;
        IReadOnlyList<HygieneDevice> devices = CorrelateAndAssess(
            sourceLoad.ActiveDirectory.IsSuccess ? sourceLoad.ActiveDirectory.Value.Computers : [],
            sourceLoad.Kaspersky.IsSuccess ? sourceLoad.Kaspersky.Value.Computers : [],
            sourceLoad.Opsi.IsSuccess ? sourceLoad.Opsi.Value.Computers : [],
            sourceLoad.Nessus.IsSuccess
                ? sourceLoad.Nessus.Value
                : new NessusComputerInventory([], NessusInventoryAvailability.Unavailable, null),
            sourceLoad.States,
            now,
            _options);

        HygieneSummary summary = Summarize(devices);
        progress.Complete(summary);
        return Result.Success(new ItHygieneResult(
            now,
            sourceLoad.DomainName,
            sourceLoad.States,
            summary,
            devices)
        {
            SourceRecords = ManagementDeviceSnapshotProvider.Project(sourceLoad, now, request, _options),
        });
    }

    internal static IReadOnlyList<HygieneDevice> CorrelateAndAssess(
        IReadOnlyList<AdComputerInventoryItem> adComputers,
        IReadOnlyList<KasperskyComputer> kasperskyComputers,
        IReadOnlyList<OpsiComputerInventoryItem> opsiComputers,
        EnvironmentSourceStates sources,
        DateTimeOffset now,
        ItLifecycleOptions options)
        => CorrelateAndAssess(adComputers, kasperskyComputers, opsiComputers,
            new NessusComputerInventory([], NessusInventoryAvailability.NotConnected, null), sources, now, options, false);

    internal static IReadOnlyList<HygieneDevice> CorrelateAndAssess(
        IReadOnlyList<AdComputerInventoryItem> adComputers,
        IReadOnlyList<KasperskyComputer> kasperskyComputers,
        IReadOnlyList<OpsiComputerInventoryItem> opsiComputers,
        NessusComputerInventory nessusInventory,
        EnvironmentSourceStates sources,
        DateTimeOffset now,
        ItLifecycleOptions options,
        bool assessNessus = true)
    {
        Dictionary<string, AdComputerInventoryItem> adByName = adComputers
            .GroupBy(computer => NormalizeComputerName(computer.ComputerName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, KasperskyComputer> kscByName = kasperskyComputers
            .GroupBy(computer => NormalizeComputerName(computer.ComputerName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(computer => computer.LastSeen).First(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NessusComputerInventoryItem> nessusByName = nessusInventory.Computers
            .GroupBy(computer => NormalizeComputerName(computer.ComputerName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(computer => computer.LastCompletedScanUtc).First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, OpsiComputerInventoryItem> opsiByName = opsiComputers
            .GroupBy(computer => NormalizeComputerName(computer.ComputerName), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(computer => computer.LastSeen).First(),
                StringComparer.OrdinalIgnoreCase);

        return adByName.Keys
            .Union(kscByName.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(opsiByName.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                adByName.TryGetValue(name, out AdComputerInventoryItem? ad);
                kscByName.TryGetValue(name, out KasperskyComputer? ksc);
                opsiByName.TryGetValue(name, out OpsiComputerInventoryItem? opsi);
                nessusByName.TryGetValue(name, out NessusComputerInventoryItem? nessus);
                HygieneAssessment assessment = HygieneAssessmentPolicy.Assess(
                    ad, ksc, opsi, nessus, nessusInventory, sources, now, options, assessNessus);

                string hostName = ad?.DnsHostName
                    ?? opsi?.ComputerName
                    ?? ksc?.ComputerName
                    ?? name;

                return new HygieneDevice(
                    name,
                    hostName,
                    new AdDeviceData(
                        ad is not null,
                        ad?.Enabled,
                        ad?.DnsHostName,
                        ad?.OperatingSystem,
                        ad?.Description,
                        ad?.DistinguishedName,
                        OrganizationalUnit(ad?.DistinguishedName),
                        ad?.LastLogonDate),
                    new KasperskyDeviceData(
                        ksc is not null,
                        ksc?.LastSeen,
                        ksc?.AgentVersion,
                        ksc?.KesVersion,
                        ksc?.AdministrationGroup),
                    new OpsiDeviceData(
                        opsi is not null,
                        opsi?.ComputerName,
                        opsi?.Description,
                        opsi?.DepotId,
                        opsi?.LastSeen,
                        opsi?.ClientAgentVersion),
                    new NessusDeviceData(
                        nessus is not null,
                        nessus?.AssetId,
                        nessus?.IpAddress,
                        nessus?.LastCompletedScanUtc,
                        nessus?.Critical ?? 0,
                        nessus?.High ?? 0,
                        nessus?.Medium ?? 0,
                        nessus?.Low ?? 0,
                        nessus?.Info ?? 0,
                        nessus?.Ports ?? [],
                        nessus?.ScanSources ?? []),
                    assessment);
            })
            .ToList();
    }

    internal static string NormalizeComputerName(string? computerName)
    {
        string value = computerName?.Trim().TrimEnd('.') ?? string.Empty;
        int dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            value = value[..dot];
        }

        return value.ToUpperInvariant();
    }

    internal static bool IsVersionOlder(string? installed, string? target) =>
        HygieneAssessmentPolicy.IsVersionOlder(installed, target);

    internal static HygieneSummary Summarize(IReadOnlyList<HygieneDevice> devices)
    {
        static bool Has(HygieneDevice device, params HygieneFindingCode[] codes) =>
            device.Assessment.Findings.Any(finding => codes.Contains(finding.Code));

        int healthy = devices.Count(device => device.Assessment.Status == HygieneStatus.Healthy);
        int incomplete = devices.Count(device => device.Assessment.Status == HygieneStatus.Incomplete);
        int problems = devices.Count(device => device.Assessment.Status is
            HygieneStatus.Warning or HygieneStatus.CleanupCandidate or HygieneStatus.Critical);
        return new HygieneSummary(
            devices.Count,
            devices.Count(device => device.ActiveDirectory.Exists),
            devices.Count(device => device.Kaspersky.Exists),
            devices.Count(device => device.Opsi.Exists),
            devices.Count(device => device.Nessus.Exists),
            healthy,
            problems,
            incomplete,
            devices.Count(device => Has(device,
                HygieneFindingCode.StaleAd,
                HygieneFindingCode.StaleKaspersky,
                HygieneFindingCode.StaleOpsi,
                HygieneFindingCode.StaleNessus)),
            devices.Count(device => Has(device, HygieneFindingCode.MissingKaspersky)),
            devices.Count(device => Has(device, HygieneFindingCode.OrphanKaspersky)),
            devices.Count(device => Has(device, HygieneFindingCode.MissingOpsi)),
            devices.Count(device => Has(device, HygieneFindingCode.OrphanOpsi)),
            devices.Count(device => Has(device, HygieneFindingCode.OutdatedAgent, HygieneFindingCode.OutdatedKes)),
            devices.Count(device => Has(device, HygieneFindingCode.MissingNessus)),
            devices.Count(device => Has(device, HygieneFindingCode.StaleNessus)),
            devices.Count(device => Has(device, HygieneFindingCode.NessusCriticalVulnerabilities)),
            devices.Count(device => Has(device, HygieneFindingCode.NessusHighVulnerabilities)));
    }

    private static string? OrganizationalUnit(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return null;
        }

        int comma = distinguishedName.IndexOf(',', StringComparison.Ordinal);
        return comma >= 0 && comma + 1 < distinguishedName.Length
            ? distinguishedName[(comma + 1)..]
            : null;
    }

}
