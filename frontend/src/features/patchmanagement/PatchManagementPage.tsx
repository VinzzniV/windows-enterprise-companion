import { useCallback, useState, type ReactNode } from 'react';
import type { OpsiConnectionStatusResult } from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import {
  PatchClientFleetCard,
  type PatchClientOpenFilter,
} from './PatchClientFleetCard';
import { PatchAuditHistoryCard } from './PatchAuditHistoryCard';
import { PatchAutomationWorkspace } from './PatchAutomationWorkspace';
import { PatchProductMappingsCard } from './PatchProductMappingsCard';
import type { ClientDrillFilter } from './PatchProductClientsTable';
import { PatchProductOverviewWorkspace } from './PatchProductOverviewWorkspace';
import { opsiConnectionStatus } from './patchStatus';
import { usePatchManagementWorkspace } from './usePatchManagementWorkspace';

function OpsiConnectionBadge({
  presentation,
  status,
}: {
  presentation: ReturnType<typeof opsiConnectionStatus>;
  status: OpsiConnectionStatusResult | null;
}) {
  return (
    <span
      className="inline-flex flex-wrap items-center justify-end gap-1.5"
      title={presentation.technicalDetail ?? undefined}
    >
      <SemanticStatusBadge status={presentation.status} />
      {presentation.context && (
        <span className="text-xs text-slate-400">{presentation.context}</span>
      )}
      {status?.connected && (
        <span className="text-xs text-slate-300">
          {status.serverUrl} as {status.userName}
          {status.opsiVersion ? ` · opsi ${status.opsiVersion}` : ''}
        </span>
      )}
    </span>
  );
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

type PatchSection = 'overview' | 'clients' | 'history' | 'automation' | 'mappings';

function SectionTab({
  active,
  children,
  onClick,
}: {
  active: boolean;
  children: ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={`border-b-2 px-3 py-2 text-sm font-medium transition-colors ${
        active
          ? 'border-accent-400 text-slate-100'
          : 'border-transparent text-slate-400 hover:text-slate-200'
      }`}
    >
      {children}
    </button>
  );
}

export function PatchManagementPage() {
  const {
    status,
    statusLoading,
    statusError,
    dashboard,
    dashboardLoading,
    dashboardError,
    depotFilter,
    versionCheckBusy,
    versionCheckError,
    connected,
    stale,
    loadConnectionStatus,
    refreshDashboard,
    refreshConnectedDashboard,
    changeDepotFilter: changeWorkspaceDepotFilter,
    checkVendorVersions,
    clearVersionCheckError,
  } = usePatchManagementWorkspace();

  const [selectedProductId, setSelectedProductId] = useState<string | null>(null);
  const [selectedClients, setSelectedClients] = useState<ReadonlySet<string>>(new Set());
  // Which slice of a product's clients the detail table shows (from a count click).
  const [clientFilter, setClientFilter] = useState<ClientDrillFilter | null>(null);
  const [section, setSection] = useState<PatchSection>('overview');

  const changeDepotFilter = useCallback(
    (value: string) => {
      setSelectedProductId(null);
      setSelectedClients(new Set());
      changeWorkspaceDepotFilter(value);
    },
    [changeWorkspaceDepotFilter],
  );

  const selectProduct = useCallback(
    (productId: string) => {
      setSelectedProductId((previous) => (previous === productId ? null : productId));
      setSelectedClients(new Set());
      setClientFilter(null);
    },
    [],
  );

  // A count click opens the product detail already narrowed to that slice —
  // "which clients are behind this number".
  const drillIntoClients = useCallback(
    (productId: string, filter: ClientDrillFilter) => {
      setSelectedProductId(productId);
      setClientFilter(filter);
      setSelectedClients(new Set());
    },
    [],
  );

  const openFleetProduct = useCallback(
    (productId: string, filter: PatchClientOpenFilter) => {
      if (filter === null) {
        selectProduct(productId);
      } else {
        drillIntoClients(productId, filter);
      }
      setSection('overview');
    },
    [drillIntoClients, selectProduct],
  );

  const toggleClient = useCallback((clientId: string) => {
    setSelectedClients((previous) => {
      const next = new Set(previous);
      if (next.has(clientId)) {
        next.delete(clientId);
      } else {
        next.add(clientId);
      }
      return next;
    });
  }, []);

  const connectionPresentation = opsiConnectionStatus(
    statusLoading
      ? { kind: 'loading' }
      : statusError
        ? { kind: 'failed' }
        : status
          ? { kind: 'loaded', status }
          : { kind: 'unavailable' },
  );

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Patch Management"
        subtitle="Central overview of package versions, depot consistency, and controlled software rollouts."
      >
        <OpsiConnectionBadge
          presentation={connectionPresentation}
          status={statusLoading || statusError ? null : status}
        />
      </PageHeader>

      {statusError && (
        <ErrorState
          title="opsi connection unavailable"
          {...statusError}
          controls={(
            <Button onClick={loadConnectionStatus}>
              Check connection again
            </Button>
          )}
        />
      )}

      {!connected && !statusLoading && (
        <Card title="opsi connection">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-slate-400">
              WEC connects automatically with the securely stored opsi account. The server and account are managed in Settings.
            </p>
            <a href="#/settings" className="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800">
              Go to Settings
            </a>
          </div>
        </Card>
      )}

      {(connected || dashboard !== null) && (
        <>
          {stale && (
            <p className="rounded border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-slate-300">
              Saved view from{' '}
              {dashboard ? formatTimestamp(dashboard.generatedAtUtc) : 'the last session'} — no
              active opsi connection. Reconnect to refresh or run actions.
            </p>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-400">Location / depot</span>
              <Select
                aria-label="Depot filter"
                fullWidth={false}
                value={depotFilter}
                disabled={!connected}
                onChange={(event) => changeDepotFilter(event.target.value)}
              >
                <option value="">All depots</option>
                {(dashboard?.depots ?? []).map((depot) => (
                  <option key={depot.id} value={depot.id}>
                    {depot.description ? `${depot.description} (${depot.id})` : depot.id}
                  </option>
                ))}
              </Select>
            </label>
            <Button onClick={refreshDashboard} disabled={dashboardLoading || !connected}>
              Refresh overview
            </Button>
            {dashboardLoading && <Spinner label="Loading patch overview" />}
            {dashboard && (
              <span className="text-xs text-muted">
                Updated {formatTimestamp(dashboard.generatedAtUtc)}
                {dashboard.depotFilter ? ` — depot ${dashboard.depotFilter}` : ' — all depots'}
              </span>
            )}
          </div>

          {dashboardError && (
            <ErrorState
              title="Overview unavailable"
              {...dashboardError}
              controls={(
                <Button onClick={refreshDashboard} disabled={!connected}>
                  Reload overview
                </Button>
              )}
            />
          )}

          {dashboard && (
            <>
              <div role="tablist" aria-label="Patch Management sections" className="flex gap-1 overflow-x-auto border-b border-slate-800">
                <SectionTab active={section === 'overview'} onClick={() => setSection('overview')}>
                  Package overview
                </SectionTab>
                <SectionTab active={section === 'clients'} onClick={() => setSection('clients')}>
                  Clients{' '}
                  <span className="text-muted">
                    ({dashboard.summary.clientCount.toLocaleString('de-DE')})
                  </span>
                </SectionTab>
                <SectionTab active={section === 'history'} onClick={() => setSection('history')}>
                  History
                </SectionTab>
                <SectionTab active={section === 'automation'} onClick={() => setSection('automation')}>
                  Automation
                </SectionTab>
                <SectionTab active={section === 'mappings'} onClick={() => setSection('mappings')}>
                  Mappings
                </SectionTab>
              </div>

              {section === 'overview' && (
                <PatchProductOverviewWorkspace
                  connected={connected}
                  dashboard={dashboard}
                  depotFilter={depotFilter}
                  selectedProductId={selectedProductId}
                  clientFilter={clientFilter}
                  selectedClients={selectedClients}
                  versionCheckBusy={versionCheckBusy}
                  onSelectProduct={selectProduct}
                  onDrillIntoClients={drillIntoClients}
                  onClearClientFilter={() => setClientFilter(null)}
                  onToggleClient={toggleClient}
                  onCheckVersion={(productId) => { void checkVendorVersions([productId]); }}
                  onDashboardRefresh={refreshConnectedDashboard}
                />
              )}

              {section === 'clients' && (
                <PatchClientFleetCard
                  connected={connected}
                  dashboard={dashboard}
                  onOpenProduct={openFleetProduct}
                />
              )}

              {section === 'automation' && (
                <PatchAutomationWorkspace
                  connected={connected}
                  products={dashboard.products}
                  checkBusy={versionCheckBusy}
                  checkError={versionCheckError}
                  onCheckAll={() => checkVendorVersions()}
                  onClearCheckError={clearVersionCheckError}
                  onDashboardRefresh={refreshConnectedDashboard}
                />
              )}

              {section === 'mappings' && (
                <PatchProductMappingsCard
                  unmappedSoftware={dashboard.unmappedSoftware}
                  onMappingsChanged={refreshConnectedDashboard}
                />
              )}
            </>
          )}

          {section === 'history' && <PatchAuditHistoryCard />}
        </>
      )}

      {!connected && status !== null && dashboard === null && (
        <EmptyState
          title="No opsi connection"
          message="Connect to an opsi server to load packages, affected clients, and deployment status. Nothing on the server is changed without explicit confirmation."
        />
      )}
    </div>
  );
}
