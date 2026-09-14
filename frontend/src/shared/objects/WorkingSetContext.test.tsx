import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { useRef } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ManagementDeviceObjectLists, Microsoft365ObjectLists, StoredObjectLists } from '../api-types.generated';
import { WorkingSetProvider, useWorkingSet } from './WorkingSetContext';
import { directoryUserWorkingSetRead } from './workingSetSources';

const { invokeMock, targetsMock } = vi.hoisted(() => ({ invokeMock: vi.fn(), targetsMock: vi.fn() }));
vi.mock('../bridge/bridgeClient', async (importOriginal) => ({ ...await importOriginal<typeof import('../bridge/bridgeClient')>(), invokeCancellable: invokeMock }));
vi.mock('../targets/TargetContext', () => ({ useTargets: targetsMock }));
const tenant = '11111111-1111-1111-1111-111111111111';
const id = '22222222-2222-2222-2222-222222222222';
const now = '2026-09-14T10:00:00Z';
const stored: StoredObjectLists = { workspace: { scope: 'workspace', localComputerName: 'LOCAL' }, maximumRecords: 50,
  maximumSourceReads: 128, retrievedAtUtc: now, search: null, reads: [] };
const management = { workspace: stored.workspace, snapshotId: null, opsiSessionId: null, sessionRevision: 0, revision: 0, retrievedAtUtc: null, maximumRecords: 50, search: null, reads: [] };
let managementReply: ManagementDeviceObjectLists;
const cloud = (name = 'Original account', revision = 1, session = 1): Microsoft365ObjectLists => ({
  tenantId: tenant, sessionRevision: session, revision, recordLimit: 50, cachedSourceRecords: 1, loadedSourceRecords: 1, truncated: false,
  reads: [{ state: { query: { resource: 'USERS', objectId: null, securityIdentifier: null }, tenantId: tenant,
    sessionRevision: session, snapshotRevision: revision, availability: 'AVAILABLE', loading: false, retrievedAtUtc: now,
    lastAttemptAtUtc: now, lastAttemptError: null, retainedUntilUtc: '2026-09-14T11:00:00Z', freshUntilUtc: '2026-09-14T10:10:00Z',
    freshness: 'FRESH', coverage: 'RETURNED_SET', loadedCount: 1, declaredTotal: 1 },
  rows: [{ kind: 'USER', source: 'ENTRA', objectId: id, displayName: name, userPrincipalName: null, accountEnabled: null,
    operatingSystem: null, securityIdentifier: null, registrationDeviceId: null, associatedUserId: null, assignedSkuIds: null }] }],
});
let reply: Microsoft365ObjectLists | Promise<Microsoft365ObjectLists>;
let cancellation: ReturnType<typeof vi.fn>[];

