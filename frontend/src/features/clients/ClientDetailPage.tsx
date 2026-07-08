import { useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../../shared/api-types';
import { useTargets } from '../../shared/targets/TargetContext';
import type { CredentialValues } from '../../shared/targets/TargetSelector';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Button } from '../../shared/ui/Button';
import { Badge } from '../../shared/ui/Badge';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState } from '../../shared/ui/States';
import { clientKey, isLocalClient, toClientTarget } from './clients';
import { InventorySection } from './sections/InventorySection';
import { SecuritySection } from './sections/SecuritySection';
import { DiagnosticsSection } from './sections/DiagnosticsSection';
import { PrintersSection } from './sections/PrintersSection';
import { ReportingSection } from '../reporting/ReportingSection';
import { openPsSession } from '../../shared/ps/openPsSession';

type SectionKey = 'inventory' | 'security' | 'diagnostics' | 'printers' | 'reporting';

const SECTIONS: { key: SectionKey; label: string }[] = [
  { key: 'inventory', label: 'Inventory' },
  { key: 'security', label: 'Security' },
  { key: 'diagnostics', label: 'Diagnostics' },
  { key: 'printers', label: 'Printers' },
  { key: 'reporting', label: 'Reporting' },
];

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
  const { host: rawHost } = useParams<{ host: string }>();
  const host = decodeURIComponent(rawHost ?? '');

  const { credentialsFor, savedTargets, saveTarget, deleteTarget } = useTargets();
  // undefined = getAppInfo not resolved yet; string|null once known. Sections
  // must wait for this so the local machine is never scanned as a remote target.
  const [machineName, setMachineName] = useState<string | null | undefined>(undefined);
  const [section, setSection] = useState<SectionKey>('inventory');

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

  const savedEntry = useMemo(
    // Match by the same short-name key the Clients list merges on, so a client
    // saved under its short name is recognized when opened by FQDN.
    () => savedTargets.find((t) => t.role === 'Client' && clientKey(t.host) === clientKey(host)),
    [savedTargets, host],
  );

  const onTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
    event.preventDefault();
    const delta = event.key === 'ArrowRight' ? 1 : -1;
    const next = SECTIONS[(index + delta + SECTIONS.length) % SECTIONS.length];
    setSection(next.key);
    document.getElementById(`clienttab-${next.key}`)?.focus();
  };

  if (host === '') {
    return <EmptyState title="No client selected" message="Pick a client from the Clients list." />;
  }

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title={host} subtitle={local ? 'This machine · scanned as the current user' : 'Remote client'}>
        <div className="flex items-center gap-2">
          <Button variant="ghost" onClick={() => navigate('/clients')}>
            ← All clients
          </Button>
          {!local && (
            <Button
              variant="secondary"
              onClick={() => void openPsSession(host, credentials ?? null).catch(() => {})}
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
            onClick={() => setSection(entry.key)}
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

      <div role="tabpanel" id={`clientpanel-${section}`} aria-labelledby={`clienttab-${section}`}>
        {!appInfoResolved ? (
          <Spinner label="Preparing client …" />
        ) : (
          <>
            {section === 'inventory' && <InventorySection key={host} target={target} />}
            {section === 'security' && <SecuritySection key={host} target={target} />}
            {section === 'diagnostics' && <DiagnosticsSection key={host} target={target} />}
            {section === 'printers' && <PrintersSection key={host} target={target} />}
            {section === 'reporting' && <ReportingSection host={local ? null : host} />}
          </>
        )}
      </div>
    </div>
  );
}
