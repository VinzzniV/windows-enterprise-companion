const DEFAULT_RESPONSE_TIMEOUT_MS = 10_000;
const STANDARD_OPERATION_TIMEOUT_MS = 120_000;
const ENVIRONMENT_ANALYSIS_TIMEOUT_MS = 180_000;
const BATCH_OPERATION_TIMEOUT_MS = 600_000;
const LOG_READ_TIMEOUT_MS = 30_000;
const PACKAGE_TARGET_TIMEOUT_MS = 1_900_000;

const standardOperations = new Set([
  'activedirectory/getHygiene',
  'activedirectory/getOverview',
  'activedirectory/searchComputers',
  'activedirectory/searchUsers',
  'activedirectory/testConnection',
  'connectivity/probeHosts',
  'diagnostics/queryEventLog',
  'diagnostics/runDiagnostics',
  'inventory/getDiskEncryptionStatus',
  'inventory/getHardwareInfo',
  'patchmanagement/checkVendorVersions',
  'patchmanagement/getDashboard',
  'patchmanagement/getRolloutPreview',
  'patchmanagement/requestRollout',
  'printmanagement/scanClientPrinters',
  'reporting/exportHtml',
  'reporting/exportJson',
  'security/runScan',
  'system/restartElevated',
  'vulnerabilitymanagement/saveCredential',
]);

const batchOperations = new Set([
  'networkscan/scan',
  'security/runBatchScan',
]);

function targetCount(payload: unknown): number {
  if (typeof payload !== 'object' || payload === null) return 1;
  const depotIds = (payload as { depotIds?: unknown }).depotIds;
  return Array.isArray(depotIds) ? Math.max(1, depotIds.length) : 1;
}

/**
 * One frontend lifetime policy for every bridge action. Backend execution
 * limits are deliberately a few seconds shorter so a typed timeout response
 * reaches the UI before this correlation guard expires.
 */
export function bridgeResponseTimeoutMs(module: string, action: string, payload?: unknown): number {
  const key = `${module}/${action}`;
  if (key === 'patchmanagement/executePackageUpdate') {
    return targetCount(payload) * PACKAGE_TARGET_TIMEOUT_MS;
  }
  if (key === 'employeelifecycle/getHygiene') return ENVIRONMENT_ANALYSIS_TIMEOUT_MS;
  if (key === 'logs/recent' || key === 'system/openPsSession') return LOG_READ_TIMEOUT_MS;
  if (batchOperations.has(key)) return BATCH_OPERATION_TIMEOUT_MS;
  if (standardOperations.has(key)) return STANDARD_OPERATION_TIMEOUT_MS;
  return DEFAULT_RESPONSE_TIMEOUT_MS;
}
