using Wec.Core.Results;

namespace Wec.Core.Abstractions;

public sealed record EventLogEntrySummary(
    string ProviderName,
    int EventId,
    string Level,
    DateTimeOffset TimeCreatedUtc);

public interface IEventLogReader
{
    /// <summary>
    /// Critical and error entries of the given log within the lookback window,
    /// newest first, capped at <paramref name="maxEntries"/>.
    /// </summary>
    Result<IReadOnlyList<EventLogEntrySummary>> ReadRecentCriticalAndErrorEntries(
        string logName,
        TimeSpan lookback,
        int maxEntries);
}
