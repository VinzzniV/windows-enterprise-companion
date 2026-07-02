using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;

namespace Wec.Infrastructure.Shell;

public sealed class ShellLauncher : IShellLauncher
{
    private readonly ILogger<ShellLauncher> _logger;

    public ShellLauncher(ILogger<ShellLauncher> logger)
    {
        _logger = logger;
    }

    public bool TryOpenPath(string path)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            _logger.LogInformation("Opened {Path} via shell", path);
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Shell open of {Path} failed", path);
            return false;
        }
    }
}
