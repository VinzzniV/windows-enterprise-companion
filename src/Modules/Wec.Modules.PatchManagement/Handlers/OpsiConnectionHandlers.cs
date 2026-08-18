using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Opsi;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Application;

namespace Wec.Modules.PatchManagement.Handlers;

public sealed record OpsiConnectRequest(
    string Server,
    string UserName,
    string? Password,
    bool TrustServerCertificate,
    bool UseStoredCredential = false,
    bool RememberCredential = false);

/// <summary>The bridge never carries the password back (ADR 0008).</summary>
public sealed record OpsiConnectionStatusResult(
    bool Connected,
    string? ServerUrl,
    string? UserName,
    string? OpsiVersion,
    string DefaultDepotFilter,
    string? ConnectionError = null);

internal sealed class ConnectOpsiHandler : IActionHandler<OpsiConnectRequest, OpsiConnectionStatusResult>
{
    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly PatchManagementOptions _options;
    private readonly IServiceCredentialStore _credentials;

    public ConnectOpsiHandler(
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        IServiceCredentialStore credentials,
        IOptions<PatchManagementOptions> options)
    {
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _credentials = credentials;
        _options = options.Value;
    }

    public string Module => "patchmanagement";

    public string Action => "connect";

    public async Task<Result<OpsiConnectionStatusResult>> HandleAsync(
        OpsiConnectRequest payload, CancellationToken cancellationToken)
    {
        string userName = payload.UserName;
        string? password = payload.Password;
        if (payload.UseStoredCredential)
        {
            Result<StoredServiceCredential?> stored = _credentials.Read(ServiceCredentialKind.Opsi);
            if (stored.IsFailure)
            {
                return Result.Failure<OpsiConnectionStatusResult>(stored.Error!);
            }
            if (stored.Value is null)
            {
                return Result.Failure<OpsiConnectionStatusResult>(new Error(
                    ErrorCode.InvalidRequest, "No saved opsi credential is available."));
            }
            userName = stored.Value.UserName;
            password = stored.Value.Password;
        }

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return Result.Failure<OpsiConnectionStatusResult>(new Error(
                ErrorCode.InvalidRequest, "opsi user name and password are required."));
        }

        Result<Uri> serviceUrl = OpsiServiceUrl.Normalize(payload.Server, _options.DefaultServicePort);
        if (serviceUrl.IsFailure)
        {
            return Result.Failure<OpsiConnectionStatusResult>(serviceUrl.Error!);
        }

        var connection = new OpsiConnection(
            serviceUrl.Value,
            userName.Trim(),
            password,
            payload.TrustServerCertificate,
            _options.OpsiRequestTimeout);

        Result<OpsiServerInfo> serverInfo =
            await _opsiClient.TestConnectionAsync(connection, cancellationToken);
        if (serverInfo.IsFailure)
        {
            // A failed test never replaces a working session
            return Result.Failure<OpsiConnectionStatusResult>(serverInfo.Error!);
        }

        if (payload.RememberCredential)
        {
            Result<bool> saved = _credentials.Save(
                ServiceCredentialKind.Opsi,
                new StoredServiceCredential(userName.Trim(), null, password));
            if (saved.IsFailure)
            {
                return Result.Failure<OpsiConnectionStatusResult>(saved.Error!);
            }
        }

        _sessionState.Set(new OpsiSession(connection, serverInfo.Value));
        return Result.Success(StatusOf(_sessionState, _options));
    }

    internal static OpsiConnectionStatusResult StatusOf(
        OpsiSessionState sessionState, PatchManagementOptions options) =>
        sessionState.Current is { } session
            ? new OpsiConnectionStatusResult(
                Connected: true,
                session.Connection.ServiceUrl.ToString(),
                session.Connection.UserName,
                session.ServerInfo.OpsiVersion,
                options.DefaultDepotFilter)
            : new OpsiConnectionStatusResult(
                Connected: false, null, null, null, options.DefaultDepotFilter);
}

public sealed record OpsiDisconnectRequest;

internal sealed class DisconnectOpsiHandler : IActionHandler<OpsiDisconnectRequest, OpsiConnectionStatusResult>
{
    private readonly OpsiSessionState _sessionState;
    private readonly PatchManagementOptions _options;

    public DisconnectOpsiHandler(OpsiSessionState sessionState, IOptions<PatchManagementOptions> options)
    {
        _sessionState = sessionState;
        _options = options.Value;
    }

    public string Module => "patchmanagement";

    public string Action => "disconnect";

    public Task<Result<OpsiConnectionStatusResult>> HandleAsync(
        OpsiDisconnectRequest payload, CancellationToken cancellationToken)
    {
        _sessionState.Clear();
        return Task.FromResult(Result.Success(ConnectOpsiHandler.StatusOf(_sessionState, _options)));
    }
}

public sealed record OpsiConnectionStatusRequest;

internal sealed class GetOpsiConnectionStatusHandler
    : IActionHandler<OpsiConnectionStatusRequest, OpsiConnectionStatusResult>
{
    private readonly OpsiSessionState _sessionState;
    private readonly PatchManagementOptions _options;
    private readonly OpsiSessionConnector _connector;

    public GetOpsiConnectionStatusHandler(
        OpsiSessionState sessionState,
        OpsiSessionConnector connector,
        IOptions<PatchManagementOptions> options)
    {
        _sessionState = sessionState;
        _connector = connector;
        _options = options.Value;
    }

    public string Module => "patchmanagement";

    public string Action => "getConnectionStatus";

    public async Task<Result<OpsiConnectionStatusResult>> HandleAsync(
        OpsiConnectionStatusRequest payload, CancellationToken cancellationToken)
    {
        Result<OpsiSession?> connected = await _connector.EnsureConnectedAsync(cancellationToken);
        return connected.IsFailure
            ? Result.Success(ConnectOpsiHandler.StatusOf(_sessionState, _options) with
            {
                ConnectionError = connected.Error!.Message,
            })
            : Result.Success(ConnectOpsiHandler.StatusOf(_sessionState, _options));
    }
}
