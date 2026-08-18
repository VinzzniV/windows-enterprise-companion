using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.EmployeeLifecycle.Application;

public enum HygieneStatus
{
    Healthy = 0,
    Warning,
    CleanupCandidate,
    Incomplete,
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

public enum InventorySourceAvailability
{
    Available = 0,
    NotConnected,
    Unavailable,
    Truncated,
}

public sealed record InventorySourceState(
    InventorySourceAvailability Availability,
    string? Error = null);

public sealed record EnvironmentSourceStates(
    InventorySourceState ActiveDirectory,
    InventorySourceState Kaspersky,
    InventorySourceState Opsi);

public sealed record HygieneAssessment(
    HygieneStatus Status,
    IReadOnlyList<HygieneFinding> Findings);

public sealed record HygieneDevice(
    string ComputerName,
    string HostName,
    AdDeviceData ActiveDirectory,
    KasperskyDeviceData Kaspersky,
    OpsiDeviceData Opsi,
    HygieneAssessment Assessment);

public sealed record HygieneSummary(
    int Total,
    int AdComputers,
    int KasperskyComputers,
    int OpsiComputers,
    int Healthy,
    int Problems,
    int Incomplete,
    int Stale,
    int MissingKaspersky,
    int OrphanKaspersky,
    int MissingOpsi,
    int OrphanOpsi,
    int Outdated);

public sealed record ItHygieneResult(
    DateTimeOffset AssessedAtUtc,
    string? DomainName,
    EnvironmentSourceStates Sources,
    HygieneSummary Summary,
    IReadOnlyList<HygieneDevice> Devices);

public sealed record DirectoryInventoryConnection(
    string? Domain = null,
    string? Server = null,
    string? UserName = null,
    string? UserDomain = null,
    string? Password = null);

public sealed record KasperskyInventoryConnection(
    string? Server = null,
    int? Port = null,
    string? UserName = null,
    string? Domain = null,
    string? Password = null);

public sealed record ItHygieneRequest(
    DirectoryInventoryConnection? ActiveDirectory = null,
    KasperskyInventoryConnection? Kaspersky = null);

internal sealed class ItHygieneService
{
    private readonly IAdComputerInventoryProvider _activeDirectory;
    private readonly IKasperskyInventoryReader _kaspersky;
    private readonly IOpsiComputerInventoryProvider _opsi;
    private readonly IServiceCredentialStore _credentials;
    private readonly IClock _clock;
    private readonly ItLifecycleOptions _options;

