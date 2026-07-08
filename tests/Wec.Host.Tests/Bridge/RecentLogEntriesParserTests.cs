using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class RecentLogEntriesParserTests
{
    private static readonly string[] Sample =
    [
        "2026-07-08 10:00:00.100 +02:00 [INF] Wec.Host: started",
        "2026-07-08 10:00:01.200 +02:00 [WRN] Wec.Modules.Security: check skipped",
        "2026-07-08 10:00:02.300 +02:00 [ERR] Wec.Infrastructure.Wmi: scan failed",
        "System.Exception: WinRM not reachable",
        "   at Wec.Scan()",
        "2026-07-08 10:00:03.400 +02:00 [DBG] Wec.Host: noise",
    ];

    [Fact]
    public void ParseWarnAndError_KeepsOnlyWarnErrorFatal_NewestFirst()
    {
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100);

        Assert.Equal(2, entries.Count);
        Assert.Equal("ERR", entries[0].Level); // newest first
        Assert.Equal("WRN", entries[1].Level);
        Assert.DoesNotContain(entries, entry => entry.Level is "INF" or "DBG");
    }

    [Fact]
    public void ParseWarnAndError_AttachesExceptionContinuationToTheEntry()
    {
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100);

        LogEntry error = entries[0];
        Assert.Contains("scan failed", error.Message);
        Assert.Contains("WinRM not reachable", error.Message); // multi-line entry stays together
        Assert.Contains("at Wec.Scan()", error.Message);
    }

    [Fact]
    public void ParseWarnAndError_RespectsTheLimit()
    {
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 1);

        Assert.Single(entries);
        Assert.Equal("ERR", entries[0].Level); // the newest kept
    }

    [Fact]
    public void ParseWarnAndError_HidesEntriesBeforeTheClearMarker()
    {
        // Marker between the WRN (10:00:01) and the ERR (10:00:02), both +02:00.
        var clearedAt = new DateTimeOffset(2026, 7, 8, 10, 0, 1, 500, TimeSpan.FromHours(2));

        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100, clearedAt);

        LogEntry entry = Assert.Single(entries);
        Assert.Equal("ERR", entry.Level);
    }

    [Fact]
    public void ParseWarnAndError_KeepsEntriesWithUnparseableTimestamps()
    {
        // An entry the marker cannot be compared against must stay visible.
        string[] lines = ["not-a-timestamp [ERR] broken header line"]; // no header match → ignored entirely
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(
            lines, 100, DateTimeOffset.MaxValue);
        Assert.Empty(entries); // no header, nothing parsed — nothing silently invented either

        IReadOnlyList<LogEntry> all = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100, DateTimeOffset.MaxValue);
        Assert.Empty(all); // marker in the future hides everything parseable
    }
}
