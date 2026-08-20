import type {
  DhcpReservationInfo,
  NetworkPolicyResult,
  PrinterNotificationCheck,
} from '../../shared/api-types';
import { TonerBar } from './TonerBar';
import { ContextualPrintStatus } from './PrintStatusView';
import {
  dhcpReservationStatus,
  notificationConfigStatus,
  printerNetworkStatus,
  printerObservationStatus,
} from './printStatus';
import {
  classifySubnet,
  displayedLocation,
  locationFlag,
  type MergedPrinter,
  type PrinterGroup,
} from './printers';
import type { PrinterInventorySort, PrinterInventorySortKey } from './printerInventoryView';

interface PrinterInventoryTableProps {
  groups: readonly PrinterGroup[];
  sort: PrinterInventorySort | null;
  onSort: (header: PrinterInventorySortKey) => void;
  notifications: Readonly<Record<string, PrinterNotificationCheck>>;
  networkPolicy: NetworkPolicyResult | null;
  dhcpReservations: Readonly<Record<string, DhcpReservationInfo>>;
  dhcpCheckedIps: ReadonlySet<string>;
  expanded: ReadonlySet<string>;
  onToggleExpanded: (printerKey: string) => void;
  onOpenWebUi: (address: string) => void;
}

const headers = [
  'Printer',
  'Model',
  'Serial',
  'Location',
  'Status',
  'Toner',
  'Notify',
  'IP',
  'IP / web UI',
  'Network',
] as const;

function PrinterStatus({ printer }: { printer: MergedPrinter }) {
  const presentation = printerObservationStatus(printer.deviceError?.code ?? null, printer.status);
  return (
    <div className="flex flex-col items-start gap-0.5">
      <ContextualPrintStatus presentation={presentation} />
      {printer.deviceDataFromUtc && (
        <span
          className="text-xs text-muted"
          title="Device did not respond during this scan. Serial number, model and device location come from the last successful scan; toner and status are not carried forward."
        >
          Data from {new Date(printer.deviceDataFromUtc).toLocaleDateString()}
        </span>
      )}
    </div>
  );
}

/** Device-reported SNMP location with the print-server value as discrepancy context. */
function LocationCell({ printer }: { printer: MergedPrinter }) {
  const flag = locationFlag(printer);
  const shown = displayedLocation(printer);
  return (
    <div className="flex flex-col gap-0.5">
      <span className={shown ? undefined : 'text-muted'}>{shown ?? '—'}</span>
      {flag && (
        <span
          className="text-xs text-warn-400"
          title={
            flag.reason === 'missing'
              ? `The printer reports no location over SNMP. Print server value: “${flag.serverLocation}”. Set the location on the printer.`
              : `Differs from the printer-reported source of truth. Print server value: “${flag.serverLocation}”. Align the print server with the printer.`
          }
        >
          ⚠ Print server: {flag.serverLocation}
        </span>
      )}
    </div>
  );
}

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
  const classification = policy ? classifySubnet(printer.deviceIp, policy) : 'unknown';
  return (
    <div className="flex flex-col items-start gap-0.5">
      <ContextualPrintStatus
        presentation={printerNetworkStatus(classification)}
        title={
          classification === 'legacy'
            ? 'Legacy subnet — migrate to the target subnet'
            : classification === 'foreign'
              ? 'Foreign VLAN — printers are not expected there'
              : undefined
        }
      />
      {dhcpChecked && printer.deviceIp && (
        <ContextualPrintStatus
          presentation={dhcpReservationStatus(!!reservation)}
          title={reservation?.name ? `Reservation: ${reservation.name}` : undefined}
        />
      )}
    </div>
  );
}

