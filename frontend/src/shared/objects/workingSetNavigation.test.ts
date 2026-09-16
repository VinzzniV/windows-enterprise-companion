import { describe, expect, it } from 'vitest';
import { continueWorkingSetNavigation, workingSetEntryState, workingSetReturn } from './workingSetNavigation';

describe('working-set navigation', () => {
  it('keeps only the result origin and directory endpoint across relationship hops', () => {
    const state = workingSetEntryState({ pathname: '/users', search: '?q=admin&source=ENTRA&page=2', key: 'list-key' }, {
      domain: 'example.test', server: 'dc01.example.test', password: 'must-not-travel',
    });
    expect(continueWorkingSetNavigation({ ...state, password: 'must-not-travel' })).toEqual({
      directoryEndpoint: { domain: 'example.test', server: 'dc01.example.test' },
      fromWorkingSet: '/users?q=admin&source=ENTRA&page=2',
      workingSetLocationKey: 'list-key',
    });
  });

  it('uses a safe object-list fallback for a direct or unrelated bookmark', () => {
    expect(workingSetReturn({ fromWorkingSet: 'https://outside.example/credential', workingSetLocationKey: 'x' }, '/users'))
      .toEqual({ to: '/users', state: undefined });
    expect(workingSetReturn(null, '/devices')).toEqual({ to: '/devices', state: undefined });
  });
});
