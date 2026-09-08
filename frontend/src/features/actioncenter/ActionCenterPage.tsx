import { useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  ActionCenterPage as ActionCenterPageResult,
  ActionCenterSeverity,
  ActionCenterSortField,
  ActionCenterWorkItem,
  ActionEvidenceAvailability,
} from '../../shared/api-types';
import {
  BridgeCancelledError,
  invokeCancellable,
  type CancellableBridgeInvocation,
} from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { HygieneLoadStatus } from '../../shared/environment/HygieneLoadStatus';
import { useEnvironmentRequest } from '../../shared/environment/EnvironmentContext';
import { useHygieneOperation } from '../../shared/environment/useHygieneOperation';
import { RelationshipMap } from '../../shared/relationships/RelationshipMap';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { DataTable, type DataColumn, type DataTableSort } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { ErrorState } from '../../shared/ui/States';
import { Toolbar } from '../../shared/ui/Toolbar';
import { actionCenterRelationshipModel } from './actionCenterRelationships';

interface ActionCenterFilters {
  search: string;
  severity: ActionCenterSeverity | 'ALL';
  source: string;
}

const emptyFilters: ActionCenterFilters = { search: '', severity: 'ALL', source: '' };
const sortFieldByColumn: Record<string, ActionCenterSortField> = {
  severity: 'SEVERITY',
  device: 'DEVICE',
  source: 'SOURCE',
  evidenceAge: 'EVIDENCE_AGE',
  problem: 'PROBLEM',
};
const columnBySortField: Record<ActionCenterSortField, string> = {
  SEVERITY: 'severity',
  DEVICE: 'device',
  SOURCE: 'source',
  EVIDENCE_AGE: 'evidenceAge',
  PROBLEM: 'problem',
};
const severityPresentation: Record<ActionCenterSeverity, { label: string; tone: BadgeTone }> = {
  CRITICAL: { label: 'Critical', tone: 'fail' },
  HIGH: { label: 'High', tone: 'fail' },
  WARNING: { label: 'Warning', tone: 'warn' },
  MEDIUM: { label: 'Medium', tone: 'warn' },
  LOW: { label: 'Low', tone: 'info' },
  INFORMATION: { label: 'Information', tone: 'info' },
  UNKNOWN: { label: 'Unknown', tone: 'neutral' },
};
const coveragePresentation: Record<ActionEvidenceAvailability, { label: string; tone: BadgeTone }> = {
  AVAILABLE: { label: 'Available', tone: 'ok' },
  PARTIAL: { label: 'Partial', tone: 'warn' },
  NOT_CONNECTED: { label: 'Not connected', tone: 'neutral' },
  UNAVAILABLE: { label: 'Unavailable', tone: 'fail' },
  TRUNCATED: { label: 'Truncated', tone: 'warn' },
};
const knownSources = ['Active Directory', 'Kaspersky', 'opsi', 'Nessus', 'WEC Inventory', 'WEC Security'];

function evidenceAge(item: ActionCenterWorkItem): string {
  if (item.evidenceAgeDays === null) return 'Unknown age';
  if (item.evidenceAgeDays === 0) return 'Today';
  return `${item.evidenceAgeDays} day${item.evidenceAgeDays === 1 ? '' : 's'}`;
}

function formatTimestamp(value: string | null): string {
  return value ? new Date(value).toLocaleString() : 'No source timestamp';
}

