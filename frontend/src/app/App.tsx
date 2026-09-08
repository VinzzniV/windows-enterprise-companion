import { Suspense, useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { HashRouter, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { invoke } from '../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../shared/api-types';
import { StatusBadge } from '../shared/ui/StatusBadge';
import { LogoMark } from '../shared/ui/LogoMark';
import { ErrorBoundary } from '../shared/ui/ErrorBoundary';
import { Button } from '../shared/ui/Button';
import { CompactErrorState } from '../shared/ui/States';
import { TargetProvider } from '../shared/targets/TargetContext';
import { EnvironmentProvider } from '../shared/environment/EnvironmentContext';
import { AdminSignIn } from '../shared/targets/AdminSignIn';
import { presentError, type ErrorPresentation } from '../shared/bridge/errorPresentation';
import { appRoutes, navigationGroups, sectionLabelFor, type AppRouteDefinition } from './routeRegistry';
import { Spinner } from '../shared/ui/Spinner';
import { GlobalSearch } from './GlobalSearch';
import { useEnvironmentRequest } from '../shared/environment/EnvironmentContext';
import { clientListScope, isClientListUrl, readClientListUrl, rememberClientListUrl } from '../features/clients/clientListNavigation';

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
  searchButtonRef: RefObject<HTMLButtonElement | null>;
  onOpenNavigation(): void;
  onOpenSearch(): void;
}

/** Global status bar: responsive navigation/context (left) + session actions (right). */
function TopBar({
  appInfo,
  navigationOpen,
  navigationButtonRef,
  searchButtonRef,
  onOpenNavigation,
  onOpenSearch,
}: TopBarProps) {
  const location = useLocation();

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
        <Button
          ref={searchButtonRef}
          variant="secondary"
          aria-label="Open global search"
          aria-keyshortcuts="Control+K Meta+K"
          onClick={onOpenSearch}
          className="px-2 py-1 text-xs font-normal"
        >
          <span className="hidden sm:inline">Search</span>
          <kbd className="ml-1 hidden text-[10px] text-muted lg:inline">Ctrl K</kbd>
          <span className="sm:hidden">⌕</span>
        </Button>
        <AdminSignIn />
        {appInfo && (
          <>
            <StatusBadge variant={appInfo.isElevated ? 'elevation' : 'neutral'}>
              {appInfo.isElevated ? 'Local app: administrator' : 'Local app: standard rights'}
            </StatusBadge>
          </>
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
  const location = useLocation();
  const environmentRequest = useEnvironmentRequest();
  const clientsScope = clientListScope(environmentRequest);
  const currentUrl = `${location.pathname}${location.search}`;
  const clientDetailState = location.state as { returnTo?: unknown; scope?: unknown } | null;
  const clientsTarget = location.pathname === '/clients'
    ? currentUrl
    : clientDetailState?.scope === clientsScope && isClientListUrl(clientDetailState.returnTo)
      ? clientDetailState.returnTo
      : readClientListUrl(clientsScope);

  useEffect(() => {
    if (location.pathname === '/clients') rememberClientListUrl(currentUrl, clientsScope);
  }, [clientsScope, currentUrl, location.pathname]);

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
        {navigationGroups.map((group) => (
          <div key={group.key} className="flex flex-col gap-1">
            <span className="px-3 pb-0.5 text-[10px] font-semibold uppercase tracking-wider text-slate-600">
              {group.label}
            </span>
            {group.items.map((item) => (
              <NavLink key={item.to} to={item.to === '/clients' ? clientsTarget : item.to} end={item.to === '/'} className={navLinkClass} onClick={onNavigate}>
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

interface AppRoutesProps {
  routes?: readonly AppRouteDefinition[];
}

export function AppRoutes({ routes = appRoutes }: AppRoutesProps) {
  const location = useLocation();

  return (
    // Key on the path so route changes reset the error boundary.
    <div key={location.pathname}>
      <ErrorBoundary>
        <Suspense fallback={(
          <div className="flex min-h-48 items-center justify-center rounded-lg border border-slate-800 bg-slate-950/40">
            <Spinner label="Loading workspace…" />
          </div>
        )}>
          <Routes>
            {routes.map(({ id, path, Component }) => <Route key={id} path={path} element={<Component />} />)}
          </Routes>
        </Suspense>
      </ErrorBoundary>
    </div>
  );
}

function ApplicationShell({ appInfoState }: { appInfoState: AppInfoState }) {
  const location = useLocation();
  const [navigationOpen, setNavigationOpen] = useState(false);
  const [globalSearchOpen, setGlobalSearchOpen] = useState(false);
  const navigationButtonRef = useRef<HTMLButtonElement>(null);
  const searchButtonRef = useRef<HTMLButtonElement>(null);
  const appInfo = appInfoState.kind === 'loaded' ? appInfoState.appInfo : null;

  const closeNavigation = useCallback(() => {
    setNavigationOpen(false);
    navigationButtonRef.current?.focus();
  }, []);

  const openGlobalSearch = useCallback(() => {
    setNavigationOpen(false);
    setGlobalSearchOpen(true);
  }, []);

  const closeGlobalSearch = useCallback(() => {
    setGlobalSearchOpen(false);
    requestAnimationFrame(() => searchButtonRef.current?.focus());
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

  useEffect(() => {
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase() === 'k') {
        event.preventDefault();
        openGlobalSearch();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [openGlobalSearch]);

  return (
    <div className="flex h-dvh overflow-hidden bg-slate-950 text-slate-100">
      <aside
        data-testid="desktop-navigation"
        inert={globalSearchOpen ? true : undefined}
        className="hidden w-56 shrink-0 flex-col border-r border-slate-800 bg-slate-900 xl:flex"
      >
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
        inert={navigationOpen || globalSearchOpen ? true : undefined}
        className="flex min-w-0 flex-1 flex-col overflow-hidden"
      >
        <TopBar
          appInfo={appInfo}
          navigationOpen={navigationOpen}
          navigationButtonRef={navigationButtonRef}
          searchButtonRef={searchButtonRef}
          onOpenNavigation={() => setNavigationOpen(true)}
          onOpenSearch={openGlobalSearch}
        />
        <div data-testid="application-scroll-container" data-scroll-container="application" className="min-w-0 flex-1 overflow-y-auto p-3 sm:p-4 xl:p-6">
          <div className="mx-auto max-w-[1400px]">
            <AppRoutes />
          </div>
        </div>
      </main>
      <GlobalSearch open={globalSearchOpen} onClose={closeGlobalSearch} />
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
