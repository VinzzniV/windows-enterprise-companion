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
}
