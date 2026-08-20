import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { HygieneDevice, InventorySourceState } from '../../shared/api-types';
import { ClientIntegrationMap, integrationStatus } from './ClientIntegrationMap';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: invokeMock }));

const available: InventorySourceState = { availability: 'AVAILABLE', error: null };
describe('ClientIntegrationMap', () => {
  beforeEach(() => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false }));
    vi.stubGlobal('PointerEvent', MouseEvent);
    HTMLElement.prototype.setPointerCapture = vi.fn();
    invokeMock.mockReset();
    invokeMock.mockResolvedValue({ results: [{ host: 'PC-42.corp.local', reachable: true, manageable: false }] });
  });
  it('maps backend source and finding states without time-based frontend logic', () => {
    expect(integrationStatus('kaspersky', available, true, new Set())).toBe('connected');
    expect(integrationStatus('kaspersky', available, true, new Set(['STALE_KASPERSKY']))).toBe('stale');
    expect(integrationStatus('opsi', available, false, new Set(['MISSING_OPSI']))).toBe('disconnected');
    expect(integrationStatus('nessus', { availability: 'PARTIAL', error: null }, true, new Set())).toBe('partial');
    expect(integrationStatus('activeDirectory', { availability: 'UNAVAILABLE', error: 'Offline' }, false, new Set())).toBe('unknown');
  });
  it('renders stable animated branches and a distinct icon for every integration', () => {
    const device = { computerName: 'PC-42', hostName: 'PC-42.corp.local', activeDirectory: { exists: true }, kaspersky: { exists: true }, opsi: { exists: false }, nessus: { exists: true }, assessment: { status: 'WARNING', findings: [{ code: 'STALE_KASPERSKY', severity: 'WARNING', message: 'Old' }, { code: 'MISSING_OPSI', severity: 'WARNING', message: 'Missing' }] } } as HygieneDevice;
    render(<ClientIntegrationMap device={device} sources={{ activeDirectory: available, kaspersky: available, opsi: available, nessus: { availability: 'PARTIAL', error: 'Incomplete sync' } }} />);
    expect(screen.getByRole('heading', { name: 'Client integration map' })).toBeDefined(); expect(screen.getByText('PC-42')).toBeDefined(); expect(screen.getByText('Active Directory')).toBeDefined(); expect(screen.getByText('Kaspersky')).toBeDefined(); expect(screen.getByText('opsi')).toBeDefined(); expect(screen.getByText('Nessus')).toBeDefined(); expect(screen.getAllByText('Connected')).toHaveLength(1); expect(screen.getByText('Stale')).toBeDefined(); expect(screen.getByText('Disconnected')).toBeDefined(); expect(screen.getByText('Partial')).toBeDefined();
    for (const key of ['activeDirectory', 'kaspersky', 'opsi', 'nessus']) {
      const branch = screen.getByTestId(`integration-branch-${key}`);
      expect(branch.querySelectorAll('path')).toHaveLength(3);
      expect(branch.querySelectorAll('animate')).toHaveLength(0);
      expect(screen.getByTestId(`integration-icon-${key}`).querySelector('svg')).not.toBeNull();
    }
    expect(screen.getAllByTestId(/^integration-node-/)).toHaveLength(5);
  });

  it('moves a node and its complete branch together while dragging', () => {
    const device = { computerName: 'PC-42', hostName: 'PC-42.corp.local', activeDirectory: { exists: true }, kaspersky: { exists: true }, opsi: { exists: true }, nessus: { exists: true }, assessment: { status: 'HEALTHY', findings: [] } } as unknown as HygieneDevice;
    render(<ClientIntegrationMap device={device} sources={{ activeDirectory: available, kaspersky: available, opsi: available, nessus: available }} />);
    const node = screen.getByTestId('integration-node-kaspersky');
    const branch = screen.getByTestId('integration-branch-kaspersky');
    const originalPath = branch.querySelector('path')!.getAttribute('d');
    fireEvent.pointerDown(node, { clientX: 100, clientY: 100 });
    fireEvent.pointerMove(node, { clientX: 160, clientY: 125 });
    expect(node.style.getPropertyValue('--drag-x')).toBe('60px');
    expect(branch.querySelectorAll('path')).toHaveLength(3);
    expect(branch.querySelector('path')!.getAttribute('d')).not.toBe(originalPath);
  });

  it('automatically probes the client exactly once when opened and marks it offline', async () => {
    const device = { computerName: 'PC-42', hostName: 'PC-42.corp.local', activeDirectory: { exists: true }, kaspersky: { exists: true }, opsi: { exists: true }, nessus: { exists: true }, assessment: { status: 'HEALTHY', findings: [] } } as unknown as HygieneDevice;
    invokeMock.mockResolvedValueOnce({ results: [{ host: device.hostName, reachable: false, manageable: false }] });
    const map = <ClientIntegrationMap device={device} sources={{ activeDirectory: available, kaspersky: available, opsi: available, nessus: available }} />;
    const { rerender } = render(map);
    expect(await screen.findByText('Offline')).toBeDefined();
    expect(screen.getByTestId('integration-node-client').closest('section')?.classList.contains('integration-map--offline')).toBe(true);
    rerender(map);
    expect(invokeMock).toHaveBeenCalledTimes(1);
    expect(invokeMock).toHaveBeenCalledWith('connectivity', 'probeHosts', { hosts: [device.hostName] });
  });
});
