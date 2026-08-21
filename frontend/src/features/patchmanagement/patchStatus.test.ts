import { describe, expect, it } from 'vitest';
import type { PatchPackageStatus, PatchWorkflowState } from '../../shared/api-types';
import {
  opsiConnectionStatus,
  patchAuditResultStatus,
  manufacturerCheckStatus,
  manufacturerSourcesStatus,
  packageApprovalStatus,
  patchPackageStatus,
  patchWorkflowStatus,
} from './patchStatus';

describe('opsiConnectionStatus', () => {
  it.each([
    [{ kind: 'loading' }, 'execution', 'running', 'opsi connection'],
    [{ kind: 'failed' }, 'execution', 'failed', 'Connection check'],
    [{ kind: 'loaded', status: { connected: true } }, 'availability', 'available', 'opsi connection'],
    [{ kind: 'loaded', status: { connected: false } }, 'availability', 'unknown', 'Not connected'],
    [{ kind: 'unavailable' }, 'availability', 'unknown', 'Connection status unavailable'],
  ] as const)(
    'maps state %# to %s/%s without guessing an unloaded connection result',
    (state, dimension, value, context) => {
      expect(opsiConnectionStatus(state)).toEqual({
        status: { dimension, value },
        context,
        technicalDetail: null,
      });
    },
  );
});

describe('patchAuditResultStatus', () => {
  it.each([
    ['SUCCESS', 'execution', 'succeeded', null],
    ['FAILED', 'execution', 'failed', null],
    ['PLANNED', 'execution', 'succeeded', 'Preview created'],
  ] as const)(
    'maps %s to %s/%s with explicit preview context where required',
    (rawResult, dimension, value, context) => {
      expect(patchAuditResultStatus(rawResult)).toEqual({
        status: { dimension, value },
        context,
        technicalDetail: null,
      });
    },
  );

  it('falls back to neutral Unknown without exposing a future raw value as the label', () => {
    expect(patchAuditResultStatus('FUTURE_RESULT')).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Audit result unavailable',
      technicalDetail: 'FUTURE_RESULT',
    });
  });
});

describe('patchPackageStatus', () => {
  it.each([
    ['CURRENT', 'lifecycle', 'current', null],
    ['UPDATE_AVAILABLE', 'lifecycle', 'update-available', null],
    ['DEPOT_DEVIATION', 'health', 'warning', 'Depot deviation'],
    ['MISSING_ON_DEPOT', 'availability', 'missing', 'From depot'],
    ['CHECK_FAILED', 'execution', 'failed', 'Package check'],
    ['DEPLOYMENT_PENDING', 'lifecycle', 'pending', 'Deployment'],
  ] satisfies Array<[PatchPackageStatus, string, string, string | null]>)(
    'maps %s to %s/%s with preserved context',
    (rawStatus, dimension, value, context) => {
      expect(patchPackageStatus(rawStatus)).toEqual({
        status: { dimension, value },
        context,
        technicalDetail: null,
      });
    },
  );

  it('falls back to neutral Unknown without exposing a future raw value as the label', () => {
    expect(patchPackageStatus('FUTURE_STATUS')).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Package status unavailable',
      technicalDetail: 'FUTURE_STATUS',
    });
  });
});

describe('patchWorkflowStatus', () => {
  it.each([
    ['DETECTED', 'lifecycle', 'pending', 'Detected'],
    ['UPDATE_AVAILABLE', 'lifecycle', 'update-available', null],
    ['DOWNLOAD_NEEDED', 'lifecycle', 'pending', 'Download required'],
    ['PACKAGE_PREPARED', 'lifecycle', 'pending', 'Package prepared'],
    ['UPLOADED', 'lifecycle', 'pending', 'Uploaded'],
    ['READY_FOR_PILOT', 'lifecycle', 'pending', 'Ready for pilot'],
    ['APPROVED', 'lifecycle', 'pending', 'Approved'],
    ['ROLLOUT_REQUESTED', 'lifecycle', 'pending', 'Deployment requested'],
    ['COMPLETED', 'lifecycle', 'current', null],
    ['FAILED', 'execution', 'failed', null],
  ] satisfies Array<[PatchWorkflowState, string, string, string | null]>)(
    'maps %s to %s/%s with preserved workflow context',
    (rawStatus, dimension, value, context) => {
      expect(patchWorkflowStatus(rawStatus)).toEqual({
        status: { dimension, value },
        context,
        technicalDetail: null,
      });
    },
  );

  it('falls back to neutral Unknown without exposing a future raw value as the label', () => {
    expect(patchWorkflowStatus('FUTURE_STATE')).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Workflow status unavailable',
      technicalDetail: 'FUTURE_STATE',
    });
  });
});

