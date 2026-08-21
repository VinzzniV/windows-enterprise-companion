import { useMemo, useState } from 'react';
import type {
  PatchDashboardOverview,
  PatchPackageStatus,
  PatchProductOverviewRow,
} from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { semanticStatusPresentation } from '../../shared/ui/SemanticStatusBadge';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { ManufacturerStatusBadge } from './PatchAutomationWorkspace';
import { PatchPackageBadge } from './PatchPackageBadge';
import type { ClientDrillFilter } from './PatchProductClientsTable';
import { PatchProductDetailsPanel } from './PatchProductDetailsPanel';
import { patchPackageStatus } from './patchStatus';

const packageStatusOptions: readonly PatchPackageStatus[] = [
  'CURRENT',
  'UPDATE_AVAILABLE',
  'DEPOT_DEVIATION',
  'MISSING_ON_DEPOT',
  'CHECK_FAILED',
  'DEPLOYMENT_PENDING',
];

interface PatchProductOverviewWorkspaceProps {
  connected: boolean;
  dashboard: PatchDashboardOverview;
  depotFilter: string;
  selectedProductId: string | null;
  clientFilter: ClientDrillFilter | null;
  selectedClients: ReadonlySet<string>;
  versionCheckBusy: boolean;
  onSelectProduct: (productId: string) => void;
  onDrillIntoClients: (productId: string, filter: ClientDrillFilter) => void;
  onClearClientFilter: () => void;
  onToggleClient: (clientId: string) => void;
  onCheckVersion: (productId: string) => void;
  onDashboardRefresh: () => void;
}

function DrillCount({
  count,
  className = '',
  onDrill,
  label,
}: {
  count: number;
  className?: string;
  onDrill: () => void;
  label: string;
}) {
  if (count === 0) {
    return <>{count}</>;
  }
  return (
    <button
      type="button"
      onClick={onDrill}
      title={`Show the ${label.toLowerCase()} clients`}
      className={`cursor-pointer underline decoration-dotted underline-offset-2 hover:text-accent-300 ${className}`}
    >
      {count}
    </button>
  );
}

