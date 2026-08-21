import type {
  DhcpReservationInfo,
  NetworkPolicyResult,
  PrinterNotificationCheck,
} from '../../shared/api-types';
import { compareSortKeys, type SortKey } from '../../shared/sort';
import {
  classifySubnet,
  displayedLocation,
  filterPrinters,
  groupPrinters,
  hasLowToner,
  lowestTonerPercent,
  type MergedPrinter,
  type PrinterGroup,
  type PrinterGroupMode,
  type SubnetClass,
} from './printers';

export type PrinterInventorySortKey =
  | 'Printer'
  | 'Model'
  | 'Serial'
  | 'Location'
  | 'Status'
  | 'Toner'
  | 'Notify'
  | 'IP'
  | 'IP / web UI'
  | 'Network';

export interface PrinterInventorySort {
  key: PrinterInventorySortKey;
  dir: 'asc' | 'desc';
}

export interface PrinterInventoryMetrics {
  queueCount: number;
  deviceCount: number;
  lowTonerCount: number;
  unreachableCount: number;
  legacyNetCount: number;
  dhcpEligibleCount: number;
  dhcpCheckedCount: number;
  unreservedCount: number;
}

export interface PrinterInventoryView {
  filteredPrinters: MergedPrinter[];
  groups: PrinterGroup[];
  metrics: PrinterInventoryMetrics;
}

interface BuildPrinterInventoryViewInput {
  printers: readonly MergedPrinter[];
  search: string;
  groupMode: PrinterGroupMode;
  sort: PrinterInventorySort | null;
  notifications: Readonly<Record<string, PrinterNotificationCheck>>;
  networkPolicy: NetworkPolicyResult | null;
  dhcpReservations: Readonly<Record<string, DhcpReservationInfo>>;
  dhcpCheckedIps: ReadonlySet<string>;
}

const subnetRank: Record<SubnetClass, number> = {
  target: 0,
  legacy: 1,
  foreign: 2,
  unknown: 3,
};

function sortKey(
  header: PrinterInventorySortKey,
  printer: MergedPrinter,
  input: Pick<BuildPrinterInventoryViewInput, 'notifications' | 'networkPolicy'>,
): SortKey {
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
        ? input.notifications[printer.deviceAddress.toUpperCase()]?.status ?? null
        : null;
    case 'IP':
      return printer.deviceIp;
    case 'IP / web UI':
      return printer.deviceAddress;
    case 'Network':
      return input.networkPolicy
        ? subnetRank[classifySubnet(printer.deviceIp, input.networkPolicy)]
        : null;
  }
}

/** Pure visible-inventory projection. Data loading, state and side effects stay in the page owner. */
export function buildPrinterInventoryView(input: BuildPrinterInventoryViewInput): PrinterInventoryView {
  const filteredPrinters = filterPrinters(input.printers, input.search);
  const sort = input.sort;
  const sortedPrinters = sort
    ? [...filteredPrinters].sort((left, right) =>
        compareSortKeys(
          sortKey(sort.key, left, input),
          sortKey(sort.key, right, input),
          sort.dir,
        ))
    : filteredPrinters;
  const networkPolicy = input.networkPolicy;

  return {
    filteredPrinters,
    groups: groupPrinters(sortedPrinters, input.groupMode),
    metrics: {
      queueCount: input.printers.reduce((sum, printer) => sum + printer.queues.length, 0),
      deviceCount: input.printers.filter((printer) => printer.deviceAddress !== null).length,
      lowTonerCount: input.printers.filter((printer) => hasLowToner(printer.supplies)).length,
      unreachableCount: input.printers.filter((printer) => printer.deviceError !== null).length,
      legacyNetCount: networkPolicy
        ? input.printers.filter(
            (printer) => classifySubnet(printer.deviceIp, networkPolicy) === 'legacy',
          ).length
        : 0,
      dhcpEligibleCount: input.printers.filter((printer) => printer.deviceIp).length,
      dhcpCheckedCount: input.printers.filter(
        (printer) => printer.deviceIp && input.dhcpCheckedIps.has(printer.deviceIp),
      ).length,
      unreservedCount: input.printers.filter(
        (printer) =>
          printer.deviceIp &&
          input.dhcpCheckedIps.has(printer.deviceIp) &&
          !input.dhcpReservations[printer.deviceIp],
      ).length,
    },
  };
}
