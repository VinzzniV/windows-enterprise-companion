using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Tests.Application;

public class InstalledSoftwareReaderTests
{
    private const string NativeUninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Wow64UninstallPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly IRegistryReader _registryReader = Substitute.For<IRegistryReader>();

    public InstalledSoftwareReaderTests()
    {
        _registryReader.ReadLocalMachineSubKeyNames(Arg.Any<string>())
            .Returns(Result.Success<IReadOnlyList<string>>([]));
        _registryReader.ReadLocalMachineValue(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Result.Success<object?>(null));
    }

    private void SetUpEntry(string uninstallPath, string subKey, string? displayName, string? version = null, string? publisher = null, int? systemComponent = null)
    {
        string entryPath = $@"{uninstallPath}\{subKey}";
        _registryReader.ReadLocalMachineValue(entryPath, "DisplayName").Returns(Result.Success<object?>(displayName));
        _registryReader.ReadLocalMachineValue(entryPath, "DisplayVersion").Returns(Result.Success<object?>(version));
        _registryReader.ReadLocalMachineValue(entryPath, "Publisher").Returns(Result.Success<object?>(publisher));
        _registryReader.ReadLocalMachineValue(entryPath, "SystemComponent")
            .Returns(Result.Success<object?>(systemComponent));
    }

    [Fact]
    public void ReadsBothBitnessViews_SkipsNamelessAndSystemComponents_SortsByName()
    {
        _registryReader.ReadLocalMachineSubKeyNames(NativeUninstallPath)
            .Returns(Result.Success<IReadOnlyList<string>>(["app-b", "nameless", "syscomp"]));
        _registryReader.ReadLocalMachineSubKeyNames(Wow64UninstallPath)
            .Returns(Result.Success<IReadOnlyList<string>>(["app-a"]));
        SetUpEntry(NativeUninstallPath, "app-b", "Beta App", "2.0", "Beta Corp");
        SetUpEntry(NativeUninstallPath, "nameless", displayName: null);
        SetUpEntry(NativeUninstallPath, "syscomp", "Hidden Component", systemComponent: 1);
        SetUpEntry(Wow64UninstallPath, "app-a", "Alpha App", "1.0", "Alpha Corp");

        IReadOnlyList<InstalledSoftwareEntry> entries =
            new InstalledSoftwareReader(_registryReader).ReadInstalledSoftware();

        Assert.Collection(
            entries,
            entry => Assert.Equal(new InstalledSoftwareEntry("Alpha App", "1.0", "Alpha Corp"), entry),
            entry => Assert.Equal(new InstalledSoftwareEntry("Beta App", "2.0", "Beta Corp"), entry));
    }

    [Fact]
    public void DuplicateEntriesAcrossViews_AreReturnedOnce()
    {
        _registryReader.ReadLocalMachineSubKeyNames(NativeUninstallPath)
            .Returns(Result.Success<IReadOnlyList<string>>(["app"]));
        _registryReader.ReadLocalMachineSubKeyNames(Wow64UninstallPath)
            .Returns(Result.Success<IReadOnlyList<string>>(["app"]));
        SetUpEntry(NativeUninstallPath, "app", "Same App", "1.0");
        SetUpEntry(Wow64UninstallPath, "app", "Same App", "1.0");

        IReadOnlyList<InstalledSoftwareEntry> entries =
            new InstalledSoftwareReader(_registryReader).ReadInstalledSoftware();

        Assert.Single(entries);
    }

    [Fact]
    public void UnreadableUninstallKey_YieldsEmptyListInsteadOfThrowing()
    {
        _registryReader.ReadLocalMachineSubKeyNames(Arg.Any<string>())
            .Returns(Result.Failure<IReadOnlyList<string>>(Error.AccessDenied(
                "denied", Wec.Core.Privileges.PrivilegeLevel.Administrator)));

        IReadOnlyList<InstalledSoftwareEntry> entries =
            new InstalledSoftwareReader(_registryReader).ReadInstalledSoftware();

        Assert.Empty(entries);
    }
}
