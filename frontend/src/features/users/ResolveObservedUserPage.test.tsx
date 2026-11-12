import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { ResolveObservedUserPage } from './ResolveObservedUserPage';
const mocks = vi.hoisted(() => ({ invoke: vi.fn(), cancel: vi.fn() }));
vi.mock('../../shared/bridge/bridgeClient', async (original) => ({
  ...await original<typeof import('../../shared/bridge/bridgeClient')>(), invokeCancellable: mocks.invoke,
}));
vi.mock('../../shared/targets/TargetContext', () => ({ useTargets: () => ({ adminCredentials: null }) }));
vi.mock('../../shared/viewCache', () => ({ loadView: () => null }));
const sid = 'S-1-5-21-1-2-3-1001';
const id = '22222222-2222-2222-2222-222222222222';
function Destination() { const location = useLocation(); return <p>{location.pathname}</p>; }
function page() {
  render(<MemoryRouter initialEntries={[`/users/resolve?sid=${sid}`]}><Routes>
    <Route path="/users/resolve" element={<ResolveObservedUserPage />} />
    <Route path="/users/ad/:scope/:objectId" element={<Destination />} />
  </Routes></MemoryRouter>);
}
beforeEach(() => { mocks.invoke.mockReset(); mocks.cancel.mockReset(); });
it('makes no request until an explicit scoped SID resolution and opens only the returned GUID', async () => {
  mocks.invoke.mockReturnValue({ promise: Promise.resolve({ kind: 'USER', source: 'ACTIVE_DIRECTORY', scope: 'example.test', id }), cancel: mocks.cancel });
  page();
  expect(mocks.invoke).not.toHaveBeenCalled();
  fireEvent.change(screen.getByRole('textbox', { name: 'Directory DNS scope' }), { target: { value: 'example.test' } });
  expect(mocks.invoke).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Resolve SID and open account' }));
  await screen.findByText(`/users/ad/example.test/${id}`);
  expect(mocks.invoke).toHaveBeenCalledWith('usermanagement', 'resolveSid', { securityIdentifier: sid, directoryScope: 'example.test', connection: { domain: 'example.test' } });
});
it('keeps an ambiguous result unresolved', async () => {
  mocks.invoke.mockReturnValue({ promise: Promise.reject(new Error('Ambiguous SID')), cancel: mocks.cancel });
  page();
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'example.test' } });
  fireEvent.click(screen.getByRole('button', { name: 'Resolve SID and open account' }));
  await screen.findByRole('alert');
  expect(screen.queryByText(`/users/ad/example.test/${id}`)).toBeNull();
});
it('cancelled resolution cannot navigate when its response arrives late', async () => {
  let complete: (value: unknown) => void = () => {};
  mocks.invoke.mockReturnValue({ promise: new Promise(resolve => { complete = resolve; }), cancel: mocks.cancel });
  page();
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'example.test' } });
  fireEvent.click(screen.getByRole('button', { name: 'Resolve SID and open account' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Cancel read' }));
  await act(async () => complete({ kind: 'USER', source: 'ACTIVE_DIRECTORY', scope: 'example.test', id }));
  await waitFor(() => expect(mocks.cancel).toHaveBeenCalled());
  expect(screen.queryByText(`/users/ad/example.test/${id}`)).toBeNull();
});
