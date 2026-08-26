import { useEffect, useState, type ReactNode } from 'react';
import { NavLink } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  LatestScanResult,
  ListInventoryHostsResult,
  ListPrintServersResult,
  OpsiConnectionStatusResult,
  PatchDashboardOverview,
  PrintServerSnapshot,
  ReportReadinessPolicy,
  VulnerabilityOverview,
  VulnerabilityTrend,
} from '../../shared/api-types';
import type { MetricTone } from '../../shared/ui/SummaryMetric';
import { SemanticStatusBadge, type SemanticStatus } from '../../shared/ui/SemanticStatusBadge';
import { PageHeader } from '../../shared/ui/PageHeader';
import { navIcons } from '../../app/navIcons';
import {
  deriveInventoryTile,
  derivePatchStatusChart,
  derivePatchTile,
  derivePrintTile,
  deriveSecurityTile,
  deriveVulnerabilityTile,
  errorTile,
  loadingTile,
  type DashboardDataState,
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

const stateStatus: Record<DashboardDataState, SemanticStatus> = {
  loading: { dimension: 'execution', value: 'running' },
  fresh: { dimension: 'freshness', value: 'fresh' },
  stale: { dimension: 'freshness', value: 'stale' },
  missing: { dimension: 'availability', value: 'missing' },
  partial: { dimension: 'execution', value: 'partial' },
  error: { dimension: 'execution', value: 'failed' },
  unknown: { dimension: 'availability', value: 'unknown' },
};

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

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
        <div className="flex flex-1 flex-col gap-2">
          <div className="flex items-start justify-between gap-3">
            <div className={`text-2xl font-semibold tabular-nums ${toneText[metric.tone]}`}>
              {metric.value}
            </div>
            <SemanticStatusBadge status={stateStatus[metric.state]} />
          </div>
          <div className="text-xs text-slate-400">{metric.note}</div>
          <dl className="mt-auto grid grid-cols-[auto_1fr] gap-x-2 gap-y-0.5 border-t border-slate-800 pt-2 text-[11px] leading-4 text-muted">
            <dt>Source</dt>
            <dd className="min-w-0 truncate text-right text-slate-400" title={metric.source}>{metric.source}</dd>
            <dt>Captured</dt>
            <dd className="text-right text-slate-400">
              {metric.capturedAtUtc ? formatTimestamp(metric.capturedAtUtc) : '—'}
            </dd>
            <dt>Coverage</dt>
            <dd className="text-right text-slate-400">{metric.coverage}</dd>
          </dl>
        </div>
      ) : (
        <p className="text-sm text-slate-400">{description}</p>
      )}
    </NavLink>
  );
}

