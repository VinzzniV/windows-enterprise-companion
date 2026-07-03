import type { ReactNode } from 'react';

export interface DataColumn<T> {
  header: string;
  cell(row: T): ReactNode;
}

interface DataTableProps<T> {
  columns: DataColumn<T>[];
  rows: T[];
  emptyMessage: string;
}

/** Dense, scannable table with horizontal overflow handling and an empty state. */
export function DataTable<T>({ columns, rows, emptyMessage }: DataTableProps<T>) {
  if (rows.length === 0) {
    return <p className="text-sm text-slate-400">{emptyMessage}</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="text-slate-400">
            {columns.map((column) => (
              <th key={column.header} className="pb-1 pr-4 font-normal">
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, rowIndex) => (
            <tr key={rowIndex} className="border-t border-slate-800">
              {columns.map((column) => (
                <td key={column.header} className="py-1 pr-4 align-top">
                  {column.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
