import type { HygieneLoadProgress } from '../api-types';
import { Button } from '../ui/Button';
import { SemanticStatusBadge, semanticStatusPresentation } from '../ui/SemanticStatusBadge';
import { hygieneSourceProgressStatus } from './inventorySourceStatus';

const sourceLabels: Record<string, string> = {
  ACTIVE_DIRECTORY: 'Active Directory',
  KASPERSKY: 'Kaspersky',
  OPSI: 'opsi',
  NESSUS: 'Nessus',
};

export function HygieneLoadStatus({
  progress,
  elapsedSeconds,
  onCancel,
}: {
  progress: HygieneLoadProgress | null;
  elapsedSeconds: number;
  onCancel(): void;
}) {
  const sources = progress?.sources ?? ['ACTIVE_DIRECTORY', 'KASPERSKY', 'OPSI', 'NESSUS'].map((source) => ({
    source,
    status: 'RUNNING' as const,
    itemCount: null,
    message: null,
  }));
  const phase = progress?.phase === 'CORRELATING' ? 'Correlating devices'
    : progress?.phase === 'COMPLETED' ? 'Finishing environment inventory'
      : 'Loading environment sources';

  return <section className="rounded-lg border border-accent-500/30 bg-accent-500/5 p-4" aria-live="polite">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h3 className="font-semibold text-slate-100">{phase}</h3>
        <p className="mt-1 text-sm text-slate-400">
          {progress?.completedSources ?? 0} of {progress?.totalSources ?? 4} sources complete
          {progress && ` · ${progress.partialDeviceCount} devices available so far`} · {elapsedSeconds}s elapsed
        </p>
      </div>
      <Button variant="secondary" onClick={onCancel}>Cancel load</Button>
    </div>
    <div className="mt-3 flex flex-wrap gap-2">
      {sources.map((source) => {
        const presentation = hygieneSourceProgressStatus(source.status);
        const statusLabel = semanticStatusPresentation(presentation.status).label;
        const sourceLabel = sourceLabels[source.source] ?? source.source;
        const context = presentation.context ? ` · ${presentation.context}` : '';
        const count = source.itemCount != null ? ` · ${source.itemCount} items` : '';
        return <span
          key={source.source}
          role="group"
          aria-label={`${sourceLabel}: ${statusLabel}${context}${count}`}
          title={source.message ?? undefined}
          className="inline-flex items-center gap-1.5 rounded border border-slate-800 bg-slate-900/60 px-2 py-1"
        >
          <span className="text-xs font-medium text-slate-300">{sourceLabel}</span>
          <SemanticStatusBadge status={presentation.status} />
          {presentation.context && <span className="text-xs text-slate-400">{presentation.context}</span>}
          {source.itemCount != null && <span className="text-xs tabular-nums text-slate-400">{source.itemCount} items</span>}
        </span>;
      })}
    </div>
    {elapsedSeconds >= 10 && <p className="mt-3 text-sm text-slate-300">Large environments can take up to three minutes. Completed sources remain visible while the others continue.</p>}
  </section>;
}
