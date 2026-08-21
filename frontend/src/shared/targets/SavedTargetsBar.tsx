import type { SavedTarget, TargetRole } from '../api-types';
import { Button } from '../ui/Button';
import { useTargetsOptional } from './TargetContext';

/**
 * Quick access to saved targets of one role: click a chip to fill the picker,
 * "×" to forget it, and "Save" to remember the host currently entered. Renders
 * nothing outside a TargetProvider (e.g. isolated component tests).
 */
export function SavedTargetsBar({
  role,
  currentHost,
  currentUserName,
  onPick,
  label = 'Saved',
}: {
  role: TargetRole;
  currentHost: string;
  currentUserName?: string | null;
  onPick: (target: SavedTarget) => void;
  label?: string;
}) {
  const targets = useTargetsOptional();
  if (targets === null) return null;

  const forRole = targets.savedTargets.filter((target) => target.role === role);
  const host = currentHost.trim();
  const alreadySaved = forRole.some((target) => target.host.toUpperCase() === host.toUpperCase());

  if (forRole.length === 0 && host === '') return null;

  return (
    <div className="flex flex-wrap items-center gap-2 rounded-lg border border-slate-800 bg-slate-900/40 px-3 py-2">
      <span className="text-xs font-medium uppercase tracking-wide text-muted">{label}</span>
      {forRole.length === 0 && <span className="text-xs text-muted">none yet</span>}
      {forRole.map((target) => (
        <span
          key={target.id}
          className="inline-flex items-center rounded border border-slate-700 bg-slate-800 pl-2 text-sm"
        >
          <button
            type="button"
            className="py-0.5 text-slate-200 transition-colors hover:text-white"
            title={`Use ${target.host}${target.userName ? ` as ${target.userName}` : ''}`}
            onClick={() => onPick(target)}
          >
            {target.label}
          </button>
          <button
            type="button"
            aria-label={`Forget saved target ${target.label}`}
            className="px-1.5 py-0.5 text-slate-500 transition-colors hover:text-fail-300"
            onClick={() => void targets.deleteTarget(target.id)}
          >
            ×
          </button>
        </span>
      ))}
      {host !== '' && !alreadySaved && (
        <Button
          variant="ghost"
          onClick={() =>
            void targets.saveTarget({ label: host, host, role, userName: currentUserName ?? null })
          }
        >
          Save “{host}”
        </Button>
      )}
    </div>
  );
}
