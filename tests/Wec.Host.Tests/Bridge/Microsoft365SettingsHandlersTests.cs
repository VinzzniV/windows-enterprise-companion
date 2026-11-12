using System.IO;
using System.Text.Json.Nodes;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class Microsoft365SettingsHandlersTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wec-microsoft365-settings-tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "usersettings.json");

    [Fact]
    public async Task SavePreservesProviderLimitsAndUnrelatedSettingsWithoutPersistingTokens()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, """
            {"Wec":{"Microsoft365":{"MaximumItems":42,"Cache":{"FreshMinutes":5}},"Logging":{"MinimumLevel":"Debug"}}}
            """);
        var handler = new SaveMicrosoft365SettingsHandler(new UserSettingsStore(SettingsPath));
        var configuration = new Microsoft365Configuration(" 11111111-1111-1111-1111-111111111111 ",
            "22222222-2222-2222-2222-222222222222", true, false);
        var result = await handler.HandleAsync(new(configuration), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RestartRequired);
        JsonNode root = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath))!;
        JsonObject cloud = root["Wec"]!["Microsoft365"]!.AsObject();
        Assert.Equal("11111111-1111-1111-1111-111111111111", cloud["TenantId"]!.GetValue<string>());
        Assert.Equal(42, cloud["MaximumItems"]!.GetValue<int>());
        Assert.Equal(5, cloud["Cache"]!["FreshMinutes"]!.GetValue<int>());
        Assert.Equal("Debug", root["Wec"]!["Logging"]!["MinimumLevel"]!.GetValue<string>());
        Assert.Equal(6, cloud.Count);
    }

    [Theory]
    [InlineData("common", "22222222-2222-2222-2222-222222222222")]
    [InlineData("00000000-0000-0000-0000-000000000000", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "")]
    public async Task InvalidIdentifiersDoNotCreateASettingsFile(string tenant, string client)
    {
        var result = await new SaveMicrosoft365SettingsHandler(new UserSettingsStore(SettingsPath))
            .HandleAsync(new(new(tenant, client)), CancellationToken.None);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task ClearDefaultsIsSupported()
    {
        var result = await new SaveMicrosoft365SettingsHandler(new UserSettingsStore(SettingsPath))
            .HandleAsync(new(new("", "")), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value.Settings.TenantId);
    }

    [Fact]
    public async Task InvalidExistingFileIsPreservedWithoutLeakingContentsInTheError()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{invalid-private-content");
        var result = await new SaveMicrosoft365SettingsHandler(new UserSettingsStore(SettingsPath))
            .HandleAsync(new(new("", "")), CancellationToken.None);
        Assert.Equal(ErrorCode.FileWriteFailed, result.Error!.Code);
        Assert.Null(result.Error.Details);
        Assert.Equal("{invalid-private-content", await File.ReadAllTextAsync(SettingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
