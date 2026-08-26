namespace Wec.Modules.PatchManagement.Domain;

/// <summary>
/// Read-only client state derived from opsi product-on-client data.
/// </summary>
public enum PatchWorkflowState
{
    Detected = 0,
    UpdateAvailable,
    ActionPending,
    Completed,
    Failed,
}
