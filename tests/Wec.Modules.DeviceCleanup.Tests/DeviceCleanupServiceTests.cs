using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.DeviceCleanup.Application;

namespace Wec.Modules.DeviceCleanup.Tests;

public sealed class DeviceCleanupServiceTests
{
    private static readonly DateTimeOffset AssessedAt =
        new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private readonly IDeviceCleanupEvidenceProvider _sourceEvidence =
        Substitute.For<IDeviceCleanupEvidenceProvider>();
    private readonly IInventoryClientSnapshotProvider _inventorySnapshots =
        Substitute.For<IInventoryClientSnapshotProvider>();
    private readonly IDeviceCleanupInventoryEvidenceProvider _inventoryEvidence =
        Substitute.For<IDeviceCleanupInventoryEvidenceProvider>();

    public DeviceCleanupServiceTests()
    {
        _sourceEvidence.LoadAsync(Arg.Any<DeviceCleanupEvidenceQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Snapshot(
                Subject(
                    "PC-OLD",
                    enabled: false,
                    lastLogon: AssessedAt.AddDays(-120),
                    new DeviceCleanupFindingEvidence("StaleAd", "Critical", "AD exceeds the cleanup threshold.")),
                Subject("PC-ACTIVE", enabled: true, lastLogon: AssessedAt.AddDays(-2)))));
        _inventorySnapshots.ListHostsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<InventoryClientSnapshotHost>>(
            [
                new("PC-OLD.corp.example", AssessedAt.AddDays(-100)),
                new("PC-ACTIVE.corp.example", AssessedAt.AddDays(-1)),
                new("PC-INVENTORY-ONLY", AssessedAt.AddDays(-45)),
            ]);
        _inventoryEvidence.GetLatestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new DeviceCleanupInventoryEvidence(
                call.ArgAt<string>(0),
                AssessedAt.AddDays(-100),
                DeviceCleanupUserEvidenceAvailability.Available,
                "One observation is available.",
                [new DeviceCleanupUserObservation(
                    "LastInteractiveUser",
                    "S-1-5-21-1-2-3-1104",
                    "CORP\\alex",
                    AssessedAt.AddDays(-100),
                    null,
                    "High",
                    "Observed; not ownership.")]));
    }

    [Fact]
    public async Task DuplicateSubjectsRemainSelectableButNeverReadWindowsEvidenceByAlias()
    {
        var subject = Subject("PC", false, AssessedAt.AddYears(-1));
        _sourceEvidence.LoadAsync(Arg.Any<DeviceCleanupEvidenceQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Snapshot(subject, subject with { ActiveDirectory = subject.ActiveDirectory with { Enabled = true } })));
        _inventorySnapshots.ListHostsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<InventoryClientSnapshotHost>());
        var result = (await CreateService().GetPageAsync(new(), CancellationToken.None)).Value;
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, candidate => { Assert.False(candidate.CanTargetWindows); Assert.Equal(DeviceCleanupClassification.Review, candidate.Classification); });
        Assert.True((await CreateService().GetPageAsync(new(SelectedHost: subject.Host), CancellationToken.None)).IsFailure);
        var selected = await CreateService().GetPageAsync(new(SelectedHost: result.Candidates[1].SubjectKey), CancellationToken.None);
        Assert.True(selected.IsSuccess);
        Assert.NotNull(selected.Value.SelectedAssessment);
        await _inventoryEvidence.DidNotReceiveWithAnyArgs().GetLatestAsync(default!, default);
    }

    [Fact]
    public async Task GetPageAsync_DefaultsToReviewCandidatesAndExplainsClassification()
    {
        Result<DeviceCleanupPage> result = await CreateService().GetPageAsync(
            new ListDeviceCleanupCandidatesRequest(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Total);
        Assert.Equal("PC-OLD", result.Value.Candidates[0].SubjectKey);
        Assert.Equal("PC-OLD description", result.Value.Candidates[0].Description);
        Assert.Equal("Active Directory", result.Value.Candidates[0].DescriptionSource);
        Assert.Equal(DeviceCleanupClassification.PotentialCleanup, result.Value.Candidates[0].Classification);
        Assert.Contains("cleanup threshold", result.Value.Candidates[0].ClassificationExplanation);
        Assert.Equal("PC-INVENTORY-ONLY", result.Value.Candidates[1].SubjectKey);
        Assert.Equal(DeviceCleanupClassification.Review, result.Value.Candidates[1].Classification);
        Assert.DoesNotContain(result.Value.Candidates, candidate => candidate.SubjectKey == "PC-ACTIVE");
        await _inventoryEvidence.DidNotReceiveWithAnyArgs()
            .GetLatestAsync(default!, default);
    }

    [Fact]
    public async Task GetPageAsync_RecentObservationDoesNotOverrideCriticalStaleEvidence()
    {
        _inventorySnapshots.ListHostsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<InventoryClientSnapshotHost>>
            ([new("PC-OLD.corp.example", AssessedAt)]);

        Result<DeviceCleanupPage> result = await CreateService().GetPageAsync(
            new ListDeviceCleanupCandidatesRequest(),
            CancellationToken.None);

        Assert.Equal(DeviceCleanupClassification.PotentialCleanup,
            Assert.Single(result.Value.Candidates).Classification);
        Assert.Equal(AssessedAt, result.Value.Candidates[0].InventoryCapturedAtUtc);
    }

    [Fact]
    public async Task GetPageAsync_SelectedHostLoadsStoredUserEvidenceWithoutProbeOrScanContract()
    {
        Result<DeviceCleanupPage> result = await CreateService().GetPageAsync(
            new ListDeviceCleanupCandidatesRequest(SelectedHost: "pc-old.corp.example"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        DeviceCleanupAssessment assessment = Assert.IsType<DeviceCleanupAssessment>(result.Value.SelectedAssessment);
        Assert.Equal("Disabled", assessment.Sources.Single(source => source.Source == "Active Directory").State);
        Assert.Equal("CORP\\alex", Assert.Single(assessment.UserObservations).AccountDisplay);
        await _inventoryEvidence.Received(1).GetLatestAsync(
            "PC-OLD.corp.example",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPageAsync_IncludeWithoutSignalsKeepsHealthyAndIncompleteSubjects()
    {
        Result<DeviceCleanupPage> result = await CreateService().GetPageAsync(
            new ListDeviceCleanupCandidatesRequest(IncludeWithoutSignals: true),
            CancellationToken.None);

        Assert.Equal(3, result.Value.Total);
        Assert.Contains(result.Value.Candidates, candidate =>
            candidate.SubjectKey == "PC-ACTIVE"
            && candidate.Classification == DeviceCleanupClassification.NoCleanupSignal);
    }

    [Fact]
    public async Task GetExportSnapshotAsync_ReturnsEveryFilteredCandidateAcrossPageBoundaries()
    {
        Result<DeviceCleanupExportSnapshot> result = await CreateService().GetExportSnapshotAsync(
            new DeviceCleanupExportQuery(null, null, null, null, IncludeWithoutSignals: false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["PC-OLD", "PC-INVENTORY-ONLY"],
            result.Value.Candidates.Select(candidate => candidate.SubjectKey));
    }

    [Fact]
    public async Task GetPageAsync_UsesOpsiDescriptionWhenActiveDirectoryDescriptionIsMissing()
    {
        DeviceCleanupSubjectEvidence subject = Subject(
            "PC-OLD",
            enabled: false,
            lastLogon: AssessedAt.AddDays(-120),
            new DeviceCleanupFindingEvidence("StaleAd", "Critical", "AD exceeds the cleanup threshold."));
        subject = subject with
        {
            ActiveDirectory = subject.ActiveDirectory with { Description = null },
        };
        _sourceEvidence.LoadAsync(Arg.Any<DeviceCleanupEvidenceQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Snapshot(subject)));

        Result<DeviceCleanupPage> result = await CreateService().GetPageAsync(
            new ListDeviceCleanupCandidatesRequest(),
            CancellationToken.None);

        DeviceCleanupCandidate candidate = result.Value.Candidates.Single(item => item.SubjectKey == "PC-OLD");
        Assert.Equal("PC-OLD opsi description", candidate.Description);
        Assert.Equal("opsi", candidate.DescriptionSource);
    }

    [Fact]
    public async Task GetPageAsync_RejectsInvalidPagingBeforeLoadingSources()
    {
        Result<DeviceCleanupPage> result = await CreateService().GetPageAsync(
            new ListDeviceCleanupCandidatesRequest(PageSize: 101),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        await _sourceEvidence.DidNotReceiveWithAnyArgs()
            .LoadAsync(default!, default);
    }

    private DeviceCleanupService CreateService() => new(
        _sourceEvidence,
        _inventorySnapshots,
        _inventoryEvidence,
        Options.Create(new DeviceCleanupOptions
        {
            InventoryStaleWarningDays = 30,
            MaximumSubjects = 1000,
            MaximumPageSize = 100,
        }));

    private static DeviceCleanupEvidenceSnapshot Snapshot(params DeviceCleanupSubjectEvidence[] subjects) => new(
        AssessedAt,
        [
            new("Active Directory", ActionEvidenceAvailability.Available, "Available."),
            new("Kaspersky", ActionEvidenceAvailability.Available, "Available."),
            new("opsi", ActionEvidenceAvailability.Available, "Available."),
            new("Nessus", ActionEvidenceAvailability.Available, "Available."),
        ],
        subjects);

    private static DeviceCleanupSubjectEvidence Subject(
        string host,
        bool enabled,
        DateTimeOffset lastLogon,
        params DeviceCleanupFindingEvidence[] findings) => new(
        host,
        $"{host}.corp.example",
        findings.Any(finding => finding.Severity == "Critical") ? "CleanupCandidate" : "Healthy",
        new DeviceCleanupAdEvidence(
            true,
            enabled,
            "Windows 11",
            $"{host} description",
            null,
            "Clients",
            lastLogon),
        new DeviceCleanupKasperskyEvidence(true, AssessedAt.AddDays(-1), "Clients"),
        new DeviceCleanupOpsiEvidence(true, $"{host} opsi description", AssessedAt.AddDays(-1), "Depot-A"),
        new DeviceCleanupNessusEvidence(true, AssessedAt.AddDays(-1)),
        findings);
}
