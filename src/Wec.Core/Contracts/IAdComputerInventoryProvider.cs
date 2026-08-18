using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Contracts;

/// <summary>Read-only AD computer data needed by cross-system inventory views.</summary>
public sealed record AdComputerInventoryItem(
    string ComputerName,
    string? DnsHostName,
    string? OperatingSystem,
    string? Description,
    bool Enabled,
    string DistinguishedName,
    DateTimeOffset? LastLogonDate);

public sealed record AdComputerInventoryQuery(
    string? Domain,
    string? Server,
    ScanCredentials Credentials,
    int Limit);

public sealed record AdComputerInventory(
    bool DomainJoined,
    string? DomainName,
    IReadOnlyList<AdComputerInventoryItem> Computers,
    bool Truncated);

/// <summary>
/// Cross-module read contract implemented by the Active Directory module.
/// It deliberately exposes no directory write operation.
/// </summary>
public interface IAdComputerInventoryProvider
{
    Task<Result<AdComputerInventory>> LoadAsync(
        AdComputerInventoryQuery query,
        CancellationToken cancellationToken);
}
