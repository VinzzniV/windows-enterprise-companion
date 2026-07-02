namespace Wec.Core.Abstractions;

public interface IShellLauncher
{
    /// <summary>Opens a file or folder with its shell association; failures are logged, not thrown.</summary>
    bool TryOpenPath(string path);
}
