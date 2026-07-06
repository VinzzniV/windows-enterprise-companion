import { useCallback, useEffect, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AppInfoResponse,
  ExportPrintCsvResult,
  ListPrintServersResult,
  PrintHint,
  PrintHintsResult,
  PrintHistoryResult,
  PrintServerDiff,
  PrintServerSnapshot,
  PrintSnapshotStamp,
  TonerSupply,
} from '../../shared/api-types';
import { runWithConcurrencyLimit } from '../../shared/concurrency';
import {
  LOCAL_TARGET_SELECTION,
  TargetSelector,
  toHostList,
  toTargetRequest,
  toTargetRequestForHost,
  type TargetSelection,
} from '../../shared/targets/TargetSelector';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Spinner } from '../../shared/ui/Spinner';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';

function describeError(error: unknown): string {
  if (error instanceof BridgeInvokeError) {
    const details = error.error.details ? ` — ${error.error.details}` : '';
    return `${error.error.code}: ${error.error.message}${details}`;
  }
  return error instanceof Error ? error.message : String(error);
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

/** Compact toner chips: "Toner Black 8%" red when low, unknown levels stay neutral. */
function TonerCells({ supplies }: { supplies: TonerSupply[] }) {
  if (supplies.length === 0) {
    return <span className="text-slate-500">—</span>;
  }
  return (
    <div className="flex flex-wrap gap-1">
      {supplies.map((supply) => (
        <span
          key={supply.description}
          className={`inline-flex rounded border px-1.5 py-0.5 text-xs ${
            supply.isLow
              ? 'border-red-700 bg-red-900/60 text-red-300'
              : supply.percent !== null
                ? 'border-slate-700 bg-slate-800 text-slate-300'
                : 'border-slate-800 bg-slate-900 text-slate-500'
          }`}
        >
          {supply.description}
          {supply.percent !== null ? ` ${supply.percent}%` : ''}
        </span>
      ))}
    </div>
  );
}

interface ServerScanState {
  status: 'loading' | 'done' | 'error';
  error?: string;
}

export function PrintManagementPage() {
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [snapshots, setSnapshots] = useState<Record<string, PrintServerSnapshot>>({});
  const [scanStates, setScanStates] = useState<Record<string, ServerScanState>>({});
  const [scanning, setScanning] = useState(false);
  const [serverFilter, setServerFilter] = useState('');
  const [hints, setHints] = useState<PrintHint[]>([]);
  const [exportMessage, setExportMessage] = useState<string | null>(null);
  const [maxParallelScans, setMaxParallelScans] = useState(4);

  const [diffServer, setDiffServer] = useState('');
  const [diffHistory, setDiffHistory] = useState<PrintSnapshotStamp[]>([]);
  const [diffBaselineId, setDiffBaselineId] = useState<string>('');
  const [diff, setDiff] = useState<PrintServerDiff | null>(null);
  const [diffError, setDiffError] = useState<string | null>(null);

  const loadHints = useCallback(() => {
    invoke<PrintHintsResult>('printmanagement', 'getHints', {})
      .then((result) => setHints(result.hints))
      .catch(() => setHints([]));
  }, []);

  // Restore stored servers without touching the network
  useEffect(() => {
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then((info) => setMaxParallelScans(info.maxParallelScans))
      .catch(() => {});
    invoke<ListPrintServersResult>('printmanagement', 'listServers', {})
      .then(async (result) => {
        for (const server of result.servers) {
          try {
            const snapshot = await invoke<PrintServerSnapshot>(
              'printmanagement', 'getLatest', { server: server.server });
            setSnapshots((previous) => ({ ...previous, [snapshot.server]: snapshot }));
            setScanStates((previous) => ({ ...previous, [snapshot.server]: { status: 'done' } }));
          } catch {
            // A missing snapshot just stays out of the table
          }
        }
        loadHints();
      })
      .catch(() => {});
  }, [loadHints]);

  const scan = useCallback(() => {
    const targets =
      selection.mode === 'multiple'
        ? toHostList(selection).map((host) => toTargetRequestForHost(selection, host))
        : [toTargetRequest(selection)];
    if (selection.mode === 'remote' && targets[0] === null) {
      return;
    }

    setScanning(true);
    setExportMessage(null);
    const keys = targets.map((target) => (target?.host ?? 'LOCAL').toUpperCase());
    setScanStates((previous) => ({
      ...previous,
      ...Object.fromEntries(keys.map((key) => [key, { status: 'loading' } as ServerScanState])),
    }));

    runWithConcurrencyLimit(targets, maxParallelScans, async (target) => {
      const key = (target?.host ?? 'LOCAL').toUpperCase();
      try {
        const snapshot = await invoke<PrintServerSnapshot>(
          'printmanagement', 'scanServer', { target }, 300_000);
        setSnapshots((previous) => ({ ...previous, [snapshot.server]: snapshot }));
        setScanStates((previous) => ({ ...previous, [snapshot.server]: { status: 'done' } }));
      } catch (error) {
        setScanStates((previous) => ({
          ...previous,
          [key]: { status: 'error', error: describeError(error) },
        }));
      }
    })
      .then(loadHints)
      .finally(() => setScanning(false));
  }, [selection, maxParallelScans, loadHints]);

  const removeServer = useCallback((server: string) => {
    invoke('printmanagement', 'deleteServer', { server })
      .then(() => {
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
      })
      .catch(() => {});
  }, []);

  const exportCsv = useCallback(() => {
    setExportMessage(null);
    invoke<ExportPrintCsvResult>('printmanagement', 'exportCsv', {
      servers: serverFilter ? [serverFilter] : null,
    })
      .then((result) =>
        setExportMessage(result.cancelled ? 'Export cancelled.' : `Exported to ${result.filePath}`))
      .catch((error: unknown) => setExportMessage(describeError(error)));
  }, [serverFilter]);

  const openWebUi = useCallback((address: string) => {
    invoke('printmanagement', 'openDeviceWebUi', { address }).catch(() => {});
  }, []);

  const loadDiffHistory = useCallback((server: string) => {
    setDiffServer(server);
    setDiff(null);
    setDiffError(null);
    setDiffBaselineId('');
    setDiffHistory([]);
    if (server === '') {
      return;
    }
    invoke<PrintHistoryResult>('printmanagement', 'getHistory', { server })
      .then((result) => setDiffHistory(result.snapshots))
      .catch((error: unknown) => setDiffError(describeError(error)));
  }, []);

  const loadDiff = useCallback(() => {
    setDiff(null);
    setDiffError(null);
    invoke<PrintServerDiff>('printmanagement', 'getDiff', {
      server: diffServer,
      baselineSnapshotId: diffBaselineId === '' ? null : Number(diffBaselineId),
    })
      .then(setDiff)
      .catch((error: unknown) => setDiffError(describeError(error)));
  }, [diffServer, diffBaselineId]);

  const servers = Object.keys(snapshots).sort();
  const visibleSnapshots = servers
    .filter((server) => serverFilter === '' || server === serverFilter)
    .map((server) => snapshots[server]);
  const rows = visibleSnapshots.flatMap((snapshot) =>
    snapshot.printers.map((entry) => ({ server: snapshot.server, entry })));

  const deviceCount = rows.filter((row) => row.entry.deviceAddress !== null).length;
  const lowTonerCount = rows.filter((row) =>
    row.entry.device?.supplies.some((supply) => supply.isLow)).length;
  const unreachableCount = rows.filter((row) => row.entry.deviceError !== null).length;
  const failedScans = Object.entries(scanStates).filter(([, state]) => state.status === 'error');

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Print Management"
        subtitle="Printer inventory from the print servers, enriched per device over SNMP — serials, locations, toner levels and the lease-swap history."
      >
        <Button variant="primary" onClick={scan} disabled={scanning}>
          {scanning ? 'Scanning…' : 'Scan'}
        </Button>
        <Button onClick={exportCsv} disabled={rows.length === 0}>
          Export CSV
        </Button>
      </PageHeader>

      <TargetSelector selection={selection} onChange={setSelection} disabled={scanning} allowMultiple />

      {scanning && <Spinner label="Scanning print servers" />}
      {exportMessage && <p className="text-sm text-slate-300">{exportMessage}</p>}

      {failedScans.map(([server, state]) => (
        <ErrorState
          key={server}
          title={`Scan of ${server} failed`}
          message={state.error ?? 'Unknown error'}
        />
      ))}

      {rows.length > 0 && (
        <>
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-400">Location / print server</span>
              <select
                value={serverFilter}
                onChange={(event) => setServerFilter(event.target.value)}
                className="rounded border border-slate-700 bg-slate-950 px-2 py-1.5 text-slate-100"
              >
                <option value="">All servers</option>
                {servers.map((server) => (
                  <option key={server} value={server}>
                    {server}
                  </option>
                ))}
              </select>
            </label>
            {serverFilter !== '' && (
              <>
                <span className="text-xs text-slate-500">
                  Captured {formatTimestamp(snapshots[serverFilter].capturedAtUtc)}
                </span>
                <Button variant="ghost" onClick={() => removeServer(serverFilter)}>
                  Remove stored server
                </Button>
              </>
            )}
          </div>

          <div className="flex flex-wrap gap-3">
            <SummaryMetric label="Queues" value={rows.length} />
            <SummaryMetric label="Devices (with IP)" value={deviceCount} />
            <SummaryMetric
              label="Toner low"
              value={lowTonerCount}
              tone={lowTonerCount > 0 ? 'danger' : 'success'}
            />
            <SummaryMetric
              label="Not answering"
              value={unreachableCount}
              tone={unreachableCount > 0 ? 'warning' : 'success'}
            />
            <SummaryMetric label="Servers" value={visibleSnapshots.length} />
          </div>

          {hints.length > 0 && (
            <DetailsDisclosure summary={`Consistency hints (${hints.length})`}>
              <ul className="flex flex-col gap-1.5">
                {hints.map((hint) => (
                  <li key={`${hint.category}-${hint.message}`} className="text-sm text-slate-300">
                    <span className="mr-2 text-xs uppercase tracking-wide text-amber-400">
                      {hint.category}
                    </span>
                    {hint.message}
                  </li>
                ))}
              </ul>
            </DetailsDisclosure>
          )}

          <Card title="Printers">
            <DataTable
              columns={[
                { header: 'Server', cell: (row) => row.server },
                {
                  header: 'Queue',
                  cell: (row) => (
                    <div>
                      <div className="text-slate-100">{row.entry.queueName}</div>
                      {row.entry.shareName && (
                        <div className="text-xs text-slate-500">\\{row.server}\{row.entry.shareName}</div>
                      )}
                    </div>
                  ),
                },
                {
                  header: 'Model',
                  cell: (row) => row.entry.device?.model ?? '—',
                },
                {
                  header: 'Serial number',
                  cell: (row) => row.entry.device?.serialNumber ?? '—',
                },
                {
                  header: 'Location',
                  cell: (row) =>
                    row.entry.location
                    ?? row.entry.device?.sysLocation
                    ?? '—',
                },
                {
                  header: 'Status',
                  cell: (row) =>
                    row.entry.deviceError ? (
                      <StatusBadge variant="error">{row.entry.deviceError.code}</StatusBadge>
                    ) : row.entry.device?.status ? (
                      <StatusBadge variant={row.entry.device.status === 'Idle' ? 'success' : 'info'}>
                        {row.entry.device.status}
                      </StatusBadge>
                    ) : (
                      <span className="text-slate-500">—</span>
                    ),
                },
                {
                  header: 'Toner',
                  cell: (row) => <TonerCells supplies={row.entry.device?.supplies ?? []} />,
                },
                {
                  header: 'IP / web UI',
                  cell: (row) =>
                    row.entry.deviceAddress ? (
                      <button
                        type="button"
                        onClick={() => openWebUi(row.entry.deviceAddress!)}
                        title={`Open https://${row.entry.deviceAddress}/ in the browser`}
                        className="cursor-pointer text-sky-400 underline-offset-2 hover:underline"
                      >
                        {row.entry.deviceAddress}
                      </button>
                    ) : (
                      <span className="text-slate-500">—</span>
                    ),
                },
                {
                  header: 'Driver',
                  cell: (row) =>
                    row.entry.driverName
                      ? `${row.entry.driverName}${row.entry.driverVersion ? ` (${row.entry.driverVersion})` : ''}`
                      : '—',
                },
              ]}
              rows={rows}
              emptyMessage="No printers captured yet."
            />
          </Card>

          <Card title="Lease swap history">
            <div className="flex flex-col gap-3">
              <p className="text-sm text-slate-400">
                Serial-number-based comparison between two scans of the same server — new,
                returned and swapped devices for the lease renewal.
              </p>
              <div className="flex flex-wrap items-center gap-3">
                <select
                  value={diffServer}
                  onChange={(event) => loadDiffHistory(event.target.value)}
                  aria-label="Diff server"
                  className="rounded border border-slate-700 bg-slate-950 px-2 py-1.5 text-sm text-slate-100"
                >
                  <option value="">Select server…</option>
                  {servers.map((server) => (
                    <option key={server} value={server}>
                      {server}
                    </option>
                  ))}
                </select>
                {diffServer !== '' && (
                  <>
                    <select
                      value={diffBaselineId}
                      onChange={(event) => setDiffBaselineId(event.target.value)}
                      aria-label="Baseline snapshot"
                      className="rounded border border-slate-700 bg-slate-950 px-2 py-1.5 text-sm text-slate-100"
                    >
                      <option value="">Previous scan (default)</option>
                      {diffHistory.slice(1).map((stamp) => (
                        <option key={stamp.id} value={stamp.id}>
                          {formatTimestamp(stamp.capturedAtUtc)}
                        </option>
                      ))}
                    </select>
                    <Button onClick={loadDiff}>Compare</Button>
                  </>
                )}
              </div>

              {diffError && <ErrorState message={diffError} />}
              {diff && (
                <div className="flex flex-col gap-3">
                  <p className="text-sm text-slate-400">
                    {formatTimestamp(diff.baselineAtUtc)} → {formatTimestamp(diff.latestAtUtc)}
                    {diff.devicesWithoutSerialNumber > 0 &&
                      ` — ${diff.devicesWithoutSerialNumber} device(s) without a readable serial (not compared)`}
                  </p>
                  <div className="grid gap-4 lg:grid-cols-3">
                    <div>
                      <h3 className="mb-1 text-sm font-medium text-emerald-400">
                        New ({diff.newDevices.length})
                      </h3>
                      <DataTable
                        columns={[
                          { header: 'Serial', cell: (device) => device.serialNumber },
                          { header: 'Model', cell: (device) => device.model ?? '—' },
                          { header: 'Queue', cell: (device) => device.queueName ?? '—' },
                        ]}
                        rows={diff.newDevices}
                        emptyMessage="No new devices."
                      />
                    </div>
                    <div>
                      <h3 className="mb-1 text-sm font-medium text-red-400">
                        Gone ({diff.goneDevices.length})
                      </h3>
                      <DataTable
                        columns={[
                          { header: 'Serial', cell: (device) => device.serialNumber },
                          { header: 'Model', cell: (device) => device.model ?? '—' },
                          { header: 'Queue', cell: (device) => device.queueName ?? '—' },
                        ]}
                        rows={diff.goneDevices}
                        emptyMessage="No devices gone."
                      />
                    </div>
                    <div>
                      <h3 className="mb-1 text-sm font-medium text-amber-400">
                        Swapped ({diff.swappedQueues.length})
                      </h3>
                      <DataTable
                        columns={[
                          { header: 'Queue', cell: (swap) => swap.queueName },
                          {
                            header: 'Old → new',
                            cell: (swap) => `${swap.oldSerialNumber} → ${swap.newSerialNumber}`,
                          },
                        ]}
                        rows={diff.swappedQueues}
                        emptyMessage="No swapped queues."
                      />
                    </div>
                  </div>
                </div>
              )}
            </div>
          </Card>
        </>
      )}

      {rows.length === 0 && !scanning && failedScans.length === 0 && (
        <EmptyState
          title="No printers captured yet"
          message="Scan a print server (local machine, one remote server, or several — the AD picker finds them). Stored snapshots reappear here automatically."
        />
      )}
    </div>
  );
}
