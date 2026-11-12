using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.DeviceCleanup.Application;
using Wec.Modules.DeviceCleanup.Handlers;

namespace Wec.Modules.DeviceCleanup.Tests;

public sealed class DeviceCleanupWorkbookExporterTests
{
    private static readonly DateTimeOffset AssessedAt =
        new(2026, 9, 11, 8, 30, 0, TimeSpan.Zero);

    private readonly IPingProbe _pingProbe = Substitute.For<IPingProbe>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DeviceCleanupOptions _options = new()
    {
        InventoryStaleWarningDays = 30,
        MaximumSubjects = 1000,
        MaximumPageSize = 100,
        ExportPingTimeoutMilliseconds = 500,
        ExportPingParallelism = 4,
    };

    public DeviceCleanupWorkbookExporterTests()
    {
        _clock.UtcNow.Returns(AssessedAt.AddMinutes(5));
        _pingProbe.SendAsync("pc-old.corp.example", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(true, 12, "Success")));
        _pingProbe.SendAsync("pc-no-reply.corp.example", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PingProbeReply(false, 0, "TimedOut")));
    }

    [Fact]
    public async Task UnresolvedRowsAreExportedWithoutPingAndExplainTheOmittedCheck()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { Candidates = snapshot.Candidates.Select(candidate => candidate with { CanTargetWindows = false }).ToArray() };
        using XLWorkbook workbook = await CreateExporter().CreateAsync(snapshot, CancellationToken.None);
        Assert.Equal(snapshot.Candidates.Count, workbook.Worksheet("Device Cleanup").Cell("H3").GetValue<int>());
        Assert.Equal("Not checked: unresolved target identity", workbook.Worksheet("Device Cleanup").Cell("P8").GetString());
        await _pingProbe.DidNotReceiveWithAnyArgs().SendAsync(default!, default, default);
    }

    [Fact]
    public async Task CreateAsync_WritesTypedFilterableEvidenceAndPingResults()
    {
        using XLWorkbook workbook = await CreateExporter().CreateAsync(Snapshot(), CancellationToken.None);
        IXLWorksheet sheet = workbook.Worksheet("Device Cleanup");

        Assert.Equal("WEC Device Cleanup", sheet.Cell("A2").GetString());
        Assert.Equal(2, sheet.Cell("H3").GetValue<int>());
        Assert.Equal("Device", sheet.Cell("A7").GetString());
        Assert.Equal("Accounting workstation", sheet.Cell("B8").GetString());
        Assert.Equal("Active Directory", sheet.Cell("C8").GetString());
        Assert.Equal("Disabled", sheet.Cell("F8").GetString());
        Assert.Equal("Registered", sheet.Cell("H8").GetString());
        Assert.Equal("Ping responded", sheet.Cell("P8").GetString());
        Assert.Equal("No ping response", sheet.Cell("P9").GetString());
        Assert.Equal("Not available", sheet.Cell("B9").GetString());
        Assert.Equal(XLDataType.DateTime, sheet.Cell("G8").DataType);
        Assert.Equal("dd.mm.yyyy hh:mm", sheet.Cell("G8").Style.DateFormat.Format);
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.True(sheet.Cell("A7").Style.Font.Bold);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream, validate: true);
        stream.Position = 0;
        using var reopened = new XLWorkbook(stream);
        Assert.Equal("pc-old.corp.example", reopened.Worksheet("Device Cleanup").Cell("A8").GetString());

        await _pingProbe.Received(1).SendAsync(
            "pc-old.corp.example",
            TimeSpan.FromMilliseconds(500),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_WritesTheCompleteWorkbookToTheSelectedPath()
    {
        IDeviceCleanupEvidenceProvider evidence = Substitute.For<IDeviceCleanupEvidenceProvider>();
        evidence.LoadAsync(Arg.Any<DeviceCleanupEvidenceQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(SourceSnapshot()));
        IInventoryClientSnapshotProvider inventory = Substitute.For<IInventoryClientSnapshotProvider>();
        inventory.ListHostsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<InventoryClientSnapshotHost>>([]);
        IDeviceCleanupInventoryEvidenceProvider inventoryEvidence =
            Substitute.For<IDeviceCleanupInventoryEvidenceProvider>();
        var service = new DeviceCleanupService(
            evidence,
            inventory,
            inventoryEvidence,
            Options.Create(_options));
        ISaveFileDialogService dialog = Substitute.For<ISaveFileDialogService>();
        string path = Path.Combine(Path.GetTempPath(), $"wec-device-cleanup-{Guid.NewGuid():N}.xlsx");
        dialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(path);
        var handler = new ExportDeviceCleanupWorkbookHandler(
            service,
            CreateExporter(),
            dialog,
            _clock,
            NullLogger<ExportDeviceCleanupWorkbookHandler>.Instance);

        try
        {
            Result<ExportDeviceCleanupWorkbookResult> result = await handler.HandleAsync(
                new ExportDeviceCleanupWorkbookRequest(),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.Cancelled);
            Assert.Equal(2, result.Value.ExportedCount);
            Assert.False(result.Value.SubjectsTruncated);
            Assert.True(File.Exists(path));
            using var workbook = new XLWorkbook(path);
            Assert.Equal(2, workbook.Worksheet("Device Cleanup").Cell("H3").GetValue<int>());
            dialog.Received(1).PromptForSavePath(
                Arg.Is<string>(name => name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)),
                Arg.Is<string>(filter => filter.Contains("Excel workbooks", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private DeviceCleanupWorkbookExporter CreateExporter() => new(
        _pingProbe,
        _clock,
        Options.Create(_options));

    private static DeviceCleanupExportSnapshot Snapshot() => new(
        [
            Candidate(
                "PC-OLD",
                "pc-old.corp.example",
                "Accounting workstation",
                "Active Directory",
                AssessedAt.AddDays(-120)),
            Candidate(
                "PC-NO-REPLY",
                "pc-no-reply.corp.example",
                null,
                null,
                AssessedAt.AddDays(-90)),
        ],
        AssessedAt,
        [new ActionEvidenceSourceState("Active Directory", ActionEvidenceAvailability.Available, "Available.")],
        SubjectsTruncated: false);

    private static DeviceCleanupEvidenceSnapshot SourceSnapshot() => new(
        AssessedAt,
        [
            new ActionEvidenceSourceState("Active Directory", ActionEvidenceAvailability.Available, "Available."),
            new ActionEvidenceSourceState("Kaspersky", ActionEvidenceAvailability.Available, "Available."),
            new ActionEvidenceSourceState("opsi", ActionEvidenceAvailability.Available, "Available."),
            new ActionEvidenceSourceState("Nessus", ActionEvidenceAvailability.Available, "Available."),
        ],
        [
            Subject("PC-OLD", "pc-old.corp.example", "Accounting workstation", AssessedAt.AddDays(-120)),
            Subject("PC-NO-REPLY", "pc-no-reply.corp.example", null, AssessedAt.AddDays(-90)),
        ]);

    private static DeviceCleanupSubjectEvidence Subject(
        string subjectKey,
        string host,
        string? description,
        DateTimeOffset lastLogon) => new(
        subjectKey,
        host,
        "CleanupCandidate",
        new DeviceCleanupAdEvidence(
            Exists: true,
            Enabled: false,
            OperatingSystem: "Windows 11",
            Description: description,
            DistinguishedName: null,
            OrganizationalUnit: "Clients/Retired",
            LastLogonAtUtc: lastLogon),
        new DeviceCleanupKasperskyEvidence(true, AssessedAt.AddDays(-80), "Retired"),
        new DeviceCleanupOpsiEvidence(true, null, AssessedAt.AddDays(-70), "Depot-A"),
        new DeviceCleanupNessusEvidence(true, AssessedAt.AddDays(-60)),
        [new DeviceCleanupFindingEvidence("StaleAd", "Critical", "AD activity is stale.")]);

    private static DeviceCleanupCandidate Candidate(
        string subjectKey,
        string host,
        string? description,
        string? descriptionSource,
        DateTimeOffset lastLogon) => new(
        SubjectKey: subjectKey,
        Host: host,
        Description: description,
        DescriptionSource: descriptionSource,
        Classification: DeviceCleanupClassification.PotentialCleanup,
        ClassificationExplanation: "AD activity exceeds its cleanup threshold.",
        ActiveDirectoryExists: true,
        ActiveDirectoryEnabled: false,
        ActiveDirectoryLastLogonAtUtc: lastLogon,
        KasperskyExists: true,
        KasperskyLastSeenAtUtc: AssessedAt.AddDays(-80),
        OpsiExists: true,
        OpsiLastSeenAtUtc: AssessedAt.AddDays(-70),
        NessusExists: true,
        NessusLastScanAtUtc: AssessedAt.AddDays(-60),
        InventoryExists: true,
        InventoryCapturedAtUtc: AssessedAt.AddDays(-50),
        RelevantFindingCount: 1);
}
