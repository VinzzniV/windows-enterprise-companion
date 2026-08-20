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
    private static readonly TimeSpan StandardOperationTimeout = TimeSpan.FromSeconds(115);
    private static readonly TimeSpan EnvironmentAnalysisTimeout = TimeSpan.FromSeconds(175);
    private static readonly TimeSpan BatchOperationTimeout = TimeSpan.FromSeconds(590);
    private static readonly TimeSpan LogReadTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PackageTargetTimeout = TimeSpan.FromSeconds(1_890);

    private static readonly HashSet<string> StandardOperations =
    [
        "activedirectory/getHygiene",
        "activedirectory/getOverview",
        "activedirectory/searchComputers",
        "activedirectory/searchUsers",
        "activedirectory/testConnection",
        "connectivity/probeHosts",
        "diagnostics/queryEventLog",
        "diagnostics/runDiagnostics",
        "inventory/getDiskEncryptionStatus",
        "inventory/getHardwareInfo",
        "patchmanagement/checkVendorVersions",
        "patchmanagement/getDashboard",
        "patchmanagement/getRolloutPreview",
        "patchmanagement/requestRollout",
        "printmanagement/scanClientPrinters",
        "reporting/exportHtml",
        "reporting/exportJson",
        "security/runScan",
        "system/restartElevated",
        "vulnerabilitymanagement/saveCredential",
    ];

    private static readonly HashSet<string> BatchOperations =
    [
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
        if (key == "patchmanagement/executePackageUpdate")
        {
            return TimeSpan.FromTicks(PackageTargetTimeout.Ticks * ReadTargetCount(request.Payload));
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

    private static int ReadTargetCount(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("depotIds", out JsonElement depotIds)
            || depotIds.ValueKind != JsonValueKind.Array)
        {
            return 1;
        }

        return Math.Max(1, depotIds.GetArrayLength());
    }
}
