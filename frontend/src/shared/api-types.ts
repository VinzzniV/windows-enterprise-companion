/**
 * Mirrors the C# DTOs crossing the bridge (manual sync, architecture plan §5).
 * C# sources of truth are noted per type — review on every DTO change.
 */

/** Wec.Host.Bridge.PingResponse */
export interface PingResponse {
  message: string;
  timestamp: string;
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
  | 'UNKNOWN_ACTION';

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
