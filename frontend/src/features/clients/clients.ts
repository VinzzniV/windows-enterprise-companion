import type {
  AdComputer,
  HygieneDevice,
  SavedTarget,
  StoredInventoryHost,
  StoredSecurityScanHost,
  TargetRequest,
} from '../../shared/api-types';
import type { CredentialValues } from '../../shared/targets/Credentials';
import { hostAddressKey, isExactLocalName } from '../../shared/targets/hostAddress';

/** A client in the workspace list, merged from AD, scan history and saved targets. */
export interface ClientEntry {
  /** Scan target host (FQDN preferred when known). */
  host: string;
  /** Full address comparison key; not proof of physical device identity. */
  key: string;
  /** Short display name. */
  name: string;
  os: string | null;
  /** AD description (free-text note maintained by the admins). */
  description: string | null;
  /** AD enabled flag (true when the source doesn't know). */
  enabled: boolean;
  /** Has a stored inventory snapshot. */
  scanned: boolean;
  capturedAtUtc: string | null;
  /** Has a persisted security scan. */
  securityScanned: boolean;
  securityCompletedAtUtc: string | null;
  /** Is a saved target. */
  saved: boolean;
  inAd: boolean;
  environment: HygieneDevice | null;
}

export type GroupMode = 'none' | 'os' | 'site';

export function clientKey(host: string): string {
  return hostAddressKey(host);
}

/** Site code = the name prefix before the first '-' (KF/PK/MA/KW/SU …), else "Other". */
export function siteOf(name: string): string {
  const dash = name.indexOf('-');
  if (dash <= 0) return 'Other';
  return name.slice(0, dash).trim().toUpperCase() || 'Other';
}

/**
 * Merge the three client sources into one de-duplicated list. AD provides the
 * name/OS, scan history marks what has already been captured, saved targets
 * flag pinned servers/clients. Hosts not in AD (scanned-only, saved-only) are
 * still listed.
 */
export function buildClientList(
  environmentDevices: readonly HygieneDevice[] | readonly AdComputer[],
  scannedHosts: readonly StoredInventoryHost[],
  savedClients: readonly SavedTarget[],
  securityHosts: readonly StoredSecurityScanHost[] = [],
): ClientEntry[] {
  const byKey = new Map<string, ClientEntry>();

  for (const item of environmentDevices) {
    const device = 'computerName' in item ? item : null;
    const legacy = device ? null : item as AdComputer;
    const host = device ? device.hostName : legacy!.dnsHostName ?? legacy!.name;
    byKey.set(clientKey(host), {
      host,
      key: clientKey(host),
      name: device ? device.computerName : legacy!.name,
      os: device ? device.activeDirectory.operatingSystem : legacy!.operatingSystem,
      description: device ? device.activeDirectory.description ?? device.opsi.description : legacy!.description,
      enabled: device ? device.activeDirectory.enabled ?? true : legacy!.enabled,
      scanned: false,
      capturedAtUtc: null,
      securityScanned: false,
      securityCompletedAtUtc: null,
      saved: false,
      inAd: device ? device.activeDirectory.exists : true,
      environment: device,
    });
  }

  for (const stored of scannedHosts) {
    const key = clientKey(stored.host);
    const existing = byKey.get(key);
    if (existing) {
      existing.scanned = true;
      existing.capturedAtUtc = stored.capturedAtUtc;
    } else {
      byKey.set(key, {
        host: stored.host,
        key,
        name: stored.host,
        os: null,
        description: null,
        enabled: true,
        scanned: true,
        capturedAtUtc: stored.capturedAtUtc,
        securityScanned: false,
        securityCompletedAtUtc: null,
        saved: false,
        inAd: false,
        environment: null,
      });
    }
  }

  for (const target of savedClients) {
    const key = clientKey(target.host);
    const existing = byKey.get(key);
    if (existing) {
      existing.saved = true;
    } else {
      byKey.set(key, {
        host: target.host,
        key,
        name: target.label.trim() || target.host,
        os: null,
        description: null,
        enabled: true,
        scanned: false,
        capturedAtUtc: null,
        securityScanned: false,
        securityCompletedAtUtc: null,
        saved: true,
        inAd: false,
        environment: null,
      });
    }
  }

  for (const stored of securityHosts) {
    const key = clientKey(stored.host);
    const existing = byKey.get(key);
    if (existing) {
      existing.securityScanned = true;
      existing.securityCompletedAtUtc = stored.completedAtUtc;
    } else {
      byKey.set(key, {
        host: stored.host,
        key,
        name: stored.host,
        os: null,
        description: null,
        enabled: true,
        scanned: false,
        capturedAtUtc: null,
        securityScanned: true,
        securityCompletedAtUtc: stored.completedAtUtc,
        saved: false,
        inAd: false,
        environment: null,
      });
    }
  }

  return [...byKey.values()].sort((a, b) => a.name.localeCompare(b.name));
}

export function isLocalClient(host: string, machineName: string | null): boolean {
  return isExactLocalName(host, machineName);
}

/**
 * Scan target for a fixed client host. The local machine scans as the current
 * user (null target); a remote host carries the session credentials the user
 * entered, or none (current user / Kerberos) when they haven't.
 */
export function toClientTarget(
  host: string,
  machineName: string | null,
  credentials: CredentialValues | undefined,
): TargetRequest | null {
  if (isLocalClient(host, machineName)) {
    return null;
  }
  if (credentials && credentials.userName.trim() !== '') {
    return {
      host,
      userName: credentials.userName.trim(),
      domain: credentials.domain.trim() || null,
      password: credentials.password,
    };
  }
  return { host };
}

export function filterClients(clients: readonly ClientEntry[], term: string): ClientEntry[] {
  const needle = term.trim().toLowerCase();
  if (needle === '') return [...clients];
  return clients.filter((client) =>
    [client.name, client.host, client.os ?? '', client.description ?? '',
      ...(client.environment?.assessment.findings.map((finding) => finding.message) ?? [])].some((field) =>
      field.toLowerCase().includes(needle),
    ),
  );
}

export interface ClientGroup {
  label: string;
  clients: ClientEntry[];
}

export function groupClients(clients: readonly ClientEntry[], mode: GroupMode): ClientGroup[] {
  if (mode === 'none') {
    return [{ label: '', clients: [...clients] }];
  }
  const groups = new Map<string, ClientEntry[]>();
  for (const client of clients) {
    const label = mode === 'os' ? client.os ?? 'Unknown OS' : siteOf(client.name);
    const bucket = groups.get(label);
    if (bucket) {
      bucket.push(client);
    } else {
      groups.set(label, [client]);
    }
  }
  return [...groups.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([label, grouped]) => ({ label, clients: grouped }));
}
