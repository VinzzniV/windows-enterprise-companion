using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Application;

/// <summary>
/// One read-only diagnostic. Expected operational failures must surface as
/// results with status WARNING, FAIL or NOT_RUN — never swallowed.
/// Exceptions escaping a diagnostic are bugs.
/// </summary>
public interface IDiagnostic
{
    string DiagnosticId { get; }

    Task<IReadOnlyList<DiagnosticResult>> EvaluateAsync(CancellationToken cancellationToken);
}
