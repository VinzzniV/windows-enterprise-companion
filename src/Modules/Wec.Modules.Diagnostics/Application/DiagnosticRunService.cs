using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application;

public sealed partial class DiagnosticRunService
{
    private readonly List<IDiagnostic> _diagnostics;
    private readonly IClock _clock;
    private readonly ILogger<DiagnosticRunService> _logger;

    public DiagnosticRunService(
        IEnumerable<IDiagnostic> diagnostics,
        IClock clock,
        ILogger<DiagnosticRunService> logger)
    {
        _diagnostics = diagnostics.ToList();
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<DiagnosticRunResult>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedAtUtc = _clock.UtcNow;
        var results = new List<DiagnosticResult>();

        foreach (IDiagnostic diagnostic in _diagnostics)
        {
            try
            {
                results.AddRange(await diagnostic.EvaluateAsync(context, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A crashing diagnostic is a bug; it becomes a visible FAIL result
                // instead of taking the run down or silently disappearing
                _logger.LogError(exception, "Diagnostic {DiagnosticId} threw unexpectedly", diagnostic.DiagnosticId);
                results.Add(new DiagnosticResult(
                    diagnostic.DiagnosticId,
                    "Diagnostic crashed unexpectedly",
                    DiagnosticStatus.Fail,
                    DiagnosticCategory.Network,
                    diagnostic.DiagnosticId,
                    new Dictionary<string, string>
                    {
                        ["error"] = "Unexpected internal error. See the application log for details.",
                    },
                    ["Report this as a bug together with the log file."],
                    RequiredPrivilege: null,
                    _clock.UtcNow));
            }
        }

        DateTimeOffset completedAtUtc = _clock.UtcNow;
        LogRunFinished(results.Count, _diagnostics.Count);

        IReadOnlyList<DiagnosticResult> orderedResults = results
            .OrderBy(result => StatusRank(result.Status))
            .ThenBy(result => result.Category)
            .ThenBy(result => result.DiagnosticId, StringComparer.Ordinal)
            .ThenBy(result => result.Title, StringComparer.Ordinal)
            .ToList();

        return Result.Success(new DiagnosticRunResult(startedAtUtc, completedAtUtc, orderedResults));
    }

    internal static int StatusRank(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Fail => 0,
        DiagnosticStatus.Warning => 1,
        DiagnosticStatus.NotRun => 2,
        DiagnosticStatus.Pass => 3,
        _ => 4,
    };

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Diagnostic run finished: {ResultCount} results from {DiagnosticCount} diagnostics")]
    private partial void LogRunFinished(int resultCount, int diagnosticCount);
}
