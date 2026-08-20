import { describe, expect, it } from 'vitest';
import { bridgeResponseTimeoutMs } from './actionTimeouts';

describe('bridgeResponseTimeoutMs', () => {
  it('uses one standard policy for scan and directory actions', () => {
    expect(bridgeResponseTimeoutMs('security', 'runScan')).toBe(120_000);
    expect(bridgeResponseTimeoutMs('activedirectory', 'getOverview')).toBe(120_000);
  });

  it('keeps quick local actions on the short default', () => {
    expect(bridgeResponseTimeoutMs('targets', 'list')).toBe(10_000);
  });

  it.each(['getHygiene', 'getHygieneOverview', 'listHygieneDevices', 'listClientWorkspace'])(
    'allows a full environment analysis for %s',
    (action) => {
      expect(bridgeResponseTimeoutMs('employeelifecycle', action)).toBe(180_000);
    },
  );

  it('scales package execution by the number of target depots', () => {
    expect(bridgeResponseTimeoutMs('patchmanagement', 'executePackageUpdate', {
      depotIds: ['test', 'production'],
    })).toBe(3_800_000);
  });
});
