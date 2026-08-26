import { useMemo, useState } from 'react';
import type { PatchDashboardOverview, PatchProductOverviewRow } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { PatchPackageBadge } from './PatchPackageBadge';

function managementLabel(row: PatchProductOverviewRow): string {
  if (!row.wingetManaged) return 'Manual';
  return row.wingetUpdateAvailable ? 'Update available' : 'Current';
}

export function PatchProductOverviewWorkspace({ dashboard }: { dashboard: PatchDashboardOverview }) {
  const [search, setSearch] = useState('');
  const products = useMemo(() => {
    const needle = search.trim().toLocaleLowerCase();
    return dashboard.products.filter((product) => !needle
      || product.productId.toLocaleLowerCase().includes(needle)
      || (product.name ?? '').toLocaleLowerCase().includes(needle)
      || (product.wingetId ?? '').toLocaleLowerCase().includes(needle));
  }, [dashboard.products, search]);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-3">
        <SummaryMetric label="Packages" value={dashboard.summary.productCount} />
        <SummaryMetric label="Winget managed" value={dashboard.summary.wingetManagedCount} />
        <SummaryMetric label="Winget updates" value={dashboard.summary.wingetUpdatesAvailable} tone={dashboard.summary.wingetUpdatesAvailable ? 'warning' : 'success'} />
        <SummaryMetric label="Outdated clients" value={dashboard.summary.outdatedClientCount} tone={dashboard.summary.outdatedClientCount ? 'warning' : 'success'} />
        <SummaryMetric label="Depot deviations" value={dashboard.summary.productsWithDepotDeviation} tone={dashboard.summary.productsWithDepotDeviation ? 'warning' : 'success'} />
        <SummaryMetric label="Failures" value={dashboard.summary.productsWithFailures} tone={dashboard.summary.productsWithFailures ? 'danger' : 'success'} />
      </div>
      <Card title="Software versions on opsi depots">
        <p className="mb-3 text-sm text-slate-400">
          Manual means that WEC has not registered this Product ID as Winget-managed yet. Existing
          Winget-based opsi packages remain manual until they are searched, previewed and explicitly adopted.
        </p>
        <div className="mb-3 max-w-md">
          <Input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Package, product ID, or Winget ID" aria-label="Filter packages" />
        </div>
        <DataTable
          columns={[
            { header: 'Product', cell: (row: PatchProductOverviewRow) => <span><span className="block font-medium text-slate-100">{row.name ?? row.productId}</span><span className="block font-mono text-xs text-muted">{row.productId}</span></span> },
            { header: 'Management', cell: (row: PatchProductOverviewRow) => <span><span className={row.wingetUpdateAvailable ? 'text-warn-300' : 'text-slate-200'}>{managementLabel(row)}</span>{row.wingetId && <span className="block font-mono text-xs text-muted">{row.wingetId}</span>}</span> },
            { header: 'Depot version', mono: true, cell: (row: PatchProductOverviewRow) => row.referenceVersion ?? '—' },
            { header: 'Latest Winget', mono: true, cell: (row: PatchProductOverviewRow) => row.latestWingetVersion ?? '—' },
            { header: 'Depots', cell: (row: PatchProductOverviewRow) => `${row.depotVersions.length}/${dashboard.depots.length}` },
            { header: 'Clients behind', align: 'right', cell: (row: PatchProductOverviewRow) => row.outdatedClientCount },
            { header: 'Status', cell: (row: PatchProductOverviewRow) => <PatchPackageBadge status={row.packageStatus} /> },
          ]}
          rows={products}
          getRowKey={(row) => row.productId}
          emptyMessage="No packages match this filter."
        />
      </Card>
    </div>
  );
}
