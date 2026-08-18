import { useCallback, useEffect, useMemo, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AppInfoResponse,
  DeleteUnusedPortsResult,
  DhcpCheckResult,
  DhcpReservationInfo,
  ExportPrintCsvResult,
  ListPrintServersResult,
  NetworkPolicyResult,
  PortRemovalResult,
  PrintHint,
  PrintHintsResult,
  PrintHistoryResult,
  PrintServerDiff,
  PrintServerSnapshot,
  PrintSnapshotStamp,
  ProbeHostsResult,
  TargetRequest,
} from '../../shared/api-types';
import { runWithConcurrencyLimit } from '../../shared/concurrency';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Checkbox } from '../../shared/ui/Checkbox';
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
import { compareSortKeys } from '../../shared/sort';
import { toCsv, type CsvColumn } from '../../shared/csv';
import {
  classifySubnet,
  displayedLocation,
  filterPrinters,
  groupPrinters,
  hasLowToner,
  locationFlag,
  lowestTonerPercent,
  mergePrinters,
  siteOf,
  type MergedPrinter,
  type PrinterGroupMode,
  type SubnetClass,
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
  return (
    <div className="flex flex-col items-start gap-0.5">
      {printer.deviceError ? (
        <StatusBadge variant="error">{printer.deviceError.code}</StatusBadge>
      ) : printer.status ? (
        <StatusBadge variant={printer.status === 'Idle' ? 'success' : 'info'}>{printer.status}</StatusBadge>
      ) : (
        <span className="text-slate-500">—</span>
      )}
      {printer.deviceDataFromUtc && (
        <span
          className="text-xs text-slate-500"
          title="Gerät hat diesmal nicht geantwortet — Seriennummer, Modell und Gerätestandort stammen aus dem letzten erfolgreichen Scan. Toner und Status werden nicht übernommen."
        >
          Daten vom {new Date(printer.deviceDataFromUtc).toLocaleDateString()}
        </span>
      )}
    </div>
  );
}

/**
 * Location as the device reports it over SNMP (the source of truth), with a
 * warning when it diverges from the print server label — the pending items after
 * a printer swap. On unreachable devices we only show the print server value.
 */
function LocationCell({ printer }: { printer: MergedPrinter }) {
  const flag = locationFlag(printer);
  const shown = displayedLocation(printer);
  return (
    <div className="flex flex-col gap-0.5">
      <span className={shown ? undefined : 'text-slate-500'}>{shown ?? '—'}</span>
      {flag && (
        <span
          className="text-xs text-warn-400"
          title={
            flag.reason === 'missing'
              ? `Drucker meldet keinen Standort über SNMP. Am Printserver hinterlegt: „${flag.serverLocation}“. Standort am Drucker setzen.`
              : `Weicht vom Drucker (Wahrheit) ab. Am Printserver: „${flag.serverLocation}“. Printserver an den Drucker angleichen.`
          }
        >
          ⚠ Printserver: {flag.serverLocation}
        </span>
      )}
    </div>
  );
}

const subnetRank: Record<SubnetClass, number> = { target: 0, legacy: 1, foreign: 2, unknown: 3 };

/**
 * Where the device sits in the network policy (target subnet vs. old subnet to
 * migrate vs. foreign VLAN) and — once checked — whether it has a DHCP reservation.
 */
