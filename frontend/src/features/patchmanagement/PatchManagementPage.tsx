import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AuditLogResult,
  MappingsResult,
  OpsiConnectionStatusResult,
  PatchAuditEntry,
  PatchClientState,
  PatchDashboardResult,
  PatchPackageStatus,
  PatchProductRow,
  PatchWorkflowState,
  PackageUpdateOutcome,
  PackageWorkflowStatus,
  PreparePackagesPlan,
  ProductVersionSource,
  ProductMapping,
  RolloutPreview,
  RolloutRequestOutcome,
  VersionCheckResult,
  VersionSourcesResult,
} from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Field } from '../../shared/ui/Field';
import { Input } from '../../shared/ui/Input';
import { loadView, saveView } from '../../shared/viewCache';

/** What survives an app restart for this page — never the password (ADR 0008). */
interface CachedPatchView {
  schemaVersion: number;
  server: string;
  userName: string;
  depotFilter: string;
  dashboard: PatchDashboardResult | null;
}

const patchViewKey = 'patchmanagement';
const patchViewSchemaVersion = 2;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

/**
 * Upgrades the locally cached pre-central-dashboard shape. The cache survives
 * application updates, so TypeScript types alone cannot guarantee that newly
 * added arrays exist at runtime.
 */
export function normalizeCachedPatchView(value: unknown): CachedPatchView | null {
  if (!isRecord(value)) return null;

  const server = typeof value.server === 'string' ? value.server : '';
  const userName = typeof value.userName === 'string' ? value.userName : '';
  const depotFilter = typeof value.depotFilter === 'string' ? value.depotFilter : '';
  if (value.dashboard === null) {
    return { schemaVersion: patchViewSchemaVersion, server, userName, depotFilter, dashboard: null };
  }
  if (!isRecord(value.dashboard)
      || !Array.isArray(value.dashboard.depots)
      || !Array.isArray(value.dashboard.products)
      || !isRecord(value.dashboard.summary)) {
    return null;
  }

  const rawDashboard = value.dashboard;
  const depots = rawDashboard.depots as PatchDashboardResult['depots'];
  const rawProducts = rawDashboard.products as unknown[];
  const summary = rawDashboard.summary as Record<string, unknown>;
  const relevantDepotIds = depotFilter
    ? [depotFilter]
    : depots.map((depot) => depot.id);
  const products: PatchProductRow[] = rawProducts
    .filter(isRecord)
    .map((rawProduct): PatchProductRow => {
      const depotVersions = Array.isArray(rawProduct.depotVersions)
        ? rawProduct.depotVersions as PatchProductRow['depotVersions']
        : [];
      const presentDepots = new Set(depotVersions.map((entry) => entry.depotId.toLocaleLowerCase()));
      const missingDepotIds = Array.isArray(rawProduct.missingDepotIds)
        ? rawProduct.missingDepotIds as string[]
        : relevantDepotIds.filter((depotId) => !presentDepots.has(depotId.toLocaleLowerCase()));
      const distinctDepotVersions = new Set(depotVersions.map((entry) => entry.version)).size;
      const failedClientCount = typeof rawProduct.failedClientCount === 'number'
        ? rawProduct.failedClientCount
        : 0;
      const pendingActionCount = typeof rawProduct.pendingActionCount === 'number'
        ? rawProduct.pendingActionCount
        : 0;
      const outdatedClientCount = typeof rawProduct.outdatedClientCount === 'number'
        ? rawProduct.outdatedClientCount
        : 0;
      const packageStatus: PatchPackageStatus = typeof rawProduct.packageStatus === 'string'
        ? rawProduct.packageStatus as PatchPackageStatus
        : failedClientCount > 0
          ? 'CHECK_FAILED'
          : missingDepotIds.length > 0
            ? 'MISSING_ON_DEPOT'
            : distinctDepotVersions > 1
              ? 'DEPOT_DEVIATION'
              : pendingActionCount > 0
                ? 'DEPLOYMENT_PENDING'
                : outdatedClientCount > 0
                  ? 'UPDATE_AVAILABLE'
                  : 'CURRENT';

      return {
        ...rawProduct,
        referenceVersion: typeof rawProduct.referenceVersion === 'string'
          ? rawProduct.referenceVersion
          : typeof rawProduct.availableVersion === 'string'
            ? rawProduct.availableVersion
            : null,
        manufacturerVersion: typeof rawProduct.manufacturerVersion === 'string'
          ? rawProduct.manufacturerVersion
          : null,
        manufacturerCheckStatus: typeof rawProduct.manufacturerCheckStatus === 'string'
          ? rawProduct.manufacturerCheckStatus
          : 'NOT_CONFIGURED',
        manufacturerCheckedAtUtc: typeof rawProduct.manufacturerCheckedAtUtc === 'string'
          ? rawProduct.manufacturerCheckedAtUtc
          : null,
        manufacturerCheckError: typeof rawProduct.manufacturerCheckError === 'string'
          ? rawProduct.manufacturerCheckError
          : null,
        manufacturerUpdateAvailable: rawProduct.manufacturerUpdateAvailable === true,
        depotVersions,
        missingDepotIds,
        packageStatus,
        clients: Array.isArray(rawProduct.clients) ? rawProduct.clients : [],
        mappedSoftwareNames: Array.isArray(rawProduct.mappedSoftwareNames)
          ? rawProduct.mappedSoftwareNames
          : [],
        inventoryDetections: Array.isArray(rawProduct.inventoryDetections)
          ? rawProduct.inventoryDetections
          : [],
      } as PatchProductRow;
    });
  const normalizedDashboard = {
    ...rawDashboard,
    summary: {
      ...summary,
      productsWithDepotDeviation: typeof summary.productsWithDepotDeviation === 'number'
        ? summary.productsWithDepotDeviation
        : products.filter((product) => new Set(
            product.depotVersions.map((entry) => entry.version),
          ).size > 1).length,
      productsMissingOnDepots: typeof summary.productsMissingOnDepots === 'number'
        ? summary.productsMissingOnDepots
        : products.filter((product) => product.missingDepotIds.length > 0).length,
      outdatedClientCount: typeof summary.outdatedClientCount === 'number'
        ? summary.outdatedClientCount
        : products.reduce((total, product) => total + product.outdatedClientCount, 0),
    },
    depots,
    products,
    unmappedSoftware: Array.isArray(rawDashboard.unmappedSoftware)
      ? rawDashboard.unmappedSoftware
      : [],
  } as PatchDashboardResult;

  return {
    schemaVersion: patchViewSchemaVersion,
    server,
    userName,
    depotFilter,
    dashboard: normalizedDashboard,
  };
}
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
  DETECTED: { label: 'Erkannt', variant: 'neutral' },
  UPDATE_AVAILABLE: { label: 'Update verfügbar', variant: 'elevation' },
  DOWNLOAD_NEEDED: { label: 'Download erforderlich', variant: 'elevation' },
  PACKAGE_PREPARED: { label: 'Paket vorbereitet', variant: 'info' },
  UPLOADED: { label: 'Hochgeladen', variant: 'info' },
  READY_FOR_PILOT: { label: 'Für Testgruppe bereit', variant: 'info' },
  APPROVED: { label: 'Freigegeben', variant: 'info' },
  ROLLOUT_REQUESTED: { label: 'Deployment angefordert', variant: 'info' },
  COMPLETED: { label: 'Aktuell', variant: 'success' },
  FAILED: { label: 'Fehlgeschlagen', variant: 'error' },
};

