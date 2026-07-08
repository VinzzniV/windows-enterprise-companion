using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Abstractions;

/// <summary>
/// Read-only access to HKEY_LOCAL_MACHINE. Deliberately minimal: no writes,
/// no other hives, so modules cannot grow registry side effects through it.
/// The target-aware overloads read the local machine natively and remote
/// machines through StdRegProv over CIM (with the supplied credentials).
/// </summary>
public interface IRegistryReader
{
    /// <summary>Returns the value, or success with null when the key or value does not exist.</summary>
    Result<object?> ReadLocalMachineValue(string subKeyPath, string valueName);

    /// <summary>Returns the sub key names, or success with an empty list when the key does not exist.</summary>
    Result<IReadOnlyList<string>> ReadLocalMachineSubKeyNames(string subKeyPath);

    /// <summary>
    /// Target-aware value read: local uses the native registry, remote uses
    /// StdRegProv over CIM. Returns success with null when the key or value
    /// does not exist. Supports REG_DWORD (as <see cref="int"/>) and
    /// REG_MULTI_SZ (as <c>string[]</c>) — the value kinds the checks read.
    /// </summary>
    Task<Result<object?>> ReadLocalMachineValueAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Target-aware sub key enumeration: local uses the native registry, remote
    /// uses StdRegProv EnumKey over CIM. Returns success with an empty list when
    /// the key does not exist.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> ReadLocalMachineSubKeyNamesAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string subKeyPath,
        CancellationToken cancellationToken);
}
