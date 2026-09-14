import { act, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ManagementDeviceRecordProfile } from '../../shared/api-types.generated';
import { ManagementRecordPage } from './ManagementRecordPage';

const { invokeMock, sessionsMock, environment } = vi.hoisted(() => ({ invokeMock: vi.fn(), sessionsMock: vi.fn(), environment: {} }));
vi.mock('../../shared/bridge/bridgeClient', async importOriginal => ({ ...await importOriginal<typeof import('../../shared/bridge/bridgeClient')>(), invokeCancellable: invokeMock }));
vi.mock('../../shared/environment/EnvironmentContext', () => ({ useEnvironmentRequest: () => environment }));
vi.mock('../../shared/objects/WorkingSetContext', () => ({ useOptionalWorkingSet: () => null, useWorkingSetSessions: sessionsMock }));
const snapshotId = '11111111-1111-1111-1111-111111111111';
const data: ManagementDeviceRecordProfile = {
  record: { reference: { workspace: 'workspace', snapshotId, source: 'NESSUS', recordIndex: 0 }, nativeReference: null, label: '10.23.45.67', aliases: [], accountEnabled: null, operatingSystem: null, securityIdentifier: null },
  sourceState: { source: 'Nessus', scope: null, availability: 'Partial', error: 'Some scans are unavailable', loadedRecords: 1 }, retrievedAtUtc: '2026-09-14T10:00:00Z',
  identityExplanation: 'One source observation; stable identity not established.', activeDirectory: null, kaspersky: null, opsi: null,
  nessus: { computerName: '10.23.45.67', fqdn: null, assetId: 'legacy-id', ipAddress: '10.23.45.67', lastCompletedScanUtc: null,
    critical: 1, high: 2, medium: 3, low: 4, info: 5, ports: [443], scanSources: ['Scan'], sourceKey: '10.23.45.67|NESSUS:uuid', hostUuid: 'host-uuid', biosUuid: null },
};
function view() { return <MemoryRouter initialEntries={[`/devices/records/nessus/workspace/${snapshotId}/0`]}><Routes>
  <Route path="/devices/records/:source/:workspace/:snapshot/:index" element={<ManagementRecordPage />} /></Routes></MemoryRouter>; }
beforeEach(() => { sessionsMock.mockReturnValue({ management: 0 }); invokeMock.mockReset().mockReturnValue({ requestId: 'record', cancel: vi.fn(), promise: Promise.resolve(data) }); });

describe('management source-only profile', () => {
  it('shows original source-only fields and unknown scope without selecting a Windows execution target', async () => {
    render(view());
    expect(await screen.findByRole('heading', { name: '10.23.45.67' })).toBeDefined();
    expect(invokeMock).toHaveBeenCalledExactlyOnceWith('clients', 'getManagementRecord', { reference: data.record.reference });
    expect(screen.getByText('host-uuid')).toBeDefined();
    expect(screen.getByText('Some scans are unavailable')).toBeDefined();
    expect(screen.getByRole('link', { name: 'Inspect stored Nessus findings' }).getAttribute('href')).toBe('/vulnerabilities?tab=findings&asset=10.23.45.67%7CNESSUS%3Auuid');
    expect(screen.queryByRole('button', { name: /scan|PowerShell/i })).toBeNull();
  });

  it('clears an old profile on connection generation change and rejects its late completion', async () => {
    let resolve!: (value: ManagementDeviceRecordProfile) => void;
    const cancel = vi.fn();
    invokeMock.mockReturnValueOnce({ requestId: 'old', cancel, promise: new Promise(complete => { resolve = complete; }) });
    const rendered = render(view());
    invokeMock.mockReturnValue({ requestId: 'new', cancel: vi.fn(), promise: Promise.reject(new Error('Source snapshot replaced')) });
    sessionsMock.mockReturnValue({ management: 1 });
    rendered.rerender(view());
    await act(async () => { resolve(data); });
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('snapshot or connection'));
    expect(screen.queryByText('host-uuid')).toBeNull();
    expect(cancel).toHaveBeenCalledOnce();
  });
});
