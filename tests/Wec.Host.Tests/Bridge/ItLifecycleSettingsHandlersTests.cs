using System.IO;
using System.Text.Json.Nodes;
using Wec.Core.Results;
using Wec.Host.Bridge;
using Wec.Modules.EmployeeLifecycle;

namespace Wec.Host.Tests.Bridge;

public sealed class ItLifecycleSettingsHandlersTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "wec-it-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_MergesItLifecycleSectionWithoutLosingOtherSettings()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "usersettings.json");
        await File.WriteAllTextAsync(path, """
            {"Wec":{"Logging":{"MinimumLevel":"Debug"}},"Unrelated":42}
            """);
        var store = new UserSettingsStore(path);

        Result<bool> result = await store.SaveItLifecycleAsync(Options(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        JsonObject root = (JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject)!;
        Assert.Equal(42, root["Unrelated"]!.GetValue<int>());
        Assert.Equal("Debug", root["Wec"]!["Logging"]!["MinimumLevel"]!.GetValue<string>());
        Assert.Equal("ksc.example.test", root["Wec"]!["ItLifecycle"]!["kaspersky"]!["server"]!.GetValue<string>());
        Assert.Equal(60, root["Wec"]!["ItLifecycle"]!["staleWarningDays"]!.GetValue<int>());
        Assert.Equal(
            "Nicht für Kaspersky geeignete Geräte",
            root["Wec"]!["ItLifecycle"]!["kaspersky"]!["excludedAdministrationGroups"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Save_DoesNotOverwriteMalformedExistingFile()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "usersettings.json");
        await File.WriteAllTextAsync(path, "{broken");
        var store = new UserSettingsStore(path);

        Result<bool> result = await store.SaveItLifecycleAsync(Options(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.FileWriteFailed, result.Error!.Code);
        Assert.Equal("{broken", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveOpsi_MergesPersistentValuesWithoutCredentialsOrLosingOtherSettings()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "usersettings.json");
        await File.WriteAllTextAsync(path, """
            {"Wec":{"PatchManagement":{"PackageBuilder":"opsi-package-updater"},"Logging":{"MinimumLevel":"Debug"}}}
            """);
        var store = new UserSettingsStore(path);

        Result<bool> result = await store.SaveOpsiAsync(
            new OpsiSettingsValue("opsi.example.test", 4447, 90, "depot01", true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        string json = await File.ReadAllTextAsync(path);
        JsonObject root = (JsonNode.Parse(json) as JsonObject)!;
        JsonNode patch = root["Wec"]!["PatchManagement"]!;
        Assert.Equal("opsi.example.test", patch["OpsiServer"]!.GetValue<string>());
        Assert.Equal(4447, patch["DefaultServicePort"]!.GetValue<int>());
        Assert.Equal("00:01:30", patch["OpsiRequestTimeout"]!.GetValue<string>());
        Assert.Equal("depot01", patch["DefaultDepotFilter"]!.GetValue<string>());
        Assert.True(patch["TrustServerCertificate"]!.GetValue<bool>());
        Assert.Equal("opsi-package-updater", patch["PackageBuilder"]!.GetValue<string>());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static ItLifecycleOptions Options() => new()
    {
        InventoryLimit = 10_000,
        StaleWarningDays = 60,
        StaleCriticalDays = 90,
        TargetAgentVersion = "16.0.0.254",
        TargetKesVersion = "21.25.7.504",
        Kaspersky = new KasperskyOptions
        {
            Server = "ksc.example.test",
            Port = 13_299,
            RequestTimeout = TimeSpan.FromSeconds(60),
            ExcludedAdministrationGroups = ["Nicht für Kaspersky geeignete Geräte"],
        },
    };
}
