import { useCallback, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AdHygieneResult,
  AdOverviewResult,
  DirectoryConnectionRequest,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';
import { CredentialFields } from '../../shared/targets/TargetSelector';

function adErrorText(error: unknown): string {
  if (error instanceof BridgeInvokeError) {
    const details = error.error.details ? ` — ${error.error.details}` : '';
    return `${error.error.code}: ${error.error.message}${details}`;
  }
  return error instanceof Error ? error.message : String(error);
}

interface ConnectionFormState {
  domain: string;
  server: string;
  useExplicitCredentials: boolean;
  userName: string;
  userDomain: string;
  password: string;
}

const emptyConnectionForm: ConnectionFormState = {
  domain: '',
  server: '',
  useExplicitCredentials: false,
  userName: '',
  userDomain: '',
  password: '',
};

function toConnectionRequest(form: ConnectionFormState): DirectoryConnectionRequest | null {
  const request: DirectoryConnectionRequest = {};
  if (form.domain.trim() !== '') request.domain = form.domain.trim();
  if (form.server.trim() !== '') request.server = form.server.trim();
  if (form.useExplicitCredentials && form.userName.trim() !== '') {
    request.userName = form.userName.trim();
    request.userDomain = form.userDomain.trim() || null;
    request.password = form.password;
  }
  return Object.keys(request).length > 0 ? request : null;
}

const connectionInputClass =
  'rounded border border-slate-700 bg-slate-900 px-2 py-1 text-sm text-slate-100 ' +
  'placeholder:text-slate-500 focus:border-sky-500 focus:outline-none disabled:opacity-50';

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
  const [connectionForm, setConnectionForm] = useState<ConnectionFormState>(emptyConnectionForm);

  const loadOverview = useCallback(() => {
    setState({ kind: 'loading' });
    invoke<AdOverviewResult>(
      'activedirectory',
      'getOverview',
      { connection: toConnectionRequest(connectionForm) },
      120_000,
    )
      .then((overview) => setState({ kind: 'loaded', overview }))
      .catch((error: unknown) => setState({ kind: 'error', message: adErrorText(error) }));
  }, [connectionForm]);

  const loadHygiene = useCallback(() => {
    setHygieneState({ kind: 'loading' });
    invoke<AdHygieneResult>(
      'activedirectory',
      'getHygiene',
      { connection: toConnectionRequest(connectionForm) },
      120_000,
    )
      .then((hygiene) => setHygieneState({ kind: 'loaded', hygiene }))
      .catch((error: unknown) => setHygieneState({ kind: 'error', message: adErrorText(error) }));
  }, [connectionForm]);

  const setForm = (patch: Partial<ConnectionFormState>) =>
    setConnectionForm((current) => ({ ...current, ...patch }));

  const overview = state.kind === 'loaded' ? state.overview : null;
  const hygiene = hygieneState.kind === 'loaded' ? hygieneState.hygiene : null;

  return (
    <div className="flex flex-col gap-4">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold">Active Directory</h1>
          <p className="text-sm text-slate-400">
            Read-only directory analysis — runs as the current user unless explicit
            credentials are entered below. Nothing is ever written to AD.
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

      <fieldset className="flex flex-col gap-2 rounded-lg border border-slate-800 bg-slate-900/40 p-3">
        <legend className="px-1 text-xs font-medium uppercase tracking-wide text-slate-400">
          Directory connection (optional — empty analyzes this machine's domain as the current user)
        </legend>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          <input
            type="text"
            value={connectionForm.domain}
            onChange={(event) => setForm({ domain: event.target.value })}
            placeholder="Domain (DNS name, e.g. contoso.local)"
            aria-label="Domain"
            className={connectionInputClass}
          />
          <input
            type="text"
            value={connectionForm.server}
            onChange={(event) => setForm({ server: event.target.value })}
            placeholder="Domain controller (optional)"
            aria-label="Domain controller"
            className={connectionInputClass}
          />
        </div>
        <label className="flex items-center gap-1.5 text-sm">
          <input
            type="checkbox"
            checked={connectionForm.useExplicitCredentials}
            onChange={(event) => setForm({ useExplicitCredentials: event.target.checked })}
          />
          Use explicit credentials
        </label>
        {connectionForm.useExplicitCredentials && (
          <CredentialFields
            values={{
              userName: connectionForm.userName,
              domain: connectionForm.userDomain,
              password: connectionForm.password,
            }}
            onChange={(patch) =>
              setForm({
                ...(patch.userName !== undefined && { userName: patch.userName }),
                ...(patch.domain !== undefined && { userDomain: patch.domain }),
                ...(patch.password !== undefined && { password: patch.password }),
              })
            }
            domainPlaceholder="Credential domain (optional)"
            domainAriaLabel="Credential domain"
          />
        )}
      </fieldset>

      {state.kind === 'idle' && (
        <p className="text-sm text-slate-400">
          Run the analysis to query the domain this machine is joined to — or name another
          domain/DC above.
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
