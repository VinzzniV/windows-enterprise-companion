import { useCallback, useEffect, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AppInfoResponse,
  GetHardwareInfoRequest,
  HardwareInfoResult,
  ListInventoryHostsResult,
  TargetRequest,
} from '../../shared/api-types';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { ErrorState } from '../../shared/ui/States';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  hostKeyOf,
  toHostList,
  toTargetRequest,
  toTargetRequestForHost,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';
import { runWithConcurrencyLimit } from '../../shared/concurrency';
import { hostsToRestore } from './hosts';
import { formatSnapshotAge, SnapshotGrid } from './InventorySnapshot';

function errorText(error: unknown): string {
  if (error instanceof BridgeInvokeError) {
    const details = error.error.details ? ` ${error.error.details}` : '';
    return `${error.error.code}: ${error.error.message}${details}`;
  }
  return error instanceof Error ? error.message : String(error);
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'loaded'; result: HardwareInfoResult }
  | { kind: 'error'; message: string };

interface HostEntry {
  key: string;
  label: string;
  target: TargetRequest | null;
  state: LoadState;
  refreshing: boolean;
  refreshError: string | null;
}

export function HardwareInfoPage() {
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [entries, setEntries] = useState<HostEntry[]>([]);
  const [selectedKey, setSelectedKey] = useState('LOCAL');

  const load = useCallback(
    (
      target: TargetRequest | null,
      forceRefresh: boolean,
      options?: { cacheOnly?: boolean; select?: boolean },
    ): Promise<void> => {
      const key = hostKeyOf(target);
      if (options?.select !== false) {
        setSelectedKey(key);
      }
      setEntries((current) => {
        const existing = current.find((entry) => entry.key === key);
        const preserveSnapshot = existing?.state.kind === 'loaded';
        const entry: HostEntry = {
          key,
          label: existing?.label ?? (target?.host ?? 'Local machine'),
          target,
          state: preserveSnapshot ? existing.state : { kind: 'loading' },
          refreshing: preserveSnapshot,
          refreshError: null,
        };
        return existing
          ? current.map((candidate) => (candidate.key === key ? entry : candidate))
          : [...current, entry];
      });

      const payload: GetHardwareInfoRequest = { forceRefresh, target, cacheOnly: options?.cacheOnly };
      return invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', payload)
        .then((result) =>
          setEntries((current) =>
            current.map((entry) =>
              entry.key === key
                ? {
                    ...entry,
                    label: result.host,
                    state: { kind: 'loaded', result },
                    refreshing: false,
                    refreshError: null,
                  }
                : entry,
            ),
          ),
        )
        .catch((error: unknown) =>
          setEntries((current) => {
            const message = errorText(error);
            return current.map((entry) => {
              if (entry.key !== key) return entry;
              return entry.state.kind === 'loaded'
                ? { ...entry, refreshing: false, refreshError: message }
                : {
                    ...entry,
                    state: { kind: 'error', message },
                    refreshing: false,
                    refreshError: null,
                  };
            });
          }),
        );
    },
    [],
  );

  useEffect(() => {
    void load(null, false);
    // Restore previously scanned hosts from the store — no network traffic.
    // Learn the local machine name first so the local machine (stored under its
    // machine name, ScanTarget.CacheKey) is not listed twice next to "LOCAL".
    void (async () => {
      const appInfo = await invoke<AppInfoResponse>('system', 'getAppInfo').catch(() => null);
      const stored = await invoke<ListInventoryHostsResult>('inventory', 'listHosts').catch(
        () => null,
      );
      if (!stored) return; // Bridge unavailable (browser preview) — local entry only
      const restorable = hostsToRestore(
        stored.hosts.map((storedHost) => storedHost.host),
        appInfo?.machineName ?? null,
      );
      for (const host of restorable) {
        void load({ host }, false, { cacheOnly: true, select: false });
      }
    })();
  }, [load]);

  const removeEntry = (key: string) => {
    setEntries((current) => current.filter((entry) => entry.key !== key));
    setSelectedKey((current) => (current === key ? 'LOCAL' : current));
    invoke('inventory', 'deleteHostSnapshot', { host: key }).catch(() => {
      // Already gone or bridge unavailable — the entry is removed either way
    });
  };

  const scanAllHosts = useCallback(async () => {
    const hosts = toHostList(selection);
    const appInfo = await invoke<AppInfoResponse>('system', 'getAppInfo').catch(() => null);
    await runWithConcurrencyLimit(hosts, appInfo?.maxParallelScans ?? 4, (host) =>
      load(toTargetRequestForHost(selection, host), false, { select: false }),
    );
  }, [selection, load]);

  const anyLoading = entries.some((entry) => entry.state.kind === 'loading' || entry.refreshing);
  const selectedEntry = entries.find((entry) => entry.key === selectedKey) ?? entries[0] ?? null;

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Inventory" subtitle="Hardware overview per scanned computer">
        <Button
          variant="primary"
          onClick={() =>
            selection.mode === 'multiple'
              ? void scanAllHosts()
              : void load(toTargetRequest(selection), false)
          }
          disabled={
            anyLoading ||
            (selection.mode === 'remote' && selection.host.trim() === '') ||
            (selection.mode === 'multiple' && toHostList(selection).length === 0)
          }
        >
          {selection.mode === 'multiple' ? 'Scan all hosts' : 'Scan target'}
        </Button>
      </PageHeader>

      <TargetSelector selection={selection} onChange={setSelection} disabled={anyLoading} allowMultiple />

      <div className="flex items-start gap-4">
        {entries.length > 1 && (
          <aside className="w-56 shrink-0" aria-label="Scanned computers">
            <ul className="flex flex-col gap-1">
              {entries.map((entry) => (
                <li key={entry.key} className="flex items-center gap-1">
                  <button
                    type="button"
                    onClick={() => setSelectedKey(entry.key)}
                    className={`flex min-w-0 flex-1 items-center gap-2 rounded px-2 py-1.5 text-left text-sm transition-colors ${
                      entry.key === selectedEntry?.key
                        ? 'bg-slate-800 text-slate-100'
                        : 'text-slate-300 hover:bg-slate-800/60'
                    }`}
                  >
                    <span
                      className={`h-2 w-2 shrink-0 rounded-full ${
                        entry.state.kind === 'loading' || entry.refreshing
                          ? 'animate-pulse bg-info-400'
                          : entry.state.kind === 'error'
                            ? 'bg-fail-500'
                            : 'bg-ok-500'
                      }`}
                    />
                    <span className="truncate">{entry.label}</span>
                  </button>
                  {entry.key !== 'LOCAL' && (
                    <button
                      type="button"
                      onClick={() => removeEntry(entry.key)}
                      aria-label={`Remove ${entry.label}`}
                      className="rounded px-1.5 py-1 text-slate-500 transition-colors hover:bg-slate-800 hover:text-slate-300"
                    >
                      ×
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </aside>
        )}

        {selectedEntry && (
          <section
            key={selectedEntry.key}
            className="flex min-w-0 flex-1 flex-col gap-3"
            aria-label={`Inventory for ${selectedEntry.label}`}
          >
            <div className="flex items-end justify-between border-b border-slate-800 pb-1">
              <h2 className="text-lg font-medium">{selectedEntry.label}</h2>
              <div className="flex items-center gap-3">
                {selectedEntry.state.kind === 'loaded' && (
                  <span className="text-xs text-slate-400">
                    {selectedEntry.refreshing
                      ? 'Refreshing — previous snapshot remains visible'
                      : `${selectedEntry.state.result.fromCache ? 'From cache' : 'Freshly captured'} — ${formatSnapshotAge(selectedEntry.state.result.capturedAtUtc)} — ${new Date(selectedEntry.state.result.capturedAtUtc).toLocaleString()}`}
                  </span>
                )}
                <Button
                  onClick={() => load(selectedEntry.target, true)}
                  disabled={selectedEntry.state.kind === 'loading' || selectedEntry.refreshing}
                >
                  Refresh
                </Button>
              </div>
            </div>

            {selectedEntry.state.kind === 'loading' && (
              <Spinner label={`Loading inventory for ${selectedEntry.label} …`} />
            )}

            {selectedEntry.state.kind === 'error' && (
              <ErrorState
                title={`Error — ${selectedEntry.label}`}
                message={selectedEntry.state.message}
                hint="Check that the host is reachable, WinRM is enabled on it and the account has remote management rights."
              />
            )}

            {selectedEntry.refreshError && (
              <div role="alert" className="rounded border border-fail-700/60 bg-fail-950/30 p-3">
                <p className="text-sm text-fail-300">
                  Refresh failed; the previous snapshot is still shown. {selectedEntry.refreshError}
                </p>
              </div>
            )}

            {selectedEntry.state.kind === 'loaded' && (
              <SnapshotGrid result={selectedEntry.state.result} target={selectedEntry.target} />
            )}
          </section>
        )}
      </div>
    </div>
  );
}