export function DashboardPage() {
  const [inventory, setInventory] = useState<TileMetric>(() => loadingTile('Stored WMI/CIM inventory snapshots'));
  const [security, setSecurity] = useState<TileMetric>(() => loadingTile('Persisted Security scan and per-check outcomes'));
  const [print, setPrint] = useState<TileMetric>(() => loadingTile('Stored print-server snapshots'));
  const [patch, setPatch] = useState<TileMetric>(() => loadingTile('Live opsi connection status'));
  const [patchChart, setPatchChart] = useState<PatchChartSegment[] | null>(null);
  const [vulnerabilities, setVulnerabilities] = useState<TileMetric>(() => loadingTile('Persisted Nessus scan inventory'));

  useEffect(() => {
    let active = true;
    let vulnerabilityTimer: ReturnType<typeof setTimeout> | undefined;
    const policy = invoke<ReportReadinessPolicy>('reporting', 'getReadinessPolicy', {});

    Promise.all([
      invoke<ListInventoryHostsResult>('inventory', 'listHosts', {}),
      policy,
    ])
      .then(([result, readiness]) => {
        if (active) setInventory(deriveInventoryTile(result.hosts, readiness.maximumInventoryAgeSeconds));
      })
      .catch(() => {
        if (active) setInventory(errorTile('Stored WMI/CIM inventory snapshots', 'Could not read stored inventory'));
      });

    Promise.all([
      invoke<LatestScanResult>('security', 'getLatestScan', {}),
      policy,
    ])
      .then(([result, readiness]) => {
        if (active) setSecurity(deriveSecurityTile(result.scan, readiness.maximumSecurityScanAgeSeconds));
      })
      .catch(() => {
        if (active) setSecurity(errorTile('Persisted Security scan and per-check outcomes', 'Could not read the latest Security scan'));
      });

    invoke<ListPrintServersResult>('printmanagement', 'listServers', {})
      .then(async (result) => {
        const loaded = await Promise.all(
          result.servers.map((server) =>
            invoke<PrintServerSnapshot>('printmanagement', 'getLatest', { server: server.server })
              .then((snapshot) => snapshot)
              .catch(() => null),
          ),
        );
        const snapshots = loaded.filter((snapshot): snapshot is PrintServerSnapshot => snapshot !== null);
        if (active) setPrint(derivePrintTile(result.servers, snapshots, loaded.length - snapshots.length));
      })
      .catch(() => {
        if (active) setPrint(errorTile('Stored print-server snapshots', 'Could not read registered print servers'));
      });

    invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus', {})
      .then((status) => {
        if (!active) return;
        setPatch(derivePatchTile(status));
        if (!status.connected) return;
        // Only reachable with an active opsi session — chart the product states.
        invoke<PatchDashboardOverview>('patchmanagement', 'getDashboard', { depotFilter: null })
          .then((dashboard) => {
            if (active) setPatchChart(derivePatchStatusChart(dashboard.products));
          })
          .catch(() => {
            if (!active) return;
            setPatch((current) => ({
              ...current,
              state: 'partial',
              tone: 'warning',
              note: 'Connected; product status unavailable',
              coverage: 'Connection verified; product states not evaluated',
            }));
            setPatchChart(null);
          });
      })
      .catch(() => {
        if (active) setPatch(errorTile('Live opsi connection status', 'Could not read the opsi connection state'));
      });

    const loadVulnerabilities = async () => {
      try {
        const overview = await invoke<VulnerabilityOverview>(
          'vulnerabilitymanagement',
          'getOverview',
          { knownHosts: [] },
        );
        const trend = overview.sync.running
          ? null
          : await invoke<VulnerabilityTrend>(
            'vulnerabilitymanagement',
            'getTrend',
            { days: 30 },
          ).catch(() => null);
        if (!active) return;
        setVulnerabilities(deriveVulnerabilityTile(overview, trend));
        if (overview.sync.running) vulnerabilityTimer = setTimeout(loadVulnerabilities, 2_000);
      } catch {
        if (active) setVulnerabilities(errorTile('Persisted Nessus scan inventory', 'Could not read Nessus source data'));
      }
    };
    void loadVulnerabilities();

    return () => {
      active = false;
      if (vulnerabilityTimer) clearTimeout(vulnerabilityTimer);
    };
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
          metric={vulnerabilities}
        />
        <ModuleTile
          to="/clients"
          icon={navIcons.clients}
          title="Clients"
          description="Per-host inventory, security and diagnostics — scanned on demand."
          metric={inventory}
        />
        <ModuleTile
          to="/clients"
          icon={navIcons.security}
          title="Security"
          description="Read-only security posture from the last per-client scan."
          metric={security}
        />
        <ModuleTile
          to="/printmanagement"
          icon={navIcons.printmanagement}
          title="Print Management"
          description="Printer inventory and toner levels."
          metric={print}
        />
        <ModuleTile
          to="/patchmanagement"
          icon={navIcons.patchmanagement}
          title="Patch Management"
          description="Read-only opsi status and Winget package maintenance."
          metric={patch}
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
          title="Report export"
          description="Executive HTML/JSON summary from saved Inventory and Security data."
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
        <span className="ml-auto text-xs font-normal text-muted">
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
