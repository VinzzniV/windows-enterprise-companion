import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import type {
  DirectoryUserSortField,
  UserPageResult,
  UserSummary,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { useTargets } from '../../shared/targets/TargetContext';
import { SavedTargetsBar } from '../../shared/targets/SavedTargetsBar';
import { Badge } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn, type DataTableSort } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { Toolbar } from '../../shared/ui/Toolbar';
import { loadView, saveView } from '../../shared/viewCache';
import {
  defaultUserSort,
  emptyUserDirectoryEndpoint,
  formatDirectoryTimestamp,
  toUserDirectoryConnection,
  userDirectoryViewKey,
  type UserDirectoryEndpoint,
  type UserFilters,
  type UserSort,
} from './users';

const sortFieldByColumn: Record<string, DirectoryUserSortField> = {
  displayName: 'DISPLAY_NAME',
  samAccountName: 'SAM_ACCOUNT_NAME',
  department: 'DEPARTMENT',
  lastLogon: 'LAST_LOGON',
};

const columnBySortField: Record<DirectoryUserSortField, string> = {
  DISPLAY_NAME: 'displayName',
  SAM_ACCOUNT_NAME: 'samAccountName',
  DEPARTMENT: 'department',
  CREATED_AT: 'displayName',
  LAST_LOGON: 'lastLogon',
};

function AccountState({ enabled }: { enabled: boolean | null }) {
  if (enabled === true) return <Badge tone="ok">Enabled</Badge>;
  if (enabled === false) return <Badge tone="neutral">Disabled</Badge>;
  return <Badge tone="warn">Unknown</Badge>;
}

