using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.IO;
using Wec.Host.Runtime;

namespace Wec.Host.Tests.Runtime;

public sealed class RuntimeInstanceProfileTests
{
    [Fact]
    public void InstalledProfile_PreservesProductionDefaults()
    {
        RuntimeInstanceProfile profile = Resolve("Production", @"C:\\Program Files\\Wec", requestedProfile: null);

        Assert.Equal("installed", profile.Name);
        Assert.False(profile.IsIsolated);
        Assert.Equal(RuntimeInstanceProfile.DefaultDatabasePath, profile.DatabasePath);
        Assert.Equal(RuntimeInstanceProfile.DefaultLogDirectory, profile.LogDirectory);
        Assert.Equal(RuntimeInstanceProfile.DefaultWebViewDirectory, profile.WebViewUserDataDirectory);
    }

    [Fact]
    public void DevelopmentProfile_IsStablePerCheckoutAndDifferentAcrossCheckouts()
    {
        RuntimeInstanceProfile first = Resolve(Environments.Development, @"C:\\src\\wec-a", requestedProfile: null);
        RuntimeInstanceProfile same = Resolve(Environments.Development, @"C:\\src\\wec-a", requestedProfile: null);
        RuntimeInstanceProfile other = Resolve(Environments.Development, @"C:\\src\\wec-b", requestedProfile: null);

        Assert.True(first.IsIsolated);
        Assert.Equal(first, same);
        Assert.NotEqual(first.DatabasePath, other.DatabasePath);
        Assert.Contains(Path.Combine("Wec", "development"), first.DatabasePath);
    }

    [Fact]
    public void DevServer_UsesIsolatedProfileOutsideDevelopmentEnvironment()
    {
        RuntimeInstanceProfile profile = Resolve(
            Environments.Production,
            @"C:\\src\\wec",
            requestedProfile: null,
            useDevServer: true);

        Assert.True(profile.IsIsolated);
        Assert.StartsWith("development-", profile.Name);
    }

    [Fact]
    public void NamedProfile_UsesExplicitStableDirectory()
    {
        RuntimeInstanceProfile profile = Resolve(
            Environments.Production,
            @"C:\\Program Files\\Wec",
            "test.checkout-2");

        Assert.Equal("test.checkout-2", profile.Name);
        Assert.Equal(
            Path.Combine(@"C:\\LocalAppData", "Wec", "profiles", "test.checkout-2", "wec.db"),
            profile.DatabasePath);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("contains space")]
    [InlineData("-starts-with-symbol")]
    public void InvalidNamedProfile_IsRejected(string profileName)
    {
        Assert.Throws<RuntimeProfileConfigurationException>(() =>
            Resolve(Environments.Production, @"C:\\Program Files\\Wec", profileName));
    }

    [Fact]
    public void IsolatedProfile_PreservesExplicitCustomPaths()
    {
        IConfiguration configuration = BuildConfiguration(useDevServer: true, new Dictionary<string, string?>
        {
            ["Wec:Database:DatabasePath"] = @"D:\\custom\\data.db",
            ["Wec:Logging:LogDirectory"] = @"D:\\custom\\logs",
            ["Wec:WebView:UserDataDirectory"] = @"D:\\custom\\browser",
        });

        RuntimeInstanceProfile profile = RuntimeInstanceProfile.Resolve(
            configuration,
            Environments.Production,
            @"C:\\src\\wec",
            @"C:\\LocalAppData",
            requestedProfile: null);

        Assert.Equal(@"D:\\custom\\data.db", profile.DatabasePath);
        Assert.Equal(@"D:\\custom\\logs", profile.LogDirectory);
        Assert.Equal(@"D:\\custom\\browser", profile.WebViewUserDataDirectory);
    }

    private static RuntimeInstanceProfile Resolve(
        string environment,
        string contentRoot,
        string? requestedProfile,
        bool useDevServer = false) => RuntimeInstanceProfile.Resolve(
            BuildConfiguration(useDevServer),
            environment,
            contentRoot,
            @"C:\\LocalAppData",
            requestedProfile);

    private static IConfiguration BuildConfiguration(
        bool useDevServer,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Wec:Frontend:UseDevServer"] = useDevServer.ToString(),
            ["Wec:Database:DatabasePath"] = RuntimeInstanceProfile.DefaultDatabasePath,
            ["Wec:Logging:LogDirectory"] = RuntimeInstanceProfile.DefaultLogDirectory,
            ["Wec:WebView:UserDataDirectory"] = RuntimeInstanceProfile.DefaultWebViewDirectory,
        };
        if (overrides is not null)
        {
            foreach ((string key, string? value) in overrides)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
