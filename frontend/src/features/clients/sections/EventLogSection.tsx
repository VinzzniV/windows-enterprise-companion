import { useCallback, useMemo, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../../shared/bridge/errorPresentation';
import type { EventLogQueryResult, RemoteEventLogEntry, TargetRequest } from '../../../shared/api-types';
import { Button } from '../../../shared/ui/Button';
import { Badge, type BadgeTone } from '../../../shared/ui/Badge';
import { Select } from '../../../shared/ui/Select';
import { DataTable, type DataColumn } from '../../../shared/ui/DataTable';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';
import { DetailDialog } from '../../../shared/ui/DetailDialog';

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
export function EventLogSection({ host, target }: { host: string; target: TargetRequest | null }) {
  const [preset, setPreset] = useState(PRESETS[0].key);
  const [state, setState] = useState<State>({ kind: 'idle' });
  const [selectedEntry, setSelectedEntry] = useState<RemoteEventLogEntry | null>(null);
  const [copyStatus, setCopyStatus] = useState<'copied' | 'failed' | null>(null);

  const columns = useMemo<DataColumn<RemoteEventLogEntry>[]>(() => [
    {
      header: 'Time',
      cell: (entry) => entry.timeGenerated ? new Date(entry.timeGenerated).toLocaleString() : '—',
    },
    { header: 'Level', cell: (entry) => <Badge tone={levelTone(entry.level)}>{entry.level}</Badge> },
    { header: 'Source', cell: (entry) => entry.source },
    { header: 'Event', mono: true, align: 'right', cell: (entry) => entry.eventCode },
    {
      header: 'Message',
      cell: (entry) => (
        <span className="block max-w-xl truncate text-slate-300" title={entry.message}>
          {entry.message || '—'}
        </span>
      ),
    },
    {
      header: 'Details',
      cell: (entry) => <Button
        variant="secondary"
        onClick={() => { setSelectedEntry(entry); setCopyStatus(null); }}
        aria-label={`View full message from ${entry.source}, event ${entry.eventCode}`}
      >
        View full message
      </Button>,
    },
  ], []);

  const run = useCallback(() => {
    setSelectedEntry(null);
    setCopyStatus(null);
    setState({ kind: 'running' });
    invoke<EventLogQueryResult>('diagnostics', 'queryEventLog', { preset, target })
      .then((result) => setState({ kind: 'done', result }))
      .catch((error: unknown) => setState({
        kind: 'error',
        error: presentError(error, { message: 'The event log query could not be completed.' }),
      }));
  }, [preset, target]);

  const copyMessage = async () => {
    if (!selectedEntry) return;
    try {
      if (!navigator.clipboard?.writeText) throw new Error('Clipboard API unavailable');
      await navigator.clipboard.writeText(selectedEntry.message);
      setCopyStatus('copied');
    } catch {
      setCopyStatus('failed');
    }
  };

  const messageWasTruncated = selectedEntry?.messageTruncated ?? false;

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
        <span className="text-xs text-muted">Reads {host} live using the account context shown above. Results are not saved and the target is not changed.</span>
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
            Queried {new Date(state.result.windowStartUtc).toLocaleString()} to {new Date(state.result.windowEndUtc).toLocaleString()}.
            {' '}{state.result.totalMatched} matching event{state.result.totalMatched === 1 ? '' : 's'}.
            {state.result.truncated && ` Showing the newest ${state.result.entries.length} within the ${state.result.resultLimit}-entry result limit.`}
          </div>
          <DataTable
            columns={columns}
            rows={state.result.entries}
            emptyMessage="No matching events in the queried window."
            getRowKey={(entry, index) => `${entry.timeGenerated ?? 'na'}-${entry.eventCode}-${index}`}
          />
        </>
      )}
      {selectedEntry && <DetailDialog
        title={`Event ${selectedEntry.eventCode} on ${host}`}
        description={`${selectedEntry.level} · ${selectedEntry.source} · ${selectedEntry.timeGenerated ? new Date(selectedEntry.timeGenerated).toLocaleString() : 'time unavailable'}`}
        closeLabel="Close event message"
        onClose={() => setSelectedEntry(null)}
      >
        <dl className="mb-3 grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted">Host</dt><dd className="break-all font-mono text-slate-200">{host}</dd>
          <dt className="text-muted">Time</dt><dd>{selectedEntry.timeGenerated ? new Date(selectedEntry.timeGenerated).toLocaleString() : 'Unavailable'}</dd>
          <dt className="text-muted">Source</dt><dd className="break-words">{selectedEntry.source}</dd>
          <dt className="text-muted">Level</dt><dd><Badge tone={levelTone(selectedEntry.level)}>{selectedEntry.level}</Badge></dd>
          <dt className="text-muted">Event</dt><dd className="font-mono">{selectedEntry.eventCode}</dd>
        </dl>
        {messageWasTruncated && <p className="mb-3 rounded border border-warn-800 bg-warn-950/20 px-3 py-2 text-sm text-warn-200" role="status">
          The provider message exceeded the 500-character detail limit and was truncated.
        </p>}
        <pre className="max-h-[55vh] overflow-auto whitespace-pre-wrap break-words rounded border border-slate-800 bg-slate-950 p-3 font-mono text-xs text-slate-200">
          {selectedEntry.message || 'No message was returned.'}
        </pre>
        <div className="mt-3 flex flex-wrap items-center gap-3">
          <Button variant="secondary" onClick={() => void copyMessage()}>Copy message</Button>
          {copyStatus && <span className={`text-sm ${copyStatus === 'copied' ? 'text-ok-300' : 'text-fail-300'}`} role="status">
            {copyStatus === 'copied' ? 'Message copied.' : 'Copy failed.'}
          </span>}
        </div>
      </DetailDialog>}
    </div>
  );
}
