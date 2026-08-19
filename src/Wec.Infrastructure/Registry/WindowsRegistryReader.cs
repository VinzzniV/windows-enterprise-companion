using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Infrastructure.Registry;

public sealed class WindowsRegistryReader : IRegistryReader
{
    // StdRegProv runs server-side over the same CIM/WSMan channel as every other
    // remote scan — no Remote Registry service required. root\default matches the
    // proven namespace used by RemoteInstalledSoftwareReader.
    private const string StdRegProvNamespace = @"root\default";
    private const string StdRegProvClass = "StdRegProv";
    private const uint HklmRoot = 0x80000002; // HKEY_LOCAL_MACHINE

    private readonly IWmiQueryService _wmiQueryService;
    private readonly ILogger<WindowsRegistryReader> _logger;

    public WindowsRegistryReader(IWmiQueryService wmiQueryService, ILogger<WindowsRegistryReader> logger)
    {
        _wmiQueryService = wmiQueryService;
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

    public Result<IReadOnlyList<string>> ReadLocalMachineSubKeyNames(string subKeyPath)
    {
        try
        {
            using RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKeyPath);
            return Result.Success<IReadOnlyList<string>>(key?.GetSubKeyNames() ?? []);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Registry read access denied: HKLM\\{SubKeyPath}", subKeyPath);
            return Result.Failure<IReadOnlyList<string>>(Error.AccessDenied(
                $"Access to registry key 'HKLM\\{subKeyPath}' was denied.",
                PrivilegeLevel.Administrator));
        }
    }

    public Task<Result<object?>> ReadLocalMachineValueAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken) =>
        target.IsLocal
            ? Task.FromResult(ReadLocalMachineValue(subKeyPath, valueName))
            : ReadRemoteValueAsync(target, credentials, connection, subKeyPath, valueName, cancellationToken);

    public Task<Result<IReadOnlyList<string>>> ReadLocalMachineSubKeyNamesAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string subKeyPath,
        CancellationToken cancellationToken) =>
        target.IsLocal
            ? Task.FromResult(ReadLocalMachineSubKeyNames(subKeyPath))
            : ReadRemoteSubKeyNamesAsync(target, credentials, connection, subKeyPath, cancellationToken);

    private async Task<Result<object?>> ReadRemoteValueAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken)
    {
        var inputs = new Dictionary<string, object?>
        {
            ["hDefKey"] = HklmRoot,
            ["sSubKeyName"] = subKeyPath,
            ["sValueName"] = valueName,
        };

        // Probe REG_DWORD, then REG_SZ, then REG_MULTI_SZ — the value kinds the checks
        // read. Each returns success with a non-zero StdRegProv ReturnValue when the
        // value is a different kind, so the next probe is tried.
        Result<WmiInstance> dword = await _wmiQueryService.InvokeMethodAsync(
            target, credentials, connection, StdRegProvNamespace, StdRegProvClass, "GetDWORDValue", inputs, cancellationToken);
        if (dword.IsFailure)
        {
            return Result.Failure<object?>(dword.Error!);
        }

        if (Succeeded(dword.Value) && dword.Value.GetInteger("uValue") is long dwordValue)
        {
            // Registry DWORDs are 32-bit; return int so callers match the native reader's type.
            return Result.Success<object?>(unchecked((int)dwordValue));
        }

        Result<WmiInstance> stringValue = await _wmiQueryService.InvokeMethodAsync(
            target, credentials, connection, StdRegProvNamespace, StdRegProvClass, "GetStringValue", inputs, cancellationToken);
        if (stringValue.IsFailure)
        {
            return Result.Failure<object?>(stringValue.Error!);
        }

        if (Succeeded(stringValue.Value) && stringValue.Value.GetRawValue("sValue") is string singleString)
        {
            return Result.Success<object?>(singleString);
        }

        Result<WmiInstance> multiString = await _wmiQueryService.InvokeMethodAsync(
            target, credentials, connection, StdRegProvNamespace, StdRegProvClass, "GetMultiStringValue", inputs, cancellationToken);
        if (multiString.IsFailure)
        {
            return Result.Failure<object?>(multiString.Error!);
        }

        if (Succeeded(multiString.Value) && multiString.Value.GetRawValue("sValue") is string[] strings)
        {
            return Result.Success<object?>(strings);
        }

        // Value is absent or of an unsupported kind — treated as missing, like the native reader.
        return Result.Success<object?>(null);
    }

    private async Task<Result<IReadOnlyList<string>>> ReadRemoteSubKeyNamesAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string subKeyPath,
        CancellationToken cancellationToken)
    {
        var inputs = new Dictionary<string, object?>
        {
            ["hDefKey"] = HklmRoot,
            ["sSubKeyName"] = subKeyPath,
        };

        Result<WmiInstance> result = await _wmiQueryService.InvokeMethodAsync(
            target, credentials, connection, StdRegProvNamespace, StdRegProvClass, "EnumKey", inputs, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(result.Error!);
        }

        if (Succeeded(result.Value) && result.Value.GetRawValue("sNames") is string[] names)
        {
            return Result.Success<IReadOnlyList<string>>(names);
        }

        // Key does not exist (ReturnValue != 0) — empty, like the native reader.
        return Result.Success<IReadOnlyList<string>>([]);
    }

    // StdRegProv returns 0 on success; anything else means the key/value was not found or was inaccessible.
    private static bool Succeeded(WmiInstance methodResult) => methodResult.GetInteger("ReturnValue") == 0;
}
