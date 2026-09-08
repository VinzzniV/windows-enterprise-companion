import { beforeEach, describe, expect, it } from 'vitest';
import { loadRecentCompareHosts, recordRecentCompareHosts } from './recentCompareClients';

describe('recent compare clients', () => {
  beforeEach(() => localStorage.clear());

  it('treats malformed or unsupported cached views as a miss', () => {
    localStorage.setItem('wec.view.client-compare-recent', '{broken');
    expect(loadRecentCompareHosts()).toEqual([]);

    localStorage.setItem('wec.view.client-compare-recent', JSON.stringify({ version: 3, hosts: ['PC-A'] }));
    expect(loadRecentCompareHosts()).toEqual([]);

    localStorage.setItem('wec.view.client-compare-recent', JSON.stringify({ version: 2, hosts: 'PC-A' }));
    expect(loadRecentCompareHosts()).toEqual([]);
  });

  it('moves a completed pair to the front, preserves full identities and keeps eight hosts', () => {
    localStorage.setItem('wec.view.client-compare-recent', JSON.stringify({
      version: 2,
      hosts: Array.from({ length: 8 }, (_, index) => `OLD-${index}.corp.local`),
    }));

    expect(recordRecentCompareHosts([' PC-A.corp.local ', 'pc-b.corp.local', 'pc-a'])).toEqual([
      'PC-A.corp.local',
      'pc-b.corp.local',
      'pc-a',
      'OLD-0.corp.local',
      'OLD-1.corp.local',
      'OLD-2.corp.local',
      'OLD-3.corp.local',
      'OLD-4.corp.local',
    ]);
    expect(loadRecentCompareHosts()).toHaveLength(8);
  });
});
