import { useEffect, useState, type ReactNode } from 'react';
import { HashRouter, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { ClientsPage } from '../features/clients/ClientsPage';
import { ClientDetailPage } from '../features/clients/ClientDetailPage';
import { ComparePage } from '../features/clients/ComparePage';
import { ActiveDirectoryPage } from '../features/activedirectory/ActiveDirectoryPage';
import { EmployeeLifecyclePage } from '../features/employeelifecycle/EmployeeLifecyclePage';
import { VulnerabilitiesPage } from '../features/vulnerabilities/VulnerabilitiesPage';
import { PatchManagementPage } from '../features/patchmanagement/PatchManagementPage';
import { PrintManagementPage } from '../features/printmanagement/PrintManagementPage';
import { NetworkScanPage } from '../features/networkscan/NetworkScanPage';
import { ReportingPage } from '../features/reporting/ReportingPage';
import { SettingsPage } from '../features/verwaltung/SettingsPage';
import { ErrorLogPage } from '../features/verwaltung/ErrorLogPage';
import { invoke } from '../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../shared/api-types';
import { StatusBadge } from '../shared/ui/StatusBadge';
import { LogoMark } from '../shared/ui/LogoMark';
import { ErrorBoundary } from '../shared/ui/ErrorBoundary';
import { Button } from '../shared/ui/Button';
import { navIcons } from './navIcons';
import { TargetProvider } from '../shared/targets/TargetContext';
import { EnvironmentProvider } from '../shared/environment/EnvironmentContext';
import { AdminSignIn } from '../shared/targets/AdminSignIn';
import { errorText } from '../shared/bridge/errorText';

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
}

// Clients is the day-to-day workspace and sits right under the Dashboard.
// Per-host Inventory/Security/Diagnostics now live inside a client's detail, so
// they no longer appear as standalone nav entries. Verwaltung holds app-wide
// settings and the error log.
const navGroups: { label: string; items: NavItem[] }[] = [
  {
    label: 'Fleet',
    items: [
      { to: '/', label: 'Dashboard', icon: navIcons.dashboard },
      { to: '/clients', label: 'Clients', icon: navIcons.clients },
      { to: '/activedirectory', label: 'Active Directory', icon: navIcons.activedirectory },
      { to: '/employeelifecycle', label: 'IT Lifecycle', icon: navIcons.employeelifecycle },
      { to: '/vulnerabilities', label: 'Vulnerabilities', icon: navIcons.vulnerabilities },
      { to: '/patchmanagement', label: 'Patch Management', icon: navIcons.patchmanagement },
      { to: '/printmanagement', label: 'Print Management', icon: navIcons.printmanagement },
      { to: '/networkscan', label: 'Netzwerkscan', icon: navIcons.networkscan },
      { to: '/reporting', label: 'Report export', icon: navIcons.reporting },
    ],
  },
  {
    label: 'Verwaltung',
    items: [
      { to: '/settings', label: 'Settings', icon: navIcons.settings },
      { to: '/logs', label: 'Error log', icon: navIcons.logs },
    ],
  },
];

const allNavItems = navGroups.flatMap((group) => group.items);

function sectionLabelFor(pathname: string): string {
  if (pathname === '/') {
    return 'Dashboard';
  }
  return allNavItems.find((item) => item.to !== '/' && pathname.startsWith(item.to))?.label ?? 'Overview';
}

export type AppInfoState =
  | { kind: 'loading' }
  | { kind: 'loaded'; appInfo: AppInfoResponse }
  | { kind: 'error'; message: string };

function useAppInfo(): AppInfoState {
  const [state, setState] = useState<AppInfoState>({ kind: 'loading' });
  useEffect(() => {
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then((appInfo) => setState({ kind: 'loaded', appInfo }))
      .catch((caught: unknown) => setState({ kind: 'error', message: errorText(caught) }));
  }, []);
  return state;
}

