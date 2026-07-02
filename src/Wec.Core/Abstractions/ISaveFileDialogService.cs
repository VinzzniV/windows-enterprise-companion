namespace Wec.Core.Abstractions;

/// <summary>
/// Prompts the user for a save location. Implemented by the host shell (UI).
/// Returns null when the user cancels — cancellation is not an error.
/// </summary>
public interface ISaveFileDialogService
{
    string? PromptForSavePath(string suggestedFileName, string filter);
}
