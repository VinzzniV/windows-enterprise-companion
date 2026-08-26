using System.Globalization;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class EventLogSummaryDiagnostic : IDiagnostic
{
    private readonly IEventLogReader _eventLogReader;
    private readonly IWmiQueryService _wmiQueryService;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public EventLogSummaryDiagnostic(
        IEventLogReader eventLogReader,
        IWmiQueryService wmiQueryService,
        IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _eventLogReader = eventLogReader;
        _wmiQueryService = wmiQueryService;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-EVENTLOG";

    public async Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        if (_options.EventLogNames.Length == 0)
        {
            return [BuildResult(
                "(none)",
                DiagnosticStatus.NotRun,
                "No event logs configured for the summary",
                new Dictionary<string, string> { ["configuredLogs"] = "(empty)" },
                ["Configure Wec:Diagnostics:EventLogNames (e.g. [\"System\"])."],
                requiredPrivilege: null)];
        }

        var results = new List<DiagnosticResult>();
        foreach (string logName in _options.EventLogNames)
        {
            results.Add(context.Target.IsLocal
                ? SummarizeLocalLog(logName)
                : await SummarizeRemoteLogAsync(context, logName, cancellationToken));
        }

        return results;
    }

    private DiagnosticResult SummarizeLocalLog(string logName)
    {
        Result<IReadOnlyList<EventLogEntrySummary>> entries = _eventLogReader.ReadRecentCriticalAndErrorEntries(
            logName,
            _options.EventLogLookback,
            _options.EventLogMaxEntries);

        if (entries.IsFailure)
        {
            return BuildReadFailure(logName, entries.Error!, isRemote: false);
        }

        return SummarizeEntries(logName, entries.Value);
    }

    private async Task<DiagnosticResult> SummarizeRemoteLogAsync(
        DiagnosticContext context,
        string logName,
        CancellationToken cancellationToken)
    {
        string escapedLogName = logName.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("'", @"\'", StringComparison.Ordinal);
        string since = $"{(_clock.UtcNow - _options.EventLogLookback).UtcDateTime:yyyyMMddHHmmss}.000000+000";
        string query = "SELECT EventType, SourceName, TimeGenerated "
            + $"FROM Win32_NTLogEvent WHERE Logfile='{escapedLogName}' AND EventType=1 "
            + $"AND TimeGenerated >= '{since}'";
        Result<IReadOnlyList<WmiInstance>> events = await _wmiQueryService.QueryAsync(
            context,
            @"root\cimv2",
            query,
            cancellationToken);

        if (events.IsFailure)
        {
            return BuildReadFailure(logName, events.Error!, isRemote: true);
        }

        IReadOnlyList<EventLogEntrySummary> entries = [.. events.Value
            .Take(_options.EventLogMaxEntries)
            .Select(entry => new EventLogEntrySummary(
                entry.GetString("SourceName") ?? "unknown",
                EventId: 0,
                "Error",
                ToUtc(entry.GetValue<DateTime?>("TimeGenerated"))))];

        DiagnosticResult result = SummarizeEntries(
            logName,
            entries,
            totalErrorCount: events.Value.Count);
        if (events.Value.Count > _options.EventLogMaxEntries)
        {
            var evidence = new Dictionary<string, string>(result.Evidence)
            {
                ["truncated"] = "true",
                ["matchedEntries"] = events.Value.Count.ToString(CultureInfo.InvariantCulture),
            };
            result = result with { Evidence = evidence };
        }

        return result;
    }

    private DiagnosticResult SummarizeEntries(
        string logName,
        IReadOnlyList<EventLogEntrySummary> entries,
        int? totalErrorCount = null)
    {
        int criticalCount = entries.Count(entry => entry.Level == "Critical");
        int errorCount = totalErrorCount ?? entries.Count - criticalCount;
        string topProviders = string.Join("; ", entries
            .GroupBy(entry => entry.ProviderName)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => $"{group.Key} (x{group.Count()})"));

        var evidence = new Dictionary<string, string>
        {
            ["log"] = logName,
            ["lookback"] = _options.EventLogLookback.ToString(),
            ["criticalEntries"] = criticalCount.ToString(CultureInfo.InvariantCulture),
            ["errorEntries"] = errorCount.ToString(CultureInfo.InvariantCulture),
            ["topProviders"] = topProviders.Length > 0 ? topProviders : "—",
        };

        if (criticalCount > 0)
        {
            return BuildResult(
                logName,
                DiagnosticStatus.Warning,
                $"Event log '{logName}' contains {criticalCount} critical entries",
                evidence,
                [
                    "Open Event Viewer and inspect the critical entries — they often point at crashes or hardware issues.",
                    "Start with the top providers listed in the evidence.",
                ],
                requiredPrivilege: null);
        }

        if (errorCount > _options.EventLogErrorWarningThreshold)
        {
            return BuildResult(
                logName,
                DiagnosticStatus.Warning,
                $"Event log '{logName}' shows an elevated error rate ({errorCount} in {_options.EventLogLookback})",
                evidence,
                [
                    "Inspect the top providers in Event Viewer — a repeating error usually dominates the count.",
                    "Compare against the configured threshold (Wec:Diagnostics:EventLogErrorWarningThreshold).",
                ],
                requiredPrivilege: null);
        }

        return BuildResult(
            logName,
            DiagnosticStatus.Pass,
            $"Event log '{logName}' shows no unusual error activity",
            evidence,
            [],
            requiredPrivilege: null);
    }

    private DiagnosticResult BuildReadFailure(string logName, Error error, bool isRemote) => BuildResult(
        logName,
        DiagnosticStatus.NotRun,
        $"Event log '{logName}' could not be summarized",
        new Dictionary<string, string>
        {
            ["log"] = logName,
            ["errorCode"] = error.Code.ToString(),
            ["errorMessage"] = error.Message,
        },
        error.RequiredPrivilege is not null && !isRemote
            ? ["Restart the app as administrator to read this log."]
            : [isRemote
                ? "Verify WMI/Event Log access and credentials for the target."
                : "Verify the log name and the Windows Event Log service."],
        error.RequiredPrivilege);

    private static DateTimeOffset ToUtc(DateTime? value)
    {
        if (value is null)
        {
            return DateTimeOffset.UnixEpoch;
        }

        DateTime utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };
        return new DateTimeOffset(utc);
    }

    private DiagnosticResult BuildResult(
        string logName,
        DiagnosticStatus status,
        string title,
        IReadOnlyDictionary<string, string> evidence,
        IReadOnlyList<string> nextSteps,
        Wec.Core.Privileges.PrivilegeLevel? requiredPrivilege) => new(
        DiagnosticId,
        title,
        status,
        DiagnosticCategory.EventLog,
        $"Event log '{logName}'",
        evidence,
        nextSteps,
        requiredPrivilege,
        _clock.UtcNow);
}
