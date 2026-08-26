import { useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  PatchClientListItem,
  PatchClientStatePage,
  PatchDashboardOverview,
  PatchWorkflowState,
} from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataTableSort } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { SemanticStatusBadge, semanticStatusPresentation } from '../../shared/ui/SemanticStatusBadge';
import { ErrorState } from '../../shared/ui/States';
import { Toolbar } from '../../shared/ui/Toolbar';
import { presentOpsiError } from './patchErrors';
import { patchWorkflowStatus } from './patchStatus';

const workflowStateOptions: readonly PatchWorkflowState[] = [
  'DETECTED',
  'UPDATE_AVAILABLE',
  'ACTION_PENDING',
  'COMPLETED',
  'FAILED',
];

const clientDnsSuffix = '.kauth.local';

/** Shortens client FQDNs for display without changing their opsi identifier. */
export function formatClientName(clientId: string): string {
  return clientId.toLocaleLowerCase().endsWith(clientDnsSuffix)
    ? clientId.slice(0, -clientDnsSuffix.length)
    : clientId;
}

export function PatchClientName({ clientId }: { clientId: string }) {
  const displayName = formatClientName(clientId);
  return <span title={displayName === clientId ? undefined : clientId}>{displayName}</span>;
}

export function PatchWorkflowBadge({ state }: { state: PatchWorkflowState }) {
  const presentation = patchWorkflowStatus(state);
  return (
    <span
      className="inline-flex flex-wrap items-center gap-1.5"
      title={presentation.technicalDetail ?? undefined}
    >
      <SemanticStatusBadge status={presentation.status} />
      {presentation.context && (
        <span className="text-xs text-slate-400">{presentation.context}</span>
      )}
    </span>
  );
}

export function PatchClientFleetCard({
  connected,
  dashboard,
}: {
  connected: boolean;
  dashboard: PatchDashboardOverview;
}) {
  const [clientSearch, setClientSearch] = useState('');
  const [productSearch, setProductSearch] = useState('');
  const [clientState, setClientState] = useState<PatchWorkflowState | ''>('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [sort, setSort] = useState<DataTableSort>({ column: 'client', direction: 'asc' });
  const [clients, setClients] = useState<PatchClientStatePage | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ReturnType<typeof presentOpsiError> | null>(null);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    setPage(1);
    setClients(null);
  }, [dashboard]);

  useEffect(() => {
    if (!connected) {
      setClients(null);
      setLoading(false);
      setError(null);
      return;
    }

    let ignore = false;
    setLoading(true);
    setError(null);
    invoke<PatchClientStatePage>('patchmanagement', 'listClientStates', {
      depotFilter: dashboard.depotFilter,
      productId: null,
      clientSearch: clientSearch.trim() || null,
      productSearch: productSearch.trim() || null,
      state: clientState || null,
      installationStatus: null,
      page,
      pageSize,
      sortColumn: sort.column,
      sortDirection: sort.direction,
    })
      .then((result) => { if (!ignore) setClients(result); })
      .catch((requestError: unknown) => {
        if (!ignore) {
          setClients(null);
          setError(presentOpsiError(requestError, 'The patch client list could not be loaded.'));
        }
      })
      .finally(() => { if (!ignore) setLoading(false); });
    return () => { ignore = true; };
  }, [clientSearch, clientState, connected, dashboard, page, pageSize, productSearch, revision, sort]);

  return (
    <Card title="Client and deployment status">
      <p className="mb-3 text-xs text-muted">
        {dashboard.summary.clientCount.toLocaleString('en-US')} unique clients ·{' '}
        {(clients?.total ?? 0).toLocaleString('en-US')} of{' '}
        {(clients?.snapshotTotal ?? 0).toLocaleString('en-US')}{' '}
        {clients?.snapshotTotal === 1 ? 'package state' : 'package states'}
      </p>
      <div className="mb-3">
        <Toolbar>
          <Input
            type="search"
            value={clientSearch}
            onChange={(event) => { setClientSearch(event.target.value); setPage(1); }}
            placeholder="Client, depot, or version…"
            aria-label="Filter patch clients"
            className="w-60"
          />
          <Input
            type="search"
            value={productSearch}
            onChange={(event) => { setProductSearch(event.target.value); setPage(1); }}
            placeholder="Filter packages…"
            aria-label="Filter patch packages"
            className="w-52"
          />
          <Select
            fullWidth={false}
            value={clientState}
            onChange={(event) => {
              setClientState(event.target.value as PatchWorkflowState | '');
              setPage(1);
            }}
            aria-label="Filter patch clients by status"
          >
            <option value="">All statuses</option>
            {workflowStateOptions.map((value) => {
              const presentation = patchWorkflowStatus(value);
              return (
                <option key={value} value={value}>
                  {presentation.context ?? semanticStatusPresentation(presentation.status).label}
                </option>
              );
            })}
          </Select>
        </Toolbar>
      </div>
      {!connected && (
        <p className="mb-3 text-sm text-slate-400">
          Client details require a live connection. The saved package overview remains readable.
        </p>
      )}
      {error && (
        <div className="mb-3">
          <ErrorState
            title="Patch clients unavailable"
            {...error}
            controls={(
              <Button onClick={() => setRevision((value) => value + 1)}>
                Reload client list
              </Button>
            )}
          />
        </div>
      )}
      <DataTable
        columns={[
          {
            id: 'client',
            header: 'Client',
            sortable: true,
            cell: (row: PatchClientListItem) => <PatchClientName clientId={row.client.clientId} />,
          },
          { id: 'product', header: 'Package', sortable: true, cell: (row: PatchClientListItem) => row.productName ?? row.productId },
          { id: 'depot', header: 'Depot', sortable: true, cell: (row: PatchClientListItem) => row.client.depotId ?? '—' },
          { id: 'installed', header: 'Installed', sortable: true, mono: true, cell: (row: PatchClientListItem) => row.client.installedVersion ?? '—' },
          { id: 'target', header: 'Target version', sortable: true, mono: true, cell: (row: PatchClientListItem) => row.client.targetVersion ?? '—' },
          { id: 'status', header: 'Status', sortable: true, cell: (row: PatchClientListItem) => <PatchWorkflowBadge state={row.client.state} /> },
        ]}
        rows={clients?.items ?? []}
        getRowKey={(row) => `${row.productId}-${row.client.clientId}`}
        emptyMessage={connected
          ? 'opsi currently reports no client states for the selected view.'
          : 'Client details are unavailable in the saved offline view.'}
        loading={loading}
        sort={sort}
        onSortChange={(value) => { setSort(value); setPage(1); }}
        pagination={{
          page,
          pageSize,
          total: clients?.total ?? 0,
          onPageChange: setPage,
          onPageSizeChange: (value) => { setPageSize(value); setPage(1); },
        }}
      />
    </Card>
  );
}
