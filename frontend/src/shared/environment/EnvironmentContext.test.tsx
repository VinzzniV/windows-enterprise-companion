import { useEffect } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ItHygieneResult } from '../api-types';
import { TargetProvider } from '../targets/TargetContext';
import { EnvironmentProvider, useEnvironment } from './EnvironmentContext';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const result: ItHygieneResult = {
  assessedAtUtc: '2026-08-18T06:00:00Z',
  domainName: 'example.test',
  sources: {
    activeDirectory: { availability: 'AVAILABLE', error: null },
    kaspersky: { availability: 'AVAILABLE', error: null },
    opsi: { availability: 'NOT_CONNECTED', error: 'No session' },
    nessus: { availability: 'NOT_CONNECTED', error: 'No API keys' },
  },
  summary: {
    total: 0, adComputers: 0, kasperskyComputers: 0, opsiComputers: 0, nessusComputers: 0,
    healthy: 0, problems: 0, incomplete: 0, stale: 0,
    missingKaspersky: 0, orphanKaspersky: 0, missingOpsi: 0, orphanOpsi: 0, outdated: 0, missingNessus: 0, staleNessus: 0, nessusCritical: 0, nessusHigh: 0,
  },
  devices: [],
};

function Consumer({ name }: { name: string }) {
  const environment = useEnvironment();
  useEffect(() => { void environment.ensureLoaded(); }, [environment.ensureLoaded]);
  return <span>{name}:{environment.result ? 'loaded' : 'waiting'}</span>;
}

describe('EnvironmentProvider', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((module: string, action: string) => {
      if (module === 'targets' && action === 'list') return Promise.resolve({ targets: [] });
      if (module === 'employeelifecycle' && action === 'getHygiene') return Promise.resolve(result);
      return Promise.reject(new Error(`Unexpected action ${module}/${action}`));
    });
  });

  it('deduplicates concurrent loads for multiple consumers', async () => {
    render(
      <TargetProvider>
        <EnvironmentProvider>
          <Consumer name="clients" />
          <Consumer name="lifecycle" />
        </EnvironmentProvider>
      </TargetProvider>,
    );

    expect(await screen.findByText('clients:loaded')).toBeTruthy();
    expect(screen.getByText('lifecycle:loaded')).toBeTruthy();
    await waitFor(() => expect(invokeMock.mock.calls.filter(
      (call) => call[0] === 'employeelifecycle' && call[1] === 'getHygiene',
    )).toHaveLength(1));
  });
});
