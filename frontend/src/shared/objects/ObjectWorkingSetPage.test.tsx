import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { captureWorkingSet, type WorkingSetRead } from './workingSet';
import { UsersWorkingSetPage } from './ObjectWorkingSetPage';

const { workspaceMock, invokeMock } = vi.hoisted(() => ({ workspaceMock: vi.fn(), invokeMock: vi.fn() }));
vi.mock('./WorkingSetContext', () => ({ useWorkingSet: workspaceMock }));
vi.mock('../targets/TargetContext', () => ({ useTargets: () => ({ adminCredentials: null }) }));
vi.mock('../bridge/bridgeClient', async importOriginal => ({ ...await importOriginal<typeof import('../bridge/bridgeClient')>(), invokeCancellable: invokeMock }));
const tenant = '11111111-1111-1111-1111-111111111111';
const read: WorkingSetRead = { key: 'users', title: 'Entra users', family: 'cloud', scope: tenant, sessionRevision: 1, revision: 1,
  retrievedAtUtc: null, lastAttemptAtUtc: null, retainedUntilUtc: null, freshUntilUtc: null,
  availability: 'available', coverage: 'partial', error: null, sourceTotal: 5000, cachedRecordCount: 60, limited: false,
  rows: Array.from({ length: 60 }, (_, index) => ({ kind: 'USER', source: 'ENTRA', label: index === 0 ? 'Needle account' : `Account ${String(index).padStart(2, '0')}`, aliases: [],
    reference: { kind: 'USER', source: 'ENTRA', scope: tenant, id: `22222222-2222-2222-2222-${String(index + 1).padStart(12, '0')}` },
    accountEnabled: index % 2 === 0 ? true : null, operatingSystem: null, sid: null, registrationDeviceId: null, assignedSkuIds: null })) };
function Location() { return <output data-testid="url">{useLocation().pathname + useLocation().search}</output>; }
function Profile() { const navigate = useNavigate(); return <button onClick={() => navigate(-1)}>Back to results</button>; }
function view(initial = '/users/workspace?page=2') { return render(<MemoryRouter initialEntries={[initial]}>
  <Routes><Route path="/users/workspace" element={<UsersWorkingSetPage />} /><Route path="/users/entra/:scope/:id" element={<Profile />} /></Routes><Location />
</MemoryRouter>); }
beforeEach(() => {
  const policy = { maximumRecords: 100, maximumSourceReads: 128, directoryScope: null, tenantId: tenant };
  const snapshot = captureWorkingSet([read], policy, Date.now());
  workspaceMock.mockReturnValue({ displayed: snapshot, available: snapshot, policy, directoryEndpoint: null, busy: false, error: null,
    hasUpdates: false, cloudGeneration: 0, directoryGeneration: 0, refreshCached: vi.fn(), applyUpdates: vi.fn(), publishDirectory: vi.fn(), clearFamily: vi.fn() });
  invokeMock.mockReset();
});

describe('object working-set list', () => {
  it('filters the whole displayed set before paging, resets the page, and never queries a source on typing', () => {
    view();
    const table = screen.getByRole('table');
    expect(within(table).queryByText('Needle account')).toBeNull();
    expect(within(table).getAllByText('Account 26').length).toBeGreaterThan(0);
    fireEvent.change(screen.getByLabelText('Search loaded users'), { target: { value: 'Needle' } });
    expect(screen.getByTestId('url').textContent).toBe('/users/workspace?q=Needle');
    expect(within(table).getAllByText('Needle account').length).toBeGreaterThan(0);
    expect(screen.getByText(/1 matches in this displayed working set/)).toBeDefined();
    expect(invokeMock).not.toHaveBeenCalled();
  });

  it('sorts beyond the current page and restores filters and page through a scoped profile journey', async () => {
    view('/users/workspace?account=unknown&page=2');
    const table = screen.getByRole('table');
    const link = within(table).getAllByRole('link')[0];
    const original = link.getAttribute('href');
    fireEvent.click(link);
    fireEvent.click(screen.getByText('Back to results'));
    await waitFor(() => expect(screen.getByTestId('url').textContent).toBe('/users/workspace?account=unknown&page=2'));
    expect(within(screen.getByRole('table')).getAllByRole('link')[0].getAttribute('href')).toBe(original);
    fireEvent.click(screen.getByRole('button', { name: /Object \/ address/ }));
    fireEvent.click(screen.getByRole('button', { name: /Object \/ address/ }));
    expect(screen.getByTestId('url').textContent).toContain('sort=asc');
    expect(screen.getByTestId('url').textContent).not.toContain('page=2');
    expect(within(screen.getByRole('table')).getAllByText('Account 01').length).toBeGreaterThan(0);
  });
});
