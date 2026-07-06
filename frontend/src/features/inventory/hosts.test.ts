import { describe, expect, it } from 'vitest';
import { hostsToRestore } from './hosts';

describe('hostsToRestore', () => {
  it('drops the local machine (stored under its machine name) to avoid a duplicate', () => {
    expect(hostsToRestore(['DESKTOP-1', 'PC2', 'PC3'], 'desktop-1')).toEqual(['PC2', 'PC3']);
  });

  it('drops a literal LOCAL entry as well', () => {
    expect(hostsToRestore(['LOCAL', 'PC2'], 'DESKTOP-1')).toEqual(['PC2']);
  });

  it('matches the machine name case-insensitively', () => {
    expect(hostsToRestore(['desktop-1'], 'DESKTOP-1')).toEqual([]);
  });

  it('keeps everything when the local machine name is unknown', () => {
    expect(hostsToRestore(['PC1', 'PC2'], null)).toEqual(['PC1', 'PC2']);
    expect(hostsToRestore(['PC1', 'PC2'], '')).toEqual(['PC1', 'PC2']);
  });
});
