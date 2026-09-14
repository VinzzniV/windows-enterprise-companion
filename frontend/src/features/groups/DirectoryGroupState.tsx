import type { DirectoryGroupReadState } from '../../shared/api-types.generated';
import { timestamp } from '../microsoft365/Microsoft365Fields';

export function DirectoryGroupState({ state, now }: { state: DirectoryGroupReadState; now: number }) {
  const expired = state.retainedUntilUtc !== null && Date.parse(state.retainedUntilUtc) <= now;
  return <div className="my-3 space-y-1 text-xs text-muted">
    <p>{expired ? 'Retention expired' : state.retrievedAtUtc === null ? 'No cached source facts'
      : state.stale || state.freshUntilUtc && Date.parse(state.freshUntilUtc) <= now ? 'Stale source evidence' : 'Fresh source evidence'}</p>
    <p>Retrieved: {timestamp(state.retrievedAtUtc)} · Last attempt: {timestamp(state.lastAttemptAtUtc)}</p>
    {state.lastAttemptError && <p role="alert" className="text-fail-400">{state.lastAttemptError.message} Previous facts retain their original timestamp.</p>}
  </div>;
}
