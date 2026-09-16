import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { SoftwareLicensesPage } from './SoftwareLicensesPage';

vi.mock('./PatchManagementPage', () => ({ PatchManagementPage: ({ embedded }: { embedded?: boolean }) => <p>{embedded ? 'Embedded package workspace' : 'Standalone package workspace'}</p> }));
vi.mock('../microsoft365/LicenseWorkspace', () => ({ LicenseWorkspace: () => <p>License capacity workspace</p> }));

describe('Software & licenses shell', () => {
  it('renders one header and one five-destination navigation row around package content', () => {
    render(<MemoryRouter initialEntries={['/software?section=winget']}><SoftwareLicensesPage /></MemoryRouter>);

    expect(screen.getAllByRole('banner')).toHaveLength(1);
    const navigation = screen.getByRole('navigation', { name: 'Software and license sections' });
    expect(within(navigation).getAllByRole('link').map(link => link.textContent)).toEqual([
      'Packages', 'Client versions', 'Licenses', 'Winget packages', 'History',
    ]);
    expect(within(navigation).getByRole('link', { name: 'Winget packages' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByText('Embedded package workspace')).toBeTruthy();
    expect(screen.queryByText('Standalone package workspace')).toBeNull();
  });

  it('keeps licenses in the same navigation without loading the package workspace', () => {
    render(<MemoryRouter initialEntries={['/software?section=licenses']}><SoftwareLicensesPage /></MemoryRouter>);
    expect(screen.getByText('License capacity workspace')).toBeTruthy();
    expect(screen.queryByText('Embedded package workspace')).toBeNull();
  });
});
