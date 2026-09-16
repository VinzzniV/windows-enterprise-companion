import { type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { OpsiConnectionStatusResult } from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import { Spinner } from '../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../shared/ui/States';
import { PatchAuditHistoryCard } from './PatchAuditHistoryCard';
import { PatchClientFleetCard } from './PatchClientFleetCard';
import { PatchProductOverviewWorkspace } from './PatchProductOverviewWorkspace';
import { WingetPackagesWorkspace } from './WingetPackagesWorkspace';
import { opsiConnectionStatus } from './patchStatus';
import { usePatchManagementWorkspace } from './usePatchManagementWorkspace';

type PatchSection = 'overview' | 'clients' | 'winget' | 'history';

function OpsiConnectionBadge({ status, loading, failed }: {
  status: OpsiConnectionStatusResult | null;
  loading: boolean;
  failed: boolean;
}) {
  const presentation = opsiConnectionStatus(
    loading ? { kind: 'loading' } : failed ? { kind: 'failed' } : status
      ? { kind: 'loaded', status } : { kind: 'unavailable' },
  );
  return (
    <span className="inline-flex flex-wrap items-center justify-end gap-1.5" title={presentation.technicalDetail ?? undefined}>
      <SemanticStatusBadge status={presentation.status} />
      {presentation.context && <span className="text-xs text-slate-400">{presentation.context}</span>}
      {status?.connected && <span className="text-xs text-slate-300">{status.serverUrl}{status.opsiVersion ? ` · opsi ${status.opsiVersion}` : ''}</span>}
    </span>
  );
}

function SectionTab({ active, children, onClick }: { active: boolean; children: ReactNode; onClick: () => void }) {
  return <button type="button" role="tab" aria-selected={active} onClick={onClick} className={`border-b-2 px-3 py-2 text-sm font-medium ${active ? 'border-accent-400 text-slate-100' : 'border-transparent text-slate-400 hover:text-slate-200'}`}>{children}</button>;
}

function formatTimestamp(value: string): string {
  return new Date(value).toLocaleString();
}

export function PatchManagementPage() {
  const workspace = usePatchManagementWorkspace();
  const [parameters, setParameters] = useSearchParams();
  const section = (['overview', 'clients', 'winget', 'history'] as const).find(value => value === parameters.get('section')) ?? 'overview';
  const setSection = (next: PatchSection) => setParameters(previous => { const value = new URLSearchParams(previous); value.set('section', next); return value; });

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Patch Management"
        subtitle="Build and update Winget-backed opsi packages. Client rollout remains entirely in opsi."
      >
        <OpsiConnectionBadge status={workspace.status} loading={workspace.statusLoading} failed={!!workspace.statusError} />
      </PageHeader>

      {workspace.statusError && <ErrorState title="opsi connection unavailable" {...workspace.statusError} controls={<Button onClick={workspace.loadConnectionStatus}>Check connection again</Button>} />}

      {!workspace.connected && !workspace.statusLoading && (
        <Card title="opsi connection">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-slate-400">The server and securely stored opsi account are managed in Settings.</p>
            <a href="#/settings" className="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800">Go to Settings</a>
          </div>
        </Card>
      )}

      {(workspace.connected || workspace.dashboard) && (
        <>
          {workspace.stale && workspace.dashboard && <p className="rounded border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-slate-300">Saved read-only view from {formatTimestamp(workspace.dashboard.generatedAtUtc)}. Reconnect for current data.</p>}
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-400">Location / depot</span>
              <Select aria-label="Depot filter" fullWidth={false} value={workspace.depotFilter} disabled={!workspace.connected} onChange={(event) => workspace.changeDepotFilter(event.target.value)}>
                <option value="">All depots</option>
                {(workspace.dashboard?.depots ?? []).map((depot) => <option key={depot.id} value={depot.id}>{depot.description ? `${depot.description} (${depot.id})` : depot.id}</option>)}
              </Select>
            </label>
            <Button onClick={workspace.refreshDashboard} disabled={workspace.dashboardLoading || !workspace.connected}>Refresh overview</Button>
            {workspace.dashboardLoading && <Spinner label="Loading patch overview" />}
            {workspace.dashboard && <span className="text-xs text-muted">Updated {formatTimestamp(workspace.dashboard.generatedAtUtc)}</span>}
          </div>

          {workspace.dashboardError && <ErrorState title="Overview unavailable" {...workspace.dashboardError} controls={<Button onClick={workspace.refreshDashboard} disabled={!workspace.connected}>Reload overview</Button>} />}

          {workspace.dashboard && (
            <>
              <div role="tablist" aria-label="Patch Management sections" className="flex gap-1 overflow-x-auto border-b border-slate-800">
                <SectionTab active={section === 'overview'} onClick={() => setSection('overview')}>Overview</SectionTab>
                <SectionTab active={section === 'clients'} onClick={() => setSection('clients')}>Clients <span className="text-muted">({workspace.dashboard.summary.clientCount.toLocaleString('de-DE')})</span></SectionTab>
                <SectionTab active={section === 'winget'} onClick={() => setSection('winget')}>Winget packages</SectionTab>
                <SectionTab active={section === 'history'} onClick={() => setSection('history')}>History</SectionTab>
              </div>
              {section === 'overview' && <PatchProductOverviewWorkspace dashboard={workspace.dashboard} />}
              {section === 'clients' && <PatchClientFleetCard connected={workspace.connected} dashboard={workspace.dashboard} />}
              {section === 'winget' && <WingetPackagesWorkspace connected={workspace.connected} depots={workspace.dashboard.depots} preferredDepot={workspace.depotFilter} onChanged={workspace.refreshDashboard} />}
              {section === 'history' && <PatchAuditHistoryCard />}
            </>
          )}
        </>
      )}

      {!workspace.connected && workspace.status !== null && workspace.dashboard === null && <EmptyState title="No opsi connection" message="Connect to an opsi server to load depot packages and client versions." />}
    </div>
  );
}
