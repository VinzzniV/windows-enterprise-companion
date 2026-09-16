import type { Microsoft365Resource } from '../../shared/api-types.generated';
import { objectPath, objectReference } from '../../shared/objects/objectRoutes';

export function cloudObjectDestination(resource: Microsoft365Resource, objectId?: string | null, tenantId?: string | null): string | null {
  const tenantFilter = tenantId ? `&tenant=${encodeURIComponent(tenantId)}` : '';
  if (resource === 'USERS') return '/users/workspace?source=ENTRA' + tenantFilter;
  if (resource === 'GROUPS') return '/groups/workspace?source=ENTRA' + tenantFilter;
  if (resource === 'DEVICES') return '/devices?source=ENTRA' + tenantFilter;
  if (resource === 'MANAGED_DEVICES') return '/devices?source=INTUNE' + tenantFilter;
  if (resource === 'LICENSES') return `/software?section=licenses${tenantId ? `&tenant=${encodeURIComponent(tenantId)}` : ''}`;
  if (resource === 'TENANT') return '/sources?source=microsoft365';
  const kind = resource === 'GROUP' || resource === 'GROUP_MEMBERS' ? 'GROUP'
    : resource === 'DEVICE' || resource === 'DEVICE_OWNERS' || resource === 'MANAGED_DEVICE' ? 'DEVICE'
      : resource === 'USER' || ['USER_LICENSES', 'USER_GROUPS', 'USER_DEVICES', 'USER_ACTIVITY', 'USER_REGISTRATION'].includes(resource) ? 'USER' : null;
  const reference = kind ? objectReference(kind, resource === 'MANAGED_DEVICE' ? 'intune' : 'entra', tenantId ?? undefined, objectId ?? undefined) : null;
  if (!reference) return null;
  const section = resource === 'USER_LICENSES' ? 'licenses' : resource === 'USER_GROUPS' ? 'access' : resource === 'USER_DEVICES' ? 'devices'
    : resource === 'USER_ACTIVITY' || resource === 'USER_REGISTRATION' ? 'activity' : resource === 'DEVICE_OWNERS' ? 'relationships' : null;
  return objectPath(reference) + (section ? `?section=${section}` : '');
}
