using System.Globalization;
using Wec.Core.Microsoft365;

namespace Wec.Infrastructure.Microsoft365;

internal static class GraphQueries
{
    private const string UserFields = "id,displayName,userPrincipalName,mail,accountEnabled,userType,department,jobTitle,officeLocation,createdDateTime,onPremisesSecurityIdentifier,onPremisesImmutableId,assignedLicenses";
    private const string GroupFields = "id,displayName,securityEnabled,mailEnabled,groupTypes,membershipRule,membershipRuleProcessingState,visibility";
    private const string DeviceFields = "id,deviceId,displayName,operatingSystem,operatingSystemVersion,trustType,accountEnabled,approximateLastSignInDateTime";
    private const string ManagedDeviceFields = "id,deviceName,userId,userPrincipalName,operatingSystem,osVersion,complianceState,managementState,deviceEnrollmentType,lastSyncDateTime,manufacturer,model,serialNumber,azureADDeviceId";

    internal static bool NeedsObjectId(Microsoft365Resource resource) => resource is not
        (Microsoft365Resource.Tenant or Microsoft365Resource.Users or Microsoft365Resource.Groups
        or Microsoft365Resource.Devices or Microsoft365Resource.ManagedDevices or Microsoft365Resource.Licenses);

    internal static string Path(Microsoft365Query query, int pageSize)
    {
        if (!Enum.IsDefined(query.Resource) || (NeedsObjectId(query.Resource)
            && (!Guid.TryParse(query.ObjectId, out Guid id) || id == Guid.Empty)))
        {
            throw new ArgumentException("A valid Graph resource and non-empty object ID are required.", nameof(query));
        }
        string? objectId = query.ObjectId is null ? null : Guid.Parse(query.ObjectId).ToString("D");
        string page = "&$top=" + pageSize.ToString(CultureInfo.InvariantCulture);
        return query.Resource switch
        {
            Microsoft365Resource.Tenant => "organization?$select=id,displayName",
            Microsoft365Resource.Users => $"users?$select={UserFields}&$count=true{page}",
            Microsoft365Resource.User => $"users/{objectId}?$select={UserFields}",
            Microsoft365Resource.Groups => $"groups?$select={GroupFields}&$count=true{page}",
            Microsoft365Resource.Group => $"groups/{objectId}?$select={GroupFields}",
            Microsoft365Resource.Devices => $"devices?$select={DeviceFields}&$count=true{page}",
            Microsoft365Resource.Device => $"devices/{objectId}?$select={DeviceFields}",
            Microsoft365Resource.ManagedDevices => $"deviceManagement/managedDevices?$select={ManagedDeviceFields}{page}",
            Microsoft365Resource.ManagedDevice => $"deviceManagement/managedDevices/{objectId}?$select={ManagedDeviceFields}",
            Microsoft365Resource.Licenses => "subscribedSkus?$select=id,skuId,skuPartNumber,capabilityStatus,appliesTo,prepaidUnits,consumedUnits,servicePlans",
            Microsoft365Resource.UserLicenses => $"users/{objectId}/licenseDetails?$select=id,skuId,skuPartNumber,servicePlans",
            Microsoft365Resource.UserGroups => $"users/{objectId}/memberOf/microsoft.graph.group?$select={GroupFields}&$count=true{page}",
            Microsoft365Resource.UserDevices => $"users/{objectId}/registeredDevices/microsoft.graph.device?$select={DeviceFields}&$count=true{page}",
            Microsoft365Resource.GroupMembers => $"groups/{objectId}/members?$select=id,displayName{page}",
            Microsoft365Resource.DeviceOwners => $"devices/{objectId}/registeredOwners?$select=id,displayName{page}",
            Microsoft365Resource.UserActivity => $"users/{objectId}?$select=id,signInActivity",
            Microsoft365Resource.UserRegistration => $"reports/authenticationMethods/userRegistrationDetails/{objectId}",
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };
    }
}
