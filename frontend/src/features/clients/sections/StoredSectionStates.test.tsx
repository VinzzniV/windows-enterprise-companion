import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  BridgeInvokeError,
  BridgeTimeoutError,
  BridgeUnavailableError,
} from '../../../shared/bridge/bridgeClient';
import { HealthSection } from './HealthSection';
import { InventorySection } from './InventorySection';
import { SecuritySection } from './SecuritySection';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../../shared/bridge/bridgeClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../shared/bridge/bridgeClient')>()),
  invoke: invokeMock,
}));

const sections = [
  {
    name: 'inventory',
    readAction: 'getHardwareInfo',
    runAction: 'getHardwareInfo',
    render: () => render(<InventorySection target={{ host: 'PC-05' }} />),
    missingResult: () => Promise.reject(new BridgeInvokeError({ code: 'NOT_FOUND', message: 'No snapshot.' })),
    missingAction: 'Run inventory scan',
    readError: 'The stored hardware snapshot could not be loaded.',
    reloadAction: 'Reload stored inventory',
  },
  {
    name: 'security',
    readAction: 'getLatestScan',
    runAction: 'runScan',
    render: () => render(<SecuritySection target={{ host: 'PC-05' }} />),
    missingResult: () => Promise.resolve({ scan: null }),
    missingAction: 'Run security scan',
    readError: 'The stored security scan could not be loaded.',
    reloadAction: 'Reload stored security scan',
  },
  {
    name: 'health',
    readAction: 'getLatestDiagnostics',
    runAction: 'runDiagnostics',
    render: () => render(<HealthSection target={{ host: 'PC-05' }} />),
    missingResult: () => Promise.resolve({ run: null }),
    missingAction: 'Run health check',
    readError: 'The stored health snapshot could not be loaded.',
    reloadAction: 'Reload stored health snapshot',
  },
] as const;

const readFailures = [
  ['timeout', () => new BridgeTimeoutError('test', 'read', 1)],
  ['bridge unavailable', () => new BridgeUnavailableError()],
  ['database failure', () => new BridgeInvokeError({ code: 'INTERNAL_ERROR', message: 'SQLite read failed.' })],
  ['damaged payload', () => new BridgeInvokeError({ code: 'STORED_DATA_UNREADABLE', message: 'Stored payload is unreadable.' })],
] as const;

for (const section of sections) {
describe(`${section.name} stored state`, () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('shows the empty state only for a proven missing record', async () => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === section.readAction ? section.missingResult() : Promise.reject(new Error(`Unexpected ${action}`)));

    section.render();

    expect(await screen.findByRole('button', { name: section.missingAction })).toBeDefined();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it.each(readFailures)('keeps a %s distinct from missing data', async (_name, createError) => {
    invokeMock.mockImplementation((_module: string, action: string) =>
      action === section.readAction ? Promise.reject(createError()) : Promise.reject(new Error(`Unexpected ${action}`)));

    section.render();

    expect(await screen.findByText(section.readError)).toBeDefined();
    expect(screen.getByRole('button', { name: section.reloadAction })).toBeDefined();
    expect(screen.queryByRole('button', { name: section.missingAction })).toBeNull();
  });

  it('reloads only the stored read after a failure', async () => {
    invokeMock.mockRejectedValue(new BridgeInvokeError({ code: 'INTERNAL_ERROR', message: 'SQLite read failed.' }));
    section.render();

    await userEvent.click(await screen.findByRole('button', { name: section.reloadAction }));

    expect(invokeMock).toHaveBeenCalledTimes(2);
    expect(invokeMock.mock.calls.every((call) => call[1] === section.readAction)).toBe(true);
    if (section.runAction !== section.readAction) {
      expect(invokeMock).not.toHaveBeenCalledWith(expect.anything(), section.runAction, expect.anything());
    } else {
      expect(invokeMock.mock.calls.every((call) => call[2]?.cacheOnly === true)).toBe(true);
    }
  });
});
}
