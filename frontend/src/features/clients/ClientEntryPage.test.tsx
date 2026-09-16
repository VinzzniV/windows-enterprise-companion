import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ClientEntryPage } from './ClientEntryPage';

const mocks = vi.hoisted(() => ({ invoke: vi.fn(), cancel: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', () => ({ invokeCancellable: mocks.invoke }));
vi.mock('./ClientDetailPage', () => ({ ClientDetailPage: () => <p>Exact Windows tools</p> }));
function Resolved() { const location = useLocation(); return <output>{location.pathname}{location.search}</output>; }
function open(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/clients/:host" element={<ClientEntryPage />} /><Route path="/devices/:source/:scope/:objectId" element={<Resolved />} />
  </Routes></MemoryRouter>);
}
describe('legacy Windows address entry', () => {
  beforeEach(() => { mocks.invoke.mockReset(); mocks.invoke.mockReturnValue({ promise: Promise.resolve({ workspace: { scope: 'wec-workspace' } }), cancel: mocks.cancel }); });
  it('preserves a full original address and focused section without a remote read', async () => {
    open('/clients/pc.other.example?section=diagnostics');
    expect(await screen.findByText('/devices/wec/wec-workspace/pc.other.example?section=diagnostics')).toBeTruthy();
    expect(mocks.invoke).toHaveBeenCalledTimes(1);
    expect(mocks.invoke).toHaveBeenCalledWith('clients', 'getStoredObjectLists', { search: 'pc.other.example' });
  });
  it('keeps IPv6 scope identifiers and directly opens a previously selected exact operational target', async () => {
    const view = open('/clients/fe80%3A%3A1%2512');
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('clients', 'getStoredObjectLists', { search: 'fe80::1%12' }));
    view.unmount(); mocks.invoke.mockClear();
    open('/clients/pc.example?section=inventory&target=exact');
    expect(screen.getByText('Exact Windows tools')).toBeTruthy();
    expect(mocks.invoke).not.toHaveBeenCalled();
  });
});
