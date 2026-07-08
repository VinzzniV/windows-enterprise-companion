import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AdComputerSearchResult,
  ListInventoryHostsResult,
  StoredInventoryHost,
} from '../../shared/api-types';
import { useTargets } from '../../shared/targets/TargetContext';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Toolbar } from '../../shared/ui/Toolbar';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { Button } from '../../shared/ui/Button';
import { Badge } from '../../shared/ui/Badge';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { Spinner } from '../../shared/ui/Spinner';
import {
  buildClientList,
  filterClients,
  groupClients,
  type ClientEntry,
  type GroupMode,
} from './clients';

function errorText(error: unknown): string {
  if (error instanceof BridgeInvokeError) {
    return `${error.error.code}: ${error.error.message}`;
  }
  return error instanceof Error ? error.message : String(error);
}

function StatusCell({ client }: { client: ClientEntry }) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {client.scanned ? (
        <Badge tone="ok">Scanned</Badge>
      ) : (
        <Badge tone="neutral">Not scanned</Badge>
      )}
      {client.saved && <Badge tone="accent">Saved</Badge>}
      {!client.enabled && <Badge tone="warn">Disabled</Badge>}
      {client.scanned && client.capturedAtUtc && (
        <span className="text-xs text-slate-500">
          {new Date(client.capturedAtUtc).toLocaleString()}
        </span>
      )}
    </div>
  );
}

