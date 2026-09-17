using System.IO;
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
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100).Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal("ERR", entries[0].Level); // newest first
        Assert.Equal("WRN", entries[1].Level);
        Assert.DoesNotContain(entries, entry => entry.Level is "INF" or "DBG");
    }

    [Fact]
    public void ParseWarnAndError_AttachesExceptionContinuationToTheEntry()
    {
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100).Entries;

        LogEntry error = entries[0];
        Assert.Equal("Wec.Infrastructure.Wmi", error.Source);
        Assert.Equal("scan failed", error.Summary);
        Assert.Contains("WinRM not reachable", error.TechnicalDetails);
        Assert.Contains("at Wec.Scan()", error.TechnicalDetails);
        Assert.StartsWith("Wec.Infrastructure.Wmi: scan failed", error.TechnicalDetails);
    }

    [Fact]
    public void ParseWarnAndError_RespectsTheLimit()
    {
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 1).Entries;

        Assert.Single(entries);
        Assert.Equal("ERR", entries[0].Level); // the newest kept
    }

    [Fact]
    public void ParseWarnAndError_RemovesStructuredMetadataFromTheSummaryOnly()
    {
        string[] lines =
        [
            "2026-08-19 16:15:28.662 +02:00 [WRN] Microsoft.EntityFrameworkCore.Query: Query may be slow. {\"EventId\":20504,\"Module\":\"security\"}",
        ];

        LogEntry entry = Assert.Single(RecentLogEntriesHandler.ParseWarnAndError(lines, 10).Entries);

        Assert.Equal("Query may be slow.", entry.Summary);
        Assert.Contains("\"Module\":\"security\"", entry.TechnicalDetails);
    }

    [Fact]
    public void ParseWarnAndError_HidesEntriesBeforeTheClearMarker()
    {
        // Marker between the WRN (10:00:01) and the ERR (10:00:02), both +02:00.
        var clearedAt = new DateTimeOffset(2026, 7, 8, 10, 0, 1, 500, TimeSpan.FromHours(2));

        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100, clearedAt).Entries;

        LogEntry entry = Assert.Single(entries);
        Assert.Equal("ERR", entry.Level);
    }

    [Fact]
    public void ParseWarnAndError_KeepsEntriesWithUnparseableTimestamps()
    {
        // An entry the marker cannot be compared against must stay visible.
        string[] lines = ["not-a-timestamp [ERR] broken header line"]; // no header match → ignored entirely
        IReadOnlyList<LogEntry> entries = RecentLogEntriesHandler.ParseWarnAndError(
            lines, 100, DateTimeOffset.MaxValue).Entries;
        Assert.Empty(entries); // no header, nothing parsed — nothing silently invented either

        IReadOnlyList<LogEntry> all = RecentLogEntriesHandler.ParseWarnAndError(Sample, 100, DateTimeOffset.MaxValue).Entries;
        Assert.Empty(all); // marker in the future hides everything parseable
    }

    [Fact]
    public void ParseWarnAndError_AppliesTheLevelFilterBeforeTheResultLimit()
    {
        var lines = new List<string>
        {
            "2026-07-08 09:00:00.000 +02:00 [ERR] Wec.Host: relevant error",
        };
        lines.AddRange(Enumerable.Range(0, 500).Select(index =>
            $"2026-07-08 10:{index / 60:00}:{index % 60:00}.000 +02:00 [WRN] Wec.Host: warning {index}"));

        ParsedLogEntries errors = RecentLogEntriesHandler.ParseWarnAndError(
            lines, 500, levelFilter: RecentLogLevelFilter.Errors);

        LogEntry entry = Assert.Single(errors.Entries);
        Assert.Equal("relevant error", entry.Summary);
        Assert.Equal(1, errors.TotalMatched);
        Assert.False(errors.ResultTruncated);
    }

    [Fact]
    public void ParseWarnAndError_ReportsTruncatedContinuationDetails()
    {
        var lines = new List<string>
        {
            "2026-07-08 10:00:00.000 +02:00 [ERR] Wec.Host: failed",
        };
        lines.AddRange(Enumerable.Range(1, 41).Select(index => $"continuation {index}"));

        LogEntry entry = Assert.Single(RecentLogEntriesHandler.ParseWarnAndError(
            lines, 10, maxContinuationLines: 40).Entries);

        Assert.True(entry.TechnicalDetailsTruncated);
        Assert.Contains("continuation 40", entry.TechnicalDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("continuation 41", entry.TechnicalDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSharedTailAsync_BoundsTheEvaluatedFileWindow()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(path, Enumerable.Range(0, 200).Select(index => $"line {index:0000} data"));

            LogFileReadResult result = await RecentLogEntriesHandler.ReadSharedTailAsync(
                path, 128, CancellationToken.None);

            Assert.True(result.Truncated);
            Assert.InRange(result.EvaluatedBytes, 1, 128);
            Assert.DoesNotContain("line 0000 data", result.Lines);
            Assert.Equal("line 0199 data", result.Lines[^1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
