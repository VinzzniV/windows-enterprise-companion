using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application;

namespace Wec.Modules.Diagnostics.Tests.Application;

public class EventLogQueryServiceTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private EventLogQueryService CreateService() => new(_wmiQueryService, TestDefaults.Clock());

    private void SetUpEvents(params WmiInstance[] instances) =>
        _wmiQueryService.SetUpWmiQuery("Win32_NTLogEvent", instances);

    private static WmiInstance Event(string source, long eventType, DateTime time) =>
        SystemTestSetup.Instance(
            ("SourceName", source),
            ("EventType", eventType),
            ("EventCode", 7031L),
            ("TimeGenerated", time),
            ("Message", "Service crashed."));

    [Fact]
    public void BuildQuery_EmbedsWhereClauseAndDmtfUtcLookback()
    {
        var preset = new EventLogPreset("x", "Logfile='System' AND EventType=1", TimeSpan.FromHours(24));
        string wql = EventLogQueryService.BuildQuery(
            preset, new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains("Logfile='System' AND EventType=1", wql, StringComparison.Ordinal);
        Assert.Contains("TimeGenerated >= '20260707120000.000000+000'", wql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPreset_FailsWithInvalidRequest()
    {
        Result<EventLogQueryResult> result = await CreateService().QueryAsync(
            DiagnosticContext.Local, "nope", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
    }

    [Fact]
    public async Task Query_MapsLevelsAndSortsNewestFirst()
    {
        SetUpEvents(
            Event("disk", 2, new DateTime(2026, 7, 8, 8, 0, 0, DateTimeKind.Utc)),
            Event("Ntfs", 1, new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc)));

        Result<EventLogQueryResult> result = await CreateService().QueryAsync(
            DiagnosticContext.Local, "disk-events", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("disk-events", result.Value.PresetKey);
        Assert.Equal(2, result.Value.TotalMatched);
        Assert.False(result.Value.Truncated);
        Assert.Equal("Ntfs", result.Value.Entries[0].Source); // newest first
        Assert.Equal("Error", result.Value.Entries[0].Level);
        Assert.Equal("Warning", result.Value.Entries[1].Level);
    }

    [Fact]
    public async Task Query_TruncatesToMaxEntries()
    {
        SetUpEvents([.. Enumerable.Range(0, EventLogQueryService.MaxEntries + 5)
            .Select(index => Event("disk", 1, new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index)))]);

        Result<EventLogQueryResult> result = await CreateService().QueryAsync(
            DiagnosticContext.Local, "disk-events", CancellationToken.None);

        Assert.True(result.Value.Truncated);
        Assert.Equal(EventLogQueryService.MaxEntries + 5, result.Value.TotalMatched);
        Assert.Equal(EventLogQueryService.MaxEntries, result.Value.Entries.Count);
    }
}
