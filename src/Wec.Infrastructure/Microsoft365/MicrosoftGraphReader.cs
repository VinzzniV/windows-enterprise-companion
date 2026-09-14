using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Wec.Core.Microsoft365;
using Wec.Core.Results;

namespace Wec.Infrastructure.Microsoft365;

public sealed class MicrosoftGraphReader : IMicrosoft365Reader, IDisposable
{
    private readonly Microsoft365Options _options;
    private readonly IMicrosoft365AuthenticationWindow _window;
    private readonly ILogger<MicrosoftGraphReader> _logger;
    private readonly HttpClient _http;
    private MsalGraphSession? _session;
    private GraphServiceClient? _graph;
    private Microsoft365Configuration _configuration;

    public MicrosoftGraphReader(IOptions<Microsoft365Options> options, IMicrosoft365AuthenticationWindow window,
        ILogger<MicrosoftGraphReader> logger)
    {
        _options = options.Value;
        _window = window;
        _logger = logger;
        _configuration = _options.Configuration;
        _http = new HttpClient(new GraphReadOnlyHandler(_options, new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
        })) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Microsoft365Connection Connection => new(_configuration, _session is not null,
        _session?.Account, _session?.Permissions ?? Microsoft365Scopes.For(_configuration)
            .Select(scope => new Microsoft365ScopeGrant(scope, false)).ToArray());

