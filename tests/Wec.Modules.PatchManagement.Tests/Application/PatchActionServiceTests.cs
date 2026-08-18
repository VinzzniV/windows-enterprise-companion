using NSubstitute;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Modules.PatchManagement;
using Wec.Modules.PatchManagement.Application;
using Wec.Modules.PatchManagement.Domain;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Tests.Application;

public sealed class PatchActionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly OpsiConnection Connection = new(
        new Uri("https://opsi.kauth.local:4447"), "admin", "secret", false, TimeSpan.FromSeconds(5));

    private readonly IOpsiClient _opsiClient = Substitute.For<IOpsiClient>();
    private readonly OpsiSessionState _sessionState = new();
    private readonly InMemoryAuditRepository _auditRepository = new();
    private readonly IRemoteCommandExecutor _remoteCommandExecutor = Substitute.For<IRemoteCommandExecutor>();
    private readonly IRemoteArtifactStager _remoteArtifactStager = Substitute.For<IRemoteArtifactStager>();
    private readonly IProductVersionSourceRepository _versionSourceRepository =
        Substitute.For<IProductVersionSourceRepository>();
    private readonly PatchActionService _service;

    public PatchActionServiceTests()
    {
        _sessionState.Set(new OpsiSession(Connection, new OpsiServerInfo("4.3")));

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var softwareInventory = Substitute.For<IInstalledSoftwareInventoryProvider>();
        softwareInventory.GetAllHostsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        var mappingRepository = Substitute.For<IPatchMappingRepository>();
        mappingRepository.ListAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        _versionSourceRepository.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ProductVersionSource(
                "greenshot",
                "https://api.github.com/repos/greenshot/greenshot/releases/latest",
                "version ([0-9.]+)",
                true,
                "1.3.315",
                Now,
                "SUCCESS",
                null),
        ]);
        var patchOptions = new PatchManagementOptions
        {
            PackageAutomationProfiles = new Dictionary<string, PackageAutomationProfileOptions>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["greenshot"] = new()
                {
                    ReleaseUrl = "https://api.github.com/repos/greenshot/greenshot/releases/latest",
                    ArtifactUrlPattern = "artifact (https://example.test/v{version}/greenshot-{version}.exe)",
                    WorkbenchPath = "/var/lib/opsi/workbench/greenshot",
                    InstallerRelativePath = "CLIENT_DATA/Greenshot-Setup.exe",
                    PreviousInstallerFileNames = ["Greenshot-INSTALLER-1.2.10.6-RELEASE.exe"],
                },
            },
        };
        var manufacturerVersionService = new ManufacturerVersionService(
            Substitute.For<IVendorVersionClient>(),
            _versionSourceRepository,
            _auditRepository,
            clock,
            Options.Create(patchOptions));

        IServiceCredentialStore credentials = Substitute.For<IServiceCredentialStore>();
        credentials.Read(ServiceCredentialKind.Opsi)
            .Returns(Result.Success<StoredServiceCredential?>(null));
        var sessionConnector = new OpsiSessionConnector(
            _opsiClient,
            _sessionState,
            credentials,
            Options.Create(patchOptions));

        _opsiClient.GetDepotsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiDepot>>(
            [
                new("opsi.kauth.local", null, IsConfigServer: true),
                new("test.kauth.local", "Test depot", IsConfigServer: false),
            ]));
        _opsiClient.GetClientsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiClientHost>>(
            [
                new("pc1.kauth.local", null, null, null),
                new("pc2.kauth.local", null, null, null),
            ]));
        _opsiClient.GetProductsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProduct>>(
                [new("firefox", "Mozilla Firefox", "128.0", "2", null)]));
        _opsiClient.GetProductsOnDepotsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnDepot>>(
            [
                new("firefox", "opsi.kauth.local", "128.0", "2"),
                new("firefox", "test.kauth.local", "127.0", "1"),
            ]));
        _opsiClient.GetProductStatesAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnClient>>(
            [
                new("firefox", "pc1.kauth.local", "installed", "none", "successful", "127.0", "1", null),
                new("firefox", "pc2.kauth.local", "installed", "none", "successful", "128.0", "2", null),
            ]));

        var dashboardService = new PatchDashboardService(
            _opsiClient,
            _sessionState,
            sessionConnector,
            softwareInventory,
            mappingRepository,
            _versionSourceRepository,
            manufacturerVersionService,
            _auditRepository,
            clock,
            Options.Create(patchOptions));
        _service = new PatchActionService(
            _opsiClient,
            _sessionState,
            sessionConnector,
            dashboardService,
            _auditRepository,
            _remoteCommandExecutor,
            _remoteArtifactStager,
            _versionSourceRepository,
            clock,
            Options.Create(patchOptions));
    }

    [Fact]
    public async Task Preview_DefaultTargets_AreTheOutdatedClients()
    {
        Result<RolloutPreview> preview =
            await _service.BuildRolloutPreviewAsync("firefox", null, null, CancellationToken.None);

        Assert.True(preview.IsSuccess);
        RolloutPreviewClient target = Assert.Single(preview.Value.Clients);
        Assert.Equal("pc1.kauth.local", target.ClientId);
        Assert.Equal("127.0-1", target.InstalledVersion);
        Assert.Equal("128.0-2", target.TargetVersion);
        Assert.Equal(PatchWorkflowState.UpdateAvailable, target.CurrentState);
        Assert.Equal("setup", preview.Value.PlannedAction);
    }

    [Fact]
    public async Task Preview_UnknownClient_IsRejected()
    {
        Result<RolloutPreview> preview = await _service.BuildRolloutPreviewAsync(
            "firefox", null, ["ghost.kauth.local"], CancellationToken.None);

        Assert.True(preview.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, preview.Error!.Code);
        Assert.Contains("ghost.kauth.local", preview.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestRollout_WithoutConfirmation_DoesNothing()
    {
        Result<RolloutRequestOutcome> outcome = await _service.RequestRolloutAsync(
            "firefox", ["pc1.kauth.local"], null, confirmed: false, CancellationToken.None);

        Assert.True(outcome.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, outcome.Error!.Code);
        Assert.Empty(_auditRepository.Entries);
        await _opsiClient.DidNotReceiveWithAnyArgs()
            .RequestSetupAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RequestRollout_Confirmed_CallsOpsiAndWritesSuccessAudit()
    {
        _opsiClient.RequestSetupAsync(
                Connection, "firefox", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(1));

        Result<RolloutRequestOutcome> outcome = await _service.RequestRolloutAsync(
            "firefox", ["pc1.kauth.local"], null, confirmed: true, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, outcome.Value.RequestedClientCount);
        PatchAuditEntry entry = Assert.Single(_auditRepository.Entries);
        Assert.Equal(PatchActionService.RolloutAction, entry.Action);
        Assert.Equal("SUCCESS", entry.Result);
        Assert.Equal("firefox", entry.ProductId);
        Assert.Equal(["pc1.kauth.local"], entry.TargetClients);
        Assert.Contains("128.0-2", entry.PreviewJson, StringComparison.Ordinal);
        Assert.Equal(Now, entry.TimestampUtc);
    }

    [Fact]
    public async Task RequestRollout_OpsiFailure_WritesFailedAuditAndPropagatesError()
    {
        _opsiClient.RequestSetupAsync(
                Connection, "firefox", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>(new Error(ErrorCode.AccessDenied, "opsi denied access.")));

        Result<RolloutRequestOutcome> outcome = await _service.RequestRolloutAsync(
            "firefox", ["pc1.kauth.local"], null, confirmed: true, CancellationToken.None);

        Assert.True(outcome.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, outcome.Error!.Code);
        PatchAuditEntry entry = Assert.Single(_auditRepository.Entries);
        Assert.Equal("FAILED", entry.Result);
        Assert.Equal("opsi denied access.", entry.ErrorMessage);
    }

    [Fact]
    public async Task PackageUpdatePreview_BuildsTestDepotCommandAndWritesPlannedAudit()
    {
        Result<PreparePackagesPlan> plan = await _service.BuildPackageUpdatePreviewAsync(
            "firefox", PatchActionService.TestStage, ["test.kauth.local"], CancellationToken.None);

        Assert.True(plan.IsSuccess);
        PackageUpdateTarget target = Assert.Single(plan.Value.Targets);
        Assert.Equal("opsi-package-updater -v update firefox", target.Command);
        Assert.Equal("test.kauth.local", target.DepotId);
        Assert.Equal("127.0-1", target.CurrentVersion);
        PatchAuditEntry entry = Assert.Single(_auditRepository.Entries);
        Assert.Equal(PatchActionService.PreparePackagesAction, entry.Action);
        Assert.Equal("PLANNED", entry.Result);
    }

    [Fact]
    public async Task PackageUpdatePreview_WithAutomationProfile_BuildsManufacturerArtifactPlan()
    {
        _opsiClient.GetProductsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProduct>>(
                [new("greenshot", "Greenshot", "1.2.10.6", "1", null)]));
        _opsiClient.GetProductsOnDepotsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnDepot>>(
            [
                new("greenshot", "opsi.kauth.local", "1.2.10.6", "1"),
                new("greenshot", "test.kauth.local", "1.2.10.6", "1"),
            ]));

        Result<PreparePackagesPlan> plan = await _service.BuildPackageUpdatePreviewAsync(
            "greenshot", PatchActionService.TestStage, ["test.kauth.local"], CancellationToken.None);

        Assert.True(plan.IsSuccess);
        Assert.Equal("CUSTOM_BUILD", plan.Value.Mode);
        Assert.Equal("1.3.315", plan.Value.ArtifactVersion);
        PackageUpdateTarget target = Assert.Single(plan.Value.Targets);
        Assert.Contains("Greenshot-Setup.exe", target.Command, StringComparison.Ordinal);
        Assert.Contains("Greenshot-INSTALLER-1\\.2\\.10\\.6-RELEASE\\.exe", target.Command, StringComparison.Ordinal);
        Assert.Contains("opsi-makepackage", target.Command, StringComparison.Ordinal);
        Assert.Contains("opsi-package-manager", target.Command, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', target.Command);
    }

    [Fact]
    public void AutomationProfile_RejectsParentDirectoryTraversal()
    {
        var profile = new PackageAutomationProfileOptions
        {
            ReleaseUrl = "https://example.test/releases/latest",
            ArtifactUrlPattern = "(https://example.test/setup.exe)",
            WorkbenchPath = "/var/lib/opsi/workbench/greenshot",
            InstallerRelativePath = "../outside.exe",
        };

        Result<bool> validation = PatchActionService.ValidateAutomationProfile("greenshot", profile);

        Assert.True(validation.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, validation.Error!.Code);
    }

    [Fact]
    public void AutomationProfile_RejectsArtifactPatternWithoutVersionBinding()
    {
        var profile = new PackageAutomationProfileOptions
        {
            ReleaseUrl = "https://example.test/releases/latest",
            ArtifactUrlPattern = "(https://example.test/setup.exe)",
            WorkbenchPath = "/var/lib/opsi/workbench/greenshot",
            InstallerRelativePath = "CLIENT_DATA/Greenshot-Setup.exe",
        };

        Result<bool> validation = PatchActionService.ValidateAutomationProfile("greenshot", profile);

        Assert.True(validation.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, validation.Error!.Code);
    }

    [Fact]
    public async Task ExecutePackageUpdate_WithoutConfirmation_DoesNotRunSsh()
    {
        Result<PackageUpdateOutcome> outcome = await _service.ExecutePackageUpdateAsync(
            "firefox",
            PatchActionService.TestStage,
            ["test.kauth.local"],
            confirmed: false,
            CancellationToken.None);

        Assert.True(outcome.IsFailure);
        await _remoteCommandExecutor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteTestUpdate_VerifiesVersionAndWritesVersionedAudit()
    {
        _remoteCommandExecutor.ExecuteAsync(
                Arg.Any<RemoteCommandRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RemoteCommandResult(
                0, "updated firefox", string.Empty, TimeSpan.FromSeconds(3))));
        _opsiClient.GetProductsOnDepotsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<OpsiProductOnDepot>>(
                [
                    new("firefox", "opsi.kauth.local", "128.0", "2"),
                    new("firefox", "test.kauth.local", "127.0", "1"),
                ]),
                Result.Success<IReadOnlyList<OpsiProductOnDepot>>(
                [
                    new("firefox", "opsi.kauth.local", "128.0", "2"),
                    new("firefox", "test.kauth.local", "129.0", "1"),
                ]));

        Result<PackageUpdateOutcome> outcome = await _service.ExecutePackageUpdateAsync(
            "firefox",
            PatchActionService.TestStage,
            ["test.kauth.local"],
            confirmed: true,
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, outcome.Value.SucceededTargetCount);
        PatchUpdateAuditShouldContain("127.0-1", "129.0-1", "SUCCESS");
        await _remoteCommandExecutor.Received(1).ExecuteAsync(
            Arg.Is<RemoteCommandRequest>(request =>
                request.Host == "test.kauth.local"
                && request.UserName == "root"
                && request.Command == "opsi-package-updater -v update firefox"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Synchronization_IsLockedUntilSuccessfulTestIsApproved()
    {
        Result<PreparePackagesPlan> locked = await _service.BuildPackageUpdatePreviewAsync(
            "firefox",
            PatchActionService.DepotSynchronizationStage,
            ["opsi.kauth.local"],
            CancellationToken.None);

        Assert.True(locked.IsFailure);
        Assert.Contains("locked", locked.Error!.Message, StringComparison.OrdinalIgnoreCase);

        _auditRepository.Entries.Add(new PatchAuditEntry(
            0,
            Now,
            "admin",
            PatchActionService.TestPackageUpdateAction,
            "firefox",
            "test.kauth.local",
            [],
            null,
            "SUCCESS",
            null,
            "127.0-1",
            "129.0-1"));
        _opsiClient.GetClientsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiClientHost>>(
                [new("pilot.kauth.local", null, "test.kauth.local", null)]));
        _opsiClient.GetProductStatesAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnClient>>(
            [
                new(
                    "firefox",
                    "pilot.kauth.local",
                    "installed",
                    "none",
                    "successful",
                    "129.0",
                    "1",
                    null),
            ]));
        Result<PackageWorkflowStatus> approval = await _service.ApprovePackagePilotAsync(
            "firefox", confirmed: true, CancellationToken.None);
        Result<PreparePackagesPlan> unlocked = await _service.BuildPackageUpdatePreviewAsync(
            "firefox",
            PatchActionService.DepotSynchronizationStage,
            ["opsi.kauth.local"],
            CancellationToken.None);

        Assert.True(approval.IsSuccess);
        Assert.True(approval.Value.PilotApproved);
        Assert.True(unlocked.IsSuccess);
        Assert.Equal("opsi.kauth.local", Assert.Single(unlocked.Value.Targets).DepotId);
    }

    [Fact]
    public async Task CustomPackageSynchronization_PromotesTheApprovedTestArtifact()
    {
        _opsiClient.GetProductsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProduct>>(
                [new("greenshot", "Greenshot", "1.3.315", "1", null)]));
        _opsiClient.GetProductsOnDepotsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnDepot>>(
            [
                new("greenshot", "opsi.kauth.local", "1.2.10.6", "1"),
                new("greenshot", "test.kauth.local", "1.3.315", "1"),
            ]));
        _auditRepository.Entries.Add(new PatchAuditEntry(
            0, Now.AddMinutes(-1), "admin", PatchActionService.TestPackageUpdateAction,
            "greenshot", "test.kauth.local", [], null, "SUCCESS", null,
            "1.2.10.6-1", "1.3.315-1"));
        _auditRepository.Entries.Add(new PatchAuditEntry(
            0, Now, "admin", PatchActionService.PilotApprovedAction,
            "greenshot", "test.kauth.local", ["pilot.kauth.local"], null, "SUCCESS", null,
            "1.2.10.6-1", "1.3.315-1"));

        Result<PreparePackagesPlan> plan = await _service.BuildPackageUpdatePreviewAsync(
            "greenshot",
            PatchActionService.DepotSynchronizationStage,
            ["opsi.kauth.local"],
            CancellationToken.None);

        Assert.True(plan.IsSuccess);
        Assert.Equal("CUSTOM_PROMOTION", plan.Value.Mode);
        Assert.Equal("1.3.315-1", plan.Value.ArtifactVersion);
        Assert.Contains("wec-approved-greenshot.opsi", Assert.Single(plan.Value.Targets).Command);
        Assert.DoesNotContain("opsi-package-updater", plan.Value.Targets[0].Command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedLatestTest_InvalidatesEarlierApproval()
    {
        _auditRepository.Entries.Add(new PatchAuditEntry(
            0, Now.AddMinutes(-2), "admin", PatchActionService.TestPackageUpdateAction,
            "firefox", "test.kauth.local", [], null, "SUCCESS", null));
        _auditRepository.Entries.Add(new PatchAuditEntry(
            0, Now.AddMinutes(-1), "admin", PatchActionService.PilotApprovedAction,
            "firefox", "test.kauth.local", [], null, "SUCCESS", null));
        _auditRepository.Entries.Add(new PatchAuditEntry(
            0, Now, "admin", PatchActionService.TestPackageUpdateAction,
            "firefox", "test.kauth.local", [], null, "FAILED", "updater failed"));

        PackageWorkflowStatus status = await _service.GetPackageWorkflowStatusAsync(
            "firefox", CancellationToken.None);

        Assert.False(status.PilotApproved);
        Assert.Equal("updater failed", status.LastError);
    }

    [Fact]
    public async Task PilotApproval_RejectsWhenNoClientReportsTheTestedVersion()
    {
        _auditRepository.Entries.Add(new PatchAuditEntry(
            0,
            Now,
            "admin",
            PatchActionService.TestPackageUpdateAction,
            "firefox",
            "test.kauth.local",
            [],
            null,
            "SUCCESS",
            null,
            "127.0-1",
            "129.0-1"));

        Result<PackageWorkflowStatus> approval = await _service.ApprovePackagePilotAsync(
            "firefox", confirmed: true, CancellationToken.None);

        Assert.True(approval.IsFailure);
        Assert.Contains("No successfully updated pilot client", approval.Error!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            _auditRepository.Entries,
            entry => entry.Action == PatchActionService.PilotApprovedAction);
    }

    [Fact]
    public async Task WithoutSession_ActionsFailWithConnectHint()
    {
        _sessionState.Clear();

        Result<RolloutRequestOutcome> outcome = await _service.RequestRolloutAsync(
            "firefox", ["pc1.kauth.local"], null, confirmed: true, CancellationToken.None);

        Assert.True(outcome.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, outcome.Error!.Code);
        Assert.Contains("Not connected", outcome.Error.Message, StringComparison.Ordinal);
    }

    private sealed class InMemoryAuditRepository : IPatchAuditRepository
    {
        public List<PatchAuditEntry> Entries { get; } = [];

        public Task AddAsync(PatchAuditEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PatchAuditEntry>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PatchAuditEntry>>(
                [.. Entries.OrderByDescending(entry => entry.TimestampUtc).Take(limit)]);

        public Task<PatchAuditEntry?> FindLatestAsync(
            string productId,
            string action,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entries
                .Where(entry => string.Equals(entry.ProductId, productId, StringComparison.OrdinalIgnoreCase)
                    && entry.Action == action)
                .OrderByDescending(entry => entry.TimestampUtc)
                .FirstOrDefault());
    }

    private void PatchUpdateAuditShouldContain(string oldVersion, string newVersion, string result)
    {
        PatchAuditEntry entry = Assert.Single(
            _auditRepository.Entries,
            audit => audit.Action == PatchActionService.TestPackageUpdateAction);
        Assert.Equal(oldVersion, entry.OldVersion);
        Assert.Equal(newVersion, entry.NewVersion);
        Assert.Equal(result, entry.Result);
    }
}
