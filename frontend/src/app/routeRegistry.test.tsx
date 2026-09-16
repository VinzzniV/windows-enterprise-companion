import { describe, expect, it } from 'vitest';
import { appRoutes, navigationGroups, sectionLabelFor } from './routeRegistry';

describe('routeRegistry', () => {
  it('keeps every route and navigation destination unique', () => {
    expect(new Set(appRoutes.map((route) => route.id)).size).toBe(appRoutes.length);
    expect(new Set(appRoutes.map((route) => route.path)).size).toBe(appRoutes.length);

    const navigationItems = navigationGroups.flatMap((group) => group.items);
    expect(new Set(navigationItems.map((item) => item.to)).size).toBe(navigationItems.length);
    expect(navigationItems.map((item) => item.label)).toEqual([
      'Overview',
      'Action Center',
      'Devices',
      'Users',
      'Groups',
      'Software & licenses',
      'Vulnerabilities',
      'Print Management',
      'Network Scan',
      'Reports',
      'Data sources',
      'Settings',
      'Error log',
    ]);
  });

  it('keeps detail and legacy routes hidden while resolving their section labels', () => {
    const navigationPaths = navigationGroups.flatMap((group) => group.items.map((item) => item.to));
    expect(navigationPaths).not.toContain('/clients/:host');
    expect(navigationPaths).not.toContain('/clients/compare');
    expect(navigationPaths).not.toContain('/users/:objectId');
    expect(navigationPaths).not.toContain('/employeelifecycle');
    expect(sectionLabelFor('/clients/PC-42')).toBe('Devices');
    expect(sectionLabelFor('/clients/compare')).toBe('Devices');
    expect(sectionLabelFor('/cleanup')).toBe('Devices');
    expect(sectionLabelFor('/microsoft365')).toBe('Data sources');
    expect(sectionLabelFor('/patchmanagement')).toBe('Software & licenses');
    expect(navigationGroups.map(group => group.label)).toEqual(['Work', 'Objects', 'Operations', 'Administration']);
    expect(sectionLabelFor('/users/00112233-4455-6677-8899-aabbccddeeff')).toBe('Users');
    expect(sectionLabelFor('/unregistered')).toBe('Overview');
  });
});
