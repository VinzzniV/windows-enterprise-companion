import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { PatchManagementPage } from './PatchManagementPage';

vi.mock('./usePatchManagementWorkspace', () => ({ usePatchManagementWorkspace: () => ({
  status: { connected: true, serverUrl: 'https://opsi.example.test', opsiVersion: '4.3' }, statusLoading: false, statusError: null,
  connected: true, stale: false, dashboardLoading: false, dashboardError: null, depotFilter: '',
  dashboard: { generatedAtUtc: '2026-09-16T10:00:00Z', depots: [], summary: { clientCount: 12 } },
  loadConnectionStatus: vi.fn(), refreshDashboard: vi.fn(), changeDepotFilter: vi.fn(),
}) }));
vi.mock('./PatchProductOverviewWorkspace', () => ({ PatchProductOverviewWorkspace: () => <p>Packages content</p> }));
vi.mock('./PatchClientFleetCard', () => ({ PatchClientFleetCard: () => <p>Client versions content</p> }));
vi.mock('./WingetPackagesWorkspace', () => ({ WingetPackagesWorkspace: () => <p>Winget packages content</p> }));
vi.mock('./PatchAuditHistoryCard', () => ({ PatchAuditHistoryCard: () => <p>History content</p> }));

function Location() { const location = useLocation(); return <output aria-label="Current location">{location.pathname + location.search}</output>; }

describe('legacy software route', () => {
  it('keeps old section bookmarks on equivalent content with the same vocabulary', () => {
    render(<MemoryRouter initialEntries={['/patchmanagement?section=clients']}><PatchManagementPage /><Location /></MemoryRouter>);

    expect(screen.getByRole('heading', { name: 'Software & licenses' })).toBeTruthy();
    expect(screen.getByText('Client versions content')).toBeTruthy();
    const tabs = screen.getByRole('tablist', { name: 'Software and license sections' });
    expect(within(tabs).getAllByRole('tab').map(tab => tab.textContent)).toEqual(['Packages', 'Client versions (12)', 'Winget packages', 'History']);
    fireEvent.click(within(tabs).getByRole('tab', { name: 'Winget packages' }));
    expect(screen.getByRole('status', { name: 'Current location' }).textContent).toBe('/patchmanagement?section=winget');
    expect(screen.getByText('Winget packages content')).toBeTruthy();
  });
});
