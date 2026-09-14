import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { Microsoft365Resource, Microsoft365User, Microsoft365Device, Microsoft365ManagedDevice } from '../../shared/api-types.generated';

export const available = (value: string | number | boolean | null | undefined): string =>
  value === null || value === undefined || value === '' ? 'Not available' : typeof value === 'boolean' ? value ? 'Yes' : 'No' : String(value);
export const timestamp = (value: string | null | undefined): string => value ? new Date(value).toLocaleString() : 'Not available';
export const cloudPath = (resource: Microsoft365Resource, objectId?: string | null) =>
  `/microsoft365?resource=${resource}${objectId ? `&objectId=${encodeURIComponent(objectId)}` : ''}`;

export function CloudLink({ resource, id, children }: { resource: Microsoft365Resource; id?: string | null; children: ReactNode }) {
  return id ? <Link className="text-accent-400 underline" to={cloudPath(resource, id)}>{children}</Link> : <span>{children}</span>;
}
export function CloudFields({ fields }: { fields: [string, string | number | boolean | null | undefined][] }) {
  return <dl className="grid gap-x-4 sm:grid-cols-2 lg:grid-cols-3">{fields.map(([label, value]) =>
    <div key={label} className="min-w-0 border-b border-slate-800 py-2">
      <dt className="text-xs text-muted">{label}</dt><dd className="break-words text-sm text-slate-200">{available(value)}</dd>
    </div>)}</dl>;
}
export function CloudUserFields({ user }: { user: Microsoft365User }) {
  return <CloudFields fields={[
    ['Display name', user.displayName], ['UPN', user.userPrincipalName], ['Mail address', user.mail],
    ['Enabled', user.accountEnabled], ['Object ID', user.id], ['User type', user.userType],
    ['Department', user.department], ['Job title', user.jobTitle], ['Office', user.officeLocation],
    ['Created', timestamp(user.createdAtUtc)], ['On-premises SID', user.onPremisesSid],
    ['Immutable ID (configured sourceAnchor)', user.onPremisesImmutableId],
    ['Assigned license count', user.assignedLicenses?.length],
  ]} />;
}
export function CloudDeviceFields({ device }: { device: Microsoft365Device }) {
  return <CloudFields fields={[
    ['Name', device.displayName], ['Object ID', device.id], ['Entra device ID', device.deviceId],
    ['OS', device.operatingSystem], ['OS version', device.operatingSystemVersion], ['Trust / join type', device.trustType],
    ['Enabled', device.accountEnabled], ['Approximate last sign-in', timestamp(device.approximateLastSignInAtUtc)],
  ]} />;
}
export function CloudManagedFields({ device }: { device: Microsoft365ManagedDevice }) {
  return <CloudFields fields={[
    ['Device', device.deviceName], ['Intune object ID', device.id], ['Associated user UPN', device.userPrincipalName],
    ['Associated user ID', device.userId], ['OS', device.operatingSystem], ['OS version', device.operatingSystemVersion],
    ['Compliance', device.complianceState], ['Management state', device.managementState], ['Enrollment type', device.enrollmentType],
    ['Last Intune sync', timestamp(device.lastSyncAtUtc)], ['Manufacturer', device.manufacturer], ['Model', device.model],
    ['Serial number', device.serialNumber], ['Entra device ID', device.entraDeviceId],
  ]} />;
}
