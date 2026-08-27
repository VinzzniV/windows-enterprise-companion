using Wec.Core.Targets;

namespace Wec.Modules.Diagnostics.Domain;

public enum DiagnosticBatchHostStatus
{
    Queued = 0,
    Running,
    Completed,
    Failed,
}

public sealed record DiagnosticBatchHostOutcome(
    string Host,
    DiagnosticBatchHostStatus Status,
    DiagnosticRunResult? Run,
    ScanError? Error);

public sealed record DiagnosticBatchResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<DiagnosticBatchHostOutcome> Hosts);
