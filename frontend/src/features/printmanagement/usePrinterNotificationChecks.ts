import { useCallback, useState } from 'react';
import type { PrinterNotificationCheck } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { runWithConcurrencyLimit } from '../../shared/concurrency';
import { siteOf } from './printers';

export interface PrinterNotificationTarget {
  name: string;
  deviceAddress: string | null;
}

// Notification checks are lightweight HTTPS requests (no SNMP), so the fleet
// can use a wider pool than the print-server scan workflow.
const NOTIFICATION_CONCURRENCY = 16;

export function usePrinterNotificationChecks() {
  const [notifications, setNotifications] = useState<Record<string, PrinterNotificationCheck>>({});
  const [checking, setChecking] = useState(false);
  // Session-only; blank means the provider's factory Admin/Admin credentials.
  const [password, setPassword] = useState('');

  const check = useCallback(
    (devices: readonly PrinterNotificationTarget[]) => {
      const targets = devices.filter(
        (printer): printer is PrinterNotificationTarget & { deviceAddress: string } =>
          printer.deviceAddress !== null && printer.deviceAddress !== '',
      );
      if (targets.length === 0) return;

      setChecking(true);
      void runWithConcurrencyLimit(targets, NOTIFICATION_CONCURRENCY, async (printer) => {
        const host = printer.deviceAddress;
        try {
          const result = await invoke<PrinterNotificationCheck>(
            'printmanagement',
            'checkNotificationConfig',
            { host, siteCode: siteOf(printer.name), password: password || null },
            60_000,
          );
          setNotifications((previous) => ({ ...previous, [host.toUpperCase()]: result }));
        } catch {
          // Preserve successful per-device results when another endpoint fails.
        }
      }).finally(() => setChecking(false));
    },
    [password],
  );

  return {
    notifications,
    checking,
    password,
    setPassword,
    check,
  };
}
