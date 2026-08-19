namespace Wec.Modules.Security.Domain;

public enum FindingCategory
{
    Unknown = -1,
    Firewall = 0,
    MalwareProtection,
    NetworkServices,
    Encryption,
    PlatformIntegrity,
    Accounts,
    OperatingSystem,
}
