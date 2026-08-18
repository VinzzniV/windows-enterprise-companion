import { describe, expect, it } from 'vitest';
import { compareSortKeys, type SortKey } from './sort';

const sorted = (keys: SortKey[], dir: 'asc' | 'desc') =>
  [...keys].sort((a, b) => compareSortKeys(a, b, dir));

describe('compareSortKeys', () => {
  it('sorts numbers numerically, not lexically', () => {
    expect(sorted([10, 2, 1], 'asc')).toEqual([1, 2, 10]);
  });

  it('sorts numeric-aware strings and is case-insensitive', () => {
    expect(sorted(['IP 10', 'IP 2', 'ip 1'], 'asc')).toEqual(['ip 1', 'IP 2', 'IP 10']);
  });

  it('keeps nulls last regardless of direction', () => {
    expect(sorted(['b', null, 'a'], 'asc')).toEqual(['a', 'b', null]);
    expect(sorted(['b', null, 'a'], 'desc')).toEqual(['b', 'a', null]);
  });
});
