using Wec.Core.Targets;

namespace Wec.Modules.Security.Domain;

public enum HostScanStatus
{
    Queued = 0,
    Connecting,
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
}

public sealed record HostScanOutcome(
    string Host,
    HostScanStatus Status,
    SecurityScanResult? Scan,
    ScanError? Error);

public sealed record BatchScanResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<HostScanOutcome> Hosts);