function Probe() {
  const workspace = useWorkingSet();
  const originalRefresh = useRef(workspace.refreshCached).current;
  return <>
    <output data-testid="names">{workspace.displayed?.objects.map(object => object.label).join(', ') ?? 'loading'}</output>
    <output data-testid="pending">{workspace.hasUpdates ? 'new data' : 'unchanged'}</output>
    <output data-testid="cloud-generation">{workspace.cloudGeneration}</output>
    <output data-testid="error">{workspace.error}</output>
    <button onClick={() => void workspace.refreshCached()}>Check cache</button>
    <button onClick={() => void originalRefresh()}>Use captured refresh</button>
    <button onClick={workspace.applyUpdates}>Apply</button>
    <button onClick={() => workspace.clearFamily('cloud')}>Clear cloud</button>
    <button onClick={() => workspace.publishDirectory(directoryUserWorkingSetRead({
      data: { directoryScope: 'example.test', retrievedAtUtc: now, page: 1, pageSize: 100, totalCount: 1,
        users: [{ objectId: id, securityIdentifier: null, directoryScope: 'example.test', displayName: 'AD account', samAccountName: 'ad',
          userPrincipalName: null, enabled: null, department: null }] }, lastAttemptAtUtc: now, lastAttemptError: null,
      sessionRevision: 1, revision: 1, retainedUntilUtc: '2026-09-14T11:00:00Z', freshUntilUtc: '2026-09-14T10:10:00Z', stale: false,
    }, { scope: 'example.test', search: '', page: 1, pageSize: 100 }), { domain: 'example.test', server: '' })}>Add directory page</button>
  </>;
}
const view = () => <MemoryRouter initialEntries={['/devices']}><WorkingSetProvider><Probe /></WorkingSetProvider></MemoryRouter>;
beforeEach(() => {
  vi.spyOn(Date, 'now').mockReturnValue(Date.parse(now));
  targetsMock.mockReturnValue({ adminCredentials: null, savedTargets: [] });
  reply = cloud(); managementReply = management; cancellation = [];
  invokeMock.mockReset().mockImplementation((_module: string, action: string) => {
    const cancel = vi.fn(); cancellation.push(cancel);
    return { requestId: String(cancellation.length), cancel,
      promise: action === 'getStoredObjectLists' ? Promise.resolve(stored) : action === 'getCachedManagementObjectLists' ? Promise.resolve(managementReply) : Promise.resolve(reply) };
  });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('working-set session and view lifecycle', () => {
  it('drops cached opsi rows after its source session changes while leaving cloud evidence available', async () => {
    managementReply = { ...management, snapshotId: id, opsiSessionId: 'first-session', sessionRevision: 1, revision: 1,
      reads: [{ source: 'OPSI', state: { source: 'Opsi', scope: 'opsi.example', availability: 'Available', error: null, loadedRecords: 1 },
        matchingCachedRecords: 1, limited: false, rows: [{ reference: { workspace: 'workspace', snapshotId: id, source: 'OPSI', recordIndex: 0 },
          nativeReference: null, label: 'Old opsi record', aliases: [], accountEnabled: null, operatingSystem: null, securityIdentifier: null }] }] };
    render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Old opsi record'));
    managementReply = { ...managementReply, opsiSessionId: 'second-session', reads: [] };
    fireEvent.click(screen.getByText('Check cache'));
    await waitFor(() => expect(screen.getByTestId('names').textContent).not.toContain('Old opsi record'));
    expect(screen.getByTestId('names').textContent).toContain('Original account');
    expect(screen.getByTestId('pending').textContent).toBe('unchanged');
  });

  it('uses the active authority context even when an old source callback invokes a captured refresh function', async () => {
    const rendered = render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    targetsMock.mockReturnValue({ adminCredentials: { userName: 'new-reader', domain: 'DOMAIN', password: 'test-value' }, savedTargets: [] });
    rendered.rerender(view());
    await waitFor(() => expect(invokeMock.mock.calls.filter(call => call[1] === 'getCachedManagementObjectLists').at(-1)?.[2])
      .toEqual({ activeDirectory: { userName: 'new-reader', userDomain: 'DOMAIN', password: 'test-value' }, kaspersky: null, search: null }));
    invokeMock.mockClear();
    fireEvent.click(screen.getByText('Use captured refresh'));
    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith('clients', 'getCachedManagementObjectLists', {
      activeDirectory: { userName: 'new-reader', userDomain: 'DOMAIN', password: 'test-value' }, kaspersky: null, search: null,
    }));
  });

  it('reads only cached/stored projections and holds the displayed revision until explicit adoption', async () => {
    render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    expect(invokeMock.mock.calls.map(call => call[1])).toEqual(['getStoredObjectLists', 'getCachedObjectLists', 'getCachedManagementObjectLists']);
    reply = cloud('New account name', 2);
    fireEvent.click(screen.getByText('Check cache'));
    await waitFor(() => expect(screen.getByTestId('pending').textContent).toContain('new data'));
    expect(screen.getByTestId('names').textContent).toContain('Original account');
    fireEvent.click(screen.getByText('Apply'));
    expect(screen.getByTestId('names').textContent).toContain('New account name');
  });

  it('drops the prior cloud view immediately when the source session changes', async () => {
    render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    reply = cloud('Other session', 2, 2);
    fireEvent.click(screen.getByText('Check cache'));
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Other session'));
    expect(screen.getByTestId('names').textContent).not.toContain('Original account');
    expect(screen.getByTestId('cloud-generation').textContent).toContain('1');
  });

  it('cancels and discards late responses after a cloud-context invalidation', async () => {
    render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    let resolve!: (value: Microsoft365ObjectLists) => void;
    reply = new Promise<Microsoft365ObjectLists>(complete => { resolve = complete; });
    fireEvent.click(screen.getByText('Check cache'));
    fireEvent.click(screen.getByText('Clear cloud'));
    expect(screen.getByTestId('names').textContent).not.toContain('Original account');
    await act(async () => { resolve(cloud('Late response', 2)); });
    expect(screen.getByTestId('names').textContent).not.toContain('Late response');
    expect(cancellation.slice(-3).every(cancel => cancel.mock.calls.length === 1)).toBe(true);
  });

  it('removes directory evidence when Windows administrative credentials change', async () => {
    const rendered = render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    fireEvent.click(screen.getByText('Add directory page'));
    expect(screen.getByTestId('names').textContent).toContain('AD account');
    targetsMock.mockReturnValue({ adminCredentials: { userName: 'another-operator', domain: '', password: '' }, savedTargets: [] });
    rendered.rerender(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).not.toContain('AD account'));
    expect(screen.getByTestId('names').textContent).toContain('Original account');
  });

  it('presents synchronous bridge failures without an unhandled promise or fabricated empty inventory', async () => {
    invokeMock.mockImplementation(() => { throw new Error('Unavailable bridge'); });
    render(view());
    await waitFor(() => expect(screen.getByTestId('error').textContent).not.toBe(''));
    expect(screen.getByTestId('names').textContent).toContain('loading');
  });

  it('removes the old tenant after a cloud session switch even if stored-source retrieval fails', async () => {
    render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    invokeMock.mockImplementation((_module: string, action: string) => ({ requestId: action, cancel: vi.fn(),
      promise: action === 'getStoredObjectLists' ? Promise.reject(new Error('Storage unavailable'))
        : action === 'getCachedManagementObjectLists' ? Promise.resolve(management)
          : Promise.resolve({ ...cloud('Different tenant', 2, 2), tenantId: '99999999-9999-9999-9999-999999999999' }) }));
    fireEvent.click(screen.getByText('Check cache'));
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Different tenant'));
    expect(screen.getByTestId('names').textContent).not.toContain('Original account');
    expect(screen.getByTestId('error').textContent).not.toBe('');
  });
});
