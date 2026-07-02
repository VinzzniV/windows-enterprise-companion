namespace Wec.Core.Abstractions;

/// <summary>
/// One directory entry as returned by <see cref="IDirectoryReader"/> —
/// LDAP attributes are multi-valued, so every attribute maps to a value list.
/// Attribute name lookup is case-insensitive regardless of the dictionary
/// passed in.
/// </summary>
public sealed class DirectoryEntryData
{
    private readonly Dictionary<string, IReadOnlyList<string>> _attributes;

    public DirectoryEntryData(
        string distinguishedName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> attributes)
    {
        DistinguishedName = distinguishedName;
        _attributes = new Dictionary<string, IReadOnlyList<string>>(attributes, StringComparer.OrdinalIgnoreCase);
    }

    public string DistinguishedName { get; }

    public IReadOnlyList<string> GetValues(string attributeName) =>
        _attributes.TryGetValue(attributeName, out IReadOnlyList<string>? values) ? values : [];

    public string? GetFirstValue(string attributeName)
    {
        IReadOnlyList<string> values = GetValues(attributeName);
        return values.Count > 0 ? values[0] : null;
    }

    public long? GetLong(string attributeName) =>
        long.TryParse(GetFirstValue(attributeName), System.Globalization.CultureInfo.InvariantCulture, out long value)
            ? value
            : null;

    /// <summary>
    /// Binary attribute values (e.g. objectSid) cross the seam Base64-encoded;
    /// this decodes the first value. Returns null when absent or not Base64.
    /// </summary>
    public byte[]? GetBytes(string attributeName)
    {
        string? value = GetFirstValue(attributeName);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
