import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../../shared/api-types';
import { useTargets } from '../../shared/targets/TargetContext';
import type { CredentialValues } from '../../shared/targets/TargetSelector';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Button } from '../../shared/ui/Button';
import { Badge } from '../../shared/ui/Badge';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { clientKey, isLocalClient, toClientTarget } from './clients';
import { InventorySection } from './sections/InventorySection';
import { SecuritySection } from './sections/SecuritySection';
import { DiagnosticsSection } from './sections/DiagnosticsSection';
import { EventLogSection } from './sections/EventLogSection';
import { PrintersSection } from './sections/PrintersSection';
import { ReportingSection } from '../reporting/ReportingSection';
import { OverviewSection } from './sections/OverviewSection';
import { openPsSession } from '../../shared/ps/openPsSession';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';

type SectionKey = 'overview' | 'inventory' | 'security' | 'diagnostics' | 'events' | 'printers' | 'reporting';

const SECTIONS: { key: SectionKey; label: string }[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'inventory', label: 'Inventory' },
  { key: 'security', label: 'Security' },
  { key: 'diagnostics', label: 'Diagnostics' },
  { key: 'events', label: 'Event logs' },
  { key: 'printers', label: 'Printers' },
  { key: 'reporting', label: 'Report export' },
];

function isSectionKey(value: string | null): value is SectionKey {
  return SECTIONS.some((section) => section.key === value);
}

/** Read-only reminder of which identity remote sections scan as (the global admin sign-in). */
function ClientScanIdentity({ credentials }: { credentials: CredentialValues | undefined }) {
  const displayUser = credentials?.userName
    ? `${credentials.domain ? `${credentials.domain}\\` : ''}${credentials.userName}`
    : null;
  return (
    <div className="flex flex-wrap items-center gap-3 rounded-lg border border-slate-800 bg-slate-900/40 px-3 py-2 text-sm">
      {displayUser ? (
        <Badge tone="accent">Scanning as {displayUser}</Badge>
      ) : (
        <>
          <Badge tone="neutral">Scanning as current user</Badge>
          <span className="text-xs text-slate-400">Sign in as admin (top right) to scan with the admin account.</span>
        </>
      )}
    </div>
  );
}