export function ActionCenterPage() {
  const environmentRequest = useEnvironmentRequest();
  const hygieneOperation = useHygieneOperation();
  const [draftFilters, setDraftFilters] = useState<ActionCenterFilters>(emptyFilters);
  const [activeFilters, setActiveFilters] = useState<ActionCenterFilters>(emptyFilters);
  const [sortField, setSortField] = useState<ActionCenterSortField>('SEVERITY');
  const [sortDirection, setSortDirection] = useState<'ASCENDING' | 'DESCENDING'>('ASCENDING');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [refreshRevision, setRefreshRevision] = useState(0);
  const lastForcedRevision = useRef(0);
  const requestId = useRef(0);
  const activeLoad = useRef<CancellableBridgeInvocation<ActionCenterPageResult> | null>(null);
  const [result, setResult] = useState<ActionCenterPageResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [selectedItem, setSelectedItem] = useState<ActionCenterWorkItem | null>(null);

  useEffect(() => {
    const currentRequest = ++requestId.current;
    const force = refreshRevision > lastForcedRevision.current;
    lastForcedRevision.current = refreshRevision;
    const operationId = hygieneOperation.begin();
    setLoading(true);
    setError(null);
    const invocation = invokeCancellable<ActionCenterPageResult>('actioncenter', 'listItems', {
      activeDirectory: environmentRequest.activeDirectory,
      kaspersky: environmentRequest.kaspersky,
      operationId,
      force,
      search: activeFilters.search.trim() || null,
      severity: activeFilters.severity === 'ALL' ? null : activeFilters.severity,
      source: activeFilters.source || null,
      page,
      pageSize,
      sortField,
      sortDirection,
    });
    activeLoad.current = invocation;
    void invocation.promise.then((value) => {
      if (requestId.current === currentRequest) {
        setResult(value);
        setSelectedItem((current) => current && value.items.some((item) => item.id === current.id) ? current : null);
      }
    }).catch((caught: unknown) => {
      if (requestId.current === currentRequest && !(caught instanceof BridgeCancelledError)) {
        setError(presentError(caught, {
          message: 'The Action Center could not be computed.',
          action: 'Review source coverage and credentials, then retry.',
        }));
      }
    }).finally(() => {
      if (requestId.current === currentRequest) {
        activeLoad.current = null;
        hygieneOperation.end();
        setLoading(false);
      }
    });
    return () => invocation.cancel();
  }, [activeFilters, environmentRequest, hygieneOperation.begin, hygieneOperation.end, page, pageSize, refreshRevision, sortDirection, sortField]);

  const columns = useMemo<DataColumn<ActionCenterWorkItem>[]>(() => [
    {
      id: 'severity',
      header: 'Severity',
      sortable: true,
      cell: (item) => {
        const presentation = severityPresentation[item.severity];
        return <Badge tone={presentation.tone}>{presentation.label}</Badge>;
      },
    },
    {
      id: 'device',
      header: 'Affected device',
      sortable: true,
      cell: (item) => <div className="flex flex-col gap-0.5">
        <Link className="font-medium text-accent-300 hover:text-accent-200" to={`/clients/${encodeURIComponent(item.device)}`}>
          {item.device}
        </Link>
        {item.userDisplayName && <span className="text-xs text-muted">User: {item.userDisplayName}</span>}
      </div>,
    },
    {
      id: 'problem',
      header: 'Problem / deviation',
      sortable: true,
      cell: (item) => <div className="min-w-0">
        <div className="font-medium text-slate-100">{item.problem}</div>
        <p className="mt-0.5 text-xs text-slate-400">{item.explanation}</p>
      </div>,
    },
    {
      id: 'source',
      header: 'Source',
      sortable: true,
      cell: (item) => <span className="text-slate-200">{item.source}</span>,
    },
    {
      id: 'evidenceAge',
      header: 'Evidence age',
      sortable: true,
      cell: (item) => {
        const coverage = coveragePresentation[item.coverage];
        return <div className="flex min-w-0 flex-col gap-1">
          <span className="text-xs text-muted" title={formatTimestamp(item.evidenceAtUtc)}>{evidenceAge(item)} old</span>
          <span><Badge tone={coverage.tone}>{coverage.label} · {item.reliability}</Badge></span>
        </div>;
      },
    },
    {
      header: 'Next action',
      cell: (item) => <div className="min-w-0">
        <p className="text-xs text-slate-300">{item.recommendedAction}</p>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <Link className="text-sm font-medium text-accent-300 hover:text-accent-200" to={item.href}>
            Inspect evidence →
          </Link>
          <button type="button" className="text-xs text-slate-400 hover:text-slate-200" onClick={() => setSelectedItem(item)}>
            Show context
          </button>
        </div>
      </div>,
    },
  ], []);

  const applyFilters = () => {
    setPage(1);
    setActiveFilters({ ...draftFilters });
  };
  const tableSort: DataTableSort = {
    column: columnBySortField[sortField],
    direction: sortDirection === 'ASCENDING' ? 'asc' : 'desc',
  };
  const changeSort = (next: DataTableSort) => {
    const nextField = sortFieldByColumn[next.column];
    if (!nextField) return;
    setPage(1);
    setSortField(nextField);
    setSortDirection(next.direction === 'asc' ? 'ASCENDING' : 'DESCENDING');
  };

  return <div className="flex flex-col gap-4">
    <PageHeader title="Action Center" subtitle="Computed read-only work list from current source evidence">
      <Badge tone="info">Read-only</Badge>
      <Button variant="secondary" disabled={loading} onClick={() => setRefreshRevision((value) => value + 1)}>
        {loading ? 'Refreshing…' : 'Refresh sources'}
      </Button>
    </PageHeader>

    {loading && result === null && <HygieneLoadStatus
      progress={hygieneOperation.progress}
      elapsedSeconds={hygieneOperation.elapsedSeconds}
      onCancel={() => activeLoad.current?.cancel()}
    />}
    {error && result === null && <ErrorState
      title="Action Center unavailable"
      {...error}
      controls={<Button onClick={() => setRefreshRevision((value) => value + 1)}>Retry</Button>}
    />}
    {error && result && <div className="rounded border border-fail-800 bg-fail-950/20 px-3 py-2" role="alert">
      <p className="text-sm font-medium text-fail-200">Refresh failed; the previous computed result remains visible.</p>
      <p className="mt-1 text-xs text-slate-300">{error.message}</p>
    </div>}

    {result && <>
      <section className="rounded-lg border border-slate-800 bg-slate-900/50 px-4 py-3" aria-label="Action Center summary">
        <dl className="flex flex-wrap gap-x-8 gap-y-3">
          {[
            ['Open evidence', result.summary.total, 'text-slate-100'],
            ['Critical', result.summary.critical, 'text-fail-300'],
            ['High', result.summary.high, 'text-fail-300'],
            ['Warning', result.summary.warning, 'text-warn-300'],
            ['Unknown coverage', result.summary.unknownCoverage, 'text-slate-300'],
          ].map(([label, value, color]) => <div key={label as string}>
            <dt className="text-xs uppercase tracking-wide text-muted">{label}</dt>
            <dd className={`mt-0.5 font-mono text-xl font-semibold tabular-nums ${color}`}>{value}</dd>
          </div>)}
          <div className="ml-auto">
            <dt className="text-xs uppercase tracking-wide text-muted">Assessed</dt>
            <dd className="mt-1 text-sm text-slate-300">{formatTimestamp(result.assessedAtUtc)}</dd>
          </div>
        </dl>
      </section>

      <DetailsDisclosure summary="Source coverage">
        <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          {result.sources.map((source) => {
            const presentation = coveragePresentation[source.availability];
            return <li key={source.source} className="rounded border border-slate-800 bg-slate-900/50 p-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium text-slate-200">{source.source}</span>
                <Badge tone={presentation.tone}>{presentation.label}</Badge>
              </div>
              {source.explanation && <p className="mt-1 text-xs text-muted">{source.explanation}</p>}
            </li>;
          })}
        </ul>
      </DetailsDisclosure>

      {result.itemsTruncated && <div className="rounded border border-warn-800 bg-warn-950/20 px-3 py-2 text-sm text-warn-200" role="status">
        The computed source evidence reached a configured limit. Filters operate only on the explicitly bounded result.
      </div>}

      <form onSubmit={(event) => { event.preventDefault(); applyFilters(); }}>
        <Toolbar actions={<Button type="submit" variant="primary" disabled={loading}>Apply filters</Button>}>
          <Input
            type="search"
            aria-label="Search Action Center"
            placeholder="Device, problem, source or next action"
            value={draftFilters.search}
            onChange={(event) => setDraftFilters((current) => ({ ...current, search: event.target.value }))}
            className="w-80"
          />
          <Select
            fullWidth={false}
            aria-label="Severity"
            value={draftFilters.severity}
            onChange={(event) => setDraftFilters((current) => ({
              ...current,
              severity: event.target.value as ActionCenterFilters['severity'],
            }))}
          >
            <option value="ALL">All severities</option>
            {Object.entries(severityPresentation).map(([value, presentation]) =>
              <option key={value} value={value}>{presentation.label}</option>)}
          </Select>
          <Select
            fullWidth={false}
            aria-label="Source"
            value={draftFilters.source}
            onChange={(event) => setDraftFilters((current) => ({ ...current, source: event.target.value }))}
          >
            <option value="">All sources</option>
            {knownSources.map((source) => <option key={source} value={source}>{source}</option>)}
          </Select>
        </Toolbar>
      </form>

      <DataTable layout="fixed"
        columns={columns}
        rows={result.items}
        getRowKey={(item) => item.id}
        emptyMessage="No computed work items match these filters. Review source coverage before treating an empty list as healthy."
        loading={loading}
        stickyHeader
        sort={tableSort}
        onSortChange={changeSort}
        pagination={{
          page: result.page,
          pageSize: result.pageSize,
          total: result.total,
          itemLabel: 'work items',
          onPageChange: setPage,
          onPageSizeChange: (value) => { setPage(1); setPageSize(value); },
        }}
      />
      {selectedItem && <RelationshipMap
        model={actionCenterRelationshipModel(selectedItem)}
        actions={<Button variant="ghost" onClick={() => setSelectedItem(null)}>Close context</Button>}
      />}
    </>}
  </div>;
}
