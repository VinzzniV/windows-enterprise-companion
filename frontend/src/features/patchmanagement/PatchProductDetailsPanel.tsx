import type { PatchDashboardOverview, PatchProductOverviewRow } from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { ManufacturerStatusBadge } from './PatchAutomationWorkspace';
import { PatchDeploymentWorkflow } from './PatchDeploymentWorkflow';
import { PatchPackageApprovalWorkflow } from './PatchPackageApprovalWorkflow';
import { PatchPackageBadge } from './PatchPackageBadge';
import {
  PatchProductClientsTable,
  type ClientDrillFilter,
} from './PatchProductClientsTable';

interface PatchProductDetailsPanelProps {
  connected: boolean;
  dashboard: PatchDashboardOverview;
  product: PatchProductOverviewRow;
  depotFilter: string;
  clientFilter: ClientDrillFilter | null;
  selectedClients: ReadonlySet<string>;
  versionCheckBusy: boolean;
  onClose: () => void;
  onClearClientFilter: () => void;
  onToggleClient: (clientId: string) => void;
  onCheckVersion: (productId: string) => void;
  onDashboardRefresh: () => void;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

function manufacturerVersionDescription(product: PatchProductOverviewRow): string {
  if (product.manufacturerVersion) {
    return `Latest version: ${product.manufacturerVersion}${product.manufacturerCheckedAtUtc
      ? ` · checked ${formatTimestamp(product.manufacturerCheckedAtUtc)}`
      : ''}`;
  }
  if (product.manufacturerCheckError) return product.manufacturerCheckError;
  if (product.manufacturerCheckStatus === 'NOT_CHECKED') {
    return 'The configured manufacturer source has not been checked yet.';
  }
  if (product.manufacturerCheckStatus === 'NOT_CONFIGURED') {
    return 'No manufacturer source is configured for this package. No version is estimated.';
  }
  if (product.manufacturerCheckStatus === 'FAILED') {
    return 'The manufacturer version check failed without an error detail.';
  }
  return 'The manufacturer check status is unavailable.';
}

/** Controlled product-detail boundary; child workflows retain their own request state. */
export function PatchProductDetailsPanel({
  connected,
  dashboard,
  product,
  depotFilter,
  clientFilter,
  selectedClients,
  versionCheckBusy,
  onClose,
  onClearClientFilter,
  onToggleClient,
  onCheckVersion,
  onDashboardRefresh,
}: PatchProductDetailsPanelProps) {
  const visibleDepots = depotFilter
    ? dashboard.depots.filter((depot) => depot.id === depotFilter)
    : dashboard.depots;

  return (
    <div className="lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-y-auto">
      <Card title={`Package details — ${product.name ?? product.productId}`}>
        <div className="flex flex-col gap-4">
          <div className="-mt-1 flex justify-end">
            <Button variant="ghost" onClick={onClose}>Close</Button>
          </div>

          {product.lastError && (
            <p className="text-sm text-fail-400">{product.lastError}</p>
          )}

          <div className="flex flex-wrap items-center gap-2">
            <PatchPackageBadge status={product.packageStatus} />
            <span className="font-mono text-xs text-muted">{product.productId}</span>
          </div>

          <div className="grid gap-2 sm:grid-cols-2">
            {visibleDepots.map((depot) => {
              const depotVersion = product.depotVersions.find(
                (version) => version.depotId === depot.id,
              );
              return (
                <div key={depot.id} className={`rounded border p-2 ${
                  depotVersion
                    ? 'border-slate-800 bg-slate-950/50'
                    : 'border-fail-900 bg-fail-950/30'
                }`}>
                  <div className="truncate text-xs text-muted" title={depot.id}>
                    {depot.description ?? depot.id}
                  </div>
                  <div className={`mt-1 font-mono text-sm ${
                    depotVersion ? 'text-slate-200' : 'text-fail-300'
                  }`}>
                    {depotVersion?.version ?? 'Package missing'}
                  </div>
                </div>
              );
            })}
          </div>

          <div className="rounded border border-slate-800 bg-slate-950/40 p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <div className="flex items-center gap-2 text-sm font-medium text-slate-200">
                  Manufacturer version
                  <ManufacturerStatusBadge status={product.manufacturerCheckStatus} />
                </div>
                <p className="mt-0.5 text-xs text-muted">
                  {manufacturerVersionDescription(product)}
                </p>
              </div>
              <Button
                disabled={
                  !connected
                  || versionCheckBusy
                  || product.manufacturerCheckStatus === 'NOT_CONFIGURED'
                }
                onClick={() => onCheckVersion(product.productId)}
              >
                {versionCheckBusy ? 'Checking…' : 'Check version'}
              </Button>
            </div>
          </div>

          <PatchProductClientsTable
            connected={connected}
            dashboard={dashboard}
            productId={product.productId}
            filter={clientFilter}
            selectedClients={selectedClients}
            onClearFilter={onClearClientFilter}
            onToggleClient={onToggleClient}
          />

          {product.inventoryDetections.length > 0 && (
            <DetailsDisclosure
              summary={`WEC inventory detections (${product.inventoryDetections.length})`}
            >
              <ul className="list-inside list-disc text-sm text-slate-300">
                {product.inventoryDetections.map((detection) => (
                  <li key={`${detection.host}-${detection.version}`}>
                    {detection.host}: {detection.version ?? 'unknown version'}
                  </li>
                ))}
              </ul>
            </DetailsDisclosure>
          )}

          <PatchPackageApprovalWorkflow
            connected={connected}
            productId={product.productId}
            depots={dashboard.depots}
            onDashboardRefresh={onDashboardRefresh}
          >
            <PatchDeploymentWorkflow
              connected={connected}
              productId={product.productId}
              depotFilter={depotFilter}
              selectedClients={selectedClients}
              onDashboardRefresh={onDashboardRefresh}
            />
          </PatchPackageApprovalWorkflow>
        </div>
      </Card>
    </div>
  );
}
