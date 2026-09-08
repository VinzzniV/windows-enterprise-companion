import { describe, expect, it } from 'vitest';
import { presentSourceError } from './errorPresentation';

describe('source error presentation', () => {
  it('keeps certificate diagnostics separate from actionable trust guidance', () => {
    const details = 'The TLS certificate could not be validated. Technical detail: RemoteCertificateValidationCallback rejected it.';
    const result = presentSourceError(details);
    expect(result.message).toBe('The server certificate could not be verified.');
    expect(result.action).toContain('Verify the server certificate');
    expect(result.technicalDetails).toBe(details);
    expect(result.message).not.toContain('RemoteCertificateValidationCallback');
  });

  it('preserves an unfamiliar provider failure without guessing a certificate problem', () => {
    const result = presentSourceError('Provider returned unexpected response 503.');
    expect(result.message).toBe('The source could not be evaluated.');
    expect(result.technicalDetails).toContain('503');
  });
});