/** Global status bar: where you are (left) + privilege and quick utilities (right). */
function TopBar({ appInfo }: { appInfo: AppInfoResponse | null }) {
  const location = useLocation();
  const [restartError, setRestartError] = useState<string | null>(null);
  return (
    <header className="flex shrink-0 items-center justify-between gap-3 border-b border-slate-800 bg-slate-950 px-6 py-2.5">
      <div className="min-w-0 text-xs text-slate-500">
        <span className="text-slate-600">Windows Enterprise Companion</span>
        <span className="mx-1.5 text-slate-700">/</span>
        <span className="font-medium text-slate-300">{sectionLabelFor(location.pathname)}</span>
      </div>
      <div className="flex shrink-0 items-center gap-2">
        <AdminSignIn />
        {appInfo && (
          <>
            <StatusBadge variant={appInfo.isElevated ? 'elevation' : 'neutral'}>
              {appInfo.isElevated ? 'Administrator' : 'Standard user'}
            </StatusBadge>
            {!appInfo.isElevated && (
              <Button
                variant="secondary"
                title="Starts an elevated copy via the UAC prompt and closes this one"
                onClick={() => {
                  setRestartError(null);
                  invoke('system', 'restartElevated', {}).catch((caught: unknown) =>
                    setRestartError(errorText(caught)),
                  );
                }}
                className="px-2 py-0.5 text-xs font-normal"
              >
                Restart as administrator
              </Button>
            )}
          </>
        )}
        {restartError && (
          <span role="alert" className="max-w-64 text-xs text-fail-400">
            {restartError}
          </span>
        )}
      </div>
    </header>
  );
}

/** Compact runtime identity and a keyboard-accessible log-folder action. */
export function AppInfoFooter({ state }: { state: AppInfoState }) {
  const [openError, setOpenError] = useState<string | null>(null);

  if (state.kind === 'loading') {
    return (
      <div className="mt-auto border-t border-slate-800 px-4 py-3 text-xs text-slate-500">
        Loading application information…
      </div>
    );
  }

  if (state.kind === 'error') {
    return (
      <div className="mt-auto border-t border-slate-800 px-4 py-3 text-xs text-fail-400" role="alert">
        Application information unavailable: {state.message}
      </div>
    );
  }

  const { appInfo } = state;
  return (
    <div className="mt-auto flex flex-col gap-1.5 border-t border-slate-800 px-4 py-3 text-xs text-slate-500">
      <span className="truncate" title={`Version ${appInfo.version}`}>
        v{appInfo.version}
      </span>
      <span className="truncate" title={`Runtime profile: ${appInfo.runtimeProfile}`}>
        Profile: {appInfo.runtimeProfile}
      </span>
      <Button
        variant="ghost"
        onClick={() => {
          setOpenError(null);
          let request: Promise<unknown>;
          try {
            request = invoke('system', 'openLogsFolder');
          } catch (caught: unknown) {
            setOpenError(errorText(caught));
            return;
          }
          void request.catch((caught: unknown) => setOpenError(errorText(caught)));
        }}
        className="self-start text-xs"
      >
        Open log folder
      </Button>
      {openError && (
        <span role="alert" className="text-fail-400">
          {openError}
        </span>
      )}
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
    // Key on the path so route changes reset the error boundary.
    <div key={location.pathname}>
      <ErrorBoundary>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/clients" element={<ClientsPage />} />
          <Route path="/clients/compare" element={<ComparePage />} />
          <Route path="/clients/:host" element={<ClientDetailPage />} />
          <Route path="/activedirectory" element={<ActiveDirectoryPage />} />
          <Route path="/employeelifecycle" element={<EmployeeLifecyclePage />} />
          <Route path="/vulnerabilities" element={<VulnerabilitiesPage />} />
          <Route path="/patchmanagement" element={<PatchManagementPage />} />
          <Route path="/printmanagement" element={<PrintManagementPage />} />
          <Route path="/networkscan" element={<NetworkScanPage />} />
          <Route path="/reporting" element={<ReportingPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/logs" element={<ErrorLogPage />} />
        </Routes>
      </ErrorBoundary>
    </div>
  );
}

export function App() {
  const appInfoState = useAppInfo();
  const appInfo = appInfoState.kind === 'loaded' ? appInfoState.appInfo : null;

  return (
    <HashRouter>
      <TargetProvider>
      <EnvironmentProvider>
      <div className="flex h-screen bg-slate-950 text-slate-100">
        <aside className="flex w-56 shrink-0 flex-col border-r border-slate-800 bg-slate-900">
          <div className="flex items-center gap-2.5 border-b border-slate-800 px-4 py-4">
            <LogoMark className="h-7 w-7 shrink-0 text-accent-400" />
            <h1 className="text-sm font-semibold leading-tight tracking-wide text-slate-200">
              Windows Enterprise Companion
            </h1>
          </div>
          <nav className="flex flex-1 flex-col gap-4 overflow-y-auto p-2">
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
          <AppInfoFooter state={appInfoState} />
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
      </EnvironmentProvider>
      </TargetProvider>
    </HashRouter>
  );
}
