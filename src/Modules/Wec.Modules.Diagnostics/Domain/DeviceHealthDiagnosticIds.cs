namespace Wec.Modules.Diagnostics.Domain;

internal static class DeviceHealthDiagnosticIds
{
    public const string WindowsUpdateRecency = "WEC-DIAG-SYS-UPDATES";
    public const string ServiceStatus = "WEC-DIAG-SYS-SERVICES";
    public const string EventLogSummary = "WEC-DIAG-SYS-EVENTLOG";
    public const string DiskFreeSpace = "WEC-DIAG-SYS-DISKSPACE";

    public static bool IsCurrent(string diagnosticId) => diagnosticId is
        WindowsUpdateRecency or
        ServiceStatus or
        EventLogSummary or
        DiskFreeSpace;
}
