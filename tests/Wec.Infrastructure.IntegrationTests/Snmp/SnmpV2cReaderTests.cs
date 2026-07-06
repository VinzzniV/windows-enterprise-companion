using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Results;
using Wec.Core.Snmp;
using Wec.Infrastructure.Snmp;

namespace Wec.Infrastructure.IntegrationTests.Snmp;

public sealed class SnmpV2cReaderTests
{
    private static readonly SnmpEndpoint Endpoint = new(
        "printer.kauth.local", 161, "internal-ro", TimeSpan.FromSeconds(1));

    /// <summary>Fake agent: answers GETs from an OID→value map, GETNEXT walks it in order.</summary>
    private static SnmpV2cReader.SnmpTransport FakeAgent(params (string Oid, SnmpValue Value)[] tree) =>
        (_, _, request, _, _) =>
        {
            SnmpResponse decoded = SnmpBer.DecodeResponse(request);
            // The PDU tag 0xA1 (GetNextRequest) appears exactly once in these small messages
            bool isGetNext = request.Contains((byte)0xA1);

            var answers = new List<(string Oid, SnmpValue Value)>();
            foreach (SnmpVarBind requested in decoded.VarBinds)
            {
                if (isGetNext)
                {
                    (string Oid, SnmpValue Value) next = tree
                        .FirstOrDefault(entry => string.CompareOrdinal(entry.Oid, requested.Oid) > 0);
                    answers.Add(next.Oid is null ? ("1.3.9999", SnmpValue.Empty) : next);
                }
                else
                {
                    (string Oid, SnmpValue Value) match = tree.FirstOrDefault(entry => entry.Oid == requested.Oid);
                    answers.Add((requested.Oid, match.Oid is null ? SnmpValue.Empty : match.Value));
                }
            }

            return Task.FromResult(SnmpTestMessages.BuildResponse(decoded.RequestId, answers));
        };

    private static SnmpV2cReader CreateReader(SnmpV2cReader.SnmpTransport transport) =>
        new(NullLogger<SnmpV2cReader>.Instance, transport);

    [Fact]
    public async Task Get_ReturnsValuesInRequestOrder()
    {
        SnmpV2cReader reader = CreateReader(FakeAgent(
            ("1.3.6.1.2.1.1.5.0", SnmpValue.OfText("PRINTER1")),
            ("1.3.6.1.2.1.1.6.0", SnmpValue.OfText("Denkingen"))));

        Result<IReadOnlyList<SnmpVarBind>> result = await reader.GetAsync(
            Endpoint, ["1.3.6.1.2.1.1.6.0", "1.3.6.1.2.1.1.5.0"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Denkingen", result.Value[0].Value.Text);
        Assert.Equal("PRINTER1", result.Value[1].Value.Text);
    }

    [Fact]
    public async Task Walk_CollectsTheSubtreeAndStopsOutsideIt()
    {
        SnmpV2cReader reader = CreateReader(FakeAgent(
            ("1.3.6.1.2.1.43.11.1.1.6.1.1", SnmpValue.OfText("Toner Black")),
            ("1.3.6.1.2.1.43.11.1.1.6.1.2", SnmpValue.OfText("Toner Cyan")),
            ("1.3.6.1.2.1.43.11.1.1.9.1.1", SnmpValue.OfNumber(42)),
            ("1.3.6.1.2.1.44.1.1", SnmpValue.OfText("outside"))));

        Result<IReadOnlyList<SnmpVarBind>> result = await reader.WalkAsync(
            Endpoint, "1.3.6.1.2.1.43.11.1.1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal("Toner Black", result.Value[0].Value.Text);
        Assert.Equal(42, result.Value[2].Value.Number);
        Assert.DoesNotContain(result.Value, varBind => varBind.Value.Text == "outside");
    }

    [Fact]
    public async Task Timeout_MapsToConnectionTimeoutWithCommunityHint()
    {
        SnmpV2cReader reader = CreateReader((_, _, _, _, _) =>
            throw new OperationCanceledException());

        Result<IReadOnlyList<SnmpVarBind>> result = await reader.GetAsync(
            Endpoint, ["1.3.6.1.2.1.1.5.0"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.ConnectionTimeout, result.Error!.Code);
        Assert.Contains("community", result.Error.Details, StringComparison.OrdinalIgnoreCase);
        // The community value itself must never appear in the error
        Assert.DoesNotContain("internal-ro", result.Error.Message + result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedAnswer_MapsToServiceUnavailable()
    {
        SnmpV2cReader reader = CreateReader((_, _, _, _, _) =>
            Task.FromResult(new byte[] { 0x30, 0x02, 0xFF, 0xFF }));

        Result<IReadOnlyList<SnmpVarBind>> result = await reader.GetAsync(
            Endpoint, ["1.3.6.1.2.1.1.5.0"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.ServiceUnavailable, result.Error!.Code);
    }

    [Fact]
    public void Endpoint_ToStringNeverContainsTheCommunity()
    {
        Assert.DoesNotContain("internal-ro", Endpoint.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>Builds response messages for the fake agent (BER by hand, mirroring SnmpBerTests).</summary>
internal static class SnmpTestMessages
{
    public static byte[] BuildResponse(int requestId, List<(string Oid, SnmpValue Value)> answers)
    {
        var varBinds = new List<byte>();
        foreach ((string oid, SnmpValue value) in answers)
        {
            byte[] oidTlv = SnmpBer.EncodeOid(oid);
            byte[] valueTlv = value switch
            {
                { Number: { } number } => EncodeInteger(number),
                { Text: { } text } => Tlv(0x04, System.Text.Encoding.UTF8.GetBytes(text)),
                _ => [0x05, 0x00],
            };
            varBinds.AddRange(Tlv(0x30, [.. oidTlv, .. valueTlv]));
        }

        byte[] pduContent =
        [
            .. EncodeInteger(requestId),
            .. EncodeInteger(0),
            .. EncodeInteger(0),
            .. Tlv(0x30, [.. varBinds]),
        ];
        byte[] messageContent =
        [
            .. EncodeInteger(1),
            .. Tlv(0x04, System.Text.Encoding.ASCII.GetBytes("internal-ro")),
            .. Tlv(0xA2, pduContent),
        ];
        return Tlv(0x30, messageContent);
    }

    private static byte[] EncodeInteger(long value)
    {
        var bytes = new List<byte>();
        long remaining = value;
        do
        {
            bytes.Insert(0, (byte)(remaining & 0xFF));
            remaining >>= 8;
        }
        while (remaining is not (0 or -1));
        if (value >= 0 && bytes[0] >= 0x80)
        {
            bytes.Insert(0, 0);
        }

        return Tlv(0x02, [.. bytes]);
    }

    private static byte[] Tlv(byte tag, byte[] content)
    {
        if (content.Length < 0x80)
        {
            return [tag, (byte)content.Length, .. content];
        }

        return [tag, 0x81, (byte)content.Length, .. content];
    }
}