describe('manufacturerCheckStatus', () => {
  it.each([
    ['SUCCESS', true, 'execution', 'succeeded', null],
    ['FAILED', true, 'execution', 'failed', null],
    ['NOT_CHECKED', true, 'availability', 'unknown', 'Not checked'],
    ['NOT_CONFIGURED', true, 'availability', 'not-configured', null],
    ['SUCCESS', false, 'lifecycle', 'disabled', null],
  ] satisfies Array<[string, boolean, string, string, string | null]>) (
    'maps %s with enabled=%s to %s/%s',
    (rawStatus, enabled, dimension, value, context) => {
      expect(manufacturerCheckStatus(rawStatus, enabled)).toEqual({
        status: { dimension, value },
        context,
        technicalDetail: null,
      });
    },
  );

  it('falls back to neutral Unknown without exposing a future raw value as the label', () => {
    expect(manufacturerCheckStatus('FUTURE_STATUS')).toEqual({
      status: { dimension: 'availability', value: 'unknown' },
      context: 'Check status unavailable',
      technicalDetail: 'FUTURE_STATUS',
    });
  });
});

describe('manufacturerSourcesStatus', () => {
  it('counts only enabled sources as active and distinguishes configured disabled sources', () => {
    expect(manufacturerSourcesStatus([{ enabled: true }, { enabled: false }], false)).toEqual({
      status: { dimension: 'availability', value: 'available' },
      context: '1 active',
      technicalDetail: null,
    });
    expect(manufacturerSourcesStatus([{ enabled: false }], false)).toEqual({
      status: { dimension: 'lifecycle', value: 'disabled' },
      context: '1 configured',
      technicalDetail: null,
    });
  });

  it('distinguishes a load failure from an empty configuration', () => {
    expect(manufacturerSourcesStatus([], true)).toEqual({
      status: { dimension: 'execution', value: 'failed' },
      context: 'Sources unavailable',
      technicalDetail: null,
    });
    expect(manufacturerSourcesStatus([], false)).toEqual({
      status: { dimension: 'availability', value: 'not-configured' },
      context: null,
      technicalDetail: null,
    });
  });
});

describe('packageApprovalStatus', () => {
  it.each([
    [{ kind: 'loading' }, 'execution', 'running', 'Approval status'],
    [{ kind: 'failed' }, 'execution', 'failed', 'Approval status'],
    [{ kind: 'unavailable' }, 'availability', 'unknown', 'Approval status unavailable'],
    [{
      kind: 'loaded',
      workflow: { pilotApproved: true, testUpdateSucceededAtUtc: '2026-07-03T12:03:00Z' },
    }, 'execution', 'succeeded', 'Pilot approved'],
    [{
      kind: 'loaded',
      workflow: { pilotApproved: false, testUpdateSucceededAtUtc: '2026-07-03T12:03:00Z' },
    }, 'execution', 'succeeded', 'Test update'],
    [{
      kind: 'loaded',
      workflow: { pilotApproved: false, testUpdateSucceededAtUtc: null },
    }, 'lifecycle', 'pending', 'Test update'],
  ] as const)(
    'maps state %# to %s/%s with explicit context',
    (state, dimension, value, context) => {
      expect(packageApprovalStatus(state)).toEqual({
        status: { dimension, value },
        context,
        technicalDetail: null,
      });
    },
  );
});
