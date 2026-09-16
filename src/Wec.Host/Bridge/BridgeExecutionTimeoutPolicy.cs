using System.Text.Json;
using Wec.Core.Messaging;

namespace Wec.Host.Bridge;

internal interface IBridgeExecutionTimeoutPolicy
{
    TimeSpan Resolve(BridgeRequest request);
}

/// <summary>
/// Authoritative backend lifetime for bridge work. The frontend correlation
/// guard is intentionally a few seconds longer so this timeout can return a
/// typed failure before the browser stops waiting.
/// </summary>
internal sealed class BridgeExecutionTimeoutPolicy : IBridgeExecutionTimeoutPolicy
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(9);
    private static readonly TimeSpan PrintServerScanTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StandardOperationTimeout = TimeSpan.FromSeconds(115);
    private static readonly TimeSpan WingetCatalogOperationTimeout = TimeSpan.FromSeconds(205);
    private static readonly TimeSpan EnvironmentAnalysisTimeout = TimeSpan.FromSeconds(175);
    private static readonly TimeSpan BatchOperationTimeout = TimeSpan.FromSeconds(590);
    private static readonly TimeSpan LogReadTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PackageTargetTimeout = TimeSpan.FromSeconds(1_890);

    private static readonly HashSet<string> StandardOperations =
    [
        "activedirectory/getHygiene",
        "activedirectory/getOverview",
        "activedirectory/exportCsv",
        "activedirectory/searchComputers",
        "activedirectory/searchUsers",
        "activedirectory/testConnection",
        "connectivity/probeHosts",
        "diagnostics/queryEventLog",
        "diagnostics/runDiagnostics",
        "inventory/getDiskEncryptionStatus",
        "inventory/getHardwareInfo",
        "patchmanagement/getDashboard",
        "printmanagement/scanClientPrinters",
        "reporting/exportHtml",
        "reporting/exportJson",
        "security/runScan",
        "system/restartElevated",
        "vulnerabilitymanagement/saveCredential",
    ];

    private static readonly HashSet<string> BatchOperations =
    [
        "devicecleanup/exportWorkbook",
        "diagnostics/runBatchDiagnostics",
        "inventory/runBatchScan",
        "networkscan/scan",
        "security/runBatchScan",
    ];

    private static readonly HashSet<string> EnvironmentAnalysisOperations =
    [
        "employeelifecycle/getHygiene",
        "employeelifecycle/getHygieneOverview",
        "employeelifecycle/listHygieneDevices",
        "employeelifecycle/listClientWorkspace",
    ];

    public TimeSpan Resolve(BridgeRequest request)
    {
        string key = $"{request.Module}/{request.Action}";
        if (key == "microsoft365/connect") { return TimeSpan.FromSeconds(310); }
        if (request.Module == "microsoft365") { return StandardOperationTimeout; }
        if (key == "patchmanagement/createOrAdoptWingetPackage")
        {
            return PackageTargetTimeout;
        }
        if (key is "patchmanagement/prepareWingetUpdates" or "patchmanagement/applyWingetUpdates")
        {
            return TimeSpan.FromTicks(PackageTargetTimeout.Ticks * ReadPackageCount(request.Payload));
        }

        if (key is "patchmanagement/searchWingetPackages" or "patchmanagement/previewWingetPackage")
        {
            return WingetCatalogOperationTimeout;
        }

        if (key == "patchmanagement/checkWingetUpdates")
        {
            return BatchOperationTimeout;
        }

        if (key == "printmanagement/scanServer")
        {
            return PrintServerScanTimeout;
        }

        if (EnvironmentAnalysisOperations.Contains(key))
        {
            return EnvironmentAnalysisTimeout;
        }

        if (key is "logs/recent" or "system/openPsSession")
        {
            return LogReadTimeout;
        }

        if (BatchOperations.Contains(key))
        {
            return BatchOperationTimeout;
        }

        return StandardOperations.Contains(key) ? StandardOperationTimeout : DefaultTimeout;
    }

    private static int ReadPackageCount(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("packages", out JsonElement packages)
            || packages.ValueKind != JsonValueKind.Array)
        {
            return 1;
        }

        return Math.Max(1, packages.GetArrayLength());
    }
}
