import { useCallback, useEffect, useRef, useState } from 'react';
import type { OpsiConnectionStatusResult, PatchDashboardOverview } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { loadView, saveView } from '../../shared/viewCache';
import { presentOpsiError } from './patchErrors';

interface CachedPatchView {
  schemaVersion: number;
  server: string;
  userName: string;
  depotFilter: string;
  dashboard: PatchDashboardOverview | null;
}

const patchViewKey = 'patchmanagement';
const patchViewSchemaVersion = 4;

function loadCachedView(): CachedPatchView | null {
  const value = loadView<CachedPatchView>(patchViewKey);
  return value?.schemaVersion === patchViewSchemaVersion ? value : null;
}

export function resolveDefaultDepot(
  depots: { id: string; description: string | null }[],
  defaultDepotFilter: string,
): string | null {
  const needle = defaultDepotFilter.trim().toLocaleLowerCase();
  if (!needle) return null;
  return depots.find((depot) =>
    depot.id.toLocaleLowerCase().includes(needle)
    || (depot.description ?? '').toLocaleLowerCase().includes(needle))?.id ?? null;
}

export function usePatchManagementWorkspace() {
  const cached = useRef(loadCachedView()).current;
  const [status, setStatus] = useState<OpsiConnectionStatusResult | null>(null);
  const [statusLoading, setStatusLoading] = useState(true);
  const [statusError, setStatusError] = useState<ErrorPresentation | null>(null);
  const [dashboard, setDashboard] = useState<PatchDashboardOverview | null>(cached?.dashboard ?? null);
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [dashboardError, setDashboardError] = useState<ErrorPresentation | null>(null);
  const [depotFilter, setDepotFilter] = useState(cached?.depotFilter ?? '');
  const [depotResolved, setDepotResolved] = useState(false);
  const dueWingetCheckStarted = useRef(false);

  const loadDashboard = useCallback((filter: string) => {
    setDashboardLoading(true);
    setDashboardError(null);
    invoke<PatchDashboardOverview>('patchmanagement', 'getDashboard', {
      depotFilter: filter || null,
    })
      .then(setDashboard)
      .catch((error: unknown) => {
        setDashboardError(presentOpsiError(error, 'The patch overview could not be loaded.'));
      })
      .finally(() => setDashboardLoading(false));
  }, []);

  const loadConnectionStatus = useCallback(() => {
    setStatusLoading(true);
    setStatusError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus', {})
      .then((result) => {
        setStatus(result);
        if (result.connectionError) {
          setStatusError(presentOpsiError(
            new Error(result.connectionError),
            'The automatic opsi connection could not be established.',
          ));
        }
        if (result.connected) loadDashboard('');
      })
      .catch((error: unknown) => {
        setStatus(null);
        setStatusError(presentOpsiError(error, 'The opsi connection status could not be loaded.'));
      })
      .finally(() => setStatusLoading(false));
  }, [loadDashboard]);

  useEffect(loadConnectionStatus, [loadConnectionStatus]);

  useEffect(() => {
    if (!status?.connected || dueWingetCheckStarted.current) return;
    dueWingetCheckStarted.current = true;
    invoke('patchmanagement', 'checkWingetUpdates', { productIds: null, force: false })
      .then(() => loadDashboard(depotFilter))
      .catch(() => { /* Winget availability is presented independently from opsi. */ });
  }, [depotFilter, loadDashboard, status?.connected]);

  useEffect(() => {
    if (!dashboard || depotResolved || !status?.connected) return;
    setDepotResolved(true);
    const defaultDepot = resolveDefaultDepot(dashboard.depots, status.defaultDepotFilter);
    if (defaultDepot && defaultDepot !== depotFilter) {
      setDepotFilter(defaultDepot);
      loadDashboard(defaultDepot);
    }
  }, [dashboard, depotFilter, depotResolved, loadDashboard, status]);

  useEffect(() => {
    saveView<CachedPatchView>(patchViewKey, {
      schemaVersion: patchViewSchemaVersion,
      server: status?.serverUrl ?? cached?.server ?? '',
      userName: status?.userName ?? cached?.userName ?? '',
      depotFilter,
      dashboard,
    });
  }, [cached, dashboard, depotFilter, status?.serverUrl, status?.userName]);

  const changeDepotFilter = useCallback((value: string) => {
    setDepotFilter(value);
    loadDashboard(value);
  }, [loadDashboard]);

  const refreshDashboard = useCallback(() => loadDashboard(depotFilter), [depotFilter, loadDashboard]);

  return {
    status,
    statusLoading,
    statusError,
    dashboard,
    dashboardLoading,
    dashboardError,
    depotFilter,
    connected: status?.connected === true,
    stale: status?.connected !== true && dashboard !== null,
    loadConnectionStatus,
    refreshDashboard,
    changeDepotFilter,
  };
}
