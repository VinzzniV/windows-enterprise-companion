import { describe, expect, it } from 'vitest';
import { toCsv, type CsvColumn } from './csv';

interface Row {
  name: string;
  count: number;
  note: string | null;
}

const columns: CsvColumn<Row>[] = [
  { key: 'name', header: 'Name', value: (row) => row.name },
  { key: 'count', header: 'Count', value: (row) => row.count },
  { key: 'note', header: 'Note', value: (row) => row.note },
];

describe('toCsv', () => {
  it('writes a header row and quotes only where needed', () => {
    const csv = toCsv(
      [
        { name: 'PK-NETPRT001', count: 2, note: null },
        { name: 'Queue;with;semicolons', count: 0, note: 'say "hi"' },
      ],
      columns,
    );

    expect(csv.split('\r\n')).toEqual([
      'Name;Count;Note',
      'PK-NETPRT001;2;',
      '"Queue;with;semicolons";0;"say ""hi"""',
    ]);
  });

  it('exports only the given columns', () => {
    const csv = toCsv([{ name: 'A', count: 1, note: 'x' }], [columns[0], columns[2]]);
    expect(csv).toBe('Name;Note\r\nA;x');
  });
});
