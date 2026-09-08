import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { PatchDashboardOverview } from '../../shared/api-types';
import { PatchProductOverviewWorkspace } from './PatchProductOverviewWorkspace';

describe('PatchProductOverviewWorkspace', () => {
  it('labels client-product states as installations and explains their population', () => {
    const dashboard = {
      serverUrl: 'https://opsi.example.test:4447/',
      depotFilter: 'depot-a',
      generatedAtUtc: '2026-09-08T12:00:00Z',
      depots: [{ id: 'depot-a', description: 'Depot A', isConfigServer: true, clientCount: 1 }],
      summary: {
        productCount: 2,
        productsWithUpdates: 2,
        wingetManagedCount: 0,
        wingetUpdatesAvailable: 0,
        outdatedInstallationCount: 2,
        productsWithDepotDeviation: 0,
        productsMissingOnDepots: 0,
        productsWithFailures: 0,
        clientCount: 1,
        depotCount: 1,
      },
      products: [
        product('product-a', 'Product A'),
        product('product-b', 'Product B'),
      ],
    } satisfies PatchDashboardOverview;

    render(<PatchProductOverviewWorkspace dashboard={dashboard} />);

    expect(screen.getByText('Outdated product installations')).toBeTruthy();
    expect(screen.getByText(/Each outdated client-product pair is one product installation/)).toBeTruthy();
    expect(screen.getByText(/not a distinct-device count/)).toBeTruthy();
    const table = screen.getByRole('table');
    expect(within(table).getByRole('columnheader', { name: 'Outdated installations' })).toBeTruthy();
    expect(within(table).getAllByText('1')).toHaveLength(2);
  });
});

function product(productId: string, name: string): PatchDashboardOverview['products'][number] {
  return {
    productId,
    name,
    availableVersion: '2.0-1',
    referenceVersion: '2.0-1',
    depotVersions: [],
    missingDepotIds: [],
    packageStatus: 'UPDATE_AVAILABLE',
    state: 'UPDATE_AVAILABLE',
    installedClientCount: 1,
    outdatedInstallationCount: 1,
    failedClientCount: 0,
    pendingActionCount: 0,
    lastError: null,
    wingetManaged: false,
    wingetId: null,
    latestWingetVersion: null,
    wingetCheckStatus: 'MANUAL',
    wingetCheckedAtUtc: null,
    wingetCheckError: null,
    wingetUpdateAvailable: false,
  };
}
