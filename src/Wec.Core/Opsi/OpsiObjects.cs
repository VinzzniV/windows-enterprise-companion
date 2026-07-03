namespace Wec.Core.Opsi;

public sealed record OpsiServerInfo(string? OpsiVersion);

/// <summary>Depots double as locations; the configserver is itself a depot.</summary>
public sealed record OpsiDepot(string Id, string? Description, bool IsConfigServer);

/// <summary>
/// <paramref name="DepotId"/> is null when the client uses the default depot
/// (no explicit <c>clientconfig.depot.id</c> config state).
/// </summary>
public sealed record OpsiClientHost(
    string Id,
    string? Description,
    string? DepotId,
    DateTimeOffset? LastSeen);

public sealed record OpsiProduct(
    string Id,
    string? Name,
    string ProductVersion,
    string PackageVersion,
    string? Description);

public sealed record OpsiProductOnDepot(
    string ProductId,
    string DepotId,
    string ProductVersion,
    string PackageVersion);

public sealed record OpsiProductOnClient(
    string ProductId,
    string ClientId,
    string? InstallationStatus,
    string? ActionRequest,
    string? ActionResult,
    string? InstalledProductVersion,
    string? InstalledPackageVersion,
    DateTimeOffset? ModificationTime);
