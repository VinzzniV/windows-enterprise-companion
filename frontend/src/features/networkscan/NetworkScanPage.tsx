import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import type {
  DeviceKind,
  NetworkHostRow,
  NetworkScanPolicyResult,
  NetworkScanResult,
  ScanNetworkRequest,
  TargetRequest,
} from '../../shared/api-types';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Toolbar } from '../../shared/ui/Toolbar';
import { Button } from '../../shared/ui/Button';
import { Input } from '../../shared/ui/Input';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { StatusBadge, type StatusBadgeVariant } from '../../shared/ui/StatusBadge';
import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import { ErrorState } from '../../shared/ui/States';
import { deviceKindLabel, hostStatus, hostStatusPresentation } from './network';

const kindVariant: Record<DeviceKind, StatusBadgeVariant> = {
  PRINTER: 'info',
  COMPUTER: 'neutral',
  NETWORK_DEVICE: 'info',
  UNKNOWN: 'neutral',
};

function Metric({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/40 px-4 py-2">
      <div className={`text-lg font-semibold ${tone ?? 'text-slate-200'}`}>{value}</div>
      <div className="text-xs text-muted">{label}</div>
    </div>
  );
}

export function NetworkScanPage() {
  const targets = useTargetsOptional();
  const adminCredentials = targets?.adminCredentials ?? null;

  const [target, setTarget] = useState('172.20.20.0/24');
  const [scanPorts, setScanPorts] = useState(true);
  const [dhcpServer, setDhcpServer] = useState('');
  const [scanning, setScanning] = useState(false);
  const [result, setResult] = useState<NetworkScanResult | null>(null);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [scanPolicy, setScanPolicy] = useState<NetworkScanPolicyResult | null>(null);
  const scanInFlight = useRef(false);

  useEffect(() => {
    invoke<NetworkScanPolicyResult>('networkscan', 'getPolicy', {})
      .then(setScanPolicy)
      .catch(() => setScanPolicy(null));
  }, []);

  // The DHCP server usually sits on a domain controller and needs an admin token;
  // carry the session admin identity when one is signed in (never persisted).
  const dhcpRequest = useCallback(
    (host: string): TargetRequest =>
      adminCredentials && adminCredentials.userName.trim() !== ''
        ? {
            host,
            userName: adminCredentials.userName.trim(),
            domain: adminCredentials.domain.trim() || null,
            password: adminCredentials.password,
          }
        : { host },
    [adminCredentials],
  );

  const scan = useCallback(() => {
    if (scanInFlight.current) return;
    const trimmedTarget = target.trim();
    if (trimmedTarget === '') {
      setError({
        message: 'The network scan requires a target.',
        cause: 'The CIDR, IP range, or individual IP field is empty.',
        action: 'Enter a target such as 172.20.20.0/24 and start the scan again.',
        technicalDetails: 'Validation: network scan target is empty.',
      });
      return;
    }
    const trimmedDhcp = dhcpServer.trim();
    const request: ScanNetworkRequest = {
      target: trimmedTarget,
      scanPorts,
      dhcp: trimmedDhcp === '' ? null : dhcpRequest(trimmedDhcp),
    };
    scanInFlight.current = true;
    setScanning(true);
    setError(null);
    invoke<NetworkScanResult>('networkscan', 'scan', request)
      .then(setResult)
      .catch((caught: unknown) => setError(presentError(caught, {
        message: 'The network scan could not be completed.',
      })))
      .finally(() => {
        scanInFlight.current = false;
        setScanning(false);
      });
  }, [target, scanPorts, dhcpServer, dhcpRequest]);

  const rows = result?.hosts ?? [];
  const metrics = useMemo(() => {
    const dhcpChecked = result?.dhcpChecked ?? false;
    return {
      up: rows.filter((host) => host.isUp).length,
      rogue: dhcpChecked ? rows.filter((host) => host.isUp && !host.hasReservation).length : 0,
      stale: rows.filter((host) => !host.isUp).length,
      dhcpChecked,
    };
  }, [rows, result]);

  const columns: DataColumn<NetworkHostRow>[] = useMemo(
    () => [
      {
        header: 'IP',
        mono: true,
        cell: (host) => host.ip,
        sortValue: (host) => host.ip,
      },
      {
        header: 'Hostname',
        cell: (host) => host.hostname ?? '—',
        sortValue: (host) => host.hostname,
      },
      {
        header: 'Type',
        cell: (host) => <StatusBadge variant={kindVariant[host.kind]}>{deviceKindLabel[host.kind]}</StatusBadge>,
        sortValue: (host) => deviceKindLabel[host.kind],
      },
      {
        header: 'Status',
        cell: (host) => {
          const status = hostStatus(host, metrics.dhcpChecked);
          const presentation = hostStatusPresentation(status);
          return (
            <span className="flex flex-wrap items-center gap-2" title={host.reservationName ?? undefined}>
              <SemanticStatusBadge status={presentation.status} />
              <span className="text-xs text-slate-400">{presentation.context}</span>
            </span>
          );
        },
        sortValue: (host) => hostStatus(host, metrics.dhcpChecked),
      },
      {
        header: 'Open ports',
        mono: true,
        cell: (host) => (host.openPorts.length > 0 ? host.openPorts.join(', ') : '—'),
        sortValue: (host) => host.openPorts.length,
      },
      {
        header: 'MAC vendor',
        cell: (host) => host.macVendor ?? '—',
        sortValue: (host) => host.macVendor,
      },
    ],
    [metrics.dhcpChecked],
  );

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Network Scan"
        subtitle="Discover live hosts, device types, open ports, and optional DHCP reservations in a subnet with nmap."
      />

      <Toolbar
        actions={
          <Button variant="primary" onClick={scan} disabled={scanning}>
            {scanning ? 'Scanning…' : 'Scan'}
          </Button>
        }
      >
        <label className="flex flex-col gap-0.5">
          <span className="text-xs text-muted">Target (CIDR, range, or IP)</span>
          <Input
            className="w-56"
            value={target}
            placeholder="172.20.20.0/24"
            onChange={(event) => setTarget(event.target.value)}
            onKeyDown={(event) => event.key === 'Enter' && scan()}
          />
        </label>
        <label className="flex flex-col gap-0.5">
          <span className="text-xs text-muted">DHCP server (optional)</span>
          <Input
            className="w-48"
            value={dhcpServer}
            placeholder="e.g. PK-SRVDC001"
            onChange={(event) => setDhcpServer(event.target.value)}
            onKeyDown={(event) => event.key === 'Enter' && scan()}
          />
        </label>
        <label className="mt-4 flex items-center gap-2 text-sm text-slate-300">
          <input
            type="checkbox"
            checked={scanPorts}
            onChange={(event) => setScanPorts(event.target.checked)}
            className="h-4 w-4 accent-accent-500"
          />
          Scan ports
        </label>
      </Toolbar>

      <p className="rounded border border-slate-800 bg-slate-900/40 px-3 py-2 text-xs text-slate-400" role="status">
        Target: <span className="font-mono text-slate-200">{target.trim() || 'not set'}</span>. This starts active host discovery from the WEC machine
        {scanPorts
          ? ` and probes ${scanPolicy ? `TCP ports ${scanPolicy.scanPorts.join(', ')}` : 'the configured TCP service-port set'} on every discovered host`
          : ' without TCP service-port probes'}.
        {dhcpServer.trim() !== ''
          ? ` It also reads DHCP reservations from ${dhcpServer.trim()} using the remote-account context.`
          : ' No DHCP server will be queried.'}
        {' '}Results stay in this view and are not persisted; no target configuration is changed.
      </p>

      {dhcpServer.trim() !== '' && !adminCredentials && (
        <p className="text-xs text-elevation-400">
          For the DHCP check, use “Set remote account” in the top right. Otherwise the query runs as the current
          Windows user and will usually fail.
        </p>
      )}

      {error && (
        <ErrorState
          title="Network scan failed"
          {...error}
          controls={<Button variant="secondary" onClick={scan} disabled={scanning}>Scan again</Button>}
        />
      )}

      {result && (
        <>
          <div className="flex flex-wrap gap-3">
            <Metric label="Active" value={metrics.up} />
            {metrics.dhcpChecked && (
              <Metric label="Without reservation" value={metrics.rogue} tone={metrics.rogue > 0 ? 'text-fail-400' : undefined} />
            )}
            {metrics.dhcpChecked && (
              <Metric
                label="Stale reservations"
                value={metrics.stale}
                tone={metrics.stale > 0 ? 'text-elevation-400' : undefined}
              />
            )}
          </div>

          <Card title={`Result for ${result.target}`}>
            <DataTable
              columns={columns}
              rows={rows}
              getRowKey={(host) => host.ip}
              stickyHeader
              emptyMessage="No hosts found."
            />
          </Card>
        </>
      )}
    </div>
  );
}
