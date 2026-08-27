using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed record HygieneSourceLoad(
    Result<AdComputerInventory> ActiveDirectory,
    Result<KasperskyInventory> Kaspersky,
    Result<OpsiComputerInventory> Opsi,
    Result<NessusComputerInventory> Nessus,
    EnvironmentSourceStates States,
    string? DomainName);

internal sealed class HygieneSourceLoader
{
    private readonly IAdComputerInventoryProvider _activeDirectory;
    private readonly IKasperskyInventoryReader _kaspersky;
    private readonly IOpsiComputerInventoryProvider _opsi;
    private readonly INessusComputerInventoryProvider _nessus;
    private readonly IServiceCredentialStore _credentials;
    private readonly ItLifecycleOptions _options;

    public HygieneSourceLoader(
        IAdComputerInventoryProvider activeDirectory,
        IKasperskyInventoryReader kaspersky,
        IOpsiComputerInventoryProvider opsi,
        INessusComputerInventoryProvider nessus,
        IServiceCredentialStore credentials,
        ItLifecycleOptions options)
    {
        _activeDirectory = activeDirectory;
        _kaspersky = kaspersky;
        _opsi = opsi;
        _nessus = nessus;
        _credentials = credentials;
        _options = options;
    }

    public async Task<HygieneSourceLoad> LoadAsync(
        ItHygieneRequest request,
        HygieneLoadProgressTracker progress,
        CancellationToken cancellationToken)
    {
        Result<AdComputerInventoryQuery> adQuery = BuildAdQuery(request.ActiveDirectory);
        Result<KasperskyInventoryConnection?> kscInput = KasperskyInput(request.Kaspersky);
        Result<KasperskyConnection> kscConnection = kscInput.IsFailure
            ? Result.Failure<KasperskyConnection>(kscInput.Error!)
            : BuildKasperskyConnection(kscInput.Value);
        Task<Result<AdComputerInventory>> adTask = progress.TrackAdAsync(
            adQuery.IsSuccess
                ? LoadSourceAsync(
                    () => _activeDirectory.LoadAsync(adQuery.Value, cancellationToken),
                    "Active Directory",
                    cancellationToken)
                : Task.FromResult(Result.Failure<AdComputerInventory>(adQuery.Error!)));
        Task<Result<KasperskyInventory>> kscTask = progress.TrackKasperskyAsync(
            kscConnection.IsSuccess
                ? LoadSourceAsync(
                    () => _kaspersky.LoadAsync(kscConnection.Value, cancellationToken),
                    "Kaspersky Security Center",
                    cancellationToken)
                : Task.FromResult(Result.Failure<KasperskyInventory>(kscConnection.Error!)));
        Task<Result<OpsiComputerInventory>> opsiTask = progress.TrackOpsiAsync(
            LoadSourceAsync(
                () => _opsi.LoadAsync(_options.InventoryLimit, cancellationToken),
                "opsi",
                cancellationToken));
        Task<Result<NessusComputerInventory>> nessusTask = progress.TrackNessusAsync(
            LoadSourceAsync(
                () => _nessus.LoadAsync(cancellationToken),
                "Nessus",
                cancellationToken));

        await Task.WhenAll(adTask, kscTask, opsiTask, nessusTask);

        Result<AdComputerInventory> ad = await adTask;
        Result<KasperskyInventory> ksc = await kscTask;
        Result<OpsiComputerInventory> opsi = await opsiTask;
        Result<NessusComputerInventory> nessus = await nessusTask;
        var states = new EnvironmentSourceStates(
            AdState(ad),
            KasperskyState(ksc),
            OpsiState(opsi),
            NessusState(nessus));
        return new HygieneSourceLoad(
            ad,
            ksc,
            opsi,
            nessus,
            states,
            ad.IsSuccess ? ad.Value.DomainName : null);
    }

    internal static InventorySourceState AdState(Result<AdComputerInventory> result)
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

    internal static InventorySourceState KasperskyState(Result<KasperskyInventory> result) =>
        SourceState(result, notConnectedOnInvalidRequest: true);

    internal static InventorySourceState OpsiState(Result<OpsiComputerInventory> result) =>
        SourceState(result, notConnectedOnInvalidRequest: true);

    internal static InventorySourceState NessusState(Result<NessusComputerInventory> result)
    {
        if (result.IsFailure)
        {
            return new InventorySourceState(InventorySourceAvailability.Unavailable, ErrorText(result.Error!));
        }

        return new InventorySourceState(result.Value.Availability switch
        {
            NessusInventoryAvailability.Available => InventorySourceAvailability.Available,
            NessusInventoryAvailability.Partial => InventorySourceAvailability.Partial,
            NessusInventoryAvailability.NotConnected => InventorySourceAvailability.NotConnected,
            _ => InventorySourceAvailability.Unavailable,
        }, result.Value.Error);
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

    private Result<KasperskyConnection> BuildKasperskyConnection(KasperskyInventoryConnection? input)
    {
        input ??= new KasperskyInventoryConnection();
        string? userName = NormalizeOptional(input.UserName);
        string? password = input.Password;
        string? domain = NormalizeOptional(input.Domain);
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

    private static string ErrorText(Error error) =>
        string.IsNullOrWhiteSpace(error.Details) ? error.Message : $"{error.Message} {error.Details}";

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
