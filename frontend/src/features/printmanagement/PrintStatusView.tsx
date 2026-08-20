import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import type { PrintStatusPresentation } from './printStatus';

/** Shared Print Management status rendering: canonical badge plus concrete context. */
export function ContextualPrintStatus({ presentation, title }: {
  presentation: PrintStatusPresentation;
  title?: string;
}) {
  return (
    <span
      className="flex flex-col items-start gap-0.5"
      title={title ?? presentation.technicalDetail}
    >
      <SemanticStatusBadge status={presentation.status} />
      {presentation.context && <span className="text-xs text-slate-400">{presentation.context}</span>}
    </span>
  );
}
