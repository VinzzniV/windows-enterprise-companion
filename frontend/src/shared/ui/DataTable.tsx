import { Fragment, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react';
import { compareSortKeys, type SortKey } from '../sort';
import { Button } from './Button';
import { Select } from './Select';

export type DataTableSortDirection = 'asc' | 'desc';

export interface DataTableSort {
  column: string;
  direction: DataTableSortDirection;
}

export interface DataTablePagination {
  page: number;
  pageSize: number;
  total: number;
  /** Optional label when pagination counts groups rather than individual rows. */
  itemLabel?: string;
  onPageChange(page: number): void;
  onPageSizeChange?(pageSize: number): void;
  pageSizeOptions?: readonly number[];
}

export interface DataColumn<T> {
  /** Stable identifier used by controlled server sorting. */
  id?: string;
  header: string;
  cell(row: T): ReactNode;
  align?: 'left' | 'right' | 'center';
  /** Monospace the cell (serials, IPs, MACs, versions, OIDs). */
  mono?: boolean;
  /**
   * Explicit sort key. When omitted, a primitive (string/number) cell value is
   * used automatically; columns whose cells render JSX stay unsortable unless
   * they provide this.
   */
  sortValue?(row: T): SortKey;
  /** Enable this column in controlled sorting mode. */
  sortable?: boolean;
}

export interface DataTableGroup {
  key: string;
  label: ReactNode;
}

interface DataTableProps<T> {
  layout?: 'auto' | 'fixed';
  columns: readonly DataColumn<T>[];
  /** The rows for the current page. DataTable never fetches or slices them. */
  rows: readonly T[];
  emptyMessage: string;
  getRowKey?: (row: T, index: number) => string | number;
  onRowClick?: (row: T) => void;
  isRowActive?: (row: T) => boolean;
  /** Alternating row tint for scanning; on by default. */
  zebra?: boolean;
  /** Keep the header visible while the body scrolls (long tables). */
  stickyHeader?: boolean;
  /** Controlled server sorting. Omit both props to retain local sorting. */
  sort?: DataTableSort | null;
  onSortChange?: (sort: DataTableSort) => void;
  /** Controlled page metadata and navigation. */
  pagination?: DataTablePagination;
  loading?: boolean;
  /** Optional collapsible groups for already sorted, server-paged rows. */
  groupBy?(row: T): DataTableGroup;
}

const alignClass: Record<NonNullable<DataColumn<unknown>['align']>, string> = {
  left: 'text-left',
  right: 'text-right',
  center: 'text-center',
};

/** Dense, scannable table with per-column alignment/monospace, zebra rows and an empty state. */
export function DataTable<T>({
  columns,
  rows,
  emptyMessage,
  getRowKey,
  onRowClick,
  isRowActive,
  zebra = true,
  stickyHeader = false,
  sort: controlledSort,
  onSortChange,
  pagination,
  loading = false,
  groupBy,
  layout = 'auto',
}: DataTableProps<T>) {
  const [localSort, setLocalSort] = useState<{ index: number; dir: DataTableSortDirection } | null>(null);
  const [expandedGroups, setExpandedGroups] = useState<ReadonlySet<string>>(new Set());
  const isControlledSort = onSortChange !== undefined;

  const sortKeyOf = (column: DataColumn<T>, row: T): SortKey => {
    if (column.sortValue) return column.sortValue(row);
    const value = column.cell(row);
    return typeof value === 'string' || typeof value === 'number' ? value : null;
  };

  const sortable = columns.map((column) =>
    isControlledSort
      ? Boolean(column.sortable)
      : Boolean(column.sortValue) || rows.some((row) => sortKeyOf(column, row) != null),
  );

  const sortedRows = !isControlledSort && localSort
    ? [...rows].sort((a, b) =>
        compareSortKeys(sortKeyOf(columns[localSort.index], a), sortKeyOf(columns[localSort.index], b), localSort.dir),
      )
    : rows;

  const toggleSort = (index: number) => {
    const columnId = columns[index].id ?? columns[index].header;
    if (isControlledSort) {
      onSortChange({
        column: columnId,
        direction: controlledSort?.column === columnId && controlledSort.direction === 'asc' ? 'desc' : 'asc',
      });
      return;
    }
    setLocalSort((current) =>
      current && current.index === index
        ? { index, dir: current.dir === 'asc' ? 'desc' : 'asc' }
        : { index, dir: 'asc' },
    );
  };

  const handleKey = (event: KeyboardEvent<HTMLTableRowElement>, row: T) => {
    if (event.target !== event.currentTarget) return;
    if (onRowClick && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      onRowClick(row);
    }
  };

  const handleRowClick = (event: MouseEvent<HTMLTableRowElement>, row: T) => {
    const target = event.target instanceof Element ? event.target : null;
    const interactiveTarget = target?.closest(
      'a, button, input, select, textarea, label, summary, [contenteditable="true"], [role="button"], [role="link"], [role="checkbox"]',
    );
    if (interactiveTarget && interactiveTarget !== event.currentTarget) return;
    onRowClick?.(row);
  };

  const pageCount = pagination ? Math.max(1, Math.ceil(pagination.total / pagination.pageSize)) : 1;
  const rangeStart = pagination?.total
    ? Math.min((pagination.page - 1) * pagination.pageSize + 1, pagination.total)
    : 0;
  const groupedRows = groupBy
    ? sortedRows.reduce<{ group: DataTableGroup; rows: T[] }[]>((groups, row) => {
      const group = groupBy(row);
      const existing = groups.find((entry) => entry.group.key === group.key);
      if (existing) existing.rows.push(row);
      else groups.push({ group, rows: [row] });
      return groups;
    }, [])
    : null;
  const rangeEnd = pagination
    ? Math.min(rangeStart + (groupedRows?.length ?? rows.length) - 1, pagination.total)
    : 0;
  const toggleGroup = (key: string) => setExpandedGroups((current) => {
    const next = new Set(current);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    return next;
  });
  const renderRow = (row: T, rowIndex: number) => {
    const active = isRowActive?.(row) ?? false;
    return (
      <tr
        key={getRowKey ? getRowKey(row, rowIndex) : rowIndex}
        onClick={onRowClick ? (event) => handleRowClick(event, row) : undefined}
        onKeyDown={onRowClick ? (event) => handleKey(event, row) : undefined}
        tabIndex={onRowClick ? 0 : undefined}
        aria-selected={onRowClick ? active : undefined}
        className={`border-t border-slate-800/70 ${
          zebra ? 'even:bg-slate-800/20' : ''
        } ${active ? 'bg-accent-500/10' : ''} ${
          onRowClick ? 'cursor-pointer transition-colors hover:bg-slate-800/40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-accent-400' : ''
        }`}
      >
        {columns.map((column) => (
          <td
            key={column.header}
            className={`px-3 py-1.5 align-top [overflow-wrap:anywhere] ${column.align ? alignClass[column.align] : ''} ${
              column.mono ? 'font-mono text-[13px] tabular-nums' : ''
            }`}
          >
            {column.cell(row)}
          </td>
        ))}
      </tr>
    );
  };

  return (
    <div aria-busy={loading} className="min-w-0 max-w-full">
      <div className="overflow-x-auto">
      {rows.length === 0 ? (
        <p className="text-sm text-slate-400" role={loading ? 'status' : undefined}>
          {loading ? 'Loading table data…' : emptyMessage}
        </p>
      ) : <table className={`w-full border-collapse text-left text-sm ${layout === 'fixed' ? 'table-fixed min-w-[44rem]' : ''}`}>
        <thead>
          <tr>
            {columns.map((column, index) => {
              const canSort = sortable[index];
              const columnId = column.id ?? column.header;
              const isSorted = isControlledSort
                ? controlledSort?.column === columnId
                : localSort?.index === index;
              const sortDirection = isControlledSort ? controlledSort?.direction : localSort?.dir;
              return (
                <th
                  key={column.header}
                  scope="col"
                  aria-sort={isSorted ? (sortDirection === 'asc' ? 'ascending' : 'descending') : undefined}
                  className={`border-b border-slate-800 px-3 py-1.5 text-xs font-medium uppercase tracking-wide text-muted ${
                    column.align ? alignClass[column.align] : 'text-left'
                  } ${stickyHeader ? 'sticky top-0 z-10 bg-slate-900' : ''}`}
                >
                  <button
                    type="button"
                    disabled={!canSort}
                    onClick={canSort ? () => toggleSort(index) : undefined}
                    className={`inline-flex items-center gap-1 text-inherit ${canSort ? 'cursor-pointer select-none hover:text-slate-300' : 'cursor-default'}`}
                  >
                    {column.header}
                    {canSort && (
                      <span aria-hidden className="text-[10px] text-slate-600">
                        {isSorted ? (sortDirection === 'asc' ? '▲' : '▼') : '↕'}
                      </span>
                    )}
                  </button>
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {groupedRows
            ? groupedRows.map(({ group, rows: groupRows }) => {
              const expanded = expandedGroups.has(group.key);
              return <Fragment key={group.key}>
                <tr className="border-t border-slate-700 bg-slate-800/50">
                  <td colSpan={columns.length} className="px-3 py-2">
                    <button type="button" onClick={() => toggleGroup(group.key)} aria-expanded={expanded} className="inline-flex items-center gap-2 font-medium text-slate-200 hover:text-white">
                      <span aria-hidden>{expanded ? '▾' : '▸'}</span>{group.label}
                    </button>
                  </td>
                </tr>
                {expanded && groupRows.map(renderRow)}
              </Fragment>;
            })
            : sortedRows.map(renderRow)}
        </tbody>
      </table>}
      </div>
      {pagination && (
        <div className="mt-3 flex flex-wrap items-center justify-between gap-3 border-t border-slate-800 pt-3 text-sm text-slate-400">
          <span aria-live="polite">
            {rangeStart}–{Math.max(rangeStart, rangeEnd)} of {pagination.total}{pagination.itemLabel ? ` ${pagination.itemLabel}` : ''}
            {loading && <span className="ml-2" role="status">Loading…</span>}
          </span>
          <div className="flex items-center gap-2">
            {pagination.onPageSizeChange && (
              <label className="flex items-center gap-2">
                <span>Rows per page</span>
                <Select
                  fullWidth={false}
                  aria-label="Rows per page"
                  value={pagination.pageSize}
                  disabled={loading}
                  onChange={(event) => pagination.onPageSizeChange?.(Number(event.target.value))}
                >
                  {(pagination.pageSizeOptions ?? [25, 50, 100]).map((value) => (
                    <option key={value} value={value}>{value}</option>
                  ))}
                </Select>
              </label>
            )}
            <Button
              variant="ghost"
              disabled={loading || pagination.page <= 1}
              onClick={() => pagination.onPageChange(pagination.page - 1)}
            >
              Previous
            </Button>
            <span>Page {pagination.page} of {pageCount}</span>
            <Button
              variant="ghost"
              disabled={loading || pagination.page >= pageCount}
              onClick={() => pagination.onPageChange(pagination.page + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