    public ItHygieneService(
        IAdComputerInventoryProvider activeDirectory,
        IKasperskyInventoryReader kaspersky,
        IOpsiComputerInventoryProvider opsi,
        IServiceCredentialStore credentials,
        IClock clock,
        IOptions<ItLifecycleOptions> options)
    {
        _activeDirectory = activeDirectory;
        _kaspersky = kaspersky;
        _opsi = opsi;
        _credentials = credentials;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<ItHygieneResult>> LoadAsync(
        ItHygieneRequest request,
        CancellationToken cancellationToken)
    {
        Result<AdComputerInventoryQuery> adQuery = BuildAdQuery(request.ActiveDirectory);
        Result<KasperskyInventoryConnection?> kscInput = KasperskyInput(request.Kaspersky);
        Result<KasperskyConnection> kscConnection = kscInput.IsFailure
            ? Result.Failure<KasperskyConnection>(kscInput.Error!)
            : BuildKasperskyConnection(kscInput.Value, request.ActiveDirectory);
        Task<Result<AdComputerInventory>> adTask =
            adQuery.IsSuccess
                ? LoadSourceAsync(
                    () => _activeDirectory.LoadAsync(adQuery.Value, cancellationToken),
                    "Active Directory",
                    cancellationToken)
                : Task.FromResult(Result.Failure<AdComputerInventory>(adQuery.Error!));
        Task<Result<KasperskyInventory>> kscTask =
            kscConnection.IsSuccess
                ? LoadSourceAsync(
                    () => _kaspersky.LoadAsync(kscConnection.Value, cancellationToken),
                    "Kaspersky Security Center",
                    cancellationToken)
                : Task.FromResult(Result.Failure<KasperskyInventory>(kscConnection.Error!));
        Task<Result<OpsiComputerInventory>> opsiTask =
            LoadSourceAsync(
                () => _opsi.LoadAsync(_options.InventoryLimit, cancellationToken),
                "opsi",
                cancellationToken);
        await Task.WhenAll(adTask, kscTask, opsiTask);

        Result<AdComputerInventory> ad = await adTask;
        Result<KasperskyInventory> ksc = await kscTask;
        Result<OpsiComputerInventory> opsi = await opsiTask;

        InventorySourceState adState = AdState(ad);
        InventorySourceState kscState = SourceState(ksc, notConnectedOnInvalidRequest: true);
        InventorySourceState opsiState = SourceState(opsi, notConnectedOnInvalidRequest: true);
        var sources = new EnvironmentSourceStates(adState, kscState, opsiState);

        DateTimeOffset now = _clock.UtcNow;
        IReadOnlyList<HygieneDevice> devices = CorrelateAndAssess(
            ad.IsSuccess ? ad.Value.Computers : [],
            ksc.IsSuccess ? ksc.Value.Computers : [],
            opsi.IsSuccess ? opsi.Value.Computers : [],
            sources,
            now,
            _options);

        return Result.Success(new ItHygieneResult(
            now,
            ad.IsSuccess ? ad.Value.DomainName : null,
            sources,
            Summarize(devices),
            devices));
    }

    private Result<KasperskyInventoryConnection?> KasperskyInput(KasperskyInventoryConnection? input)
    {
        if (!string.IsNullOrWhiteSpace(input?.UserName) && input.Password is not null)
        {
            return Result.Success<KasperskyInventoryConnection?>(input);
        }

        Result<StoredServiceCredential?> stored = _credentials.Read(ServiceCredentialKind.Kaspersky);
        if (stored.IsFailure)
        {
            return Result.Failure<KasperskyInventoryConnection?>(stored.Error!);
        }

        return stored.Value is null
            ? Result.Success<KasperskyInventoryConnection?>(input)
            : Result.Success<KasperskyInventoryConnection?>(new KasperskyInventoryConnection(
                input?.Server,
                input?.Port,
                stored.Value.UserName,
                stored.Value.Domain,
                stored.Value.Password));
    }

    private static async Task<Result<T>> LoadSourceAsync<T>(
        Func<Task<Result<T>>> load,
        string sourceName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await load().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure<T>(new Error(
                ErrorCode.ServiceUnavailable,
                $"{sourceName} inventory is unavailable.")
            {
                Details = exception.Message,
            });
        }
    }

    private static InventorySourceState AdState(Result<AdComputerInventory> result)
    {
        if (result.IsFailure)
        {
            return new InventorySourceState(InventorySourceAvailability.Unavailable, ErrorText(result.Error!));
        }

        if (!result.Value.DomainJoined)
        {
            return new InventorySourceState(
                InventorySourceAvailability.NotConnected,
                "No Active Directory domain context is available.");
        }

        return new InventorySourceState(
            result.Value.Truncated ? InventorySourceAvailability.Truncated : InventorySourceAvailability.Available);
    }

    private static InventorySourceState SourceState<T>(Result<T> result, bool notConnectedOnInvalidRequest)
    {
        if (result.IsFailure)
        {
            InventorySourceAvailability availability =
                notConnectedOnInvalidRequest && result.Error!.Code == ErrorCode.InvalidRequest
                    ? InventorySourceAvailability.NotConnected
                    : InventorySourceAvailability.Unavailable;
            return new InventorySourceState(availability, ErrorText(result.Error!));
        }

        bool truncated = result.Value switch
        {
            KasperskyInventory inventory => inventory.Truncated,
            OpsiComputerInventory inventory => inventory.Truncated,
            _ => false,
        };
        return new InventorySourceState(
            truncated ? InventorySourceAvailability.Truncated : InventorySourceAvailability.Available);
    }

