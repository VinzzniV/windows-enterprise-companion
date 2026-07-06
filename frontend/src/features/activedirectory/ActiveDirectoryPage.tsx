import { useCallback, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import type {
  AdHygieneResult,
  AdOverviewResult,
  DirectoryConnectionRequest,
  TestDirectoryConnectionResult,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { Input } from '../../shared/ui/Input';
import { Checkbox } from '../../shared/ui/Checkbox';
import { PageHeader } from '../../shared/ui/PageHeader';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { CredentialFields } from '../../shared/targets/TargetSelector';

/** What the admin should do next, per typed directory error. */
const adErrorHints: Record<string, string> = {
  DNS_RESOLUTION_FAILED:
    "Point this machine's DNS at a server that knows the domain (usually a domain controller), or enter a specific DC above.",
  AUTHENTICATION_FAILED:
    'The LDAP bind was rejected — check user name, credential domain and password.',
  DIRECTORY_UNAVAILABLE:
    'No domain controller answered — check that a DC is running and TCP 389 is not blocked by a firewall.',
  CONNECTION_TIMEOUT:
    'The directory did not answer in time — check the network path, or pin a closer domain controller above.',
  NOT_FOUND:
    'The naming context was not found — verify the domain name is the DNS name of the directory (e.g. corp.contoso.com).',
  ACCESS_DENIED:
    'The account authenticated but was refused read access — use an account with directory read rights.',
};

function adError(error: unknown): { message: string; hint?: string } {
  if (error instanceof BridgeInvokeError) {
    const details = error.error.details ? ` — ${error.error.details}` : '';
    return {
      message: `${error.error.code}: ${error.error.message}${details}`,
      hint: adErrorHints[error.error.code],
    };
  }
  return { message: error instanceof Error ? error.message : String(error) };
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

type OverviewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; overview: AdOverviewResult }
  | { kind: 'error'; message: string; hint?: string };

type HygieneState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; hygiene: AdHygieneResult }
  | { kind: 'error'; message: string; hint?: string };

function OverviewStat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded border border-slate-800 bg-slate-900/50 px-4 py-3">
      <div className="text-2xl font-semibold tabular-nums">{value.toLocaleString()}</div>
      <div className="text-xs text-slate-400">{label}</div>
    </div>
  );
}

type TestBindState =
  | { kind: 'idle' }
  | { kind: 'testing' }
  | { kind: 'ok'; result: TestDirectoryConnectionResult }
  | { kind: 'error'; message: string; hint?: string };

