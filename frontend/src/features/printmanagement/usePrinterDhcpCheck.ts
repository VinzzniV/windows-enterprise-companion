import { useCallback, useEffect, useState } from 'react';
import type {
  DhcpCheckResult,
  DhcpReservationInfo,
  TargetRequest,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';

export interface PrinterDhcpTarget {
  deviceIp: string | null;
}

export interface PrinterDhcpCheckState {
  server: string;
  checkedIps: ReadonlySet<string>;
  reservations: Readonly<Record<string, DhcpReservationInfo>>;
}

interface UsePrinterDhcpCheckOptions {
  configuredServer: string | null;
  toServerRequest: (host: string) => TargetRequest;
}

const EMPTY_CHECKED_IPS: ReadonlySet<string> = new Set();
const EMPTY_RESERVATIONS: Readonly<Record<string, DhcpReservationInfo>> = {};

export function usePrinterDhcpCheck({
  configuredServer,
  toServerRequest,
}: UsePrinterDhcpCheckOptions) {
  const [server, setServerState] = useState('');
  const [result, setResult] = useState<PrinterDhcpCheckState | null>(null);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!configuredServer) return;
    setServerState((current) => current.trim() === '' ? configuredServer : current);
  }, [configuredServer]);

  const setServer = useCallback((value: string) => {
    setServerState(value);
    setResult(null);
    setError(null);
  }, []);

  const check = useCallback(
    (devices: readonly PrinterDhcpTarget[]) => {
      const requestedServer = server.trim();
      if (requestedServer === '') {
        setError('Enter a DHCP server.');
        return;
      }

      const ips = [...new Set(
        devices.map((printer) => printer.deviceIp).filter((ip): ip is string => !!ip),
      )];
      if (ips.length === 0) {
        setError('No resolved IP addresses are available. Rescan first.');
        return;
      }

      setChecking(true);
      setResult(null);
      setError(null);
      void invoke<DhcpCheckResult>(
        'printmanagement',
        'checkDhcp',
        { target: toServerRequest(requestedServer), ips },
        60_000,
      )
        .then((response) => {
          const reservations: Record<string, DhcpReservationInfo> = {};
          for (const entry of response.reserved) reservations[entry.ip] = entry;
          setResult({
            server: requestedServer,
            checkedIps: new Set(ips),
            reservations,
          });
        })
        .catch((reason: unknown) => {
          setResult(null);
          setError(errorText(reason));
        })
        .finally(() => setChecking(false));
    },
    [server, toServerRequest],
  );

  return {
    server,
    setServer,
    result,
    checking,
    error,
    checkedIps: result?.checkedIps ?? EMPTY_CHECKED_IPS,
    reservations: result?.reservations ?? EMPTY_RESERVATIONS,
    check,
  };
}
