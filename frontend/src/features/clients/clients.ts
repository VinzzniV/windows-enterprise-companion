import type {
  AdComputer,
  HygieneDevice,
  SavedTarget,
  StoredInventoryHost,
  StoredSecurityScanHost,
  TargetRequest,
} from '../../shared/api-types';
import type { CredentialValues } from '../../shared/targets/Credentials';

/** A client in the workspace list, merged from AD, scan history and saved targets. */
export interface ClientEntry {
  /** Scan target host (FQDN preferred when known). */
  host: string;
  /** Case-insensitive complete host or address identity. */
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

/** Complete normalized hostname, FQDN or address. */
export function clientKey(host: string): string {
  return host.trim().replace(/\.+$/, '').toUpperCase();
}

function addAliasOwner(owners: Map<string, Set<string>>, alias: string, canonicalKey: string): void {
  const key = clientKey(alias);
  const existing = owners.get(key) ?? new Set<string>();
  existing.add(canonicalKey);
  owners.set(key, existing);
}

function resolveClientKey(host: string, clients: Map<string, ClientEntry>, aliasOwners: Map<string, Set<string>>): string {
  const key = clientKey(host);
  if (clients.has(key)) return key;
  const owners = aliasOwners.get(key);
  return owners?.size === 1 ? [...owners][0] : key;
}

export function findDeviceByHost(devices: readonly HygieneDevice[], host: string): HygieneDevice | null {
  const candidate = clientKey(host);
  const exact = devices.find((device) => clientKey(device.hostName) === candidate);
  if (exact) return exact;

  const aliasMatches = devices.filter((device) =>
    [device.computerName, device.hostName].some((alias) => clientKey(alias) === candidate),
  );
  return aliasMatches.length === 1 ? aliasMatches[0] : null;
}

export function findClientByHost(clients: readonly ClientEntry[], host: string): ClientEntry | null {
  const candidate = clientKey(host);
  const exact = clients.find((client) => client.key === candidate);
  if (exact) return exact;

  const aliasMatches = clients.filter((client) => clientKey(client.name) === candidate);
  return aliasMatches.length === 1 ? aliasMatches[0] : null;
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
  const aliasOwners = new Map<string, Set<string>>();

  for (const item of environmentDevices) {
    const device = 'computerName' in item ? item : null;
    const legacy = device ? null : item as AdComputer;
    const host = device ? device.hostName : legacy!.dnsHostName ?? legacy!.name;
    const key = clientKey(host);
    byKey.set(key, {
      host,
      key,
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
    const aliases = [device?.computerName ?? legacy!.name, host];
    for (const alias of [...aliases, key]) addAliasOwner(aliasOwners, alias, key);
  }

  for (const stored of scannedHosts) {
    const key = resolveClientKey(stored.host, byKey, aliasOwners);
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
    const key = resolveClientKey(target.host, byKey, aliasOwners);
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
    const key = resolveClientKey(stored.host, byKey, aliasOwners);
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

/** True only for an exact local machine-name or locally derived FQDN alias. */
export function isLocalClient(host: string, machineName: string | null, machineFqdn: string | null = null): boolean {
  if (machineName == null || machineName.trim() === '') return false;
  const key = clientKey(host);
  return [machineName, machineFqdn]
    .filter((alias): alias is string => alias != null && alias.trim() !== '')
    .some((alias) => clientKey(alias) === key);
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
  machineFqdn: string | null = null,
): TargetRequest | null {
  if (isLocalClient(host, machineName, machineFqdn)) {
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
