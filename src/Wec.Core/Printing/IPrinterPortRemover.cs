using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Printing;

/// <summary>Outcome of removing one printer port; the server may refuse a port still in use.</summary>
public sealed record PortRemovalResult(string Name, bool Removed, string? Error);

/// <summary>
/// Deletes TCP/IP printer ports on a Windows print server. The single deliberate
/// write in an otherwise read-only app: only unused ports, only after an explicit
/// confirmation, and the server itself refuses a port still bound to a queue.
/// </summary>
public interface IPrinterPortRemover
{
    Task<Result<IReadOnlyList<PortRemovalResult>>> RemovePortsAsync(
        string printServer,
        IReadOnlyList<string> portNames,
        ScanCredentials credentials,
        CancellationToken cancellationToken);
}
