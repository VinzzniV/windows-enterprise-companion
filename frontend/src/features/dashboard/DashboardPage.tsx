import { useEffect, useState, type ReactNode } from 'react';
import { NavLink } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  LatestScanResult,
  ListInventoryHostsResult,
  ListPrintServersResult,
  OpsiConnectionStatusResult,
  PatchDashboardResult,
  PrintServerSnapshot,
  VulnerabilityOverview,
  VulnerabilityTrend,
} from '../../shared/api-types';
import type { MetricTone } from '../../shared/ui/SummaryMetric';
import { PageHeader } from '../../shared/ui/PageHeader';
import { navIcons } from '../../app/navIcons';
import {
  deriveInventoryTile,
  derivePatchStatusChart,
  derivePatchTile,
  derivePrintTile,
  deriveSecurityTile,
  type PatchChartSegment,
  type TileMetric,
} from './dashboard';

const toneText: Record<MetricTone, string> = {
  neutral: 'text-slate-100',
  success: 'text-ok-400',
  warning: 'text-warn-400',
  danger: 'text-fail-400',
  info: 'text-info-400',
};

interface TileProps {
  to: string;
  icon: ReactNode;
  title: string;
  description: string;
  metric?: TileMetric;
}

function ModuleTile({ to, icon, title, description, metric }: TileProps) {
  return (
    <NavLink
      to={to}
      className="group flex flex-col gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 transition-colors hover:border-slate-700 hover:bg-slate-800/60"
    >
      <div className="flex items-center gap-2.5 text-slate-300 group-hover:text-slate-100">
        <span className="text-accent-400">{icon}</span>
        <span className="text-sm font-semibold">{title}</span>
        <svg
          viewBox="0 0 16 16"
          aria-hidden="true"
          className="ml-auto h-4 w-4 text-slate-600 transition-transform group-hover:translate-x-0.5 group-hover:text-slate-400"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.5"
        >
          <path d="M6 4l4 4-4 4" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </div>
      {metric ? (
        <div>
          <div className={`text-2xl font-semibold tabular-nums ${toneText[metric.tone]}`}>
            {metric.value}
          </div>
          {metric.note && <div className="mt-0.5 text-xs text-slate-500">{metric.note}</div>}
        </div>
      ) : (
        <p className="text-sm text-slate-400">{description}</p>
      )}
    </NavLink>
  );
}

