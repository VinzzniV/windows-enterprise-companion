using System.Text.Json;
using Wec.Core.Messaging;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

/// <summary>Wire conventions per ADR 0003: camelCase properties, SCREAMING_SNAKE enums.</summary>
public sealed class BridgeJsonTests
{
    [Fact]
    public void ResponseEnvelope_SerializesCamelCaseWithScreamingSnakeEnums()
    {
        var response = BridgeResponse.ForFailure(
            "abc-123",
            Error.AccessDenied("Denied.", PrivilegeLevel.Administrator));

        string json = JsonSerializer.Serialize(response, BridgeJson.Options);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("abc-123", root.GetProperty("id").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        JsonElement error = root.GetProperty("error");
        Assert.Equal("ACCESS_DENIED", error.GetProperty("code").GetString());
        Assert.Equal("ADMINISTRATOR", error.GetProperty("requiredPrivilege").GetString());
    }

    [Fact]
    public void RequestEnvelope_DeserializesFromCamelCaseWire()
    {
        const string json = """
            {"id":"42","module":"inventory","action":"getHardwareInfo","payload":{"forceRefresh":true}}
            """;

        BridgeRequest? request = JsonSerializer.Deserialize<BridgeRequest>(json, BridgeJson.Options);

        Assert.NotNull(request);
        Assert.Equal("42", request.Id);
        Assert.Equal("inventory", request.Module);
        Assert.Equal("getHardwareInfo", request.Action);
        Assert.True(request.Payload!.Value.GetProperty("forceRefresh").GetBoolean());
    }

    [Fact]
    public void MultiWordEnumValues_UseSnakeCaseUpper()
    {
        string json = JsonSerializer.Serialize(ErrorCode.DirectoryUnavailable, BridgeJson.Options);

        Assert.Equal("\"DIRECTORY_UNAVAILABLE\"", json);
    }
}