function NetworkCell({
  printer,
  policy,
  reservation,
  dhcpChecked,
}: {
  printer: MergedPrinter;
  policy: NetworkPolicyResult | null;
  reservation?: DhcpReservationInfo;
  dhcpChecked: boolean;
}) {
  const cls = policy ? classifySubnet(printer.deviceIp, policy) : 'unknown';
  return (
    <div className="flex flex-col items-start gap-0.5">
      {cls === 'target' && <span className="text-xs text-slate-500">Ziel-Netz</span>}
      {cls === 'legacy' && (
        <span title="Altes Netz — auf das Ziel-Netz migrieren">
          <StatusBadge variant="elevation">Alt-Netz</StatusBadge>
        </span>
      )}
      {cls === 'foreign' && (
        <span title="Fremdes VLAN — dort sollten keine Drucker liegen">
          <StatusBadge variant="elevation">Fremd-VLAN</StatusBadge>
        </span>
      )}
      {cls === 'unknown' && <span className="text-slate-600">—</span>}
      {dhcpChecked &&
        printer.deviceIp &&
        (reservation ? (
          <span title={reservation.name ? `Reservierung: ${reservation.name}` : 'DHCP-Reservierung vorhanden'}>
            <StatusBadge variant="success">Reserviert</StatusBadge>
          </span>
        ) : (
          <StatusBadge variant="error">Keine Reservierung</StatusBadge>
        ))}
    </div>
  );
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
  policy,
  reservation,
  dhcpChecked,
  expanded,
  onToggle,
  onOpenWebUi,
}: {
  printer: MergedPrinter;
  notification?: PrinterNotificationCheck;
  policy: NetworkPolicyResult | null;
  reservation?: DhcpReservationInfo;
  dhcpChecked: boolean;
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
        <td className="px-3 py-1.5 align-top">
          <LocationCell printer={printer} />
        </td>
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
          {printer.deviceIp ?? <span className="text-slate-500">—</span>}
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
        <td className="px-3 py-1.5 align-top">
          <NetworkCell
            printer={printer}
            policy={policy}
            reservation={reservation}
            dhcpChecked={dhcpChecked}
          />
        </td>
      </tr>
      {expanded && (
        <tr className="border-t border-slate-800/40 bg-slate-950/40">
          <td colSpan={10} className="px-3 py-2">
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

const distinctJoin = (values: readonly (string | null)[]): string =>
  [...new Set(values.filter((value): value is string => !!value))].join(' | ');

/**
 * Export columns. One row per physical device (the queue variants _B/_A5/_PCL are
 * already merged into one MergedPrinter), so no printer appears twice.
 */
const CSV_COLUMNS: CsvColumn<MergedPrinter>[] = [
  { key: 'printer', header: 'Printer', value: (printer) => printer.name, defaultOn: true },
  { key: 'serial', header: 'SerialNumber', value: (printer) => printer.serialNumber, defaultOn: true },
  { key: 'model', header: 'Model', value: (printer) => printer.model, defaultOn: true },
  { key: 'location', header: 'Location', value: displayedLocation, defaultOn: true },
  { key: 'ip', header: 'IPAddress', value: (printer) => printer.deviceIp, defaultOn: true },
  {
    key: 'status',
    header: 'Status',
    value: (printer) => (printer.deviceError ? printer.deviceError.code : printer.status),
    defaultOn: true,
  },
  {
    key: 'toner',
    header: 'Toner',
    value: (printer) =>
      printer.supplies
        .map((supply) => (supply.percent != null ? `${supply.description} ${supply.percent}%` : supply.description))
        .join(' | '),
  },
  {
    key: 'dataFrom',
    header: 'DeviceDataFrom',
    value: (printer) => (printer.deviceDataFromUtc ? formatTimestamp(printer.deviceDataFromUtc) : ''),
  },
  { key: 'deviceLocation', header: 'DeviceLocation', value: (printer) => printer.deviceLocation },
  { key: 'serverLocation', header: 'ServerLocation', value: (printer) => printer.serverLocation },
  { key: 'host', header: 'PortAddress', value: (printer) => printer.deviceAddress },
  { key: 'server', header: 'PrintServer', value: (printer) => printer.servers.join(' | ') },
  { key: 'site', header: 'Site', value: (printer) => printer.site },
  {
    key: 'driver',
    header: 'Driver',
    value: (printer) => distinctJoin(printer.queues.map((queue) => queue.driverName)),
  },
  {
    key: 'driverVersion',
    header: 'DriverVersion',
    value: (printer) => distinctJoin(printer.queues.map((queue) => queue.driverVersion)),
  },
  {
    key: 'queues',
    header: 'Queues',
    value: (printer) => printer.queues.map((queue) => queue.queueName).join(' | '),
  },
  { key: 'queueCount', header: 'QueueCount', value: (printer) => printer.queues.length },
];

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
  const [printerSort, setPrinterSort] = useState<{ key: string; dir: 'asc' | 'desc' } | null>(null);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  // Notification-config check results keyed by device address (upper-cased).
  const [notifications, setNotifications] = useState<Record<string, PrinterNotificationCheck>>({});
  const [notifChecking, setNotifChecking] = useState(false);
  // CCRX admin password, session-only (never persisted). Blank = factory Admin/Admin.
  const [ccrxPassword, setCcrxPassword] = useState('');
  const [hints, setHints] = useState<PrintHint[]>([]);
  const [exportMessage, setExportMessage] = useState<string | null>(null);
  const [exportOpen, setExportOpen] = useState(false);
  const [exportColumns, setExportColumns] = useState<ReadonlySet<string>>(
    () => new Set(CSV_COLUMNS.filter((column) => column.defaultOn).map((column) => column.key)),
  );
  const [maxParallelScans, setMaxParallelScans] = useState(4);

  const [networkPolicy, setNetworkPolicy] = useState<NetworkPolicyResult | null>(null);
  const [dhcpServer, setDhcpServer] = useState('');
  // Reservations keyed by device IP, from the last DHCP check.
  const [dhcpReserved, setDhcpReserved] = useState<Record<string, DhcpReservationInfo>>({});
  const [dhcpChecked, setDhcpChecked] = useState(false);
  const [dhcpChecking, setDhcpChecking] = useState(false);
  const [dhcpError, setDhcpError] = useState<string | null>(null);

  const [diffServer, setDiffServer] = useState('');
  const [diffHistory, setDiffHistory] = useState<PrintSnapshotStamp[]>([]);
  const [diffBaselineId, setDiffBaselineId] = useState<string>('');
  const [diff, setDiff] = useState<PrintServerDiff | null>(null);
  const [diffError, setDiffError] = useState<string | null>(null);

  // Unused-port cleanup (the one write): selection keyed by server + port name.
  const [selectedPorts, setSelectedPorts] = useState<ReadonlySet<string>>(new Set());
  const [deletingPorts, setDeletingPorts] = useState(false);
  const [portDeleteMessage, setPortDeleteMessage] = useState<string | null>(null);
  // Reachability of unused-port addresses (ICMP), keyed by host address upper-cased.
  const [portReach, setPortReach] = useState<Record<string, boolean>>({});
  const [portReachChecking, setPortReachChecking] = useState(false);

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
    invoke<NetworkPolicyResult>('printmanagement', 'getNetworkPolicy', {})
      .then((policy) => {
        setNetworkPolicy(policy);
        if (policy.dhcpServer) setDhcpServer(policy.dhcpServer);
      })
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

  // Look up DHCP reservations for every visible device that has a resolved IP.
  const checkDhcp = useCallback(
    (devices: readonly MergedPrinter[]) => {
      const server = dhcpServer.trim();
      if (server === '') {
        setDhcpError('Bitte einen DHCP-Server angeben.');
        return;
      }
      const ips = [...new Set(devices.map((printer) => printer.deviceIp).filter((ip): ip is string => !!ip))];
      if (ips.length === 0) {
        setDhcpError('Keine aufgelösten IPs — zuerst neu scannen.');
        return;
      }
      setDhcpChecking(true);
      setDhcpError(null);
      invoke<DhcpCheckResult>('printmanagement', 'checkDhcp', { target: toServerRequest(server), ips }, 60_000)
        .then((result) => {
          const reserved: Record<string, DhcpReservationInfo> = {};
          for (const entry of result.reserved) reserved[entry.ip] = entry;
          setDhcpReserved(reserved);
          setDhcpChecked(true);
        })
        .catch((error: unknown) => {
          setDhcpChecked(false);
          setDhcpError(describeError(error));
        })
        .finally(() => setDhcpChecking(false));
    },
    [dhcpServer, toServerRequest],
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
  const unusedPorts = visibleSnapshots.flatMap((snapshot) =>
    (snapshot.unusedPorts ?? []).map((port) => ({ server: snapshot.server, port })));
  const queueCount = printers.reduce((sum, printer) => sum + printer.queues.length, 0);

  const portKey = (server: string, name: string) => `${server} ${name}`;
  const togglePort = (server: string, name: string) =>
    setSelectedPorts((current) => {
      const next = new Set(current);
      const key = portKey(server, name);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  const selectedUnusedPorts = unusedPorts.filter((row) =>
    selectedPorts.has(portKey(row.server, row.port.name)));
  const allPortsSelected = unusedPorts.length > 0 && selectedUnusedPorts.length === unusedPorts.length;
  const toggleAllPorts = () =>
    setSelectedPorts(
      allPortsSelected
        ? new Set()
        : new Set(unusedPorts.map((row) => portKey(row.server, row.port.name))),
    );

  // The one write in this module: delete the picked ports per server (the server
  // refuses any that turned out to still be in use), then rescan to refresh the list.
  const deleteSelectedPorts = () => {
    if (selectedUnusedPorts.length === 0) return;
    const byServer = new Map<string, string[]>();
    for (const row of selectedUnusedPorts) {
      const list = byServer.get(row.server) ?? [];
      list.push(row.port.name);
      byServer.set(row.server, list);
    }
    const confirmed = window.confirm(
      `${selectedUnusedPorts.length} verwaiste(n) Port(s) auf ${byServer.size} Server endgültig löschen? ` +
        'Ports, die noch von einem Drucker verwendet werden, lehnt der Server ab.',
    );
    if (!confirmed) return;
    setDeletingPorts(true);
    setPortDeleteMessage(null);
    void Promise.all(
      [...byServer.entries()].map(([server, portNames]) =>
        invoke<DeleteUnusedPortsResult>(
          'printmanagement',
          'deleteUnusedPorts',
          { target: toServerRequest(server), portNames, confirmed: true },
          120_000,
        )
          .then((result): { server: string; results?: PortRemovalResult[]; error?: string } => ({
            server,
            results: result.results,
          }))
          .catch((error: unknown): { server: string; results?: PortRemovalResult[]; error?: string } => ({
            server,
            error: describeError(error),
          })),
      ),
    )
      .then((outcomes) => {
        const removed = outcomes.reduce(
          (sum, outcome) => sum + (outcome.results?.filter((result) => result.removed).length ?? 0),
          0,
        );
        const refused = outcomes.flatMap((outcome) => outcome.results?.filter((result) => !result.removed) ?? []);
        const serverErrors = outcomes.filter((outcome) => outcome.error);
        const parts = [`${removed} Port(s) gelöscht`];
        if (refused.length > 0)
          parts.push(`${refused.length} abgelehnt (${refused.map((result) => result.error ?? result.name).join('; ')})`);
        if (serverErrors.length > 0)
          parts.push(`${serverErrors.length} Server-Fehler (${serverErrors.map((outcome) => outcome.error).join('; ')})`);
        setPortDeleteMessage(parts.join(' · '));
        setSelectedPorts(new Set());
        scanServers([...byServer.keys()]);
      })
      .finally(() => setDeletingPorts(false));
  };

  // ICMP-check the address behind each unused port: unreachable = the device is
  // likely truly gone, a reassurance before deleting. Runs from the WEC machine.
  const checkPortsReachable = () => {
    const hosts = [...new Set(unusedPorts.map((row) => row.port.hostAddress).filter((h): h is string => !!h))];
    if (hosts.length === 0) return;
    setPortReachChecking(true);
    invoke<ProbeHostsResult>('connectivity', 'probeHosts', { hosts }, 120_000)
      .then((result) =>
        setPortReach((current) => ({
          ...current,
          ...Object.fromEntries(result.results.map((probe) => [probe.host.toUpperCase(), probe.reachable])),
        })),
      )
      .catch(() => {})
      .finally(() => setPortReachChecking(false));
  };

  const filteredPrinters = filterPrinters(printers, search);

  const toggleExportColumn = (key: string) =>
    setExportColumns((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

  // Exports exactly what the table shows: the merged devices after server filter
  // and search, with the picked columns. The host only owns the save dialog.
  const exportCsv = () => {
    const columns = CSV_COLUMNS.filter((column) => exportColumns.has(column.key));
    if (columns.length === 0 || filteredPrinters.length === 0) return;
    setExportMessage(null);
    invoke<ExportPrintCsvResult>('printmanagement', 'exportCsv', {
      csv: toCsv(filteredPrinters, columns),
    })
      .then((result) => {
        setExportMessage(result.cancelled ? 'Export cancelled.' : `Exported to ${result.filePath}`);
        if (!result.cancelled) setExportOpen(false);
      })
      .catch((error: unknown) => setExportMessage(describeError(error)));
  };

  const printerSortKey = (header: string, printer: MergedPrinter): string | number | null => {
    switch (header) {
      case 'Printer':
        return printer.name;
      case 'Model':
        return printer.model;
      case 'Serial':
        return printer.serialNumber;
      case 'Location':
        return displayedLocation(printer);
      case 'Status':
        return printer.deviceError ? printer.deviceError.code : printer.status;
      case 'Toner':
        return lowestTonerPercent(printer.supplies);
      case 'Notify':
        return printer.deviceAddress
          ? notifications[printer.deviceAddress.toUpperCase()]?.status ?? null
          : null;
      case 'IP':
        return printer.deviceIp;
      case 'IP / web UI':
        return printer.deviceAddress;
      case 'Netz':
        return networkPolicy ? subnetRank[classifySubnet(printer.deviceIp, networkPolicy)] : null;
      default:
        return null;
    }
  };

  // Sort before grouping so each group is ordered by the clicked column.
  const sortedPrinters = printerSort
    ? [...filteredPrinters].sort((a, b) =>
        compareSortKeys(printerSortKey(printerSort.key, a), printerSortKey(printerSort.key, b), printerSort.dir),
      )
    : filteredPrinters;
  const printerGroups = groupPrinters(sortedPrinters, groupMode);

  const togglePrinterSort = (key: string) =>
    setPrinterSort((current) =>
      current && current.key === key
        ? { key, dir: current.dir === 'asc' ? 'desc' : 'asc' }
        : { key, dir: 'asc' });

  const deviceCount = printers.filter((printer) => printer.deviceAddress !== null).length;
  const lowTonerCount = printers.filter((printer) => hasLowToner(printer.supplies)).length;
  const unreachableCount = printers.filter((printer) => printer.deviceError !== null).length;
  const failedScans = Object.entries(scanStates).filter(([, state]) => state.status === 'error');
  const legacyNetCount = networkPolicy
    ? printers.filter((printer) => classifySubnet(printer.deviceIp, networkPolicy) === 'legacy').length
    : 0;
  const unreservedCount = dhcpChecked
    ? printers.filter((printer) => printer.deviceIp && !dhcpReserved[printer.deviceIp]).length
    : 0;

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
        <Button onClick={() => setExportOpen((open) => !open)} disabled={printers.length === 0}>
          Export CSV
        </Button>
      </PageHeader>

      {exportOpen && printers.length > 0 && (
        <Card title="CSV-Export">
          <div className="flex flex-col gap-3">
            <p className="text-sm text-slate-400">
              Exportiert die {filteredPrinters.length} angezeigten Geräte — eine Zeile pro Gerät.
              Warteschlangen-Varianten (_B, _A5, _PCL …) sind zusammengefasst, der Name ist auf den
              Hauptdrucker gekürzt. Spalten auswählen:
            </p>
            <div className="grid grid-cols-2 gap-x-6 gap-y-1 sm:grid-cols-3 lg:grid-cols-4">
              {CSV_COLUMNS.map((column) => (
                <Checkbox
                  key={column.key}
                  label={column.header}
                  checked={exportColumns.has(column.key)}
                  onChange={() => toggleExportColumn(column.key)}
                />
              ))}
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                variant="primary"
                onClick={exportCsv}
                disabled={exportColumns.size === 0 || filteredPrinters.length === 0}
              >
                Exportieren
              </Button>
              <Button variant="ghost" onClick={() => setExportOpen(false)}>
                Abbrechen
              </Button>
              <span className="text-xs text-slate-500">{exportColumns.size} Spalte(n)</span>
            </div>
          </div>
        </Card>
      )}

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
            <SummaryMetric
              label="Alt-Netz"
              value={legacyNetCount}
              tone={legacyNetCount > 0 ? 'warning' : 'success'}
            />
            {dhcpChecked && (
              <SummaryMetric
                label="Ohne Reservierung"
                value={unreservedCount}
                tone={unreservedCount > 0 ? 'danger' : 'success'}
              />
            )}
            <SummaryMetric
              label="Verwaiste Ports"
              value={unusedPorts.length}
              tone={unusedPorts.length > 0 ? 'warning' : 'success'}
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

          {unusedPorts.length > 0 && (
            <DetailsDisclosure summary={`Verwaiste Ports (${unusedPorts.length})`}>
              <p className="mb-2 text-sm text-slate-400">
                TCP/IP-Ports, die kein Drucker mehr verwendet — Überbleibsel entfernter Drucker.
                Auswählen und löschen entfernt sie auf dem Printserver (als angemeldeter Admin);
                noch belegte Ports lehnt der Server ab.
              </p>
              {!adminCredentials && (
                <p className="mb-2 text-xs text-elevation-400">
                  Zum Löschen oben rechts „Sign in as admin" — sonst fehlen die Rechte auf dem Server.
                </p>
              )}
              <div className="mb-2 flex flex-wrap items-center gap-3">
                <Button
                  onClick={deleteSelectedPorts}
                  disabled={deletingPorts || selectedUnusedPorts.length === 0}
                  title="Löscht die ausgewählten Ports auf dem jeweiligen Printserver"
                >
                  {deletingPorts ? 'Lösche…' : `Ausgewählte löschen (${selectedUnusedPorts.length})`}
                </Button>
                <Button variant="ghost" onClick={toggleAllPorts} disabled={deletingPorts}>
                  {allPortsSelected ? 'Auswahl aufheben' : 'Alle auswählen'}
                </Button>
                <Button
                  variant="ghost"
                  onClick={checkPortsReachable}
                  disabled={portReachChecking}
                  title="ICMP-Ping auf die Adresse jedes Ports (von der WEC-Maschine aus) — nicht erreichbar deutet auf ein wirklich entferntes Gerät hin"
                >
                  {portReachChecking ? 'Prüfe…' : 'Erreichbarkeit prüfen'}
                </Button>
                {portDeleteMessage && <span className="text-xs text-slate-400">{portDeleteMessage}</span>}
              </div>
              <DataTable
                columns={[
                  {
                    header: '',
                    cell: (row) => (
                      <input
                        type="checkbox"
                        checked={selectedPorts.has(portKey(row.server, row.port.name))}
                        onChange={() => togglePort(row.server, row.port.name)}
                        className="h-4 w-4 accent-accent-500"
                        aria-label={`Port ${row.port.name} auswählen`}
                      />
                    ),
                  },
                  { header: 'Print server', cell: (row) => row.server },
                  { header: 'Port', mono: true, cell: (row) => row.port.name },
                  { header: 'Adresse', mono: true, cell: (row) => row.port.hostAddress ?? '—' },
                  {
                    header: 'Erreichbar',
                    cell: (row) => {
                      const reachable = row.port.hostAddress
                        ? portReach[row.port.hostAddress.toUpperCase()]
                        : undefined;
                      if (reachable === undefined) {
                        return <span className="text-slate-600">—</span>;
                      }
                      return reachable ? (
                        <StatusBadge variant="success">Erreichbar</StatusBadge>
                      ) : (
                        <StatusBadge variant="neutral">Keine Antwort</StatusBadge>
                      );
                    },
                  },
                ]}
                rows={unusedPorts}
                getRowKey={(row) => portKey(row.server, row.port.name)}
                emptyMessage="Keine verwaisten Ports."
              />
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

              <div className="flex flex-wrap items-center gap-3">
                <label className="flex items-center gap-2 text-sm text-slate-400">
                  DHCP-Server
                  <Input
                    type="text"
                    value={dhcpServer}
                    onChange={(event) => setDhcpServer(event.target.value)}
                    placeholder="z. B. dc01"
                    aria-label="DHCP server"
                    className="w-52"
                  />
                </label>
                <Button
                  onClick={() => checkDhcp(filteredPrinters)}
                  disabled={dhcpChecking || filteredPrinters.every((printer) => !printer.deviceIp)}
                  title="Prüft für jeden Drucker mit IP, ob am DHCP-Server eine Reservierung hinterlegt ist"
                >
                  {dhcpChecking ? 'Prüfe…' : 'DHCP-Reservierungen prüfen'}
                </Button>
                {dhcpError && <span className="text-xs text-fail-400">{dhcpError}</span>}
                {dhcpChecked && !dhcpError && (
                  <span className="text-xs text-slate-500">
                    {unreservedCount === 0
                      ? 'Alle geprüften Drucker haben eine Reservierung.'
                      : `${unreservedCount} Drucker ohne Reservierung.`}
                  </span>
                )}
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
                            {['Printer', 'Model', 'Serial', 'Location', 'Status', 'Toner', 'Notify', 'IP', 'IP / web UI', 'Netz'].map(
                              (header) => {
                                const isSorted = printerSort?.key === header;
                                return (
                                  <th
                                    key={header}
                                    scope="col"
                                    aria-sort={
                                      isSorted ? (printerSort!.dir === 'asc' ? 'ascending' : 'descending') : undefined
                                    }
                                    onClick={() => togglePrinterSort(header)}
                                    onKeyDown={(event) => {
                                      if (event.key === 'Enter' || event.key === ' ') {
                                        event.preventDefault();
                                        togglePrinterSort(header);
                                      }
                                    }}
                                    tabIndex={0}
                                    className="cursor-pointer select-none border-b border-slate-800 px-3 py-1.5 text-xs font-medium uppercase tracking-wide text-slate-500 hover:text-slate-300"
                                  >
                                    <span className="inline-flex items-center gap-1">
                                      {header}
                                      <span aria-hidden className="text-[10px] text-slate-600">
                                        {isSorted ? (printerSort!.dir === 'asc' ? '▲' : '▼') : '↕'}
                                      </span>
                                    </span>
                                  </th>
                                );
                              },
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
                              policy={networkPolicy}
                              reservation={printer.deviceIp ? dhcpReserved[printer.deviceIp] : undefined}
                              dhcpChecked={dhcpChecked}
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
