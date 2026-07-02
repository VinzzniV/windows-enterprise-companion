import { useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { PingResponse } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';

export function HardwareInfoPage() {
  const [bridgeStatus, setBridgeStatus] = useState('Contacting host …');

  useEffect(() => {
    let cancelled = false;
    invoke<PingResponse>('system', 'ping')
      .then((response) => {
        if (!cancelled) {
          setBridgeStatus(
            `Bridge OK — ${response.message} @ ${new Date(response.timestamp).toLocaleString()}`,
          );
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setBridgeStatus(error instanceof Error ? error.message : String(error));
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="flex flex-col gap-4">
      <header>
        <h1 className="text-xl font-semibold">Hardware Inventory</h1>
        <p className="text-sm text-slate-400">Local machine overview</p>
      </header>
      <Card title="Bridge status">
        <p className="text-sm">{bridgeStatus}</p>
      </Card>
      <Card title="Hardware information">
        <p className="text-sm text-slate-400">
          CPU, memory, disks and OS details arrive with M1 step 7 (inventory module).
        </p>
      </Card>
    </div>
  );
}