export function ActiveDirectoryPage() {
  const [state, setState] = useState<OverviewState>({ kind: 'idle' });
  const [hygieneState, setHygieneState] = useState<HygieneState>({ kind: 'idle' });
  const [connectionForm, setConnectionForm] = useState<ConnectionFormState>(emptyConnectionForm);
  const [testBindState, setTestBindState] = useState<TestBindState>({ kind: 'idle' });

  const testConnection = useCallback(() => {
    setTestBindState({ kind: 'testing' });
    invoke<TestDirectoryConnectionResult>(
      'activedirectory',
      'testConnection',
      { connection: toConnectionRequest(connectionForm) },
      120_000,
    )
      .then((result) => setTestBindState({ kind: 'ok', result }))
      .catch((error: unknown) => setTestBindState({ kind: 'error', ...adError(error) }));
  }, [connectionForm]);

  const loadOverview = useCallback(() => {
    setState({ kind: 'loading' });
    invoke<AdOverviewResult>(
      'activedirectory',
      'getOverview',
      { connection: toConnectionRequest(connectionForm) },
      120_000,
    )
      .then((overview) => setState({ kind: 'loaded', overview }))
      .catch((error: unknown) => setState({ kind: 'error', ...adError(error) }));
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
      .catch((error: unknown) => setHygieneState({ kind: 'error', ...adError(error) }));
  }, [connectionForm]);

  const setForm = (patch: Partial<ConnectionFormState>) =>
    setConnectionForm((current) => ({ ...current, ...patch }));

  const overview = state.kind === 'loaded' ? state.overview : null;
  const hygiene = hygieneState.kind === 'loaded' ? hygieneState.hygiene : null;

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Active Directory"
        subtitle="Read-only directory analysis — nothing is ever written to AD"
      >
        <Button variant="primary" onClick={loadOverview} disabled={state.kind === 'loading'}>
          {state.kind === 'loading' ? 'Analyzing …' : 'Analyze directory'}
        </Button>
        <Button onClick={loadHygiene} disabled={hygieneState.kind === 'loading'}>
          {hygieneState.kind === 'loading' ? 'Checking …' : 'Run hygiene checks'}
        </Button>
      </PageHeader>

      <fieldset className="flex flex-col gap-2 rounded-lg border border-slate-800 bg-slate-900/40 p-3">
        <legend className="px-1 text-xs font-medium uppercase tracking-wide text-slate-400">
          Directory connection
        </legend>
        <p className="text-xs text-slate-500">
          Empty analyzes this machine's own domain as the current user. Enter a domain to analyze
          a different directory, a DC to pin the connection, and credentials to run as another account.
        </p>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          <Input
            type="text"
            value={connectionForm.domain}
            onChange={(event) => setForm({ domain: event.target.value })}
            placeholder="Domain (DNS name, e.g. contoso.local)"
            aria-label="Domain"
          />
          <Input
            type="text"
            value={connectionForm.server}
            onChange={(event) => setForm({ server: event.target.value })}
            placeholder="Domain controller (optional)"
            aria-label="Domain controller"
          />
        </div>
        <Checkbox
          label="Use explicit credentials"
          checked={connectionForm.useExplicitCredentials}
          onChange={(event) => setForm({ useExplicitCredentials: event.target.checked })}
        />
        {connectionForm.useExplicitCredentials && (
          <>
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
            <p className="text-xs text-slate-500">
              The credential domain is the account's domain — not necessarily the directory being
              analyzed. Accepted forms: <span className="font-mono">user@domain.tld</span>,{' '}
              <span className="font-mono">DOMAIN\user</span>, or user + credential domain.
            </p>
            {connectionForm.userName.trim() !== '' &&
              !connectionForm.userName.includes('@') &&
              !connectionForm.userName.includes('\\') &&
              connectionForm.userDomain.trim() === '' && (
                <p className="text-xs text-warn-400">
                  {connectionForm.domain.trim() !== ''
                    ? `No credential domain set — "${connectionForm.domain.trim()}" (the directory domain) will be used.`
                    : 'This user name has no domain. Enter it as user@domain.tld or DOMAIN\\user, or fill in the credential domain.'}
                </p>
              )}
          </>
        )}
        <div className="flex flex-wrap items-center gap-3">
          <Button onClick={testConnection} disabled={testBindState.kind === 'testing'}>
            {testBindState.kind === 'testing' ? 'Testing …' : 'Test connection'}
          </Button>
          {testBindState.kind === 'ok' && (
            <span className="text-sm text-ok-400">
              {testBindState.result.domainJoined
                ? `Connected — ${testBindState.result.domainName} (${testBindState.result.defaultNamingContext})`
                : 'This machine is not domain-joined and no domain was entered.'}
            </span>
          )}
        </div>
        {testBindState.kind === 'error' && (
          <div role="alert" className="flex flex-col gap-1">
            <p className="break-words text-sm text-fail-400">{testBindState.message}</p>
            {testBindState.hint && <p className="text-xs text-slate-400">{testBindState.hint}</p>}
          </div>
        )}
      </fieldset>

      {state.kind === 'idle' && (
        <EmptyState
          title="No analysis yet"
          message="Run the analysis to query the domain this machine is joined to — or name another domain/DC above."
        />
      )}

      {state.kind === 'loading' && <Spinner label="Querying the directory …" />}

      {state.kind === 'error' && (
        <ErrorState title="Directory analysis failed" message={state.message} hint={state.hint} />
      )}

      {overview && !overview.domainJoined && (
        <Card title="Not domain-joined">
          <p className="text-sm text-slate-300">
            This machine is in a workgroup — there is no directory to analyze. Enter a domain
            above to analyze a foreign directory, or join a domain.
          </p>
        </Card>
      )}

      {overview?.domainJoined && (
        <section aria-label="Directory overview" className="flex flex-col gap-4">
          <h2 className="border-b border-slate-800 pb-1 text-sm font-medium uppercase tracking-wide text-slate-400">
            Overview
          </h2>
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
        </section>
      )}

      {hygieneState.kind === 'error' && (
        <ErrorState
          title="Hygiene checks failed"
          message={hygieneState.message}
          hint={hygieneState.hint}
        />
      )}

      {hygiene && !hygiene.domainJoined && (
        <Card title="Hygiene checks">
          <p className="text-sm text-slate-300">Not domain-joined — nothing to check.</p>
        </Card>
      )}

      {hygiene?.domainJoined && (
        <section aria-label="Directory hygiene" className="flex flex-col gap-4">
          <h2 className="border-b border-slate-800 pb-1 text-sm font-medium uppercase tracking-wide text-slate-400">
            Hygiene
          </h2>
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
        </section>
      )}
    </div>
  );
}
