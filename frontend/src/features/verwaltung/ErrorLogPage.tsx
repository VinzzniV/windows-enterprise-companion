import { useCallback, useEffect, useMemo, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { LogEntry, RecentLogEntriesResult } from '../../shared/api-types';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Toolbar } from '../../shared/ui/Toolbar';
import { Button } from '../../shared/ui/Button';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { EmptyState, ErrorState } from '../../shared/ui/States';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'loaded'; result: RecentLogEntriesResult }
  | { kind: 'error'; message: string };

type LevelFilter = 'all' | 'errors';

const levelTone: Record<string, BadgeTone> = {
  WRN: 'warn',
  ERR: 'fail',
  FTL: 'fail',
};

const columns: DataColumn<LogEntry>[] = [
  { header: 'Time', cell: (entry) => <span className="font-mono text-xs text-slate-400">{entry.timestamp}</span> },
  { header: 'Level', cell: (entry) => <Badge tone={levelTone[entry.level] ?? 'neutral'}>{entry.level}</Badge> },
  {
    header: 'Message',
    cell: (entry) => <span className="whitespace-pre-wrap break-words text-sm text-slate-200">{entry.message}</span>,
  },
];

/** Recent warning/error/fatal log entries — the "what failed during scans and queries" feed. */
export function ErrorLogPage() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [filter, setFilter] = useState<LevelFilter>('all');

  const load = useCallback(() => {
    setState({ kind: 'loading' });
    invoke<RecentLogEntriesResult>('logs', 'recent', { limit: 500 }, 30_000)
      .then((result) => setState({ kind: 'loaded', result }))
      .catch((error: unknown) =>
        setState({ kind: 'error', message: error instanceof Error ? error.message : String(error) }),
      );
  }, []);

  useEffect(load, [load]);

  const entries = state.kind === 'loaded' ? state.result.entries : [];
  const shown = useMemo(
    () => (filter === 'errors' ? entries.filter((entry) => entry.level !== 'WRN') : entries),
    [entries, filter],
  );

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Error log"
        subtitle="Warnings, errors and fatals from scans and queries — newest first, read from the current log file"
      >
        <Button variant="secondary" onClick={load} disabled={state.kind === 'loading'}>
          {state.kind === 'loading' ? 'Loading…' : 'Refresh'}
        </Button>
      </PageHeader>

      <Toolbar>
        <label className="flex items-center gap-2 text-sm text-slate-400">
          Show
          <Select
            fullWidth={false}
            value={filter}
            onChange={(event) => setFilter(event.target.value as LevelFilter)}
            aria-label="Log level filter"
          >
            <option value="all">Warnings and errors</option>
            <option value="errors">Errors and fatals only</option>
          </Select>
        </label>
        {state.kind === 'loaded' && state.result.source && (
          <span className="text-xs text-slate-500">Source: {state.result.source}</span>
        )}
      </Toolbar>

      {state.kind === 'loading' && <Spinner label="Reading the log …" />}
      {state.kind === 'error' && <ErrorState message={state.message} />}
      {state.kind === 'loaded' &&
        (shown.length === 0 ? (
          <EmptyState title="Nothing logged" message="No warnings or errors in the current log file." />
        ) : (
          <DataTable
            columns={columns}
            rows={shown}
            emptyMessage="No entries."
            getRowKey={(_entry, index) => index}
            stickyHeader
          />
        ))}
    </div>
  );
}
