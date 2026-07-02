using Wec.Core.Results;

namespace Wec.Core.Abstractions;

/// <summary>
/// Read-only access to HKEY_LOCAL_MACHINE. Deliberately minimal: no writes,
/// no other hives, so modules cannot grow registry side effects through it.
/// </summary>
public interface IRegistryReader
{
    /// <summary>Returns the value, or success with null when the key or value does not exist.</summary>
    Result<object?> ReadLocalMachineValue(string subKeyPath, string valueName);
}