export function DashboardPage() {
  const [inventory, setInventory] = useState<TileMetric | null>(null);
  const [security, setSecurity] = useState<TileMetric | null>(null);
  const [print, setPrint] = useState<TileMetric | null>(null);
  const [patch, setPatch] = useState<TileMetric | null>(null);
  const [patchChart, setPatchChart] = useState<PatchChartSegment[] | null>(null);
  const [vulnerabilities, setVulnerabilities] = useState<TileMetric | null>(null);

  useEffect(() => {
    invoke<ListInventoryHostsResult>('inventory', 'listHosts', {})
      .then((result) => setInventory(deriveInventoryTile(result.hosts)))
      .catch(() => setInventory(deriveInventoryTile([])));

    invoke<LatestScanResult>('security', 'getLatestScan', {})
      .then((result) => setSecurity(deriveSecurityTile(result.scan)))
      .catch(() => setSecurity(deriveSecurityTile(null)));

    invoke<ListPrintServersResult>('printmanagement', 'listServers', {})
      .then(async (result) => {
        const snapshots = (
          await Promise.all(
            result.servers.map((server) =>
              invoke<PrintServerSnapshot>('printmanagement', 'getLatest', { server: server.server })
                .then((snapshot) => snapshot)
                .catch(() => null),
            ),
          )
        ).filter((snapshot): snapshot is PrintServerSnapshot => snapshot !== null);
        setPrint(derivePrintTile(result.servers, snapshots));
      })
      .catch(() => setPrint(derivePrintTile([], [])));

    invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus', {})
      .then((status) => {
        setPatch(derivePatchTile(status));
        if (!status.connected) return;
        // Only reachable with an active opsi session — chart the product states.
        invoke<PatchDashboardResult>('patchmanagement', 'getDashboard', { depotFilter: null })
          .then((dashboard) => setPatchChart(derivePatchStatusChart(dashboard.products)))
          .catch(() => setPatchChart(null));
      })
      .catch(() => setPatch(derivePatchTile(null)));

    Promise.all([
      invoke<VulnerabilityOverview>('vulnerabilitymanagement', 'getOverview', { knownHosts: [] }),
      invoke<VulnerabilityTrend>('vulnerabilitymanagement', 'getTrend', { days: 30 }),
    ]).then(([overview, trend]) => setVulnerabilities({
      value: String(overview.criticalAssets),
      tone: overview.criticalAssets ? 'danger' : 'success',
      note: `${overview.highAssets} high · ${trend.verdict === 'BETTER' ? '↓' : trend.verdict === 'WORSE' ? '↑' : '→'} ${trend.verdict.replace('_', ' ').toLowerCase()}`,
    })).catch(() => setVulnerabilities({ value: '—', tone: 'neutral', note: 'Nessus not configured' }));
  }, []);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Dashboard"
        subtitle="At a glance across the modules — from the last stored scan of each. Open a module to run a fresh one."
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        <ModuleTile
          to="/vulnerabilities"
          icon={navIcons.vulnerabilities}
          title="Vulnerabilities"
          description="Deduplicated Nessus findings and security trend."
          metric={vulnerabilities ?? undefined}
        />
        <ModuleTile
          to="/clients"
          icon={navIcons.clients}
          title="Clients"
          description="Per-host inventory, security and diagnostics — scanned on demand."
          metric={inventory ?? undefined}
        />
        <ModuleTile
          to="/clients"
          icon={navIcons.security}
          title="Security"
          description="Read-only security posture from the last per-client scan."
          metric={security ?? undefined}
        />
        <ModuleTile
          to="/printmanagement"
          icon={navIcons.printmanagement}
          title="Print Management"
          description="Printer inventory and toner levels."
          metric={print ?? undefined}
        />
        <ModuleTile
          to="/patchmanagement"
          icon={navIcons.patchmanagement}
          title="Patch Management"
          description="opsi patch workflow and rollout."
          metric={patch ?? undefined}
        />
        <ModuleTile
          to="/activedirectory"
          icon={navIcons.activedirectory}
          title="Active Directory"
          description="Domain overview and hygiene checks over LDAP."
        />
        <ModuleTile
          to="/reporting"
          icon={navIcons.reporting}
          title="Reporting"
          description="Executive HTML/JSON summary of this machine."
        />
      </div>

      {patchChart && patchChart.length > 0 && <PatchStatusChart segments={patchChart} />}
    </div>
  );
}

/** Stacked product-state bar for the connected opsi server — plain CSS, no chart lib. */
function PatchStatusChart({ segments }: { segments: PatchChartSegment[] }) {
  const total = segments.reduce((sum, segment) => sum + segment.count, 0);
  return (
    <NavLink
      to="/patchmanagement"
      aria-label="Patch status — open Patch Management"
      className="group flex flex-col gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 transition-colors hover:border-slate-700"
    >
      <div className="flex items-center gap-2.5 text-sm font-semibold text-slate-300 group-hover:text-slate-100">
        <span className="text-accent-400">{navIcons.patchmanagement}</span>
        Patch status
        <span className="ml-auto text-xs font-normal text-slate-500">
          {total} product{total === 1 ? '' : 's'}
        </span>
      </div>
      <div className="flex h-3 overflow-hidden rounded-full bg-slate-800" role="img" aria-label={
        segments.map((segment) => `${segment.label}: ${segment.count}`).join(', ')
      }>
        {segments.map((segment) => (
          <div
            key={segment.label}
            className={segment.colorClass}
            style={{ width: `${(segment.count / total) * 100}%` }}
            title={`${segment.label}: ${segment.count}`}
          />
        ))}
      </div>
      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
        {segments.map((segment) => (
          <span key={segment.label} className="flex items-center gap-1.5">
            <span className={`h-2 w-2 rounded-full ${segment.colorClass}`} aria-hidden />
            {segment.label}
            <span className="tabular-nums text-slate-300">{segment.count}</span>
          </span>
        ))}
      </div>
    </NavLink>
  );
}
