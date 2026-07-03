using System.Globalization;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application.Diagnostics;

internal sealed class EventLogSummaryDiagnostic : IDiagnostic
{
    private readonly IEventLogReader _eventLogReader;
    private readonly DiagnosticsOptions _options;
    private readonly IClock _clock;

    public EventLogSummaryDiagnostic(
        IEventLogReader eventLogReader,
        IOptions<DiagnosticsOptions> options,
        IClock clock)
    {
        _eventLogReader = eventLogReader;
        _options = options.Value;
        _clock = clock;
    }

    public string DiagnosticId => "WEC-DIAG-SYS-EVENTLOG";

    public Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        if (!context.Target.IsLocal)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([DiagnosticResults.LocalPerspective(
                DiagnosticId, "Event log summary", DiagnosticCategory.EventLog,
                "Event logs", context.Target.DisplayName, _clock.UtcNow)]);
        }

        if (_options.EventLogNames.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticResult>>([BuildResult(
                "(none)",
                DiagnosticStatus.NotRun,
                "No event logs configured for the summary",
                new Dictionary<string, string> { ["configuredLogs"] = "(empty)" },
                ["Configure Wec:Diagnostics:EventLogNames (e.g. [\"System\"])."],
                requiredPrivilege: null)]);
        }

        var results = new List<DiagnosticResult>();
        foreach (string logName in _options.EventLogNames)
        {
            results.Add(SummarizeLog(logName));
        }

        return Task.FromResult<IReadOnlyList<DiagnosticResult>>(results);
    }

    private DiagnosticResult SummarizeLog(string logName)
    {
        Result<IReadOnlyList<EventLogEntrySummary>> entries = _eventLogReader.ReadRecentCriticalAndErrorEntries(
            logName,
            _options.EventLogLookback,
            _options.EventLogMaxEntries);

        if (entries.IsFailure)
        {
            return BuildResult(
                logName,
                DiagnosticStatus.NotRun,
                $"Event log '{logName}' could not be summarized",
                new Dictionary<string, string>
                {
                    ["log"] = logName,
                    ["errorCode"] = entries.Error!.Code.ToString(),
                    ["errorMessage"] = entries.Error.Message,
                },
                entries.Error.RequiredPrivilege is not null
                    ? ["Restart the app as administrator to read this log."]
                    : ["Verify the log name and the Windows Event Log service."],
                entries.Error.RequiredPrivilege);
        }

        int criticalCount = entries.Value.Count(entry => entry.Level == "Critical");
        int errorCount = entries.Value.Count - criticalCount;
        string topProviders = string.Join("; ", entries.Value
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
