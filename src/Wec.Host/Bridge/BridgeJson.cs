using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wec.Host.Bridge;

internal static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        // Wire conventions per ADR 0003: camelCase properties, enums as SCREAMING_SNAKE strings
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        return options;
    }
}
