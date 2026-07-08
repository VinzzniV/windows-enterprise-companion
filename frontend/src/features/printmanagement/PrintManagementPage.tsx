import { useCallback, useEffect, useMemo, useState } from 'react';
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
  TargetRequest,
} from '../../shared/api-types';
import { runWithConcurrencyLimit } from '../../shared/concurrency';
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
  siteOf,
  type MergedPrinter,
  type PrinterGroupMode,
} from './printers';
import type { PrinterNotificationCheck } from '../../shared/api-types';

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

/** Notification-config check result for one device (MPS provider alerting). */
function NotificationBadge({ check }: { check?: PrinterNotificationCheck }) {
  if (!check) {
    return <span className="text-slate-600">—</span>;
  }
  if (check.status === 'OK') {
    return <StatusBadge variant="success">Configured</StatusBadge>;
  }
  if (check.status === 'NOT_CHECKED') {
    return (
      <span title={check.error ?? 'Not checked'}>
        <StatusBadge variant="neutral">Not checked</StatusBadge>
      </span>
    );
  }
  const failed = check.rules.filter((rule) => !rule.passed).length;
  return <StatusBadge variant="error">{failed} issue{failed === 1 ? '' : 's'}</StatusBadge>;
}

/** One physical device as a single compact row; expands to its queues and full toner. */
function PrinterRow({
  printer,
  notification,
  expanded,
  onToggle,
  onOpenWebUi,
}: {
  printer: MergedPrinter;
  notification?: PrinterNotificationCheck;
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
        <td className="px-3 py-1.5 align-top">
          <NotificationBadge check={notification} />
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
          <td colSpan={8} className="px-3 py-2">
            {notification && notification.status !== 'NOT_CHECKED' && (
              <div className="mb-3">
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-500">
                  Service-provider notification
                </h4>
                <ul className="flex flex-col gap-0.5 text-sm">
                  {notification.rules.map((rule) => (
                    <li key={rule.id} className={rule.passed ? 'text-slate-300' : 'text-fail-300'}>
                      {rule.passed ? '✓' : '✗'} {rule.title} — <span className="text-slate-400">{rule.detail}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {notification?.status === 'NOT_CHECKED' && (
              <p className="mb-3 text-sm text-slate-400">
                Notification config not checked: {notification.error ?? 'device not reachable or not a Command Center RX.'}
              </p>
            )}
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
                        {queue.portAddress && queue.portAddress !== queue.portName && (
                          <>
                            {' → '}
                            <span className="font-mono text-slate-400">{queue.portAddress}</span>
                          </>
                        )}
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

// The notification check is lightweight HTTPS (no SNMP), so run many at once
// instead of throttling to the scan limit — the whole fleet resolves fast.
const NOTIFICATION_CONCURRENCY = 16;

export function PrintManagementPage() {
  const targets = useTargetsOptional();
  const adminCredentials = targets?.adminCredentials ?? null;
  const savedTargets = targets?.savedTargets ?? [];
  const [newServer, setNewServer] = useState('');
  const [snapshots, setSnapshots] = useState<Record<string, PrintServerSnapshot>>({});
  const [scanStates, setScanStates] = useState<Record<string, ServerScanState>>({});
  const [scanning, setScanning] = useState(false);
  const [serverFilter, setServerFilter] = useState('');
  const [search, setSearch] = useState('');
  const [groupMode, setGroupMode] = useState<PrinterGroupMode>('none');
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  // Notification-config check results keyed by device address (upper-cased).
  const [notifications, setNotifications] = useState<Record<string, PrinterNotificationCheck>>({});
  const [notifChecking, setNotifChecking] = useState(false);
  // CCRX admin password, session-only (never persisted). Blank = factory Admin/Admin.
  const [ccrxPassword, setCcrxPassword] = useState('');
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

  // Print servers are always remote; carry the session admin identity when set.
  const toServerRequest = useCallback(
    (host: string): TargetRequest =>
      adminCredentials && adminCredentials.userName.trim() !== ''
        ? {
            host,
            userName: adminCredentials.userName.trim(),
            domain: adminCredentials.domain.trim() || null,
            password: adminCredentials.password,
          }
        : { host },
    [adminCredentials],
  );

  const scanServers = useCallback(
    (hosts: string[]) => {
      if (hosts.length === 0) return;
      setScanning(true);
      setExportMessage(null);
      setScanStates((previous) => ({
        ...previous,
        ...Object.fromEntries(hosts.map((host) => [host.toUpperCase(), { status: 'loading' } as ServerScanState])),
      }));

      void runWithConcurrencyLimit(hosts, maxParallelScans, async (host) => {
        try {
          const snapshot = await invoke<PrintServerSnapshot>(
            'printmanagement', 'scanServer', { target: toServerRequest(host) }, 300_000);
          setSnapshots((previous) => ({ ...previous, [snapshot.server]: snapshot }));
          setScanStates((previous) => ({ ...previous, [snapshot.server]: { status: 'done' } }));
        } catch (error) {
          setScanStates((previous) => ({
            ...previous,
            [host.toUpperCase()]: { status: 'error', error: describeError(error) },
          }));
        }
      })
        .then(loadHints)
        .finally(() => setScanning(false));
    },
    [maxParallelScans, loadHints, toServerRequest],
  );

  // Add a print server: remembered as a saved target (stays until removed) and scanned once.
  const addServer = useCallback(() => {
    const host = newServer.trim();
    if (host === '') return;
    setNewServer('');
    void targets?.saveTarget({ label: host, host, role: 'PrintServer' }).catch(() => {});
    scanServers([host]);
  }, [newServer, targets, scanServers]);

  const removeServer = useCallback(
    (server: string) => {
      void invoke('printmanagement', 'deleteServer', { server }).catch(() => {});
      const saved = savedTargets.find(
        (target) => target.role === 'PrintServer' && target.host.toUpperCase() === server.toUpperCase());
      if (saved) {
        void targets?.deleteTarget(saved.id).catch(() => {});
      }
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
    [savedTargets, targets],
  );

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

  // Check the notification config of every visible device that has an IP.
  const checkNotifications = useCallback(
    (devices: readonly MergedPrinter[]) => {
      const targets = devices.filter((printer) => printer.deviceAddress);
      if (targets.length === 0) return;
      setNotifChecking(true);
      void runWithConcurrencyLimit(targets, NOTIFICATION_CONCURRENCY, async (printer) => {
        const host = printer.deviceAddress!;
        try {
          const check = await invoke<PrinterNotificationCheck>(
            'printmanagement',
            'checkNotificationConfig',
            { host, siteCode: siteOf(printer.name), password: ccrxPassword || null },
            60_000,
          );
          setNotifications((previous) => ({ ...previous, [host.toUpperCase()]: check }));
        } catch {
          // A hard failure just leaves the device without a result badge.
        }
      }).finally(() => setNotifChecking(false));
    },
    [ccrxPassword],
  );

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

  // The managed print servers: saved ones (persisted) merged with any that have a
  // stored snapshot. De-duplicated case-insensitively, saved display name preferred.
  const managedServers = useMemo(() => {
    const byKey = new Map<string, string>();
    for (const server of Object.keys(snapshots)) byKey.set(server.toUpperCase(), server);
    for (const target of savedTargets) {
      if (target.role === 'PrintServer') byKey.set(target.host.toUpperCase(), target.host);
    }
    return [...byKey.values()].sort((a, b) => a.localeCompare(b));
  }, [snapshots, savedTargets]);

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
        subtitle="Printer inventory from your saved print servers, enriched per device over SNMP — serials, locations, toner levels and the lease-swap history."
      >
        <Button
          variant="primary"
          onClick={() => scanServers(managedServers)}
          disabled={scanning || managedServers.length === 0}
        >
          {scanning ? 'Scanning…' : 'Rescan all'}
        </Button>
        <Button onClick={exportCsv} disabled={printers.length === 0}>
          Export CSV
        </Button>
      </PageHeader>

      <Card title="Print servers">
        <div className="flex flex-col gap-3">
          <p className="text-sm text-slate-400">
            Add each print server once — they stay saved until you remove them. Scanning reads
            the queues over WinRM and enriches each device over SNMP (as the signed-in admin).
          </p>
          <form
            className="flex flex-wrap items-center gap-2"
            onSubmit={(event) => {
              event.preventDefault();
              addServer();
            }}
          >
            <Input
              type="text"
              value={newServer}
              onChange={(event) => setNewServer(event.target.value)}
              placeholder="Print server hostname (e.g. pk-srvprint01)"
              aria-label="Print server hostname"
              className="w-80"
            />
            <Button type="submit" disabled={newServer.trim() === '' || scanning}>
              Add &amp; scan
            </Button>
          </form>

          {managedServers.length === 0 ? (
            <p className="text-sm text-slate-500">No print servers yet — add one above.</p>
          ) : (
            <ul className="flex flex-col divide-y divide-slate-800/70 rounded border border-slate-800">
              {managedServers.map((server) => {
                const state = scanStates[server.toUpperCase()];
                const snapshot = snapshots[server];
                return (
                  <li key={server} className="flex flex-wrap items-center gap-3 px-3 py-2 text-sm">
                    <span className="font-medium text-slate-100">{server}</span>
                    {state?.status === 'loading' && <StatusBadge variant="info">Scanning…</StatusBadge>}
                    {state?.status === 'error' && (
                      <span className="text-xs text-fail-400" title={state.error}>
                        Scan failed
                      </span>
                    )}
                    {snapshot ? (
                      <span className="text-xs text-slate-500">
                        {snapshot.printers.length} printer{snapshot.printers.length === 1 ? '' : 's'} · captured{' '}
                        {formatTimestamp(snapshot.capturedAtUtc)}
                      </span>
                    ) : (
                      !state && <span className="text-xs text-slate-500">not scanned yet</span>
                    )}
                    <div className="ml-auto flex items-center gap-2">
                      <Button variant="ghost" onClick={() => scanServers([server])} disabled={scanning}>
                        Rescan
                      </Button>
                      <Button variant="ghost" onClick={() => removeServer(server)}>
                        Remove
                      </Button>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </Card>

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
            {serverFilter !== '' && snapshots[serverFilter] && (
              <span className="text-xs text-slate-500">
                Captured {formatTimestamp(snapshots[serverFilter].capturedAtUtc)}
              </span>
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
                <div className="ml-auto flex items-center gap-2">
                  <Input
                    type="password"
                    value={ccrxPassword}
                    onChange={(event) => setCcrxPassword(event.target.value)}
                    placeholder="CCRX admin pw (optional)"
                    aria-label="Command Center RX admin password"
                    autoComplete="off"
                    className="w-52"
                  />
                  <Button
                    onClick={() => checkNotifications(filteredPrinters)}
                    disabled={notifChecking || filteredPrinters.every((printer) => !printer.deviceAddress)}
                    title="Check whether these printers notify the service provider (SMTP + low-toner event report)"
                  >
                    {notifChecking ? 'Checking…' : 'Check notifications'}
                  </Button>
                </div>
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
                            {['Printer', 'Model', 'Serial', 'Location', 'Status', 'Toner', 'Notify', 'IP / web UI'].map(
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
                              notification={
                                printer.deviceAddress
                                  ? notifications[printer.deviceAddress.toUpperCase()]
                                  : undefined
                              }
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
          message="Add a print server above and scan it. Saved servers and their snapshots reappear here automatically."
        />
      )}
    </div>
  );
}
