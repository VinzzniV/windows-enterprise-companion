import { useEffect, useState } from 'react';
import { HashRouter, Navigate, NavLink, Route, Routes } from 'react-router-dom';
import { HardwareInfoPage } from '../features/inventory/HardwareInfoPage';
import { SecurityPage } from '../features/security/SecurityPage';
import { DiagnosticsPage } from '../features/diagnostics/DiagnosticsPage';
import { invoke } from '../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../shared/api-types';
import { StatusBadge } from '../shared/ui/StatusBadge';

const navigation = [
  { to: '/inventory', label: 'Inventory' },
  { to: '/security', label: 'Security' },
  { to: '/diagnostics', label: 'Diagnostics' },
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

export function App() {
  return (
    <HashRouter>
      <div className="flex h-screen bg-slate-950 text-slate-100">
        <aside className="flex w-56 shrink-0 flex-col border-r border-slate-800 bg-slate-900">
          <div className="border-b border-slate-800 px-4 py-4">
            <h1 className="text-sm font-semibold tracking-wide text-slate-200">
              Windows Enterprise Companion
            </h1>
          </div>
          <nav className="flex flex-col gap-1 p-2">
            {navigation.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) =>
                  `rounded px-3 py-2 text-sm transition-colors ${
                    isActive
                      ? 'bg-slate-800 font-medium text-white'
                      : 'text-slate-400 hover:bg-slate-800/50 hover:text-slate-200'
                  }`
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>
          <AppInfoFooter />
        </aside>
        <main className="flex-1 overflow-y-auto p-6">
          <Routes>
            <Route path="/" element={<Navigate to="/inventory" replace />} />
            <Route path="/inventory" element={<HardwareInfoPage />} />
            <Route path="/security" element={<SecurityPage />} />
            <Route path="/diagnostics" element={<DiagnosticsPage />} />
          </Routes>
        </main>
      </div>
    </HashRouter>
  );
}
