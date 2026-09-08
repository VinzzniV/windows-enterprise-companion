import { useState } from 'react';
import type {
  DeleteUnusedPortsResult,
  PortRemovalResult,
  ProbeHostsResult,
  TargetRequest,
  UnusedPort,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import { Button } from '../../shared/ui/Button';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { ContextualPrintStatus } from './PrintStatusView';
import { portReachabilityStatus } from './printStatus';

export interface UnusedPrinterPortRow {
  server: string;
  port: UnusedPort;
}

interface UnusedPrinterPortsCardProps {
  ports: readonly UnusedPrinterPortRow[];
  adminAvailable: boolean;
  toServerRequest: (server: string) => TargetRequest;
  onRefreshServers: (servers: string[]) => void;
}

const portKey = (server: string, name: string) => `${server}\0${name}`;

export function UnusedPrinterPortsCard({
  ports,
  adminAvailable,
  toServerRequest,
  onRefreshServers,
}: UnusedPrinterPortsCardProps) {
  const [selectedPorts, setSelectedPorts] = useState<ReadonlySet<string>>(new Set());
  const [deleting, setDeleting] = useState(false);
  const [deleteMessage, setDeleteMessage] = useState<string | null>(null);
  const [reachability, setReachability] = useState<Record<string, boolean>>({});
  const [checkingReachability, setCheckingReachability] = useState(false);

  if (ports.length === 0) return null;

  const selectedRows = ports.filter((row) => selectedPorts.has(portKey(row.server, row.port.name)));
  const allSelected = selectedRows.length === ports.length;

  const togglePort = (server: string, name: string) => {
    setSelectedPorts((current) => {
      const next = new Set(current);
      const key = portKey(server, name);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const toggleAll = () => {
    setSelectedPorts(
      allSelected
        ? new Set()
        : new Set(ports.map((row) => portKey(row.server, row.port.name))),
    );
  };

  const checkReachability = () => {
    const hosts = [...new Set(
      ports.map((row) => row.port.hostAddress).filter((host): host is string => !!host),
    )];
    if (hosts.length === 0) return;
    setCheckingReachability(true);
    invoke<ProbeHostsResult>('connectivity', 'probeHosts', { hosts }, 120_000)
      .then((result) => {
        setReachability((current) => ({
          ...current,
          ...Object.fromEntries(result.results.map((probe) => [
            probe.host.toUpperCase(),
            probe.reachable,
          ])),
        }));
      })
      .catch(() => {})
      .finally(() => setCheckingReachability(false));
  };

  const deleteSelected = () => {
    if (selectedRows.length === 0) return;
    const byServer = new Map<string, string[]>();
    for (const row of selectedRows) {
      const names = byServer.get(row.server) ?? [];
      names.push(row.port.name);
      byServer.set(row.server, names);
    }
    const portCount = selectedRows.length;
    const serverCount = byServer.size;
    const confirmed = window.confirm(
      `Permanently delete ${portCount} unused port${portCount === 1 ? '' : 's'} on ${serverCount} server${serverCount === 1 ? '' : 's'}? `
        + 'Ports still used by a printer will be refused by the server.',
    );
    if (!confirmed) return;

    setDeleting(true);
    setDeleteMessage(null);
    void Promise.all(
      [...byServer.entries()].map(([server, portNames]) => invoke<DeleteUnusedPortsResult>(
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
          error: errorText(error),
        }))),
    )
      .then((outcomes) => {
        const removed = outcomes.reduce(
          (sum, outcome) => sum
            + (outcome.results?.filter((result) => result.removed).length ?? 0),
          0,
        );
        const refused = outcomes.flatMap(
          (outcome) => outcome.results?.filter((result) => !result.removed) ?? [],
        );
        const serverErrors = outcomes.filter((outcome) => outcome.error);
        const parts = [`${removed} port${removed === 1 ? '' : 's'} removed`];
        if (refused.length > 0) {
          parts.push(`${refused.length} refused (${refused
            .map((result) => result.error ?? result.name).join('; ')})`);
        }
        if (serverErrors.length > 0) {
          parts.push(`${serverErrors.length} server error${serverErrors.length === 1 ? '' : 's'} (${serverErrors
            .map((outcome) => outcome.error).join('; ')})`);
        }
        setDeleteMessage(parts.join(' · '));
        setSelectedPorts(new Set());
        onRefreshServers([...byServer.keys()]);
      })
      .finally(() => setDeleting(false));
  };

  return (
    <DetailsDisclosure summary={`Unused ports (${ports.length})`}>
      <p className="mb-2 text-sm text-slate-400">
        TCP/IP ports that are no longer used by any printer — remnants of removed printers.
        Selecting and deleting removes them from the print server as the signed-in admin; the
        server refuses ports that are still in use.
      </p>
      {!adminAvailable && (
        <p className="mb-2 text-xs text-elevation-400">
          Set remote account at the top right before deleting; otherwise the server permissions
          are unavailable.
        </p>
      )}
      <div className="mb-2 flex flex-wrap items-center gap-3">
        <Button
          onClick={deleteSelected}
          disabled={deleting || selectedRows.length === 0}
          title="Delete the selected ports from their print servers"
        >
          {deleting ? 'Deleting…' : `Delete selected (${selectedRows.length})`}
        </Button>
        <Button variant="ghost" onClick={toggleAll} disabled={deleting}>
          {allSelected ? 'Clear selection' : 'Select all'}
        </Button>
        <Button
          variant="ghost"
          onClick={checkReachability}
          disabled={checkingReachability}
          title="Ping each port address from the WEC machine. No response can indicate a device that has already been removed."
        >
          {checkingReachability ? 'Checking…' : 'Check reachability'}
        </Button>
        {deleteMessage && <span className="text-xs text-slate-400">{deleteMessage}</span>}
      </div>
      <DataTable
        columns={[
          {
            header: '',
            cell: (row: UnusedPrinterPortRow) => (
              <input
                type="checkbox"
                checked={selectedPorts.has(portKey(row.server, row.port.name))}
                onChange={() => togglePort(row.server, row.port.name)}
                className="h-4 w-4 accent-accent-500"
                aria-label={`Select port ${row.port.name}`}
              />
            ),
          },
          { header: 'Print server', cell: (row: UnusedPrinterPortRow) => row.server },
          { header: 'Port', mono: true, cell: (row: UnusedPrinterPortRow) => row.port.name },
          {
            header: 'Address',
            mono: true,
            cell: (row: UnusedPrinterPortRow) => row.port.hostAddress ?? '—',
          },
          {
            header: 'Reachability',
            cell: (row: UnusedPrinterPortRow) => {
              const reachable = row.port.hostAddress
                ? reachability[row.port.hostAddress.toUpperCase()]
                : undefined;
              if (reachable === undefined) return <span className="text-slate-600">—</span>;
              return <ContextualPrintStatus presentation={portReachabilityStatus(reachable)} />;
            },
          },
        ]}
        rows={ports}
        getRowKey={(row) => portKey(row.server, row.port.name)}
        emptyMessage="No unused ports."
      />
    </DetailsDisclosure>
  );
}
