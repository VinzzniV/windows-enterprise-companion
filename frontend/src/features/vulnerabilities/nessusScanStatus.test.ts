import { describe, expect, it } from 'vitest';
import { nessusScanStatus } from './nessusScanStatus';

const documentedCases = [
  ['aborted', 'execution', 'failed', 'Aborted · Partial results possible'],
  ['canceled', 'execution', 'partial', 'Canceled'],
  [' COMPLETED ', 'execution', 'succeeded', null],
  ['empty', 'availability', 'missing', 'Never run'],
  ['imported', 'availability', 'available', 'Imported scan'],
  ['initializing', 'execution', 'running', 'Initializing'],
  ['pausing', 'execution', 'running', 'Pausing'],
  ['paused', 'lifecycle', 'pending', 'Paused'],
  ['pending', 'lifecycle', 'pending', 'Waiting for scanner'],
  ['processing', 'execution', 'running', 'Processing results'],
  ['publishing', 'execution', 'running', 'Publishing results'],
  ['resuming', 'execution', 'running', 'Resuming'],
  ['running', 'execution', 'running', 'Scan'],
  ['stopped', 'execution', 'running', 'Stopping'],
  ['stopping', 'execution', 'running', 'Stopping'],
] as const;

describe('Nessus scan status', () => {
  it.each(documentedCases)('maps documented provider value %s to %s/%s', (raw, dimension, value, context) => {
    expect(nessusScanStatus(raw, null)).toEqual({
      status: { dimension, value },
      context,
      technicalDetail: `Provider status: ${raw.trim()}`,
    });
  });

  it('prioritizes a persisted scan error and retains both raw details', () => {
    expect(nessusScanStatus('running', 'provider export failed')).toEqual({
      status: { dimension: 'execution', value: 'failed' },
      context: 'Scan status',
      technicalDetail: 'provider export failed\nProvider status: running',
    });
  });

  it.each([null, '   '])('maps absent provider value %j to a safe unknown state', (raw) => {
    expect(nessusScanStatus(raw, null)).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Status unavailable',
      technicalDetail: null,
    });
  });

  it('keeps an unknown future provider value out of the primary label', () => {
    expect(nessusScanStatus('future_state', null)).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Unmapped scan status',
      technicalDetail: 'Provider status: future_state',
    });
  });
});
