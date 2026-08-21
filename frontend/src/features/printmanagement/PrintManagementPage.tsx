import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  NetworkPolicyResult,
  PrintHint,
  PrintHintsResult,
  TargetRequest,
} from '../../shared/api-types';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';
import { LeaseSwapHistoryCard } from './LeaseSwapHistoryCard';
import { PrintCsvExportCard } from './PrintCsvExportCard';
import { PrinterInventoryTable } from './PrinterInventoryTable';
import { PrintServerManagerCard } from './PrintServerManagerCard';
import { UnusedPrinterPortsCard } from './UnusedPrinterPortsCard';
import {
  mergePrinters,
  type PrinterGroupMode,
} from './printers';
import {
  buildPrinterInventoryView,
  type PrinterInventorySort,
  type PrinterInventorySortKey,
} from './printerInventoryView';
import { usePrinterDhcpCheck } from './usePrinterDhcpCheck';
import { usePrinterNotificationChecks } from './usePrinterNotificationChecks';
import { usePrintServerWorkspace } from './usePrintServerWorkspace';

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

export function PrintManagementPage() {
  const targets = useTargetsOptional();
  const adminCredentials = targets?.adminCredentials ?? null;
  const savedTargets = targets?.savedTargets ?? [];
  const [serverFilter, setServerFilter] = useState('');
  const [search, setSearch] = useState('');
  const [groupMode, setGroupMode] = useState<PrinterGroupMode>('none');
  const [printerSort, setPrinterSort] = useState<PrinterInventorySort | null>(null);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const {
    notifications,
    checking: notifChecking,
    password: ccrxPassword,
    setPassword: setCcrxPassword,
    check: checkNotifications,
  } = usePrinterNotificationChecks();
  const [hints, setHints] = useState<PrintHint[]>([]);
  const [exportOpen, setExportOpen] = useState(false);
  const [networkPolicy, setNetworkPolicy] = useState<NetworkPolicyResult | null>(null);

  const loadHints = useCallback(() => {
    invoke<PrintHintsResult>('printmanagement', 'getHints', {})
      .then((result) => setHints(result.hints))
      .catch(() => setHints([]));
  }, []);

  useEffect(() => {
    invoke<NetworkPolicyResult>('printmanagement', 'getNetworkPolicy', {})
      .then((policy) => setNetworkPolicy(policy))
      .catch(() => {});
  }, []);

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

  const {
    newServer,
    setNewServer,
    snapshots,
    scanStates,
    scanning,
    restoring,
    servers,
    managedServers,
    scanServers,
    addServer,
    removeServer,
    hasFailedScans,
  } = usePrintServerWorkspace({
    savedTargets,
    toServerRequest,
    onRefreshHints: loadHints,
    saveTarget: targets?.saveTarget,
    deleteTarget: targets?.deleteTarget,
  });

  const {
    server: dhcpServer,
    setServer: setDhcpServer,
    result: dhcpResult,
    checking: dhcpChecking,
    error: dhcpError,
    checkedIps: dhcpCheckedIps,
    reservations: dhcpReservations,
    check: checkDhcp,
  } = usePrinterDhcpCheck({
    configuredServer: networkPolicy?.dhcpServer ?? null,
    toServerRequest,
  });

  const openWebUi = useCallback((address: string) => {
    invoke('printmanagement', 'openDeviceWebUi', { address }).catch(() => {});
  }, []);

  const visibleSnapshots = servers
    .filter((server) => serverFilter === '' || server === serverFilter)
    .map((server) => snapshots[server]);
  const serverEntries = visibleSnapshots.flatMap((snapshot) =>
    snapshot.printers.map((entry) => ({ server: snapshot.server, entry })));
  const printers = mergePrinters(serverEntries);
  const unusedPorts = visibleSnapshots.flatMap((snapshot) =>
    (snapshot.unusedPorts ?? []).map((port) => ({ server: snapshot.server, port })));
  const inventoryView = buildPrinterInventoryView({
    printers,
    search,
    groupMode,
    sort: printerSort,
    notifications,
    networkPolicy,
    dhcpReservations,
    dhcpCheckedIps,
  });
  const { filteredPrinters, groups: printerGroups } = inventoryView;
  const {
    queueCount,
    deviceCount,
    lowTonerCount,
    unreachableCount,
    legacyNetCount,
    dhcpEligibleCount,
    dhcpCheckedCount,
    unreservedCount,
  } = inventoryView.metrics;

  const togglePrinterSort = (key: PrinterInventorySortKey) =>
    setPrinterSort((current) =>
      current && current.key === key
        ? { key, dir: current.dir === 'asc' ? 'desc' : 'asc' }
        : { key, dir: 'asc' });

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

      <PrintCsvExportCard
        open={exportOpen && printers.length > 0}
        printers={filteredPrinters}
        onClose={() => setExportOpen(false)}
      />

      <PrintServerManagerCard
        newServer={newServer}
        managedServers={managedServers}
        snapshots={snapshots}
        scanStates={scanStates}
        scanning={scanning}
        restoring={restoring}
        onNewServerChange={setNewServer}
        onAddServer={addServer}
        onScanServer={(server) => scanServers([server])}
        onRemoveServer={removeServer}
      />

      {scanning && <Spinner label="Scanning print servers" />}
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
              <span className="text-xs text-muted">
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
              label="Legacy subnet"
              value={legacyNetCount}
              tone={legacyNetCount > 0 ? 'warning' : 'success'}
            />
            {dhcpCheckedCount > 0 && (
              <SummaryMetric
                label="Without reservation (checked)"
                value={unreservedCount}
                tone={unreservedCount > 0 ? 'danger' : 'success'}
              />
            )}
            <SummaryMetric
              label="Unused ports"
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

          <UnusedPrinterPortsCard
            ports={unusedPorts}
            adminAvailable={adminCredentials !== null}
            toServerRequest={toServerRequest}
            onRefreshServers={scanServers}
          />

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
                <span className="text-xs text-muted">
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
                  DHCP server
                  <Input
                    type="text"
                    value={dhcpServer}
                    onChange={(event) => setDhcpServer(event.target.value)}
                    placeholder="e.g. dc01"
                    aria-label="DHCP server"
                    disabled={dhcpChecking}
                    className="w-52"
                  />
                </label>
                <Button
                  onClick={() => checkDhcp(filteredPrinters)}
                  disabled={dhcpChecking || filteredPrinters.every((printer) => !printer.deviceIp)}
                  title="Check whether each printer with an IP address has a reservation on the DHCP server"
                >
                  {dhcpChecking ? 'Checking…' : 'Check DHCP reservations'}
                </Button>
                {dhcpError && <span className="text-xs text-fail-400">{dhcpError}</span>}
                {dhcpResult && dhcpCheckedCount > 0 && !dhcpError && (
                  <span className="text-xs text-muted">
                    Checked on {dhcpResult.server} ·{' '}
                    <span>
                      {unreservedCount === 0
                        ? `${dhcpCheckedCount} of ${dhcpEligibleCount} printer IP${dhcpEligibleCount === 1 ? '' : 's'} checked; all checked printers have reservations.`
                        : `${dhcpCheckedCount} of ${dhcpEligibleCount} printer IP${dhcpEligibleCount === 1 ? '' : 's'} checked; ${unreservedCount} without a reservation.`}
                    </span>
                  </span>
                )}
              </div>

              {filteredPrinters.length === 0 ? (
                <p className="text-sm text-slate-400">No printers match the search.</p>
              ) : (
                <PrinterInventoryTable
                  groups={printerGroups}
                  sort={printerSort}
                  onSort={togglePrinterSort}
                  notifications={notifications}
                  networkPolicy={networkPolicy}
                  dhcpReservations={dhcpReservations}
                  dhcpCheckedIps={dhcpCheckedIps}
                  expanded={expanded}
                  onToggleExpanded={toggleExpanded}
                  onOpenWebUi={openWebUi}
                />
              )}
            </div>
          </Card>

          <LeaseSwapHistoryCard servers={servers} />
        </>
      )}

      {!restoring && managedServers.length > 0 && printers.length === 0 && !scanning && !hasFailedScans && (
        <EmptyState
          title="No printers captured yet"
          message="No saved scan currently contains printer queues. Rescan a server above; captured printers will appear here automatically."
        />
      )}
    </div>
  );
}
