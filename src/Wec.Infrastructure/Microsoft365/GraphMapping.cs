using Microsoft.Graph.Models;
using Wec.Core.Microsoft365;

namespace Wec.Infrastructure.Microsoft365;

internal static class GraphMapping
{
    internal static Microsoft365User User(User user) => new(user.Id, user.DisplayName,
        user.UserPrincipalName, user.Mail, user.AccountEnabled, user.UserType, user.Department,
        user.JobTitle, user.OfficeLocation, user.CreatedDateTime, user.OnPremisesSecurityIdentifier,
        user.OnPremisesImmutableId, user.AssignedLicenses?.Select(license =>
            new Microsoft365AssignedLicense(license.SkuId?.ToString(), license.DisabledPlans?.Select(id => id?.ToString() ?? "Unknown plan ID").ToArray())).ToArray());

    internal static Microsoft365Group Group(Group group) => new(group.Id, group.DisplayName,
        group.SecurityEnabled, group.MailEnabled, group.GroupTypes, group.MembershipRule,
        group.MembershipRuleProcessingState, group.Visibility);

    internal static Microsoft365Device Device(Device device) => new(device.Id, device.DeviceId,
        device.DisplayName, device.OperatingSystem, device.OperatingSystemVersion, device.TrustType,
        device.AccountEnabled, device.ApproximateLastSignInDateTime);

    internal static Microsoft365ManagedDevice ManagedDevice(ManagedDevice device) => new(device.Id,
        device.DeviceName, device.UserId, device.UserPrincipalName, device.OperatingSystem, device.OsVersion,
        device.ComplianceState?.ToString(), device.ManagementState?.ToString(), device.DeviceEnrollmentType?.ToString(),
        device.LastSyncDateTime, device.Manufacturer, device.Model, device.SerialNumber, device.AzureADDeviceId);

    internal static Microsoft365License License(SubscribedSku sku) => new(sku.Id, sku.SkuId?.ToString(),
        sku.SkuPartNumber, sku.CapabilityStatus, sku.AppliesTo, sku.PrepaidUnits?.Enabled, sku.ConsumedUnits,
        sku.ServicePlans?.Select(plan => new Microsoft365ServicePlan(plan.ServicePlanId?.ToString(),
            plan.ServicePlanName, plan.ProvisioningStatus)).ToArray());

    internal static Microsoft365License License(LicenseDetails license) => new(license.Id,
        license.SkuId?.ToString(), license.SkuPartNumber, null, null, null, null,
        license.ServicePlans?.Select(plan => new Microsoft365ServicePlan(plan.ServicePlanId?.ToString(),
            plan.ServicePlanName, plan.ProvisioningStatus)).ToArray());

    internal static Microsoft365Member Member(DirectoryObject member) => member switch
    {
        User user => new(user.Id, user.DisplayName, "user", user.UserPrincipalName),
        Group group => new(group.Id, group.DisplayName, "group", null),
        Device device => new(device.Id, device.DisplayName, "device", null),
        _ => new(member.Id, null, member.OdataType?.Replace("#microsoft.graph.", string.Empty, StringComparison.Ordinal), null),
    };
}
