using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Application;

/// <summary>
/// Reads installed software from the registry uninstall keys (both bitness
/// views). Deliberately not Win32_Product: enumerating that class triggers
/// MSI reconfiguration on every scan.
/// </summary>
public sealed class InstalledSoftwareReader
{
    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    private readonly IRegistryReader _registryReader;

    public InstalledSoftwareReader(IRegistryReader registryReader)
    {
        _registryReader = registryReader;
    }

    public IReadOnlyList<InstalledSoftwareEntry> ReadInstalledSoftware()
    {
        var entries = new List<InstalledSoftwareEntry>();
        foreach (string uninstallKeyPath in UninstallKeyPaths)
        {
            Result<IReadOnlyList<string>> subKeyNames = _registryReader.ReadLocalMachineSubKeyNames(uninstallKeyPath);
            if (subKeyNames.IsFailure)
            {
                continue;
            }

            foreach (string subKeyName in subKeyNames.Value)
            {
                string entryPath = $@"{uninstallKeyPath}\{subKeyName}";
                if (ReadEntry(entryPath) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries
            .DistinctBy(entry => (entry.Name, entry.Version))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private InstalledSoftwareEntry? ReadEntry(string entryPath)
    {
        string? displayName = ReadString(entryPath, "DisplayName");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        Result<object?> systemComponent = _registryReader.ReadLocalMachineValue(entryPath, "SystemComponent");
        if (systemComponent.IsSuccess && systemComponent.Value is int flag && flag == 1)
        {
            return null;
        }

        return new InstalledSoftwareEntry(
            displayName.Trim(),
            ReadString(entryPath, "DisplayVersion"),
            ReadString(entryPath, "Publisher"));
    }

    private string? ReadString(string entryPath, string valueName)
    {
        Result<object?> value = _registryReader.ReadLocalMachineValue(entryPath, valueName);
        return value.IsSuccess ? value.Value as string : null;
    }
}
