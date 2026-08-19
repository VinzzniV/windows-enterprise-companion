import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { HardwareInfoResult } from '../../shared/api-types';
import { formatLinkSpeed, HardwareInfoPage } from './HardwareInfoPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const snapshotResult: HardwareInfoResult = {
  host: 'TEST-PC',
  capturedAtUtc: '2026-08-19T08:00:00Z',
  fromCache: false,
  snapshot: {
    cpu: { name: 'Test CPU', physicalCores: 4, logicalProcessors: 8, maxClockSpeedMhz: 4000 },
    memoryBanks: [],
    disks: [],
    operatingSystem: { caption: 'Windows 11', version: '10.0', buildNumber: '26200', architecture: '64-bit' },
    networkAdapters: [],
    gpus: [],
    monitors: [],
    installedSoftware: [],
    installedSoftwareError: null,
  },
};

beforeEach(() => {
  invokeMock.mockReset();
});

describe('formatLinkSpeed', () => {
  it('formats plausible speeds', () => {
    expect(formatLinkSpeed(1_000_000_000)).toBe('1 Gbit/s');
    expect(formatLinkSpeed(2_500_000_000)).toBe('2.5 Gbit/s');
    expect(formatLinkSpeed(100_000_000)).toBe('100 Mbit/s');
  });

  it('treats WMI unknown sentinels as no value', () => {
    // Int64.MaxValue bit/s is what Win32_NetworkAdapter reports for unknown links
    expect(formatLinkSpeed(9223372036854775807)).toBe('—');
    expect(formatLinkSpeed(0)).toBe('—');
    expect(formatLinkSpeed(null)).toBe('—');
  });
});

describe('HardwareInfoPage refresh', () => {
  it('keeps the previous snapshot visible while refresh runs and after it fails', async () => {
    let inventoryCalls = 0;
    let rejectRefresh!: (reason: Error) => void;
    const pendingRefresh = new Promise<HardwareInfoResult>((_resolve, reject) => {
      rejectRefresh = reject;
    });
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'getHardwareInfo') {
        inventoryCalls += 1;
        return inventoryCalls === 1 ? Promise.resolve(snapshotResult) : pendingRefresh;
      }
      if (action === 'getDiskEncryptionStatus') return Promise.resolve({ volumes: [] });
      if (action === 'listHosts') return Promise.resolve({ hosts: [] });
      if (action === 'getAppInfo') return Promise.reject(new Error('not needed'));
      return Promise.resolve({});
    });

    render(<HardwareInfoPage />);

    expect(await screen.findByText('Test CPU')).toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }));

    expect(screen.getByText('Test CPU')).toBeDefined();
    expect(screen.getByText('Refreshing — previous snapshot remains visible')).toBeDefined();

    rejectRefresh(new Error('provider unavailable'));
    expect(await screen.findByText(/Refresh failed; the previous snapshot is still shown/)).toBeDefined();
    expect(screen.getByText('Test CPU')).toBeDefined();
  });
});
