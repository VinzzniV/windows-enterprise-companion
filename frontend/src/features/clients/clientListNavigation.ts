import type { ItHygieneRequest } from '../../shared/api-types';

const clientsViewStorageKey = 'wec.view.clients-list';

interface StoredClientsView {
  scope: string;
  url: string;
}

export function clientListScope(request: ItHygieneRequest): string {
  const ad = request.activeDirectory;
  const kaspersky = request.kaspersky;
  return JSON.stringify({
    adDomain: ad?.domain?.trim().toLocaleLowerCase() ?? '',
    adServer: ad?.server?.trim().toLocaleLowerCase() ?? '',
    adUser: ad?.userName?.trim().toLocaleLowerCase() ?? '',
    adUserDomain: ad?.userDomain?.trim().toLocaleLowerCase() ?? '',
    kasperskyServer: kaspersky?.server?.trim().toLocaleLowerCase() ?? '',
    kasperskyUser: kaspersky?.userName?.trim().toLocaleLowerCase() ?? '',
    kasperskyDomain: kaspersky?.domain?.trim().toLocaleLowerCase() ?? '',
  });
}

export function rememberClientListUrl(url: string, scope: string): void {
  if (!isClientListUrl(url)) return;
  try {
    sessionStorage.setItem(clientsViewStorageKey, JSON.stringify({ scope, url } satisfies StoredClientsView));
  } catch {
    // Session storage is optional; the current URL remains the source of truth.
  }
}

export function readClientListUrl(scope: string): string {
  try {
    const stored = JSON.parse(sessionStorage.getItem(clientsViewStorageKey) ?? 'null') as StoredClientsView | null;
    return stored?.scope === scope && isClientListUrl(stored.url) ? stored.url : '/clients';
  } catch {
    return '/clients';
  }
}

export function isClientListUrl(value: unknown): value is string {
  return typeof value === 'string' && (value === '/clients' || value.startsWith('/clients?'));
}
