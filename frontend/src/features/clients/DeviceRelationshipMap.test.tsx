import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientOverviewResult, HygieneDevice, InventorySourceState } from '../../shared/api-types';
import { buildDeviceRelationshipModel, DeviceRelationshipMap, integrationStatus } from './DeviceRelationshipMap';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({ invoke: invokeMock }));

const available: InventorySourceState = { availability: 'AVAILABLE', error: null };
const sources = { activeDirectory: available, kaspersky: available, opsi: available, nessus: available };
const device = {
  computerName: 'PC-42',
  hostName: 'PC-42.corp.local',
  activeDirectory: { exists: true, lastLogonDate: '2026-08-20T10:00:00Z' },
  kaspersky: { exists: true, lastSeen: '2026-08-20T11:00:00Z' },
  opsi: { exists: false, lastSeen: null },
  nessus: { exists: true, lastCompletedScanUtc: '2026-08-20T12:00:00Z', critical: 1, high: 2 },
  assessment: {
    status: 'WARNING',
    findings: [
      { code: 'STALE_KASPERSKY', severity: 'WARNING', message: 'Old' },
      { code: 'MISSING_OPSI', severity: 'WARNING', message: 'Missing' },
    ],
  },
} as HygieneDevice;
const overview = {
  host: device.hostName,
  inventory: { operatingSystem: 'Windows 11' },
  software: { installedCount: 42 },
  health: { criticalCount: 0, warningCount: 1, healthyCount: 3 },
  security: { criticalCount: 0, highCount: 2, mediumCount: 4 },
  sources: [
    { source: 'Inventory', provenance: 'Stored hardware snapshot', freshness: 'FRESH', capturedAtUtc: '2026-08-20T08:00:00Z', ageSeconds: 60, isComplete: true, coverage: 'Hardware captured', detailSection: 'inventory' },
    { source: 'Installed software', provenance: 'Stored software snapshot', freshness: 'FRESH', capturedAtUtc: '2026-08-20T08:00:00Z', ageSeconds: 60, isComplete: true, coverage: '42 applications', detailSection: 'inventory' },
    { source: 'Health', provenance: 'Stored Health run', freshness: 'STALE', capturedAtUtc: '2026-08-19T08:00:00Z', ageSeconds: 90000, isComplete: true, coverage: '4 checks', detailSection: 'diagnostics' },
    { source: 'Security', provenance: 'Stored Security scan', freshness: 'FRESH', capturedAtUtc: '2026-08-20T09:00:00Z', ageSeconds: 30, isComplete: false, coverage: 'Partial scan', detailSection: 'security' },
  ],
} as unknown as ClientOverviewResult;

function renderMap() {
  return render(<MemoryRouter><DeviceRelationshipMap device={device} sources={sources} overview={overview} managementObservedAtUtc="2026-08-20T12:30:00Z" /></MemoryRouter>);
}

describe('DeviceRelationshipMap', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockResolvedValue({ results: [{ host: device.hostName, reachable: true, manageable: false }] });
  });

  it('maps backend source and finding states without time-based frontend logic', () => {
    expect(integrationStatus('kaspersky', available, true, new Set())).toBe('connected');
    expect(integrationStatus('kaspersky', available, true, new Set(['STALE_KASPERSKY']))).toBe('stale');
    expect(integrationStatus('kaspersky', available, true, new Set(['MISSING_KES']))).toBe('disconnected');
    expect(integrationStatus('opsi', available, false, new Set(['MISSING_OPSI']))).toBe('disconnected');
    expect(integrationStatus('nessus', { availability: 'PARTIAL', error: null }, true, new Set())).toBe('partial');
    expect(integrationStatus('activeDirectory', { availability: 'UNAVAILABLE', error: 'Offline' }, false, new Set())).toBe('unknown');
  });

  it.each([
    ['NOT_CONNECTED', 'unknown'],
    ['UNAVAILABLE', 'unknown'],
    ['PARTIAL', 'partial'],
    ['TRUNCATED', 'partial'],
  ] as const)('maps %s source availability to %s before considering device evidence', (availability, expected) => {
    expect(integrationStatus('kaspersky', { availability, error: 'Source detail' }, true, new Set(['STALE_KASPERSKY'])))
      .toBe(expected);
  });

  it.each([
    ['activeDirectory', 'STALE_AD'],
    ['kaspersky', 'STALE_KASPERSKY'],
    ['opsi', 'STALE_OPSI'],
    ['nessus', 'STALE_NESSUS'],
  ] as const)('keeps the %s stale finding ahead of missing-device evidence', (source, finding) => {
    expect(integrationStatus(source, available, false, new Set([finding]))).toBe('stale');
  });

  it.each([
    ['kaspersky', 'MISSING_KASPERSKY_AGENT'],
    ['kaspersky', 'MISSING_KES'],
    ['opsi', 'MISSING_OPSI'],
    ['nessus', 'MISSING_NESSUS'],
  ] as const)('maps the %s finding %s to disconnected', (source, finding) => {
    expect(integrationStatus(source, available, true, new Set([finding]))).toBe('disconnected');
  });

  it('builds a bounded device context with management, inventory, health and security evidence', () => {
    const model = buildDeviceRelationshipModel(device, sources, overview, 'unknown', '2026-08-20T12:30:00Z');

    expect(model.nodes.map((node) => node.label)).toEqual([
      'PC-42', 'Active Directory', 'Kaspersky', 'opsi', 'Nessus', 'WEC Inventory', 'Device Health', 'Security posture',
    ]);
    expect(model.edges).toHaveLength(7);
    expect(model.nodes.find((node) => node.label === 'WEC Inventory')?.context).toContain('42 installed applications');
    expect(model.nodes.find((node) => node.label === 'Device Health')?.status).toBe('stale');
    expect(model.nodes.find((node) => node.label === 'Security posture')?.status).toBe('partial');
    expect(model.edges.find((edge) => edge.toNodeId === 'wec:inventory')?.confidence).toBe('confirmed');
    expect(model.edges.find((edge) => edge.toNodeId === 'management:active-directory')?.observedAtUtc)
      .toBe('2026-08-20T12:30:00Z');
  });

  it('renders source links and no decorative drag interaction', () => {
    renderMap();

    expect(screen.getByRole('heading', { name: 'Device relationships' })).toBeDefined();
    expect(screen.getByRole('link', { name: /WEC Inventory/ }).getAttribute('href')).toContain('section=inventory');
    expect(screen.getByRole('link', { name: /Nessus/ }).getAttribute('href')).toContain('/vulnerabilities?tab=findings');
    expect(screen.getByRole('link', { name: /Kaspersky/ }).getAttribute('href')).toBe('/settings?section=environment-health');
    expect(screen.getByTestId('relationship-node-management:kaspersky').getAttribute('draggable')).toBeNull();
    expect(invokeMock).not.toHaveBeenCalled();
  });

  it('probes connectivity only after the explicit action and does not infer offline state', async () => {
    invokeMock.mockResolvedValueOnce({ results: [{ host: device.hostName, reachable: false, manageable: false }] });
    renderMap();

    expect(screen.getByText('Connectivity: Not checked')).toBeDefined();
    expect(invokeMock).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Check connectivity' }));

    expect(await screen.findByText('Connectivity: No ping or WinRM response')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledOnce();
    expect(invokeMock).toHaveBeenCalledWith('connectivity', 'probeHosts', { hosts: [device.hostName] });
  });
});
