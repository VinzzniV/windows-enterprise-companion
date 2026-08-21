import { describe, expect, it } from 'vitest';
// Vitest executes in Node; the production application intentionally omits Node typings.
// @ts-expect-error Node's built-in module is available in the test runtime.
import { readFileSync } from 'node:fs';
// @ts-expect-error Node's built-in module is available in the test runtime.
import { resolve } from 'node:path';

function hexRgb(hex: string): [number, number, number] {
  const value = hex.replace('#', '');
  return [0, 2, 4].map((offset) => Number.parseInt(value.slice(offset, offset + 2), 16)) as [number, number, number];
}

function relativeLuminance(hex: string): number {
  const channels = hexRgb(hex).map((channel) => {
    const normalized = channel / 255;
    return normalized <= 0.04045
      ? normalized / 12.92
      : ((normalized + 0.055) / 1.055) ** 2.4;
  });
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

function contrastRatio(foreground: string, background: string): number {
  const lighter = Math.max(relativeLuminance(foreground), relativeLuminance(background));
  const darker = Math.min(relativeLuminance(foreground), relativeLuminance(background));
  return (lighter + 0.05) / (darker + 0.05);
}

describe('text contrast tokens', () => {
  it('keeps muted helper text AA-readable on page and card backgrounds', () => {
    const designTokens = readFileSync(resolve('src/index.css'), 'utf8');
    const muted = /--color-muted:\s*(#[\da-f]{6})/i.exec(designTokens)?.[1];

    expect(muted).toBeDefined();
    expect(contrastRatio(muted!, '#020617')).toBeGreaterThanOrEqual(4.5); // slate-950 page
    expect(contrastRatio(muted!, '#0f172a')).toBeGreaterThanOrEqual(4.5); // slate-900 card
  });
});
