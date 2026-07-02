using Wec.Core.Abstractions;

namespace Wec.Host.Dialogs;

/// <summary>
/// WinForms save dialog. Bridge dispatch runs on the UI thread (WinForms
/// synchronization context), so showing a modal dialog here is safe.
/// </summary>
internal sealed class WinFormsSaveFileDialogService : ISaveFileDialogService
{
    public string? PromptForSavePath(string suggestedFileName, string filter)
    {
        using var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}
