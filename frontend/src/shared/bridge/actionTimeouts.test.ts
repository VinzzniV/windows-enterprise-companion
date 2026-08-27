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

  it('allows a print-server scan to reach its 30-second backend limit', () => {
    expect(bridgeResponseTimeoutMs('printmanagement', 'scanServer')).toBe(35_000);
  });

  it.each(['getHygiene', 'getHygieneOverview', 'listHygieneDevices', 'listClientWorkspace'])(
    'allows a full environment analysis for %s',
    (action) => {
      expect(bridgeResponseTimeoutMs('employeelifecycle', action)).toBe(180_000);
    },
  );

  it('scales Winget package execution by the number of selected packages', () => {
    expect(bridgeResponseTimeoutMs('patchmanagement', 'applyWingetUpdates', {
      packages: [{ opsiProductId: '7zip' }, { opsiProductId: 'firefox' }],
    })).toBe(3_800_000);
    expect(bridgeResponseTimeoutMs('patchmanagement', 'createOrAdoptWingetPackage')).toBe(1_900_000);
  });

  it('allows Winget catalog refreshes to finish without using package-build timeouts', () => {
    expect(bridgeResponseTimeoutMs('patchmanagement', 'searchWingetPackages')).toBe(210_000);
    expect(bridgeResponseTimeoutMs('patchmanagement', 'previewWingetPackage')).toBe(210_000);
    expect(bridgeResponseTimeoutMs('patchmanagement', 'checkWingetUpdates')).toBe(600_000);
  });

  it.each([
    ['inventory', 'runBatchScan'],
    ['security', 'runBatchScan'],
    ['diagnostics', 'runBatchDiagnostics'],
  ])('allows the %s batch operation to finish', (module, action) => {
    expect(bridgeResponseTimeoutMs(module, action)).toBe(600_000);
  });
});
