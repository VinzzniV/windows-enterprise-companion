import { describe, expect, it } from 'vitest';
import { BridgeInvokeError, BridgeTimeoutError } from './bridgeClient';
import { presentError } from './errorPresentation';

describe('presentError', () => {
  it('maps a bridge code to admin guidance while preserving technical evidence', () => {
    const presentation = presentError(
      new BridgeInvokeError({
        code: 'AUTHENTICATION_FAILED',
        message: 'The remote logon failed.',
        details: 'Kerberos returned 0x52e.',
        requiredPrivilege: 'ADMINISTRATOR',
      }),
      { message: 'The print server could not be scanned.' },
    );

    expect(presentation.message).toBe('The print server could not be scanned.');
    expect(presentation.cause).toMatch(/credentials/i);
    expect(presentation.action).toMatch(/sign in|credentials/i);
    expect(presentation.technicalDetails).toContain('AUTHENTICATION_FAILED');
    expect(presentation.technicalDetails).toContain('The remote logon failed.');
    expect(presentation.technicalDetails).toContain('Kerberos returned 0x52e.');
    expect(presentation.technicalDetails).toContain('ADMINISTRATOR');
  });

  it('turns a client-side timeout into actionable guidance', () => {
    const presentation = presentError(new BridgeTimeoutError('inventory', 'scan', 30_000));

    expect(presentation.message).not.toContain('inventory/scan');
    expect(presentation.cause).toMatch(/time limit/i);
    expect(presentation.action).toMatch(/retry/i);
    expect(presentation.technicalDetails).toContain('inventory/scan');
  });
});
