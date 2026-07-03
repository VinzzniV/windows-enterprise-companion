using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Tests.Application;

public class RemoteInstalledSoftwareReaderTests
{
    private static readonly ScanTarget Target = ScanTarget.Remote("pc-042");

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private Task<Result<IReadOnlyList<InstalledSoftwareEntry>>> ReadAsync() =>
        new RemoteInstalledSoftwareReader(_wmiQueryService).ReadAsync(
            Target, ScanCredentials.CurrentUser, ConnectionOptions.Default, CancellationToken.None);

    private void SetUpMethod(
        string methodName,
        Func<IReadOnlyDictionary<string, object?>, Result<WmiInstance>> respond) =>
        _wmiQueryService
            .InvokeMethodAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Is<string>(ns => ns.Contains("default", StringComparison.OrdinalIgnoreCase)),
                Arg.Is("StdRegProv"),
                Arg.Is(methodName),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => respond(callInfo.Arg<IReadOnlyDictionary<string, object?>>()));

    private static Result<WmiInstance> Bag(params (string Name, object? Value)[] properties) =>
        Result.Success(new WmiInstance(properties.ToDictionary(
            property => property.Name, property => property.Value)));

    [Fact]
    public async Task ReadsEntriesFromBothViews_SkipsNamelessAndDeduplicates()
    {
        SetUpMethod("EnumKey", parameters => Bag(
            ("ReturnValue", 0u),
            ("sNames", new[] { "App1", "NoName" })));
        SetUpMethod("GetStringValue", parameters =>
        {
            string subKey = (string)parameters["sSubKeyName"]!;
            string valueName = (string)parameters["sValueName"]!;
            if (subKey.EndsWith(@"\NoName", StringComparison.Ordinal))
            {
                return Bag(("ReturnValue", 1u));
            }

            return valueName switch
            {
                "DisplayName" => Bag(("ReturnValue", 0u), ("sValue", "Remote App")),
                "DisplayVersion" => Bag(("ReturnValue", 0u), ("sValue", "2.0")),
                "Publisher" => Bag(("ReturnValue", 0u), ("sValue", "Contoso")),
                _ => Bag(("ReturnValue", 1u)),
            };
        });

        Result<IReadOnlyList<InstalledSoftwareEntry>> result = await ReadAsync();

        Assert.True(result.IsSuccess);
        // Both uninstall views return the same App1 → deduplicated by name+version
        InstalledSoftwareEntry entry = Assert.Single(result.Value);
        Assert.Equal("Remote App", entry.Name);
        Assert.Equal("2.0", entry.Version);
        Assert.Equal("Contoso", entry.Publisher);
    }

    [Fact]
    public async Task AccessDeniedRegistryStatus_MapsToStructuredAccessDeniedError()
    {
        SetUpMethod("EnumKey", _ => Bag(("ReturnValue", 5u)));

        Result<IReadOnlyList<InstalledSoftwareEntry>> result = await ReadAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Contains("pc-042", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingUninstallKeysEverywhere_MapsToNotFound()
    {
        SetUpMethod("EnumKey", _ => Bag(("ReturnValue", 2u)));

        Result<IReadOnlyList<InstalledSoftwareEntry>> result = await ReadAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task TransportFailure_PropagatesTypedError()
    {
        SetUpMethod("EnumKey", _ => Result.Failure<WmiInstance>(new Error(
            ErrorCode.WinRmUnavailable, "WinRM on 'pc-042' is not reachable.")));

        Result<IReadOnlyList<InstalledSoftwareEntry>> result = await ReadAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WinRmUnavailable, result.Error!.Code);
    }
}
