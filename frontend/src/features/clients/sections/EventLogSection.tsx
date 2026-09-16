import { useCallback, useRef, useState, type KeyboardEvent } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../../shared/bridge/errorPresentation';
import type { EventLogQueryResult, RemoteEventLogEntry, TargetRequest } from '../../../shared/api-types';
import { Button } from '../../../shared/ui/Button';
import { Badge, type BadgeTone } from '../../../shared/ui/Badge';
import { Select } from '../../../shared/ui/Select';
import { DataTable, type DataColumn } from '../../../shared/ui/DataTable';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

// Keys must match EventLogQueryService.Presets in the backend.
const PRESETS: { key: string; label: string }[] = [
  { key: 'system-errors', label: 'System errors (last 24h)' },
  { key: 'system-warnings', label: 'System warnings + errors (last 24h)' },
  { key: 'application-errors', label: 'Application errors (last 24h)' },
  { key: 'app-crashes', label: 'Application crashes (last 7 days)' },
  { key: 'unexpected-shutdowns', label: 'Unexpected shutdowns (last 30 days)' },
  { key: 'service-failures', label: 'Service failures (last 7 days)' },
  { key: 'disk-events', label: 'Disk & filesystem events (last 7 days)' },
];

type State =
  | { kind: 'idle' }
  | { kind: 'running' }
  | { kind: 'done'; result: EventLogQueryResult }
  | { kind: 'error'; error: ErrorPresentation };

function levelTone(level: string): BadgeTone {
  if (level === 'Error' || level === 'Audit Failure') return 'fail';
  if (level === 'Warning') return 'warn';
  return 'neutral';
}

/** Live event-log queries with canned presets — never persisted, always fresh. */
export function EventLogSection({ target }: { target: TargetRequest | null }) {
  const [preset, setPreset] = useState(PRESETS[0].key);
  const [state, setState] = useState<State>({ kind: 'idle' });
  const [detail, setDetail] = useState<RemoteEventLogEntry | null>(null);
  const detailTrigger = useRef<HTMLButtonElement | null>(null);

  const closeDetail = () => {
    setDetail(null);
    requestAnimationFrame(() => detailTrigger.current?.focus());
  };
  const onDetailKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') { event.preventDefault(); closeDetail(); }
  };
  const columns: DataColumn<RemoteEventLogEntry>[] = [
    { header: 'Time', cell: entry => entry.timeGenerated ? new Date(entry.timeGenerated).toLocaleString() : '—' },
    { header: 'Level', cell: entry => <Badge tone={levelTone(entry.level)}>{entry.level}</Badge> },
    { header: 'Source', cell: entry => entry.source },
    { header: 'Event', mono: true, align: 'right', cell: entry => entry.eventCode },
    { header: 'Message', cell: entry => entry.message ? <div className="max-w-xl">
      <span className="block truncate text-slate-300">{entry.message}</span>
      <button type="button" className="mt-1 text-xs text-accent-400 underline" onClick={event => { detailTrigger.current = event.currentTarget; setDetail(entry); }}>View full message</button>
    </div> : '—' },
  ];

  const run = useCallback(() => {
    setState({ kind: 'running' });
    invoke<EventLogQueryResult>('diagnostics', 'queryEventLog', { preset, target })
      .then((result) => setState({ kind: 'done', result }))
      .catch((error: unknown) => setState({
        kind: 'error',
        error: presentError(error, { message: 'The event log query could not be completed.' }),
      }));
  }, [preset, target]);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-end gap-3 rounded border border-slate-800 bg-slate-900/50 px-3 py-2">
        <label className="flex flex-col gap-1 text-sm text-slate-300">
          Query
          <Select value={preset} onChange={(event) => setPreset(event.target.value)} aria-label="Event log query preset">
            {PRESETS.map((candidate) => (
              <option key={candidate.key} value={candidate.key}>
                {candidate.label}
              </option>
            ))}
          </Select>
        </label>
        <Button variant="primary" onClick={run} disabled={state.kind === 'running'}>
          Run query
        </Button>
        <span className="text-xs text-muted">Live view — results are not saved.</span>
      </div>

      {state.kind === 'running' && <Spinner label="Querying event log …" />}
      {state.kind === 'error' && (
        <ErrorState
          {...state.error}
          controls={<Button onClick={run}>Retry query</Button>}
        />
      )}
      {state.kind === 'idle' && (
        <EmptyState
          title="Event logs"
          message="Pick a canned query and run it against this machine. Reads the classic System/Application logs live over WMI."
        />
      )}
      {state.kind === 'done' && (
        <>
          <div className="text-sm text-slate-400">
            {state.result.totalMatched} matching event{state.result.totalMatched === 1 ? '' : 's'}
            {state.result.truncated && ` — showing the newest ${state.result.entries.length}`}
          </div>
          <DataTable
            columns={columns}
            rows={state.result.entries}
            emptyMessage="No matching events in the queried window."
            getRowKey={(entry, index) => `${entry.timeGenerated ?? 'na'}-${entry.eventCode}-${index}`}
          />
        </>
      )}
      {detail && <div className="fixed inset-0 z-[80] flex items-center justify-center bg-slate-950/80 p-4" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) closeDetail(); }}>
        <div role="dialog" aria-modal="true" aria-labelledby="event-message-title" onKeyDown={onDetailKeyDown}
          className="max-h-[80vh] w-full max-w-3xl overflow-auto rounded-lg border border-slate-700 bg-slate-900 p-4 shadow-2xl">
          <div className="mb-3 flex items-start justify-between gap-3"><div><h2 id="event-message-title" className="font-semibold">Event {detail.eventCode} message</h2>
            <p className="text-xs text-muted">{detail.source} · {detail.level}{detail.timeGenerated ? ` · ${new Date(detail.timeGenerated).toLocaleString()}` : ''}</p></div>
            <Button autoFocus onClick={closeDetail}>Close message</Button></div>
          <pre className="select-text whitespace-pre-wrap break-words rounded bg-slate-950/60 p-3 font-sans text-sm text-slate-200">{detail.message}</pre>
        </div>
      </div>}
    </div>
  );
}