    private static string ErrorText(Error error) =>
        string.IsNullOrWhiteSpace(error.Details) ? error.Message : $"{error.Message} {error.Details}";

    internal static IReadOnlyList<HygieneDevice> CorrelateAndAssess(
        IReadOnlyList<AdComputerInventoryItem> adComputers,
        IReadOnlyList<KasperskyComputer> kasperskyComputers,
        IReadOnlyList<OpsiComputerInventoryItem> opsiComputers,
        EnvironmentSourceStates sources,
        DateTimeOffset now,
        ItLifecycleOptions options)
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
                List<HygieneFinding> findings = Assess(ad, ksc, opsi, sources, now, options);
                HygieneStatus status = findings.Any(finding => finding.Severity == HygieneFindingSeverity.Critical)
                    ? HygieneStatus.CleanupCandidate
                    : findings.Count > 0
                        ? HygieneStatus.Warning
                        : SourcesComplete(sources)
                            ? HygieneStatus.Healthy
                            : HygieneStatus.Incomplete;

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
                    new HygieneAssessment(status, findings));
            })
            .ToList();
    }

    private static bool SourcesComplete(EnvironmentSourceStates sources) =>
        sources.ActiveDirectory.Availability == InventorySourceAvailability.Available
        && sources.Kaspersky.Availability == InventorySourceAvailability.Available
        && sources.Opsi.Availability == InventorySourceAvailability.Available;

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

    internal static bool IsVersionOlder(string? installed, string? target)
    {
        if (!TryParseVersion(installed, out int[] installedParts)
            || !TryParseVersion(target, out int[] targetParts))
        {
            return false;
        }

        int count = Math.Max(installedParts.Length, targetParts.Length);
        for (int index = 0; index < count; index++)
        {
            int installedPart = index < installedParts.Length ? installedParts[index] : 0;
            int targetPart = index < targetParts.Length ? targetParts[index] : 0;
            if (installedPart != targetPart)
            {
                return installedPart < targetPart;
            }
        }

        return false;
    }

    private static List<HygieneFinding> Assess(
        AdComputerInventoryItem? ad,
        KasperskyComputer? ksc,
        OpsiComputerInventoryItem? opsi,
        EnvironmentSourceStates sources,
        DateTimeOffset now,
        ItLifecycleOptions options)
    {
        var findings = new List<HygieneFinding>();
        bool canCompareKaspersky = CanCompare(sources.ActiveDirectory, sources.Kaspersky);
        bool canCompareOpsi = CanCompare(sources.ActiveDirectory, sources.Opsi);
        if (canCompareKaspersky && ad is { Enabled: true } && ksc is null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.MissingKaspersky,
                HygieneFindingSeverity.Warning,
                "Enabled in Active Directory, but no matching Kaspersky device was found."));
        }

        if (canCompareKaspersky && ad is null && ksc is not null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OrphanKaspersky,
                HygieneFindingSeverity.Warning,
                "Present in Kaspersky, but no matching Active Directory computer was found."));
        }

        AddStaleFinding(
            findings,
            HygieneFindingCode.StaleAd,
            "Active Directory last logon",
            ad?.LastLogonDate,
            now,
            options);
        AddStaleFinding(
            findings,
            HygieneFindingCode.StaleOpsi,
            "opsi last seen",
            opsi?.LastSeen,
            now,
            options);

        if (canCompareOpsi && ad is { Enabled: true } && IsWindowsClient(ad.OperatingSystem) && opsi is null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.MissingOpsi,
                HygieneFindingSeverity.Warning,
                "Active Windows client in Active Directory, but no matching opsi client was found."));
        }

        if (canCompareOpsi && ad is null && opsi is not null)
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OrphanOpsi,
                HygieneFindingSeverity.Warning,
                "Present in opsi, but no matching Active Directory computer was found."));
        }
        AddStaleFinding(
            findings,
            HygieneFindingCode.StaleKaspersky,
            "Kaspersky last seen",
            ksc?.LastSeen,
            now,
            options);

        if (ksc is not null && IsVersionOlder(ksc.AgentVersion, options.TargetAgentVersion))
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OutdatedAgent,
                HygieneFindingSeverity.Warning,
                $"Network Agent {ksc.AgentVersion} is below target {options.TargetAgentVersion}."));
        }

        if (ksc is not null && IsVersionOlder(ksc.KesVersion, options.TargetKesVersion))
        {
            findings.Add(new HygieneFinding(
                HygieneFindingCode.OutdatedKes,
                HygieneFindingSeverity.Warning,
                $"KES {ksc.KesVersion} is below target {options.TargetKesVersion}."));
        }

        return findings;
    }

    private static bool CanCompare(InventorySourceState left, InventorySourceState right) =>
        left.Availability == InventorySourceAvailability.Available
        && right.Availability == InventorySourceAvailability.Available;

    internal static bool IsWindowsClient(string? operatingSystem) =>
        !string.IsNullOrWhiteSpace(operatingSystem)
        && operatingSystem.Contains("Windows", StringComparison.OrdinalIgnoreCase)
        && !operatingSystem.Contains("Server", StringComparison.OrdinalIgnoreCase);

    private static void AddStaleFinding(
        List<HygieneFinding> findings,
        HygieneFindingCode code,
        string label,
        DateTimeOffset? timestamp,
        DateTimeOffset now,
        ItLifecycleOptions options)
    {
        if (timestamp is null)
        {
            return;
        }

        double ageDays = Math.Max(0, (now - timestamp.Value).TotalDays);
        if (ageDays > options.StaleCriticalDays)
        {
            findings.Add(new HygieneFinding(
                code,
                HygieneFindingSeverity.Critical,
                $"{label} was {Math.Floor(ageDays)} days ago (cleanup threshold: {options.StaleCriticalDays} days)."));
        }
        else if (ageDays > options.StaleWarningDays)
        {
            findings.Add(new HygieneFinding(
                code,
                HygieneFindingSeverity.Warning,
                $"{label} was {Math.Floor(ageDays)} days ago (warning threshold: {options.StaleWarningDays} days)."));
        }
    }

    private static HygieneSummary Summarize(IReadOnlyList<HygieneDevice> devices)
    {
        static bool Has(HygieneDevice device, params HygieneFindingCode[] codes) =>
            device.Assessment.Findings.Any(finding => codes.Contains(finding.Code));

        int healthy = devices.Count(device => device.Assessment.Status == HygieneStatus.Healthy);
        int incomplete = devices.Count(device => device.Assessment.Status == HygieneStatus.Incomplete);
        int problems = devices.Count(device => device.Assessment.Status is
            HygieneStatus.Warning or HygieneStatus.CleanupCandidate);
        return new HygieneSummary(
            devices.Count,
            devices.Count(device => device.ActiveDirectory.Exists),
            devices.Count(device => device.Kaspersky.Exists),
            devices.Count(device => device.Opsi.Exists),
            healthy,
            problems,
            incomplete,
            devices.Count(device => Has(device,
                HygieneFindingCode.StaleAd,
                HygieneFindingCode.StaleKaspersky,
                HygieneFindingCode.StaleOpsi)),
            devices.Count(device => Has(device, HygieneFindingCode.MissingKaspersky)),
            devices.Count(device => Has(device, HygieneFindingCode.OrphanKaspersky)),
            devices.Count(device => Has(device, HygieneFindingCode.MissingOpsi)),
            devices.Count(device => Has(device, HygieneFindingCode.OrphanOpsi)),
            devices.Count(device => Has(device, HygieneFindingCode.OutdatedAgent, HygieneFindingCode.OutdatedKes)));
    }

    private Result<AdComputerInventoryQuery> BuildAdQuery(DirectoryInventoryConnection? input)
    {
        input ??= new DirectoryInventoryConnection();
        Result<ScanCredentials> credentials = BuildCredentials(
            input.UserName,
            input.UserDomain,
            input.Password,
            input.Domain);
        return credentials.IsFailure
            ? Result.Failure<AdComputerInventoryQuery>(credentials.Error!)
            : Result.Success(new AdComputerInventoryQuery(
                NormalizeOptional(input.Domain),
                NormalizeOptional(input.Server),
                credentials.Value,
                _options.InventoryLimit));
    }

    private Result<KasperskyConnection> BuildKasperskyConnection(
        KasperskyInventoryConnection? input,
        DirectoryInventoryConnection? adInput)
    {
        input ??= new KasperskyInventoryConnection();
        string? userName = NormalizeOptional(input.UserName) ?? NormalizeOptional(adInput?.UserName);
        string? password = input.Password ?? adInput?.Password;
        string? domain = NormalizeOptional(input.Domain) ?? NormalizeOptional(adInput?.UserDomain);
        if (userName is null || password is null)
        {
            return Result.Failure<KasperskyConnection>(new Error(
                ErrorCode.InvalidRequest,
                "Kaspersky Security Center credentials are required. Sign in with the read-only KSC account."));
        }

        if (userName.Contains('\\', StringComparison.Ordinal))
        {
            string[] parts = userName.Split('\\', 2);
            domain = parts[0];
            userName = parts[1];
        }
        else if (userName.Contains('@', StringComparison.Ordinal))
        {
            domain = null;
        }

        return Result.Success(new KasperskyConnection(
            NormalizeOptional(input.Server) ?? _options.Kaspersky.Server,
            input.Port ?? _options.Kaspersky.Port,
            userName,
            domain,
            password,
            NormalizeOptional(_options.Kaspersky.TrustedCertificateThumbprint),
            _options.Kaspersky.RequestTimeout,
            _options.InventoryLimit,
            _options.Kaspersky.ExcludedAdministrationGroups ?? []));
    }

    private static Result<ScanCredentials> BuildCredentials(
        string? userName,
        string? userDomain,
        string? password,
        string? directoryDomain)
    {
        string? normalizedUser = NormalizeOptional(userName);
        if (normalizedUser is null)
        {
            return Result.Success(ScanCredentials.CurrentUser);
        }

        if (password is null)
        {
            return Result.Failure<ScanCredentials>(new Error(
                ErrorCode.InvalidRequest,
                "Explicit Active Directory credentials require a password."));
        }

        if (normalizedUser.Contains('\\', StringComparison.Ordinal))
        {
            string[] parts = normalizedUser.Split('\\', 2);
            return parts.All(part => part.Length > 0)
                ? Result.Success(ScanCredentials.Explicit(parts[1], parts[0], password))
                : Result.Failure<ScanCredentials>(new Error(
                    ErrorCode.InvalidRequest,
                    "The Active Directory account is not a valid DOMAIN\\user name."));
        }

        if (normalizedUser.Contains('@', StringComparison.Ordinal))
        {
            return Result.Success(ScanCredentials.Explicit(normalizedUser, null, password));
        }

        string? domain = NormalizeOptional(userDomain) ?? NormalizeOptional(directoryDomain);
        return domain is not null
            ? Result.Success(ScanCredentials.Explicit(normalizedUser, domain, password))
            : Result.Failure<ScanCredentials>(new Error(
                ErrorCode.InvalidRequest,
                "The Active Directory account needs a domain."));
    }

    private static bool TryParseVersion(string? value, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] tokens = value.Trim().Split('.');
        parts = new int[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
        {
            if (!int.TryParse(tokens[index], out parts[index]) || parts[index] < 0)
            {
                parts = [];
                return false;
            }
        }

        return true;
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
