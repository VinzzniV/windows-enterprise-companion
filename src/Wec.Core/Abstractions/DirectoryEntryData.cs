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
}
