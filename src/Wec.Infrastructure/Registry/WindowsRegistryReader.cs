using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;

namespace Wec.Infrastructure.Registry;

public sealed class WindowsRegistryReader : IRegistryReader
{
    private readonly ILogger<WindowsRegistryReader> _logger;

    public WindowsRegistryReader(ILogger<WindowsRegistryReader> logger)
    {
        _logger = logger;
    }

    public Result<object?> ReadLocalMachineValue(string subKeyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKeyPath);
            return Result.Success(key?.GetValue(valueName));
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Registry read access denied: HKLM\\{SubKeyPath}", subKeyPath);
            return Result.Failure<object?>(Error.AccessDenied(
                $"Access to registry key 'HKLM\\{subKeyPath}' was denied.",
                PrivilegeLevel.Administrator));
        }
    }
}
