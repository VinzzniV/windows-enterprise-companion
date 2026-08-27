using Wec.Core.Results;

namespace Wec.Core.Contracts;

public enum ActionEvidenceAvailability
{
    Available = 0,
    Partial,
    NotConnected,
    Unavailable,
    Truncated,
}

public sealed record HygieneActionDirectoryConnection(
    string? Domain = null,
    string? Server = null,
    string? UserName = null,
    string? UserDomain = null,
    string? Password = null);

public sealed record HygieneActionKasperskyConnection(
    string? Server = null,
    int? Port = null,
    string? UserName = null,
    string? Domain = null,
    string? Password = null);

public sealed record HygieneActionEvidenceQuery(
    HygieneActionDirectoryConnection? ActiveDirectory = null,
    HygieneActionKasperskyConnection? Kaspersky = null,
    string? OperationId = null,
    bool Force = false);

public sealed record ActionEvidenceSourceState(
    string Source,
    ActionEvidenceAvailability Availability,
    string? Explanation);

public sealed record HygieneActionEvidence(
    string SubjectKey,
    string Host,
    string FindingCode,
    string Severity,
    string Message,
    string Source,
    DateTimeOffset? EvidenceAtUtc,
    ActionEvidenceAvailability Coverage,
    string CoverageExplanation);

public sealed record HygieneActionEvidenceSnapshot(
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<ActionEvidenceSourceState> Sources,
    IReadOnlyList<HygieneActionEvidence> Findings);

/// <summary>
/// Action-Center-specific projection of the computed fleet hygiene snapshot.
/// Implemented by Employee Lifecycle; credentials remain request-local.
/// </summary>
public interface IHygieneActionEvidenceProvider
{
    Task<Result<HygieneActionEvidenceSnapshot>> LoadAsync(
        HygieneActionEvidenceQuery query,
        CancellationToken cancellationToken);
}
