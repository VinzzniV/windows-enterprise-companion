namespace Wec.Modules.NetworkScan.Domain;

/// <summary>Best-effort device category inferred from ports and MAC vendor.</summary>
public enum DeviceKind
{
    Unknown = 0,
    Printer,
    Computer,
    NetworkDevice,
}
