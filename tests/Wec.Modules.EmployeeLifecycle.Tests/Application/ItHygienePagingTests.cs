using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class ItHygienePagingTests
{
    [Fact]
    public async Task SnapshotCacheReusesMatchingRequestAndForceRefreshesOnce()
    {
        using var cache = new ItHygieneSnapshotCache();
        var request = new ItHygieneRequest(new DirectoryInventoryConnection(Server: "dc01"));
        int loads = 0;
        Task<Result<ItHygieneResult>> Load(ItHygieneRequest _, CancellationToken __)
        {
            loads++;
            return Task.FromResult(Result.Success(ResultAt(DateTimeOffset.UnixEpoch.AddMinutes(loads))));
        }

        Result<ItHygieneResult> first = await cache.GetAsync(request, false, Load, CancellationToken.None);
        Result<ItHygieneResult> reused = await cache.GetAsync(request, false, Load, CancellationToken.None);
        Result<ItHygieneResult> refreshed = await cache.GetAsync(request, true, Load, CancellationToken.None);

        Assert.Equal(2, loads);
        Assert.Equal(first.Value.AssessedAtUtc, reused.Value.AssessedAtUtc);
        Assert.True(refreshed.Value.AssessedAtUtc > reused.Value.AssessedAtUtc);
    }

    [Fact]
    public async Task SnapshotCacheDoesNotReuseAnotherConnectionRequest()
    {
        using var cache = new ItHygieneSnapshotCache();
        int loads = 0;
        Task<Result<ItHygieneResult>> Load(ItHygieneRequest _, CancellationToken __)
        {
            loads++;
            return Task.FromResult(Result.Success(ResultAt(DateTimeOffset.UnixEpoch)));
        }

        await cache.GetAsync(new ItHygieneRequest(new DirectoryInventoryConnection(Server: "dc01")), false, Load, CancellationToken.None);
        await cache.GetAsync(new ItHygieneRequest(new DirectoryInventoryConnection(Server: "dc02")), false, Load, CancellationToken.None);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task SnapshotCacheIgnoresUiOperationIdForTheSameConnection()
    {
        using var cache = new ItHygieneSnapshotCache();
        int loads = 0;
        Task<Result<ItHygieneResult>> Load(ItHygieneRequest _, CancellationToken __)
        {
            loads++;
            return Task.FromResult(Result.Success(ResultAt(DateTimeOffset.UnixEpoch)));
        }

        await cache.GetAsync(new ItHygieneRequest(OperationId: "first"), false, Load, CancellationToken.None);
        await cache.GetAsync(new ItHygieneRequest(OperationId: "second"), false, Load, CancellationToken.None);

        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task SnapshotCacheRetriesAfterAnUnavailableKasperskyResult()
    {
        using var cache = new ItHygieneSnapshotCache();
        var request = new ItHygieneRequest();
        int loads = 0;
        Task<Result<ItHygieneResult>> Load(ItHygieneRequest _, CancellationToken __)
        {
            loads++;
            ItHygieneResult result = ResultAt(DateTimeOffset.UnixEpoch.AddMinutes(loads));
            if (loads == 1)
            {
                result = result with
                {
                    Sources = result.Sources with
                    {
                        Kaspersky = new InventorySourceState(
                            InventorySourceAvailability.Unavailable,
                            "Authentication failure"),
                    },
                };
            }
            return Task.FromResult(Result.Success(result));
        }

        Result<ItHygieneResult> failedSource = await cache.GetAsync(request, false, Load, CancellationToken.None);
        Result<ItHygieneResult> recovered = await cache.GetAsync(request, false, Load, CancellationToken.None);
        Result<ItHygieneResult> cached = await cache.GetAsync(request, false, Load, CancellationToken.None);

        Assert.Equal(2, loads);
        Assert.Equal(InventorySourceAvailability.Unavailable, failedSource.Value.Sources.Kaspersky.Availability);
        Assert.Equal(InventorySourceAvailability.Available, recovered.Value.Sources.Kaspersky.Availability);
        Assert.Equal(recovered.Value.AssessedAtUtc, cached.Value.AssessedAtUtc);
    }

    [Fact]
    public void PageFiltersSortsAndBoundsTheServerResult()
    {
        ItHygieneResult result = ResultAt(DateTimeOffset.UnixEpoch,
            Device("ALPHA", HygieneStatus.Healthy),
            Device("BRAVO", HygieneStatus.Warning, HygieneFindingCode.MissingKaspersky),
            Device("CHARLIE", HygieneStatus.Critical, HygieneFindingCode.NessusCriticalVulnerabilities));

        HygieneDevicePage page = ItHygienePaging.Page(result, new ListHygieneDevicesRequest(
            Filter: "PROBLEMS", Page: 2, PageSize: 1, SortColumn: "overall", SortDirection: "desc"));

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal("BRAVO", Assert.Single(page.Items).ComputerName);
    }

    [Fact]
    public void PageSearchesSourceAndFindingText()
    {
        HygieneDevice alpha = Device("ALPHA", HygieneStatus.Healthy) with
        {
            Kaspersky = new KasperskyDeviceData(true, null, null, null, "Workstations"),
        };
        ItHygieneResult result = ResultAt(DateTimeOffset.UnixEpoch,
            alpha,
            Device("BRAVO", HygieneStatus.Warning, HygieneFindingCode.MissingKaspersky));

        HygieneDevicePage sourceMatch = ItHygienePaging.Page(result, new ListHygieneDevicesRequest(Search: "workstations"));
        HygieneDevicePage findingMatch = ItHygienePaging.Page(result, new ListHygieneDevicesRequest(Search: "missing kaspersky"));

        Assert.Equal("ALPHA", Assert.Single(sourceMatch.Items).ComputerName);
        Assert.Equal("BRAVO", Assert.Single(findingMatch.Items).ComputerName);
    }

    [Fact]
    public void ClientWorkspaceMergeDeduplicatesAndKeepsScanOnlyAndSavedOnlyClients()
    {
        ItHygieneResult result = ResultAt(DateTimeOffset.UnixEpoch,
            Device("ALPHA", HygieneStatus.Healthy));
        InventoryClientSnapshotHost[] scanned =
        [
            new("alpha.other.test", DateTimeOffset.UnixEpoch.AddHours(1)),
            new("SCAN-ONLY", DateTimeOffset.UnixEpoch.AddHours(2)),
        ];
        SavedClientTarget[] saved =
        [
            new("Pinned alpha", "ALPHA.example.test"),
            new("Saved label", "SAVED-ONLY"),
        ];

        ClientWorkspacePage page = ClientWorkspacePaging.Page(
            result, scanned, saved, new ListClientWorkspaceRequest());

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.SnapshotTotal);
        Assert.Equal(2, page.ScannedTotal);
        ClientWorkspaceListItem alpha = Assert.Single(page.Items, item => item.Name == "ALPHA");
        Assert.True(alpha.Scanned);
        Assert.True(alpha.Saved);
        Assert.NotNull(alpha.Environment);
        Assert.Contains(page.Items, item => item.Host == "SCAN-ONLY" && item.Scanned && item.Environment is null);
        Assert.Contains(page.Items, item => item.Name == "Saved label" && item.Saved && item.Environment is null);
    }

    [Fact]
    public void ClientWorkspaceIgnoresSnapshotsWithoutAHost()
    {
        ClientWorkspacePage page = ClientWorkspacePaging.Page(
            ResultAt(DateTimeOffset.UnixEpoch, Device("ALPHA", HygieneStatus.Healthy)),
            [
                new InventoryClientSnapshotHost(string.Empty, DateTimeOffset.UnixEpoch),
                new InventoryClientSnapshotHost("   ", DateTimeOffset.UnixEpoch),
                new InventoryClientSnapshotHost("BRAVO", DateTimeOffset.UnixEpoch),
            ],
            [],
            new ListClientWorkspaceRequest());

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.SnapshotTotal);
        Assert.Equal(1, page.ScannedTotal);
        Assert.DoesNotContain(page.Items, item => string.IsNullOrWhiteSpace(item.Host));
    }

    [Fact]
    public void ClientWorkspaceFiltersSortsGroupsAndReportsExactCounts()
    {
        HygieneDevice warning = Device("KF-BRAVO", HygieneStatus.Warning, HygieneFindingCode.MissingKaspersky);
        ItHygieneResult result = ResultAt(DateTimeOffset.UnixEpoch,
            Device("PK-ALPHA", HygieneStatus.Healthy),
            warning,
            Device("KF-CHARLIE", HygieneStatus.Critical));

        ClientWorkspacePage page = ClientWorkspacePaging.Page(
            result,
            [new InventoryClientSnapshotHost("KF-BRAVO.example.test", DateTimeOffset.UnixEpoch)],
            [],
            new ListClientWorkspaceRequest(
                Search: "detected",
                StatusFilter: "PROBLEMS",
                SourceFilter: "SCANNED",
                GroupMode: "site",
                SortColumn: "overall",
                SortDirection: "desc"));

        ClientWorkspaceListItem item = Assert.Single(page.Items);
        Assert.Equal("KF-BRAVO", item.Name);
        Assert.Equal("KF", item.GroupLabel);
        Assert.Equal(1, item.GroupTotal);
        Assert.Equal(1, page.Total);
        Assert.Equal(1, page.ScannedTotal);
        Assert.Equal(3, page.SnapshotTotal);
    }

    [Fact]
    public void ClientWorkspaceIncludesPostureMetadataAndAppliesDetailedFindingFilters()
    {
        DateTimeOffset assessedAtUtc = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        ItHygieneResult result = ResultAt(assessedAtUtc,
            Device("ALPHA", HygieneStatus.Healthy),
            Device("BRAVO", HygieneStatus.Warning, HygieneFindingCode.MissingKaspersky));

        ClientWorkspacePage page = ClientWorkspacePaging.Page(
            result,
            [],
            [],
            new ListClientWorkspaceRequest(StatusFilter: "MISSING_KASPERSKY"));

        Assert.Equal(assessedAtUtc, page.AssessedAtUtc);
        Assert.Equal("example.test", page.DomainName);
        Assert.Equal(ItHygieneService.Summarize(result.Devices), page.Summary);
        Assert.Equal("BRAVO", Assert.Single(page.Items).Name);
    }

    [Fact]
    public void ClientWorkspaceExposesTheActiveDirectoryDescription()
    {
        HygieneDevice described = Device("ALPHA", HygieneStatus.Healthy) with
        {
            ActiveDirectory = Device("ALPHA", HygieneStatus.Healthy).ActiveDirectory with
            {
                Description = "Accounting workstation",
            },
        };

        ClientWorkspacePage page = ClientWorkspacePaging.Page(
            ResultAt(DateTimeOffset.UnixEpoch, described),
            [],
            [],
            new ListClientWorkspaceRequest());

        Assert.Equal("Accounting workstation", Assert.Single(page.Items).Description);
    }

    [Fact]
    public void ClientWorkspaceSummaryMatchesTheDeduplicatedCanonicalRows()
    {
        HygieneDevice missing = Device("DUPLICATE", HygieneStatus.Warning, HygieneFindingCode.MissingKaspersky);
        HygieneDevice canonical = Device("DUPLICATE", HygieneStatus.Healthy) with
        {
            HostName = "DUPLICATE.other.test",
        };
        ItHygieneResult raw = ResultAt(DateTimeOffset.UnixEpoch, missing, canonical);
        ItHygieneResult result = raw with { Summary = ItHygieneService.Summarize(raw.Devices) };

        ClientWorkspacePage page = ClientWorkspacePaging.Page(
            result,
            [],
            [],
            new ListClientWorkspaceRequest());

        Assert.Equal(2, result.Summary.Total);
        Assert.Equal(1, result.Summary.MissingKaspersky);
        Assert.Equal(1, page.SnapshotTotal);
        Assert.Equal(1, page.Summary.Total);
        Assert.Equal(0, page.Summary.MissingKaspersky);
    }

    [Fact]
    public void ClientWorkspaceClampsPagesToOneHundredRows()
    {
        HygieneDevice[] devices = Enumerable.Range(1, 101)
            .Select(index => Device($"PC-{index:000}", HygieneStatus.Healthy))
            .ToArray();

        ClientWorkspacePage page = ClientWorkspacePaging.Page(
            ResultAt(DateTimeOffset.UnixEpoch, devices),
            [],
            [],
            new ListClientWorkspaceRequest(PageSize: 500));

        Assert.Equal(101, page.Total);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(100, page.Items.Count);
        Assert.Equal("PC-001", page.Items[0].Name);
        Assert.Equal("PC-100", page.Items[^1].Name);
    }

    [Fact]
    public void ClientWorkspacePaginatesCompleteGroupsInsteadOfIndividualClients()
    {
        HygieneDevice windowsOne = Device("WIN-01", HygieneStatus.Healthy);
        HygieneDevice windowsTwo = Device("WIN-02", HygieneStatus.Healthy);
        HygieneDevice server = Device("SERVER-01", HygieneStatus.Healthy) with
        {
            ActiveDirectory = Device("SERVER-01", HygieneStatus.Healthy).ActiveDirectory with
            {
                OperatingSystem = "Windows Server 2025",
            },
        };
        ItHygieneResult result = ResultAt(DateTimeOffset.UnixEpoch, windowsOne, windowsTwo, server);

        ClientWorkspacePage firstPage = ClientWorkspacePaging.Page(
            result, [], [], new ListClientWorkspaceRequest(GroupMode: "OS", PageSize: 1));
        ClientWorkspacePage secondPage = ClientWorkspacePaging.Page(
            result, [], [], new ListClientWorkspaceRequest(GroupMode: "OS", Page: 2, PageSize: 1));

        Assert.Equal(2, firstPage.GroupCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.All(firstPage.Items, item => Assert.Equal("Windows 11", item.GroupLabel));
        Assert.Single(secondPage.Items);
        Assert.Equal("Windows Server 2025", secondPage.Items[0].GroupLabel);
    }

    private static ItHygieneResult ResultAt(DateTimeOffset assessedAtUtc, params HygieneDevice[] devices) => new(
        assessedAtUtc,
        "example.test",
        new EnvironmentSourceStates(
            new InventorySourceState(InventorySourceAvailability.Available),
            new InventorySourceState(InventorySourceAvailability.Available),
            new InventorySourceState(InventorySourceAvailability.Available),
            new InventorySourceState(InventorySourceAvailability.Available)),
        new HygieneSummary(devices.Length, devices.Length, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        devices);

    private static HygieneDevice Device(string name, HygieneStatus status, HygieneFindingCode? finding = null) => new(
        name,
        $"{name}.example.test",
        new AdDeviceData(true, true, $"{name}.example.test", "Windows 11", null, null, null, null),
        new KasperskyDeviceData(false, null, null, null, null),
        new OpsiDeviceData(false, null, null, null, null, null),
        new NessusDeviceData(false, null, null, null, 0, 0, 0, 0, 0, [], []),
        new HygieneAssessment(status, finding.HasValue
            ? [new HygieneFinding(finding.Value, HygieneFindingSeverity.Warning, $"{finding.Value} detected")]
            : []));
}