export function ClientDetailPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { host: rawHost } = useParams<{ host: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const host = decodeURIComponent(rawHost ?? '');

  const { credentialsFor, savedTargets, saveTarget, deleteTarget } = useTargets();
  // undefined = getAppInfo not resolved yet; string|null once known. Sections
  // must wait for this so the local machine is never scanned as a remote target.
  const [machineName, setMachineName] = useState<string | null | undefined>(undefined);
  const requestedSection = searchParams.get('section');
  const section: SectionKey = isSectionKey(requestedSection) ? requestedSection : 'overview';
  const [powerShellError, setPowerShellError] = useState<ErrorPresentation | null>(null);
  const [reportRevision, setReportRevision] = useState(0);

  useEffect(() => {
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then((info) => setMachineName(info.machineName))
      .catch(() => setMachineName(null));
  }, []);

  const appInfoResolved = machineName !== undefined;
  const resolvedMachineName = machineName ?? null;
  const local = isLocalClient(host, resolvedMachineName);
  const credentials = credentialsFor(host);
  const target = useMemo(
    () => toClientTarget(host, resolvedMachineName, credentials),
    [host, resolvedMachineName, credentials],
  );
  const refreshReport = useCallback(() => setReportRevision((revision) => revision + 1), []);

  const savedEntry = useMemo(
    // Match by the same short-name key the Clients list merges on, so a client
    // saved under its short name is recognized when opened by FQDN.
    () => savedTargets.find((t) => t.role === 'Client' && clientKey(t.host) === clientKey(host)),
    [savedTargets, host],
  );

  const selectSection = useCallback((nextSection: SectionKey) => {
    const nextParams = new URLSearchParams(searchParams);
    if (nextSection === 'overview') {
      nextParams.delete('section');
    } else {
      nextParams.set('section', nextSection);
    }
    setSearchParams(nextParams);
  }, [searchParams, setSearchParams]);

  const onTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
    event.preventDefault();
    const delta = event.key === 'ArrowRight' ? 1 : -1;
    const next = SECTIONS[(index + delta + SECTIONS.length) % SECTIONS.length];
    selectSection(next.key);
    document.getElementById(`clienttab-${next.key}`)?.focus();
  };

  if (host === '') {
    return <EmptyState title="No client selected" message="Pick a client from the Clients list." />;
  }

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title={host} subtitle={local ? 'This machine · scanned as the current user' : 'Remote client'}>
        <div className="flex items-center gap-2">
          <Button variant="ghost" onClick={() => navigate('/clients', { state: location.state })}>
            ← All clients
          </Button>
          {!local && (
            <Button
              variant="secondary"
              onClick={() => {
                setPowerShellError(null);
                void openPsSession(host, credentials ?? null).catch((caught: unknown) =>
                  setPowerShellError(presentError(caught, {
                    message: 'The PowerShell session could not be opened.',
                  })),
                );
              }}
              title={`Open a PowerShell session to ${host}`}
            >
              PowerShell
            </Button>
          )}
          {savedEntry ? (
            <Button variant="secondary" onClick={() => void deleteTarget(savedEntry.id)}>
              Unsave client
            </Button>
          ) : (
            <Button
              variant="secondary"
              onClick={() =>
                void saveTarget({ label: host, host, role: 'Client', userName: credentials?.userName ?? null })
              }
            >
              Save client
            </Button>
          )}
        </div>
      </PageHeader>
      {powerShellError && (
        <ErrorState title="PowerShell session unavailable" {...powerShellError} />
      )}

      {!local && <ClientScanIdentity credentials={credentials} />}

      <div role="tablist" aria-label="Client sections" className="flex flex-wrap gap-1 border-b border-slate-800">
        {SECTIONS.map((entry, index) => (
          <button
            key={entry.key}
            id={`clienttab-${entry.key}`}
            role="tab"
            aria-selected={section === entry.key}
            aria-controls={`clientpanel-${entry.key}`}
            tabIndex={section === entry.key ? 0 : -1}
            type="button"
            onClick={() => selectSection(entry.key)}
            onKeyDown={(event) => onTabKeyDown(event, index)}
            className={`-mb-px border-b-2 px-3 py-2 text-sm transition-colors ${
              section === entry.key
                ? 'border-accent-400 font-medium text-white'
                : 'border-transparent text-slate-400 hover:text-slate-200'
            }`}
          >
            {entry.label}
          </button>
        ))}
      </div>

      {!appInfoResolved ? (
        <Spinner label="Preparing client …" />
      ) : (
        // All sections stay mounted; switching tabs only hides them, so an
        // in-progress scan keeps running and its result is never discarded.
        <>
          <div role="tabpanel" id="clientpanel-overview" aria-labelledby="clienttab-overview" hidden={section !== 'overview'}>
            <OverviewSection host={host} />
          </div>
          <div role="tabpanel" id="clientpanel-inventory" aria-labelledby="clienttab-inventory" hidden={section !== 'inventory'}>
            <InventorySection key={host} target={target} onDataChanged={refreshReport} />
          </div>
          <div role="tabpanel" id="clientpanel-security" aria-labelledby="clienttab-security" hidden={section !== 'security'}>
            <SecuritySection key={host} target={target} onDataChanged={refreshReport} />
          </div>
          <div role="tabpanel" id="clientpanel-diagnostics" aria-labelledby="clienttab-diagnostics" hidden={section !== 'diagnostics'}>
            <DiagnosticsSection key={host} target={target} />
          </div>
          <div role="tabpanel" id="clientpanel-events" aria-labelledby="clienttab-events" hidden={section !== 'events'}>
            <EventLogSection key={host} target={target} />
          </div>
          <div role="tabpanel" id="clientpanel-printers" aria-labelledby="clienttab-printers" hidden={section !== 'printers'}>
            <PrintersSection key={host} target={target} />
          </div>
          <div role="tabpanel" id="clientpanel-reporting" aria-labelledby="clienttab-reporting" hidden={section !== 'reporting'}>
            <ReportingSection
              host={local ? null : host}
              refreshKey={reportRevision}
            />
          </div>
        </>
      )}
    </div>
  );
}
