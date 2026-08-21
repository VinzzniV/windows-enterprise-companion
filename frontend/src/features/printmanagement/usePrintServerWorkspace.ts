import { useCallback, useEffect, useMemo, useState } from 'react';
import type {
  AppInfoResponse,
  ListPrintServersResult,
  PrintServerSnapshot,
  SavedTarget,
  TargetRequest,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { runWithConcurrencyLimit } from '../../shared/concurrency';
import type { SaveTargetInput } from '../../shared/targets/TargetContext';
import type { ServerScanState } from './PrintServerManagerCard';

interface UsePrintServerWorkspaceOptions {
  savedTargets: readonly SavedTarget[];
  toServerRequest: (host: string) => TargetRequest;
  onRefreshHints: () => void;
  saveTarget?: (input: SaveTargetInput) => Promise<void>;
  deleteTarget?: (id: number) => Promise<void>;
}

export function usePrintServerWorkspace({
  savedTargets,
  toServerRequest,
  onRefreshHints,
  saveTarget,
  deleteTarget,
}: UsePrintServerWorkspaceOptions) {
  const [newServer, setNewServer] = useState('');
  const [snapshots, setSnapshots] = useState<Record<string, PrintServerSnapshot>>({});
  const [scanStates, setScanStates] = useState<Record<string, ServerScanState>>({});
  const [scanning, setScanning] = useState(false);
  const [restoring, setRestoring] = useState(true);
  const [maxParallelScans, setMaxParallelScans] = useState(4);

  useEffect(() => {
    let active = true;

    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then((info) => {
        if (active) setMaxParallelScans(info.maxParallelScans);
      })
      .catch(() => {});
    invoke<ListPrintServersResult>('printmanagement', 'listServers', {})
      .then(async (result) => {
        for (const server of result.servers) {
          try {
            const snapshot = await invoke<PrintServerSnapshot>(
              'printmanagement',
              'getLatest',
              { server: server.server },
            );
            if (!active) return;
            setSnapshots((previous) => ({ ...previous, [snapshot.server]: snapshot }));
            setScanStates((previous) => ({
              ...previous,
              [snapshot.server]: { status: 'done' },
            }));
          } catch {
            // A missing snapshot stays out of the table.
          }
        }
        if (active) onRefreshHints();
      })
      .catch(() => {})
      .finally(() => {
        if (active) setRestoring(false);
      });

    return () => {
      active = false;
    };
  }, [onRefreshHints]);

  const scanServers = useCallback(
    (hosts: string[]) => {
      if (hosts.length === 0) return;
      setScanning(true);
      setScanStates((previous) => ({
        ...previous,
        ...Object.fromEntries(
          hosts.map((host) => [host.toUpperCase(), { status: 'loading' } as ServerScanState]),
        ),
      }));

      void runWithConcurrencyLimit(hosts, maxParallelScans, async (host) => {
        try {
          const snapshot = await invoke<PrintServerSnapshot>(
            'printmanagement',
            'scanServer',
            { target: toServerRequest(host) },
            300_000,
          );
          setSnapshots((previous) => ({ ...previous, [snapshot.server]: snapshot }));
          setScanStates((previous) => ({
            ...previous,
            [snapshot.server]: { status: 'done' },
          }));
        } catch (error) {
          setScanStates((previous) => ({
            ...previous,
            [host.toUpperCase()]: {
              status: 'error',
              error: presentError(error, {
                message: 'The print server could not be scanned.',
              }),
            },
          }));
        }
      })
        .then(onRefreshHints)
        .finally(() => setScanning(false));
    },
    [maxParallelScans, onRefreshHints, toServerRequest],
  );

  const addServer = useCallback(() => {
    const host = newServer.trim();
    if (host === '') return;
    setNewServer('');
    void saveTarget?.({ label: host, host, role: 'PrintServer' }).catch(() => {});
    scanServers([host]);
  }, [newServer, saveTarget, scanServers]);

  const removeServer = useCallback(
    (server: string) => {
      void invoke('printmanagement', 'deleteServer', { server }).catch(() => {});
      const saved = savedTargets.find(
        (target) =>
          target.role === 'PrintServer'
          && target.host.toUpperCase() === server.toUpperCase(),
      );
      if (saved) void deleteTarget?.(saved.id).catch(() => {});
      setSnapshots((previous) => {
        const next = { ...previous };
        delete next[server];
        return next;
      });
      setScanStates((previous) => {
        const next = { ...previous };
        delete next[server];
        return next;
      });
    },
    [deleteTarget, savedTargets],
  );

  const servers = useMemo(() => Object.keys(snapshots).sort(), [snapshots]);
  const managedServers = useMemo(() => {
    const byKey = new Map<string, string>();
    for (const server of Object.keys(snapshots)) byKey.set(server.toUpperCase(), server);
    for (const target of savedTargets) {
      if (target.role === 'PrintServer') byKey.set(target.host.toUpperCase(), target.host);
    }
    return [...byKey.values()].sort((left, right) => left.localeCompare(right));
  }, [savedTargets, snapshots]);
  const hasFailedScans = useMemo(
    () => Object.values(scanStates).some((state) => state.status === 'error'),
    [scanStates],
  );

  return {
    newServer,
    setNewServer,
    snapshots,
    scanStates,
    scanning,
    restoring,
    servers,
    managedServers,
    scanServers,
    addServer,
    removeServer,
    hasFailedScans,
  };
}
