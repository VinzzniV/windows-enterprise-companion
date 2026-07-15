import { useCallback, useEffect, useRef, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AuditLogResult,
  MappingsResult,
  OpsiConnectionStatusResult,
  PatchAuditEntry,
  PatchClientState,
  PatchDashboardResult,
  PatchProductRow,
  PatchWorkflowState,
  PreparePackagesPlan,
  ProductMapping,
  RolloutPreview,
  RolloutRequestOutcome,
} from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Checkbox } from '../../shared/ui/Checkbox';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Field } from '../../shared/ui/Field';
import { Input } from '../../shared/ui/Input';
import { SavedTargetsBar } from '../../shared/targets/SavedTargetsBar';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { loadView, saveView } from '../../shared/viewCache';

/** What survives an app restart for this page — never the password (ADR 0008). */
interface CachedPatchView {
  server: string;
  userName: string;
  depotFilter: string;
  dashboard: PatchDashboardResult | null;
}

const patchViewKey = 'patchmanagement';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { StatusBadge, type StatusBadgeVariant } from '../../shared/ui/StatusBadge';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';

/** What the admin should do next, per typed opsi error. */
const opsiErrorHints: Record<string, string> = {
  AUTHENTICATION_FAILED: 'opsi rejected the credentials — check user name and password.',
  ACCESS_DENIED: 'The user authenticated but lacks rights — it must be in the opsi admin group (opsiadmin).',
  SERVICE_UNAVAILABLE:
    'opsiconfd did not answer — check the service URL (usually https://<server>:4447), the service state '
    + "and the firewall. For opsi's self-signed CA, tick “Trust server certificate”.",
  DNS_RESOLUTION_FAILED: 'The server name does not resolve — check DNS or use the IP address.',
  CONNECTION_TIMEOUT: 'The opsi service did not answer in time — check the network path.',
  REMOTE_COMMAND_FAILED: 'opsi accepted the connection but the call failed — see the details.',
};

function opsiError(error: unknown): { message: string; hint?: string } {
  if (error instanceof BridgeInvokeError) {
    const details = error.error.details ? ` — ${error.error.details}` : '';
    return {
      message: `${error.error.code}: ${error.error.message}${details}`,
      hint: opsiErrorHints[error.error.code],
    };
  }
  return { message: error instanceof Error ? error.message : String(error) };
}

const stateBadges: Record<PatchWorkflowState, { label: string; variant: StatusBadgeVariant }> = {
  DETECTED: { label: 'Detected', variant: 'neutral' },
  UPDATE_AVAILABLE: { label: 'Update available', variant: 'elevation' },
  DOWNLOAD_NEEDED: { label: 'Download needed', variant: 'elevation' },
  PACKAGE_PREPARED: { label: 'Package prepared', variant: 'info' },
  UPLOADED: { label: 'Uploaded', variant: 'info' },
  READY_FOR_PILOT: { label: 'Ready for pilot', variant: 'info' },
  APPROVED: { label: 'Approved', variant: 'info' },
  ROLLOUT_REQUESTED: { label: 'Rollout requested', variant: 'info' },
  COMPLETED: { label: 'Up to date', variant: 'success' },
  FAILED: { label: 'Failed', variant: 'error' },
};

