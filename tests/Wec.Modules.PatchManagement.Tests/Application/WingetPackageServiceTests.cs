using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class WingetPackageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
    private const string Depot = "depot-test.example.test";

    [Fact]
    public async Task CheckUpdates_UsesDailyCacheUntilAForcedRefresh()
    {
        var stored = Managed("7zip", "7zip.7zip", "25.01", Now.AddHours(-1));
        var repository = new MemoryWingetRepository(stored);
        IWingetCatalogClient catalog = Substitute.For<IWingetCatalogClient>();
        catalog.GetExactAsync("7zip.7zip", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(Eligible("7zip.7zip", "26.02"))));
        IOpsiClient opsi = Substitute.For<IOpsiClient>();
        opsi.GetProductsOnDepotsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<OpsiProductOnDepot>>(
                [new("7zip", Depot, "25.01", "1")])));
        WingetPackageService service = CreateService(repository, catalog, opsi);

        Result<WingetUpdateCheckResult> cached = await service.CheckUpdatesAsync(null, false, CancellationToken.None);
        Assert.True(cached.IsSuccess);
        Assert.Equal(0, cached.Value.CheckedCount);
        await catalog.DidNotReceive().GetExactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        Result<WingetUpdateCheckResult> refreshed = await service.CheckUpdatesAsync(null, true, CancellationToken.None);
        Assert.True(refreshed.IsSuccess);
        Assert.Equal(1, refreshed.Value.CheckedCount);
        Assert.Equal(1, refreshed.Value.UpdateCount);
        Assert.Equal("26.02", repository.Items.Single().LatestWingetVersion);
        Assert.Equal("SUCCESS", repository.Items.Single().CheckStatus);
    }

    [Fact]
    public async Task Preview_AdoptsExistingProductIdAndStartsNewProductVersionAtPackageOne()
    {
        var repository = new MemoryWingetRepository();
        IWingetCatalogClient catalog = Substitute.For<IWingetCatalogClient>();
        catalog.GetExactAsync("7zip.7zip", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(Eligible("7zip.7zip", "26.02"))));
        catalog.CompareVersionsAsync("7zip.7zip", "26.02", "25.01", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(1)));
        IOpsiClient opsi = OpsiForPreview(
            [new("7zip", "7-Zip", "25.01", "1", null)],
            [new("7zip", Depot, "25.01", "1")]);
        WingetPackageService service = CreateService(repository, catalog, opsi);

        Result<WingetPackagePreview> result = await service.PreviewAsync(
            "7zip", "7-Zip", "7zip.7zip", Depot, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.AdoptsExistingProduct);
        Assert.Equal(1, result.Value.PackageVersion);
        Assert.Equal("26.02-1", result.Value.TargetDepotVersion);
        Assert.Contains("Existing clients are not changed", result.Value.ConfirmationText);
    }

    [Fact]
    public async Task Preview_RejectsDowngradeAndManagedIdentityConflicts()
    {
        var repository = new MemoryWingetRepository(Managed("7zip", "7zip.7zip", "26.02", Now));
        IWingetCatalogClient catalog = Substitute.For<IWingetCatalogClient>();
        IOpsiClient opsi = OpsiForPreview(
            [new("7zip", "7-Zip", "26.02", "1", null)],
            [new("7zip", Depot, "26.02", "1")]);
        WingetPackageService service = CreateService(repository, catalog, opsi);

        Result<WingetPackagePreview> conflict = await service.PreviewAsync(
            "7zip", "7-Zip", "Microsoft.PowerToys", Depot, CancellationToken.None);
        Assert.True(conflict.IsFailure);
        Assert.Contains("already managed", conflict.Error!.Message);
        await catalog.DidNotReceive().GetExactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        repository.Items.Clear();
        catalog.GetExactAsync("7zip.7zip", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(Eligible("7zip.7zip", "25.01"))));
        catalog.CompareVersionsAsync("7zip.7zip", "25.01", "26.02", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(-1)));
        Result<WingetPackagePreview> downgrade = await service.PreviewAsync(
            "7zip", "7-Zip", "7zip.7zip", Depot, CancellationToken.None);
        Assert.True(downgrade.IsFailure);
        Assert.Contains("Downgrades are not allowed", downgrade.Error!.Message);
    }

    [Fact]
    public async Task ApplyUpdates_ContinuesAfterOnePackageUploadFails()
    {
        var repository = new MemoryWingetRepository(
            Managed("7zip", "7zip.7zip", "25.01", Now, "26.02"),
            Managed("powertoys", "Microsoft.PowerToys", "0.94.0", Now, "0.95.0"));
        IWingetCatalogClient catalog = Substitute.For<IWingetCatalogClient>();
        catalog.GetExactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            string id = call.ArgAt<string>(0);
            return Task.FromResult(Result.Success(id == "7zip.7zip"
                ? Eligible(id, "26.02")
                : Eligible(id, "0.95.0")));
        });
        catalog.CompareVersionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(1)));

        IOpsiClient opsi = Substitute.For<IOpsiClient>();
        opsi.GetDepotsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<OpsiDepot>>([new(Depot, "Test", true)])));
        opsi.GetProductsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<OpsiProduct>>(
                [new("7zip", "7-Zip", "25.01", "1", null), new("powertoys", "PowerToys", "0.94.0", "1", null)])));
        IReadOnlyList<OpsiProductOnDepot> oldProducts =
            [new("7zip", Depot, "25.01", "1"), new("powertoys", Depot, "0.94.0", "1")];
        IReadOnlyList<OpsiProductOnDepot> verifiedProducts =
            [new("7zip", Depot, "25.01", "1"), new("powertoys", Depot, "0.95.0", "1")];
        opsi.GetProductsOnDepotsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(Result.Success(oldProducts)),
                Task.FromResult(Result.Success(oldProducts)),
                Task.FromResult(Result.Success(verifiedProducts)));

        IRemoteFileUploader uploader = Substitute.For<IRemoteFileUploader>();
        uploader.UploadAsync(Arg.Any<RemoteFileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(Result.Failure<RemoteFileUploadResult>(new Error(
                    ErrorCode.RemoteCommandFailed, "simulated upload failure"))),
                Task.FromResult(Result.Success(new RemoteFileUploadResult("/tmp/wec-winget-test.tar.gz", 1, "hash"))));
        IRemoteCommandExecutor commands = Substitute.For<IRemoteCommandExecutor>();
        commands.ExecuteAsync(Arg.Any<RemoteCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RemoteCommandResult(0, "ok", "", TimeSpan.Zero))));
        WingetPackageService service = CreateService(repository, catalog, opsi, uploader, commands);

        Result<WingetUpdateOutcome> result = await service.ApplyUpdatesAsync(
            [new("7zip", "26.02"), new("powertoys", "0.95.0")],
            true,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value.SucceededCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.False(result.Value.Packages.Single(item => item.OpsiProductId == "7zip").Success);
        Assert.True(result.Value.Packages.Single(item => item.OpsiProductId == "powertoys").Success);
        Assert.Equal("0.95.0", repository.Items.Single(item => item.OpsiProductId == "powertoys").LastPackagedWingetVersion);
        await commands.Received(1).ExecuteAsync(Arg.Any<RemoteCommandRequest>(), Arg.Any<CancellationToken>());
        await uploader.Received(2).UploadAsync(
            Arg.Is<RemoteFileUploadRequest>(request => request.UserName == "test"),
            Arg.Any<CancellationToken>());
        await commands.Received(1).ExecuteAsync(
            Arg.Is<RemoteCommandRequest>(request => request.UserName == "test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RemoteBuildCommand_CleansTemporaryDataAndRestoresThePreviousWorkbenchOnFailure()
    {
        WingetPackageService service = CreateService(
            new MemoryWingetRepository(),
            Substitute.For<IWingetCatalogClient>(),
            Substitute.For<IOpsiClient>());
        var preview = new WingetPackagePreview(
            "7zip", "7-Zip", "7zip.7zip", "26.02", "winget", "machine", "Msi", "X64",
            Depot, "25.01-1", "26.02-1", 1, true,
            "/var/lib/opsi/workbench/packages/wec-winget/7zip", [], "confirm", Now);

        string command = service.BuildRemoteBuildCommand(
            preview,
            "/tmp/wec-winget-0123456789abcdef0123456789abcdef.tar.gz");

        Assert.Contains("trap", command);
        Assert.Contains("cleanup_temp", command);
        Assert.Contains("restore_previous", command);
        Assert.Contains("opsi-makepackage --no-zsync --no-md5", command);
        Assert.Contains("opsi-package-manager --quiet -i", command);
        Assert.Contains("/var/lib/opsi/workbench/packages/wec-winget/7zip", command);
    }

    private static WingetPackageService CreateService(
        MemoryWingetRepository repository,
        IWingetCatalogClient catalog,
        IOpsiClient opsi,
        IRemoteFileUploader? uploader = null,
        IRemoteCommandExecutor? commands = null)
    {
        var state = new OpsiSessionState();
        state.Set(new OpsiSession(
            new OpsiConnection(new Uri("https://opsi.example.test:4447"), "test", "secret", true, TimeSpan.FromSeconds(5)),
            new OpsiServerInfo("4.3")));
        IServiceCredentialStore credentials = Substitute.For<IServiceCredentialStore>();
        var options = Options.Create(new PatchManagementOptions
        {
            WingetRequestTimeout = TimeSpan.FromSeconds(5),
            WingetCheckInterval = TimeSpan.FromDays(1),
            SshUserName = "",
            WingetWorkbenchRoot = "/var/lib/opsi/workbench/packages/wec-winget",
        });
        var connector = new OpsiSessionConnector(opsi, state, credentials, options);
        IPatchAuditRepository audit = Substitute.For<IPatchAuditRepository>();
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new WingetPackageService(
            catalog,
            repository,
            opsi,
            state,
            connector,
            uploader ?? Substitute.For<IRemoteFileUploader>(),
            commands ?? Substitute.For<IRemoteCommandExecutor>(),
            audit,
            clock,
            options);
    }

    private static IOpsiClient OpsiForPreview(
        IReadOnlyList<OpsiProduct> products,
        IReadOnlyList<OpsiProductOnDepot> depotProducts)
    {
        IOpsiClient opsi = Substitute.For<IOpsiClient>();
        opsi.GetDepotsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<OpsiDepot>>([new(Depot, "Test", true)])));
        opsi.GetProductsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(products)));
        opsi.GetProductsOnDepotsAsync(Arg.Any<OpsiConnection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(depotProducts)));
        return opsi;
    }

    private static WingetPackageInfo Eligible(string id, string version) => new(
        id,
        id == "7zip.7zip" ? "7-Zip" : "PowerToys",
        "Publisher",
        version,
        "winget",
        "Msi",
        "X64",
        "System",
        true,
        null);

    private static WingetManagedPackage Managed(
        string productId,
        string wingetId,
        string packagedVersion,
        DateTimeOffset checkedAt,
        string? latestVersion = null) => new(
        1,
        productId,
        wingetId,
        "winget",
        "machine",
        Depot,
        productId,
        packagedVersion,
        WingetOpsiPackageTemplate.Version,
        latestVersion ?? packagedVersion,
        "SUCCESS",
        checkedAt,
        null,
        Now.AddDays(-1),
        Now);

    private sealed class MemoryWingetRepository(params WingetManagedPackage[] packages)
        : IWingetManagedPackageRepository
    {
        public List<WingetManagedPackage> Items { get; } = [.. packages];

        public Task<IReadOnlyList<WingetManagedPackage>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WingetManagedPackage>>([.. Items]);

        public Task<WingetManagedPackage?> FindByProductIdAsync(
            string productId,
            CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(item =>
                string.Equals(item.OpsiProductId, productId, StringComparison.OrdinalIgnoreCase)));

        public Task UpsertAsync(WingetManagedPackage package, CancellationToken cancellationToken)
        {
            int index = Items.FindIndex(item =>
                string.Equals(item.OpsiProductId, package.OpsiProductId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                Items[index] = package;
            }
            else
            {
                Items.Add(package);
            }
            return Task.CompletedTask;
        }
    }
}
