import { beforeEach, describe, expect, it } from 'vitest';
import { clientListScope, readClientListUrl, rememberClientListUrl } from './clientListNavigation';

describe('client list navigation scope', () => {
  beforeEach(() => sessionStorage.clear());

  it('retains the last URL only for the same directory and management context', () => {
    const firstScope = clientListScope({
      activeDirectory: { domain: 'corp.example', server: 'dc-a', userName: 'admin' },
      kaspersky: { server: 'ksc-a', userName: 'operator' },
    });
    const secondScope = clientListScope({
      activeDirectory: { domain: 'branch.example', server: 'dc-b', userName: 'admin' },
      kaspersky: { server: 'ksc-a', userName: 'operator' },
    });

    rememberClientListUrl('/clients?q=PC&source=AD&page=2', firstScope);

    expect(readClientListUrl(firstScope)).toBe('/clients?q=PC&source=AD&page=2');
    expect(readClientListUrl(secondScope)).toBe('/clients');
  });

  it('does not retain a detail route as list state', () => {
    const scope = clientListScope({ activeDirectory: {}, kaspersky: null });
    rememberClientListUrl('/clients/PC-01?section=inventory', scope);
    expect(readClientListUrl(scope)).toBe('/clients');
  });
});
