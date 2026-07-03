import { useEffect, useState } from 'react';
import { HashRouter, Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { HardwareInfoPage } from '../features/inventory/HardwareInfoPage';
import { SecurityPage } from '../features/security/SecurityPage';
import { DiagnosticsPage } from '../features/diagnostics/DiagnosticsPage';
import { ActiveDirectoryPage } from '../features/activedirectory/ActiveDirectoryPage';
import { PatchManagementPage } from '../features/patchmanagement/PatchManagementPage';
import { ReportingPage } from '../features/reporting/ReportingPage';
import { invoke } from '../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../shared/api-types';
import { StatusBadge } from '../shared/ui/StatusBadge';
import { LogoMark } from '../shared/ui/LogoMark';
import { ErrorBoundary } from '../shared/ui/ErrorBoundary';
import { SplashIntro } from './SplashIntro';
import { navIcons } from './navIcons';

const navigation = [
  { to: '/inventory', label: 'Inventory', icon: navIcons.inventory },
  { to: '/security', label: 'Security', icon: navIcons.security },
  { to: '/diagnostics', label: 'Diagnostics', icon: navIcons.diagnostics },
  { to: '/activedirectory', label: 'Active Directory', icon: navIcons.activedirectory },
  { to: '/patchmanagement', label: 'Patch Management', icon: navIcons.patchmanagement },
  { to: '/reporting', label: 'Reporting', icon: navIcons.reporting },
];

function AppInfoFooter() {
  const [appInfo, setAppInfo] = useState<AppInfoResponse | null>(null);

  useEffect(() => {
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then(setAppInfo)
      .catch(() => setAppInfo(null));
  }, []);

  if (!appInfo) {
    return null;
  }

  return (
    <div className="mt-auto flex flex-col gap-1.5 border-t border-slate-800 px-4 py-3 text-xs text-slate-500">
      <div className="flex items-center justify-between gap-2">
        <span className="min-w-0 truncate" title={`v${appInfo.version}`}>
          v{appInfo.version}
        </span>
        <span className="shrink-0 whitespace-nowrap">
          <StatusBadge variant={appInfo.isElevated ? 'elevation' : 'neutral'}>
            {appInfo.isElevated ? 'Administrator' : 'Standard user'}
          </StatusBadge>
        </span>
      </div>
      {!appInfo.isElevated && (
        <button
          type="button"
          title="Starts an elevated copy via the UAC prompt and closes this one"
          onClick={() => {
            // A dismissed UAC prompt is a valid outcome; errors surface in the host log
            invoke('system', 'restartElevated', {}, 120_000).catch(() => {});
          }}
          className="self-start rounded border border-slate-700 px-1.5 py-0.5 text-[10px] text-slate-400 transition-colors hover:bg-slate-800 hover:text-slate-200"
        >
          Restart as administrator
        </button>
      )}
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
          className="shrink-0 rounded border border-slate-700 px-1.5 py-0.5 text-[10px] text-slate-400 transition-colors hover:bg-slate-800 hover:text-slate-200"
        >
          Open
        </button>
      </span>
    </div>
  );
}

function AppRoutes() {
  const location = useLocation();

  return (
    // Key on the path so route changes replay the enter animation and
    // reset the error boundary
    <div key={location.pathname} className="wec-page-enter">
      <ErrorBoundary>
        <Routes>
          <Route path="/" element={<Navigate to="/inventory" replace />} />
          <Route path="/inventory" element={<HardwareInfoPage />} />
          <Route path="/security" element={<SecurityPage />} />
          <Route path="/diagnostics" element={<DiagnosticsPage />} />
          <Route path="/activedirectory" element={<ActiveDirectoryPage />} />
          <Route path="/patchmanagement" element={<PatchManagementPage />} />
          <Route path="/reporting" element={<ReportingPage />} />
        </Routes>
      </ErrorBoundary>
    </div>
  );
}

export function App() {
  const [introDone, setIntroDone] = useState(false);

  return (
    <HashRouter>
      <div className="flex h-screen bg-slate-950 text-slate-100">
        <aside className="flex w-56 shrink-0 flex-col border-r border-slate-800 bg-slate-900">
          <div className="flex items-center gap-2.5 border-b border-slate-800 px-4 py-4">
            <LogoMark className="h-7 w-7 shrink-0 text-slate-300" />
            <h1 className="text-sm font-semibold leading-tight tracking-wide text-slate-200">
              Windows Enterprise Companion
            </h1>
          </div>
          <nav className="flex flex-col gap-1 p-2">
            {navigation.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) =>
                  `relative flex items-center gap-2.5 rounded px-3 py-2 text-sm transition-colors ${
                    isActive
                      ? 'bg-slate-800 font-medium text-white before:absolute before:inset-y-1.5 before:left-0 before:w-0.5 before:rounded-full before:bg-sky-400'
                      : 'text-slate-400 hover:bg-slate-800/50 hover:text-slate-200'
                  }`
                }
              >
                {item.icon}
                {item.label}
              </NavLink>
            ))}
          </nav>
          <AppInfoFooter />
        </aside>
        <main className="flex-1 overflow-y-auto p-6">
          <div className="mx-auto max-w-[1400px]">
            <AppRoutes />
          </div>
        </main>
      </div>
      {!introDone && <SplashIntro onDone={() => setIntroDone(true)} />}
    </HashRouter>
  );
}
