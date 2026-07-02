using System.Diagnostics.Eventing.Reader;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;

namespace Wec.Infrastructure.EventLog;

public sealed class SystemEventLogReader : IEventLogReader
{
    private const int LevelCritical = 1;

    private readonly ILogger<SystemEventLogReader> _logger;

    public SystemEventLogReader(ILogger<SystemEventLogReader> logger)
    {
        _logger = logger;
    }

    public Result<IReadOnlyList<EventLogEntrySummary>> ReadRecentCriticalAndErrorEntries(
        string logName,
        TimeSpan lookback,
        int maxEntries)
    {
        try
        {
            long lookbackMilliseconds = (long)lookback.TotalMilliseconds;
            var query = new EventLogQuery(
                logName,
                PathType.LogName,
                $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {lookbackMilliseconds}]]]")
            {
                ReverseDirection = true,
            };

            using var reader = new EventLogReader(query);
            var entries = new List<EventLogEntrySummary>();
            for (EventRecord? record = reader.ReadEvent();
                record is not null && entries.Count < maxEntries;
                record = reader.ReadEvent())
            {
                using (record)
                {
                    entries.Add(new EventLogEntrySummary(
                        record.ProviderName ?? "Unknown",
                        record.Id,
                        record.Level == LevelCritical ? "Critical" : "Error",
                        record.TimeCreated is { } timeCreated
                            ? new DateTimeOffset(timeCreated.ToUniversalTime(), TimeSpan.Zero)
                            : DateTimeOffset.UnixEpoch));
                }
            }

            _logger.LogDebug(
                "Event log {LogName}: {EntryCount} critical/error entries in the last {Lookback}",
                logName,
                entries.Count,
                lookback);
            return Result.Success<IReadOnlyList<EventLogEntrySummary>>(entries);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Access to event log {LogName} denied", logName);
            return Result.Failure<IReadOnlyList<EventLogEntrySummary>>(Error.AccessDenied(
                $"Access to the '{logName}' event log was denied.",
                PrivilegeLevel.Administrator));
        }
        catch (EventLogNotFoundException exception)
        {
            _logger.LogWarning(exception, "Event log {LogName} not found", logName);
            return Result.Failure<IReadOnlyList<EventLogEntrySummary>>(
                Error.NotFound($"The event log '{logName}' does not exist."));
        }
        catch (EventLogException exception)
        {
            _logger.LogError(exception, "Reading event log {LogName} failed", logName);
            return Result.Failure<IReadOnlyList<EventLogEntrySummary>>(new Error(
                ErrorCode.EventLogUnavailable,
                $"The event log '{logName}' could not be read.")
            {
                Details = exception.Message,
            });
        }
    }
}
