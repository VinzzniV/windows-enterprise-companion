using System.Globalization;
using System.Text;
using Wec.Core.Snmp;

namespace Wec.Infrastructure.Snmp;

internal enum SnmpPduType : byte
{
    GetRequest = 0xA0,
    GetNextRequest = 0xA1,
    Response = 0xA2,
}

internal sealed record SnmpResponse(
    int RequestId,
    int ErrorStatus,
    IReadOnlyList<SnmpVarBind> VarBinds,
    IReadOnlyList<byte> ValueTags);

/// <summary>
/// Minimal BER codec for SNMP v2c GET/GETNEXT (ADR 0009): pure byte handling,
/// no I/O. Malformed input throws <see cref="FormatException"/>; the transport
/// maps that to a typed error.
/// </summary>
internal static class SnmpBer
{
    private const byte TagInteger = 0x02;
    private const byte TagOctetString = 0x04;
    private const byte TagNull = 0x05;
    private const byte TagObjectIdentifier = 0x06;
    private const byte TagSequence = 0x30;
    private const byte TagIpAddress = 0x40;
    private const byte TagCounter32 = 0x41;
    private const byte TagGauge32 = 0x42;
    private const byte TagTimeTicks = 0x43;
    private const byte TagCounter64 = 0x46;
    internal const byte TagNoSuchObject = 0x80;
    internal const byte TagNoSuchInstance = 0x81;
    internal const byte TagEndOfMibView = 0x82;

    private const int SnmpV2cVersion = 1;

    internal static byte[] EncodeRequest(
        string community, SnmpPduType pduType, int requestId, IReadOnlyList<string> oids)
    {
        byte[] varBinds = EncodeTlv(TagSequence, Concat(
            [.. oids.Select(oid => EncodeTlv(TagSequence, Concat([EncodeOid(oid), EncodeTlv(TagNull, [])])))]));
        byte[] pdu = EncodeTlv((byte)pduType, Concat(
        [
            EncodeInteger(requestId),
            EncodeInteger(0),
            EncodeInteger(0),
            varBinds,
        ]));
        return EncodeTlv(TagSequence, Concat(
        [
            EncodeInteger(SnmpV2cVersion),
            EncodeTlv(TagOctetString, Encoding.ASCII.GetBytes(community)),
            pdu,
        ]));
    }

    internal static SnmpResponse DecodeResponse(byte[] message)
    {
        int offset = 0;
        int messageEnd = ReadTlvHeader(message, ref offset, TagSequence);

        // version
        ReadInteger(message, ref offset);
        // community (not returned — never logged)
        int communityEnd = ReadTlvHeader(message, ref offset, TagOctetString);
        offset = communityEnd;

        byte pduTag = PeekTag(message, offset);
        if (pduTag is not ((byte)SnmpPduType.Response or (byte)SnmpPduType.GetRequest or (byte)SnmpPduType.GetNextRequest))
        {
            throw new FormatException($"Unexpected PDU tag 0x{pduTag:X2}.");
        }

        ReadTlvHeader(message, ref offset, pduTag);
        int requestId = (int)ReadInteger(message, ref offset);
        int errorStatus = (int)ReadInteger(message, ref offset);
        ReadInteger(message, ref offset); // error index

        int varBindListEnd = ReadTlvHeader(message, ref offset, TagSequence);
        var varBinds = new List<SnmpVarBind>();
        var valueTags = new List<byte>();
        while (offset < varBindListEnd && offset < messageEnd)
        {
            ReadTlvHeader(message, ref offset, TagSequence);
            string oid = ReadOid(message, ref offset);
            byte valueTag = PeekTag(message, offset);
            SnmpValue value = ReadValue(message, ref offset);
            varBinds.Add(new SnmpVarBind(oid, value));
            valueTags.Add(valueTag);
        }

        return new SnmpResponse(requestId, errorStatus, varBinds, valueTags);
    }

    internal static bool IsWithinSubtree(string baseOid, string oid) =>
        oid.StartsWith(baseOid + ".", StringComparison.Ordinal);

    private static SnmpValue ReadValue(byte[] data, ref int offset)
    {
        byte tag = PeekTag(data, offset);
        switch (tag)
        {
            case TagInteger:
                return SnmpValue.OfNumber(ReadInteger(data, ref offset));
            case TagCounter32:
            case TagGauge32:
            case TagTimeTicks:
            case TagCounter64:
                return SnmpValue.OfNumber(ReadUnsigned(data, ref offset));
            case TagOctetString:
            {
                int end = ReadTlvHeader(data, ref offset, tag);
                string text = DecodeOctetString(data.AsSpan(offset, end - offset));
                offset = end;
                return SnmpValue.OfText(text);
            }

            case TagObjectIdentifier:
                return SnmpValue.OfText(ReadOid(data, ref offset));
            case TagIpAddress:
            {
                int end = ReadTlvHeader(data, ref offset, tag);
                string address = string.Join('.', data.AsSpan(offset, end - offset).ToArray());
                offset = end;
                return SnmpValue.OfText(address);
            }

            default:
            {
                // NULL, noSuchObject, noSuchInstance, endOfMibView, anything exotic
                int end = ReadTlvHeader(data, ref offset, tag);
                offset = end;
                return SnmpValue.Empty;
            }
        }
    }

