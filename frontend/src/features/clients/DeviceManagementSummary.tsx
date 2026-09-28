import type { DeviceProfileResult } from '../../shared/api-types.generated';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';

const sources = [
  { name: 'Active Directory', codes: ['StaleAd'], missing: ['MissingAd', 'OrphanKaspersky', 'OrphanOpsi'] },
  { name: 'Kaspersky', codes: ['StaleKaspersky'], missing: ['MissingKaspersky'] },
  { name: 'opsi', codes: ['StaleOpsi'], missing: ['MissingOpsi'] },
  { name: 'Nessus', codes: ['StaleNessus'], missing: ['MissingNessus'] },
] as const;

function recordState(profile: DeviceProfileResult, source: string): { count: number; observedAt: string | null } {
  const records = profile.managementCandidates;
  if (!records) return { count: 0, observedAt: null };
  const observations = source === 'Active Directory' ? records.activeDirectory.map(record => record.lastLogonDate)
    : source === 'Kaspersky' ? records.kaspersky.map(record => record.lastSeen)
      : source === 'opsi' ? records.opsi.map(record => record.lastSeen)
        : records.nessus.map(record => record.lastCompletedScanUtc);
  const times = observations.filter((value): value is string => value !== null).sort();
  return { count: observations.length, observedAt: times.at(-1) ?? null };
}

export function DeviceManagementSummary({ profile, loading, error, refresh }: {
  profile: DeviceProfileResult;
  loading: boolean;
  error: string | null;
  refresh(): void;
}) {
  const loaded = profile.managementCandidates !== null || profile.managementSources.length > 0;
  const ambiguous = profile.identity === 'AMBIGUOUS' || profile.identity === 'CONFLICT';
  return <Card title="Management systems">
    <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
      <p className="text-xs text-muted">{loaded
        ? `Cached source evidence · assessed ${profile.managementAssessedAtUtc ? new Date(profile.managementAssessedAtUtc).toLocaleString() : 'time unknown'}`
        : 'Not loaded. Read the configured sources to see their current observations.'}</p>
      <Button disabled={loading} onClick={refresh}>{loading ? 'Loading…' : 'Refresh management sources'}</Button>
    </div>
    {error && <p role="alert" className="mb-3 text-sm text-fail-300">{error}</p>}
    {ambiguous && <p className="mb-3 text-sm text-warn-300">Identity is ambiguous. Source records below are candidates, not confirmed facts about one device.</p>}
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{sources.map(source => {
      const state = profile.managementSources.find(entry => entry.source === source.name);
      const record = recordState(profile, source.name);
      const findings = profile.managementFindings.filter(finding => finding.source === source.name);
      const stale = findings.some(finding => source.codes.some(code => finding.findingCode === code));
      const missing = findings.some(finding => source.missing.some(code => finding.findingCode === code));
      const incomplete = state?.availability === 'PARTIAL' || state?.availability === 'TRUNCATED';
      const unavailable = state && state.availability !== 'AVAILABLE' && !incomplete;
      const baseStatus = !loaded || !state ? 'Not loaded' : unavailable ? state.availability.replaceAll('_', ' ').toLowerCase()
        : ambiguous || record.count > 1 ? 'Needs review' : stale ? 'Stale' : missing ? 'Missing'
          : record.count === 1 ? 'Available candidate' : 'No matching candidate';
      const status = incomplete ? `${baseStatus} · partial coverage` : baseStatus;
      return <div key={source.name} className="rounded border border-slate-800 p-3 text-sm">
        <div className="font-medium text-slate-100">{source.name}</div>
        <div className={stale || missing || ambiguous || record.count > 1 ? 'mt-1 text-warn-300' : 'mt-1 text-slate-300'}>{status}</div>
        <div className="mt-1 text-xs text-muted">{record.count} matching source record{record.count === 1 ? '' : 's'}</div>
        <div className="text-xs text-muted">Last observation: {record.observedAt ? new Date(record.observedAt).toLocaleString() : 'Unknown'}</div>
        {state && <div className="mt-1 text-xs text-muted">Coverage: {state.availability.toLowerCase().replaceAll('_', ' ')}{state.explanation ? ` · ${state.explanation}` : ''}</div>}
      </div>;
    })}</div>
  </Card>;
}
