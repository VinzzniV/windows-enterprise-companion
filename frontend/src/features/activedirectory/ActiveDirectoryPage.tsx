import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import type {
  AdHygieneResult,
  AdOverviewResult,
  DirectoryConnectionRequest,
  TestDirectoryConnectionResult,
} from '../../shared/api-types';
import { Card } from '../../shared/ui/Card';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Spinner } from '../../shared/ui/Spinner';
import { Button } from '../../shared/ui/Button';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { CompactErrorState, EmptyState, ErrorState } from '../../shared/ui/States';
import type { CredentialValues } from '../../shared/targets/TargetSelector';
import { useTargets } from '../../shared/targets/TargetContext';
import { SavedTargetsBar } from '../../shared/targets/SavedTargetsBar';
import { loadView, saveView } from '../../shared/viewCache';
import { DirectoryIdentityTable, privilegedIdentityRows, ruleIdentityRows } from './directoryIdentity';
import { DirectoryRuleBrowser } from './DirectoryRuleBrowser';
import { DirectoryPrivilegedGroupBrowser } from './DirectoryPrivilegedGroupBrowser';

/** What survives an app restart for this page — never the password. */
interface CachedAdView {
  form: ConnectionFormState;
  overview: AdOverviewResult | null;
  hygiene: AdHygieneResult | null;
}

const adViewKey = 'activedirectory';

/** What the admin should do next, per typed directory error. */
const adErrorActions: Record<string, string> = {
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

function directoryError(error: unknown, message: string): ErrorPresentation {
  const action = error instanceof BridgeInvokeError ? adErrorActions[error.error.code] : undefined;
  return presentError(error, { message, action });
}

interface ConnectionFormState {
  domain: string;
  server: string;
}

const emptyConnectionForm: ConnectionFormState = {
  domain: '',
  server: '',
};

// The bind account is the global admin sign-in (top bar) — the admin's own
// domain is the credential domain, distinct from the directory being analyzed.
function toConnectionRequest(
  form: ConnectionFormState,
  admin: CredentialValues | null,
): DirectoryConnectionRequest | null {
  const request: DirectoryConnectionRequest = {};
  if (form.domain.trim() !== '') request.domain = form.domain.trim();
  if (form.server.trim() !== '') request.server = form.server.trim();
  if (admin && admin.userName.trim() !== '') {
    request.userName = admin.userName.trim();
    request.userDomain = admin.domain.trim() || null;
    request.password = admin.password;
  }
  return Object.keys(request).length > 0 ? request : null;
}

type OverviewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; overview: AdOverviewResult }
  | { kind: 'error'; error: ErrorPresentation };

type HygieneState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; hygiene: AdHygieneResult }
  | { kind: 'error'; error: ErrorPresentation };

function OverviewStat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded border border-slate-800 bg-slate-900/50 px-4 py-3">
      <div className="text-2xl font-semibold tabular-nums">{value.toLocaleString()}</div>
      <div className="text-xs text-slate-400">{label}</div>
    </div>
  );
}

/**
 * One result category, summarized to a single row (title + count badge);
 * the details only render when the row is expanded. Native <details> keeps
 * this free of open/close state.
 */
function ResultCategory({
  title,
  count,
  tone,
  summary,
  children,
}: {
  title: string;
  count: number;
  tone: BadgeTone;
  summary?: string;
  children: React.ReactNode;
}) {
  return (
    <details className="group rounded-lg border border-slate-800 bg-slate-900/40">
      <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 text-sm [&::-webkit-details-marker]:hidden">
        <span className="text-slate-500 transition-transform group-open:rotate-90" aria-hidden>
          ▸
        </span>
        <span className="font-medium text-slate-100">{title}</span>
        <Badge tone={tone}>{count.toLocaleString()}</Badge>
        {summary && <span className="text-xs text-slate-400">{summary}</span>}
      </summary>
      <div className="border-t border-slate-800 px-4 py-3">{children}</div>
    </details>
  );
}

