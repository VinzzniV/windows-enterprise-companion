namespace Wec.Modules.ActiveDirectory.Domain;

public sealed record DomainControllerInfo(string HostName, string DistinguishedName);

/// <summary>
/// Read-only snapshot of the directory from the perspective of the current
/// machine and identity. On a workgroup machine <see cref="DomainJoined"/>
/// is false and everything else is empty — a valid result, not an error.
/// </summary>
public sealed record AdOverviewResult(
    bool DomainJoined,
    string? DomainName,
    string? DefaultNamingContext,
    IReadOnlyList<DomainControllerInfo> DomainControllers,
    int UserCount,
    int DisabledUserCount,
    int GroupCount,
    int ComputerCount,
    DateTimeOffset CapturedAtUtc);
