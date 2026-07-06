using Wec.Core.Results;

namespace Wec.Core.Snmp;

/// <summary>
/// One SNMP agent (ADR 0009). The community string is configuration-derived,
/// passed per request, never logged and never persisted.
/// </summary>
public sealed record SnmpEndpoint(string Host, int Port, string Community, TimeSpan Timeout)
{
    // The generated record ToString would print the community; keep it out of
    // every log/exception path
    public override string ToString() => $"SnmpEndpoint {{ Host = {Host}, Port = {Port} }}";
}

/// <summary>
/// Decoded SNMP value: numeric types (INTEGER, Counter, Gauge, TimeTicks)
/// carry <see cref="Number"/>, string-like types (OCTET STRING, OID,
/// IpAddress) carry <see cref="Text"/>; NULL and noSuchObject/noSuchInstance
/// carry neither.
/// </summary>
public sealed record SnmpValue(string? Text, long? Number)
{
    public static SnmpValue Empty { get; } = new(null, null);

    public static SnmpValue OfText(string text) => new(text, null);

    public static SnmpValue OfNumber(long number) => new(null, number);
}

public sealed record SnmpVarBind(string Oid, SnmpValue Value);

/// <summary>
/// Read-only SNMP v2c access (ADR 0009) — the seam deliberately exposes no
/// write operation. Implemented in Infrastructure.
/// </summary>
public interface ISnmpReader
{
    /// <summary>GET for a fixed OID list; missing objects come back as empty values.</summary>
    Task<Result<IReadOnlyList<SnmpVarBind>>> GetAsync(
        SnmpEndpoint endpoint, IReadOnlyList<string> oids, CancellationToken cancellationToken);

    /// <summary>GETNEXT walk of the subtree under <paramref name="baseOid"/>.</summary>
    Task<Result<IReadOnlyList<SnmpVarBind>>> WalkAsync(
        SnmpEndpoint endpoint, string baseOid, CancellationToken cancellationToken);
}