export function ClientsPage() {
  const navigate = useNavigate();
  const { savedTargets } = useTargets();

  const [adResult, setAdResult] = useState<AdComputerSearchResult | null>(null);
  const [scannedHosts, setScannedHosts] = useState<StoredInventoryHost[]>([]);
  const [adFilter, setAdFilter] = useState('');
  const [includeDisabled, setIncludeDisabled] = useState(false);
  const [searching, setSearching] = useState(false);
  const [adError, setAdError] = useState<string | null>(null);

  const [search, setSearch] = useState('');
  const [groupMode, setGroupMode] = useState<GroupMode>('none');
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());

  const searchAd = useCallback((filter: string, disabled: boolean) => {
    setSearching(true);
    setAdError(null);
    invoke<AdComputerSearchResult>(
      'activedirectory',
      'searchComputers',
      { nameFilter: filter.trim() || null, includeDisabled: disabled },
      120_000,
    )
      .then(setAdResult)
      .catch((error: unknown) => setAdError(errorText(error)))
      .finally(() => setSearching(false));
  }, []);

  const reloadScanned = useCallback(() => {
    invoke<ListInventoryHostsResult>('inventory', 'listHosts')
      .then((result) => setScannedHosts(result.hosts))
      .catch(() => setScannedHosts([]));
  }, []);

  useEffect(() => {
    searchAd('', false);
    reloadScanned();
  }, [searchAd, reloadScanned]);

  const savedClients = useMemo(
    () => savedTargets.filter((target) => target.role === 'Client'),
    [savedTargets],
  );
  const clients = useMemo(
    () => buildClientList(adResult?.computers ?? [], scannedHosts, savedClients),
    [adResult, scannedHosts, savedClients],
  );
  const filtered = useMemo(() => filterClients(clients, search), [clients, search]);
  const groups = useMemo(() => groupClients(filtered, groupMode), [filtered, groupMode]);
  const scannedCount = filtered.filter((client) => client.scanned).length;

  const openClient = (client: ClientEntry) =>
    navigate(`/clients/${encodeURIComponent(client.host)}`);

  const toggleGroup = (label: string) =>
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(label)) next.delete(label);
      else next.add(label);
      return next;
    });

  const columns: DataColumn<ClientEntry>[] = [
    {
      header: 'Client',
      cell: (client) => (
        <div className="flex flex-col">
          <span className="font-medium text-slate-100">{client.name}</span>
          {client.host !== client.name && (
            <span className="font-mono text-xs text-slate-500">{client.host}</span>
          )}
        </div>
      ),
    },
    { header: 'Operating system', cell: (client) => client.os ?? '—' },
    { header: 'Status', cell: (client) => <StatusCell client={client} /> },
  ];

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Clients" subtitle="Pick a client to scan it on demand — nothing runs until you open it">
        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={() => navigate('/clients/compare')}>
            Compare
          </Button>
          <Button variant="secondary" onClick={() => { searchAd(adFilter, includeDisabled); reloadScanned(); }} disabled={searching}>
            {searching ? 'Refreshing…' : 'Refresh'}
          </Button>
        </div>
      </PageHeader>

      <Toolbar
        actions={
          <>
            <Input
              type="text"
              value={adFilter}
              onChange={(event) => setAdFilter(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  searchAd(adFilter, includeDisabled);
                }
              }}
              placeholder="AD name filter (empty = all)"
              aria-label="Active Directory name filter"
              disabled={searching}
              className="w-56"
            />
            <label className="flex cursor-pointer items-center gap-1.5 text-xs text-slate-400">
              <input
                type="checkbox"
                className="accent-accent-500"
                checked={includeDisabled}
                onChange={(event) => setIncludeDisabled(event.target.checked)}
                disabled={searching}
              />
              Include disabled
            </label>
            <Button variant="secondary" onClick={() => searchAd(adFilter, includeDisabled)} disabled={searching}>
              Search AD
            </Button>
          </>
        }
      >
        <Input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Filter clients by name, host or OS"
          aria-label="Filter clients"
          className="w-64"
        />
        <label className="flex items-center gap-2 text-sm text-slate-400">
          Group by
          <Select
            fullWidth={false}
            value={groupMode}
            onChange={(event) => setGroupMode(event.target.value as GroupMode)}
            aria-label="Group clients by"
          >
            <option value="none">None</option>
            <option value="os">Operating system</option>
            <option value="site">Site</option>
          </Select>
        </label>
      </Toolbar>

      <p className="text-sm text-slate-400">
        {filtered.length} client{filtered.length === 1 ? '' : 's'} · {scannedCount} scanned
        {adResult?.domainJoined === false && ' · not domain-joined (showing scanned and saved only)'}
        {adResult?.truncated && ' · AD list truncated, refine the filter'}
      </p>

      {adError && (
        <ErrorState
          title="Active Directory search failed"
          message={adError}
          hint="Clients already scanned or saved are still listed below."
        />
      )}

      {adResult === null && adError === null ? (
        // Wait for the first AD search before drawing the table, so the view does
        // not flash the scanned-only rows (fast) and then swap to the full AD list.
        <Spinner label="Loading clients from Active Directory …" />
      ) : filtered.length === 0 ? (
        <EmptyState
          title="No clients"
          message="No clients matched. Search Active Directory, adjust the filter, or scan a host to add it here."
        />
      ) : groupMode === 'none' ? (
        <DataTable
          columns={columns}
          rows={filtered}
          emptyMessage="No clients."
          getRowKey={(client) => client.key}
          onRowClick={openClient}
          stickyHeader
        />
      ) : (
        <div className="flex flex-col gap-3">
          {groups.map((group) => {
            const isCollapsed = collapsed.has(group.label);
            return (
              <div key={group.label} className="rounded-lg border border-slate-800">
                <button
                  type="button"
                  onClick={() => toggleGroup(group.label)}
                  aria-expanded={!isCollapsed}
                  className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm font-medium text-slate-200 hover:bg-slate-800/40"
                >
                  <span className={`text-slate-500 transition-transform ${isCollapsed ? '' : 'rotate-90'}`}>
                    ›
                  </span>
                  {group.label}
                  <span className="text-xs font-normal text-slate-500">
                    {group.clients.length} client{group.clients.length === 1 ? '' : 's'}
                  </span>
                </button>
                {!isCollapsed && (
                  <div className="border-t border-slate-800 px-1 pb-1">
                    <DataTable
                      columns={columns}
                      rows={group.clients}
                      emptyMessage="No clients."
                      getRowKey={(client) => client.key}
                      onRowClick={openClient}
                    />
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
