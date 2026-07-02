namespace Wec.Core.Abstractions;

public sealed class WmiInstance
{
    private readonly IReadOnlyDictionary<string, object?> _properties;

    public WmiInstance(IReadOnlyDictionary<string, object?> properties)
    {
        _properties = properties;
    }

    public object? GetRawValue(string propertyName) =>
        _properties.TryGetValue(propertyName, out object? value) ? value : null;

    public T? GetValue<T>(string propertyName) =>
        GetRawValue(propertyName) is T typedValue ? typedValue : default;

    public string? GetString(string propertyName) => GetRawValue(propertyName)?.ToString();

    public long? GetInteger(string propertyName) => GetRawValue(propertyName) switch
    {
        null => null,
        sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(
            GetRawValue(propertyName), System.Globalization.CultureInfo.InvariantCulture),
        ulong unsignedValue => unchecked((long)unsignedValue),
        _ => null,
    };
}
