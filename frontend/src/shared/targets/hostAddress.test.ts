import { describe, expect, it } from 'vitest';
import { hostAddressKey, isExactLocalName } from './hostAddress';

describe('host addresses', () => {
  it.each([
    [' pc01.site-a.example. ', 'PC01.SITE-A.EXAMPLE'],
    ['10.1.2.3', '10.1.2.3'],
    ['10.8.9.10', '10.8.9.10'],
    ['2001:0db8:0000:0000:0000:0000:0000:0001', '2001:DB8::1'],
    ['[2001:db8::1]', '2001:DB8::1'],
  ])('retains the full address %s', (input, expected) => {
    expect(hostAddressKey(input)).toBe(expected);
  });

  it('does not select the local machine from a namesake in another domain', () => {
    expect(isExactLocalName('PC01.b.example', 'PC01')).toBe(false);
    expect(isExactLocalName('PC01', null)).toBe(false);
    expect(isExactLocalName('PC01', '')).toBe(false);
    expect(isExactLocalName(' pc01 ', 'PC01')).toBe(true);
  });
});
