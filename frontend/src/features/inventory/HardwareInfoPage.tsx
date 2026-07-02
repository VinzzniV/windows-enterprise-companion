import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { GetHardwareInfoRequest, HardwareInfoResult } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';

function formatBytes(bytes: number): string {
  if (bytes <= 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const exponent = Math.min(Math.floor(Math.log2(bytes) / 10), units.length - 1);
  const value = bytes / 2 ** (10 * exponent);
  return `${value.toFixed(value >= 100 ? 0 : 1)} ${units[exponent]}`;
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'loaded'; result: HardwareInfoResult }
  | { kind: 'error'; message: string };

export function HardwareInfoPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });

  const load = useCallback((forceRefresh: boolean) => {
    setState({ kind: 'loading' });
    const payload: GetHardwareInfoRequest = { forceRefresh };
    invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', payload)
      .then((result) => setState({ kind: 'loaded', result }))
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      );
  }, []);

  useEffect(() => {
    load(false);
  }, [load]);

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-end justify-between">
        <div>
          <h1 className="text-xl font-semibold">Hardware Inventory</h1>
          <p className="text-sm text-slate-400">Local machine overview</p>
        </div>
        <div className="flex items-center gap-3">
          {state.kind === 'loaded' && (
            <span className="text-xs text-slate-400">
              {state.result.fromCache ? 'From cache' : 'Freshly captured'} —{' '}
              {new Date(state.result.capturedAtUtc).toLocaleString()}
            </span>
          )}
          <button
            type="button"
            onClick={() => load(true)}
            disabled={state.kind === 'loading'}
            className="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-600 disabled:opacity-50"
          >
            Refresh
          </button>
        </div>
      </header>

      {state.kind === 'loading' && <p className="text-sm text-slate-400">Loading hardware information …</p>}

      {state.kind === 'error' && (
        <Card title="Error">
          <p className="text-sm text-red-400">{state.message}</p>
        </Card>
      )}

      {state.kind === 'loaded' && (
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
          <Card title="CPU">
            <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
              <dt className="text-slate-400">Name</dt>
              <dd>{state.result.snapshot.cpu.name}</dd>
              <dt className="text-slate-400">Cores</dt>
              <dd>
                {state.result.snapshot.cpu.physicalCores} physical /{' '}
                {state.result.snapshot.cpu.logicalProcessors} logical
              </dd>
              <dt className="text-slate-400">Max clock</dt>
              <dd>{state.result.snapshot.cpu.maxClockSpeedMhz} MHz</dd>
            </dl>
          </Card>

          <Card title="Operating system">
            <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
              <dt className="text-slate-400">Name</dt>
              <dd>{state.result.snapshot.operatingSystem.caption}</dd>
              <dt className="text-slate-400">Version</dt>
              <dd>
                {state.result.snapshot.operatingSystem.version} (Build{' '}
                {state.result.snapshot.operatingSystem.buildNumber})
              </dd>
              <dt className="text-slate-400">Architecture</dt>
              <dd>{state.result.snapshot.operatingSystem.architecture ?? '—'}</dd>
            </dl>
          </Card>

          <Card title={`Memory (${state.result.snapshot.memoryBanks.length} banks)`}>
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
                {state.result.snapshot.memoryBanks.map((bank, index) => (
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

          <Card title={`Disks (${state.result.snapshot.disks.length})`}>
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="text-slate-400">
                  <th className="pb-1 font-normal">Model</th>
                  <th className="pb-1 font-normal">Size</th>
                  <th className="pb-1 font-normal">Interface</th>
                </tr>
              </thead>
              <tbody>
                {state.result.snapshot.disks.map((disk, index) => (
                  <tr key={index} className="border-t border-slate-800">
                    <td className="py-1">{disk.model}</td>
                    <td className="py-1">{formatBytes(disk.sizeBytes)}</td>
                    <td className="py-1">{disk.interfaceType ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        </div>
      )}
    </div>
  );
}
