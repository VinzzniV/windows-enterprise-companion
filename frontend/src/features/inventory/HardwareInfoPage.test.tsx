import { describe, expect, it, vi } from 'vitest';
import { formatLinkSpeed } from './HardwareInfoPage';

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: vi.fn(),
  BridgeInvokeError: class extends Error {},
}));

describe('formatLinkSpeed', () => {
  it('formats plausible speeds', () => {
    expect(formatLinkSpeed(1_000_000_000)).toBe('1 Gbit/s');
    expect(formatLinkSpeed(2_500_000_000)).toBe('2.5 Gbit/s');
    expect(formatLinkSpeed(100_000_000)).toBe('100 Mbit/s');
  });

  it('treats WMI unknown sentinels as no value', () => {
    // Int64.MaxValue bit/s is what Win32_NetworkAdapter reports for unknown links
    expect(formatLinkSpeed(9223372036854775807)).toBe('—');
    expect(formatLinkSpeed(0)).toBe('—');
    expect(formatLinkSpeed(null)).toBe('—');
  });
});
