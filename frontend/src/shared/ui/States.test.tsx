import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { CompactErrorState, ErrorState } from './States';

describe('ErrorState', () => {
  it('prioritizes admin guidance and progressively discloses technical details', async () => {
    render(
      <ErrorState
        title="Scan failed"
        message="The print server could not be scanned."
        cause="The supplied credentials were rejected."
        action="Sign in with a print-server administrator account and retry."
        technicalDetails="AUTHENTICATION_FAILED: Logon failed (0x52e)"
      />,
    );

    expect(screen.getByText('Cause')).toBeDefined();
    expect(screen.getByText('Next action')).toBeDefined();
    expect(screen.getByText(/supplied credentials/)).toBeDefined();
    const disclosure = screen.getByText('Technical details').closest('details');
    expect(disclosure?.hasAttribute('open')).toBe(false);

    await userEvent.click(screen.getByText('Technical details'));
    expect(disclosure?.hasAttribute('open')).toBe(true);
    expect(screen.getByText(/AUTHENTICATION_FAILED/)).toBeDefined();
  });

  it('offers the same guidance hierarchy in compact shell surfaces', async () => {
    render(
      <CompactErrorState
        title="Elevation failed"
        message="The elevated application could not be started."
        cause="Windows rejected the launch request."
        action="Retry or start the application as administrator from Windows."
        technicalDetails="ACCESS_DENIED: ShellExecute returned 5"
      />,
    );

    const alert = screen.getByRole('alert');
    expect(screen.getByText('Elevation failed')).toBeDefined();
    expect(screen.getByText('Cause')).toBeDefined();
    expect(screen.getByText('Next action')).toBeDefined();
    const disclosure = screen.getByText('Technical details').closest('details') as HTMLDetailsElement;
    expect(disclosure.open).toBe(false);
    await userEvent.click(disclosure.querySelector('summary')!);
    expect(disclosure.open).toBe(true);
    expect(alert.textContent).toContain('ShellExecute returned 5');
  });
});
