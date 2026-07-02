using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application;

public sealed class DiagnosticRunService
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

    public async Task<Result<DiagnosticRunResult>> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAtUtc = _clock.UtcNow;
        var results = new List<DiagnosticResult>();

        foreach (IDiagnostic diagnostic in _diagnostics)
        {
            try
            {
                results.AddRange(await diagnostic.EvaluateAsync(cancellationToken));
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
        _logger.LogInformation(
            "Diagnostic run finished: {ResultCount} results from {DiagnosticCount} diagnostics",
            results.Count,
            _diagnostics.Count);

        return Result.Success(new DiagnosticRunResult(startedAtUtc, completedAtUtc, results));
    }
}
