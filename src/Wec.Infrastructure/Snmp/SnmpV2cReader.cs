using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Wec.Core.Results;
using Wec.Core.Snmp;

namespace Wec.Infrastructure.Snmp;

/// <summary>
/// Minimal SNMP v2c reader (ADR 0009): GET and GETNEXT walks over UDP.
/// Deliberately no library dependency and no write operation. The community
/// string never reaches a log or an error message.
/// </summary>
public sealed partial class SnmpV2cReader : ISnmpReader
{
    // Safety bound against broken agents that loop instead of leaving the
    // subtree — a printer supplies table has a handful of rows, not hundreds
    private const int MaxWalkSteps = 512;

    internal delegate Task<byte[]> SnmpTransport(
        string host, int port, byte[] request, TimeSpan timeout, CancellationToken cancellationToken);

    private static int _requestCounter = Environment.TickCount & 0x7FFF;

    private readonly ILogger<SnmpV2cReader> _logger;
    private readonly SnmpTransport _transport;

    public SnmpV2cReader(ILogger<SnmpV2cReader> logger)
        : this(logger, ExchangeOverUdpAsync)
    {
    }

    // Tests inject a fake transport
    internal SnmpV2cReader(ILogger<SnmpV2cReader> logger, SnmpTransport transport)
    {
        _logger = logger;
        _transport = transport;
    }

    public async Task<Result<IReadOnlyList<SnmpVarBind>>> GetAsync(
        SnmpEndpoint endpoint, IReadOnlyList<string> oids, CancellationToken cancellationToken)
    {
        if (oids.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SnmpVarBind>>(new Error(
                ErrorCode.InvalidRequest, "An SNMP GET needs at least one OID."));
        }

        Result<SnmpResponse> response = await ExchangeAsync(
            endpoint, SnmpPduType.GetRequest, oids, cancellationToken).ConfigureAwait(false);
        return response.IsSuccess
            ? Result.Success(response.Value.VarBinds)
            : Result.Failure<IReadOnlyList<SnmpVarBind>>(response.Error!);
    }

    public async Task<Result<IReadOnlyList<SnmpVarBind>>> WalkAsync(
        SnmpEndpoint endpoint, string baseOid, CancellationToken cancellationToken)
    {
        var collected = new List<SnmpVarBind>();
        string currentOid = baseOid;
        for (int step = 0; step < MaxWalkSteps; step++)
        {
            Result<SnmpResponse> response = await ExchangeAsync(
                endpoint, SnmpPduType.GetNextRequest, [currentOid], cancellationToken).ConfigureAwait(false);
            if (response.IsFailure)
            {
                return Result.Failure<IReadOnlyList<SnmpVarBind>>(response.Error!);
            }

            if (response.Value.VarBinds.Count == 0
                || response.Value.ValueTags[0] == SnmpBer.TagEndOfMibView
                || !SnmpBer.IsWithinSubtree(baseOid, response.Value.VarBinds[0].Oid)
                || string.CompareOrdinal(response.Value.VarBinds[0].Oid, currentOid) <= 0)
            {
                return Result.Success<IReadOnlyList<SnmpVarBind>>(collected);
            }

            collected.Add(response.Value.VarBinds[0]);
            currentOid = response.Value.VarBinds[0].Oid;
        }

        LogWalkCapped(baseOid, endpoint.Host, MaxWalkSteps);
        return Result.Success<IReadOnlyList<SnmpVarBind>>(collected);
    }

    private async Task<Result<SnmpResponse>> ExchangeAsync(
        SnmpEndpoint endpoint,
        SnmpPduType pduType,
        IReadOnlyList<string> oids,
        CancellationToken cancellationToken)
    {
        int requestId = Interlocked.Increment(ref _requestCounter) & 0x7FFFFFFF;
        byte[] request;
        try
        {
            request = SnmpBer.EncodeRequest(endpoint.Community, pduType, requestId, oids);
        }
        catch (FormatException exception)
        {
            return Result.Failure<SnmpResponse>(new Error(ErrorCode.InvalidRequest, exception.Message));
        }

        try
        {
            byte[] responseBytes = await _transport(
                endpoint.Host, endpoint.Port, request, endpoint.Timeout, cancellationToken).ConfigureAwait(false);
            SnmpResponse response = SnmpBer.DecodeResponse(responseBytes);
            if (response.RequestId != requestId)
            {
                return Result.Failure<SnmpResponse>(new Error(
                    ErrorCode.ServiceUnavailable,
                    $"'{endpoint.Host}' answered with a mismatched SNMP request id."));
            }

            if (response.ErrorStatus != 0)
            {
                return Result.Failure<SnmpResponse>(new Error(
                    ErrorCode.RemoteCommandFailed,
                    $"The SNMP agent on '{endpoint.Host}' reported error status {response.ErrorStatus}."));
            }

            return Result.Success(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<SnmpResponse>(new Error(
                ErrorCode.ConnectionTimeout,
                $"'{endpoint.Host}' did not answer on UDP {endpoint.Port} within "
                + $"{endpoint.Timeout.TotalSeconds:0} seconds.")
            {
                Details = "SNMP disabled on the device, a wrong read community (agents silently "
                    + "drop those requests), or a firewall in between.",
            });
        }
        catch (SocketException exception) when (exception.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return Result.Failure<SnmpResponse>(new Error(
                ErrorCode.DnsResolutionFailed,
                $"'{endpoint.Host}' could not be resolved in DNS."));
        }
        catch (SocketException exception)
        {
            LogTransportError(endpoint.Host, exception.Message);
            return Result.Failure<SnmpResponse>(new Error(
                ErrorCode.ServiceUnavailable,
                $"SNMP transport to '{endpoint.Host}' failed ({exception.SocketErrorCode})."));
        }
        catch (FormatException exception)
        {
            LogTransportError(endpoint.Host, exception.Message);
            return Result.Failure<SnmpResponse>(new Error(
                ErrorCode.ServiceUnavailable,
                $"'{endpoint.Host}' answered with a malformed SNMP message.")
            {
                Details = exception.Message,
            });
        }
    }

    private static async Task<byte[]> ExchangeOverUdpAsync(
        string host, int port, byte[] request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var client = new UdpClient();
        client.Connect(host, port);
        await client.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
        UdpReceiveResult result = await client.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
        return result.Buffer;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SNMP walk of {BaseOid} on {Host} hit the {MaxSteps}-step cap")]
    private partial void LogWalkCapped(string baseOid, string host, int maxSteps);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SNMP exchange with {Host} failed: {Reason}")]
    private partial void LogTransportError(string host, string reason);
}
