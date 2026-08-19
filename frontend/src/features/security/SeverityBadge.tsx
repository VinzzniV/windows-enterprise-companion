import type { FindingSeverity } from '../../shared/api-types';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';

const severityTone: Record<FindingSeverity, BadgeTone> = {
  UNKNOWN: 'warn',
  CRITICAL: 'fail',
  HIGH: 'fail',
  MEDIUM: 'warn',
  LOW: 'info',
  INFO: 'neutral',
};

export function SeverityBadge({ severity, count }: { severity: FindingSeverity; count?: number }) {
  return (
    <Badge tone={severityTone[severity]}>
      {severity}
      {count !== undefined && <span className="font-normal opacity-80">{count}</span>}
    </Badge>
  );
}
