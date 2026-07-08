using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Persistence;

public interface IDiagnosticRunRepository
{
    Task<DiagnosticRunResult?> GetLatestAsync(string hostKey, CancellationToken cancellationToken);

    Task SaveAsync(string hostKey, DiagnosticRunResult run, CancellationToken cancellationToken);
}
