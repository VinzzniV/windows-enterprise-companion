import {
  BridgeCancelledError,
  BridgeInvokeError,
  BridgeTimeoutError,
  BridgeUnavailableError,
} from './bridgeClient';

export interface ErrorPresentation {
  message: string;
  cause: string;
  action: string;
  technicalDetails: string;
}

export type ErrorPresentationContext = Partial<Pick<ErrorPresentation, 'message' | 'cause' | 'action'>>;

type AdminGuidance = Omit<ErrorPresentation, 'technicalDetails'>;

const guidanceByCode: Record<string, AdminGuidance> = {
  INTERNAL_ERROR: {
    message: 'The application could not complete the operation.',
    cause: 'An unexpected internal error occurred.',
    action: 'Retry once. If the problem persists, open the error log and share the technical details.',
  },
  ACCESS_DENIED: {
    message: 'The operation was blocked by Windows permissions.',
    cause: 'The current account does not have the required access on the target.',
    action: 'Sign in with an authorized administrator account and retry.',
  },
  NOT_FOUND: {
    message: 'The requested item could not be found.',
    cause: 'The target, file, or saved record no longer exists at the expected location.',
    action: 'Check the target and configuration, refresh the view, and retry.',
  },
  WMI_UNAVAILABLE: {
    message: 'Windows management data could not be read.',
    cause: 'Windows Management Instrumentation (WMI) was unavailable or rejected the query.',
    action: 'Retry the scan. If it still fails, verify WMI health and permissions on the target.',
  },
  INVALID_REQUEST: {
    message: 'The operation could not be started with the current input.',
    cause: 'A required value is missing or invalid.',
    action: 'Review the entered values and configuration, then retry.',
  },
  UNKNOWN_ACTION: {
    message: 'This operation is not available in the current application version.',
    cause: 'The frontend requested an action the host does not recognize.',
    action: 'Restart the application. If the problem persists, update or repair the installation.',
  },
  NETWORK_PROBE_FAILED: {
    message: 'The target could not be reached over the network.',
    cause: 'The network probe failed before the requested data could be collected.',
    action: 'Check the target name, network route, and firewall rules, then retry.',
  },
  EVENT_LOG_UNAVAILABLE: {
    message: 'The Windows event log could not be read.',
    cause: 'The event-log service, log, or remote permissions were unavailable.',
    action: 'Verify the Event Log service and administrator access on the target, then retry.',
  },
  FILE_WRITE_FAILED: {
    message: 'The file could not be saved.',
    cause: 'The destination is unavailable, read-only, or not writable by the current account.',
    action: 'Choose a writable destination or correct its permissions, then retry.',
  },
  DIRECTORY_UNAVAILABLE: {
    message: 'The directory service could not be reached.',
    cause: 'Active Directory or the configured directory endpoint did not respond.',
    action: 'Check the directory connection, DNS, and credentials, then retry.',
  },
  DNS_RESOLUTION_FAILED: {
    message: 'The target name could not be resolved.',
    cause: 'DNS did not return an address for the supplied host name.',
    action: 'Check the host name and DNS connectivity, then retry.',
  },
  CONNECTION_TIMEOUT: {
    message: 'The target did not respond in time.',
    cause: 'The connection exceeded its time limit before the operation completed.',
    action: 'Check target availability and network connectivity, then retry.',
  },
  AUTHENTICATION_FAILED: {
    message: 'The target rejected the sign-in attempt.',
    cause: 'The supplied credentials were not accepted by the target.',
    action: 'Check the admin credentials, sign in again, and retry.',
  },
  WIN_RM_UNAVAILABLE: {
    message: 'Remote Windows management is unavailable.',
    cause: 'WinRM is disabled, unreachable, or not configured for this connection.',
    action: 'Verify WinRM on the target and the relevant firewall and trust settings, then retry.',
  },
  UNSUPPORTED_REMOTE_OPERATION: {
    message: 'This check is not supported for a remote target.',
    cause: 'The required Windows API can only run locally on the selected device.',
    action: 'Run the check on the device itself or choose a supported remote check.',
  },
  SERVICE_UNAVAILABLE: {
    message: 'A required service is currently unavailable.',
    cause: 'The configured service or local dependency could not be reached or started.',
    action: 'Check the service configuration and availability, then retry.',
  },
  REMOTE_COMMAND_FAILED: {
    message: 'The remote system could not complete the command.',
    cause: 'The target accepted the connection but the requested command failed.',
    action: 'Check the target state and permissions, review the technical details, and retry.',
  },
};

function technicalBridgeDetails(error: BridgeInvokeError): string {
  const lines = [`Code: ${error.error.code}`, `Message: ${error.error.message}`];
  if (error.error.details) lines.push(`Details: ${error.error.details}`);
  if (error.error.requiredPrivilege) lines.push(`Required privilege: ${error.error.requiredPrivilege}`);
  return lines.join('\n');
}

function withContext(
  guidance: AdminGuidance,
  technicalDetails: string,
  context: ErrorPresentationContext,
): ErrorPresentation {
  return { ...guidance, ...context, technicalDetails };
}

/** Maps transport failures to administrator guidance without discarding diagnostics. */
export function presentError(error: unknown, context: ErrorPresentationContext = {}): ErrorPresentation {
  if (error instanceof BridgeInvokeError) {
    const guidance = guidanceByCode[error.error.code] ?? guidanceByCode.INTERNAL_ERROR;
    return withContext(guidance, technicalBridgeDetails(error), context);
  }

  if (error instanceof BridgeTimeoutError) {
    return withContext(
      guidanceByCode.CONNECTION_TIMEOUT,
      `${error.name}: ${error.message}`,
      context,
    );
  }

  if (error instanceof BridgeUnavailableError) {
    return withContext(
      {
        message: 'The desktop host is not available.',
        cause: 'The user interface cannot communicate with the Windows application host.',
        action: 'Close this view and start Windows Enterprise Companion again.',
      },
      `${error.name}: ${error.message}`,
      context,
    );
  }

  if (error instanceof BridgeCancelledError) {
    return withContext(
      {
        message: 'The operation was cancelled.',
        cause: 'The active request was stopped before it completed.',
        action: 'Start the operation again if it is still needed.',
      },
      `${error.name}: ${error.message}`,
      context,
    );
  }

  const technicalDetails = error instanceof Error
    ? `${error.name}: ${error.message}`
    : `Thrown value: ${String(error)}`;
  return withContext(
    {
      message: 'The operation could not be completed.',
      cause: 'An unexpected error occurred in the user interface.',
      action: 'Retry once. If the problem persists, open the error log and share the technical details.',
    },
    technicalDetails,
    context,
  );
}
