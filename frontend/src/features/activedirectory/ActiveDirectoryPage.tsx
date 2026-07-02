import { useCallback, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { AdHygieneResult, AdOverviewResult } from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';

type OverviewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; overview: AdOverviewResult }
  | { kind: 'error'; message: string };

type HygieneState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; hygiene: AdHygieneResult }
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
  const [hygieneState, setHygieneState] = useState<HygieneState>({ kind: 'idle' });

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

  const loadHygiene = useCallback(() => {
    setHygieneState({ kind: 'loading' });
    invoke<AdHygieneResult>('activedirectory', 'getHygiene', {}, 120_000)
      .then((hygiene) => setHygieneState({ kind: 'loaded', hygiene }))
      .catch((error: unknown) =>
        setHygieneState({
          kind: 'error',
          message: error instanceof Error ? error.message : String(error),
        }),
      );
  }, []);

  const overview = state.kind === 'loaded' ? state.overview : null;
  const hygiene = hygieneState.kind === 'loaded' ? hygieneState.hygiene : null;

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold">Active Directory</h1>
          <p className="text-sm text-slate-400">
            Read-only directory overview as the current user — nothing is ever written to AD.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button
            type="button"
            onClick={loadOverview}
            disabled={state.kind === 'loading'}
            className="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-600 disabled:opacity-50"
          >
            {state.kind === 'loading' ? 'Analyzing …' : 'Analyze directory'}
          </button>
          <button
            type="button"
            onClick={loadHygiene}
            disabled={hygieneState.kind === 'loading'}
            className="rounded border border-slate-600 px-3 py-1.5 text-sm font-medium text-slate-200 transition-colors hover:bg-slate-800 disabled:opacity-50"
          >
            {hygieneState.kind === 'loading' ? 'Checking …' : 'Run hygiene checks'}
          </button>
        </div>
      </header>

      {state.kind === 'idle' && (
        <p className="text-sm text-slate-400">
          Run the analysis to query the domain this machine is joined to.
        </p>
      )}

      {state.kind === 'loading' && <Spinner label="Querying the directory …" />}

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

      {hygieneState.kind === 'error' && (
        <Card title="Hygiene check error">
          <p className="text-sm text-red-400">{hygieneState.message}</p>
        </Card>
      )}

      {hygiene && !hygiene.domainJoined && (
        <Card title="Hygiene checks">
          <p className="text-sm text-slate-300">Not domain-joined — nothing to check.</p>
        </Card>
      )}

      {hygiene?.domainJoined && (
        <>
          <Card title={`Privileged groups (${hygiene.privilegedGroups.length})`}>
            <ul className="flex flex-col gap-3 text-sm">
              {hygiene.privilegedGroups.map((group) => (
                <li key={group.distinguishedName} className="flex flex-col gap-0.5">
                  <span className="font-medium">
                    {group.groupName}
                    <span className="ml-2 text-xs font-normal text-slate-400">
                      {group.directMemberCount} direct member{group.directMemberCount === 1 ? '' : 's'}
                    </span>
                  </span>
                  {group.memberDistinguishedNames.map((member) => (
                    <span key={member} className="font-mono text-xs text-slate-500">
                      {member}
                    </span>
                  ))}
                  {group.directMemberCount > group.memberDistinguishedNames.length && (
                    <span className="text-xs text-slate-500">
                      … and {group.directMemberCount - group.memberDistinguishedNames.length} more
                    </span>
                  )}
                </li>
              ))}
            </ul>
          </Card>

          {hygiene.rules.map((rule) => (
            <Card key={rule.ruleId} title={`${rule.title} — ${rule.matchCount}`}>
              <div className="flex flex-col gap-2 text-sm">
                <p className="text-slate-400">{rule.recommendation}</p>
                {rule.examples.length > 0 && (
                  <ul className="flex flex-col gap-1">
                    {rule.examples.map((account) => (
                      <li key={account.distinguishedName} className="flex items-baseline gap-2">
                        <span>{account.name}</span>
                        {account.lastLogonUtc && (
                          <span className="text-xs text-slate-500">
                            last logon {new Date(account.lastLogonUtc).toLocaleDateString()}
                          </span>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
                {rule.matchCount > rule.examples.length && (
                  <p className="text-xs text-slate-500">
                    Showing {rule.examples.length} of {rule.matchCount} matches.
                  </p>
                )}
              </div>
            </Card>
          ))}
        </>
      )}
    </div>
  );
}
