import { describe, expect, it } from 'vitest';
import { objectPath, objectReference } from './objectRoutes';

const id = '11111111-1111-1111-1111-111111111111';
describe('scoped object routes', () => {
  it('keeps identical native IDs in different directories separate', () => {
    expect(objectPath(objectReference('DEVICE', 'ad', 'alpha.test', id)!)).not.toBe(objectPath(objectReference('DEVICE', 'ad', 'beta.test', id)!));
  });
  it('preserves full IPv6 targets and reserved characters as encoded segments', () => {
    expect(objectPath(objectReference('DEVICE', 'wec', 'profile', 'fe80::1%12')!)).toBe('/devices/wec/profile/fe80%3A%3A1%2512');
    expect(objectPath(objectReference('DEVICE', 'wec', 'profile', 'PC.alpha.test')!)).toContain('/PC.alpha.test');
  });
  it('rejects malformed sources, IDs, tenants and source-kind combinations', () => {
    expect(objectReference('DEVICE', 'entra', 'not-a-tenant', id)).toBeNull();
    expect(objectReference('DEVICE', 'ad', 'alpha.test', 'PC')).toBeNull();
    expect(objectReference('USER', 'intune', id, id)).toBeNull();
    expect(objectReference('DEVICE', 'unknown', id, id)).toBeNull();
    expect(objectReference('DEVICE', 'entra', id, '00000000-0000-0000-0000-000000000000')).toBeNull();
  });
});
