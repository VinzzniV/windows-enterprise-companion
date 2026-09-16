using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.ActionCenter.Application;

namespace Wec.Modules.ActionCenter.Tests;

public sealed class ActionCenterServiceTests
{
    [Fact]
    public async Task SourceEvidenceLinksKeepCleanupKeysAndNessusKeysAndNeverExecuteAmbiguousTargets()
    {
        ConfigureEvidence();
        _hygiene.LoadAsync(Arg.Any<HygieneActionEvidenceQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new HygieneActionEvidenceSnapshot(AssessedAt, [], [],
            [
                new("evidence:one", "pc.example", "StaleAd", "Warning", "Old", "Active Directory", null, ActionEvidenceAvailability.Partial, "Ambiguous") { CanTargetWindows = false },
                new("evidence:two", "pc.example", "OutdatedAgent", "Warning", "Old", "Kaspersky", null, ActionEvidenceAvailability.Partial, "Ambiguous") { CanTargetWindows = false },
                new("evidence:three", "pc.example", "NessusHighVulnerabilities", "Warning", "High", "Nessus", null, ActionEvidenceAvailability.Partial, "Candidate") { NessusSourceKey = "PC.EXAMPLE|HOST:3" },
            ])));
        Result<ActionCenterPage> result = await CreateService().GetPageAsync(new(PageSize: 10), CancellationToken.None);
        Assert.Contains(result.Value.Items, item => item.Href == "/cleanup?host=evidence%3Aone");
        Assert.Contains(result.Value.Items, item => item.Href == "/devices?q=pc.example");
        Assert.Contains(result.Value.Items, item => item.Href == "/vulnerabilities?tab=findings&asset=PC.EXAMPLE%7CHOST%3A3");
    }

    private static readonly DateTimeOffset AssessedAt =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private readonly IHygieneActionEvidenceProvider _hygiene = Substitute.For<IHygieneActionEvidenceProvider>();
    private readonly IInventoryClientSnapshotProvider _inventory = Substitute.For<IInventoryClientSnapshotProvider>();
    private readonly ISecurityActionEvidenceProvider _security = Substitute.For<ISecurityActionEvidenceProvider>();

    [Fact]
    public async Task GetPageAsync_ComputesPrioritizedReadOnlyItemsAndSourceCoverage()
    {
        ConfigureEvidence();
        ActionCenterService service = CreateService();
        var directory = new HygieneActionDirectoryConnection(Server: "dc.corp.example");

        Result<ActionCenterPage> result = await service.GetPageAsync(
            new ListActionCenterItemsRequest(
                ActiveDirectory: directory,
                Force: true,
                PageSize: 10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Total);
        Assert.Equal(2, result.Value.Summary.Critical);
        Assert.Equal("hygiene:PC-01:NessusCriticalVulnerabilities", result.Value.Items[0].Id);
        Assert.Equal("/vulnerabilities?tab=findings&asset=PC-01", result.Value.Items[0].Href);
        Assert.Contains(result.Value.Items, item =>
            item.Id == "security:PC-SEC:secure-boot"
            && item.Href == "/clients/PC-SEC?section=security");
        Assert.Contains(result.Value.Items, item =>
            item.Id == "hygiene:PC-02:StaleAd"
            && item.Href == "/cleanup?host=PC-02");
        Assert.Contains(result.Value.Items, item =>
            item.Id == "inventory:PC-OLD:stale"
            && item.EvidenceAgeDays == 31
            && item.Href == "/cleanup?host=PC-OLD");
        Assert.True(result.Value.ItemsTruncated);
        Assert.Equal(ActionEvidenceAvailability.Truncated,
            result.Value.Sources.Single(source => source.Source == "WEC Security").Availability);
        await _hygiene.Received(1).LoadAsync(
            Arg.Is<HygieneActionEvidenceQuery>(query => query.ActiveDirectory == directory && query.Force),
            Arg.Any<CancellationToken>());
        await _security.Received(1).LoadStoredAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPageAsync_AppliesSearchFilterSortAndPagingAfterComputation()
    {
        ConfigureEvidence();
        ActionCenterService service = CreateService();

        Result<ActionCenterPage> result = await service.GetPageAsync(
            new ListActionCenterItemsRequest(
                Search: "inventory",
                Severity: ActionCenterSeverity.Warning,
                Source: "WEC Inventory",
                Page: 1,
                PageSize: 1,
                SortField: ActionCenterSortField.EvidenceAge,
                SortDirection: ActionCenterSortDirection.Descending),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        ActionCenterWorkItem item = Assert.Single(result.Value.Items);
        Assert.Equal("PC-OLD", item.Device);
        Assert.Equal(1, result.Value.Total);
        Assert.Equal(1, result.Value.Summary.Warning);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetPageAsync_RejectsInvalidPagingWithoutReadingSources(int page, int pageSize)
    {
        Result<ActionCenterPage> result = await CreateService().GetPageAsync(
            new ListActionCenterItemsRequest(Page: page, PageSize: pageSize),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        await _hygiene.DidNotReceive().LoadAsync(Arg.Any<HygieneActionEvidenceQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPageAsync_PropagatesHygieneFailureWithoutInventingHealthyCoverage()
    {
        _hygiene.LoadAsync(Arg.Any<HygieneActionEvidenceQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<HygieneActionEvidenceSnapshot>(new Error(
                ErrorCode.AuthenticationFailed, "Directory authentication failed.")));
        _inventory.ListHostsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _security.LoadStoredAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityActionEvidenceSnapshot([], 0, false));

        Result<ActionCenterPage> result = await CreateService().GetPageAsync(
            new ListActionCenterItemsRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AuthenticationFailed, result.Error!.Code);
    }

    private void ConfigureEvidence()
    {
        _hygiene.LoadAsync(Arg.Any<HygieneActionEvidenceQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new HygieneActionEvidenceSnapshot(
                AssessedAt,
                [new ActionEvidenceSourceState("Nessus", ActionEvidenceAvailability.Available, "Available")],
                [new HygieneActionSubject("PC-01", "PC-01")],
                [
                    new HygieneActionEvidence(
                        "PC-01", "PC-01", "NessusCriticalVulnerabilities", "Critical",
                        "Nessus reports one critical finding.", "Nessus", AssessedAt.AddDays(-2),
                        ActionEvidenceAvailability.Available, "Nessus inventory was available."),
                    new HygieneActionEvidence(
                        "PC-02", "PC-02", "StaleAd", "Critical",
                        "Active Directory last logon was 100 days ago.", "Active Directory", AssessedAt.AddDays(-100),
                        ActionEvidenceAvailability.Partial, "Directory result was partial."),
                ])));
        _inventory.ListHostsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new InventoryClientSnapshotHost("PC-OLD", AssessedAt.AddDays(-31)),
            new InventoryClientSnapshotHost("PC-FRESH", AssessedAt.AddDays(-2)),
        ]);
        _security.LoadStoredAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            new SecurityActionEvidenceSnapshot(
            [
                new SecurityActionEvidence(
                    "PC-SEC", "PC-SEC", "secure-boot", "Secure Boot disabled", "Secure Boot is disabled.",
                    "Critical", "PlatformIntegrity", "Enable Secure Boot after validating compatibility.",
                    AssessedAt.AddDays(-1), AssessedAt.AddDays(-1), ActionEvidenceAvailability.Available,
                    "All checks completed."),
            ],
            1,
            true));
    }

    private ActionCenterService CreateService() => new(
        _hygiene,
        _inventory,
        _security,
        Options.Create(new ActionCenterOptions
        {
            MaximumSecurityScans = 20,
            MaximumComputedItems = 100,
            InventoryStaleWarningDays = 30,
            MaximumPageSize = 100,
        }));
}