const packageBadges: Record<PatchPackageStatus, { label: string; variant: StatusBadgeVariant }> = {
  CURRENT: { label: 'Aktuell', variant: 'success' },
  UPDATE_AVAILABLE: { label: 'Update verfügbar', variant: 'elevation' },
  DEPOT_DEVIATION: { label: 'Depot-Abweichung', variant: 'elevation' },
  MISSING_ON_DEPOT: { label: 'Paket fehlt auf Depot', variant: 'error' },
  CHECK_FAILED: { label: 'Prüfung fehlgeschlagen', variant: 'error' },
  DEPLOYMENT_PENDING: { label: 'Deployment ausstehend', variant: 'info' },
};

function PackageBadge({ status }: { status: PatchPackageStatus }) {
  const badge = packageBadges[status] ?? { label: status, variant: 'neutral' as StatusBadgeVariant };
  return <StatusBadge variant={badge.variant}>{badge.label}</StatusBadge>;
}

function WorkflowBadge({ state }: { state: PatchWorkflowState }) {
  const badge = stateBadges[state] ?? { label: state, variant: 'neutral' as StatusBadgeVariant };
  return <StatusBadge variant={badge.variant}>{badge.label}</StatusBadge>;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

const clientDnsSuffix = '.kauth.local';

/** Shortens client FQDNs for display without changing their opsi identifier. */
export function formatClientName(clientId: string): string {
  return clientId.toLocaleLowerCase().endsWith(clientDnsSuffix)
    ? clientId.slice(0, -clientDnsSuffix.length)
    : clientId;
}

function ClientName({ clientId }: { clientId: string }) {
  const displayName = formatClientName(clientId);
  return <span title={displayName === clientId ? undefined : clientId}>{displayName}</span>;
}

/** Which slice of a product's clients a count click drills into. */
type ClientDrillFilter = 'installed' | 'UPDATE_AVAILABLE' | 'FAILED' | 'ROLLOUT_REQUESTED';

const drillFilterLabels: Record<ClientDrillFilter, string> = {
  installed: 'Installiert',
  UPDATE_AVAILABLE: 'Veraltet',
  FAILED: 'Fehlgeschlagen',
  ROLLOUT_REQUESTED: 'Ausstehend',
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

type AsyncError = { message: string; hint?: string } | null;
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
  // The stored view *is* the initial state — a restart opens on the last dashboard
  // and the server/user it came from. The password is never cached (ADR 0008).
  const cached = useRef(normalizeCachedPatchView(loadView<unknown>(patchViewKey))).current;
  const [status, setStatus] = useState<OpsiConnectionStatusResult | null>(null);

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
  const [packageWorkflow, setPackageWorkflow] = useState<PackageWorkflowStatus | null>(null);
  const [packageReviewed, setPackageReviewed] = useState(false);
  const [packageBusy, setPackageBusy] = useState(false);
  const [packageOutcome, setPackageOutcome] = useState<PackageUpdateOutcome | null>(null);
  const [packageTestDepotId, setPackageTestDepotId] = useState('');
  const [pilotApprovalReviewed, setPilotApprovalReviewed] = useState(false);

  const [mappings, setMappings] = useState<ProductMapping[]>([]);
  const [mappingInputs, setMappingInputs] = useState<Record<string, string>>({});
  const [mappingError, setMappingError] = useState<AsyncError>(null);

  const [audit, setAudit] = useState<PatchAuditEntry[]>([]);
  const [versionSources, setVersionSources] = useState<ProductVersionSource[]>([]);
  const [versionSourceForm, setVersionSourceForm] = useState({
    productId: '',
    sourceUrl: '',
    versionPattern: '',
  });
  const [versionCheckBusy, setVersionCheckBusy] = useState(false);
  const [versionSourceError, setVersionSourceError] = useState<AsyncError>(null);
  const [section, setSection] = useState<PatchSection>('overview');
  const [productSearch, setProductSearch] = useState('');
  const [packageStatusFilter, setPackageStatusFilter] = useState<PatchPackageStatus | ''>('');

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

  const loadVersionSources = useCallback(() => {
    invoke<VersionSourcesResult>('patchmanagement', 'listVersionSources', {})
      .then((result) => setVersionSources(result.sources))
      .catch(() => setVersionSources([]));
  }, []);

  const loadPackageWorkflow = useCallback((productId: string) => {
    invoke<PackageWorkflowStatus>('patchmanagement', 'getPackageWorkflowStatus', { productId })
      .then((result) => {
        setPackageWorkflow(result);
        if (result.testDepotId) setPackageTestDepotId(result.testDepotId);
      })
      .catch(() => setPackageWorkflow(null));
  }, []);

  const loadDashboard = useCallback((filter: string) => {
    setDashboardLoading(true);
    setDashboardError(null);
    invoke<PatchDashboardResult>(
      'patchmanagement',
      'getDashboard',
      { depotFilter: filter.length > 0 ? filter : null },
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
    loadVersionSources();
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus', {})
      .then((result) => {
        setStatus(result);
        if (result.connected) {
          loadDashboard('');
        }
      })
      .catch(() => setStatus(null));
  }, [loadDashboard, loadMappings, loadAudit, loadVersionSources]);

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

  useEffect(() => {
    if (dashboard === null || packageTestDepotId !== '') return;
    const preferred = dashboard.depots.find((depot) =>
      depot.description?.toLowerCase().includes('test'))
      ?? dashboard.depots.find((depot) => !depot.isConfigServer)
      ?? dashboard.depots[0];
    if (preferred) setPackageTestDepotId(preferred.id);
  }, [dashboard, packageTestDepotId]);

  useEffect(() => {
    if (selectedProductId && status?.connected) {
      loadPackageWorkflow(selectedProductId);
    } else {
      setPackageWorkflow(null);
    }
  }, [selectedProductId, status?.connected, loadPackageWorkflow]);

  // Remember the dashboard and the server/user behind it. Only these fields — the
  // password is deliberately not part of the cached shape.
  useEffect(() => {
    saveView<CachedPatchView>(patchViewKey, {
      schemaVersion: patchViewSchemaVersion,
      server: status?.serverUrl ?? cached?.server ?? '',
      userName: status?.userName ?? cached?.userName ?? '',
      depotFilter,
      dashboard,
    });
  }, [status?.serverUrl, status?.userName, cached, depotFilter, dashboard]);

  const resetActionPanels = useCallback(() => {
    setPreview(null);
    setPreviewError(null);
    setPreviewReviewed(false);
    setRolloutOutcome(null);
    setPackagePlan(null);
    setPackageReviewed(false);
    setPackageOutcome(null);
    setPilotApprovalReviewed(false);
  }, []);

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
    )
      .then((outcome) => {
        setRolloutOutcome(
          `Deployment für ${outcome.requestedClientCount} Client(s) angefordert — opsi installiert beim nächsten Action-Check.`,
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
    (product: PatchProductRow, stage: 'TEST' | 'DEPOT_SYNC') => {
      setPackagePlan(null);
      setPackageReviewed(false);
      setPackageOutcome(null);
      const depotIds = stage === 'TEST'
        ? [packageTestDepotId]
        : (dashboard?.depots ?? [])
            .map((depot) => depot.id)
            .filter((depotId) => depotId !== packageWorkflow?.testDepotId);
      invoke<PreparePackagesPlan>('patchmanagement', 'preparePackages', {
        productId: product.productId,
        stage,
        depotIds,
      })
        .then((plan) => {
          setPackagePlan(plan);
          loadAudit();
        })
        .catch((error: unknown) => setPreviewError(opsiError(error)));
    },
    [dashboard?.depots, packageTestDepotId, packageWorkflow?.testDepotId, loadAudit],
  );

  const executePackageUpdate = useCallback(() => {
    if (packagePlan === null) return;
    setPackageBusy(true);
    setPackageOutcome(null);
    setPreviewError(null);
    invoke<PackageUpdateOutcome>('patchmanagement', 'executePackageUpdate', {
      productId: packagePlan.productId,
      stage: packagePlan.stage,
      depotIds: packagePlan.targets.map((target) => target.depotId),
      confirmed: true,
    })
      .then((outcome) => {
        setPackageOutcome(outcome);
        setPackagePlan(null);
        setPackageReviewed(false);
        loadPackageWorkflow(outcome.productId);
        loadDashboard(depotFilter);
        loadAudit();
      })
      .catch((error: unknown) => {
        setPreviewError(opsiError(error));
        loadAudit();
      })
      .finally(() => setPackageBusy(false));
  }, [packagePlan, depotFilter, loadAudit, loadDashboard, loadPackageWorkflow]);

  const approvePackagePilot = useCallback((productId: string) => {
    setPackageBusy(true);
    setPreviewError(null);
    invoke<PackageWorkflowStatus>('patchmanagement', 'approvePackagePilot', {
      productId,
      confirmed: true,
    })
      .then((result) => {
        setPackageWorkflow(result);
        setPilotApprovalReviewed(false);
        loadAudit();
      })
      .catch((error: unknown) => setPreviewError(opsiError(error)))
      .finally(() => setPackageBusy(false));
  }, [loadAudit]);

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

  const checkVendorVersions = useCallback((productIds?: string[]) => {
    setVersionCheckBusy(true);
    setVersionSourceError(null);
    invoke<VersionCheckResult>('patchmanagement', 'checkVendorVersions', {
      productIds: productIds ?? null,
    })
      .then(() => {
        loadVersionSources();
        loadDashboard(depotFilter);
        loadAudit();
      })
      .catch((error: unknown) => setVersionSourceError(opsiError(error)))
      .finally(() => setVersionCheckBusy(false));
  }, [depotFilter, loadAudit, loadDashboard, loadVersionSources]);

  const saveVersionSource = useCallback(() => {
    setVersionSourceError(null);
    invoke<VersionSourcesResult>('patchmanagement', 'saveVersionSource', {
      ...versionSourceForm,
      enabled: true,
    })
      .then((result) => {
        setVersionSources(result.sources);
        setVersionSourceForm({ productId: '', sourceUrl: '', versionPattern: '' });
      })
      .catch((error: unknown) => setVersionSourceError(opsiError(error)));
  }, [versionSourceForm]);

  const deleteVersionSource = useCallback((productId: string) => {
    invoke<VersionSourcesResult>('patchmanagement', 'deleteVersionSource', { productId })
      .then((result) => {
        setVersionSources(result.sources);
        if (status?.connected) loadDashboard(depotFilter);
      })
      .catch((error: unknown) => setVersionSourceError(opsiError(error)));
  }, [status?.connected, depotFilter, loadDashboard]);

  const connected = status?.connected === true;
  // A restored dashboard with no live session: readable, but nothing may act on it.
  const stale = !connected && dashboard !== null;
  const selectedProduct =
    dashboard?.products.find((product) => product.productId === selectedProductId) ?? null;
  const visibleProducts = useMemo(() => {
    const needle = productSearch.trim().toLocaleLowerCase();
    return (dashboard?.products ?? []).filter((product) => {
      const matchesSearch = needle.length === 0
        || product.productId.toLocaleLowerCase().includes(needle)
        || (product.name ?? '').toLocaleLowerCase().includes(needle);
      return matchesSearch
        && (packageStatusFilter === '' || product.packageStatus === packageStatusFilter);
    });
  }, [dashboard?.products, productSearch, packageStatusFilter]);
  const clientRows = useMemo(
    () => (dashboard?.products ?? []).flatMap((product) =>
      product.clients.map((client) => ({ product, client }))),
    [dashboard?.products],
  );

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Patch Management"
        subtitle="Zentrale Übersicht für Paketstände, Depot-Konsistenz und kontrollierte Software-Rollouts."
      >
        {connected ? (
          <StatusBadge variant="success">
            Verbunden: {status?.serverUrl} als {status?.userName}
            {status?.opsiVersion ? ` (opsi ${status.opsiVersion})` : ''}
          </StatusBadge>
        ) : (
          <StatusBadge variant="neutral">Nicht verbunden</StatusBadge>
        )}
      </PageHeader>

      {status?.connectionError && (
        <ErrorState
          title="Automatische opsi-Verbindung fehlgeschlagen"
          message={status.connectionError}
          hint="Prüfe Server, Zertifikatsvertrauen und das gespeicherte opsi-Konto unter Einstellungen."
        />
      )}

      {!connected && (
        <Card title="opsi-Verbindung">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-slate-400">
              WEC verbindet sich hier automatisch mit dem sicher gespeicherten opsi-Konto. Server und Konto befinden sich in den Einstellungen.
            </p>
            <a href="#/settings" className="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800">
              Zu den Einstellungen
            </a>
          </div>
        </Card>
      )}

      {(connected || dashboard !== null) && (
        <>
          {stale && (
            <p className="rounded border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-slate-300">
              Gespeicherte Ansicht vom{' '}
              {dashboard ? formatTimestamp(dashboard.generatedAtUtc) : 'the last session'} — not
              — ohne aktive opsi-Verbindung. Zum Aktualisieren oder Ausführen von Aktionen erneut verbinden.
            </p>
          )}
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-400">Standort / Depot</span>
              <Select
                aria-label="Depotfilter"
                fullWidth={false}
                value={depotFilter}
                disabled={!connected}
                onChange={(event) => changeDepotFilter(event.target.value)}
              >
                <option value="">Alle Depots</option>
                {(dashboard?.depots ?? []).map((depot) => (
                  <option key={depot.id} value={depot.id}>
                    {depot.description ? `${depot.description} (${depot.id})` : depot.id}
                  </option>
                ))}
              </Select>
            </label>
            <Button onClick={() => loadDashboard(depotFilter)} disabled={dashboardLoading || !connected}>
              Übersicht aktualisieren
            </Button>
            {dashboardLoading && <Spinner label="Patch-Übersicht wird geladen" />}
            {dashboard && (
              <span className="text-xs text-slate-500">
                Stand {formatTimestamp(dashboard.generatedAtUtc)}
                {dashboard.depotFilter ? ` — Depot ${dashboard.depotFilter}` : ' — alle Depots'}
              </span>
            )}
          </div>

          {dashboardError && (
            <ErrorState
              title="Übersicht nicht verfügbar"
              message={dashboardError.message}
              hint={dashboardError.hint}
            />
          )}

          {dashboard && (
            <>
              <div role="tablist" aria-label="Patch-Management-Bereiche" className="flex gap-1 overflow-x-auto border-b border-slate-800">
                <SectionTab active={section === 'overview'} onClick={() => setSection('overview')}>
                  Paketübersicht
                </SectionTab>
                <SectionTab active={section === 'clients'} onClick={() => setSection('clients')}>
                  Clients{' '}
                  <span className="text-slate-500">
                    ({dashboard.summary.clientCount.toLocaleString('de-DE')})
                  </span>
                </SectionTab>
                <SectionTab active={section === 'history'} onClick={() => setSection('history')}>
                  Historie
                </SectionTab>
                <SectionTab active={section === 'automation'} onClick={() => setSection('automation')}>
                  Automatisierung
                </SectionTab>
                <SectionTab active={section === 'mappings'} onClick={() => setSection('mappings')}>
                  Zuordnungen
                </SectionTab>
              </div>

              {section === 'overview' && <div className="flex flex-wrap gap-3">
                <SummaryMetric label="Pakete" value={dashboard.summary.productCount} />
                <SummaryMetric
                  label="Updates verfügbar"
                  value={dashboard.summary.productsWithUpdates}
                  tone={dashboard.summary.productsWithUpdates > 0 ? 'warning' : 'success'}
                />
                <SummaryMetric
                  label="Depot-Abweichungen"
                  value={dashboard.summary.productsWithDepotDeviation}
                  tone={dashboard.summary.productsWithDepotDeviation > 0 ? 'warning' : 'success'}
                />
                <SummaryMetric
                  label="Auf Depots fehlend"
                  value={dashboard.summary.productsMissingOnDepots}
                  tone={dashboard.summary.productsMissingOnDepots > 0 ? 'danger' : 'success'}
                />
                <SummaryMetric
                  label="Fehler"
                  value={dashboard.summary.productsWithFailures}
                  tone={dashboard.summary.productsWithFailures > 0 ? 'danger' : 'success'}
                />
                <SummaryMetric
                  label="Veraltete Clients"
                  value={dashboard.summary.outdatedClientCount}
                  tone={dashboard.summary.outdatedClientCount > 0 ? 'warning' : 'success'}
                />
                <SummaryMetric
                  label="Ausstehende Deployments"
                  value={dashboard.summary.pendingRolloutCount}
                  tone={dashboard.summary.pendingRolloutCount > 0 ? 'info' : 'neutral'}
                />
              </div>}

              {section === 'overview' && <>
              {(dashboard.summary.productsWithFailures > 0
                || dashboard.summary.productsMissingOnDepots > 0
                || dashboard.summary.productsWithDepotDeviation > 0) && (
                <div className="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-lg border border-warn-700/70 bg-warn-950/25 px-4 py-3 text-sm">
                  <span className="font-medium text-warn-300">Handlungsbedarf</span>
                  {dashboard.summary.productsWithFailures > 0 && (
                    <span className="text-fail-300">{dashboard.summary.productsWithFailures} fehlgeschlagene Pakete</span>
                  )}
                  {dashboard.summary.productsMissingOnDepots > 0 && (
                    <span className="text-warn-300">{dashboard.summary.productsMissingOnDepots} Pakete fehlen auf Depots</span>
                  )}
                  {dashboard.summary.productsWithDepotDeviation > 0 && (
                    <span className="text-warn-300">{dashboard.summary.productsWithDepotDeviation} Versionsabweichungen</span>
                  )}
                </div>
              )}

              <div className="flex flex-wrap items-end gap-3 rounded-lg border border-slate-800 bg-slate-900/60 p-3">
                <label className="min-w-56 flex-1 text-xs font-medium uppercase tracking-wide text-slate-500">
                  Paket suchen
                  <Input
                    type="search"
                    value={productSearch}
                    onChange={(event) => setProductSearch(event.target.value)}
                    placeholder="Name oder Produkt-ID"
                    className="mt-1"
                  />
                </label>
                <label className="text-xs font-medium uppercase tracking-wide text-slate-500">
                  Status
                  <Select
                    aria-label="Statusfilter"
                    value={packageStatusFilter}
                    onChange={(event) => setPackageStatusFilter(event.target.value as PatchPackageStatus | '')}
                    className="mt-1"
                  >
                    <option value="">Alle Status</option>
                    {Object.entries(packageBadges).map(([value, badge]) => (
                      <option key={value} value={value}>{badge.label}</option>
                    ))}
                  </Select>
                </label>
                <span className="pb-2 text-xs text-slate-500">
                  {visibleProducts.length} von {dashboard.products.length} Paketen
                </span>
              </div>

              <div
                className={
                  selectedProduct
                    ? 'grid items-start gap-4 lg:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)]'
                    : ''
                }
              >
              <Card title="Softwarestände auf den opsi-Depots">
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
                          <span className="block font-medium">{row.name ?? row.productId}</span>
                          <span className="block font-mono text-xs text-slate-500">{row.productId}</span>
                        </button>
                      ),
                    },
                    {
                      header: 'opsi-Referenz',
                      mono: true,
                      cell: (row: PatchProductRow) => row.referenceVersion ?? '—',
                    },
                    {
                      header: 'Hersteller',
                      mono: true,
                      cell: (row: PatchProductRow) => (
                        <span className={row.manufacturerUpdateAvailable ? 'text-warn-300' : 'text-slate-300'}>
                          {row.manufacturerVersion ?? (row.manufacturerCheckStatus === 'FAILED' ? 'Fehler' : '—')}
                        </span>
                      ),
                    },
                    {
                      header: 'Depots',
                      cell: (row: PatchProductRow) => (
                        <span className={row.missingDepotIds.length > 0 ? 'text-fail-300' : 'text-slate-300'}>
                          {row.depotVersions.length}/{depotFilter ? 1 : dashboard.depots.length}
                        </span>
                      ),
                    },
                    { header: 'Status', cell: (row: PatchProductRow) => <PackageBadge status={row.packageStatus} /> },
                    {
                      header: 'Installiert',
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
                      header: 'Veraltet',
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
                      header: 'Fehler',
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
                      header: 'Ausstehend',
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
                      header: '',
                      cell: (row: PatchProductRow) => (
                        <Button variant="ghost" onClick={() => selectProduct(row.productId)}>
                          Details
                        </Button>
                      ),
                    },
                  ]}
                  rows={visibleProducts}
                  getRowKey={(row) => row.productId}
                  emptyMessage="Keine Pakete entsprechen den gewählten Filtern."
                />
              </Card>

              {selectedProduct && (
                <div className="lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-y-auto">
                <Card title={`Paketdetails — ${selectedProduct.name ?? selectedProduct.productId}`}>
                  <div className="flex flex-col gap-4">
                    <div className="-mt-1 flex justify-end">
                      <Button variant="ghost" onClick={() => selectProduct(selectedProduct.productId)}>
                        Schließen
                      </Button>
                    </div>
                    {selectedProduct.lastError && (
                      <p className="text-sm text-fail-400">{selectedProduct.lastError}</p>
                    )}
                    <div className="flex flex-wrap items-center gap-2">
                      <PackageBadge status={selectedProduct.packageStatus} />
                      <span className="font-mono text-xs text-slate-500">{selectedProduct.productId}</span>
                    </div>

                    <div className="grid gap-2 sm:grid-cols-2">
                      {(depotFilter
                        ? dashboard.depots.filter((depot) => depot.id === depotFilter)
                        : dashboard.depots
                      ).map((depot) => {
                        const depotVersion = selectedProduct.depotVersions.find(
                          (version) => version.depotId === depot.id,
                        );
                        return (
                          <div key={depot.id} className={`rounded border p-2 ${
                            depotVersion ? 'border-slate-800 bg-slate-950/50' : 'border-fail-900 bg-fail-950/30'
                          }`}>
                            <div className="truncate text-xs text-slate-500" title={depot.id}>
                              {depot.description ?? depot.id}
                            </div>
                            <div className={`mt-1 font-mono text-sm ${depotVersion ? 'text-slate-200' : 'text-fail-300'}`}>
                              {depotVersion?.version ?? 'Paket fehlt'}
                            </div>
                          </div>
                        );
                      })}
                    </div>

                    <div className="rounded border border-slate-800 bg-slate-950/40 p-3">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div>
                          <div className="flex items-center gap-2 text-sm font-medium text-slate-200">
                            Hersteller-Version
                            {selectedProduct.manufacturerCheckStatus === 'SUCCESS' && <StatusBadge variant="success">Geprüft</StatusBadge>}
                            {selectedProduct.manufacturerCheckStatus === 'FAILED' && <StatusBadge variant="error">Prüfung fehlgeschlagen</StatusBadge>}
                          </div>
                          <p className="mt-0.5 text-xs text-slate-500">
                            {selectedProduct.manufacturerVersion
                              ? `Neueste Version: ${selectedProduct.manufacturerVersion}${selectedProduct.manufacturerCheckedAtUtc ? ` · geprüft ${formatTimestamp(selectedProduct.manufacturerCheckedAtUtc)}` : ''}`
                              : selectedProduct.manufacturerCheckError
                                ? selectedProduct.manufacturerCheckError
                                : 'Keine Herstellerquelle für dieses Paket konfiguriert. Es wird kein Versionsstand geschätzt.'}
                          </p>
                        </div>
                        <Button
                          disabled={!connected || versionCheckBusy || selectedProduct.manufacturerCheckStatus === 'NOT_CONFIGURED'}
                          onClick={() => checkVendorVersions([selectedProduct.productId])}
                        >
                          {versionCheckBusy ? 'Prüfung läuft…' : 'Version prüfen'}
                        </Button>
                      </div>
                    </div>

                    {clientFilter && (
                      <div className="flex items-center gap-2 text-sm">
                        <span className="text-slate-400">Anzeige:</span>
                        <StatusBadge variant="info">{drillFilterLabels[clientFilter]}</StatusBadge>
                        <Button variant="ghost" onClick={() => setClientFilter(null)}>
                          Alle Clients anzeigen
                        </Button>
                      </div>
                    )}

                    <DataTable
                      columns={[
                        {
                          header: 'Auswahl',
                          cell: (client: PatchClientState) => (
                            <input
                              type="checkbox"
                              aria-label={`Client ${formatClientName(client.clientId)} auswählen`}
                              checked={selectedClients.has(client.clientId)}
                              onChange={() => toggleClient(client.clientId)}
                            />
                          ),
                        },
                        {
                          header: 'Client',
                          cell: (client: PatchClientState) => <ClientName clientId={client.clientId} />,
                        },
                        {
                          header: 'Depot',
                          cell: (client: PatchClientState) => client.depotId ?? '—',
                        },
                        {
                          header: 'Installiert',
                          cell: (client: PatchClientState) => client.installedVersion ?? '—',
                        },
                        {
                          header: 'Zielversion',
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
                      emptyMessage="Für dieses Paket sind auf den gefilterten Clients keine Zustände vorhanden."
                    />

                    {selectedProduct.inventoryDetections.length > 0 && (
                      <DetailsDisclosure
                        summary={`WEC-Inventarisierungsfunde (${selectedProduct.inventoryDetections.length})`}
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

                    <div className="rounded border border-slate-800 bg-slate-950/40 p-3">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="flex items-center gap-2 text-sm font-medium text-slate-200">
                            Paket-Freigabekette
                            {packageWorkflow?.pilotApproved
                              ? <StatusBadge variant="success">Pilot freigegeben</StatusBadge>
                              : packageWorkflow?.testUpdateSucceededAtUtc
                                ? <StatusBadge variant="info">Testupdate erfolgreich</StatusBadge>
                                : <StatusBadge variant="neutral">Test ausstehend</StatusBadge>}
                          </div>
                          <p className="mt-1 text-xs text-slate-500">
                            {packageWorkflow?.testUpdateSucceededAtUtc
                              ? `${packageWorkflow.testDepotId}: ${packageWorkflow.testedVersion ?? 'Version bestätigt'} · ${formatTimestamp(packageWorkflow.testUpdateSucceededAtUtc)}`
                              : 'Zuerst ein Depot aktualisieren, Testclients deployen und das Ergebnis prüfen.'}
                          </p>
                          {packageWorkflow?.lastError && (
                            <p className="mt-1 text-xs text-fail-400">Letzter Paketfehler: {packageWorkflow.lastError}</p>
                          )}
                        </div>
                        <div className="min-w-56">
                          <Field label="Testdepot">
                            {(controlId) => (
                              <Select
                                id={controlId}
                                aria-label="Testdepot"
                                value={packageTestDepotId}
                                disabled={packageBusy}
                                onChange={(event) => {
                                  setPackageTestDepotId(event.target.value);
                                  setPackagePlan(null);
                                  setPackageReviewed(false);
                                }}
                              >
                                {(dashboard?.depots ?? []).map((depot) => (
                                  <option key={depot.id} value={depot.id}>
                                    {depot.id}{depot.description ? ` · ${depot.description}` : ''}
                                  </option>
                                ))}
                              </Select>
                            )}
                          </Field>
                        </div>
                      </div>
                    </div>

                    <div className="flex flex-wrap items-center gap-3">
                      <Button
                        variant="primary"
                        disabled={!connected}
                        onClick={() => loadPreview(selectedProduct)}
                      >
                        Deployment vorbereiten
                        {selectedClients.size > 0
                          ? ` (${selectedClients.size} selected)`
                          : ' (veraltete & fehlgeschlagene Clients)'}
                      </Button>
                      <Button
                        disabled={!connected || packageBusy || packageTestDepotId === ''}
                        onClick={() => planPackages(selectedProduct, 'TEST')}
                      >
                        Testupdate vorbereiten
                      </Button>
                      <Button
                        disabled={
                          !connected
                          || packageBusy
                          || !packageWorkflow?.pilotApproved
                          || (dashboard?.depots.length ?? 0) < 2
                        }
                        onClick={() => planPackages(selectedProduct, 'DEPOT_SYNC')}
                      >
                        Auf weitere Depots verteilen
                      </Button>
                      {stale && (
                        <span className="text-xs text-slate-500">
                          Für Vorschau und Deployment mit opsi verbinden.
                        </span>
                      )}
                    </div>

                    {previewError && (
                      <ErrorState
                        title="Aktion fehlgeschlagen"
                        message={previewError.message}
                        hint={previewError.hint}
                      />
                    )}
                    {rolloutOutcome && <p className="text-sm text-ok-400">{rolloutOutcome}</p>}

                    {packageWorkflow?.testUpdateSucceededAtUtc && !packageWorkflow.pilotApproved && (
                      <div className="flex flex-col gap-3 rounded border border-warn-700 bg-warn-950/30 p-3">
                        <p className="text-sm text-warn-300">
                          Das Paketupdate auf <strong>{packageWorkflow.testDepotId}</strong> war erfolgreich.
                          Führe jetzt das Deployment auf ausgewählten Testclients aus und prüfe das Ergebnis,
                          bevor du die Depot-Verteilung freigibst.
                        </p>
                        <label className="flex cursor-pointer items-center gap-2 text-sm text-warn-300">
                          <input
                            type="checkbox"
                            className="accent-accent-500"
                            checked={pilotApprovalReviewed}
                            onChange={(event) => setPilotApprovalReviewed(event.target.checked)}
                          />
                          Testinstallation und Anwendungstest waren erfolgreich; ich gebe diese Paketversion frei.
                        </label>
                        <div>
                          <Button
                            variant="primary"
                            disabled={!pilotApprovalReviewed || packageBusy}
                            onClick={() => approvePackagePilot(selectedProduct.productId)}
                          >
                            Pilot freigeben
                          </Button>
                        </div>
                      </div>
                    )}

                    {packageOutcome && (
                      <div className={`rounded border p-3 ${packageOutcome.failedTargetCount > 0 ? 'border-fail-700 bg-fail-950/20' : 'border-ok-700 bg-ok-950/20'}`}>
                        <p className="text-sm text-slate-200">
                          Paketaktion abgeschlossen: {packageOutcome.succeededTargetCount} erfolgreich,
                          {' '}{packageOutcome.failedTargetCount} fehlgeschlagen.
                        </p>
                        <ul className="mt-2 space-y-1 text-xs text-slate-400">
                          {packageOutcome.targets.map((target) => (
                            <li key={target.depotId}>
                              {target.depotId}: {target.success ? `${target.oldVersion ?? 'fehlend'} → ${target.newVersion ?? 'unbekannt'}` : target.error}
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {preview && (
                      <div className="flex flex-col gap-3 rounded border border-warn-700 bg-warn-950/30 p-3">
                        <p className="text-sm font-medium text-warn-300">
                          Deployment-Vorschau — Aktion „{preview.plannedAction}“ für{' '}
                          {preview.clients.length} Client(s)
                          {preview.depotFilter ? ` auf ${preview.depotFilter}` : ' über alle Depots'}.
                          Es wurde noch nichts an opsi übermittelt.
                        </p>
                        <DataTable
                          columns={[
                            {
                              header: 'Client',
                              cell: (client) => <ClientName clientId={client.clientId} />,
                            },
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
                              Ich habe die betroffenen Clients geprüft und möchte dieses Deployment anfordern.
                            </label>
                            <div>
                              <Button
                                variant="primary"
                                disabled={!previewReviewed || rolloutBusy}
                                onClick={requestRollout}
                              >
                                {rolloutBusy
                                  ? 'Deployment wird angefordert…'
                                  : `Deployment für ${preview.clients.length} Client(s) anfordern`}
                              </Button>
                            </div>
                          </>
                        )}
                      </div>
                    )}

                    {packagePlan && (
                      <div className="flex flex-col gap-3 rounded border border-warn-700 bg-warn-950/30 p-3">
                        <div className="flex flex-wrap items-center gap-2">
                          <StatusBadge variant={packagePlan.mode === 'REPOSITORY' ? 'info' : 'elevation'}>
                            {packagePlan.mode === 'CUSTOM_BUILD'
                              ? 'Herstellerpaket bauen'
                              : packagePlan.mode === 'CUSTOM_PROMOTION'
                                ? 'Geprüftes Paket verteilen'
                                : 'Repository-Paket'}
                          </StatusBadge>
                          {packagePlan.artifactVersion && (
                            <span className="text-xs text-slate-400">
                              Zielversion {packagePlan.artifactVersion}
                            </span>
                          )}
                        </div>
                        <p className="text-sm text-slate-300">{packagePlan.note}</p>
                        {packagePlan.targets.map((target) => (
                          <div key={target.depotId} className="grid gap-1">
                            <span className="text-xs text-slate-400">
                              {target.depotId} · aktuell {target.currentVersion ?? 'Paket fehlt'}
                            </span>
                            <code className="overflow-x-auto rounded bg-slate-900 p-2 text-sm text-accent-300">
                              {target.command}
                            </code>
                          </div>
                        ))}
                        <p className="text-xs text-slate-500">
                          Download und SSH/SCP nutzen begrenzte Transfers, Schlüssel/Agent, BatchMode und bereits vertrauenswürdige Host-Keys.
                          Es wird kein Passwort gespeichert oder interaktiv abgefragt.
                        </p>
                        <label className="flex cursor-pointer items-center gap-2 text-sm text-warn-300">
                          <input
                            type="checkbox"
                            className="accent-accent-500"
                            checked={packageReviewed}
                            onChange={(event) => setPackageReviewed(event.target.checked)}
                          />
                          {packagePlan.confirmationText}
                        </label>
                        <div>
                          <Button
                            variant="primary"
                            disabled={!packageReviewed || packageBusy}
                            onClick={executePackageUpdate}
                          >
                            {packageBusy
                              ? 'Paketaktion läuft…'
                              : packagePlan.mode === 'CUSTOM_BUILD'
                                ? 'Herstellerdatei laden und Testpaket bauen'
                                : 'Bestätigen und über SSH ausführen'}
                          </Button>
                        </div>
                      </div>
                    )}
                  </div>
                </Card>
                </div>
              )}
              </div>
              </>}

              {section === 'clients' && (
                <Card title="Client- und Deploymentstatus">
                  <p className="mb-3 text-xs text-slate-500">
                    {dashboard.summary.clientCount.toLocaleString('de-DE')} eindeutige Clients ·{' '}
                    {clientRows.length.toLocaleString('de-DE')}{' '}
                    {clientRows.length === 1 ? 'Paketstand' : 'Paketstände'}
                  </p>
                  <DataTable
                    columns={[
                      {
                        header: 'Client',
                        cell: (row) => <ClientName clientId={row.client.clientId} />,
                      },
                      { header: 'Paket', cell: (row) => row.product.name ?? row.product.productId },
                      { header: 'Depot', cell: (row) => row.client.depotId ?? '—' },
                      { header: 'Installiert', mono: true, cell: (row) => row.client.installedVersion ?? '—' },
                      { header: 'Zielversion', mono: true, cell: (row) => row.client.targetVersion ?? '—' },
                      { header: 'Status', cell: (row) => <WorkflowBadge state={row.client.state} /> },
                      {
                        header: '',
                        cell: (row) => (
                          <Button
                            variant="ghost"
                            onClick={() => {
                              drillIntoClients(row.product.productId, row.client.state === 'FAILED' ? 'FAILED' : row.client.state === 'ROLLOUT_REQUESTED' ? 'ROLLOUT_REQUESTED' : 'UPDATE_AVAILABLE');
                              setSection('overview');
                            }}
                          >
                            Paket öffnen
                          </Button>
                        ),
                      },
                    ]}
                    rows={clientRows}
                    getRowKey={(row) => `${row.product.productId}-${row.client.clientId}`}
                    emptyMessage="opsi meldet derzeit keine Clientzustände für die gewählte Ansicht."
                  />
                </Card>
              )}

              {section === 'automation' && (
                <div className="grid gap-4 lg:grid-cols-2">
                  <Card title="Regelmäßige Prüfungen">
                    <div className="flex flex-col gap-3 text-sm">
                      <div className="flex items-center justify-between gap-3 rounded border border-slate-800 p-3">
                        <div>
                          <div className="font-medium text-slate-200">Depot-Abgleich</div>
                          <div className="text-xs text-slate-500">Vergleicht Paketbestand und Versionen aller Depots.</div>
                        </div>
                        <StatusBadge variant="info">Bei Aktualisierung</StatusBadge>
                      </div>
                      <div className="flex items-center justify-between gap-3 rounded border border-slate-800 p-3">
                        <div>
                          <div className="font-medium text-slate-200">Hersteller-Versionscheck</div>
                          <div className="text-xs text-slate-500">
                            Fällige Quellen werden beim Öffnen der verbundenen Übersicht täglich geprüft.
                          </div>
                        </div>
                        <StatusBadge variant={versionSources.length > 0 ? 'success' : 'neutral'}>
                          {versionSources.length > 0 ? `${versionSources.length} aktiv` : 'Nicht konfiguriert'}
                        </StatusBadge>
                      </div>
                      <Button variant="primary" disabled={!connected || versionCheckBusy} onClick={() => checkVendorVersions()}>
                        {versionCheckBusy ? 'Prüfungen laufen…' : 'Alle Prüfungen jetzt starten'}
                      </Button>
                    </div>
                  </Card>
                  <Card title="Kontrollierter Rollout">
                    <ol className="flex flex-col gap-3 text-sm">
                      {[
                        ['1', 'Herstellerversion prüfen', 'Versionsquelle je Paket verifizieren'],
                        ['2', 'Testdepot aktualisieren', 'SSH-Vorschau bestätigen und Repository-Paket einspielen'],
                        ['3', 'Testgruppe deployen', 'Testclients auswählen, installieren und Anwendung prüfen'],
                        ['4', 'Freigeben und verteilen', 'Pilot freigeben und übrige Depots automatisch angleichen'],
                        ['5', 'Breit ausrollen', 'Veraltete Clients kontrolliert auf setup setzen'],
                      ].map(([number, title, description]) => (
                        <li key={number} className="flex gap-3">
                          <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-slate-800 text-xs text-slate-300">{number}</span>
                          <span><span className="block font-medium text-slate-200">{title}</span><span className="text-xs text-slate-500">{description}</span></span>
                        </li>
                      ))}
                    </ol>
                  </Card>
                  <div className="lg:col-span-2">
                    <Card title="Herstellerquellen">
                      <div className="flex flex-col gap-4">
                        <p className="text-sm text-slate-400">
                          Pro Paket eine HTTPS-Seite oder Release-API hinterlegen. Das reguläre
                          Ausdrucksmuster muss die Version in der ersten Klammer erfassen, z. B.
                          <code className="ml-1 text-accent-300">{'latest-version\\W+([0-9.]+)'}</code>.
                        </p>
                        <div className="grid gap-3 lg:grid-cols-[minmax(10rem,0.7fr)_minmax(16rem,1.3fr)_minmax(16rem,1.3fr)_auto]">
                          <Select
                            aria-label="opsi-Produkt für Herstellerquelle"
                            value={versionSourceForm.productId}
                            onChange={(event) => setVersionSourceForm((previous) => ({ ...previous, productId: event.target.value }))}
                          >
                            <option value="">Paket auswählen</option>
                            {dashboard.products.map((product) => (
                              <option key={product.productId} value={product.productId}>{product.name ?? product.productId}</option>
                            ))}
                          </Select>
                          <Input
                            aria-label="HTTPS-URL der Herstellerquelle"
                            value={versionSourceForm.sourceUrl}
                            onChange={(event) => setVersionSourceForm((previous) => ({ ...previous, sourceUrl: event.target.value }))}
                            placeholder="https://vendor.example/releases"
                          />
                          <Input
                            aria-label="Versionsmuster"
                            value={versionSourceForm.versionPattern}
                            onChange={(event) => setVersionSourceForm((previous) => ({ ...previous, versionPattern: event.target.value }))}
                            placeholder={'Version\\s+([0-9.]+)'}
                          />
                          <Button
                            variant="primary"
                            onClick={saveVersionSource}
                            disabled={!versionSourceForm.productId || !versionSourceForm.sourceUrl || !versionSourceForm.versionPattern}
                          >
                            Quelle speichern
                          </Button>
                        </div>
                        {versionSourceError && <ErrorState title="Herstellerprüfung fehlgeschlagen" message={versionSourceError.message} hint={versionSourceError.hint} />}
                        <DataTable
                          columns={[
                            { header: 'Paket', cell: (source: ProductVersionSource) => source.productId },
                            { header: 'Letzte Version', mono: true, cell: (source) => source.latestVersion ?? '—' },
                            { header: 'Letzte Prüfung', cell: (source) => source.lastCheckedUtc ? formatTimestamp(source.lastCheckedUtc) : 'Noch nicht geprüft' },
                            { header: 'Status', cell: (source) => <StatusBadge variant={source.checkStatus === 'FAILED' ? 'error' : source.checkStatus === 'SUCCESS' ? 'success' : 'neutral'}>{source.checkStatus}</StatusBadge> },
                            { header: 'Fehler', cell: (source) => source.lastError ?? '—' },
                            { header: '', cell: (source) => <Button variant="ghost" onClick={() => deleteVersionSource(source.productId)}>Entfernen</Button> },
                          ]}
                          rows={versionSources}
                          getRowKey={(source) => source.productId}
                          emptyMessage="Noch keine Herstellerquellen konfiguriert."
                        />
                      </div>
                    </Card>
                  </div>
                </div>
              )}

              {section === 'mappings' && <Card title="Inventarisierte Software ohne opsi-Zuordnung">
                <div className="flex flex-col gap-3">
                  <p className="text-sm text-slate-400">
                    Diese Software wurde von WEC inventarisiert, ist aber noch keinem opsi-Produkt
                    zugeordnet. Vorschläge entstehen ausschließlich bei exakten Namensübereinstimmungen.
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
                              Zuordnen
                            </Button>
                          </div>
                        ),
                      },
                    ]}
                    rows={dashboard.unmappedSoftware}
                    emptyMessage="Alle inventarisierten Programme sind zugeordnet oder es liegen noch keine Inventurdaten vor."
                  />
                  {mappings.length > 0 && (
                    <DetailsDisclosure summary={`Bestehende Zuordnungen (${mappings.length})`}>
                      <DataTable
                        columns={[
                          { header: 'Software', cell: (mapping) => mapping.softwareName },
                          { header: 'opsi product', cell: (mapping) => mapping.opsiProductId },
                          {
                            header: '',
                            cell: (mapping: ProductMapping) => (
                              <Button variant="ghost" onClick={() => deleteMapping(mapping.softwareName)}>
                                Entfernen
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
              </Card>}
            </>
          )}

          {section === 'history' && <Card title="Paket- und Deploymenthistorie">
            <DataTable
              columns={[
                {
                  header: 'Zeitpunkt',
                  cell: (entry: PatchAuditEntry) => formatTimestamp(entry.timestampUtc),
                },
                { header: 'Benutzer', cell: (entry: PatchAuditEntry) => entry.userName },
                { header: 'Aktion', cell: (entry: PatchAuditEntry) => entry.action },
                { header: 'Paket', cell: (entry: PatchAuditEntry) => entry.productId ?? '—' },
                { header: 'Depot', cell: (entry: PatchAuditEntry) => entry.depotId ?? '—' },
                {
                  header: 'Version',
                  cell: (entry: PatchAuditEntry) =>
                    entry.oldVersion || entry.newVersion
                      ? `${entry.oldVersion ?? 'fehlend'} → ${entry.newVersion ?? 'unbekannt'}`
                      : '—',
                },
                {
                  header: 'Ziele',
                  cell: (entry: PatchAuditEntry) =>
                    entry.targetClients.length > 0
                      ? `${entry.targetClients.length}: ${entry.targetClients.map(formatClientName).join(', ')}`
                      : '—',
                },
                {
                  header: 'Ergebnis',
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
                  header: 'Fehler',
                  cell: (entry: PatchAuditEntry) => entry.errorMessage ?? '—',
                },
              ]}
              rows={audit}
              emptyMessage="Noch keine Patch-Management-Aktionen protokolliert."
            />
          </Card>}
        </>
      )}

      {!connected && status !== null && dashboard === null && (
        <EmptyState
          title="Keine opsi-Verbindung"
          message="Mit einem opsi-Server verbinden, um Pakete, betroffene Clients und Deploymentstatus zu laden. Ohne ausdrückliche Bestätigung wird nichts auf dem Server verändert."
        />
      )}
    </div>
  );
}