export function PatchProductOverviewWorkspace({
  connected,
  dashboard,
  depotFilter,
  selectedProductId,
  clientFilter,
  selectedClients,
  versionCheckBusy,
  onSelectProduct,
  onDrillIntoClients,
  onClearClientFilter,
  onToggleClient,
  onCheckVersion,
  onDashboardRefresh,
}: PatchProductOverviewWorkspaceProps) {
  const [productSearch, setProductSearch] = useState('');
  const [packageStatusFilter, setPackageStatusFilter] = useState<PatchPackageStatus | ''>('');
  const selectedProduct = dashboard.products.find(
    (product) => product.productId === selectedProductId,
  ) ?? null;
  const visibleProducts = useMemo(() => {
    const needle = productSearch.trim().toLocaleLowerCase();
    return dashboard.products.filter((product) => {
      const matchesSearch = needle.length === 0
        || product.productId.toLocaleLowerCase().includes(needle)
        || (product.name ?? '').toLocaleLowerCase().includes(needle);
      return matchesSearch
        && (packageStatusFilter === '' || product.packageStatus === packageStatusFilter);
    });
  }, [dashboard.products, productSearch, packageStatusFilter]);

  return (
    <>
      <div className="flex flex-wrap gap-3">
        <SummaryMetric label="Packages" value={dashboard.summary.productCount} />
        <SummaryMetric
          label="Updates available"
          value={dashboard.summary.productsWithUpdates}
          tone={dashboard.summary.productsWithUpdates > 0 ? 'warning' : 'success'}
        />
        <SummaryMetric
          label="Depot deviations"
          value={dashboard.summary.productsWithDepotDeviation}
          tone={dashboard.summary.productsWithDepotDeviation > 0 ? 'warning' : 'success'}
        />
        <SummaryMetric
          label="Missing from depots"
          value={dashboard.summary.productsMissingOnDepots}
          tone={dashboard.summary.productsMissingOnDepots > 0 ? 'danger' : 'success'}
        />
        <SummaryMetric
          label="Failures"
          value={dashboard.summary.productsWithFailures}
          tone={dashboard.summary.productsWithFailures > 0 ? 'danger' : 'success'}
        />
        <SummaryMetric
          label="Outdated clients"
          value={dashboard.summary.outdatedClientCount}
          tone={dashboard.summary.outdatedClientCount > 0 ? 'warning' : 'success'}
        />
        <SummaryMetric
          label="Pending deployments"
          value={dashboard.summary.pendingRolloutCount}
          tone={dashboard.summary.pendingRolloutCount > 0 ? 'info' : 'neutral'}
        />
      </div>

      {(dashboard.summary.productsWithFailures > 0
        || dashboard.summary.productsMissingOnDepots > 0
        || dashboard.summary.productsWithDepotDeviation > 0) && (
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-lg border border-warn-700/70 bg-warn-950/25 px-4 py-3 text-sm">
          <span className="font-medium text-warn-300">Action required</span>
          {dashboard.summary.productsWithFailures > 0 && (
            <span className="text-fail-300">{dashboard.summary.productsWithFailures} failed packages</span>
          )}
          {dashboard.summary.productsMissingOnDepots > 0 && (
            <span className="text-warn-300">{dashboard.summary.productsMissingOnDepots} packages missing from depots</span>
          )}
          {dashboard.summary.productsWithDepotDeviation > 0 && (
            <span className="text-warn-300">{dashboard.summary.productsWithDepotDeviation} version deviations</span>
          )}
        </div>
      )}

      <div className="flex flex-wrap items-end gap-3 rounded-lg border border-slate-800 bg-slate-900/60 p-3">
        <label className="min-w-56 flex-1 text-xs font-medium uppercase tracking-wide text-muted">
          Search packages
          <Input
            type="search"
            value={productSearch}
            onChange={(event) => setProductSearch(event.target.value)}
            placeholder="Name or product ID"
            className="mt-1"
          />
        </label>
        <label className="text-xs font-medium uppercase tracking-wide text-muted">
          Status
          <Select
            aria-label="Status filter"
            value={packageStatusFilter}
            onChange={(event) => setPackageStatusFilter(event.target.value as PatchPackageStatus | '')}
            className="mt-1"
          >
            <option value="">All statuses</option>
            {packageStatusOptions.map((value) => {
              const presentation = patchPackageStatus(value);
              const label = semanticStatusPresentation(presentation.status).label;
              return (
                <option key={value} value={value}>
                  {presentation.context ? `${label} — ${presentation.context}` : label}
                </option>
              );
            })}
          </Select>
        </label>
        <span className="pb-2 text-xs text-muted">
          {visibleProducts.length} of {dashboard.products.length} packages
        </span>
      </div>

      <div
        className={
          selectedProduct
            ? 'grid items-start gap-4 lg:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)]'
            : ''
        }
      >
        <Card title="Software versions on opsi depots">
          <DataTable
            columns={[
              {
                header: 'Product',
                cell: (row: PatchProductOverviewRow) => (
                  <button
                    type="button"
                    onClick={() => onSelectProduct(row.productId)}
                    className={`cursor-pointer text-left hover:text-accent-300 ${
                      row.productId === selectedProductId ? 'text-accent-400' : 'text-slate-100'
                    }`}
                  >
                    <span className="block font-medium">{row.name ?? row.productId}</span>
                    <span className="block font-mono text-xs text-muted">{row.productId}</span>
                  </button>
                ),
              },
              {
                header: 'opsi reference',
                mono: true,
                cell: (row: PatchProductOverviewRow) => row.referenceVersion ?? '—',
              },
              {
                header: 'Manufacturer',
                mono: true,
                cell: (row: PatchProductOverviewRow) => (
                  <span className="inline-flex flex-wrap items-center gap-1.5">
                    {row.manufacturerVersion && (
                      <span className={row.manufacturerUpdateAvailable ? 'text-warn-300' : 'text-slate-300'}>
                        {row.manufacturerVersion}
                      </span>
                    )}
                    {row.manufacturerCheckStatus !== 'SUCCESS' && (
                      <ManufacturerStatusBadge status={row.manufacturerCheckStatus} />
                    )}
                    {!row.manufacturerVersion && row.manufacturerCheckStatus === 'SUCCESS' && '—'}
                  </span>
                ),
              },
              {
                header: 'Depots',
                cell: (row: PatchProductOverviewRow) => (
                  <span className={row.missingDepotIds.length > 0 ? 'text-fail-300' : 'text-slate-300'}>
                    {row.depotVersions.length}/{depotFilter ? 1 : dashboard.depots.length}
                  </span>
                ),
              },
              {
                header: 'Status',
                cell: (row: PatchProductOverviewRow) => <PatchPackageBadge status={row.packageStatus} />,
              },
              {
                header: 'Installed',
                align: 'right',
                cell: (row: PatchProductOverviewRow) => (
                  <DrillCount
                    count={row.installedClientCount}
                    label="Installed"
                    onDrill={() => onDrillIntoClients(row.productId, 'installed')}
                  />
                ),
              },
              {
                header: 'Outdated',
                align: 'right',
                cell: (row: PatchProductOverviewRow) => (
                  <DrillCount
                    count={row.outdatedClientCount}
                    className="text-warn-400"
                    label="Outdated"
                    onDrill={() => onDrillIntoClients(row.productId, 'UPDATE_AVAILABLE')}
                  />
                ),
              },
              {
                header: 'Failed',
                align: 'right',
                cell: (row: PatchProductOverviewRow) => (
                  <DrillCount
                    count={row.failedClientCount}
                    className="text-fail-400"
                    label="Failed"
                    onDrill={() => onDrillIntoClients(row.productId, 'FAILED')}
                  />
                ),
              },
              {
                header: 'Pending',
                align: 'right',
                cell: (row: PatchProductOverviewRow) => (
                  <DrillCount
                    count={row.pendingActionCount}
                    label="Pending"
                    onDrill={() => onDrillIntoClients(row.productId, 'ROLLOUT_REQUESTED')}
                  />
                ),
              },
              {
                header: '',
                cell: (row: PatchProductOverviewRow) => (
                  <Button variant="ghost" onClick={() => onSelectProduct(row.productId)}>
                    Details
                  </Button>
                ),
              },
            ]}
            rows={visibleProducts}
            getRowKey={(row) => row.productId}
            emptyMessage="No packages match the selected filters."
          />
        </Card>

        {selectedProduct && (
          <PatchProductDetailsPanel
            connected={connected}
            dashboard={dashboard}
            product={selectedProduct}
            depotFilter={depotFilter}
            clientFilter={clientFilter}
            selectedClients={selectedClients}
            versionCheckBusy={versionCheckBusy}
            onClose={() => onSelectProduct(selectedProduct.productId)}
            onClearClientFilter={onClearClientFilter}
            onToggleClient={onToggleClient}
            onCheckVersion={onCheckVersion}
            onDashboardRefresh={onDashboardRefresh}
          />
        )}
      </div>
    </>
  );
}
