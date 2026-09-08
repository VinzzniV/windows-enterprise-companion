import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { MouseEvent } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { DataTable, type DataColumn } from './DataTable';

interface Row { id: number; name: string }
const columns: DataColumn<Row>[] = [
  { id: 'name', header: 'Name', cell: (row) => row.name, sortable: true },
];

describe('DataTable', () => {
  it('renders the supplied page and reports its range without slicing or fetching', () => {
    render(
      <DataTable
        columns={columns}
        rows={[{ id: 3, name: 'Charlie' }, { id: 4, name: 'Delta' }]}
        getRowKey={(row) => row.id}
        emptyMessage="No rows"
        pagination={{ page: 2, pageSize: 2, total: 5, onPageChange: vi.fn() }}
      />,
    );

    expect(screen.getByText('Charlie')).toBeTruthy();
    expect(screen.getByText('Delta')).toBeTruthy();
    expect(screen.getByText('3–4 of 5')).toBeTruthy();
    expect(screen.getByText('Page 2 of 3')).toBeTruthy();
    expect(screen.getByRole('columnheader', { name: /Name/ }).className).toContain('text-muted');
  });

  it('emits controlled page and page-size changes', async () => {
    const onPageChange = vi.fn();
    const onPageSizeChange = vi.fn();
    render(
      <DataTable
        columns={columns}
        rows={[{ id: 1, name: 'Alpha' }, { id: 2, name: 'Bravo' }]}
        emptyMessage="No rows"
        pagination={{ page: 1, pageSize: 2, total: 5, onPageChange, onPageSizeChange, pageSizeOptions: [2, 5] }}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    await userEvent.selectOptions(screen.getByLabelText('Rows per page'), '5');
    expect(onPageChange).toHaveBeenCalledWith(2);
    expect(onPageSizeChange).toHaveBeenCalledWith(5);
  });

  it('emits controlled sort changes without reordering the current page locally', async () => {
    const onSortChange = vi.fn();
    render(
      <DataTable
        columns={columns}
        rows={[{ id: 2, name: 'Zulu' }, { id: 1, name: 'Alpha' }]}
        emptyMessage="No rows"
        sort={{ column: 'name', direction: 'asc' }}
        onSortChange={onSortChange}
      />,
    );

    expect(screen.getAllByRole('row')[1].textContent).toContain('Zulu');
    expect(screen.getByRole('columnheader', { name: /Name/ }).getAttribute('aria-sort')).toBe('ascending');
    await userEvent.click(screen.getByRole('button', { name: /Name/ }));
    expect(onSortChange).toHaveBeenCalledWith({ column: 'name', direction: 'desc' });
  });

  it('distinguishes loading from an empty result', () => {
    render(
      <DataTable columns={columns} rows={[]} emptyMessage="No rows" loading />,
    );

    expect(screen.getByRole('status').textContent).toContain('Loading table data');
    expect(screen.queryByText('No rows')).toBeNull();
  });

  it('applies feature-owned responsive sizing to both header and cells', () => {
    render(
      <DataTable
        columns={[{ ...columns[0], className: 'w-72' }]}
        rows={[{ id: 1, name: 'Alpha' }]}
        emptyMessage="No rows"
      />,
    );

    expect(screen.getByRole('columnheader', { name: /Name/ }).className).toContain('w-72');
    expect(screen.getByText('Alpha').closest('td')?.className).toContain('w-72');
  });

  it('activates a focused row with Enter or Space', async () => {
    const onRowClick = vi.fn();
    render(
      <DataTable
        columns={columns}
        rows={[{ id: 1, name: 'Alpha' }]}
        emptyMessage="No rows"
        onRowClick={onRowClick}
        isRowActive={() => true}
      />,
    );
    const row = screen.getAllByRole('row')[1];

    row.focus();
    await userEvent.keyboard('{Enter} ');

    expect(onRowClick).toHaveBeenCalledTimes(2);
    expect(row.getAttribute('aria-selected')).toBe('true');
    expect(row.className).toContain('focus-visible:outline');
  });

  it('focuses a clicked selectable row before opening its details', async () => {
    let focusedAtSelection: Element | null = null;
    render(
      <DataTable
        columns={columns}
        rows={[{ id: 1, name: 'Alpha' }]}
        emptyMessage="No rows"
        onRowClick={() => { focusedAtSelection = document.activeElement; }}
      />,
    );
    const row = screen.getAllByRole('row')[1];

    await userEvent.click(row);

    expect(focusedAtSelection).toBe(row);
  });

  it('leaves checkbox, button and link activation to the child controls', async () => {
    const onRowClick = vi.fn();
    const onButtonClick = vi.fn();
    const onLinkClick = vi.fn((event: MouseEvent<HTMLAnchorElement>) => event.preventDefault());
    const interactiveColumns: DataColumn<Row>[] = [{
      header: 'Actions',
      cell: () => (
        <span>
          <label><input type="checkbox" aria-label="Select Alpha" /> Select</label>
          <button type="button" onClick={onButtonClick}>Inspect</button>
          <a href="/alpha" onClick={onLinkClick}>Open</a>
        </span>
      ),
    }];
    render(
      <DataTable
        columns={interactiveColumns}
        rows={[{ id: 1, name: 'Alpha' }]}
        emptyMessage="No rows"
        onRowClick={onRowClick}
      />,
    );

    const checkbox = screen.getByRole('checkbox', { name: 'Select Alpha' });
    checkbox.focus();
    await userEvent.keyboard(' ');
    await userEvent.click(screen.getByRole('button', { name: 'Inspect' }));
    await userEvent.click(screen.getByRole('link', { name: 'Open' }));

    expect((checkbox as HTMLInputElement).checked).toBe(true);
    expect(onButtonClick).toHaveBeenCalledOnce();
    expect(onLinkClick).toHaveBeenCalledOnce();
    expect(onRowClick).not.toHaveBeenCalled();
  });
});
