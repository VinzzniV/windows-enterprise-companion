using Wec.Core.Snmp;
using Wec.Infrastructure.Snmp;

namespace Wec.Infrastructure.IntegrationTests.Snmp;

public sealed class SnmpBerTests
{
    /// <summary>
    /// Hand-built response (independent of our encoder): v2c, community
    /// "public", request id 1, no error, one var bind
    /// sysName.0 = OCTET STRING "PRINTER1".
    /// </summary>
    private const string SysNameResponseHex =
        "302E" +            // SEQUENCE, 46 bytes
        "020101" +          // INTEGER version = 1 (v2c)
        "04067075626C6963" + // OCTET STRING "public"
        "A221" +            // Response PDU, 33 bytes
        "020101" +          // request id = 1
        "020100" +          // error status = 0
        "020100" +          // error index = 0
        "3016" +            // var bind list
        "3014" +            // var bind
        "06082B06010201010500" + // OID 1.3.6.1.2.1.1.5.0
        "04085052494E54455231"; // OCTET STRING "PRINTER1"

    private static byte[] FromHex(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void DecodeResponse_ReadsHandBuiltSysNameAnswer()
    {
        SnmpResponse response = SnmpBer.DecodeResponse(FromHex(SysNameResponseHex));

        Assert.Equal(1, response.RequestId);
        Assert.Equal(0, response.ErrorStatus);
        SnmpVarBind varBind = Assert.Single(response.VarBinds);
        Assert.Equal("1.3.6.1.2.1.1.5.0", varBind.Oid);
        Assert.Equal("PRINTER1", varBind.Value.Text);
        Assert.Null(varBind.Value.Number);
    }

    [Fact]
    public void EncodeRequest_RoundTripsThroughTheDecoder()
    {
        byte[] request = SnmpBer.EncodeRequest(
            "internal-ro", SnmpPduType.GetRequest, 4711,
            ["1.3.6.1.2.1.43.5.1.1.17.1", "1.3.6.1.2.1.1.6.0"]);

        SnmpResponse decoded = SnmpBer.DecodeResponse(request);

        Assert.Equal(4711, decoded.RequestId);
        Assert.Equal(2, decoded.VarBinds.Count);
        Assert.Equal("1.3.6.1.2.1.43.5.1.1.17.1", decoded.VarBinds[0].Oid);
        Assert.Equal("1.3.6.1.2.1.1.6.0", decoded.VarBinds[1].Oid);
        // Request var binds carry NULL values
        Assert.Equal(SnmpValue.Empty, decoded.VarBinds[0].Value);
    }

    [Fact]
    public void EncodeOid_HandlesMultiByteArcs()
    {
        // 1.3.6.1.4.1.311 — arc 311 needs two base-128 bytes (0x82 0x37)
        byte[] encoded = SnmpBer.EncodeOid("1.3.6.1.4.1.311");

        Assert.Equal(new byte[] { 0x06, 0x07, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37 }, encoded);
    }

    [Fact]
    public void DecodeResponse_ReadsNegativeIntegerSupplyLevels()
    {
        // RFC 3805 uses -3 for "some supply remaining"; INTEGER -3 = 0xFD
        byte[] response = BuildResponse(
            requestId: 7, oidHex: "2B060102012B0B0101090101", valueTlvHex: "0201FD");

        SnmpResponse decoded = SnmpBer.DecodeResponse(response);

        Assert.Equal(-3, Assert.Single(decoded.VarBinds).Value.Number);
    }

    [Fact]
    public void DecodeResponse_ReadsCounter32AsNumber()
    {
        // Counter32 305419896 = 0x12345678
        byte[] response = BuildResponse(
            requestId: 7, oidHex: "2B060102012B0A0201040101", valueTlvHex: "410412345678");

        SnmpResponse decoded = SnmpBer.DecodeResponse(response);

        Assert.Equal(0x12345678, Assert.Single(decoded.VarBinds).Value.Number);
    }

    [Fact]
    public void DecodeResponse_TreatsNoSuchObjectAsEmptyValue()
    {
        byte[] response = BuildResponse(requestId: 7, oidHex: "2B06010201010500", valueTlvHex: "8000");

        SnmpResponse decoded = SnmpBer.DecodeResponse(response);

        Assert.Equal(SnmpValue.Empty, Assert.Single(decoded.VarBinds).Value);
        Assert.Equal(SnmpBer.TagNoSuchObject, Assert.Single(decoded.ValueTags));
    }

    [Fact]
    public void DecodeResponse_ThrowsOnTruncatedMessage()
    {
        byte[] truncated = FromHex(SysNameResponseHex)[..20];

        Assert.Throws<FormatException>(() => SnmpBer.DecodeResponse(truncated));
    }

    [Fact]
    public void IsWithinSubtree_RequiresProperPrefix()
    {
        Assert.True(SnmpBer.IsWithinSubtree("1.3.6.1.2.1.43.11.1.1", "1.3.6.1.2.1.43.11.1.1.9.1.1"));
        Assert.False(SnmpBer.IsWithinSubtree("1.3.6.1.2.1.43.11.1.1", "1.3.6.1.2.1.43.11.1.1"));
        Assert.False(SnmpBer.IsWithinSubtree("1.3.6.1.2.1.43.11.1.1", "1.3.6.1.2.1.43.11.1.10.1"));
        Assert.False(SnmpBer.IsWithinSubtree("1.3.6.1.2.1.43.11.1.1", "1.3.6.1.2.1.43.12.1.1.9"));
    }

    /// <summary>Builds a syntactically valid response around one raw OID content + value TLV.</summary>
    private static byte[] BuildResponse(int requestId, string oidHex, string valueTlvHex)
    {
        byte[] oidContent = FromHex(oidHex);
        byte[] valueTlv = FromHex(valueTlvHex);
        byte[] oidTlv = [0x06, (byte)oidContent.Length, .. oidContent];
        byte[] varBind = [0x30, (byte)(oidTlv.Length + valueTlv.Length), .. oidTlv, .. valueTlv];
        byte[] varBindList = [0x30, (byte)varBind.Length, .. varBind];
        byte[] pduContent =
        [
            0x02, 0x01, (byte)requestId,
            0x02, 0x01, 0x00,
            0x02, 0x01, 0x00,
            .. varBindList,
        ];
        byte[] pdu = [0xA2, (byte)pduContent.Length, .. pduContent];
        byte[] messageContent =
        [
            0x02, 0x01, 0x01,
            0x04, 0x06, .. "public"u8.ToArray(),
            .. pdu,
        ];
        return [0x30, (byte)messageContent.Length, .. messageContent];
    }
}
