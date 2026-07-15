/**
 * Remembers what a page last showed, across app restarts.
 *
 * The host runs the frontend from a stable origin (https://<virtual host>) with a
 * persistent WebView2 user-data folder, so localStorage survives a restart — no
 * server round trip needed to bring a view back.
 *
 * Never put credentials in here: passwords stay in memory for the session only
 * (ADR 0007, ADR 0008). Cached payloads land unencrypted in the user-data folder,
 * the same machine-local trust boundary as the snapshot database.
 *
 * The cache is a convenience, never load-bearing: every failure is a miss.
 */
const keyPrefix = 'wec.view.';

export function loadView<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(keyPrefix + key);
    return raw === null ? null : (JSON.parse(raw) as T);
  } catch {
    return null;
  }
}

export function saveView<T>(key: string, value: T): void {
  try {
    localStorage.setItem(keyPrefix + key, JSON.stringify(value));
  } catch {
    // Unavailable or full — the page still works, it just won't remember.
  }
}
