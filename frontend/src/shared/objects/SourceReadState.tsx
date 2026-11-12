import type { Microsoft365ReadState } from '../api-types.generated';

const timestamp = (value: string | null) => value ? new Date(value).toLocaleString() : 'Not available';

export function sourceRetained(state: Microsoft365ReadState, now: number) {
  return state.retainedUntilUtc === null || new Date(state.retainedUntilUtc).getTime() > now;
}

export function SourceReadState({ state, now }: { state: Microsoft365ReadState; now: number }) {
  const expired = !sourceRetained(state, now);
  const freshness = state.freshness === 'UNKNOWN' ? 'Freshness unknown'
    : state.freshness === 'STALE' || state.freshUntilUtc && new Date(state.freshUntilUtc).getTime() <= now ? 'Stale' : 'Fresh';
  const availability = { NOT_CACHED: 'Not queried or no longer cached', AVAILABLE: 'Available', UNAVAILABLE: 'Read failed', NOT_ENABLED: 'Not enabled', NOT_CONNECTED: 'Not connected' }[state.availability];
  return <div className="my-3 space-y-1 text-xs text-muted">
    <p>{expired ? 'Retention expired; load this source to obtain current evidence.' : `${availability} · ${freshness}`}{state.loading ? ' · Reading source…' : ''}</p>
    <p>Retrieved: {timestamp(state.retrievedAtUtc)} · Last attempt: {timestamp(state.lastAttemptAtUtc)}</p>
    <p>{state.coverage === 'PARTIAL' ? 'Partial source query' : state.coverage === 'RETURNED_SET' ? 'Returned source set; permission visibility applies' : 'Coverage unknown'}
      {state.loadedCount !== null ? ` · ${state.loadedCount} records loaded by this query` : ''}{state.declaredTotal !== null ? ` · Source-declared count: ${state.declaredTotal}` : ''}</p>
    {state.lastAttemptError && <p role="alert" className="text-fail-400">{state.lastAttemptError.message} Previous facts retain their original retrieval time.</p>}
  </div>;
}
