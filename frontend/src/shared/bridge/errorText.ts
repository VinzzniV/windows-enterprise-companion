import { BridgeInvokeError } from './bridgeClient';

/** Human-readable one-liner for a failed bridge call (code: message [details]). */
export function errorText(error: unknown): string {
  if (error instanceof BridgeInvokeError) {
    const details = error.error.details ? ` ${error.error.details}` : '';
    return `${error.error.code}: ${error.error.message}${details}`;
  }
  return error instanceof Error ? error.message : String(error);
}