type TestBindState =
  | { kind: 'idle' }
  | { kind: 'testing' }
  | { kind: 'ok'; result: TestDirectoryConnectionResult }
  | { kind: 'error'; error: ErrorPresentation };

export function ActiveDirectoryPage() {
  const { adminCredentials, savedTargets, saveTarget, savedTargetsReady } = useTargets();
  // The stored view *is* the initial state — a restart opens on the last analysis
  // and the connection it used, with no flash of an empty form.
  const cached = useRef(loadView<CachedAdView>(adViewKey)).current;
  const [state, setState] = useState<OverviewState>(() =>
    cached?.overview ? { kind: 'loaded', overview: cached.overview } : { kind: 'idle' },
  );
  const [hygieneState, setHygieneState] = useState<HygieneState>(() =>
    cached?.hygiene ? { kind: 'loaded', hygiene: cached.hygiene } : { kind: 'idle' },
  );
  const [connectionForm, setConnectionForm] = useState<ConnectionFormState>(
    () => cached?.form ?? emptyConnectionForm,
  );
  const [testBindState, setTestBindState] = useState<TestBindState>({ kind: 'idle' });
  const [hygieneSearch, setHygieneSearch] = useState('');

  const testConnection = useCallback(() => {
    setTestBindState({ kind: 'testing' });
    invoke<TestDirectoryConnectionResult>(
      'activedirectory',
      'testConnection',
      { connection: toConnectionRequest(connectionForm, adminCredentials) },
    )
      .then((result) => {
        setTestBindState({ kind: 'ok', result });
        if (!result.domainJoined && connectionForm.domain.trim() === '') {
          setHygieneState({ kind: 'idle' });
        }
      })
      .catch((error: unknown) =>
        setTestBindState({
          kind: 'error',
          error: directoryError(error, 'The directory connection could not be verified.'),
        }),
      );
  }, [connectionForm, adminCredentials]);

  const loadOverview = useCallback(() => {
    setState({ kind: 'loading' });
    invoke<AdOverviewResult>(
      'activedirectory',
      'getOverview',
      { connection: toConnectionRequest(connectionForm, adminCredentials) },
    )
      .then((overview) => {
        setState({ kind: 'loaded', overview });
        if (!overview.domainJoined) {
          setHygieneState({ kind: 'idle' });
        }
        // Remember a pinned DC so it prefills next launch — like a saved print server.
        const host = connectionForm.server.trim();
        const alreadySaved = savedTargets.some(
          (target) =>
            target.role === 'DomainController' && target.host.toUpperCase() === host.toUpperCase(),
        );
        if (host !== '' && !alreadySaved) {
          void saveTarget({
            label: host,
            host,
            role: 'DomainController',
            userName: adminCredentials?.userName ?? null,
          });
        }
      })
      .catch((error: unknown) =>
        setState({
          kind: 'error',
          error: directoryError(error, 'The directory overview could not be loaded.'),
        }),
      );
  }, [connectionForm, adminCredentials, savedTargets, saveTarget]);

  const loadHygiene = useCallback(() => {
    setHygieneState({ kind: 'loading' });
    invoke<AdHygieneResult>(
      'activedirectory',
      'getHygiene',
      { connection: toConnectionRequest(connectionForm, adminCredentials) },
    )
      .then((hygiene) => {
        setHygieneSearch('');
        setHygieneState({ kind: 'loaded', hygiene });
      })
      .catch((error: unknown) =>
        setHygieneState({
          kind: 'error',
          error: directoryError(error, 'The directory hygiene checks could not be completed.'),
        }),
      );
  }, [connectionForm, adminCredentials]);

  const setForm = (patch: Partial<ConnectionFormState>) => {
    setConnectionForm((current) => ({ ...current, ...patch }));
    setState({ kind: 'idle' });
    setHygieneState({ kind: 'idle' });
    setTestBindState({ kind: 'idle' });
  };

  // Nothing cached yet: fall back to the newest saved DC so the user only has to hit
  // Analyze. The analysis itself stays manual — the bind needs the admin sign-in.
  const prefilledRef = useRef(false);
  useEffect(() => {
    if (prefilledRef.current || !savedTargetsReady || cached !== null) {
      return;
    }
    prefilledRef.current = true;
    const savedDc = savedTargets.filter((target) => target.role === 'DomainController').at(-1);
    if (savedDc) {
      setForm({ server: savedDc.host });
    }
  }, [savedTargetsReady, savedTargets, cached]);

  // Remember the form and the last successful results.
  useEffect(() => {
    saveView<CachedAdView>(adViewKey, {
      form: connectionForm,
      overview: state.kind === 'loaded' ? state.overview : null,
      hygiene: hygieneState.kind === 'loaded' ? hygieneState.hygiene : null,
    });
  }, [connectionForm, state, hygieneState]);

  const overview = state.kind === 'loaded' ? state.overview : null;
  const hygiene = hygieneState.kind === 'loaded' ? hygieneState.hygiene : null;
  const usesLocalDomain = connectionForm.domain.trim() === '';
  const knownWorkgroup =
    usesLocalDomain &&
    ((overview !== null && !overview.domainJoined) ||
      (hygiene !== null && !hygiene.domainJoined) ||
      (testBindState.kind === 'ok' && !testBindState.result.domainJoined));
  const privilegedRows = useMemo(
    () => privilegedIdentityRows(hygiene?.privilegedGroups ?? []),
    [hygiene?.privilegedGroups],
  );

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Active Directory"
        subtitle="Read-only directory analysis — nothing is ever written to AD"
      >
        <Button variant="primary" onClick={loadOverview} disabled={state.kind === 'loading'}>
          {state.kind === 'loading' ? 'Analyzing …' : 'Analyze directory'}
        </Button>
        <Button
          onClick={loadHygiene}
          disabled={hygieneState.kind === 'loading' || knownWorkgroup}
        >
          {knownWorkgroup
            ? 'Hygiene unavailable (workgroup)'
            : hygieneState.kind === 'loading'
              ? 'Checking …'
              : 'Run hygiene checks'}
        </Button>
      </PageHeader>

      <fieldset className="flex flex-col gap-2 rounded-lg border border-slate-800 bg-slate-900/40 p-3">
        <legend className="px-1 text-xs font-medium uppercase tracking-wide text-slate-400">
          Directory connection
        </legend>
        <p className="text-xs text-muted">
          Empty analyzes this machine's own domain. Enter a domain to analyze a different directory
          and a DC to pin the connection. The bind runs as the signed-in admin (top right), or the
          current user when not signed in.
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
        <SavedTargetsBar
          role="DomainController"
          label="Saved domain controllers"
          currentHost={connectionForm.server}
          currentUserName={adminCredentials?.userName ?? null}
          onPick={(target) => setForm({ server: target.host })}
        />
        <div className="flex flex-wrap items-center gap-3">
          <Button onClick={testConnection} disabled={testBindState.kind === 'testing'}>
            {testBindState.kind === 'testing' ? 'Testing …' : 'Test connection'}
          </Button>
          {testBindState.kind === 'ok' && testBindState.result.domainJoined && (
            <span className="text-sm text-ok-400">
              {`Connected — ${testBindState.result.domainName} (${testBindState.result.defaultNamingContext})`}
            </span>
          )}
        </div>
        {testBindState.kind === 'error' && (
          <CompactErrorState {...testBindState.error} />
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
        <ErrorState
          title="Directory analysis failed"
          {...state.error}
          controls={<Button onClick={loadOverview}>Retry directory analysis</Button>}
        />
      )}

      {knownWorkgroup && (
        <Card title="No local Active Directory domain">
          <p className="text-sm text-slate-300">
            This machine is in a workgroup, so local directory analysis and hygiene are not
            applicable. Enter a domain above to analyze a foreign directory, or join a domain.
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

          <ResultCategory
            title="Domain controllers"
            count={overview.domainControllers.length}
            tone={overview.domainControllers.length > 0 ? 'info' : 'warn'}
          >
            {overview.domainControllers.length === 0 ? (
              <p className="text-sm text-slate-400">
                No domain controllers were readable with the current credentials.
              </p>
            ) : (
              <ul className="flex flex-col gap-1 text-sm">
                {overview.domainControllers.map((dc) => (
                  <li key={dc.distinguishedName} className="flex flex-col">
                    <span>{dc.hostName}</span>
                    <span className="font-mono text-xs text-muted">{dc.distinguishedName}</span>
                  </li>
                ))}
              </ul>
            )}
          </ResultCategory>
        </section>
      )}

      {hygieneState.kind === 'error' && (
        <ErrorState
          title="Hygiene checks failed"
          {...hygieneState.error}
          controls={<Button onClick={loadHygiene}>Retry hygiene checks</Button>}
        />
      )}

      {hygiene?.domainJoined && (
        <section aria-label="Directory hygiene" className="flex flex-col gap-4">
          <h2 className="border-b border-slate-800 pb-1 text-sm font-medium uppercase tracking-wide text-slate-400">
            Hygiene
          </h2>
          <div className="rounded border border-slate-800 bg-slate-900/40 p-3">
            <Input
              type="search"
              value={hygieneSearch}
              onChange={(event) => setHygieneSearch(event.target.value)}
              placeholder="Search loaded name, account, path, finding or DN"
              aria-label="Search loaded hygiene identities"
              className="max-w-xl"
            />
            <p className="mt-2 text-xs text-slate-400">
              This searches only the bounded examples loaded with this hygiene result. Exact finding totals remain visible on each category.
            </p>
          </div>
          <ResultCategory
            title="Privileged groups"
            count={hygiene.privilegedGroups.length}
            tone="info"
            summary={`${hygiene.privilegedGroups.reduce((sum, group) => sum + group.directMemberCount, 0)} direct members in total`}
          >
            <DirectoryIdentityTable
              rows={privilegedRows}
              totalCount={hygiene.privilegedGroups.reduce((sum, group) => sum + group.directMemberCount, 0)}
              totalLabel="direct members"
              query={hygieneSearch}
            />
            <div className="flex flex-col gap-2">
              {hygiene.privilegedGroups
                .filter((group) => group.directMemberCount > 0)
                .map((group) => <DirectoryPrivilegedGroupBrowser
                  key={`${hygiene.capturedAtUtc}-${group.distinguishedName}`}
                  group={group}
                  connection={toConnectionRequest(connectionForm, adminCredentials)}
                />)}
            </div>
          </ResultCategory>

          {hygiene.rules.map((rule) => (
            <ResultCategory
              key={`${hygiene.capturedAtUtc}-${rule.ruleId}`}
              title={rule.title}
              count={rule.matchCount}
              tone={rule.matchCount > 0 ? 'warn' : 'ok'}
            >
              <div className="flex flex-col gap-2 text-sm">
                <p className="text-slate-400">{rule.recommendation}</p>
                <DirectoryIdentityTable
                  rows={ruleIdentityRows(rule)}
                  totalCount={rule.matchCount}
                  totalLabel="matches"
                  query={hygieneSearch}
                />
                {rule.matchCount > rule.examples.length && (
                  <DirectoryRuleBrowser
                    rule={rule}
                    evaluatedAtUtc={hygiene.capturedAtUtc}
                    connection={toConnectionRequest(connectionForm, adminCredentials)}
                  />
                )}
              </div>
            </ResultCategory>
          ))}
        </section>
      )}
    </div>
  );
}
