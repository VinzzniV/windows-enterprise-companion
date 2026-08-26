import { useCallback, useEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
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
import { CompactErrorState } from '../shared/ui/States';
import { navIcons } from './navIcons';
import { TargetProvider } from '../shared/targets/TargetContext';
import { EnvironmentProvider } from '../shared/environment/EnvironmentContext';
import { AdminSignIn } from '../shared/targets/AdminSignIn';
import { presentError, type ErrorPresentation } from '../shared/bridge/errorPresentation';

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
}

// Clients is the day-to-day workspace and sits right under the Dashboard.
// Per-host Inventory/Security/Health now live inside a client's detail, so
// they no longer appear as standalone nav entries. Administration holds app-wide
// settings and the error log.
const navGroups: { label: string; items: NavItem[] }[] = [
  {
    label: 'Fleet',
    items: [
      { to: '/', label: 'Dashboard', icon: navIcons.dashboard },
      { to: '/clients', label: 'Clients', icon: navIcons.clients },
      { to: '/activedirectory', label: 'Active Directory', icon: navIcons.activedirectory },
      { to: '/vulnerabilities', label: 'Vulnerabilities', icon: navIcons.vulnerabilities },
      { to: '/patchmanagement', label: 'Patch Management', icon: navIcons.patchmanagement },
      { to: '/printmanagement', label: 'Print Management', icon: navIcons.printmanagement },
      { to: '/networkscan', label: 'Network Scan', icon: navIcons.networkscan },
      { to: '/reporting', label: 'Report export', icon: navIcons.reporting },
    ],
  },
  {
    label: 'Administration',
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
  | { kind: 'error'; error: ErrorPresentation };

function useAppInfo(): AppInfoState {
  const [state, setState] = useState<AppInfoState>({ kind: 'loading' });
  useEffect(() => {
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then((appInfo) => setState({ kind: 'loaded', appInfo }))
      .catch((caught: unknown) => setState({
        kind: 'error',
        error: presentError(caught, { message: 'Application information could not be loaded.' }),
      }));
  }, []);
  return state;
}

interface TopBarProps {
  appInfo: AppInfoResponse | null;
  navigationOpen: boolean;
  navigationButtonRef: RefObject<HTMLButtonElement | null>;
  onOpenNavigation(): void;
}

/** Global status bar: responsive navigation/context (left) + session actions (right). */
function TopBar({ appInfo, navigationOpen, navigationButtonRef, onOpenNavigation }: TopBarProps) {
  const location = useLocation();
  const [restartError, setRestartError] = useState<ErrorPresentation | null>(null);
  return (
    <header className="flex min-h-14 shrink-0 flex-wrap items-center justify-between gap-2 border-b border-slate-800 bg-slate-950 px-3 py-2 sm:px-4 xl:px-6">
      <div className="flex min-w-0 items-center gap-2.5 text-xs text-muted">
        <button
          ref={navigationButtonRef}
          type="button"
          aria-label="Open navigation"
          aria-controls="mobile-main-navigation"
          aria-expanded={navigationOpen}
          onClick={onOpenNavigation}
          className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded border border-slate-700 text-slate-300 transition-colors hover:bg-slate-800 hover:text-white xl:hidden"
        >
          <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
            <path d="M4 7h16M4 12h16M4 17h16" />
          </svg>
        </button>
        <div className="min-w-0 truncate">
          <span className="hidden text-slate-600 xl:inline">Windows Enterprise Companion</span>
          <span className="mx-1.5 hidden text-slate-700 xl:inline">/</span>
          <span className="font-medium text-slate-300">{sectionLabelFor(location.pathname)}</span>
        </div>
      </div>
      <div className="flex min-w-0 flex-wrap items-center justify-end gap-1.5 sm:gap-2">
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
                    setRestartError(presentError(caught, {
                      message: 'The elevated application could not be started.',
                    })),
                  );
                }}
                className="px-2 py-0.5 text-xs font-normal"
              >
                <span className="hidden sm:inline">Restart as administrator</span>
                <span className="sm:hidden">Elevate</span>
              </Button>
            )}
          </>
        )}
        {restartError && (
          <CompactErrorState title="Elevation failed" {...restartError} className="w-full sm:max-w-80" />
        )}
      </div>
    </header>
  );
}

