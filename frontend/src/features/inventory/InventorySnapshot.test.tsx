import { describe, expect, it } from 'vitest';
import { formatLinkSpeed, formatSnapshotAge } from './InventorySnapshot';

describe('formatLinkSpeed', () => {
  it('formats plausible speeds', () => {
    expect(formatLinkSpeed(1_000_000_000)).toBe('1 Gbit/s');
    expect(formatLinkSpeed(2_500_000_000)).toBe('2.5 Gbit/s');
    expect(formatLinkSpeed(100_000_000)).toBe('100 Mbit/s');
  });

  it('treats WMI unknown sentinels as no value', () => {
    expect(formatLinkSpeed(9223372036854775807)).toBe('—');
    expect(formatLinkSpeed(0)).toBe('—');
    expect(formatLinkSpeed(null)).toBe('—');
  });
});

describe('formatSnapshotAge', () => {
  it('formats snapshot age without hiding the absolute capture time', () => {
    expect(formatSnapshotAge('2026-08-19T08:00:00Z', Date.parse('2026-08-19T10:30:00Z'))).toBe('2 h old');
    expect(formatSnapshotAge('invalid', Date.parse('2026-08-19T10:30:00Z'))).toBe('age unavailable');
  });
});
