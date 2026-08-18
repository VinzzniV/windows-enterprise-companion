using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.Results;

namespace Wec.Modules.PatchManagement.Application;

/// <summary>Restores the shared opsi session on demand from Windows Credential Manager.</summary>
public sealed class OpsiSessionConnector : IDisposable
{
    private readonly IOpsiClient _client;
    private readonly OpsiSessionState _session;
    private readonly IServiceCredentialStore _credentials;
    private readonly PatchManagementOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OpsiSessionConnector(
        IOpsiClient client,
        OpsiSessionState session,
        IServiceCredentialStore credentials,
        IOptions<PatchManagementOptions> options)
    {
        _client = client;
        _session = session;
        _credentials = credentials;
        _options = options.Value;
    }

    public async Task<Result<OpsiSession?>> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_session.Current is { } current)
        {
            return Result.Success<OpsiSession?>(current);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session.Current is { } connected)
            {
                return Result.Success<OpsiSession?>(connected);
            }

            Result<StoredServiceCredential?> stored = _credentials.Read(ServiceCredentialKind.Opsi);
            if (stored.IsFailure)
            {
                return Result.Failure<OpsiSession?>(stored.Error!);
            }
            if (stored.Value is null || string.IsNullOrWhiteSpace(_options.OpsiServer))
            {
                return Result.Success<OpsiSession?>(null);
            }

            Result<Uri> serviceUrl = OpsiServiceUrl.Normalize(
                _options.OpsiServer,
                _options.DefaultServicePort);
            if (serviceUrl.IsFailure)
            {
                return Result.Failure<OpsiSession?>(serviceUrl.Error!);
            }

            var connection = new OpsiConnection(
                serviceUrl.Value,
                stored.Value.UserName,
                stored.Value.Password,
                _options.TrustServerCertificate,
                _options.OpsiRequestTimeout);
            Result<OpsiServerInfo> serverInfo =
                await _client.TestConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (serverInfo.IsFailure)
            {
                return Result.Failure<OpsiSession?>(serverInfo.Error!);
            }

            var session = new OpsiSession(connection, serverInfo.Value);
            _session.Set(session);
            return Result.Success<OpsiSession?>(session);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
