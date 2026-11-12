import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DeviceConnectivityStatus } from './DeviceConnectivityStatus';

describe('DeviceConnectivityStatus', () => {
  it.each([
    ['responding', 'Ping or WinRM responded', 'text-ok-200'],
    ['no-response', 'No Ping or WinRM response', 'text-fail-200'],
    ['failed', 'Connectivity check failed', 'text-fail-200'],
    ['checking', 'Checking…', 'text-warn-200'],
    ['not-checked', 'Not checked', 'text-slate-200'],
  ] as const)('highlights %s without relying on color alone', (status, label, tone) => {
    render(<DeviceConnectivityStatus status={status} />);
    const indicator = screen.getByRole('status');
    expect(indicator.textContent).toContain(label);
    expect(indicator.className).toContain(tone);
    expect(indicator.className).toContain('font-semibold');
  });
});
