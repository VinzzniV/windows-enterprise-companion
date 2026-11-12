using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.Diagnostics.Application;

public sealed record RemoteEventLogEntry(
    DateTimeOffset? TimeGenerated,
    string Level,
    string Source,
    long EventCode,
    string Message,
    bool MessageTruncated);

public sealed record EventLogQueryResult(
    string PresetKey,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int TotalMatched,
    int ResultLimit,
    bool Truncated,
    IReadOnlyList<RemoteEventLogEntry> Entries);

/// <summary>A canned event-log question ("system errors, last 24h") expressed as WQL.</summary>
public sealed record EventLogPreset(string Key, string WhereClause, TimeSpan Lookback);

/// <summary>
/// On-demand event-log queries over Win32_NTLogEvent through the
/// credential-aware CIM seam — works for the local machine and remote targets
/// alike. Deliberately not persisted: this is a live troubleshooting view.
/// </summary>
public sealed class EventLogQueryService
{
    private const string CimV2Namespace = @"root\cimv2";
    // ponytail: hard result cap; WQL has no TOP and unfiltered NT logs are huge.
    // Raise or page if a preset legitimately needs more than 200 rows.
    internal const int MaxEntries = 200;

    /// <summary>Canned queries; the frontend sends the key. Keep keys in sync with EventLogSection.tsx.</summary>
    public static readonly IReadOnlyList<EventLogPreset> Presets =
    [
        new("system-errors", "Logfile='System' AND EventType=1", TimeSpan.FromHours(24)),
        new("system-warnings", "Logfile='System' AND (EventType=1 OR EventType=2)", TimeSpan.FromHours(24)),
        new("application-errors", "Logfile='Application' AND EventType=1", TimeSpan.FromHours(24)),
        new("app-crashes", "Logfile='Application' AND (SourceName='Application Error' OR SourceName='Windows Error Reporting')", TimeSpan.FromDays(7)),
        new("unexpected-shutdowns", "Logfile='System' AND (EventCode=6008 OR EventCode=41)", TimeSpan.FromDays(30)),
        new("service-failures", "Logfile='System' AND SourceName='Service Control Manager' AND EventType=1", TimeSpan.FromDays(7)),
        new("disk-events", "Logfile='System' AND (SourceName='disk' OR SourceName='Ntfs' OR SourceName='volmgr') AND (EventType=1 OR EventType=2)", TimeSpan.FromDays(7)),
    ];

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IClock _clock;

    public EventLogQueryService(IWmiQueryService wmiQueryService, IClock clock)
    {
        _wmiQueryService = wmiQueryService;
        _clock = clock;
    }

    public async Task<Result<EventLogQueryResult>> QueryAsync(
        DiagnosticContext context,
        string presetKey,
        CancellationToken cancellationToken)
    {
        EventLogPreset? preset = Presets.FirstOrDefault(
            candidate => string.Equals(candidate.Key, presetKey, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return Result.Failure<EventLogQueryResult>(new Error(
                ErrorCode.InvalidRequest, $"Unknown event log preset '{presetKey}'."));
        }

        DateTimeOffset windowEndUtc = _clock.UtcNow;
        DateTimeOffset windowStartUtc = windowEndUtc - preset.Lookback;
        string wql = BuildQuery(preset, windowEndUtc);
        Result<IReadOnlyList<WmiInstance>> events = await _wmiQueryService.QueryAsync(
            context, CimV2Namespace, wql, cancellationToken);
        if (events.IsFailure)
        {
            return Result.Failure<EventLogQueryResult>(events.Error!);
        }

        List<RemoteEventLogEntry> entries = [.. events.Value
            .Select(ToEntry)
            .OrderByDescending(entry => entry.TimeGenerated ?? DateTimeOffset.MinValue)];

        bool truncated = entries.Count > MaxEntries;
        return Result.Success(new EventLogQueryResult(
            preset.Key,
            windowStartUtc,
            windowEndUtc,
            entries.Count,
            MaxEntries,
            truncated,
            truncated ? entries[..MaxEntries] : entries));
    }

    internal static string BuildQuery(EventLogPreset preset, DateTimeOffset nowUtc)
    {
        // WQL compares CIM datetimes against DMTF-formatted strings; +000 = UTC.
        string since = $"{nowUtc.UtcDateTime - preset.Lookback:yyyyMMddHHmmss}.000000+000";
        return "SELECT Logfile, EventCode, EventType, SourceName, TimeGenerated, Message "
            + $"FROM Win32_NTLogEvent WHERE {preset.WhereClause} AND TimeGenerated >= '{since}'";
    }

    private static RemoteEventLogEntry ToEntry(WmiInstance instance)
    {
        string message = (instance.GetString("Message") ?? string.Empty).Trim();
        bool messageTruncated = message.Length > 500;
        return new RemoteEventLogEntry(
            instance.GetValue<DateTime?>("TimeGenerated") is DateTime time
                ? new DateTimeOffset(time.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
                    : time.ToUniversalTime())
                : null,
            LevelName(instance.GetInteger("EventType")),
            instance.GetString("SourceName") ?? "unknown",
            instance.GetInteger("EventCode") ?? 0,
            messageTruncated ? message[..500] + " …" : message,
            messageTruncated);
    }

    internal static string LevelName(long? eventType) => eventType switch
    {
        1 => "Error",
        2 => "Warning",
        3 => "Information",
        4 => "Audit Success",
        5 => "Audit Failure",
        _ => "Unknown",
    };

}
