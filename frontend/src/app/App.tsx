import { HashRouter, Navigate, NavLink, Route, Routes } from 'react-router-dom';
import { HardwareInfoPage } from '../features/inventory/HardwareInfoPage';

const navigation = [{ to: '/inventory', label: 'Inventory' }];

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
        </aside>
        <main className="flex-1 overflow-y-auto p-6">
          <Routes>
            <Route path="/" element={<Navigate to="/inventory" replace />} />
            <Route path="/inventory" element={<HardwareInfoPage />} />
          </Routes>
        </main>
      </div>
    </HashRouter>
  );
}
