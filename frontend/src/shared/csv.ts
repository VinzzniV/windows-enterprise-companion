/** One exportable column: stable key for the picker, header text, cell value. */
export interface CsvColumn<T> {
  key: string;
  header: string;
  value: (row: T) => string | number | null | undefined;
  /** Pre-selected in the column picker. */
  defaultOn?: boolean;
}

function escapeCsvField(value: string | number | null | undefined): string {
  const text = value == null ? '' : String(value);
  return /[;"\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Semicolon-separated (opens correctly in a German-locale Excel), CRLF line ends. */
export function toCsv<T>(rows: readonly T[], columns: readonly CsvColumn<T>[]): string {
  return [
    columns.map((column) => escapeCsvField(column.header)).join(';'),
    ...rows.map((row) => columns.map((column) => escapeCsvField(column.value(row))).join(';')),
  ].join('\r\n');
}
