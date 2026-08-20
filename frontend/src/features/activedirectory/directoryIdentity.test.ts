import { describe, expect, it } from 'vitest';
import {
  directoryAccountStatus,
  directoryEntityTypeForRule,
  parseDistinguishedName,
} from './directoryIdentity';

describe('directory identity presentation', () => {
  it('turns an escaped common name and nested DN into a readable identity path', () => {
    expect(parseDistinguishedName('CN=Doe\\, Jane,OU=Privileged,OU=Users,DC=corp,DC=example,DC=com')).toEqual({
      commonName: 'Doe, Jane',
      path: 'corp.example.com / Users / Privileged',
    });
  });

  it('decodes escaped separators without treating them as DN boundaries', () => {
    expect(parseDistinguishedName('CN=Service\\=Reader,OU=Apps\\2C Tier 1,DC=corp,DC=local')).toEqual({
      commonName: 'Service=Reader',
      path: 'corp.local / Apps, Tier 1',
    });
  });

  it('derives honest entity types from the stable hygiene rule identity', () => {
    expect(directoryEntityTypeForRule('WEC-AD-INACTIVE-COMPUTERS')).toBe('Computer');
    expect(directoryEntityTypeForRule('WEC-AD-INACTIVE-USERS')).toBe('User');
    expect(directoryEntityTypeForRule('WEC-AD-DISABLED-PRIVILEGED')).toBe('User');
  });

  it.each([
    ['Enabled', { dimension: 'lifecycle', value: 'current' }, 'Account enabled'],
    ['Disabled', { dimension: 'lifecycle', value: 'disabled' }, 'Account disabled'],
    ['Unknown', { dimension: 'availability', value: 'unknown' }, 'Account status unavailable'],
  ] as const)('maps the documented %s account status to its semantic dimension', (raw, status, context) => {
    expect(directoryAccountStatus(raw)).toEqual({
      status,
      context,
      technicalDetail: `Directory account status: ${raw}`,
    });
  });

  it('treats a non-account directory object as not applicable', () => {
    expect(directoryAccountStatus(null)).toEqual({
      status: { dimension: 'availability', value: 'not-applicable' },
      context: 'No account lifecycle state',
      technicalDetail: null,
    });
  });

  it('normalizes case and whitespace without losing the original technical value', () => {
    expect(directoryAccountStatus('  enabled  ')).toEqual({
      status: { dimension: 'lifecycle', value: 'current' },
      context: 'Account enabled',
      technicalDetail: 'Directory account status: enabled',
    });
  });

  it('falls back safely for a future account status', () => {
    expect(directoryAccountStatus('LOCKED_OUT')).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Unmapped account status',
      technicalDetail: 'Directory account status: LOCKED_OUT',
    });
  });
});
