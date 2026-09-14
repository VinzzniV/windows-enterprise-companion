using Wec.Core.Results;

namespace Wec.Core.Microsoft365;

public enum Microsoft365Resource
{
    Tenant, Users, User, Groups, Group, Devices, Device, ManagedDevices,
    Licenses, UserLicenses, UserGroups, UserDevices, GroupMembers, DeviceOwners,
    UserActivity, UserRegistration,
}

public sealed record Microsoft365Configuration(string TenantId, string ClientId,
    bool EnableIntune = false, bool EnableAuthenticationReports = false);

public sealed record Microsoft365ScopeGrant(string Scope, bool Granted);
public sealed record Microsoft365Connection(Microsoft365Configuration Configuration,
    bool Connected, string? Account, IReadOnlyList<Microsoft365ScopeGrant> Permissions);
public sealed record Microsoft365Query(Microsoft365Resource Resource, string? ObjectId = null);
public sealed record Microsoft365Tenant(string? Id, string? DisplayName);
public sealed record Microsoft365AssignedLicense(string? SkuId, IReadOnlyList<string>? DisabledPlans);
public sealed record Microsoft365User(string? Id, string? DisplayName, string? UserPrincipalName,
    string? Mail, bool? AccountEnabled, string? UserType, string? Department, string? JobTitle,
    string? OfficeLocation, DateTimeOffset? CreatedAtUtc, string? OnPremisesSid,
    string? OnPremisesImmutableId, IReadOnlyList<Microsoft365AssignedLicense>? AssignedLicenses);
public sealed record Microsoft365Group(string? Id, string? DisplayName, bool? SecurityEnabled,
    bool? MailEnabled, IReadOnlyList<string>? GroupTypes, string? MembershipRule,
    string? MembershipRuleProcessingState, string? Visibility);
public sealed record Microsoft365Device(string? Id, string? DeviceId, string? DisplayName,
    string? OperatingSystem, string? OperatingSystemVersion, string? TrustType,
    bool? AccountEnabled, DateTimeOffset? ApproximateLastSignInAtUtc);
public sealed record Microsoft365ManagedDevice(string? Id, string? DeviceName, string? UserId,
    string? UserPrincipalName, string? OperatingSystem, string? OperatingSystemVersion,
    string? ComplianceState, string? ManagementState, string? EnrollmentType,
    DateTimeOffset? LastSyncAtUtc, string? Manufacturer, string? Model, string? SerialNumber,
    string? EntraDeviceId);
public sealed record Microsoft365ServicePlan(string? Id, string? Name, string? Status);
public sealed record Microsoft365License(string? Id, string? SkuId, string? SkuPartNumber,
    string? CapabilityStatus, string? AppliesTo, int? EnabledSeats, int? ConsumedSeats,
    IReadOnlyList<Microsoft365ServicePlan>? ServicePlans);
public sealed record Microsoft365Member(string? Id, string? DisplayName, string? ObjectType,
    string? UserPrincipalName);
public sealed record Microsoft365Activity(DateTimeOffset? LastSignInAtUtc,
    DateTimeOffset? LastSuccessfulSignInAtUtc, bool? MfaRegistered, bool? MfaCapable,
    IReadOnlyList<string>? MethodsRegistered);

public sealed record Microsoft365Data
{
    public IReadOnlyList<Microsoft365Tenant> Tenants { get; init; } = [];
    public IReadOnlyList<Microsoft365User> Users { get; init; } = [];
    public IReadOnlyList<Microsoft365Group> Groups { get; init; } = [];
    public IReadOnlyList<Microsoft365Device> Devices { get; init; } = [];
    public IReadOnlyList<Microsoft365ManagedDevice> ManagedDevices { get; init; } = [];
    public IReadOnlyList<Microsoft365License> Licenses { get; init; } = [];
    public IReadOnlyList<Microsoft365Member> Members { get; init; } = [];
    public Microsoft365Activity? Activity { get; init; }
    public long? TotalCount { get; init; }
    public bool Truncated { get; init; }
}

public interface IMicrosoft365Reader
{
    Microsoft365Connection Connection { get; }
    Task<Result<Microsoft365Connection>> ConnectAsync(Microsoft365Configuration configuration,
        CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<Result<Microsoft365Data>> ReadAsync(Microsoft365Query query, CancellationToken cancellationToken);
}

public interface IMicrosoft365AuthenticationWindow
{
    nint Handle { get; }
}
