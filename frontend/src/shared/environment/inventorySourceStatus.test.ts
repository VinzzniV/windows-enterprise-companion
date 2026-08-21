import { describe, expect, it } from 'vitest';
import type { HygieneSourceProgressStatus, InventorySourceAvailability } from '../api-types';
import { hygieneSourceProgressStatus, inventorySourceStatus } from './inventorySourceStatus';

const inventoryCases: Array<[InventorySourceAvailability, string, string, string | null]> = [
  ['AVAILABLE', 'availability', 'available', null],
  ['NOT_CONNECTED', 'availability', 'not-configured', 'Not connected'],
  ['UNAVAILABLE', 'execution', 'failed', 'Source unavailable'],
  ['TRUNCATED', 'execution', 'partial', 'Result truncated'],
  ['PARTIAL', 'execution', 'partial', null],
];

const progressCases: Array<[HygieneSourceProgressStatus, string, string]> = [
  ['RUNNING', 'execution', 'running'],
  ['AVAILABLE', 'availability', 'available'],
  ['PARTIAL', 'execution', 'partial'],
  ['NOT_CONNECTED', 'availability', 'not-configured'],
  ['UNAVAILABLE', 'execution', 'failed'],
  ['TRUNCATED', 'execution', 'partial'],
];

describe('inventory source semantic status', () => {
  it.each(inventoryCases)('maps %s without collapsing its source meaning', (availability, dimension, value, context) => {
    expect(inventorySourceStatus(availability)).toEqual({
      status: { dimension, value },
      context,
    });
  });

  it.each(progressCases)('maps progress %s to %s/%s', (progress, dimension, value) => {
    expect(hygieneSourceProgressStatus(progress).status).toEqual({ dimension, value });
  });
});
