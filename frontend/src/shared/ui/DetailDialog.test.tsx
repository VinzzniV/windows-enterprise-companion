import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { DetailDialog } from './DetailDialog';

function Harness() {
  const [open, setOpen] = useState(false);
  return <>
    <button type="button" onClick={() => setOpen(true)}>Open details</button>
    {open && <DetailDialog
      title="Evidence details"
      description="Current source evidence"
      closeLabel="Close evidence details"
      onClose={() => setOpen(false)}
    >
      <button type="button">First action</button>
      <button type="button">Last action</button>
    </DetailDialog>}
  </>;
}

describe('DetailDialog', () => {
  it('keeps details in the viewport and restores focus after Escape', async () => {
    render(<Harness />);
    const trigger = screen.getByRole('button', { name: 'Open details' });
    await userEvent.click(trigger);

    const dialog = screen.getByRole('dialog', { name: 'Evidence details' });
    expect(dialog.className).toContain('max-h-[calc(100dvh-1rem)]');
    expect(document.activeElement).toBe(dialog);

    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('wraps Tab focus within the detail controls', async () => {
    render(<Harness />);
    await userEvent.click(screen.getByRole('button', { name: 'Open details' }));
    const close = screen.getByRole('button', { name: 'Close evidence details' });
    const last = screen.getByRole('button', { name: 'Last action' });

    last.focus();
    await userEvent.tab();
    expect(document.activeElement).toBe(close);

    close.focus();
    await userEvent.tab({ shift: true });
    expect(document.activeElement).toBe(last);
  });
});
