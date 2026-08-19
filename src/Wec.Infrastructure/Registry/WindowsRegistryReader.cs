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
    private const long ErrorSuccess = 0;
    private const long ErrorInvalidFunction = 1;
    private const long ErrorFileNotFound = 2;
    private const long ErrorPathNotFound = 3;
    private const long ErrorAccessDenied = 5;
    private const long ErrorInvalidData = 13;
    private const long WbemErrorTypeMismatch = 0x80041005;

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

        long? dwordReturnCode = dword.Value.GetInteger("ReturnValue");
        if (dwordReturnCode == ErrorSuccess)
        {
            if (dword.Value.GetInteger("uValue") is long dwordValue)
            {
                // Registry DWORDs are 32-bit; return int so callers match the native reader's type.
                return Result.Success<object?>(unchecked((int)dwordValue));
            }

            return MalformedValueResult<object?>("GetDWORDValue", subKeyPath, valueName);
        }

        if (IsMissingValue(dwordReturnCode))
        {
            return Result.Success<object?>(null);
        }

        if (!IsTypeMismatch(dwordReturnCode))
        {
            return RegistryFailure<object?>("GetDWORDValue", subKeyPath, valueName, dwordReturnCode);
        }

        Result<WmiInstance> stringValue = await _wmiQueryService.InvokeMethodAsync(
            target, credentials, connection, StdRegProvNamespace, StdRegProvClass, "GetStringValue", inputs, cancellationToken);
        if (stringValue.IsFailure)
        {
            return Result.Failure<object?>(stringValue.Error!);
        }

        long? stringReturnCode = stringValue.Value.GetInteger("ReturnValue");
        if (stringReturnCode == ErrorSuccess)
        {
            if (stringValue.Value.GetRawValue("sValue") is string singleString)
            {
                return Result.Success<object?>(singleString);
            }

            return MalformedValueResult<object?>("GetStringValue", subKeyPath, valueName);
        }

        if (IsMissingValue(stringReturnCode))
        {
            return Result.Success<object?>(null);
        }

        if (!IsTypeMismatch(stringReturnCode))
        {
            return RegistryFailure<object?>("GetStringValue", subKeyPath, valueName, stringReturnCode);
        }

        Result<WmiInstance> multiString = await _wmiQueryService.InvokeMethodAsync(
            target, credentials, connection, StdRegProvNamespace, StdRegProvClass, "GetMultiStringValue", inputs, cancellationToken);
        if (multiString.IsFailure)
        {
            return Result.Failure<object?>(multiString.Error!);
        }

        long? multiStringReturnCode = multiString.Value.GetInteger("ReturnValue");
        if (multiStringReturnCode == ErrorSuccess)
        {
            if (multiString.Value.GetRawValue("sValue") is string[] strings)
            {
                return Result.Success<object?>(strings);
            }

            return MalformedValueResult<object?>("GetMultiStringValue", subKeyPath, valueName);
        }

        if (IsMissingValue(multiStringReturnCode))
        {
            return Result.Success<object?>(null);
        }

        if (IsTypeMismatch(multiStringReturnCode))
        {
            return Result.Failure<object?>(Error.WmiUnavailable(
                $"Registry value 'HKLM\\{subKeyPath}\\{valueName}' has an unsupported value type."));
        }

        return RegistryFailure<object?>("GetMultiStringValue", subKeyPath, valueName, multiStringReturnCode);
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

        long? returnCode = result.Value.GetInteger("ReturnValue");
        if (returnCode == ErrorSuccess)
        {
            object? rawNames = result.Value.GetRawValue("sNames");
            if (rawNames is null)
            {
                return Result.Success<IReadOnlyList<string>>([]);
            }

            if (rawNames is string[] names)
            {
                return Result.Success<IReadOnlyList<string>>(names);
            }

            return Result.Failure<IReadOnlyList<string>>(Error.WmiUnavailable(
                $"StdRegProv.EnumKey returned malformed data for registry key 'HKLM\\{subKeyPath}'."));
        }

        if (IsMissingKey(returnCode))
        {
            return Result.Success<IReadOnlyList<string>>([]);
        }

        return RegistryFailure<IReadOnlyList<string>>("EnumKey", subKeyPath, valueName: null, returnCode);
    }

    // Real StdRegProv calls return ERROR_INVALID_FUNCTION for a missing named
    // value from the DWORD/String getters and ERROR_FILE_NOT_FOUND from other
    // getters. Both represent controlled absence, not successful observation.
    private static bool IsMissingValue(long? returnCode) =>
        returnCode is ErrorInvalidFunction or ErrorFileNotFound or ErrorPathNotFound;

    private static bool IsMissingKey(long? returnCode) =>
        returnCode is ErrorFileNotFound or ErrorPathNotFound;

    // Probing a value with a getter for another registry kind returns either
    // WBEM_E_TYPE_MISMATCH (observed with CIM) or ERROR_INVALID_DATA. Only these
    // expected outcomes may advance; every other non-zero result is a failure.
    private static bool IsTypeMismatch(long? returnCode) =>
        returnCode is ErrorInvalidData or WbemErrorTypeMismatch;

    private static Result<T> MalformedValueResult<T>(string methodName, string subKeyPath, string valueName) =>
        Result.Failure<T>(Error.WmiUnavailable(
            $"StdRegProv.{methodName} returned success without a value for registry entry "
            + $"'HKLM\\{subKeyPath}\\{valueName}'."));

    private static Result<T> RegistryFailure<T>(
        string methodName,
        string subKeyPath,
        string? valueName,
        long? returnCode)
    {
        string subject = valueName is null
            ? $"registry key 'HKLM\\{subKeyPath}'"
            : $"registry value 'HKLM\\{subKeyPath}\\{valueName}'";

        if (returnCode == ErrorAccessDenied)
        {
            return Result.Failure<T>(Error.AccessDenied(
                $"Access to {subject} was denied by StdRegProv.{methodName}.",
                PrivilegeLevel.Administrator));
        }

        string code = returnCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing";
        return Result.Failure<T>(Error.WmiUnavailable(
            $"StdRegProv.{methodName} could not read {subject} (return code: {code})."));
    }
}
