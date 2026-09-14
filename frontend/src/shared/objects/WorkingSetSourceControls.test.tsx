import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { WorkingSetSourceControls } from './WorkingSetSourceControls';

const { workspaceMock, targetsMock, invokeMock } = vi.hoisted(() => ({ workspaceMock: vi.fn(), targetsMock: vi.fn(), invokeMock: vi.fn() }));
vi.mock('./WorkingSetContext', () => ({ useWorkingSet: workspaceMock }));
vi.mock('../targets/TargetContext', () => ({ useTargets: targetsMock }));
vi.mock('../bridge/bridgeClient', async importOriginal => ({ ...await importOriginal<typeof import('../bridge/bridgeClient')>(), invokeCancellable: invokeMock }));
const publishDirectory = vi.fn();
const refreshCached = vi.fn();
const cached = { data: { directoryScope: 'example.test', retrievedAtUtc: new Date().toISOString(), page: 1, pageSize: 100, totalCount: 2000, users: [] },
  lastAttemptAtUtc: new Date().toISOString(), lastAttemptError: null, sessionRevision: 1, revision: 1, retainedUntilUtc: null, freshUntilUtc: null, stale: false };
const view = () => <MemoryRouter><WorkingSetSourceControls kind="USER" /></MemoryRouter>;
beforeEach(() => {
  workspaceMock.mockReturnValue({ directoryEndpoint: null, cloudGeneration: 0, directoryGeneration: 0, publishDirectory, refreshCached, clearFamily: vi.fn() });
  targetsMock.mockReturnValue({ adminCredentials: null });
  publishDirectory.mockReset(); refreshCached.mockReset(); invokeMock.mockReset();
});

describe('explicit working-set source reads', () => {
  it('loads targeted AD computers with native identity and partial coverage', async () => {
    invokeMock.mockReturnValue({ cancel: vi.fn(), promise: Promise.resolve({ directoryScope: 'example.test', search: 'PC', limit: 100, freshUntilUtc: null,
      read: { ...cached, data: { directoryScope: 'example.test', retrievedAtUtc: cached.lastAttemptAtUtc, truncated: true,
        computers: [{ computerName: 'PC', dnsHostName: null, enabled: null, operatingSystem: null, distinguishedName: 'CN=PC',
          objectId: '11111111-1111-1111-1111-111111111111', securityIdentifier: null, directoryScope: 'example.test' }] } } }) });
    render(<MemoryRouter><WorkingSetSourceControls kind="DEVICE" /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText('Directory DNS scope'), { target: { value: 'example.test' } });
    fireEvent.change(screen.getByLabelText('Source query'), { target: { value: 'PC' } });
    expect(invokeMock).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText('Read AD computers (up to 100)'));
    await waitFor(() => expect(publishDirectory).toHaveBeenCalledOnce());
    expect(invokeMock).toHaveBeenCalledExactlyOnceWith('activedirectory', 'readComputerList', {
      connection: { domain: 'example.test' }, directoryScope: 'example.test', search: 'PC', limit: 100, refresh: true,
    });
    expect(publishDirectory.mock.calls[0][0]).toMatchObject({ coverage: 'partial', sourceTotal: null,
      rows: [{ reference: { kind: 'DEVICE', source: 'ACTIVE_DIRECTORY', scope: 'example.test', id: '11111111-1111-1111-1111-111111111111' }, accountEnabled: null }] });
  });

  it('loads only the requested bounded AD page and does not query while editing search fields', async () => {
    invokeMock.mockReturnValue({ requestId: 'page', cancel: vi.fn(), promise: Promise.resolve(cached) });
    render(view());
    fireEvent.change(screen.getByLabelText('Directory DNS scope'), { target: { value: 'example.test' } });
    fireEvent.change(screen.getByLabelText('Source query'), { target: { value: 'alex' } });
    fireEvent.change(screen.getByLabelText('Source page'), { target: { value: '4' } });
    expect(invokeMock).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText('Read AD page (up to 100)'));
    await waitFor(() => expect(publishDirectory).toHaveBeenCalledOnce());
    expect(invokeMock).toHaveBeenCalledExactlyOnceWith('usermanagement', 'readDirectoryPage', {
      connection: { domain: 'example.test' }, directoryScope: 'example.test', search: 'alex', page: 4, pageSize: 100, refresh: true,
    });
    expect(publishDirectory.mock.calls[0][0].coverage).toBe('partial');
  });

  it('cancels and rejects a late AD page after administrative credentials change', async () => {
    let complete!: (value: typeof cached) => void;
    const cancel = vi.fn();
    invokeMock.mockReturnValue({ requestId: 'page', cancel, promise: new Promise(resolve => { complete = resolve; }) });
    const result = render(view());
    fireEvent.change(screen.getByLabelText('Directory DNS scope'), { target: { value: 'example.test' } });
    fireEvent.click(screen.getByText('Read AD page (up to 100)'));
    targetsMock.mockReturnValue({ adminCredentials: { userName: 'different-operator', domain: 'example.test', password: '' } });
    result.rerender(view());
    await act(async () => { complete(cached); });
    expect(cancel).toHaveBeenCalledOnce();
    expect(publishDirectory).not.toHaveBeenCalled();
  });

  it('binds an explicit Graph inventory read to the current tenant and then refreshes cached list projections', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => ({ requestId: action, cancel: vi.fn(),
      promise: Promise.resolve(action === 'getStatus' ? { connection: { configuration: { tenantId: 'tenant' } } } : {}) }));
    render(view());
    expect(invokeMock).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText('Read bounded Entra inventory'));
    await waitFor(() => expect(refreshCached).toHaveBeenCalledWith(true));
    expect(invokeMock.mock.calls).toEqual([['microsoft365', 'getStatus'], ['microsoft365', 'read', { resource: 'USERS', tenantId: 'tenant', refresh: true }]]);
  });
});