function NotificationStatus({ check }: { check?: PrinterNotificationCheck }) {
  if (!check) {
    return <span className="text-slate-600">—</span>;
  }
  const failed = check.rules.filter((rule) => !rule.passed).length;
  return <ContextualPrintStatus presentation={notificationConfigStatus(check.status, failed, check.error)} />;
}

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
              <span className="text-xs text-muted">{printer.queues.length} queues</span>
            )}
          </div>
        </td>
        <td className="px-3 py-1.5 align-top">{printer.model ?? '—'}</td>
        <td className="px-3 py-1.5 align-top font-mono text-[13px] tabular-nums">
          {printer.serialNumber ?? '—'}
        </td>
        <td className="px-3 py-1.5 align-top"><LocationCell printer={printer} /></td>
        <td className="px-3 py-1.5 align-top"><PrinterStatus printer={printer} /></td>
        <td className="px-3 py-1.5 align-top"><TonerBar supplies={printer.supplies} /></td>
        <td className="px-3 py-1.5 align-top"><NotificationStatus check={notification} /></td>
        <td className="px-3 py-1.5 align-top font-mono text-[13px] tabular-nums">
          {printer.deviceIp ?? <span className="text-muted">—</span>}
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
          ) : <span className="text-muted">—</span>}
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
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">
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
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">Queues</h4>
                <ul className="flex flex-col gap-1.5 text-sm">
                  {printer.queues.map((queue) => (
                    <li key={`${queue.server}-${queue.queueName}`} className="flex flex-col text-slate-300">
                      <span className="font-mono text-xs text-slate-400">
                        \\{queue.server}\{queue.shareName ?? queue.queueName}
                      </span>
                      <span className="text-xs text-muted">
                        Port: <span className="font-mono">{queue.portName ?? '—'}</span>
                        {queue.portAddress && queue.portAddress !== queue.portName && (
                          <>{' → '}<span className="font-mono text-slate-400">{queue.portAddress}</span></>
                        )}
                        {queue.driverName && (
                          <>{' · Driver: '}{queue.driverName}{queue.driverVersion ? ` (${queue.driverVersion})` : ''}</>
                        )}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
              {printer.supplies.length > 0 && (
                <div>
                  <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">Toner</h4>
                  <ul className="flex flex-col gap-0.5 text-sm">
                    {printer.supplies.map((supply) => (
                      <li key={supply.description} className={supply.isLow ? 'text-fail-300' : 'text-slate-300'}>
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

/** Pure inventory presentation. The page owns all data, state and side effects. */
export function PrinterInventoryTable({
  groups,
  sort,
  onSort,
  notifications,
  networkPolicy,
  dhcpReservations,
  dhcpCheckedIps,
  expanded,
  onToggleExpanded,
  onOpenWebUi,
}: PrinterInventoryTableProps) {
  return groups.map((group) => (
    <div key={group.label || 'all'} className="flex flex-col gap-1">
      {group.label !== '' && (
        <h3 className="px-1 text-xs font-semibold uppercase tracking-wide text-muted">
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
              {headers.map((header) => {
                const isSorted = sort?.key === header;
                return (
                  <th
                    key={header}
                    scope="col"
                    aria-sort={isSorted ? (sort.dir === 'asc' ? 'ascending' : 'descending') : undefined}
                    onClick={() => onSort(header)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        onSort(header);
                      }
                    }}
                    tabIndex={0}
                    className="cursor-pointer select-none border-b border-slate-800 px-3 py-1.5 text-xs font-medium uppercase tracking-wide text-muted hover:text-slate-300"
                  >
                    <span className="inline-flex items-center gap-1">
                      {header}
                      <span aria-hidden className="text-[10px] text-slate-600">
                        {isSorted ? (sort.dir === 'asc' ? '▲' : '▼') : '↕'}
                      </span>
                    </span>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {group.printers.map((printer) => (
              <PrinterRow
                key={printer.key}
                printer={printer}
                notification={printer.deviceAddress ? notifications[printer.deviceAddress.toUpperCase()] : undefined}
                policy={networkPolicy}
                reservation={printer.deviceIp ? dhcpReservations[printer.deviceIp] : undefined}
                dhcpChecked={printer.deviceIp ? dhcpCheckedIps.has(printer.deviceIp) : false}
                expanded={expanded.has(printer.key)}
                onToggle={() => onToggleExpanded(printer.key)}
                onOpenWebUi={onOpenWebUi}
              />
            ))}
          </tbody>
        </table>
      </div>
    </div>
  ));
}
