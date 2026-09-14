using Wec.Core.Microsoft365;

namespace Wec.Infrastructure.Microsoft365;

internal static class Microsoft365Scopes
{
    internal static string[] For(Microsoft365Configuration configuration) =>
    [
        "User.Read", "User.Read.All", "GroupMember.Read.All",
        "Device.Read.All", "LicenseAssignment.Read.All",
        .. configuration.EnableIntune ? new[] { "DeviceManagementManagedDevices.Read.All" } : [],
        .. configuration.EnableAuthenticationReports ? new[] { "AuditLog.Read.All" } : [],
    ];

    internal static string[] For(Microsoft365Resource resource) => resource switch
    {
        Microsoft365Resource.Tenant => ["User.Read"],
        Microsoft365Resource.Users or Microsoft365Resource.User => ["User.Read.All"],
        Microsoft365Resource.Groups or Microsoft365Resource.Group or Microsoft365Resource.GroupMembers => ["GroupMember.Read.All"],
        Microsoft365Resource.UserGroups => ["User.Read.All", "GroupMember.Read.All"],
        Microsoft365Resource.Devices or Microsoft365Resource.Device or Microsoft365Resource.DeviceOwners => ["Device.Read.All"],
        Microsoft365Resource.UserDevices => ["User.Read.All", "Device.Read.All"],
        Microsoft365Resource.ManagedDevices or Microsoft365Resource.ManagedDevice => ["DeviceManagementManagedDevices.Read.All"],
        Microsoft365Resource.Licenses or Microsoft365Resource.UserLicenses => ["LicenseAssignment.Read.All"],
        Microsoft365Resource.UserActivity => ["User.Read.All", "AuditLog.Read.All"],
        Microsoft365Resource.UserRegistration => ["AuditLog.Read.All"],
        _ => [],
    };
}
