export type SortKey = string | number | null;

/**
 * Compare two sort keys for click-to-sort tables: missing values (null) always
 * sort last regardless of direction, numbers compare numerically, strings
 * numeric-aware and case-insensitive (so "IP 10" sorts after "IP 9").
 */
export function compareSortKeys(a: SortKey, b: SortKey, direction: 'asc' | 'desc'): number {
  if (a == null && b == null) return 0;
  if (a == null) return 1;
  if (b == null) return -1;
  const base =
    typeof a === 'number' && typeof b === 'number'
      ? a - b
      : String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
  return direction === 'asc' ? base : -base;
}
