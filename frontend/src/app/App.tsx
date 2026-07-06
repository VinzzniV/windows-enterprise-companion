import { useEffect, useState, type ReactNode } from 'react';
import { HashRouter, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { ClientsPage } from '../features/clients/ClientsPage';
import { ClientDetailPage } from '../features/clients/ClientDetailPage';
import { ComparePage } from '../features/clients/ComparePage';
import { HardwareInfoPage } from '../features/inventory/HardwareInfoPage';
import { SecurityPage } from '../features/security/SecurityPage';
import { DiagnosticsPage } from '../features/diagnostics/DiagnosticsPage';
import { ActiveDirectoryPage } from '../features/activedirectory/ActiveDirectoryPage';
import { PatchManagementPage } from '../features/patchmanagement/PatchManagementPage';
import { PrintManagementPage } from '../features/printmanagement/PrintManagementPage';
import { ReportingPage } from '../features/reporting/ReportingPage';
import { invoke } from '../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../shared/api-types';
import { StatusBadge } from '../shared/ui/StatusBadge';
import { LogoMark } from '../shared/ui/LogoMark';
import { ErrorBoundary } from '../shared/ui/ErrorBoundary';
import { SplashIntro } from './SplashIntro';
import { navIcons } from './navIcons';
import { TargetProvider } from '../shared/targets/TargetContext';

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
}

// Clients is the primary workspace; everything else is fleet- or global-scoped.
const navGroups: { label: string; items: NavItem[] }[] = [
  {
    label: 'Fleet',
    items: [
      { to: '/', label: 'Dashboard', icon: navIcons.dashboard },
      { to: '/activedirectory', label: 'Active Directory', icon: navIcons.activedirectory },
      { to: '/patchmanagement', label: 'Patch Management', icon: navIcons.patchmanagement },
      { to: '/printmanagement', label: 'Print Management', icon: navIcons.printmanagement },
    ],
  },
  {
    // The standalone pages remain as batch runners for scanning many hosts at once.
    label: 'Multi-host',
    items: [
      { to: '/inventory', label: 'Inventory', icon: navIcons.inventory },
      { to: '/security', label: 'Security', icon: navIcons.security },
      { to: '/diagnostics', label: 'Diagnostics', icon: navIcons.diagnostics },
      { to: '/reporting', label: 'Reporting', icon: navIcons.reporting },
    ],
  },
];

const clientsNav: NavItem = { to: '/clients', label: 'Clients', icon: navIcons.clients };

const allNavItems = [clientsNav, ...navGroups.flatMap((group) => group.items)];

function sectionLabelFor(pathname: string): string {
  if (pathname === '/') {
    return 'Dashboard';
  }
  return allNavItems.find((item) => item.to !== '/' && pathname.startsWith(item.to))?.label ?? 'Overview';
}

function useAppInfo(): AppInfoResponse | null {
  const [appInfo, setAppInfo] = useState<AppInfoResponse | null>(null);
  useEffect(() => {
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then(setAppInfo)
      .catch(() => setAppInfo(null));
  }, []);
  return appInfo;
}

const utilityButtonClass =
  'rounded border border-slate-700 px-2 py-0.5 text-xs text-slate-400 transition-colors ' +
  'hover:bg-slate-800 hover:text-slate-200';

/** Global status bar: where you are (left) + privilege and quick utilities (right). */
function TopBar({ appInfo }: { appInfo: AppInfoResponse | null }) {
  const location = useLocation();
  return (
    <header className="flex shrink-0 items-center justify-between gap-3 border-b border-slate-800 bg-slate-950 px-6 py-2.5">
      <div className="min-w-0 text-xs text-slate-500">
        <span className="text-slate-600">Windows Enterprise Companion</span>
        <span className="mx-1.5 text-slate-700">/</span>
        <span className="font-medium text-slate-300">{sectionLabelFor(location.pathname)}</span>
      </div>
      {appInfo && (
        <div className="flex shrink-0 items-center gap-2">
          <StatusBadge variant={appInfo.isElevated ? 'elevation' : 'neutral'}>
            {appInfo.isElevated ? 'Administrator' : 'Standard user'}
          </StatusBadge>
          {!appInfo.isElevated && (
            <button
              type="button"
              title="Starts an elevated copy via the UAC prompt and closes this one"
              onClick={() => {
                // A dismissed UAC prompt is a valid outcome; errors surface in the host log
                invoke('system', 'restartElevated', {}, 120_000).catch(() => {});
              }}
              className={utilityButtonClass}
            >
              Restart as administrator
            </button>
          )}
        </div>
      )}
    </header>
  );
}

