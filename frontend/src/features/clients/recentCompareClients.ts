import { loadView, saveView } from '../../shared/viewCache';
import { clientKey } from './clients';

const recentCompareViewKey = 'client-compare-recent';
const recentCompareViewVersion = 2;
const maximumRecentHosts = 8;

interface RecentCompareView {
  version: typeof recentCompareViewVersion;
  hosts: string[];
}

function normalizeHosts(value: unknown): string[] {
  if (!Array.isArray(value)) return [];

  const normalized: string[] = [];
  const seen = new Set<string>();
  for (const candidate of value) {
    if (typeof candidate !== 'string') continue;
    const host = candidate.trim();
    const key = clientKey(host);
    if (key === '' || seen.has(key)) continue;
    normalized.push(host);
    seen.add(key);
    if (normalized.length === maximumRecentHosts) break;
  }
  return normalized;
}

export function loadRecentCompareHosts(): string[] {
  const cached = loadView<unknown>(recentCompareViewKey);
  if (cached === null || typeof cached !== 'object' || Array.isArray(cached)) return [];

  const view = cached as Partial<RecentCompareView>;
  return view.version === recentCompareViewVersion ? normalizeHosts(view.hosts) : [];
}

/** Records the completed pair first and returns the new bounded recent order. */
export function recordRecentCompareHosts(hosts: readonly string[]): string[] {
  const nextHosts = normalizeHosts([...hosts, ...loadRecentCompareHosts()]);
  saveView<RecentCompareView>(recentCompareViewKey, {
    version: recentCompareViewVersion,
    hosts: nextHosts,
  });
  return nextHosts;
}
