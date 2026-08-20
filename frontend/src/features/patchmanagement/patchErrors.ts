import { BridgeInvokeError } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';

/** What the admin should do next, per typed opsi error. */
const opsiErrorHints: Record<string, string> = {
  AUTHENTICATION_FAILED: 'opsi rejected the credentials — check user name and password.',
  ACCESS_DENIED: 'The user authenticated but lacks rights — it must be in the opsi admin group (opsiadmin).',
  SERVICE_UNAVAILABLE:
    'opsiconfd did not answer — check the service URL (usually https://<server>:4447), the service state '
    + "and the firewall. For opsi's self-signed CA, tick “Trust server certificate”.",
  DNS_RESOLUTION_FAILED: 'The server name does not resolve — check DNS or use the IP address.',
  CONNECTION_TIMEOUT: 'The opsi service did not answer in time — check the network path.',
  REMOTE_COMMAND_FAILED: 'opsi accepted the connection but the call failed — see the details.',
};

export function presentOpsiError(error: unknown, message: string): ErrorPresentation {
  const action = error instanceof BridgeInvokeError ? opsiErrorHints[error.error.code] : undefined;
  return presentError(error, { message, action });
}
