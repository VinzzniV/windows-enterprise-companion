using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

/// <summary>
/// Reads the registry uninstall keys of a remote machine through the WMI
/// StdRegProv provider (read-only; deliberately not Win32_Product). Needs an
/// account with remote WMI/registry rights on the target — failures surface
/// as structured errors, never as a silently empty software list.
/// </summary>
public sealed class RemoteInstalledSoftwareReader
{
    private const string RegistryNamespace = @"root\default";
    private const string StdRegProvClass = "StdRegProv";
    private const uint HkeyLocalMachine = 0x80000002;
    private const long RegSuccess = 0;
    private const long RegFileNotFound = 2;
    private const long RegAccessDenied = 5;

    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    private readonly IWmiQueryService _wmiQueryService;

    public RemoteInstalledSoftwareReader(IWmiQueryService wmiQueryService)
    {
        _wmiQueryService = wmiQueryService;
    }

    public async Task<Result<IReadOnlyList<InstalledSoftwareEntry>>> ReadAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        CancellationToken cancellationToken)
    {
        var entries = new List<InstalledSoftwareEntry>();
        var readableKeyCount = 0;
        foreach (string uninstallKeyPath in UninstallKeyPaths)
        {
            Result<WmiInstance> subKeys = await InvokeAsync(
                target, credentials, connection, "EnumKey",
                new Dictionary<string, object?>
                {
                    ["hDefKey"] = HkeyLocalMachine,
                    ["sSubKeyName"] = uninstallKeyPath,
                },
                cancellationToken);
            if (subKeys.IsFailure)
            {
                return Result.Failure<IReadOnlyList<InstalledSoftwareEntry>>(subKeys.Error!);
            }

            long enumStatus = subKeys.Value.GetInteger("ReturnValue") ?? -1;
            if (enumStatus == RegFileNotFound)
            {
                // No WOW6432Node view on 32-bit targets — a valid absence
                continue;
            }

            if (enumStatus != RegSuccess)
            {
                return Result.Failure<IReadOnlyList<InstalledSoftwareEntry>>(MapRegistryStatus(
                    enumStatus, uninstallKeyPath, target.DisplayName));
            }

            readableKeyCount++;
            string[] subKeyNames = subKeys.Value.GetRawValue("sNames") as string[] ?? [];
            foreach (string subKeyName in subKeyNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entryPath = $@"{uninstallKeyPath}\{subKeyName}";
                Result<InstalledSoftwareEntry?> entry = await ReadEntryAsync(
                    target, credentials, connection, entryPath, cancellationToken);
                if (entry.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<InstalledSoftwareEntry>>(entry.Error!);
                }

                if (entry.Value is not null)
                {
                    entries.Add(entry.Value);
                }
            }
        }

        if (readableKeyCount == 0)
        {
            return Result.Failure<IReadOnlyList<InstalledSoftwareEntry>>(new Error(
                ErrorCode.NotFound,
                $"No registry uninstall key was readable on '{target.DisplayName}'."));
        }

        return Result.Success<IReadOnlyList<InstalledSoftwareEntry>>(entries
            .DistinctBy(entry => (entry.Name, entry.Version))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private async Task<Result<InstalledSoftwareEntry?>> ReadEntryAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string entryPath,
        CancellationToken cancellationToken)
    {
        // ponytail: no SystemComponent filter remotely — it would double the
        // WinRM round trips per entry; slight noise beats twice the scan time
        Result<string?> displayName = await ReadStringValueAsync(
            target, credentials, connection, entryPath, "DisplayName", cancellationToken);
        if (displayName.IsFailure)
        {
            return Result.Failure<InstalledSoftwareEntry?>(displayName.Error!);
        }

        if (string.IsNullOrWhiteSpace(displayName.Value))
        {
            return Result.Success<InstalledSoftwareEntry?>(null);
        }

        Result<string?> version = await ReadStringValueAsync(
            target, credentials, connection, entryPath, "DisplayVersion", cancellationToken);
        if (version.IsFailure)
        {
            return Result.Failure<InstalledSoftwareEntry?>(version.Error!);
        }

        Result<string?> publisher = await ReadStringValueAsync(
            target, credentials, connection, entryPath, "Publisher", cancellationToken);
        if (publisher.IsFailure)
        {
            return Result.Failure<InstalledSoftwareEntry?>(publisher.Error!);
        }

        return Result.Success<InstalledSoftwareEntry?>(new InstalledSoftwareEntry(
            displayName.Value.Trim(), version.Value, publisher.Value));
    }

    private async Task<Result<string?>> ReadStringValueAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string subKeyPath,
        string valueName,
        CancellationToken cancellationToken)
    {
        Result<WmiInstance> value = await InvokeAsync(
            target, credentials, connection, "GetStringValue",
            new Dictionary<string, object?>
            {
                ["hDefKey"] = HkeyLocalMachine,
                ["sSubKeyName"] = subKeyPath,
                ["sValueName"] = valueName,
            },
            cancellationToken);
        if (value.IsFailure)
        {
            return Result.Failure<string?>(value.Error!);
        }

        // Non-zero here just means "value not present" for this entry
        return Result.Success((value.Value.GetInteger("ReturnValue") ?? -1) == RegSuccess
            ? value.Value.GetString("sValue")
            : null);
    }

    private Task<Result<WmiInstance>> InvokeAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string methodName,
        IReadOnlyDictionary<string, object?> inputParameters,
        CancellationToken cancellationToken) =>
        _wmiQueryService.InvokeMethodAsync(
            target, credentials, connection, RegistryNamespace, StdRegProvClass,
            methodName, inputParameters, cancellationToken);

    private static Error MapRegistryStatus(long status, string keyPath, string targetDisplayName) =>
        status == RegAccessDenied
            ? new Error(
                ErrorCode.AccessDenied,
                $"Reading the registry uninstall keys on '{targetDisplayName}' was denied.")
            {
                Details = "The account needs remote WMI/registry read rights (typically local Administrators) on the target.",
            }
            : new Error(
                ErrorCode.WmiUnavailable,
                $"Reading '{keyPath}' on '{targetDisplayName}' failed with registry status {status}.");
}
