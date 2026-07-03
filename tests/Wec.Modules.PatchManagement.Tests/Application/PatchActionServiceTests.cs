using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Opsi;
using Wec.Core.Results;
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

        _opsiClient.GetDepotsAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiDepot>>(
                [new("opsi.kauth.local", null, IsConfigServer: true)]));
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
                [new("firefox", "opsi.kauth.local", "128.0", "2")]));
        _opsiClient.GetProductStatesAsync(Connection, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<OpsiProductOnClient>>(
            [
                new("firefox", "pc1.kauth.local", "installed", "none", "successful", "127.0", "1", null),
                new("firefox", "pc2.kauth.local", "installed", "none", "successful", "128.0", "2", null),
            ]));

        var dashboardService = new PatchDashboardService(
            _opsiClient, _sessionState, softwareInventory, mappingRepository, clock);
        _service = new PatchActionService(
            _opsiClient, _sessionState, dashboardService, _auditRepository, clock);
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
    public async Task PlanPackageUpdate_BuildsCommandAndWritesPlannedAudit()
    {
        Result<PreparePackagesPlan> plan = await _service.PlanPackageUpdateAsync(
            ["firefox", "7zip"], CancellationToken.None);

        Assert.True(plan.IsSuccess);
        Assert.Equal("opsi-package-updater -v update 7zip firefox", plan.Value.Command);
        Assert.Contains("opsi.kauth.local", plan.Value.Note, StringComparison.Ordinal);
        PatchAuditEntry entry = Assert.Single(_auditRepository.Entries);
        Assert.Equal(PatchActionService.PreparePackagesAction, entry.Action);
        Assert.Equal("PLANNED", entry.Result);
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
    }
}
