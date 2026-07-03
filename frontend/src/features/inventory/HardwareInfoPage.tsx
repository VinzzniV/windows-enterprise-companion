import { useCallback, useEffect, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  DiskEncryptionStatus,
  EncryptableVolume,
  GetHardwareInfoRequest,
  HardwareInfoResult,
  PhysicalNetworkAdapter,
  TargetRequest,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { PageHeader } from '../../shared/ui/PageHeader';
import { ErrorState } from '../../shared/ui/States';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  hostKeyOf,
  toTargetRequest,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';

function formatBytes(bytes: number): string {
  if (bytes <= 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const exponent = Math.min(Math.floor(Math.log2(bytes) / 10), units.length - 1);
  const value = bytes / 2 ** (10 * exponent);
  return `${value.toFixed(value >= 100 ? 0 : 1)} ${units[exponent]}`;
}

export function formatLinkSpeed(bitsPerSecond: number | null): string {
  // 1 Tbit/s+ is a WMI "unknown" sentinel (e.g. Int64.MaxValue), not a link speed
  if (!bitsPerSecond || bitsPerSecond <= 0 || bitsPerSecond >= 1_000_000_000_000) return '—';
  if (bitsPerSecond >= 1_000_000_000)
    return `${Number((bitsPerSecond / 1_000_000_000).toFixed(1))} Gbit/s`;
  if (bitsPerSecond >= 1_000_000) return `${Number((bitsPerSecond / 1_000_000).toFixed(1))} Mbit/s`;
  return `${bitsPerSecond} bit/s`;
}

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
}

type EncryptionState =
  | { kind: 'loading' }
  | { kind: 'loaded'; volumes: EncryptableVolume[] }
  | { kind: 'requiresElevation'; message: string }
  | { kind: 'error'; message: string };

function EncryptionCard({ target }: { target: TargetRequest | null }) {
  const [state, setState] = useState<EncryptionState>({ kind: 'loading' });

  useEffect(() => {
    setState({ kind: 'loading' });
    invoke<DiskEncryptionStatus>('inventory', 'getDiskEncryptionStatus', { target })
      .then((status) => setState({ kind: 'loaded', volumes: status.volumes }))
      .catch((error: unknown) => {
        if (error instanceof BridgeInvokeError && error.error.code === 'ACCESS_DENIED') {
          setState({ kind: 'requiresElevation', message: error.error.message });
        } else {
          setState({ kind: 'error', message: errorText(error) });
        }
      });
  }, [target]);

  return (
    <Card title="BitLocker">
      {state.kind === 'loading' && <Spinner label="Checking encryption status …" />}

      {state.kind === 'requiresElevation' && (
        <div className="flex flex-col gap-2">
          <StatusBadge variant="elevation">Requires elevation</StatusBadge>
          <p className="text-sm text-slate-400">{state.message}</p>
          <p className="text-xs text-slate-500">
            Restart the app as administrator to run this check.
          </p>
        </div>
      )}

      {state.kind === 'error' && (
        <div className="flex flex-col gap-2">
          <StatusBadge variant="error">Failed</StatusBadge>
          <p className="text-sm text-slate-400">{state.message}</p>
        </div>
      )}

      {state.kind === 'loaded' && (
        <ul className="flex flex-col gap-1 text-sm">
          {state.volumes.map((volume, index) => (
            <li key={index} className="flex items-center gap-2">
              <span className="w-10">{volume.driveLetter ?? '—'}</span>
              <StatusBadge
                variant={
                  volume.protectionStatus === 'PROTECTED'
                    ? 'success'
                    : volume.protectionStatus === 'UNPROTECTED'
                      ? 'error'
                      : 'neutral'
                }
              >
                {volume.protectionStatus}
              </StatusBadge>
            </li>
          ))}
          {state.volumes.length === 0 && <li className="text-slate-400">No encryptable volumes found.</li>}
        </ul>
      )}
    </Card>
  );
}

function NotCaptured({ reason }: { reason?: string }) {
  return (
    <p className="text-sm text-slate-400">
      {reason ?? 'Not captured in this snapshot — refresh to include it.'}
    </p>
  );
}

function SnapshotGrid({ result, target }: { result: HardwareInfoResult; target: TargetRequest | null }) {
  const { snapshot } = result;
  return (
    <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
      <Card title="System">
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
          <dt className="text-slate-400">Computer</dt>
          <dd>{result.host}</dd>
          <dt className="text-slate-400">Operating system</dt>
          <dd>{snapshot.operatingSystem.caption}</dd>
          <dt className="text-slate-400">Version</dt>
          <dd>
            {snapshot.operatingSystem.version} (Build {snapshot.operatingSystem.buildNumber})
          </dd>
          <dt className="text-slate-400">Architecture</dt>
          <dd>{snapshot.operatingSystem.architecture ?? '—'}</dd>
        </dl>
      </Card>

      <Card title="CPU">
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
          <dt className="text-slate-400">Name</dt>
          <dd>{snapshot.cpu.name}</dd>
          <dt className="text-slate-400">Cores</dt>
          <dd>
            {snapshot.cpu.physicalCores} physical / {snapshot.cpu.logicalProcessors} logical
          </dd>
          <dt className="text-slate-400">Max clock</dt>
          <dd>{snapshot.cpu.maxClockSpeedMhz} MHz</dd>
        </dl>
      </Card>

      <Card title={`Memory (${snapshot.memoryBanks.length} banks)`}>
        <DataTable
          rows={snapshot.memoryBanks}
          emptyMessage="No memory bank information available."
          columns={[
            { header: 'Manufacturer', cell: (bank) => bank.manufacturer ?? '—' },
            { header: 'Part number', cell: (bank) => bank.partNumber ?? '—' },
            { header: 'Capacity', cell: (bank) => formatBytes(bank.capacityBytes) },
            { header: 'Speed', cell: (bank) => (bank.speedMtps ? `${bank.speedMtps} MT/s` : '—') },
          ]}
        />
      </Card>

      <Card title={`Storage (${snapshot.disks.length} disks)`}>
        <DataTable
          rows={snapshot.disks}
          emptyMessage="No physical disks reported."
          columns={[
            { header: 'Model', cell: (disk) => disk.model },
            { header: 'Size', cell: (disk) => formatBytes(disk.sizeBytes) },
            { header: 'Interface', cell: (disk) => disk.interfaceType ?? '—' },
          ]}
        />
      </Card>

      <Card title={`Network (${snapshot.networkAdapters?.length ?? 0} adapters)`}>
        {snapshot.networkAdapters == null ? (
          <NotCaptured />
        ) : (
          (() => {
            const activeAdapters = snapshot.networkAdapters.filter(
              (adapter) => adapter.connected !== false,
            );
            const inactiveAdapters = snapshot.networkAdapters.filter(
              (adapter) => adapter.connected === false,
            );
            const adapterColumns = [
              { header: 'Name', cell: (adapter: PhysicalNetworkAdapter) => adapter.name },
              {
                header: 'IP addresses',
                cell: (adapter: PhysicalNetworkAdapter) =>
                  adapter.ipAddresses?.length ? (
                    <span className="break-all">{adapter.ipAddresses.join(', ')}</span>
                  ) : (
                    '—'
                  ),
              },
              { header: 'MAC', cell: (adapter: PhysicalNetworkAdapter) => adapter.macAddress ?? '—' },
              {
                header: 'Link speed',
                cell: (adapter: PhysicalNetworkAdapter) => formatLinkSpeed(adapter.speedBitsPerSecond),
              },
            ];
            return (
              <div className="flex flex-col gap-2">
                <DataTable
                  rows={activeAdapters}
                  emptyMessage="No connected network adapters."
                  columns={adapterColumns}
                />
                {inactiveAdapters.length > 0 && (
                  <DetailsDisclosure
                    summary={`${inactiveAdapters.length} disconnected adapter${inactiveAdapters.length === 1 ? '' : 's'}`}
                  >
                    <DataTable
                      rows={inactiveAdapters}
                      emptyMessage=""
                      columns={adapterColumns}
                    />
                  </DetailsDisclosure>
                )}
              </div>
            );
          })()
        )}
      </Card>

      <Card title={`GPU (${snapshot.gpus?.length ?? 0})`}>
        {snapshot.gpus == null ? (
          <NotCaptured />
        ) : (
          <DataTable
            rows={snapshot.gpus}
            emptyMessage="No graphics adapters reported."
            columns={[
              { header: 'Name', cell: (gpu) => gpu.name },
              { header: 'Memory', cell: (gpu) => (gpu.memoryBytes ? formatBytes(gpu.memoryBytes) : '—') },
              { header: 'Driver', cell: (gpu) => gpu.driverVersion ?? '—' },
            ]}
          />
        )}
      </Card>

      <Card title={`Monitors (${snapshot.monitors?.length ?? 0})`}>
        {snapshot.monitors == null ? (
          <NotCaptured />
        ) : snapshot.monitors.length === 0 ? (
          <p className="text-sm text-slate-400">No monitor identification available (typical for VMs).</p>
        ) : (
          <ul className="flex flex-col gap-1 text-sm">
            {snapshot.monitors.map((monitor, index) => (
              <li key={index}>
                {monitor.model ?? 'Unknown model'}{' '}
                <span className="text-slate-400">
                  {monitor.manufacturer ?? ''} {monitor.serialNumber ? `· S/N ${monitor.serialNumber}` : ''}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title={`Software (${snapshot.installedSoftware?.length ?? 0})`}>
        {snapshot.installedSoftware == null ? (
          <NotCaptured
            reason={
              target
                ? 'Available for the local machine only (registry-based).'
                : undefined
            }
          />
        ) : (
          <DetailsDisclosure summary={`Show ${snapshot.installedSoftware.length} entries`}>
            <div className="max-h-80 overflow-y-auto">
              <DataTable
                rows={snapshot.installedSoftware}
                emptyMessage="No installed software found."
                columns={[
                  { header: 'Name', cell: (entry) => entry.name },
                  { header: 'Version', cell: (entry) => entry.version ?? '—' },
                  { header: 'Publisher', cell: (entry) => entry.publisher ?? '—' },
                ]}
              />
            </div>
          </DetailsDisclosure>
        )}
      </Card>

      <EncryptionCard target={target} />
    </div>
  );
}

export function HardwareInfoPage() {
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [entries, setEntries] = useState<HostEntry[]>([]);
  const [selectedKey, setSelectedKey] = useState('LOCAL');

  const load = useCallback((target: TargetRequest | null, forceRefresh: boolean) => {
    const key = hostKeyOf(target);
    setSelectedKey(key);
    setEntries((current) => {
      const existing = current.find((entry) => entry.key === key);
      const entry: HostEntry = {
        key,
        label: existing?.label ?? (target?.host ?? 'Local machine'),
        target,
        state: { kind: 'loading' },
      };
      return existing
        ? current.map((candidate) => (candidate.key === key ? entry : candidate))
        : [...current, entry];
    });

    const payload: GetHardwareInfoRequest = { forceRefresh, target };
    invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', payload)
      .then((result) =>
        setEntries((current) =>
          current.map((entry) =>
            entry.key === key
              ? { ...entry, label: result.host, state: { kind: 'loaded', result } }
              : entry,
          ),
        ),
      )
      .catch((error: unknown) =>
        setEntries((current) =>
          current.map((entry) =>
            entry.key === key ? { ...entry, state: { kind: 'error', message: errorText(error) } } : entry,
          ),
        ),
      );
  }, []);

  useEffect(() => {
    load(null, false);
  }, [load]);

  const removeEntry = (key: string) => {
    setEntries((current) => current.filter((entry) => entry.key !== key));
    setSelectedKey((current) => (current === key ? 'LOCAL' : current));
  };

  const anyLoading = entries.some((entry) => entry.state.kind === 'loading');
  const selectedEntry = entries.find((entry) => entry.key === selectedKey) ?? entries[0] ?? null;

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Inventory" subtitle="Hardware overview per scanned computer">
        <Button
          variant="primary"
          onClick={() => load(toTargetRequest(selection), false)}
          disabled={anyLoading || (selection.mode === 'remote' && selection.host.trim() === '')}
        >
          Scan target
        </Button>
      </PageHeader>

      <TargetSelector selection={selection} onChange={setSelection} disabled={anyLoading} />

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
                        entry.state.kind === 'loading'
                          ? 'animate-pulse bg-sky-400'
                          : entry.state.kind === 'error'
                            ? 'bg-red-500'
                            : 'bg-emerald-500'
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
                    {selectedEntry.state.result.fromCache ? 'From cache' : 'Freshly captured'} —{' '}
                    {new Date(selectedEntry.state.result.capturedAtUtc).toLocaleString()}
                  </span>
                )}
                <Button
                  onClick={() => load(selectedEntry.target, true)}
                  disabled={selectedEntry.state.kind === 'loading'}
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

            {selectedEntry.state.kind === 'loaded' && (
              <SnapshotGrid result={selectedEntry.state.result} target={selectedEntry.target} />
            )}
          </section>
        )}
      </div>
    </div>
  );
}
