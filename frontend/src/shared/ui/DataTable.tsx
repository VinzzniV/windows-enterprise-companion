import { useState, type KeyboardEvent, type ReactNode } from 'react';
import { compareSortKeys, type SortKey } from '../sort';

export interface DataColumn<T> {
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
}

interface DataTableProps<T> {
  columns: DataColumn<T>[];
  rows: T[];
  emptyMessage: string;
  getRowKey?: (row: T, index: number) => string | number;
  onRowClick?: (row: T) => void;
  isRowActive?: (row: T) => boolean;
  /** Alternating row tint for scanning; on by default. */
  zebra?: boolean;
  /** Keep the header visible while the body scrolls (long tables). */
  stickyHeader?: boolean;
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
}: DataTableProps<T>) {
  const [sort, setSort] = useState<{ index: number; dir: 'asc' | 'desc' } | null>(null);

  if (rows.length === 0) {
    return <p className="text-sm text-slate-400">{emptyMessage}</p>;
  }

  const sortKeyOf = (column: DataColumn<T>, row: T): SortKey => {
    if (column.sortValue) return column.sortValue(row);
    const value = column.cell(row);
    return typeof value === 'string' || typeof value === 'number' ? value : null;
  };

  const sortable = columns.map(
    (column) => Boolean(column.sortValue) || rows.some((row) => sortKeyOf(column, row) != null),
  );

  const sortedRows = sort
    ? [...rows].sort((a, b) =>
        compareSortKeys(sortKeyOf(columns[sort.index], a), sortKeyOf(columns[sort.index], b), sort.dir),
      )
    : rows;

  const toggleSort = (index: number) =>
    setSort((current) =>
      current && current.index === index
        ? { index, dir: current.dir === 'asc' ? 'desc' : 'asc' }
        : { index, dir: 'asc' });

  const handleKey = (event: KeyboardEvent<HTMLTableRowElement>, row: T) => {
    if (onRowClick && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      onRowClick(row);
    }
  };

  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-left text-sm">
        <thead>
          <tr>
            {columns.map((column, index) => {
              const canSort = sortable[index];
              const isSorted = sort?.index === index;
              return (
                <th
                  key={column.header}
                  scope="col"
                  aria-sort={isSorted ? (sort!.dir === 'asc' ? 'ascending' : 'descending') : undefined}
                  onClick={canSort ? () => toggleSort(index) : undefined}
                  onKeyDown={
                    canSort
                      ? (event) => {
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault();
                            toggleSort(index);
                          }
                        }
                      : undefined
                  }
                  tabIndex={canSort ? 0 : undefined}
                  className={`border-b border-slate-800 px-3 py-1.5 text-xs font-medium uppercase tracking-wide text-slate-500 ${
                    column.align ? alignClass[column.align] : 'text-left'
                  } ${stickyHeader ? 'sticky top-0 z-10 bg-slate-900' : ''} ${
                    canSort ? 'cursor-pointer select-none hover:text-slate-300' : ''
                  }`}
                >
                  <span className="inline-flex items-center gap-1">
                    {column.header}
                    {canSort && (
                      <span aria-hidden className="text-[10px] text-slate-600">
                        {isSorted ? (sort!.dir === 'asc' ? '▲' : '▼') : '↕'}
                      </span>
                    )}
                  </span>
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {sortedRows.map((row, rowIndex) => {
            const active = isRowActive?.(row) ?? false;
            return (
              <tr
                key={getRowKey ? getRowKey(row, rowIndex) : rowIndex}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                onKeyDown={onRowClick ? (event) => handleKey(event, row) : undefined}
                tabIndex={onRowClick ? 0 : undefined}
                role={onRowClick ? 'button' : undefined}
                aria-pressed={onRowClick ? active : undefined}
                className={`border-t border-slate-800/70 ${
                  zebra ? 'even:bg-slate-800/20' : ''
                } ${active ? 'bg-accent-500/10' : ''} ${
                  onRowClick ? 'cursor-pointer transition-colors hover:bg-slate-800/40' : ''
                }`}
              >
                {columns.map((column) => (
                  <td
                    key={column.header}
                    className={`px-3 py-1.5 align-top ${column.align ? alignClass[column.align] : ''} ${
                      column.mono ? 'font-mono text-[13px] tabular-nums' : ''
                    }`}
                  >
                    {column.cell(row)}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
