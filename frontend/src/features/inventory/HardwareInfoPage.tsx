import { useCallback, useEffect, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  DiskEncryptionStatus,
  EncryptableVolume,
  GetHardwareInfoRequest,
  HardwareInfoResult,
  TargetRequest,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
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

function formatLinkSpeed(bitsPerSecond: number | null): string {
  if (!bitsPerSecond || bitsPerSecond <= 0) return '—';
  if (bitsPerSecond >= 1_000_000_000) return `${bitsPerSecond / 1_000_000_000} Gbit/s`;
  if (bitsPerSecond >= 1_000_000) return `${bitsPerSecond / 1_000_000} Mbit/s`;
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
    <Card title="Disk encryption (BitLocker)">
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

function SnapshotGrid({ result, target }: { result: HardwareInfoResult; target: TargetRequest | null }) {
  const { snapshot } = result;
  return (
    <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
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

      <Card title="Operating system">
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
          <dt className="text-slate-400">Name</dt>
          <dd>{snapshot.operatingSystem.caption}</dd>
          <dt className="text-slate-400">Version</dt>
          <dd>
            {snapshot.operatingSystem.version} (Build {snapshot.operatingSystem.buildNumber})
          </dd>
          <dt className="text-slate-400">Architecture</dt>
          <dd>{snapshot.operatingSystem.architecture ?? '—'}</dd>
        </dl>
      </Card>

      <Card title={`Memory (${snapshot.memoryBanks.length} banks)`}>
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="text-slate-400">
              <th className="pb-1 font-normal">Manufacturer</th>
              <th className="pb-1 font-normal">Part number</th>
              <th className="pb-1 font-normal">Capacity</th>
              <th className="pb-1 font-normal">Speed</th>
            </tr>
          </thead>
          <tbody>
            {snapshot.memoryBanks.map((bank, index) => (
              <tr key={index} className="border-t border-slate-800">
                <td className="py-1">{bank.manufacturer ?? '—'}</td>
                <td className="py-1">{bank.partNumber ?? '—'}</td>
                <td className="py-1">{formatBytes(bank.capacityBytes)}</td>
                <td className="py-1">{bank.speedMtps ? `${bank.speedMtps} MT/s` : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      <Card title={`Disks (${snapshot.disks.length})`}>
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="text-slate-400">
              <th className="pb-1 font-normal">Model</th>
              <th className="pb-1 font-normal">Size</th>
              <th className="pb-1 font-normal">Interface</th>
            </tr>
          </thead>
          <tbody>
            {snapshot.disks.map((disk, index) => (
              <tr key={index} className="border-t border-slate-800">
                <td className="py-1">{disk.model}</td>
                <td className="py-1">{formatBytes(disk.sizeBytes)}</td>
                <td className="py-1">{disk.interfaceType ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      <Card title={`Network adapters (${snapshot.networkAdapters?.length ?? 0})`}>
        {snapshot.networkAdapters == null ? (
          <p className="text-sm text-slate-400">Not captured in this snapshot — refresh to include it.</p>
        ) : (
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="text-slate-400">
                <th className="pb-1 font-normal">Name</th>
                <th className="pb-1 font-normal">MAC</th>
                <th className="pb-1 font-normal">Link speed</th>
                <th className="pb-1 font-normal">Status</th>
              </tr>
            </thead>
            <tbody>
              {snapshot.networkAdapters.map((adapter, index) => (
                <tr key={index} className="border-t border-slate-800">
                  <td className="py-1">{adapter.name}</td>
                  <td className="py-1">{adapter.macAddress ?? '—'}</td>
                  <td className="py-1">{formatLinkSpeed(adapter.speedBitsPerSecond)}</td>
                  <td className="py-1">
                    {adapter.connected == null ? '—' : adapter.connected ? 'Connected' : 'Disconnected'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title={`Graphics (${snapshot.gpus?.length ?? 0})`}>
        {snapshot.gpus == null ? (
          <p className="text-sm text-slate-400">Not captured in this snapshot — refresh to include it.</p>
        ) : (
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="text-slate-400">
                <th className="pb-1 font-normal">Name</th>
                <th className="pb-1 font-normal">Memory</th>
                <th className="pb-1 font-normal">Driver</th>
              </tr>
            </thead>
            <tbody>
              {snapshot.gpus.map((gpu, index) => (
                <tr key={index} className="border-t border-slate-800">
                  <td className="py-1">{gpu.name}</td>
                  <td className="py-1">{gpu.memoryBytes ? formatBytes(gpu.memoryBytes) : '—'}</td>
                  <td className="py-1">{gpu.driverVersion ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title={`Monitors (${snapshot.monitors?.length ?? 0})`}>
        {snapshot.monitors == null ? (
          <p className="text-sm text-slate-400">Not captured in this snapshot — refresh to include it.</p>
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

      <Card title={`Installed software (${snapshot.installedSoftware?.length ?? 0})`}>
        {snapshot.installedSoftware == null ? (
          <p className="text-sm text-slate-400">
            {target ? 'Available for the local machine only (registry-based).' : 'Not captured in this snapshot — refresh to include it.'}
          </p>
        ) : (
          <details>
            <summary className="cursor-pointer text-sm text-slate-300">
              Show {snapshot.installedSoftware.length} entries
            </summary>
            <div className="mt-2 max-h-80 overflow-y-auto">
              <table className="w-full text-left text-sm">
                <thead>
                  <tr className="text-slate-400">
                    <th className="pb-1 font-normal">Name</th>
                    <th className="pb-1 font-normal">Version</th>
                    <th className="pb-1 font-normal">Publisher</th>
                  </tr>
                </thead>
                <tbody>
                  {snapshot.installedSoftware.map((entry, index) => (
                    <tr key={index} className="border-t border-slate-800">
                      <td className="py-1">{entry.name}</td>
                      <td className="py-1">{entry.version ?? '—'}</td>
                      <td className="py-1">{entry.publisher ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
        )}
      </Card>

      <EncryptionCard target={target} />
    </div>
  );
}

export function HardwareInfoPage() {
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [entries, setEntries] = useState<HostEntry[]>([]);

  const load = useCallback((target: TargetRequest | null, forceRefresh: boolean) => {
    const key = hostKeyOf(target);
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

  const removeEntry = (key: string) =>
    setEntries((current) => current.filter((entry) => entry.key !== key));

  const anyLoading = entries.some((entry) => entry.state.kind === 'loading');

  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-col gap-3">
        <div>
          <h1 className="text-xl font-semibold">Hardware Inventory</h1>
          <p className="text-sm text-slate-400">Hardware overview per scanned computer</p>
        </div>
        <TargetSelector selection={selection} onChange={setSelection} disabled={anyLoading} />
        <div>
          <button
            type="button"
            onClick={() => load(toTargetRequest(selection), false)}
            disabled={anyLoading || (selection.mode === 'remote' && selection.host.trim() === '')}
            className="rounded bg-sky-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-sky-600 disabled:opacity-50"
          >
            Scan target
          </button>
        </div>
      </header>

      {entries.map((entry) => (
        <section key={entry.key} className="flex flex-col gap-3" aria-label={`Inventory for ${entry.label}`}>
          <div className="flex items-end justify-between border-b border-slate-800 pb-1">
            <h2 className="text-lg font-medium">{entry.label}</h2>
            <div className="flex items-center gap-3">
              {entry.state.kind === 'loaded' && (
                <span className="text-xs text-slate-400">
                  {entry.state.result.fromCache ? 'From cache' : 'Freshly captured'} —{' '}
                  {new Date(entry.state.result.capturedAtUtc).toLocaleString()}
                </span>
              )}
              <button
                type="button"
                onClick={() => load(entry.target, true)}
                disabled={entry.state.kind === 'loading'}
                className="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-600 disabled:opacity-50"
              >
                Refresh
              </button>
              {entry.key !== 'LOCAL' && (
                <button
                  type="button"
                  onClick={() => removeEntry(entry.key)}
                  className="rounded bg-slate-800 px-3 py-1.5 text-sm text-slate-300 transition-colors hover:bg-slate-700"
                >
                  Remove
                </button>
              )}
            </div>
          </div>

          {entry.state.kind === 'loading' && <Spinner label={`Loading inventory for ${entry.label} …`} />}

          {entry.state.kind === 'error' && (
            <Card title={`Error — ${entry.label}`}>
              <p className="text-sm text-red-400">{entry.state.message}</p>
            </Card>
          )}

          {entry.state.kind === 'loaded' && (
            <SnapshotGrid result={entry.state.result} target={entry.target} />
          )}
        </section>
      ))}
    </div>
  );
}
