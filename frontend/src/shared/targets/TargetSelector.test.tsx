import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdComputerSearchResult } from '../api-types';
import { LOCAL_TARGET_SELECTION, TargetSelector, type TargetSelection } from './TargetSelector';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const searchResult: AdComputerSearchResult = {
  domainJoined: true,
  domainName: 'kauth.local',
  truncated: false,
  computers: [
    { name: 'PC1', dnsHostName: 'pc1.kauth.local', operatingSystem: 'Windows 11 Pro', enabled: true, description: null, distinguishedName: null, lastLogonDate: null },
    { name: 'PC2', dnsHostName: 'pc2.kauth.local', operatingSystem: null, enabled: true, description: null, distinguishedName: null, lastLogonDate: null },
    { name: 'OLD1', dnsHostName: 'old1.kauth.local', operatingSystem: null, enabled: false, description: null, distinguishedName: null, lastLogonDate: null },
  ],
};

const multipleSelection: TargetSelection = {
  ...LOCAL_TARGET_SELECTION,
  mode: 'multiple',
  hosts: 'existing.kauth.local',
};

describe('TargetSelector AD computer picker', () => {
  beforeEach(() => {
    invokeMock.mockReset();
  });

  it('searches AD and adds the selected computers to the host list', async () => {
    invokeMock.mockResolvedValue(searchResult);
    const onChange = vi.fn();

    render(<TargetSelector selection={multipleSelection} onChange={onChange} allowMultiple />);
    await userEvent.type(screen.getByLabelText('AD computer name filter'), 'pc');
    await userEvent.click(screen.getByRole('button', { name: 'Search AD' }));

    expect(await screen.findByText(/3 computer\(s\) in kauth.local/)).toBeDefined();
    expect(invokeMock).toHaveBeenCalledWith(
      'activedirectory',
      'searchComputers',
      { nameFilter: 'pc', includeDisabled: false },
    );

    // Enabled computers are preselected, the disabled one is not
    expect((screen.getByRole('checkbox', { name: /pc1.kauth.local/ }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole('checkbox', { name: /old1.kauth.local/ }) as HTMLInputElement).checked).toBe(false);
    expect(screen.getByText('disabled')).toBeDefined();

    await userEvent.click(screen.getByRole('button', { name: /Add 2 computer\(s\)/ }));

    const updated = onChange.mock.calls.at(-1)?.[0] as TargetSelection;
    const hosts = updated.hosts.split('\n');
    expect(hosts).toContain('existing.kauth.local');
    expect(hosts).toContain('pc1.kauth.local');
    expect(hosts).toContain('pc2.kauth.local');
    expect(hosts).not.toContain('old1.kauth.local');
  });

  it('explains a workgroup machine instead of showing an empty list', async () => {
    invokeMock.mockResolvedValue({
      domainJoined: false,
      domainName: null,
      computers: [],
      truncated: false,
    } satisfies AdComputerSearchResult);

    render(<TargetSelector selection={multipleSelection} onChange={vi.fn()} allowMultiple />);
    await userEvent.click(screen.getByRole('button', { name: 'Search AD' }));

    expect(await screen.findByText(/not domain-joined/)).toBeDefined();
  });

  it('surfaces search errors with the AD-page hint', async () => {
    invokeMock.mockRejectedValue(new Error('LDAP down'));

    render(<TargetSelector selection={multipleSelection} onChange={vi.fn()} allowMultiple />);
    await userEvent.click(screen.getByRole('button', { name: 'Search AD' }));

    expect(await screen.findByRole('alert')).toBeDefined();
    expect(screen.getByText(/LDAP down/)).toBeDefined();
  });

  it('does not render the picker outside multiple mode', () => {
    render(
      <TargetSelector
        selection={{ ...LOCAL_TARGET_SELECTION, mode: 'remote', host: 'pc1' }}
        onChange={vi.fn()}
        allowMultiple
      />,
    );

    expect(screen.queryByText('Add from Active Directory')).toBeNull();
  });
});
