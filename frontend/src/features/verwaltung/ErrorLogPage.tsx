import { useCallback, useEffect, useState } from 'react';
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
import { DetailDialog } from '../../shared/ui/DetailDialog';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';

type LevelFilter = 'all' | 'errors';

function formatBytes(value: number): string {
  return value >= 1024 * 1024
    ? `${(value / (1024 * 1024)).toFixed(1)} MB`
    : `${Math.ceil(value / 1024)} KB`;
}

const levelTone: Record<string, BadgeTone> = {
  WRN: 'warn',
  ERR: 'fail',
  FTL: 'fail',
};

const columns: DataColumn<LogEntry>[] = [
  { header: 'Time', cell: (entry) => <span className="whitespace-nowrap font-mono text-xs text-slate-400">{entry.timestamp}</span> },
  { header: 'Level', cell: (entry) => <Badge tone={levelTone[entry.level] ?? 'neutral'}>{entry.level}</Badge> },
  {
    header: 'Source',
    cell: (entry) => (
      <span className="block max-w-56 truncate font-mono text-xs text-slate-400" title={entry.source}>
        {entry.source}
      </span>
    ),
  },
  {
    header: 'Summary',
    cell: (entry) => (
      <span className="block max-w-xl truncate text-sm text-slate-200" title={entry.summary}>
        {entry.summary}
      </span>
    ),
  },
];

/** Recent warning/error/fatal log entries — the "what failed during scans and queries" feed. */
export function ErrorLogPage() {
  const [result, setResult] = useState<RecentLogEntriesResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<{ kind: 'load' | 'hide'; presentation: ErrorPresentation } | null>(null);
  const [filter, setFilter] = useState<LevelFilter>('all');
  const [selectedEntry, setSelectedEntry] = useState<LogEntry | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    setSelectedEntry(null);
    invoke<RecentLogEntriesResult>('logs', 'recent', {
      limit: 500,
      levelFilter: filter === 'errors' ? 'ERRORS' : 'ALL',
    })
      .then(setResult)
      .catch((caught: unknown) => setError({
        kind: 'load',
        presentation: presentError(caught, { message: 'The error log could not be loaded.' }),
      }))
      .finally(() => setLoading(false));
  }, [filter]);

  useEffect(load, [load]);

  // The marker starts a fresh view; existing log files stay intact on disk.
  const hidePreviousEntries = useCallback(() => {
    setError(null);
    invoke('logs', 'clearRecent', {})
      .then(load)
      .catch((caught: unknown) => setError({
        kind: 'hide',
        presentation: presentError(caught, {
          message: 'The previous error-log entries could not be hidden.',
          action: 'Retry hiding the previous entries. Existing entries remain visible and the log files are unchanged.',
        }),
      }));
  }, [load]);

  const entries = result?.entries ?? [];
  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Error log"
        subtitle="Warnings, errors and fatals from scans and queries — newest first, across the recent log files"
      >
        <Button variant="secondary" onClick={load} disabled={loading}>
          {loading ? 'Loading…' : 'Refresh'}
        </Button>
      </PageHeader>

      <Toolbar actions={(
        <>
          <span className="max-w-sm text-xs text-muted">
            Sets a local visibility marker at the current time. All earlier entries are hidden from this view; log files stay on disk.
          </span>
          <Button
            variant="ghost"
            onClick={hidePreviousEntries}
            disabled={loading || result === null || entries.length === 0}
          >
            Hide previous entries
          </Button>
        </>
      )}>
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
        {result?.source && (
          <span className="text-xs text-muted">Source: {result.source}</span>
        )}
        {result?.clearedAtUtc && (
          <span className="text-xs text-muted">
            Previous entries hidden since{' '}
            <time dateTime={result.clearedAtUtc}>{new Date(result.clearedAtUtc).toLocaleString()}</time>
            {' '}— log files unchanged
          </span>
        )}
      </Toolbar>

      {result && <div className="rounded border border-slate-800 bg-slate-900/40 px-3 py-2 text-xs text-slate-400" role="status">
        Evaluated {result.coverage.evaluatedFileCount} of {result.coverage.availableFileCount} retained log files
        {' '}({formatBytes(result.coverage.evaluatedBytes)} read), with {result.coverage.totalMatched} matching entries before the {result.coverage.resultLimit}-entry result limit.
        {(result.coverage.fileSelectionTruncated || result.coverage.byteWindowTruncated) && <span className="mt-1 block text-warn-300">
          Coverage is limited: older files or earlier content in a large file were not evaluated.
        </span>}
        {result.coverage.resultTruncated && <span className="mt-1 block text-warn-300">
          Showing the newest {result.entries.length} matching entries; older matches exist in the evaluated window.
        </span>}
        {result.coverage.truncatedDetailCount > 0 && <span className="mt-1 block text-warn-300">
          {result.coverage.truncatedDetailCount} shown {result.coverage.truncatedDetailCount === 1 ? 'entry has' : 'entries have'} truncated continuation details.
        </span>}
      </div>}

      {loading && result === null && <Spinner label="Reading the log …" />}
      {error && (
        <ErrorState
          title={error.kind === 'load' ? 'Error log unavailable' : 'Previous entries could not be hidden'}
          {...error.presentation}
          controls={error.kind === 'load'
            ? <Button variant="secondary" onClick={load}>Retry loading</Button>
            : <Button variant="secondary" onClick={hidePreviousEntries}>Retry hiding entries</Button>}
        />
      )}
      {result &&
        (entries.length === 0 ? (
          result.clearedAtUtc
            ? <EmptyState title="No new entries" message="No warnings or errors have been logged since previous entries were hidden." />
            : <EmptyState title="Nothing matched" message="No matching entries were found in the evaluated log window. Coverage limits above still apply." />
        ) : (
          <DataTable
            columns={columns}
            rows={entries}
            emptyMessage="No entries."
            getRowKey={(_entry, index) => index}
            onRowClick={setSelectedEntry}
            isRowActive={(entry) => entry === selectedEntry}
            stickyHeader
          />
        ))}

      {selectedEntry && (
        <DetailDialog
          title="Log entry details"
          description={selectedEntry.summary}
          closeLabel="Close log entry details"
          onClose={() => setSelectedEntry(null)}
        >
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <Badge tone={levelTone[selectedEntry.level] ?? 'neutral'}>{selectedEntry.level}</Badge>
            <span className="font-mono text-xs text-slate-300">Source: {selectedEntry.source}</span>
            <span className="font-mono text-xs text-muted">Time: {selectedEntry.timestamp}</span>
          </div>
          <h3 className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">
            Technical details
          </h3>
          {selectedEntry.technicalDetailsTruncated && <p className="mb-2 rounded border border-warn-800 bg-warn-950/20 px-3 py-2 text-sm text-warn-200" role="status">
            Continuation details were truncated by the configured per-entry line limit.
          </p>}
          <pre className="max-h-[60vh] overflow-auto whitespace-pre-wrap break-words rounded border border-slate-800 bg-slate-950 p-3 font-mono text-xs text-slate-300">
            {selectedEntry.technicalDetails}
          </pre>
        </DetailDialog>
      )}
    </div>
  );
}