/** Compact runtime identity and a keyboard-accessible log-folder action. */
export function AppInfoFooter({ state }: { state: AppInfoState }) {
  const [openError, setOpenError] = useState<ErrorPresentation | null>(null);

  if (state.kind === 'loading') {
    return (
      <div className="mt-auto border-t border-slate-800 px-4 py-3 text-xs text-muted">
        Loading application information…
      </div>
    );
  }

  if (state.kind === 'error') {
    return (
      <div className="mt-auto border-t border-slate-800 px-3 py-3">
        <CompactErrorState title="Application information unavailable" {...state.error} />
      </div>
    );
  }

  const { appInfo } = state;
  return (
    <div className="mt-auto flex flex-col gap-1.5 border-t border-slate-800 px-4 py-3 text-xs text-muted">
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
            setOpenError(presentError(caught, { message: 'The log folder could not be opened.' }));
            return;
          }
          void request.catch((caught: unknown) => setOpenError(presentError(caught, {
            message: 'The log folder could not be opened.',
          })));
        }}
        className="self-start text-xs"
      >
        Open log folder
      </Button>
      {openError && (
        <CompactErrorState title="Log folder unavailable" {...openError} />
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

interface NavigationContentProps {
  appInfoState: AppInfoState;
  onNavigate?(): void;
  onClose?(): void;
}

/** One navigation tree rendered in either the desktop rail or the mobile drawer. */
function NavigationContent({ appInfoState, onNavigate, onClose }: NavigationContentProps) {
  return (
    <>
      <div className="flex items-center gap-2.5 border-b border-slate-800 px-4 py-4">
        <LogoMark className="h-7 w-7 shrink-0 text-accent-400" />
        <h1 className="min-w-0 flex-1 text-sm font-semibold leading-tight tracking-wide text-slate-200">
          Windows Enterprise Companion
        </h1>
        {onClose && (
          <button
            type="button"
            autoFocus
            aria-label="Close navigation"
            onClick={onClose}
            className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded text-slate-400 transition-colors hover:bg-slate-800 hover:text-white"
          >
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        )}
      </div>
      <nav className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-2" aria-label="Primary">
        {navGroups.map((group) => (
          <div key={group.label} className="flex flex-col gap-1">
            <span className="px-3 pb-0.5 text-[10px] font-semibold uppercase tracking-wider text-slate-600">
              {group.label}
            </span>
            {group.items.map((item) => (
              <NavLink key={item.to} to={item.to} end={item.to === '/'} className={navLinkClass} onClick={onNavigate}>
                {item.icon}
                {item.label}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>
      <AppInfoFooter state={appInfoState} />
    </>
  );
}

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

function ApplicationShell({ appInfoState }: { appInfoState: AppInfoState }) {
  const location = useLocation();
  const [navigationOpen, setNavigationOpen] = useState(false);
  const navigationButtonRef = useRef<HTMLButtonElement>(null);
  const appInfo = appInfoState.kind === 'loaded' ? appInfoState.appInfo : null;

  const closeNavigation = useCallback(() => {
    setNavigationOpen(false);
    navigationButtonRef.current?.focus();
  }, []);

  useEffect(() => {
    setNavigationOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!navigationOpen) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeNavigation();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [closeNavigation, navigationOpen]);

  return (
    <div className="flex h-dvh overflow-hidden bg-slate-950 text-slate-100">
      <aside data-testid="desktop-navigation" className="hidden w-56 shrink-0 flex-col border-r border-slate-800 bg-slate-900 xl:flex">
        <NavigationContent appInfoState={appInfoState} />
      </aside>

      {navigationOpen && (
        <>
          <div className="fixed inset-0 z-40 bg-slate-950/75 backdrop-blur-[1px] xl:hidden" aria-hidden="true" onMouseDown={closeNavigation} />
          <aside
            id="mobile-main-navigation"
            role="dialog"
            aria-modal="true"
            aria-label="Main navigation"
            className="fixed inset-y-0 left-0 z-50 flex w-[min(20rem,88vw)] flex-col border-r border-slate-700 bg-slate-900 shadow-2xl xl:hidden"
          >
            <NavigationContent appInfoState={appInfoState} onNavigate={closeNavigation} onClose={closeNavigation} />
          </aside>
        </>
      )}

      <main
        data-testid="application-main"
        inert={navigationOpen ? true : undefined}
        className="flex min-w-0 flex-1 flex-col overflow-hidden"
      >
        <TopBar
          appInfo={appInfo}
          navigationOpen={navigationOpen}
          navigationButtonRef={navigationButtonRef}
          onOpenNavigation={() => setNavigationOpen(true)}
        />
        <div data-testid="application-scroll-container" className="min-w-0 flex-1 overflow-y-auto p-3 sm:p-4 xl:p-6">
          <div className="mx-auto max-w-[1400px]">
            <AppRoutes />
          </div>
        </div>
      </main>
    </div>
  );
}

export function App() {
  const appInfoState = useAppInfo();

  return (
    <HashRouter>
      <TargetProvider>
      <EnvironmentProvider>
      <ApplicationShell appInfoState={appInfoState} />
      </EnvironmentProvider>
      </TargetProvider>
    </HashRouter>
  );
}