function positiveInteger(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function filtersFromParams(params: URLSearchParams): UserFilters {
  const accountState = params.get('account');
  return {
    search: params.get('q') ?? '',
    department: params.get('department') ?? '',
    baseDistinguishedName: params.get('ou') ?? '',
    accountState: accountState === 'ENABLED' || accountState === 'DISABLED' ? accountState : 'ALL',
  };
}

export function UsersPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const { adminCredentials, savedTargets, savedTargetsReady } = useTargets();
  const cachedEndpoint = useRef(loadView<UserDirectoryEndpoint>(userDirectoryViewKey));
  const [draftEndpoint, setDraftEndpoint] = useState<UserDirectoryEndpoint>(
    () => cachedEndpoint.current ?? emptyUserDirectoryEndpoint,
  );
  const [activeEndpoint, setActiveEndpoint] = useState<UserDirectoryEndpoint | null>(null);
  const initialFilters = useRef(filtersFromParams(searchParams));
  const [draftFilters, setDraftFilters] = useState<UserFilters>(initialFilters.current);
  const [activeFilters, setActiveFilters] = useState<UserFilters>(initialFilters.current);
  const [sort, setSort] = useState<UserSort>(() => {
    const field = searchParams.get('sort');
    return {
      field: field && field in columnBySortField ? field as DirectoryUserSortField : defaultUserSort.field,
      direction: searchParams.get('dir') === 'DESCENDING' ? 'DESCENDING' : 'ASCENDING',
    };
  });
  const [page, setPage] = useState(() => positiveInteger(searchParams.get('page'), 1));
  const [pageSize, setPageSize] = useState(() => positiveInteger(searchParams.get('size'), 25));
  const [refreshRevision, setRefreshRevision] = useState(0);
  const [result, setResult] = useState<UserPageResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const requestId = useRef(0);

  const persistView = useCallback((
    filters: UserFilters,
    nextPage: number,
    nextPageSize: number,
    nextSort: UserSort,
  ) => {
    const next = new URLSearchParams();
    if (filters.search.trim()) next.set('q', filters.search.trim());
    if (filters.department.trim()) next.set('department', filters.department.trim());
    if (filters.baseDistinguishedName.trim()) next.set('ou', filters.baseDistinguishedName.trim());
    if (filters.accountState !== 'ALL') next.set('account', filters.accountState);
    next.set('page', String(nextPage));
    next.set('size', String(nextPageSize));
    next.set('sort', nextSort.field);
    next.set('dir', nextSort.direction);
    setSearchParams(next, { replace: true });
  }, [setSearchParams]);

  useEffect(() => {
    if (!savedTargetsReady || activeEndpoint !== null) return;
    const savedDc = savedTargets.filter((target) => target.role === 'DomainController').at(-1);
    const initial = cachedEndpoint.current ?? {
      domain: '',
      server: savedDc?.host ?? '',
    };
    setDraftEndpoint(initial);
    setActiveEndpoint(initial);
  }, [activeEndpoint, savedTargets, savedTargetsReady]);

  useEffect(() => {
    if (activeEndpoint === null) return;
    saveView(userDirectoryViewKey, activeEndpoint);
  }, [activeEndpoint]);

  useEffect(() => {
    if (activeEndpoint === null) return;
    const currentRequest = ++requestId.current;
    setLoading(true);
    setError(null);
    void invoke<UserPageResult>('usermanagement', 'listUsers', {
      search: activeFilters.search.trim() || null,
      department: activeFilters.department.trim() || null,
      baseDistinguishedName: activeFilters.baseDistinguishedName.trim() || null,
      accountState: activeFilters.accountState,
      page,
      pageSize,
      sortField: sort.field,
      sortDirection: sort.direction,
      connection: toUserDirectoryConnection(activeEndpoint, adminCredentials),
    }).then((value) => {
      if (requestId.current === currentRequest) setResult(value);
    }).catch((caught: unknown) => {
      if (requestId.current === currentRequest) {
        setError(presentError(caught, {
          message: 'The directory user inventory could not be loaded.',
          action: 'Check the directory endpoint and admin sign-in, then retry.',
        }));
      }
    }).finally(() => {
      if (requestId.current === currentRequest) setLoading(false);
    });
  }, [activeEndpoint, activeFilters, adminCredentials, page, pageSize, refreshRevision, sort]);

  const applyFilters = useCallback(() => {
    setPage(1);
    setActiveEndpoint({ ...draftEndpoint });
    setActiveFilters({ ...draftFilters });
    persistView(draftFilters, 1, pageSize, sort);
  }, [draftEndpoint, draftFilters, pageSize, persistView, sort]);

  const columns = useMemo<DataColumn<UserSummary>[]>(() => [
    {
      id: 'displayName',
      header: 'User',
      sortable: true,
      cell: (user) => <div className="flex flex-col gap-0.5">
        <span className="font-medium text-slate-100">{user.displayName}</span>
        {user.userPrincipalName && <span className="text-xs text-muted">{user.userPrincipalName}</span>}
      </div>,
    },
    {
      id: 'samAccountName',
      header: 'Account',
      sortable: true,
      mono: true,
      cell: (user) => user.samAccountName ?? '—',
    },
    {
      id: 'department',
      header: 'Organization',
      sortable: true,
      cell: (user) => <div className="flex flex-col gap-0.5">
        <span>{user.department ?? 'Not set'}</span>
        {user.title && <span className="text-xs text-muted">{user.title}</span>}
      </div>,
    },
    { header: 'State', cell: (user) => <AccountState enabled={user.enabled} /> },
    {
      id: 'lastLogon',
      header: 'Replicated last logon',
      sortable: true,
      cell: (user) => <span title="AD lastLogonTimestamp is replicated and can be stale">
        {formatDirectoryTimestamp(user.replicatedLastLogonAtUtc)}
      </span>,
    },
    {
      header: 'OU path',
      mono: true,
      cell: (user) => <span className="block max-w-80 truncate" title={user.organizationalUnitPath}>
        {user.organizationalUnitPath || '—'}
      </span>,
    },
  ], []);

  const tableSort: DataTableSort = {
    column: columnBySortField[sort.field],
    direction: sort.direction === 'ASCENDING' ? 'asc' : 'desc',
  };
  const changeSort = (next: DataTableSort) => {
    const field = sortFieldByColumn[next.column];
    if (!field) return;
    setPage(1);
    const nextSort: UserSort = {
      field,
      direction: next.direction === 'asc' ? 'ASCENDING' : 'DESCENDING',
    };
    setSort(nextSort);
    persistView(activeFilters, 1, pageSize, nextSort);
  };

  return <div className="flex flex-col gap-4">
    <PageHeader
      title="Users"
      subtitle="AD-authoritative, read-only user inventory and access context"
    >
      <Badge tone="info">Read-only</Badge>
      <Button
        variant="secondary"
        disabled={loading || activeEndpoint === null}
        onClick={() => setRefreshRevision((revision) => revision + 1)}
      >
        {loading ? 'Refreshing…' : 'Refresh'}
      </Button>
    </PageHeader>
    <Link className="text-sm text-accent-400 underline" to="/users">Open the shared account working set</Link>

    <details className="rounded-lg border border-slate-800 bg-slate-900/40" open={false}>
      <summary className="cursor-pointer px-3 py-2 text-sm font-medium text-slate-300">
        Directory connection
        <span className="ml-2 text-xs font-normal text-muted">
          {activeEndpoint?.server || activeEndpoint?.domain || 'This machine’s domain'}
        </span>
      </summary>
      <div className="flex flex-col gap-3 border-t border-slate-800 p-3">
        <p className="text-xs text-muted">
          Empty uses this machine’s domain. The bind uses the session admin sign-in from the top bar,
          or the current Windows identity when no admin is signed in.
        </p>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          <label className="flex flex-col gap-1 text-xs text-slate-400">
            Directory domain
            <Input
              value={draftEndpoint.domain}
              onChange={(event) => setDraftEndpoint((current) => ({ ...current, domain: event.target.value }))}
              placeholder="corp.example.local"
            />
          </label>
          <label className="flex flex-col gap-1 text-xs text-slate-400">
            Domain controller
            <Input
              value={draftEndpoint.server}
              onChange={(event) => setDraftEndpoint((current) => ({ ...current, server: event.target.value }))}
              placeholder="dc01.corp.example.local"
            />
          </label>
        </div>
        <SavedTargetsBar
          role="DomainController"
          label="Saved domain controllers"
          currentHost={draftEndpoint.server}
          currentUserName={adminCredentials?.userName ?? null}
          onPick={(target) => setDraftEndpoint((current) => ({ ...current, server: target.host }))}
        />
        <div><Button onClick={applyFilters}>Use directory connection</Button></div>
      </div>
    </details>

    <form onSubmit={(event) => { event.preventDefault(); applyFilters(); }}>
      <Toolbar actions={<Button type="submit" variant="primary" disabled={loading}>Apply filters</Button>}>
        <Input
          type="search"
          aria-label="Search users"
          placeholder="Name, account, UPN or employee ID"
          value={draftFilters.search}
          onChange={(event) => setDraftFilters((current) => ({ ...current, search: event.target.value }))}
          className="w-72"
        />
        <Input
          aria-label="Department"
          placeholder="Department"
          value={draftFilters.department}
          onChange={(event) => setDraftFilters((current) => ({ ...current, department: event.target.value }))}
          className="w-44"
        />
        <Input
          aria-label="Organizational unit distinguished name"
          placeholder="OU distinguished name"
          value={draftFilters.baseDistinguishedName}
          onChange={(event) => setDraftFilters((current) => ({ ...current, baseDistinguishedName: event.target.value }))}
          className="w-72 font-mono text-xs"
        />
        <Select
          fullWidth={false}
          aria-label="Account state"
          value={draftFilters.accountState}
          onChange={(event) => setDraftFilters((current) => ({
            ...current,
            accountState: event.target.value as UserFilters['accountState'],
          }))}
        >
          <option value="ALL">All accounts</option>
          <option value="ENABLED">Enabled</option>
          <option value="DISABLED">Disabled</option>
        </Select>
      </Toolbar>
    </form>

    {activeEndpoint === null && <Spinner label="Preparing directory connection…" />}
    {error && <ErrorState
      title="User inventory unavailable"
      {...error}
      controls={<Button onClick={() => setRefreshRevision((revision) => revision + 1)}>Retry</Button>}
    />}
    {!error && result && !result.domainJoined && <EmptyState
      title="No Active Directory domain"
      message="This machine is not domain-joined. Open Directory connection and name a domain or domain controller to query another directory."
    />}
    {!error && result?.domainJoined && <Card title={`Directory users · ${result.domainName ?? 'unknown domain'}`}>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2 text-xs text-muted">
        <span>{result.totalCount.toLocaleString()} matching accounts</span>
        <span className="font-mono">Base: {result.baseDistinguishedName}</span>
      </div>
      <DataTable
        columns={columns}
        rows={result.users}
        emptyMessage="No users match the current filters."
        getRowKey={(user) => user.objectId}
        onRowClick={(user) => navigate(
          `/users/${encodeURIComponent(user.objectId)}`,
          { state: {
            directoryEndpoint: activeEndpoint,
            returnTo: `${location.pathname}${location.search}`,
            returnLabel: 'AD user results',
          } },
        )}
        sort={tableSort}
        onSortChange={changeSort}
        pagination={{
          page: result.page,
          pageSize: result.pageSize,
          total: result.totalCount,
          itemLabel: 'users',
          onPageChange: (value) => { setPage(value); persistView(activeFilters, value, pageSize, sort); },
          onPageSizeChange: (size) => { setPage(1); setPageSize(size); persistView(activeFilters, 1, size, sort); },
        }}
        loading={loading}
        stickyHeader
      />
    </Card>}
    {!error && loading && result === null && activeEndpoint !== null && <Spinner label="Loading directory users…" />}
  </div>;
}
