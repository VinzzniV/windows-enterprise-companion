namespace Wec.Modules.PatchManagement.Domain;

/// <summary>
/// Full intended patch lifecycle (ADR 0008). The MVP derives Detected,
/// UpdateAvailable, RolloutRequested, Completed and Failed from opsi data;
/// the remaining states are reserved for the package-preparation pipeline
/// so it can be added without a model break.
/// </summary>
public enum PatchWorkflowState
{
    Detected = 0,
    UpdateAvailable,
    DownloadNeeded,
    PackagePrepared,
    Uploaded,
    ReadyForPilot,
    Approved,
    RolloutRequested,
    Completed,
    Failed,
}
