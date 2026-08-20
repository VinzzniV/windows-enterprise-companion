import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Field } from './Field';
import { Input } from './Input';
import { Select } from './Select';
import { Checkbox } from './Checkbox';

describe('form primitives', () => {
  it('Field associates its label with the control via the rendered id', () => {
    render(
      <Field label="opsi server" hint="host or full URL">
        {(id) => <Input id={id} defaultValue="opsi.local" />}
      </Field>,
    );

    const input = screen.getByLabelText('opsi server') as HTMLInputElement;
    expect(input.value).toBe('opsi.local');
    expect(screen.getByText('host or full URL').className).toContain('text-muted');
  });

  it('Field shows the error instead of the hint and marks it', () => {
    render(
      <Field label="Port" hint="default 4447" error="Port is required">
        {(id) => <Input id={id} invalid />}
      </Field>,
    );

    expect(screen.getByText('Port is required')).toBeDefined();
    expect(screen.queryByText('default 4447')).toBeNull();
    expect(screen.getByLabelText('Port').getAttribute('aria-invalid')).toBe('true');
  });

  it('Input forwards typing to onChange', async () => {
    const onChange = vi.fn();
    render(<Input aria-label="host" value="" onChange={onChange} />);

    await userEvent.type(screen.getByLabelText('host'), 'pc1');

    expect(onChange).toHaveBeenCalled();
  });

  it('Select is a combobox that reports the chosen value', async () => {
    const onChange = vi.fn();
    render(
      <Select aria-label="depot" defaultValue="" onChange={onChange} fullWidth={false}>
        <option value="">All</option>
        <option value="denkingen">Denkingen</option>
      </Select>,
    );

    const select = screen.getByRole('combobox') as HTMLSelectElement;
    await userEvent.selectOptions(select, 'denkingen');

    expect(onChange).toHaveBeenCalled();
    expect(select.value).toBe('denkingen');
  });

  it('Checkbox toggles and exposes its label as the accessible name', async () => {
    const onChange = vi.fn();
    render(<Checkbox label="Trust server certificate" checked={false} onChange={onChange} />);

    await userEvent.click(screen.getByRole('checkbox', { name: 'Trust server certificate' }));

    expect(onChange).toHaveBeenCalled();
  });
});
