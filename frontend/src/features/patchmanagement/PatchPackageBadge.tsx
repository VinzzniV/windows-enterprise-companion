import type { PatchPackageStatus } from '../../shared/api-types';
import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import { patchPackageStatus } from './patchStatus';

/** Shared package-health presentation for overview rows and product detail. */
export function PatchPackageBadge({ status }: { status: PatchPackageStatus }) {
  const presentation = patchPackageStatus(status);
  return (
    <span
      className="inline-flex flex-wrap items-center gap-1.5"
      title={presentation.technicalDetail ?? undefined}
    >
      <SemanticStatusBadge status={presentation.status} />
      {presentation.context && (
        <span className="text-xs text-slate-400">{presentation.context}</span>
      )}
    </span>
  );
}
