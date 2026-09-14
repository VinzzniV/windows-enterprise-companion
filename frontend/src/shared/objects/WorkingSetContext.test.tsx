import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Microsoft365ObjectLists, StoredObjectLists } from '../api-types.generated';
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
  return <>
    <output data-testid="names">{workspace.displayed?.objects.map(object => object.label).join(', ') ?? 'loading'}</output>
    <output data-testid="pending">{workspace.hasUpdates ? 'new data' : 'unchanged'}</output>
    <output data-testid="cloud-generation">{workspace.cloudGeneration}</output>
    <output data-testid="error">{workspace.error}</output>
    <button onClick={() => void workspace.refreshCached()}>Check cache</button>
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
  targetsMock.mockReturnValue({ adminCredentials: null });
  reply = cloud(); cancellation = [];
  invokeMock.mockReset().mockImplementation((_module: string, action: string) => {
    const cancel = vi.fn(); cancellation.push(cancel);
    return { requestId: String(cancellation.length), cancel,
      promise: action === 'getStoredObjectLists' ? Promise.resolve(stored) : Promise.resolve(reply) };
  });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('working-set session and view lifecycle', () => {
  it('reads only cached/stored projections and holds the displayed revision until explicit adoption', async () => {
    render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    expect(invokeMock.mock.calls.map(call => call[1])).toEqual(['getStoredObjectLists', 'getCachedObjectLists']);
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
    expect(cancellation.slice(-2).every(cancel => cancel.mock.calls.length === 1)).toBe(true);
  });

  it('removes directory evidence when Windows administrative credentials change', async () => {
    const rendered = render(view());
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Original account'));
    fireEvent.click(screen.getByText('Add directory page'));
    expect(screen.getByTestId('names').textContent).toContain('AD account');
    targetsMock.mockReturnValue({ adminCredentials: { userName: 'another-operator' } });
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
        : Promise.resolve({ ...cloud('Different tenant', 2, 2), tenantId: '99999999-9999-9999-9999-999999999999' }) }));
    fireEvent.click(screen.getByText('Check cache'));
    await waitFor(() => expect(screen.getByTestId('names').textContent).toContain('Different tenant'));
    expect(screen.getByTestId('names').textContent).not.toContain('Original account');
    expect(screen.getByTestId('error').textContent).not.toBe('');
  });
});
