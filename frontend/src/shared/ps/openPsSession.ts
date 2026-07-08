import { invoke } from '../bridge/bridgeClient';
import type { CredentialValues } from '../targets/TargetSelector';

export interface OpenPsSessionResult {
  launched: boolean;
}

/**
 * Opens an interactive PowerShell remoting session against a host in a new
 * console window (ADR 0011). Reuses the global admin sign-in when present; the
 * password never touches the command line, disk or the log — it is handed to
 * the child process via a one-shot environment variable.
 */
export function openPsSession(
  host: string,
  admin: CredentialValues | null,
): Promise<OpenPsSessionResult> {
  return invoke<OpenPsSessionResult>(
    'system',
    'openPsSession',
    {
      host,
      userName: admin?.userName ?? null,
      domain: admin?.domain ?? null,
      password: admin?.password ?? null,
    },
    30_000,
  );
}
