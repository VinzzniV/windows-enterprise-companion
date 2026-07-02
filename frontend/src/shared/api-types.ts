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
