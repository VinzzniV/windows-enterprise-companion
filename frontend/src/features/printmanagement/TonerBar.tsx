import type { TonerSupply } from '../../shared/api-types';
import { hasLowToner, lowestTonerPercent } from './printers';

// Literal colours here encode the physical toner (CMYK + waste), not status —
// the status semantics (low) ride on the fail-toned label next to the bar.
function tonerColor(description: string): string {
  const d = description.toLowerCase();
  if (d.includes('cyan')) return 'bg-cyan-400';
  if (d.includes('magenta')) return 'bg-fuchsia-400';
  if (d.includes('yellow')) return 'bg-yellow-400';
  if (d.includes('black')) return 'bg-slate-300';
  if (d.includes('waste')) return 'bg-slate-600';
  return 'bg-slate-400';
}

/** Compact multi-segment toner gauge: one bar per supply + the lowest %/low flag. */
export function TonerBar({ supplies }: { supplies: TonerSupply[] }) {
  if (supplies.length === 0) {
    return <span className="text-xs text-muted">—</span>;
  }
  const min = lowestTonerPercent(supplies);
  const low = hasLowToner(supplies);
  const summary = supplies
    .map((s) => `${s.description} ${s.percent != null ? `${s.percent}%` : 'unknown'}${s.isLow ? ' (low)' : ''}`)
    .join(', ');

  return (
    <div className="flex items-center gap-2" title={summary} aria-label={`Toner: ${summary}`}>
      <div className="flex h-4 w-16 items-end gap-px">
        {supplies.map((supply, index) => (
          <div key={index} className="relative flex-1 overflow-hidden rounded-sm bg-slate-800">
            <div
              className={`absolute inset-x-0 bottom-0 ${tonerColor(supply.description)}`}
              style={{ height: `${supply.percent != null ? Math.max(6, supply.percent) : 0}%` }}
            />
          </div>
        ))}
      </div>
      {low ? (
        <span className="whitespace-nowrap text-xs font-medium text-fail-300">{min ?? 0}% low</span>
      ) : min != null ? (
        <span className="text-xs text-slate-400">{min}%</span>
      ) : (
        <span className="text-xs text-muted">n/a</span>
      )}
    </div>
  );
}
