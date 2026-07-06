import { useEffect, useState, type ReactNode } from 'react';
import { NavLink } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  LatestScanResult,
  ListInventoryHostsResult,
  ListPrintServersResult,
  OpsiConnectionStatusResult,
  PrintServerSnapshot,
} from '../../shared/api-types';
import type { MetricTone } from '../../shared/ui/SummaryMetric';
import { PageHeader } from '../../shared/ui/PageHeader';
import { navIcons } from '../../app/navIcons';
import {
  deriveInventoryTile,
  derivePatchTile,
  derivePrintTile,
  deriveSecurityTile,
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
      .then((status) => setPatch(derivePatchTile(status)))
      .catch(() => setPatch(derivePatchTile(null)));
  }, []);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Dashboard"
        subtitle="At a glance across the modules — from the last stored scan of each. Open a module to run a fresh one."
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        <ModuleTile
          to="/inventory"
          icon={navIcons.inventory}
          title="Inventory"
          description="Hardware and installed software per host."
          metric={inventory ?? undefined}
        />
        <ModuleTile
          to="/security"
          icon={navIcons.security}
          title="Security"
          description="Read-only security posture checks."
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
          to="/diagnostics"
          icon={navIcons.diagnostics}
          title="Diagnostics"
          description="Network, domain and system troubleshooting. Results are not stored — run a check to see live status."
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
    </div>
  );
}
