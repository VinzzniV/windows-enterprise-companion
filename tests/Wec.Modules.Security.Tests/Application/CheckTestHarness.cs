using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Application;

namespace Wec.Modules.Security.Tests.Application;

/// <summary>Shared substitutes and setup helpers for check tests.</summary>
internal sealed class CheckTestHarness
{
    public static readonly DateTimeOffset Now = new(2026, 7, 2, 16, 0, 0, TimeSpan.Zero);

    public static readonly SecurityScanContext LocalContext = new(
        ScanTarget.Local, ScanCredentials.CurrentUser, ConnectionOptions.Default);

    public static readonly SecurityScanContext RemoteContext = new(
        ScanTarget.Remote("pc-042.contoso.local"), ScanCredentials.CurrentUser, ConnectionOptions.Default);

    public IWmiQueryService WmiQueryService { get; } = Substitute.For<IWmiQueryService>();

    public IRegistryReader RegistryReader { get; } = Substitute.For<IRegistryReader>();

    public IClock Clock { get; }

    public CheckTestHarness()
    {
        Clock = Substitute.For<IClock>();
        Clock.UtcNow.Returns(Now);
    }

    public void SetUpWmiQuery(string classNameFragment, params WmiInstance[] instances) =>
        WmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(classNameFragment, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(instances));

    public void SetUpWmiFailure(string classNameFragment, Error error) =>
        WmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(classNameFragment, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(error));

    public void SetUpRegistryValue(string valueName, object? value) =>
        RegistryReader
            .ReadLocalMachineValue(Arg.Any<string>(), valueName)
            .Returns(Result.Success(value));

    public static WmiInstance Instance(params (string Name, object? Value)[] properties) =>
        new(properties.ToDictionary(property => property.Name, property => property.Value));
}
