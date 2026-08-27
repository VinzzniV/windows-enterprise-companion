import { useEffect, useState } from 'react';
import type {
  DiskEncryptionStatus,
  EncryptableVolume,
  HardwareInfoResult,
  PhysicalNetworkAdapter,
  TargetRequest,
} from '../../shared/api-types';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Spinner } from '../../shared/ui/Spinner';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';

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

export function formatSnapshotAge(capturedAtUtc: string, nowMs = Date.now()): string {
  const capturedMs = Date.parse(capturedAtUtc);
  if (!Number.isFinite(capturedMs)) return 'age unavailable';

  const ageMinutes = Math.floor(Math.max(0, nowMs - capturedMs) / 60_000);
  if (ageMinutes < 1) return 'less than a minute old';
  if (ageMinutes < 60) return `${ageMinutes} min old`;

  const ageHours = Math.floor(ageMinutes / 60);
  if (ageHours < 48) return `${ageHours} h old`;

  return `${Math.floor(ageHours / 24)} d old`;
}

function errorText(error: unknown): string {
  if (error instanceof BridgeInvokeError) {
    const details = error.error.details ? ` ${error.error.details}` : '';
    return `${error.error.code}: ${error.error.message}${details}`;
  }
  return error instanceof Error ? error.message : String(error);
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
          <p className="text-xs text-muted">
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

export function SnapshotGrid({ result, target }: { result: HardwareInfoResult; target: TargetRequest | null }) {
  const { snapshot } = result;
  const totalMemoryBytes = snapshot.memoryBanks.reduce((total, bank) => total + bank.capacityBytes, 0);
  const totalStorageBytes = snapshot.disks.reduce((total, disk) => total + disk.sizeBytes, 0);
  const activeAdapterCount = snapshot.networkAdapters?.filter((adapter) => adapter.connected !== false).length;
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Inventory totals">
        <SummaryMetric label="Memory" value={formatBytes(totalMemoryBytes)} />
        <SummaryMetric label="Storage" value={formatBytes(totalStorageBytes)} />
        <SummaryMetric
          label="Active network"
          value={
            activeAdapterCount === undefined
              ? '—'
              : `${activeAdapterCount}/${snapshot.networkAdapters?.length ?? 0}`
          }
        />
        <SummaryMetric
          label="Installed software"
          value={snapshot.installedSoftware?.length ?? '—'}
        />
      </div>
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
            snapshot.installedSoftwareError ? (
              <div role="alert" className="flex flex-col gap-1">
                <p className="break-words text-sm text-fail-400">
                  {snapshot.installedSoftwareError.code}: {snapshot.installedSoftwareError.message}
                </p>
                <p className="text-xs text-muted">
                  Remote software inventory reads the registry through WMI (StdRegProv) and needs an
                  account with remote registry read rights on the target.
                </p>
              </div>
            ) : (
              <NotCaptured />
            )
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
    </div>
  );
}
