import { describe, expect, it } from 'vitest';
import { cloudObjectDestination } from './cloudObjectDestination';

const tenant = '11111111-1111-1111-1111-111111111111';
const id = '22222222-2222-2222-2222-222222222222';
describe('cloud object destinations', () => {
  it('retains native IDs, tenant and focused relationship sections', () => {
    expect(cloudObjectDestination('USER_LICENSES', id, tenant)).toBe(`/users/entra/${tenant}/${id}?section=licenses`);
    expect(cloudObjectDestination('USER_REGISTRATION', id, tenant)).toBe(`/users/entra/${tenant}/${id}?section=activity`);
    expect(cloudObjectDestination('GROUP_MEMBERS', id, tenant)).toBe(`/groups/entra/${tenant}/${id}`);
    expect(cloudObjectDestination('DEVICE_OWNERS', id, tenant)).toBe(`/devices/entra/${tenant}/${id}?section=relationships`);
    expect(cloudObjectDestination('MANAGED_DEVICE', id, tenant)).toBe(`/devices/intune/${tenant}/${id}`);
  });
  it('never infers a tenant or replaces a missing native identity with a name', () => {
    expect(cloudObjectDestination('USER', id, null)).toBeNull();
    expect(cloudObjectDestination('USER', 'alex@example.test', tenant)).toBeNull();
    expect(cloudObjectDestination('USERS_BY_SID', id, tenant)).toBeNull();
    expect(cloudObjectDestination('DEVICES', null, tenant)).toBe(`/devices?source=ENTRA&tenant=${tenant}`);
    expect(cloudObjectDestination('LICENSES', null, tenant)).toBe(`/software?section=licenses&tenant=${tenant}`);
  });
});