    public async Task<Result<Microsoft365Connection>> ConnectAsync(Microsoft365Configuration configuration,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(configuration.TenantId, out Guid tenantId) || tenantId == Guid.Empty
            || !Guid.TryParse(configuration.ClientId, out Guid clientId) || clientId == Guid.Empty)
        {
            return Result.Failure<Microsoft365Connection>(new Error(ErrorCode.Microsoft365ConfigurationInvalid,
                "Tenant ID and client ID must be non-empty GUIDs from your organizational app registration."));
        }
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        _configuration = configuration with { TenantId = tenantId.ToString("D"), ClientId = clientId.ToString("D") };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.AuthenticationTimeoutSeconds));
        try
        {
            if (_window.Handle == nint.Zero)
            {
                return Result.Failure<Microsoft365Connection>(new Error(ErrorCode.Microsoft365AuthenticationRequired,
                    "Open WEC's desktop window before signing in through Windows Account Manager."));
            }
            var session = new MsalGraphSession(_configuration, _window);
            await session.SignInAsync(_configuration.TenantId, timeout.Token).ConfigureAwait(false);
            _session = session;
            _graph = new GraphServiceClient(_http, session);
            return Result.Success(Connection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Fail<Microsoft365Connection>(exception, null);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _graph = null;
        _session = null;
        return Task.CompletedTask;
    }

    public async Task<Result<Microsoft365Data>> ReadAsync(Microsoft365Query query, CancellationToken cancellationToken)
    {
        GraphServiceClient? graph = _graph;
        if (graph is null)
        {
            return Result.Failure<Microsoft365Data>(new Error(ErrorCode.Microsoft365NotConnected,
                "Microsoft 365 is not connected. Open Microsoft 365 and sign in."));
        }
        string[] missing = Microsoft365Scopes.For(query.Resource)
            .Where(scope => !Connection.Permissions.Any(permission => permission.Granted && permission.Scope == scope)).ToArray();
        if (missing.Length > 0)
        {
            return Result.Failure<Microsoft365Data>(new Error(ErrorCode.Microsoft365PermissionMissing,
                $"This feature requires {string.Join(", ", missing)}. Enable the feature if needed, obtain admin consent and sign in again."));
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));
        try
        {
            return Result.Success(await ReadDataAsync(graph.RequestAdapter, query, _options, timeout.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Fail<Microsoft365Data>(exception, query.Resource);
        }
    }

    internal static async Task<Microsoft365Data> ReadDataAsync(IRequestAdapter adapter, Microsoft365Query query,
        Microsoft365Options options, CancellationToken cancellationToken)
    {
        string path = GraphQueries.Path(query, options.PageSize);
        if (query.Resource is Microsoft365Resource.User or Microsoft365Resource.UserActivity)
        {
            User user = await ReadOneAsync(adapter, path, User.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false);
            return query.Resource == Microsoft365Resource.User
                ? new Microsoft365Data { Users = [GraphMapping.User(user)] }
                : new Microsoft365Data { Activity = new(user.SignInActivity?.LastSignInDateTime,
                    user.SignInActivity?.LastSuccessfulSignInDateTime, null, null, null) };
        }
        if (query.Resource == Microsoft365Resource.UserRegistration)
        {
            UserRegistrationDetails report = await ReadOneAsync(adapter, path, UserRegistrationDetails.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false);
            return new Microsoft365Data { Activity = new(null, null, report.IsMfaRegistered, report.IsMfaCapable, report.MethodsRegistered) };
        }
        if (query.Resource == Microsoft365Resource.Group)
        {
            return new Microsoft365Data { Groups = [GraphMapping.Group(await ReadOneAsync(adapter, path, Group.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false))] };
        }
        if (query.Resource == Microsoft365Resource.Device)
        {
            return new Microsoft365Data { Devices = [GraphMapping.Device(await ReadOneAsync(adapter, path, Device.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false))] };
        }
        if (query.Resource == Microsoft365Resource.ManagedDevice)
        {
            return new Microsoft365Data { ManagedDevices = [GraphMapping.ManagedDevice(await ReadOneAsync(adapter, path, ManagedDevice.CreateFromDiscriminatorValue, cancellationToken).ConfigureAwait(false))] };
        }
        return query.Resource switch
        {
            Microsoft365Resource.Tenant => await ReadPagesAsync(adapter, path, OrganizationCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { Tenants = items.Select(item => new Microsoft365Tenant(item.Id, item.DisplayName)).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            Microsoft365Resource.Users => await ReadPagesAsync(adapter, path, UserCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { Users = items.Select(GraphMapping.User).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            Microsoft365Resource.Groups or Microsoft365Resource.UserGroups => await ReadPagesAsync(adapter, path, GroupCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { Groups = items.Select(GraphMapping.Group).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            Microsoft365Resource.Devices or Microsoft365Resource.UserDevices => await ReadPagesAsync(adapter, path, DeviceCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { Devices = items.Select(GraphMapping.Device).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            Microsoft365Resource.ManagedDevices => await ReadPagesAsync(adapter, path, ManagedDeviceCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { ManagedDevices = items.Select(GraphMapping.ManagedDevice).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            Microsoft365Resource.Licenses => await ReadPagesAsync(adapter, path, SubscribedSkuCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { Licenses = items.Select(GraphMapping.License).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            Microsoft365Resource.UserLicenses => await ReadPagesAsync(adapter, path, LicenseDetailsCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { Licenses = items.Select(GraphMapping.License).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            Microsoft365Resource.GroupMembers or Microsoft365Resource.DeviceOwners => await ReadPagesAsync(adapter, path, DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue,
                page => page.Value, items => new Microsoft365Data { Members = items.Select(GraphMapping.Member).ToArray() }, options, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };
    }

    private static async Task<Microsoft365Data> ReadPagesAsync<TPage, TItem>(IRequestAdapter adapter, string path,
        ParsableFactory<TPage> factory, Func<TPage, List<TItem>?> readItems, Func<List<TItem>, Microsoft365Data> map,
        Microsoft365Options options, CancellationToken cancellationToken) where TPage : BaseCollectionPaginationCountResponse, IParsable
    {
        string? next = "https://graph.microsoft.com/v1.0/" + path;
        string collectionPath = new Uri(next).AbsolutePath;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<TItem>();
        long? total = null;
        int pages = 0;
        while (next is not null && pages < options.MaximumPages && items.Count < options.MaximumItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(next) || !GraphReadOnlyHandler.IsAllowed(new Uri(next))
                || new Uri(next).AbsolutePath != collectionPath)
            {
                throw new InvalidDataException("Graph returned an invalid continuation.");
            }
            TPage page = await ReadOneAsync(adapter, next, factory, cancellationToken).ConfigureAwait(false);
            List<TItem> values = readItems(page) ?? throw new InvalidDataException("Graph collection value is missing.");
            if (page.OdataCount is < 0 || values.Any(item => item is null))
            {
                throw new InvalidDataException("Graph collection contains invalid count or null records.");
            }
            total ??= page.OdataCount;
            int remaining = options.MaximumItems - items.Count;
            items.AddRange(values.Take(remaining));
            next = page.OdataNextLink;
            pages++;
            if (values.Count > remaining)
            {
                return map(items) with { TotalCount = total, Truncated = true };
            }
        }
        return map(items) with { TotalCount = total, Truncated = next is not null };
    }

    private static async Task<T> ReadOneAsync<T>(IRequestAdapter adapter, string path, ParsableFactory<T> factory,
        CancellationToken cancellationToken) where T : IParsable
    {
        var uri = new Uri(path.StartsWith("https://", StringComparison.Ordinal) ? path : "https://graph.microsoft.com/v1.0/" + path);
        if (!GraphReadOnlyHandler.IsAllowed(uri)) { throw new InvalidDataException("Invalid Graph destination."); }
        var request = new RequestInformation { HttpMethod = Method.GET, URI = uri };
        request.Headers.Add("ConsistencyLevel", "eventual");
        request.Headers.Add("Accept", "application/json");
        return await adapter.SendAsync(request, factory, new Dictionary<string, ParsableFactory<IParsable>>(StringComparer.Ordinal)
        {
            ["4XX"] = ODataError.CreateFromDiscriminatorValue,
            ["5XX"] = ODataError.CreateFromDiscriminatorValue,
        }, cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Empty Graph response.");
    }

    private Result<T> Fail<T>(Exception exception, Microsoft365Resource? resource)
    {
        Error error = Microsoft365Errors.From(exception);
        // Raw SDK exceptions can contain account identifiers, tokens or response bodies.
        _logger.LogWarning("Microsoft 365 read failed: {Resource} {ErrorCode} HTTP {Status}",
            resource, error.Code, (exception as ApiException)?.ResponseStatusCode);
        return Result.Failure<T>(error);
    }

    private static bool IsExpected(Exception exception) => exception is Microsoft.Identity.Client.MsalException
        or ApiException or HttpRequestException or OperationCanceledException or InvalidDataException
        or System.Text.Json.JsonException or FormatException;

    public void Dispose() { _http.Dispose(); }
}
