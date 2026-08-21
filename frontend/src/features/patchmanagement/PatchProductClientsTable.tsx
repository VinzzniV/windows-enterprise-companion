import { useEffect, useState } from 'react';
import type {
  PatchClientListItem,
  PatchClientStatePage,
  PatchDashboardOverview,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { Button } from '../../shared/ui/Button';
import { DataTable, type DataTableSort } from '../../shared/ui/DataTable';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { ErrorState } from '../../shared/ui/States';
import {
  PatchClientName as ClientName,
  PatchWorkflowBadge as WorkflowBadge,
  formatClientName,
} from './PatchClientFleetCard';
import { presentOpsiError } from './patchErrors';

export type ClientDrillFilter = 'installed' | 'UPDATE_AVAILABLE' | 'FAILED' | 'ROLLOUT_REQUESTED';

const drillFilterLabels: Record<ClientDrillFilter, string> = {
  installed: 'Installed',
  UPDATE_AVAILABLE: 'Outdated',
  FAILED: 'Failed',
  ROLLOUT_REQUESTED: 'Pending',
};

interface PatchProductClientsTableProps {
  connected: boolean;
  dashboard: PatchDashboardOverview;
  productId: string;
  filter: ClientDrillFilter | null;
  selectedClients: ReadonlySet<string>;
  onClearFilter: () => void;
  onToggleClient: (clientId: string) => void;
}

function PatchProductClientsTableState({
  connected,
  dashboard,
  productId,
  filter,
  selectedClients,
  onClearFilter,
  onToggleClient,
}: PatchProductClientsTableProps) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [sort, setSort] = useState<DataTableSort>({ column: 'client', direction: 'asc' });
  const [clients, setClients] = useState<PatchClientStatePage | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ReturnType<typeof presentOpsiError> | null>(null);
  const [revision, setRevision] = useState(0);

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
      productId,
      clientSearch: null,
      productSearch: null,
      state: filter === 'installed' ? null : filter,
      installationStatus: filter === 'installed' ? 'installed' : null,
      page,
      pageSize,
      sortColumn: sort.column,
      sortDirection: sort.direction,
    })
      .then((result) => { if (!ignore) setClients(result); })
      .catch((requestError: unknown) => {
        if (!ignore) {
          setClients(null);
          setError(presentOpsiError(requestError, 'The package client details could not be loaded.'));
        }
      })
      .finally(() => { if (!ignore) setLoading(false); });
    return () => { ignore = true; };
  }, [connected, dashboard.depotFilter, filter, page, pageSize, productId, revision, sort]);

  return (
    <>
      {filter && (
        <div className="flex items-center gap-2 text-sm">
          <span className="text-slate-400">Showing:</span>
          <StatusBadge variant="info">{drillFilterLabels[filter]}</StatusBadge>
          <Button variant="ghost" onClick={onClearFilter}>Show all clients</Button>
        </div>
      )}

      {!connected && (
        <p className="text-sm text-slate-400">
          Client details require a live connection. The saved package data remains readable.
        </p>
      )}
      {error && (
        <ErrorState
          title="Package clients unavailable"
          {...error}
          controls={(
            <Button onClick={() => setRevision((value) => value + 1)}>
              Reload package clients
            </Button>
          )}
        />
      )}
      <DataTable
        columns={[
          {
            header: 'Selection',
            cell: (row: PatchClientListItem) => (
              <input
                type="checkbox"
                aria-label={`Select client ${formatClientName(row.client.clientId)}`}
                checked={selectedClients.has(row.client.clientId)}
                onChange={() => onToggleClient(row.client.clientId)}
              />
            ),
          },
          {
            id: 'client',
            header: 'Client',
            sortable: true,
            cell: (row: PatchClientListItem) => <ClientName clientId={row.client.clientId} />,
          },
          {
            id: 'depot',
            header: 'Depot',
            sortable: true,
            cell: (row: PatchClientListItem) => row.client.depotId ?? '—',
          },
          {
            id: 'installed',
            header: 'Installed',
            sortable: true,
            cell: (row: PatchClientListItem) => row.client.installedVersion ?? '—',
          },
          {
            id: 'target',
            header: 'Target version',
            sortable: true,
            cell: (row: PatchClientListItem) => row.client.targetVersion ?? '—',
          },
          {
            id: 'status',
            header: 'Status',
            sortable: true,
            cell: (row: PatchClientListItem) => <WorkflowBadge state={row.client.state} />,
          },
        ]}
        rows={clients?.items ?? []}
        getRowKey={(row) => `${row.productId}-${row.client.clientId}`}
        emptyMessage={connected
          ? 'No states are available for this package on the filtered clients.'
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
    </>
  );
}

/**
 * Owns the read-only package-client query/table state. Selection remains controlled
 * because it also drives the deployment preview in the parent workflow.
 */
export function PatchProductClientsTable(props: PatchProductClientsTableProps) {
  const scopeKey = `${props.dashboard.generatedAtUtc}\u0000${props.productId}\u0000${props.filter ?? 'all'}`;
  return <PatchProductClientsTableState key={scopeKey} {...props} />;
}