function WorkflowBadge({ state }: { state: PatchWorkflowState }) {
  const badge = stateBadges[state] ?? { label: state, variant: 'neutral' as StatusBadgeVariant };
  return <StatusBadge variant={badge.variant}>{badge.label}</StatusBadge>;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

/** Which slice of a product's clients a count click drills into. */
type ClientDrillFilter = 'installed' | 'UPDATE_AVAILABLE' | 'FAILED' | 'ROLLOUT_REQUESTED';

const drillFilterLabels: Record<ClientDrillFilter, string> = {
  installed: 'Installed',
  UPDATE_AVAILABLE: 'Outdated',
  FAILED: 'Failed',
  ROLLOUT_REQUESTED: 'Pending',
};

function matchesDrillFilter(client: PatchClientState, filter: ClientDrillFilter): boolean {
  return filter === 'installed'
    ? client.installationStatus === 'installed'
    : client.state === filter;
}

/** A count cell that opens the client drilldown when there is something behind it. */
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

/** Preselects the configured default depot (matched by id or description). */
export function resolveDefaultDepot(
  depots: { id: string; description: string | null }[],
  defaultDepotFilter: string,
): string | null {
  const needle = defaultDepotFilter.trim().toLowerCase();
  if (needle.length === 0) {
    return null;
  }
  const match = depots.find(
    (depot) =>
      depot.id.toLowerCase().includes(needle)
      || (depot.description ?? '').toLowerCase().includes(needle),
  );
  return match?.id ?? null;
}

interface ConnectFormState {
  server: string;
  userName: string;
  password: string;
  trustServerCertificate: boolean;
}

type AsyncError = { message: string; hint?: string } | null;

export function PatchManagementPage() {
  const targets = useTargetsOptional();
  // The stored view *is* the initial state — a restart opens on the last dashboard
  // and the server/user it came from. The password is never cached (ADR 0008).
  const cached = useRef(loadView<CachedPatchView>(patchViewKey)).current;
  const [status, setStatus] = useState<OpsiConnectionStatusResult | null>(null);
  const [form, setForm] = useState<ConnectFormState>(() => ({
    server: cached?.server ?? '',
    userName: cached?.userName ?? '',
    password: '',
    // opsi ships a self-signed CA — trusting it is the normal case, so default it on.
    trustServerCertificate: true,
  }));
  const [connecting, setConnecting] = useState(false);
  const [connectionError, setConnectionError] = useState<AsyncError>(null);

  const [dashboard, setDashboard] = useState<PatchDashboardResult | null>(
    () => cached?.dashboard ?? null,
  );
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [dashboardError, setDashboardError] = useState<AsyncError>(null);
  const [depotFilter, setDepotFilter] = useState<string>(() => cached?.depotFilter ?? '');
  const [depotResolved, setDepotResolved] = useState(false);

  const [selectedProductId, setSelectedProductId] = useState<string | null>(null);
  const [selectedClients, setSelectedClients] = useState<ReadonlySet<string>>(new Set());
  // Which slice of a product's clients the detail table shows (from a count click).
  const [clientFilter, setClientFilter] = useState<ClientDrillFilter | null>(null);

  const [preview, setPreview] = useState<RolloutPreview | null>(null);
  const [previewError, setPreviewError] = useState<AsyncError>(null);
  const [previewReviewed, setPreviewReviewed] = useState(false);
  const [rolloutBusy, setRolloutBusy] = useState(false);
  const [rolloutOutcome, setRolloutOutcome] = useState<string | null>(null);

  const [packagePlan, setPackagePlan] = useState<PreparePackagesPlan | null>(null);

  const [mappings, setMappings] = useState<ProductMapping[]>([]);
  const [mappingInputs, setMappingInputs] = useState<Record<string, string>>({});
  const [mappingError, setMappingError] = useState<AsyncError>(null);

  const [audit, setAudit] = useState<PatchAuditEntry[]>([]);

  const loadAudit = useCallback(() => {
    invoke<AuditLogResult>('patchmanagement', 'getAuditLog', {})
      .then((result) => setAudit(result.entries))
      .catch(() => setAudit([]));
  }, []);

  const loadMappings = useCallback(() => {
    invoke<MappingsResult>('patchmanagement', 'listMappings', {})
      .then((result) => setMappings(result.mappings))
      .catch(() => setMappings([]));
  }, []);

  const loadDashboard = useCallback((filter: string) => {
    setDashboardLoading(true);
    setDashboardError(null);
    invoke<PatchDashboardResult>(
      'patchmanagement',
      'getDashboard',
      { depotFilter: filter.length > 0 ? filter : null },
      120_000,
    )
      .then((result) => setDashboard(result))
      .catch((error: unknown) => {
        setDashboard(null);
        setDashboardError(opsiError(error));
      })
      .finally(() => setDashboardLoading(false));
  }, []);

  useEffect(() => {
    // Mappings and audit come from the local database — they need no opsi session,
    // so they load even when the cached dashboard is all we can show.
    loadMappings();
    loadAudit();
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus', {})
      .then((result) => {
        setStatus(result);
        if (result.connected) {
          loadDashboard('');
        }
      })
      .catch(() => setStatus(null));
  }, [loadDashboard, loadMappings, loadAudit]);

  // Preselect the configured default depot once the first dashboard names the depots.
  // Only while connected — a restored cached dashboard must not trigger an opsi call.
  useEffect(() => {
    if (dashboard === null || depotResolved || status === null || !status.connected) {
      return;
    }
    setDepotResolved(true);
    const defaultDepot = resolveDefaultDepot(dashboard.depots, status.defaultDepotFilter);
    if (defaultDepot !== null && defaultDepot !== depotFilter) {
      setDepotFilter(defaultDepot);
      loadDashboard(defaultDepot);
    }
  }, [dashboard, depotResolved, status, depotFilter, loadDashboard]);

  // Nothing cached yet: fall back to the newest saved opsi server, so a restart only
  // ever needs the password back.
  const prefilledRef = useRef(false);
  useEffect(() => {
    if (prefilledRef.current || !targets?.savedTargetsReady || cached !== null) {
      return;
    }
    prefilledRef.current = true;
    const saved = targets.savedTargets.filter((target) => target.role === 'OpsiServer').at(-1);
    if (saved) {
      setForm((previous) => ({
        ...previous,
        server: saved.host,
        userName: saved.userName ?? '',
      }));
    }
  }, [targets?.savedTargets, targets?.savedTargetsReady, targets, cached]);

  // Remember the dashboard and the server/user behind it. Only these fields — the
  // password is deliberately not part of the cached shape.
  useEffect(() => {
    saveView<CachedPatchView>(patchViewKey, {
      server: form.server,
      userName: form.userName,
      depotFilter,
      dashboard,
    });
  }, [form.server, form.userName, depotFilter, dashboard]);

  const resetActionPanels = useCallback(() => {
    setPreview(null);
    setPreviewError(null);
    setPreviewReviewed(false);
    setRolloutOutcome(null);
    setPackagePlan(null);
  }, []);

  const connect = useCallback(() => {
    setConnecting(true);
    setConnectionError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'connect', form, 120_000)
      .then((result) => {
        setStatus(result);
        // Remember the server + user (never the password) so the next launch is
        // one field away from connected — like a saved print server.
        const host = form.server.trim();
        const alreadySaved = targets?.savedTargets.some(
          (target) => target.role === 'OpsiServer' && target.host.toUpperCase() === host.toUpperCase(),
        );
        if (targets && host !== '' && !alreadySaved) {
          void targets.saveTarget({
            label: host,
            host,
            role: 'OpsiServer',
            userName: form.userName.trim() || null,
          });
        }
        // The password has done its job; keep it out of React state from here on
        setForm((previous) => ({ ...previous, password: '' }));
        setDepotResolved(false);
        setDepotFilter('');
        resetActionPanels();
        loadDashboard('');
        loadMappings();
        loadAudit();
      })
      .catch((error: unknown) => setConnectionError(opsiError(error)))
      .finally(() => setConnecting(false));
  }, [form, targets, loadDashboard, loadMappings, loadAudit, resetActionPanels]);

  const disconnect = useCallback(() => {
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'disconnect', {})
      .then((result) => {
        setStatus(result);
        setDashboard(null);
        setSelectedProductId(null);
        resetActionPanels();
      })
      .catch(() => {});
  }, [resetActionPanels]);

  const changeDepotFilter = useCallback(
    (value: string) => {
      setDepotFilter(value);
      setSelectedProductId(null);
      setSelectedClients(new Set());
      resetActionPanels();
      loadDashboard(value);
    },
    [loadDashboard, resetActionPanels],
  );

  const selectProduct = useCallback(
    (productId: string) => {
      setSelectedProductId((previous) => (previous === productId ? null : productId));
      setSelectedClients(new Set());
      setClientFilter(null);
      resetActionPanels();
    },
    [resetActionPanels],
  );

  // A count click opens the product detail already narrowed to that slice —
  // "which clients are behind this number".
  const drillIntoClients = useCallback(
    (productId: string, filter: ClientDrillFilter) => {
      setSelectedProductId(productId);
      setClientFilter(filter);
      setSelectedClients(new Set());
      resetActionPanels();
    },
    [resetActionPanels],
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
    setPreview(null);
    setPreviewReviewed(false);
    setRolloutOutcome(null);
  }, []);

  const loadPreview = useCallback(
    (product: PatchProductRow) => {
      setPreviewError(null);
      setPreview(null);
      setPreviewReviewed(false);
      setRolloutOutcome(null);
      invoke<RolloutPreview>(
        'patchmanagement',
        'getRolloutPreview',
        {
          productId: product.productId,
          depotFilter: depotFilter.length > 0 ? depotFilter : null,
          clientIds: selectedClients.size > 0 ? [...selectedClients] : null,
        },
        120_000,
      )
        .then(setPreview)
        .catch((error: unknown) => setPreviewError(opsiError(error)));
    },
    [depotFilter, selectedClients],
  );

  const requestRollout = useCallback(() => {
    if (preview === null) {
      return;
    }
    setRolloutBusy(true);
    invoke<RolloutRequestOutcome>(
      'patchmanagement',
      'requestRollout',
      {
        productId: preview.productId,
        clientIds: preview.clients.map((client) => client.clientId),
        depotFilter: preview.depotFilter,
        confirmed: true,
      },
      120_000,
    )
      .then((outcome) => {
        setRolloutOutcome(
          `Rollout requested for ${outcome.requestedClientCount} client(s) — opsi will install on the next action check.`,
        );
        setPreview(null);
        setPreviewReviewed(false);
        loadDashboard(depotFilter);
        loadAudit();
      })
      .catch((error: unknown) => {
        setPreviewError(opsiError(error));
        loadAudit();
      })
      .finally(() => setRolloutBusy(false));
  }, [preview, depotFilter, loadDashboard, loadAudit]);

  const planPackages = useCallback(
    (product: PatchProductRow) => {
      setPackagePlan(null);
      invoke<PreparePackagesPlan>('patchmanagement', 'preparePackages', {
        productIds: [product.productId],
      })
        .then((plan) => {
          setPackagePlan(plan);
          loadAudit();
        })
        .catch((error: unknown) => setPreviewError(opsiError(error)));
    },
    [loadAudit],
  );

  const saveMapping = useCallback(
    (softwareName: string, productId: string) => {
      setMappingError(null);
      invoke<MappingsResult>('patchmanagement', 'saveMapping', {
        softwareName,
        opsiProductId: productId,
      })
        .then((result) => {
          setMappings(result.mappings);
          // Mapping is a local-database edit and works offline; only a live session
          // can refresh the dashboard, and refreshing without one would drop the
          // restored view on the floor.
          if (status?.connected) {
            loadDashboard(depotFilter);
          }
          loadAudit();
        })
        .catch((error: unknown) => setMappingError(opsiError(error)));
    },
    [depotFilter, status?.connected, loadDashboard, loadAudit],
  );

  const deleteMapping = useCallback(
    (softwareName: string) => {
      invoke<MappingsResult>('patchmanagement', 'deleteMapping', { softwareName })
        .then((result) => {
          setMappings(result.mappings);
          if (status?.connected) {
            loadDashboard(depotFilter);
          }
          loadAudit();
        })
        .catch((error: unknown) => setMappingError(opsiError(error)));
    },
    [depotFilter, status?.connected, loadDashboard, loadAudit],
  );

  const connected = status?.connected === true;
  // A restored dashboard with no live session: readable, but nothing may act on it.
  const stale = !connected && dashboard !== null;
  const selectedProduct =
    dashboard?.products.find((product) => product.productId === selectedProductId) ?? null;

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Patch Management"
        subtitle="Semi-automatic opsi workflow: overview, preparation, preview and audit — no rollout without confirmation."
      >
        {connected ? (
          <>
            <StatusBadge variant="success">
              Connected: {status?.serverUrl} as {status?.userName}
              {status?.opsiVersion ? ` (opsi ${status.opsiVersion})` : ''}
            </StatusBadge>
            <Button onClick={disconnect}>Disconnect</Button>
          </>
        ) : (
          <StatusBadge variant="neutral">Not connected</StatusBadge>
        )}
      </PageHeader>

      {!connected && (
        <Card title="opsi connection">
          <div className="flex flex-col gap-3">
            <p className="text-sm text-slate-400">
              Credentials are kept in memory for this session only — never saved, never logged
              (ADR 0008). The user must be in the opsi admin group.
            </p>
            <div className="grid gap-3 sm:grid-cols-3">
              <Field label="opsi server">
                {(id) => (
                  <Input
                    id={id}
                    type="text"
                    value={form.server}
                    onChange={(event) => setForm({ ...form, server: event.target.value })}
                    placeholder="opsi.example.local"
                  />
                )}
              </Field>
              <Field label="User name">
                {(id) => (
                  <Input
                    id={id}
                    type="text"
                    value={form.userName}
                    onChange={(event) => setForm({ ...form, userName: event.target.value })}
                    autoComplete="off"
                  />
                )}
              </Field>
              <Field label="Password">
                {(id) => (
                  <Input
                    id={id}
                    type="password"
                    value={form.password}
                    onChange={(event) => setForm({ ...form, password: event.target.value })}
                    autoComplete="off"
                  />
                )}
              </Field>
            </div>
            <SavedTargetsBar
              role="OpsiServer"
              label="Saved opsi servers"
              currentHost={form.server}
              currentUserName={form.userName || null}
              onPick={(target) =>
                setForm({ ...form, server: target.host, userName: target.userName ?? form.userName })
              }
            />
            <Checkbox
              label="Trust server certificate (opsi uses a self-signed CA by default; this skips certificate validation for this session)"
              checked={form.trustServerCertificate}
              onChange={(event) => setForm({ ...form, trustServerCertificate: event.target.checked })}
            />
            <div className="flex items-center gap-3">
              <Button variant="primary" onClick={connect} disabled={connecting}>
                {connecting ? 'Testing connection…' : 'Test connection & connect'}
              </Button>
              {connecting && <Spinner label="Testing the opsi connection" />}
            </div>
            {connectionError && (
              <ErrorState
                title="Connection failed"
                message={connectionError.message}
                hint={connectionError.hint}
              />
            )}
          </div>
        </Card>
      )}

      {(connected || dashboard !== null) && (
        <>
          {stale && (
            <p className="rounded border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-slate-300">
              Stored view from{' '}
              {dashboard ? formatTimestamp(dashboard.generatedAtUtc) : 'the last session'} — not
              connected to opsi. Connect above to refresh it or to act on a product.
            </p>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-400">Location / depot</span>
              <Select
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
            <Button onClick={() => loadDashboard(depotFilter)} disabled={dashboardLoading || !connected}>
              Refresh
            </Button>
            {dashboardLoading && <Spinner label="Loading the patch dashboard" />}
            {dashboard && (
              <span className="text-xs text-slate-500">
                Generated {formatTimestamp(dashboard.generatedAtUtc)}
                {dashboard.depotFilter ? ` — depot ${dashboard.depotFilter}` : ' — all depots'}
              </span>
            )}
          </div>

          {dashboardError && (
            <ErrorState
              title="Dashboard unavailable"
              message={dashboardError.message}
              hint={dashboardError.hint}
            />
          )}

          {dashboard && (
            <>
              <div className="flex flex-wrap gap-3">
                <SummaryMetric label="Products" value={dashboard.summary.productCount} />
                <SummaryMetric
                  label="With updates"
                  value={dashboard.summary.productsWithUpdates}
                  tone={dashboard.summary.productsWithUpdates > 0 ? 'warning' : 'success'}
                />
                <SummaryMetric
                  label="With failures"
                  value={dashboard.summary.productsWithFailures}
                  tone={dashboard.summary.productsWithFailures > 0 ? 'danger' : 'success'}
                />
                <SummaryMetric
                  label="Pending rollouts"
                  value={dashboard.summary.pendingRolloutCount}
                  tone={dashboard.summary.pendingRolloutCount > 0 ? 'info' : 'neutral'}
                />
                <SummaryMetric label="Clients" value={dashboard.summary.clientCount} />
                <SummaryMetric
                  label="Unmapped software"
                  value={dashboard.summary.unmappedSoftwareCount}
                  tone="neutral"
                />
              </div>

              <div
                className={
                  selectedProduct
                    ? 'grid items-start gap-4 lg:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)]'
                    : ''
                }
              >
              <Card title="opsi products">
                <DataTable
                  columns={[
                    {
                      header: 'Product',
                      cell: (row: PatchProductRow) => (
                        <button
                          type="button"
                          onClick={() => selectProduct(row.productId)}
                          className={`cursor-pointer text-left hover:text-accent-300 ${
                            row.productId === selectedProductId ? 'text-accent-400' : 'text-slate-100'
                          }`}
                        >
                          <span className="font-medium">{row.productId}</span>
                          {row.name && <span className="ml-2 text-slate-400">{row.name}</span>}
                        </button>
                      ),
                    },
                    {
                      header: 'Available',
                      mono: true,
                      cell: (row: PatchProductRow) =>
                        row.availableVersion ?? `differs per depot (${row.depotVersions.length})`,
                    },
                    { header: 'Status', cell: (row: PatchProductRow) => <WorkflowBadge state={row.state} /> },
                    {
                      header: 'Installed',
                      align: 'right',
                      cell: (row: PatchProductRow) => (
                        <DrillCount
                          count={row.installedClientCount}
                          label="Installed"
                          onDrill={() => drillIntoClients(row.productId, 'installed')}
                        />
                      ),
                    },
                    {
                      header: 'Outdated',
                      align: 'right',
                      cell: (row: PatchProductRow) => (
                        <DrillCount
                          count={row.outdatedClientCount}
                          className="text-warn-400"
                          label="Outdated"
                          onDrill={() => drillIntoClients(row.productId, 'UPDATE_AVAILABLE')}
                        />
                      ),
                    },
                    {
                      header: 'Failed',
                      align: 'right',
                      cell: (row: PatchProductRow) => (
                        <DrillCount
                          count={row.failedClientCount}
                          className="text-fail-400"
                          label="Failed"
                          onDrill={() => drillIntoClients(row.productId, 'FAILED')}
                        />
                      ),
                    },
                    {
                      header: 'Pending',
                      align: 'right',
                      cell: (row: PatchProductRow) => (
                        <DrillCount
                          count={row.pendingActionCount}
                          label="Pending"
                          onDrill={() => drillIntoClients(row.productId, 'ROLLOUT_REQUESTED')}
                        />
                      ),
                    },
                    {
                      header: 'Inventory matches',
                      align: 'right',
                      cell: (row: PatchProductRow) => row.inventoryDetections.length,
                    },
                  ]}
                  rows={dashboard.products}
                  emptyMessage="No localboot products found on the selected depot."
                />
              </Card>

              {selectedProduct && (
                <div className="lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-y-auto">
                <Card title={`Product detail — ${selectedProduct.productId}`}>
                  <div className="flex flex-col gap-4">
                    <div className="-mt-1 flex justify-end">
                      <Button variant="ghost" onClick={() => selectProduct(selectedProduct.productId)}>
                        Collapse
                      </Button>
                    </div>
                    {selectedProduct.lastError && (
                      <p className="text-sm text-fail-400">{selectedProduct.lastError}</p>
                    )}
                    <div className="text-sm text-slate-400">
                      Versions per depot:{' '}
                      {selectedProduct.depotVersions
                        .map((version) => `${version.depotId}: ${version.version}`)
                        .join(' · ')}
                    </div>

                    {clientFilter && (
                      <div className="flex items-center gap-2 text-sm">
                        <span className="text-slate-400">Showing only:</span>
                        <StatusBadge variant="info">{drillFilterLabels[clientFilter]}</StatusBadge>
                        <Button variant="ghost" onClick={() => setClientFilter(null)}>
                          Show all clients
                        </Button>
                      </div>
                    )}

                    <DataTable
                      columns={[
                        {
                          header: 'Target',
                          cell: (client: PatchClientState) => (
                            <input
                              type="checkbox"
                              aria-label={`Select ${client.clientId}`}
                              checked={selectedClients.has(client.clientId)}
                              onChange={() => toggleClient(client.clientId)}
                            />
                          ),
                        },
                        { header: 'Client', cell: (client: PatchClientState) => client.clientId },
                        {
                          header: 'Depot',
                          cell: (client: PatchClientState) => client.depotId ?? '—',
                        },
                        {
                          header: 'Installed version',
                          cell: (client: PatchClientState) => client.installedVersion ?? '—',
                        },
                        {
                          header: 'Target version',
                          cell: (client: PatchClientState) => client.targetVersion ?? '—',
                        },
                        {
                          header: 'Status',
                          cell: (client: PatchClientState) => <WorkflowBadge state={client.state} />,
                        },
                      ]}
                      rows={
                        clientFilter
                          ? selectedProduct.clients.filter((client) =>
                              matchesDrillFilter(client, clientFilter),
                            )
                          : selectedProduct.clients
                      }
                      emptyMessage="opsi has no state for this product on the filtered clients."
                    />

                    {selectedProduct.inventoryDetections.length > 0 && (
                      <DetailsDisclosure
                        summary={`WEC inventory detections (${selectedProduct.inventoryDetections.length})`}
                      >
                        <ul className="list-inside list-disc text-sm text-slate-300">
                          {selectedProduct.inventoryDetections.map((detection) => (
                            <li key={`${detection.host}-${detection.version}`}>
                              {detection.host}: {detection.version ?? 'unknown version'}
                            </li>
                          ))}
                        </ul>
                      </DetailsDisclosure>
                    )}

                    <div className="flex flex-wrap items-center gap-3">
                      <Button
                        variant="primary"
                        disabled={!connected}
                        onClick={() => loadPreview(selectedProduct)}
                      >
                        Preview rollout
                        {selectedClients.size > 0
                          ? ` (${selectedClients.size} selected)`
                          : ' (outdated & failed clients)'}
                      </Button>
                      <Button disabled={!connected} onClick={() => planPackages(selectedProduct)}>
                        Prepare packages (plan)
                      </Button>
                      {stale && (
                        <span className="text-xs text-slate-500">
                          Connect to opsi to preview or request a rollout.
                        </span>
                      )}
                    </div>

                    {previewError && (
                      <ErrorState
                        title="Action failed"
                        message={previewError.message}
                        hint={previewError.hint}
                      />
                    )}
                    {rolloutOutcome && <p className="text-sm text-ok-400">{rolloutOutcome}</p>}

                    {preview && (
                      <div className="flex flex-col gap-3 rounded border border-warn-700 bg-warn-950/30 p-3">
                        <p className="text-sm font-medium text-warn-300">
                          Rollout preview — action “{preview.plannedAction}” for{' '}
                          {preview.clients.length} client(s)
                          {preview.depotFilter ? ` on ${preview.depotFilter}` : ' across all depots'}.
                          Nothing has been sent to opsi yet.
                        </p>
                        <DataTable
                          columns={[
                            { header: 'Client', cell: (client) => client.clientId },
                            { header: 'Depot', cell: (client) => client.depotId ?? '—' },
                            { header: 'Installed', cell: (client) => client.installedVersion ?? '—' },
                            { header: 'Target', cell: (client) => client.targetVersion ?? '—' },
                            {
                              header: 'State',
                              cell: (client) => <WorkflowBadge state={client.currentState} />,
                            },
                          ]}
                          rows={preview.clients}
                          emptyMessage="No clients need this update — nothing to roll out."
                        />
                        {preview.clients.length > 0 && (
                          <>
                            <label className="flex cursor-pointer items-center gap-2 text-sm text-warn-300">
                              <input
                                type="checkbox"
                                className="accent-accent-500"
                                checked={previewReviewed}
                                onChange={(event) => setPreviewReviewed(event.target.checked)}
                              />
                              I reviewed the affected clients and want to request this rollout.
                            </label>
                            <div>
                              <Button
                                variant="primary"
                                disabled={!previewReviewed || rolloutBusy}
                                onClick={requestRollout}
                              >
                                {rolloutBusy
                                  ? 'Requesting…'
                                  : `Request rollout for ${preview.clients.length} client(s)`}
                              </Button>
                            </div>
                          </>
                        )}
                      </div>
                    )}

                    {packagePlan && (
                      <div className="flex flex-col gap-2 rounded border border-slate-700 bg-slate-950 p-3">
                        <p className="text-sm text-slate-300">{packagePlan.note}</p>
                        <code className="overflow-x-auto rounded bg-slate-900 p-2 text-sm text-accent-300">
                          {packagePlan.command}
                        </code>
                      </div>
                    )}
                  </div>
                </Card>
                </div>
              )}
              </div>

              <Card title="Inventory software without opsi mapping">
                <div className="flex flex-col gap-3">
                  <p className="text-sm text-slate-400">
                    Software found by WEC inventory scans that is not mapped to an opsi product.
                    Map it to include it in the comparison; suggestions appear only on exact name
                    matches.
                  </p>
                  {mappingError && (
                    <ErrorState message={mappingError.message} hint={mappingError.hint} />
                  )}
                  <DataTable
                    columns={[
                      { header: 'Software', cell: (row) => row.name },
                      { header: 'Versions', cell: (row) => row.versions.join(', ') || '—' },
                      { header: 'Hosts', cell: (row) => row.hostCount },
                      {
                        header: 'opsi product id',
                        cell: (row) => (
                          <div className="flex items-center gap-2">
                            <div className="w-40">
                              <Input
                                type="text"
                                aria-label={`opsi product id for ${row.name}`}
                                value={mappingInputs[row.name] ?? row.suggestedProductId ?? ''}
                                onChange={(event) =>
                                  setMappingInputs((previous) => ({
                                    ...previous,
                                    [row.name]: event.target.value,
                                  }))
                                }
                                placeholder="productId"
                              />
                            </div>
                            <Button
                              onClick={() =>
                                saveMapping(
                                  row.name,
                                  mappingInputs[row.name] ?? row.suggestedProductId ?? '',
                                )
                              }
                              disabled={
                                (mappingInputs[row.name] ?? row.suggestedProductId ?? '').trim()
                                  .length === 0
                              }
                            >
                              Map
                            </Button>
                          </div>
                        ),
                      },
                    ]}
                    rows={dashboard.unmappedSoftware}
                    emptyMessage="Every inventoried software is mapped, or no inventory snapshots exist yet."
                  />
                  {mappings.length > 0 && (
                    <DetailsDisclosure summary={`Existing mappings (${mappings.length})`}>
                      <DataTable
                        columns={[
                          { header: 'Software', cell: (mapping) => mapping.softwareName },
                          { header: 'opsi product', cell: (mapping) => mapping.opsiProductId },
                          {
                            header: '',
                            cell: (mapping: ProductMapping) => (
                              <Button variant="ghost" onClick={() => deleteMapping(mapping.softwareName)}>
                                Remove
                              </Button>
                            ),
                          },
                        ]}
                        rows={mappings}
                        emptyMessage="No mappings."
                      />
                    </DetailsDisclosure>
                  )}
                </div>
              </Card>
            </>
          )}

          <Card title="Audit history">
            <DataTable
              columns={[
                {
                  header: 'Time',
                  cell: (entry: PatchAuditEntry) => formatTimestamp(entry.timestampUtc),
                },
                { header: 'User', cell: (entry: PatchAuditEntry) => entry.userName },
                { header: 'Action', cell: (entry: PatchAuditEntry) => entry.action },
                { header: 'Product', cell: (entry: PatchAuditEntry) => entry.productId ?? '—' },
                {
                  header: 'Targets',
                  cell: (entry: PatchAuditEntry) =>
                    entry.targetClients.length > 0
                      ? `${entry.targetClients.length}: ${entry.targetClients.join(', ')}`
                      : '—',
                },
                {
                  header: 'Result',
                  cell: (entry: PatchAuditEntry) => (
                    <StatusBadge
                      variant={
                        entry.result === 'SUCCESS'
                          ? 'success'
                          : entry.result === 'FAILED'
                            ? 'error'
                            : 'info'
                      }
                    >
                      {entry.result}
                    </StatusBadge>
                  ),
                },
                {
                  header: 'Error',
                  cell: (entry: PatchAuditEntry) => entry.errorMessage ?? '—',
                },
              ]}
              rows={audit}
              emptyMessage="No patch management actions recorded yet."
            />
          </Card>
        </>
      )}

      {!connected && status !== null && dashboard === null && (
        <EmptyState
          title="No opsi connection"
          message="Connect to an opsi server above to see products, affected clients and rollout state. Nothing is changed on the server without an explicit, confirmed action."
        />
      )}
    </div>
  );
}