/** Dev/runtime info at the bottom of the sidebar: version and file locations. */
function AppInfoFooter({ appInfo }: { appInfo: AppInfoResponse | null }) {
  if (!appInfo) {
    return null;
  }
  return (
    <div className="mt-auto flex flex-col gap-1.5 border-t border-slate-800 px-4 py-3 text-xs text-slate-500">
      <span className="truncate" title={`Version ${appInfo.version}`}>
        v{appInfo.version}
      </span>
      <span className="truncate" title={appInfo.databasePath}>
        DB: {appInfo.databasePath}
      </span>
      <span className="flex items-center gap-2">
        <span className="min-w-0 truncate" title={appInfo.logDirectory}>
          Logs: {appInfo.logDirectory}
        </span>
        <button
          type="button"
          onClick={() => {
            invoke('system', 'openLogsFolder').catch(() => {
              /* surfaced in host log; nothing actionable in the UI */
            });
          }}
          className={`shrink-0 ${utilityButtonClass}`}
        >
          Open
        </button>
      </span>
    </div>
  );
}

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `relative flex items-center gap-2.5 rounded-md px-3 py-2 text-sm transition-colors ${
    isActive
      ? 'bg-accent-500/10 font-medium text-white before:absolute before:inset-y-1.5 before:left-0 before:w-0.5 before:rounded-full before:bg-accent-400'
      : 'text-slate-400 hover:bg-slate-800/50 hover:text-slate-200'
  }`;

function AppRoutes() {
  const location = useLocation();

  return (
    // Key on the path so route changes replay the enter animation and
    // reset the error boundary
    <div key={location.pathname} className="wec-page-enter">
      <ErrorBoundary>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/clients" element={<ClientsPage />} />
          <Route path="/clients/compare" element={<ComparePage />} />
          <Route path="/clients/:host" element={<ClientDetailPage />} />
          <Route path="/inventory" element={<HardwareInfoPage />} />
          <Route path="/security" element={<SecurityPage />} />
          <Route path="/diagnostics" element={<DiagnosticsPage />} />
          <Route path="/activedirectory" element={<ActiveDirectoryPage />} />
          <Route path="/patchmanagement" element={<PatchManagementPage />} />
          <Route path="/printmanagement" element={<PrintManagementPage />} />
          <Route path="/reporting" element={<ReportingPage />} />
        </Routes>
      </ErrorBoundary>
    </div>
  );
}

export function App() {
  const [introDone, setIntroDone] = useState(false);
  const appInfo = useAppInfo();

  return (
    <HashRouter>
      <TargetProvider>
      <div className="flex h-screen bg-slate-950 text-slate-100">
        <aside className="flex w-56 shrink-0 flex-col border-r border-slate-800 bg-slate-900">
          <div className="flex items-center gap-2.5 border-b border-slate-800 px-4 py-4">
            <LogoMark className="h-7 w-7 shrink-0 text-accent-400" />
            <h1 className="text-sm font-semibold leading-tight tracking-wide text-slate-200">
              Windows Enterprise Companion
            </h1>
          </div>
          <nav className="flex flex-1 flex-col gap-4 overflow-y-auto p-2">
            <NavLink to={clientsNav.to} className={navLinkClass}>
              {clientsNav.icon}
              {clientsNav.label}
            </NavLink>
            {navGroups.map((group) => (
              <div key={group.label} className="flex flex-col gap-1">
                <span className="px-3 pb-0.5 text-[10px] font-semibold uppercase tracking-wider text-slate-600">
                  {group.label}
                </span>
                {group.items.map((item) => (
                  <NavLink key={item.to} to={item.to} end={item.to === '/'} className={navLinkClass}>
                    {item.icon}
                    {item.label}
                  </NavLink>
                ))}
              </div>
            ))}
          </nav>
          <AppInfoFooter appInfo={appInfo} />
        </aside>
        <main className="flex flex-1 flex-col overflow-hidden">
          <TopBar appInfo={appInfo} />
          <div className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-[1400px]">
              <AppRoutes />
            </div>
          </div>
        </main>
      </div>
      {!introDone && <SplashIntro onDone={() => setIntroDone(true)} />}
      </TargetProvider>
    </HashRouter>
  );
}