    private static string DecodeOctetString(ReadOnlySpan<byte> content)
    {
        string text = Encoding.UTF8.GetString(content).TrimEnd('\0').Trim();
        return text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
            ? Convert.ToHexString(content)
            : text;
    }

    private static long ReadInteger(byte[] data, ref int offset)
    {
        int end = ReadTlvHeader(data, ref offset, TagInteger);
        long value = data[offset] >= 0x80 ? -1 : 0;
        for (; offset < end; offset++)
        {
            value = (value << 8) | data[offset];
        }

        return value;
    }

    private static long ReadUnsigned(byte[] data, ref int offset)
    {
        byte tag = PeekTag(data, offset);
        int end = ReadTlvHeader(data, ref offset, tag);
        ulong value = 0;
        for (; offset < end; offset++)
        {
            value = (value << 8) | data[offset];
        }

        return unchecked((long)value);
    }

    private static string ReadOid(byte[] data, ref int offset)
    {
        int end = ReadTlvHeader(data, ref offset, TagObjectIdentifier);
        if (offset >= end)
        {
            throw new FormatException("Empty OBJECT IDENTIFIER.");
        }

        var arcs = new List<long> { data[offset] / 40, data[offset] % 40 };
        offset++;
        long current = 0;
        for (; offset < end; offset++)
        {
            current = (current << 7) | (uint)(data[offset] & 0x7F);
            if ((data[offset] & 0x80) == 0)
            {
                arcs.Add(current);
                current = 0;
            }
        }

        return string.Join('.', arcs);
    }

    private static byte PeekTag(byte[] data, int offset)
    {
        if (offset >= data.Length)
        {
            throw new FormatException("Truncated SNMP message.");
        }

        return data[offset];
    }

    /// <summary>Reads tag + length, returns the end offset of the content; leaves offset at the content start.</summary>
    private static int ReadTlvHeader(byte[] data, ref int offset, byte expectedTag)
    {
        byte tag = PeekTag(data, offset);
        if (tag != expectedTag)
        {
            throw new FormatException($"Expected tag 0x{expectedTag:X2} but found 0x{tag:X2} at offset {offset}.");
        }

        offset++;
        if (offset >= data.Length)
        {
            throw new FormatException("Truncated SNMP message.");
        }

        int length = data[offset];
        offset++;
        if (length >= 0x80)
        {
            int lengthBytes = length & 0x7F;
            if (lengthBytes is 0 or > 4 || offset + lengthBytes > data.Length)
            {
                throw new FormatException("Unsupported BER length encoding.");
            }

            length = 0;
            for (int index = 0; index < lengthBytes; index++, offset++)
            {
                length = (length << 8) | data[offset];
            }
        }

        if (offset + length > data.Length)
        {
            throw new FormatException("BER length exceeds the message.");
        }

        return offset + length;
    }

    private static byte[] EncodeTlv(byte tag, byte[] content)
    {
        byte[] lengthBytes;
        if (content.Length < 0x80)
        {
            lengthBytes = [(byte)content.Length];
        }
        else if (content.Length <= 0xFF)
        {
            lengthBytes = [0x81, (byte)content.Length];
        }
        else
        {
            lengthBytes = [0x82, (byte)(content.Length >> 8), (byte)content.Length];
        }

        byte[] result = new byte[1 + lengthBytes.Length + content.Length];
        result[0] = tag;
        lengthBytes.CopyTo(result, 1);
        content.CopyTo(result, 1 + lengthBytes.Length);
        return result;
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

        // Ensure the sign bit matches the value's sign
        if (value >= 0 && bytes[0] >= 0x80)
        {
            bytes.Insert(0, 0);
        }
        else if (value < 0 && bytes[0] < 0x80)
        {
            bytes.Insert(0, 0xFF);
        }

        return EncodeTlv(TagInteger, [.. bytes]);
    }

    internal static byte[] EncodeOid(string dottedOid)
    {
        long[] arcs;
        try
        {
            arcs = [.. dottedOid.Split('.').Select(arc => long.Parse(arc, CultureInfo.InvariantCulture))];
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new FormatException($"'{dottedOid}' is not a dotted OID.", exception);
        }

        if (arcs.Length < 2)
        {
            throw new FormatException($"'{dottedOid}' is not a dotted OID.");
        }

        var content = new List<byte> { (byte)(arcs[0] * 40 + arcs[1]) };
        foreach (long arc in arcs.Skip(2))
        {
            var encoded = new List<byte> { (byte)(arc & 0x7F) };
            long remaining = arc >> 7;
            while (remaining > 0)
            {
                encoded.Insert(0, (byte)(0x80 | (remaining & 0x7F)));
                remaining >>= 7;
            }

            content.AddRange(encoded);
        }

        return EncodeTlv(TagObjectIdentifier, [.. content]);
    }

    private static byte[] Concat(IReadOnlyList<byte[]> parts)
    {
        byte[] result = new byte[parts.Sum(part => part.Length)];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
