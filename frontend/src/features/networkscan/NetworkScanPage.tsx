import { useCallback, useMemo, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import type {
  DeviceKind,
  NetworkHostRow,
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
import { ErrorState } from '../../shared/ui/States';
import { deviceKindLabel, hostStatus, type HostStatus } from './network';

const statusMeta: Record<HostStatus, { variant: StatusBadgeVariant; label: string }> = {
  ok: { variant: 'success', label: 'Reserviert' },
  rogue: { variant: 'error', label: 'Keine Reservierung' },
  stale: { variant: 'elevation', label: 'Karteileiche' },
  up: { variant: 'neutral', label: 'Aktiv' },
};

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
      <div className="text-xs text-slate-500">{label}</div>
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
  const [error, setError] = useState<string | null>(null);

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
    const trimmedTarget = target.trim();
    if (trimmedTarget === '') {
      setError('Bitte ein Ziel angeben (z. B. 172.20.20.0/24).');
      return;
    }
    const trimmedDhcp = dhcpServer.trim();
    const request: ScanNetworkRequest = {
      target: trimmedTarget,
      scanPorts,
      dhcp: trimmedDhcp === '' ? null : dhcpRequest(trimmedDhcp),
    };
    setScanning(true);
    setError(null);
    invoke<NetworkScanResult>('networkscan', 'scan', request)
      .then(setResult)
      .catch((caught: unknown) => setError(errorText(caught)))
      .finally(() => setScanning(false));
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
        header: 'Typ',
        cell: (host) => <StatusBadge variant={kindVariant[host.kind]}>{deviceKindLabel[host.kind]}</StatusBadge>,
        sortValue: (host) => deviceKindLabel[host.kind],
      },
      {
        header: 'Status',
        cell: (host) => {
          const status = hostStatus(host, metrics.dhcpChecked);
          const meta = statusMeta[status];
          return (
            <span title={host.reservationName ?? undefined}>
              <StatusBadge variant={meta.variant}>{meta.label}</StatusBadge>
            </span>
          );
        },
        sortValue: (host) => hostStatus(host, metrics.dhcpChecked),
      },
      {
        header: 'Offene Ports',
        mono: true,
        cell: (host) => (host.openPorts.length > 0 ? host.openPorts.join(', ') : '—'),
        sortValue: (host) => host.openPorts.length,
      },
      {
        header: 'MAC-Hersteller',
        cell: (host) => host.macVendor ?? '—',
        sortValue: (host) => host.macVendor,
      },
    ],
    [metrics.dhcpChecked],
  );

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Netzwerkscan"
        subtitle="nmap-Discovery eines Subnetzes: lebende Hosts, Gerätetyp, offene Ports und optional DHCP-Reservierungen."
      />

      <Toolbar
        actions={
          <Button variant="primary" onClick={scan} disabled={scanning}>
            {scanning ? 'Scanne…' : 'Scannen'}
          </Button>
        }
      >
        <label className="flex flex-col gap-0.5">
          <span className="text-xs text-slate-500">Ziel (CIDR, Bereich oder IP)</span>
          <Input
            className="w-56"
            value={target}
            placeholder="172.20.20.0/24"
            onChange={(event) => setTarget(event.target.value)}
            onKeyDown={(event) => event.key === 'Enter' && scan()}
          />
        </label>
        <label className="flex flex-col gap-0.5">
          <span className="text-xs text-slate-500">DHCP-Server (optional)</span>
          <Input
            className="w-48"
            value={dhcpServer}
            placeholder="z. B. PK-SRVDC001"
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
          Ports scannen
        </label>
      </Toolbar>

      {dhcpServer.trim() !== '' && !adminCredentials && (
        <p className="text-xs text-elevation-400">
          Für die DHCP-Prüfung oben rechts „Sign in as admin" — sonst läuft die Abfrage als aktueller Benutzer und
          scheitert meist.
        </p>
      )}

      {error && <ErrorState message={error} />}

      {result && (
        <>
          <div className="flex flex-wrap gap-3">
            <Metric label="Aktiv" value={metrics.up} />
            {metrics.dhcpChecked && (
              <Metric label="Ohne Reservierung" value={metrics.rogue} tone={metrics.rogue > 0 ? 'text-fail-400' : undefined} />
            )}
            {metrics.dhcpChecked && (
              <Metric
                label="Karteileichen"
                value={metrics.stale}
                tone={metrics.stale > 0 ? 'text-elevation-400' : undefined}
              />
            )}
          </div>

          <Card title={`Ergebnis für ${result.target}`}>
            <DataTable
              columns={columns}
              rows={rows}
              getRowKey={(host) => host.ip}
              stickyHeader
              emptyMessage="Keine Hosts gefunden."
            />
          </Card>
        </>
      )}
    </div>
  );
}
