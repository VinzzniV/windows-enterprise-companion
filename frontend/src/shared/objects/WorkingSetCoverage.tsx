import { useEffect, useState } from 'react';
import { useWorkingSet } from './WorkingSetContext';
import { Button } from '../ui/Button';

function time(value: string | null) { return value ? new Date(value).toLocaleString() : 'Unknown'; }

export function WorkingSetCoverage() {
  const workspace = useWorkingSet();
  const { displayed: snapshot } = workspace;
  const [now, setNow] = useState(Date.now);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  return <div className="space-y-2 rounded-lg border border-slate-800 p-3 text-sm">
    <div className="flex flex-wrap items-center gap-3">
      <strong>Loaded working set</strong>
      <Button disabled={workspace.busy} onClick={() => void workspace.refreshCached()}>Check cached sources</Button>
      {workspace.hasUpdates && <Button onClick={workspace.applyUpdates}>Apply available updates</Button>}
      {workspace.busy && <span role="status">Reading cached and stored data…</span>}
    </div>
    {workspace.error && <p role="alert" className="text-fail-400">{workspace.error}</p>}
    {snapshot ? <>
      <p>{snapshot.loadedSourceRecords} source records · {snapshot.objects.length} displayed objects/addresses · {snapshot.confirmedObjectCount} objects with scoped identity · {snapshot.unresolvedCandidateCount} unresolved or candidate entries</p>
      <p className="text-xs text-muted">Counts cover this working set only. Source pages are not added into a company total. Filters, sorting and search use the same displayed revision.</p>
      {snapshot.limited && <p className="text-warn-400">Working set limited to {workspace.policy?.maximumRecords} records and {workspace.policy?.maximumSourceReads} source reads. Some available records are omitted; incomplete evidence cannot confirm a cross-source identity.</p>}
      {snapshot.omittedSourceReads > 0 && <p className="text-warn-400">{snapshot.omittedSourceReads} source reads were omitted.</p>}
      <details><summary className="cursor-pointer text-accent-400">Source coverage, timestamps and errors ({snapshot.reads.length})</summary>
        <ul className="mt-3 grid gap-3 md:grid-cols-2">{snapshot.reads.map(read => <li key={read.key} className="rounded border border-slate-800 p-3">
          <strong>{read.title}</strong><p className="break-all text-xs text-muted">{read.scope ?? 'Scope unavailable'}</p>
          <p>{read.availability} · {read.coverage === 'complete' ? 'Returned query set covered' : read.coverage === 'partial' ? 'Partial query coverage' : 'Coverage unknown'}
            {read.freshUntilUtc && <span> · {Date.parse(read.freshUntilUtc) > now ? 'Fresh cache' : 'Stale cache'}</span>}</p>
          <p>{read.rows.length} indexed / {read.cachedRecordCount} cached records · source query count: {read.sourceTotal ?? 'Unknown'}</p>
          {read.limited && <p className="text-warn-400">This source is limited in the working set.</p>}
          <p className="text-xs text-muted">Last successful read: {time(read.retrievedAtUtc)}<br />Last attempt: {time(read.lastAttemptAtUtc)}<br />Cache retention: {time(read.retainedUntilUtc)}</p>
          {read.error && <p className="text-fail-400">Last attempt failed: {read.error}</p>}
        </li>)}</ul>
      </details>
    </> : <p>No working set loaded.</p>}
  </div>;
}
