import { useCallback, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { AdOverviewResult } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';

type OverviewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; overview: AdOverviewResult }
  | { kind: 'error'; message: string };

function OverviewStat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded border border-slate-800 bg-slate-900/50 px-4 py-3">
      <div className="text-2xl font-semibold tabular-nums">{value.toLocaleString()}</div>
      <div className="text-xs text-slate-400">{label}</div>
    </div>
  );
}

export function ActiveDirectoryPage() {
  const [state, setState] = useState<OverviewState>({ kind: 'idle' });

  const loadOverview = useCallback(() => {
    setState({ kind: 'loading' });
    invoke<AdOverviewResult>('activedirectory', 'getOverview', {}, 120_000)
      .then((overview) => setState({ kind: 'loaded', overview }))
      .catch((error: unknown) =>
        setState({
          kind: 'error',
          message: error instanceof Error ? error.message : String(error),
        }),
      );
  }, []);

  const overview = state.kind === 'loaded' ? state.overview : null;

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold">Active Directory</h1>
          <p className="text-sm text-slate-400">
            Read-only directory overview as the current user — nothing is ever written to AD.
          </p>
        </div>
        <button
          type="button"
          onClick={loadOverview}
          disabled={state.kind === 'loading'}
          className="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-600 disabled:opacity-50"
        >
          {state.kind === 'loading' ? 'Analyzing …' : 'Analyze directory'}
        </button>
      </header>

      {state.kind === 'idle' && (
        <p className="text-sm text-slate-400">
          Run the analysis to query the domain this machine is joined to.
        </p>
      )}

      {state.kind === 'error' && (
        <Card title="Error">
          <p className="text-sm text-red-400">{state.message}</p>
        </Card>
      )}

      {overview && !overview.domainJoined && (
        <Card title="Not domain-joined">
          <p className="text-sm text-slate-300">
            This machine is in a workgroup — there is no directory to analyze. Join a domain to
            use this module.
          </p>
        </Card>
      )}

      {overview?.domainJoined && (
        <>
          <Card title={`Domain: ${overview.domainName ?? 'unknown'}`}>
            <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
              <dt className="text-slate-400">Naming context</dt>
              <dd className="font-mono text-xs">{overview.defaultNamingContext}</dd>
              <dt className="text-slate-400">Captured</dt>
              <dd>{new Date(overview.capturedAtUtc).toLocaleString()}</dd>
            </dl>
          </Card>

          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <OverviewStat label="Users" value={overview.userCount} />
            <OverviewStat label="Disabled users" value={overview.disabledUserCount} />
            <OverviewStat label="Groups" value={overview.groupCount} />
            <OverviewStat label="Computers" value={overview.computerCount} />
          </div>

          <Card title={`Domain controllers (${overview.domainControllers.length})`}>
            {overview.domainControllers.length === 0 ? (
              <p className="text-sm text-slate-400">
                No domain controllers were readable with the current credentials.
              </p>
            ) : (
              <ul className="flex flex-col gap-1 text-sm">
                {overview.domainControllers.map((dc) => (
                  <li key={dc.distinguishedName} className="flex flex-col">
                    <span>{dc.hostName}</span>
                    <span className="font-mono text-xs text-slate-500">{dc.distinguishedName}</span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </>
      )}
    </div>
  );
}
