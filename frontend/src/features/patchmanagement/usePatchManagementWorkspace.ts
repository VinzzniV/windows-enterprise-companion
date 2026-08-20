import { useCallback, useEffect, useRef, useState } from 'react';
import type {
  OpsiConnectionStatusResult,
  PatchDashboardOverview,
  PatchPackageStatus,
  PatchProductOverviewRow,
  VersionCheckResult,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { loadView, saveView } from '../../shared/viewCache';
import { presentOpsiError } from './patchErrors';

/** What survives an app restart for this page — never the password (ADR 0008). */
interface CachedPatchView {
  schemaVersion: number;
  server: string;
  userName: string;
  depotFilter: string;
  dashboard: PatchDashboardOverview | null;
}

const patchViewKey = 'patchmanagement';
const patchViewSchemaVersion = 3;
const verifyVersionCheckAction =
  'First check the latest check times and history to see whether the manufacturer check already completed. Start it again only if no recent check is visible there.';

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
  const depots = rawDashboard.depots as PatchDashboardOverview['depots'];
  const rawProducts = rawDashboard.products as unknown[];
  const summary = rawDashboard.summary as Record<string, unknown>;
  const relevantDepotIds = depotFilter
    ? [depotFilter]
    : depots.map((depot) => depot.id);
  const products: PatchProductOverviewRow[] = rawProducts
    .filter(isRecord)
    .map((rawProduct): PatchProductOverviewRow => {
      const productWithoutClients = { ...rawProduct };
      delete productWithoutClients.clients;
      const depotVersions = Array.isArray(rawProduct.depotVersions)
        ? rawProduct.depotVersions as PatchProductOverviewRow['depotVersions']
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
        ...productWithoutClients,
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
        mappedSoftwareNames: Array.isArray(rawProduct.mappedSoftwareNames)
          ? rawProduct.mappedSoftwareNames
          : [],
        inventoryDetections: Array.isArray(rawProduct.inventoryDetections)
          ? rawProduct.inventoryDetections
          : [],
      } as PatchProductOverviewRow;
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
  } as PatchDashboardOverview;

  return {
    schemaVersion: patchViewSchemaVersion,
    server,
    userName,
    depotFilter,
    dashboard: normalizedDashboard,
  };
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

export function usePatchManagementWorkspace() {
  // The stored view *is* the initial state — a restart opens on the last dashboard
  // and the server/user it came from. The password is never cached (ADR 0008).
  const cached = useRef(normalizeCachedPatchView(loadView<unknown>(patchViewKey))).current;
  const [status, setStatus] = useState<OpsiConnectionStatusResult | null>(null);
  const [statusLoading, setStatusLoading] = useState(true);
  const [statusError, setStatusError] = useState<ErrorPresentation | null>(null);
  const [dashboard, setDashboard] = useState<PatchDashboardOverview | null>(
    () => cached?.dashboard ?? null,
  );
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [dashboardError, setDashboardError] = useState<ErrorPresentation | null>(null);
  const [depotFilter, setDepotFilter] = useState(() => cached?.depotFilter ?? '');
  const [depotResolved, setDepotResolved] = useState(false);
  const [versionCheckBusy, setVersionCheckBusy] = useState(false);
  const [versionCheckError, setVersionCheckError] = useState<ErrorPresentation | null>(null);

  const loadDashboard = useCallback((filter: string) => {
    setDashboardLoading(true);
    setDashboardError(null);
    invoke<PatchDashboardOverview>(
      'patchmanagement',
      'getDashboard',
      { depotFilter: filter.length > 0 ? filter : null },
    )
      .then((result) => setDashboard(result))
      .catch((error: unknown) => {
        setDashboard(null);
        setDashboardError(presentOpsiError(error, 'The patch overview could not be loaded.'));
      })
      .finally(() => setDashboardLoading(false));
  }, []);

  const refreshDashboard = useCallback(() => {
    loadDashboard(depotFilter);
  }, [depotFilter, loadDashboard]);

  const refreshConnectedDashboard = useCallback(() => {
    if (status?.connected) loadDashboard(depotFilter);
  }, [depotFilter, status?.connected, loadDashboard]);

  const loadConnectionStatus = useCallback(() => {
    setStatusLoading(true);
    setStatusError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus', {})
      .then((result) => {
        setStatus(result);
        if (result.connectionError) {
          setStatusError(presentError(new Error(result.connectionError), {
            message: 'The automatic opsi connection could not be established.',
            cause: 'The configured opsi connection was not established successfully at startup.',
            action: 'Check the server, certificate trust, and saved opsi account in Settings.',
          }));
        }
        if (result.connected) {
          loadDashboard('');
        }
      })
      .catch((error: unknown) => {
        setStatus(null);
        setStatusError(
          presentOpsiError(error, 'The opsi connection status could not be loaded.'),
        );
      })
      .finally(() => setStatusLoading(false));
  }, [loadDashboard]);

  useEffect(() => {
    loadConnectionStatus();
  }, [loadConnectionStatus]);

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

  const changeDepotFilter = useCallback((value: string) => {
    setDepotFilter(value);
    loadDashboard(value);
  }, [loadDashboard]);

  const checkVendorVersions = useCallback((productIds?: string[]): Promise<boolean> => {
    setVersionCheckBusy(true);
    setVersionCheckError(null);
    return invoke<VersionCheckResult>('patchmanagement', 'checkVendorVersions', {
      productIds: productIds ?? null,
    })
      .then(() => {
        loadDashboard(depotFilter);
        return true;
      })
      .catch((error: unknown) => {
        setVersionCheckError(
          presentError(error, {
            message: 'The manufacturer checks could not be confirmed as completed.',
            action: verifyVersionCheckAction,
          }),
        );
        return false;
      })
      .finally(() => setVersionCheckBusy(false));
  }, [depotFilter, loadDashboard]);

  const connected = status?.connected === true;
  const stale = !connected && dashboard !== null;

  return {
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
    changeDepotFilter,
    checkVendorVersions,
    clearVersionCheckError: () => setVersionCheckError(null),
  };
}
