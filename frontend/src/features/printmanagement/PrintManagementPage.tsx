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
import { SavedTargetsBar } from '../../shared/targets/SavedTargetsBar';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { TonerBar } from './TonerBar';
import {
  filterPrinters,
  groupPrinters,
  hasLowToner,
  mergePrinters,
  type MergedPrinter,
  type PrinterGroupMode,
} from './printers';

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

function PrinterStatus({ printer }: { printer: MergedPrinter }) {
  if (printer.deviceError) {
    return <StatusBadge variant="error">{printer.deviceError.code}</StatusBadge>;
  }
  if (printer.status) {
    return (
      <StatusBadge variant={printer.status === 'Idle' ? 'success' : 'info'}>{printer.status}</StatusBadge>
    );
  }
  return <span className="text-slate-500">—</span>;
}

/** One physical device as a single compact row; expands to its queues and full toner. */
function PrinterRow({
  printer,
  expanded,
  onToggle,
  onOpenWebUi,
}: {
  printer: MergedPrinter;
  expanded: boolean;
  onToggle: () => void;
  onOpenWebUi: (address: string) => void;
}) {
  return (
    <>
      <tr
        onClick={onToggle}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            onToggle();
          }
        }}
        tabIndex={0}
        role="button"
        aria-expanded={expanded}
        className="cursor-pointer border-t border-slate-800/70 even:bg-slate-800/20 hover:bg-slate-800/40 focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-accent-400"
      >
        <td className="px-3 py-1.5 align-top">
          <div className="flex items-center gap-1.5">
            <span className={`text-slate-500 transition-transform ${expanded ? 'rotate-90' : ''}`}>›</span>
            <span className="font-medium text-slate-100">{printer.name}</span>
            {printer.queues.length > 1 && (
              <span className="text-xs text-slate-500">{printer.queues.length} queues</span>
            )}
          </div>
        </td>
        <td className="px-3 py-1.5 align-top">{printer.model ?? '—'}</td>
        <td className="px-3 py-1.5 align-top font-mono text-[13px] tabular-nums">
          {printer.serialNumber ?? '—'}
        </td>
        <td className="px-3 py-1.5 align-top">{printer.location ?? '—'}</td>
        <td className="px-3 py-1.5 align-top">
          <PrinterStatus printer={printer} />
        </td>
        <td className="px-3 py-1.5 align-top">
          <TonerBar supplies={printer.supplies} />
        </td>
        <td className="px-3 py-1.5 align-top font-mono text-[13px] tabular-nums">
          {printer.deviceAddress ? (
            <button
              type="button"
              onClick={(event) => {
                event.stopPropagation();
                onOpenWebUi(printer.deviceAddress!);
              }}
              title={`Open https://${printer.deviceAddress}/ in the browser`}
              className="cursor-pointer text-accent-400 underline-offset-2 hover:underline"
            >
              {printer.deviceAddress}
            </button>
          ) : (
            <span className="text-slate-500">—</span>
          )}
        </td>
      </tr>
      {expanded && (
        <tr className="border-t border-slate-800/40 bg-slate-950/40">
          <td colSpan={7} className="px-3 py-2">
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              <div>
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-500">Queues</h4>
                <ul className="flex flex-col gap-1.5 text-sm">
                  {printer.queues.map((queue) => (
                    <li key={`${queue.server}-${queue.queueName}`} className="flex flex-col text-slate-300">
                      <span className="font-mono text-xs text-slate-400">
                        \\{queue.server}\{queue.shareName ?? queue.queueName}
                      </span>
                      <span className="text-xs text-slate-500">
                        Port: <span className="font-mono">{queue.portName ?? '—'}</span>
                        {queue.driverName && (
                          <>
                            {' · Driver: '}
                            {queue.driverName}
                            {queue.driverVersion ? ` (${queue.driverVersion})` : ''}
                          </>
                        )}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
              {printer.supplies.length > 0 && (
                <div>
                  <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-500">Toner</h4>
                  <ul className="flex flex-col gap-0.5 text-sm">
                    {printer.supplies.map((supply) => (
                      <li
                        key={supply.description}
                        className={supply.isLow ? 'text-fail-300' : 'text-slate-300'}
                      >
                        {supply.description}
                        {supply.percent != null ? ` ${supply.percent}%` : ' (level unknown)'}
                        {supply.isLow ? ' — low' : ''}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

interface ServerScanState {
  status: 'loading' | 'done' | 'error';
  error?: string;
}

export function PrintManagementPage() {
  const adminCredentials = useTargetsOptional()?.adminCredentials ?? null;
  const [selection, setSelection] = useState<TargetSelection>(LOCAL_TARGET_SELECTION);
  const [snapshots, setSnapshots] = useState<Record<string, PrintServerSnapshot>>({});
  const [scanStates, setScanStates] = useState<Record<string, ServerScanState>>({});
  const [scanning, setScanning] = useState(false);
  const [serverFilter, setServerFilter] = useState('');
  const [search, setSearch] = useState('');
  const [groupMode, setGroupMode] = useState<PrinterGroupMode>('none');
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
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
        ? toHostList(selection).map((host) => toTargetRequestForHost(selection, host, adminCredentials))
        : [toTargetRequest(selection, adminCredentials)];
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
  }, [selection, maxParallelScans, loadHints, adminCredentials]);

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
  const serverEntries = visibleSnapshots.flatMap((snapshot) =>
    snapshot.printers.map((entry) => ({ server: snapshot.server, entry })));
  const printers = mergePrinters(serverEntries);
  const queueCount = printers.reduce((sum, printer) => sum + printer.queues.length, 0);
  const filteredPrinters = filterPrinters(printers, search);
  const printerGroups = groupPrinters(filteredPrinters, groupMode);

  const deviceCount = printers.filter((printer) => printer.deviceAddress !== null).length;
  const lowTonerCount = printers.filter((printer) => hasLowToner(printer.supplies)).length;
  const unreachableCount = printers.filter((printer) => printer.deviceError !== null).length;
  const failedScans = Object.entries(scanStates).filter(([, state]) => state.status === 'error');

  const toggleExpanded = (key: string) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Print Management"
        subtitle="Printer inventory from the print servers, enriched per device over SNMP — serials, locations, toner levels and the lease-swap history."
      >
        <Button variant="primary" onClick={scan} disabled={scanning}>
          {scanning ? 'Scanning…' : 'Scan'}
        </Button>
        <Button onClick={exportCsv} disabled={printers.length === 0}>
          Export CSV
        </Button>
      </PageHeader>

      <TargetSelector selection={selection} onChange={setSelection} disabled={scanning} allowMultiple hideCredentials />

      <SavedTargetsBar
        role="PrintServer"
        label="Saved print servers"
        currentHost={selection.mode === 'remote' ? selection.host : ''}
        currentUserName={adminCredentials?.userName ?? null}
        onPick={(target) =>
          setSelection((current) => ({
            ...current,
            mode: 'remote',
            host: target.host,
          }))
        }
      />

      {scanning && <Spinner label="Scanning print servers" />}
      {exportMessage && <p className="text-sm text-slate-300">{exportMessage}</p>}

      {failedScans.map(([server, state]) => (
        <ErrorState
          key={server}
          title={`Scan of ${server} failed`}
          message={state.error ?? 'Unknown error'}
        />
      ))}

      {printers.length > 0 && (
        <>
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-400">Print server</span>
              <Select
                fullWidth={false}
                value={serverFilter}
                onChange={(event) => setServerFilter(event.target.value)}
              >
                <option value="">All servers</option>
                {servers.map((server) => (
                  <option key={server} value={server}>
                    {server}
                  </option>
                ))}
              </Select>
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
            <SummaryMetric label="Printers" value={printers.length} />
            <SummaryMetric label="Queues" value={queueCount} />
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
                    <span className="mr-2 text-xs uppercase tracking-wide text-warn-400">
                      {hint.category}
                    </span>
                    {hint.message}
                  </li>
                ))}
              </ul>
            </DetailsDisclosure>
          )}

          <Card title="Printers">
            <div className="flex flex-col gap-3">
              <div className="flex flex-wrap items-center gap-3">
                <Input
                  type="search"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Search name, serial, location, model or IP"
                  aria-label="Search printers"
                  className="w-72"
                />
                <label className="flex items-center gap-2 text-sm text-slate-400">
                  Group by
                  <Select
                    fullWidth={false}
                    value={groupMode}
                    onChange={(event) => setGroupMode(event.target.value as PrinterGroupMode)}
                    aria-label="Group printers by"
                  >
                    <option value="none">None</option>
                    <option value="site">Location</option>
                    <option value="status">Status</option>
                    <option value="server">Print server</option>
                    <option value="model">Model</option>
                  </Select>
                </label>
                <span className="text-xs text-slate-500">
                  {filteredPrinters.length} of {printers.length} device
                  {printers.length === 1 ? '' : 's'}
                </span>
              </div>

              {filteredPrinters.length === 0 ? (
                <p className="text-sm text-slate-400">No printers match the search.</p>
              ) : (
                printerGroups.map((group) => (
                  <div key={group.label || 'all'} className="flex flex-col gap-1">
                    {group.label !== '' && (
                      <h3 className="px-1 text-xs font-semibold uppercase tracking-wide text-slate-500">
                        {group.label}
                        <span className="ml-2 font-normal normal-case tracking-normal text-slate-600">
                          {group.printers.length} device{group.printers.length === 1 ? '' : 's'}
                        </span>
                      </h3>
                    )}
                    <div className="overflow-x-auto">
                      <table className="w-full border-collapse text-left text-sm">
                        <thead>
                          <tr>
                            {['Printer', 'Model', 'Serial', 'Location', 'Status', 'Toner', 'IP / web UI'].map(
                              (header) => (
                                <th
                                  key={header}
                                  className="border-b border-slate-800 px-3 py-1.5 text-xs font-medium uppercase tracking-wide text-slate-500"
                                >
                                  {header}
                                </th>
                              ),
                            )}
                          </tr>
                        </thead>
                        <tbody>
                          {group.printers.map((printer) => (
                            <PrinterRow
                              key={printer.key}
                              printer={printer}
                              expanded={expanded.has(printer.key)}
                              onToggle={() => toggleExpanded(printer.key)}
                              onOpenWebUi={openWebUi}
                            />
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                ))
              )}
            </div>
          </Card>

          <Card title="Lease swap history">
            <div className="flex flex-col gap-3">
              <p className="text-sm text-slate-400">
                Serial-number-based comparison between two scans of the same server — new,
                returned and swapped devices for the lease renewal.
              </p>
              <div className="flex flex-wrap items-center gap-3">
                <Select
                  fullWidth={false}
                  value={diffServer}
                  onChange={(event) => loadDiffHistory(event.target.value)}
                  aria-label="Diff server"
                >
                  <option value="">Select server…</option>
                  {servers.map((server) => (
                    <option key={server} value={server}>
                      {server}
                    </option>
                  ))}
                </Select>
                {diffServer !== '' && (
                  <>
                    <Select
                      fullWidth={false}
                      value={diffBaselineId}
                      onChange={(event) => setDiffBaselineId(event.target.value)}
                      aria-label="Baseline snapshot"
                    >
                      <option value="">Previous scan (default)</option>
                      {diffHistory.slice(1).map((stamp) => (
                        <option key={stamp.id} value={stamp.id}>
                          {formatTimestamp(stamp.capturedAtUtc)}
                        </option>
                      ))}
                    </Select>
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
                      <h3 className="mb-1 text-sm font-medium text-ok-400">
                        New ({diff.newDevices.length})
                      </h3>
                      <DataTable
                        columns={[
                          { header: 'Serial', mono: true, cell: (device) => device.serialNumber },
                          { header: 'Model', cell: (device) => device.model ?? '—' },
                          { header: 'Queue', cell: (device) => device.queueName ?? '—' },
                        ]}
                        rows={diff.newDevices}
                        emptyMessage="No new devices."
                      />
                    </div>
                    <div>
                      <h3 className="mb-1 text-sm font-medium text-fail-400">
                        Gone ({diff.goneDevices.length})
                      </h3>
                      <DataTable
                        columns={[
                          { header: 'Serial', mono: true, cell: (device) => device.serialNumber },
                          { header: 'Model', cell: (device) => device.model ?? '—' },
                          { header: 'Queue', cell: (device) => device.queueName ?? '—' },
                        ]}
                        rows={diff.goneDevices}
                        emptyMessage="No devices gone."
                      />
                    </div>
                    <div>
                      <h3 className="mb-1 text-sm font-medium text-warn-400">
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

      {printers.length === 0 && !scanning && failedScans.length === 0 && (
        <EmptyState
          title="No printers captured yet"
          message="Scan a print server (local machine, one remote server, or several — the AD picker finds them). Stored snapshots reappear here automatically."
        />
      )}
    </div>
  );
}
