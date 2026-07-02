/**
 * Mirrors the C# DTOs crossing the bridge (manual sync, architecture plan §5).
 * C# sources of truth are noted per type — review on every DTO change.
 */

/** Wec.Host.Bridge.PingResponse */
export interface PingResponse {
  message: string;
  timestamp: string;
}

/** Wec.Host.Bridge.AppInfoResponse */
export interface AppInfoResponse {
  version: string;
  databasePath: string;
  logDirectory: string;
  isElevated: boolean;
}

/** Wec.Core.Privileges.PrivilegeLevel (SCREAMING_SNAKE on the wire, ADR 0003) */
export type PrivilegeLevel = 'STANDARD_USER' | 'ADMINISTRATOR';

/** Wec.Core.Results.ErrorCode (SCREAMING_SNAKE on the wire, ADR 0003) */
export type ErrorCode =
  | 'INTERNAL_ERROR'
  | 'ACCESS_DENIED'
  | 'NOT_FOUND'
  | 'WMI_UNAVAILABLE'
  | 'INVALID_REQUEST'
  | 'UNKNOWN_ACTION'
  | 'NETWORK_PROBE_FAILED'
  | 'EVENT_LOG_UNAVAILABLE';

/** Wec.Modules.Inventory.Handlers.GetHardwareInfoRequest */
export interface GetHardwareInfoRequest {
  forceRefresh?: boolean;
}

/** Wec.Modules.Inventory.Domain.CpuInfo */
export interface CpuInfo {
  name: string;
  physicalCores: number;
  logicalProcessors: number;
  maxClockSpeedMhz: number;
}

/** Wec.Modules.Inventory.Domain.MemoryBank */
export interface MemoryBank {
  manufacturer: string | null;
  partNumber: string | null;
  capacityBytes: number;
  speedMtps: number | null;
}

/** Wec.Modules.Inventory.Domain.DiskDrive */
export interface DiskDrive {
  model: string;
  sizeBytes: number;
  interfaceType: string | null;
  mediaType: string | null;
}

/** Wec.Modules.Inventory.Domain.OperatingSystemInfo */
export interface OperatingSystemInfo {
  caption: string;
  version: string;
  buildNumber: string;
  architecture: string | null;
}

/** Wec.Modules.Inventory.Domain.HardwareSnapshot */
export interface HardwareSnapshot {
  cpu: CpuInfo;
  memoryBanks: MemoryBank[];
  disks: DiskDrive[];
  operatingSystem: OperatingSystemInfo;
}

/** Wec.Modules.Inventory.Application.HardwareInfoResult */
export interface HardwareInfoResult {
  snapshot: HardwareSnapshot;
  capturedAtUtc: string;
  fromCache: boolean;
}

/** Wec.Modules.Inventory.Domain.VolumeProtectionStatus (SCREAMING_SNAKE on the wire) */
export type VolumeProtectionStatus = 'UNPROTECTED' | 'PROTECTED' | 'UNKNOWN';

/** Wec.Modules.Inventory.Domain.EncryptableVolume */
export interface EncryptableVolume {
  driveLetter: string | null;
  protectionStatus: VolumeProtectionStatus;
}

/** Wec.Modules.Inventory.Application.DiskEncryptionStatus */
export interface DiskEncryptionStatus {
  volumes: EncryptableVolume[];
}

/** Wec.Modules.Security.Domain.FindingSeverity (SCREAMING_SNAKE on the wire) */
export type FindingSeverity = 'INFO' | 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL';

/** Wec.Modules.Security.Domain.FindingCategory (SCREAMING_SNAKE on the wire) */
export type FindingCategory =
  | 'FIREWALL'
  | 'MALWARE_PROTECTION'
  | 'NETWORK_SERVICES'
  | 'ENCRYPTION'
  | 'PLATFORM_INTEGRITY'
  | 'ACCOUNTS'
  | 'OPERATING_SYSTEM';

/** Wec.Modules.Security.Domain.ScanStatus (SCREAMING_SNAKE on the wire) */
export type ScanStatus = 'COMPLETED' | 'COMPLETED_WITH_ERRORS' | 'FAILED';

/** Wec.Modules.Security.Domain.SecurityFinding */
export interface SecurityFinding {
  findingId: string;
  title: string;
  description: string;
  severity: FindingSeverity;
  category: FindingCategory;
  affectedResource: string;
  evidence: Record<string, string>;
  recommendation: string;
  requiredPrivilege: PrivilegeLevel | null;
  capturedAtUtc: string;
}

/** Wec.Modules.Security.Domain.SecurityScanResult */
export interface SecurityScanResult {
  scanId: number;
  startedAtUtc: string;
  completedAtUtc: string;
  status: ScanStatus;
  findings: SecurityFinding[];
}

/** Wec.Modules.Security.Application.LatestScanResult */
export interface LatestScanResult {
  scan: SecurityScanResult | null;
}

/** Wec.Modules.Diagnostics.Domain.DiagnosticStatus (SCREAMING_SNAKE on the wire) */
export type DiagnosticStatus = 'PASS' | 'WARNING' | 'FAIL' | 'NOT_RUN';

/** Wec.Modules.Diagnostics.Domain.DiagnosticCategory (SCREAMING_SNAKE on the wire) */
export type DiagnosticCategory =
  | 'NETWORK'
  | 'DOMAIN'
  | 'TIME_SYNCHRONIZATION'
  | 'EVENT_LOG'
  | 'SERVICES';

/** Wec.Modules.Diagnostics.Domain.DiagnosticResult */
export interface DiagnosticResult {
  diagnosticId: string;
  title: string;
  status: DiagnosticStatus;
  category: DiagnosticCategory;
  affectedResource: string;
  evidence: Record<string, string>;
  suggestedNextSteps: string[];
  requiredPrivilege: PrivilegeLevel | null;
  capturedAtUtc: string;
}

/** Wec.Modules.Diagnostics.Domain.DiagnosticRunResult */
export interface DiagnosticRunResult {
  startedAtUtc: string;
  completedAtUtc: string;
  results: DiagnosticResult[];
}
