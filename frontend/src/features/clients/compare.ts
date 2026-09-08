import type { HardwareSnapshot, InstalledSoftwareEntry, SecurityFinding } from '../../shared/api-types';

export type ComparisonMatch = 'exact' | 'equivalent' | 'different';

export interface DiffRow {
  label: string;
  a: string;
  b: string;
  same: boolean;
  match: ComparisonMatch;
  rule?: string;
}

export interface SetDiff {
  onlyA: string[];
  onlyB: string[];
  both: string[];
}

export interface SoftwareComparison extends SetDiff {
  status: 'available' | 'unknown';
  unavailableSides: ('a' | 'b')[];
  versionDifferences: DiffRow[];
}

// Small local copy of the inventory byte formatter keeps this pure comparison
// module independent from the rendered Inventory snapshot component.
function formatBytes(bytes: number): string {
  if (bytes <= 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const exponent = Math.min(Math.floor(Math.log2(bytes) / 10), units.length - 1);
  const value = bytes / 2 ** (10 * exponent);
  return `${value.toFixed(value >= 100 ? 0 : 1)} ${units[exponent]}`;
}

export function totalMemoryBytes(snapshot: HardwareSnapshot): number {
  return snapshot.memoryBanks.reduce((sum, bank) => sum + bank.capacityBytes, 0);
}

export function totalDiskBytes(snapshot: HardwareSnapshot): number {
  return snapshot.disks.reduce((sum, disk) => sum + disk.sizeBytes, 0);
}

const normalizeText = (value: string) => value.trim().replace(/\s+/g, ' ').toLocaleLowerCase();

function areWithinTolerance(a: number, b: number, minimumBytes: number): boolean {
  const tolerance = Math.max(minimumBytes, Math.max(a, b) * 0.01);
  return Math.abs(a - b) <= tolerance;
}

function normalizedNames(names: readonly string[]): string[] {
  return names
    .map((name) => name.trim().replace(/\s+/g, ' '))
    .filter((name) => name !== '')
    .sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
}

/** Side-by-side property comparison of two hardware snapshots. */
export function compareInventory(a: HardwareSnapshot, b: HardwareSnapshot): DiffRow[] {
  const rows: DiffRow[] = [];
  const add = (
    label: string,
    av: string,
    bv: string,
    equivalent = normalizeText(av) === normalizeText(bv),
    rule?: string,
    exact = av === bv,
  ) => {
    rows.push({
      label,
      a: av,
      b: bv,
      same: equivalent,
      match: exact ? 'exact' : equivalent ? 'equivalent' : 'different',
      rule,
    });
  };

  add('Operating system', a.operatingSystem.caption, b.operatingSystem.caption);
  add(
    'OS version',
    `${a.operatingSystem.version} (${a.operatingSystem.buildNumber})`,
    `${b.operatingSystem.version} (${b.operatingSystem.buildNumber})`,
  );
  add('Architecture', a.operatingSystem.architecture ?? '—', b.operatingSystem.architecture ?? '—');
  add('CPU', a.cpu.name, b.cpu.name);
  add(
    'CPU cores',
    `${a.cpu.physicalCores} physical / ${a.cpu.logicalProcessors} logical`,
    `${b.cpu.physicalCores} physical / ${b.cpu.logicalProcessors} logical`,
  );
  const memoryA = totalMemoryBytes(a);
  const memoryB = totalMemoryBytes(b);
  add(
    'Memory',
    formatBytes(memoryA),
    formatBytes(memoryB),
    areWithinTolerance(memoryA, memoryB, 64 * 1024 ** 2),
    'Equivalent within 1% or 64 MB.',
    memoryA === memoryB,
  );
  add('Memory banks', String(a.memoryBanks.length), String(b.memoryBanks.length));
  const diskA = totalDiskBytes(a);
  const diskB = totalDiskBytes(b);
  add(
    'Storage',
    formatBytes(diskA),
    formatBytes(diskB),
    areWithinTolerance(diskA, diskB, 1024 ** 3),
    'Equivalent within 1% or 1 GB.',
    diskA === diskB,
  );
  add('Disks', String(a.disks.length), String(b.disks.length));
  const gpuA = normalizedNames((a.gpus ?? []).map((gpu) => gpu.name));
  const gpuB = normalizedNames((b.gpus ?? []).map((gpu) => gpu.name));
  const gpuRawA = (a.gpus ?? []).map((gpu) => gpu.name);
  const gpuRawB = (b.gpus ?? []).map((gpu) => gpu.name);
  add(
    'GPU',
    gpuA.join(', ') || '—',
    gpuB.join(', ') || '—',
    normalizeText(gpuA.join(',')) === normalizeText(gpuB.join(',')),
    'Compared after trimming whitespace and ignoring device order.',
    JSON.stringify(gpuRawA) === JSON.stringify(gpuRawB),
  );
  return rows;
}

/** Case-insensitive set difference preserving the first-seen display value. */
export function setDiff(a: readonly string[], b: readonly string[]): SetDiff {
  const norm = (value: string) => value.trim().toLowerCase();
  const bKeys = new Set(b.map(norm));
  const aKeys = new Set(a.map(norm));
  const seen = new Set<string>();
  const onlyA: string[] = [];
  const both: string[] = [];
  for (const value of a) {
    const key = norm(value);
    if (seen.has(key)) continue;
    seen.add(key);
    (bKeys.has(key) ? both : onlyA).push(value);
  }
  const onlyB: string[] = [];
  const seenB = new Set<string>();
  for (const value of b) {
    const key = norm(value);
    if (seenB.has(key) || aKeys.has(key)) continue;
    seenB.add(key);
    onlyB.push(value);
  }
  return {
    onlyA: onlyA.sort((x, y) => x.localeCompare(y)),
    onlyB: onlyB.sort((x, y) => x.localeCompare(y)),
    both: both.sort((x, y) => x.localeCompare(y)),
  };
}

function softwareByName(entries: readonly InstalledSoftwareEntry[]): Map<string, InstalledSoftwareEntry> {
  const byName = new Map<string, InstalledSoftwareEntry>();
  for (const entry of entries) {
    const key = normalizeText(entry.name);
    if (key !== '' && !byName.has(key)) byName.set(key, entry);
  }
  return byName;
}

export function compareSoftware(a: HardwareSnapshot, b: HardwareSnapshot): SoftwareComparison {
  const aSoftware = a.installedSoftware;
  const bSoftware = b.installedSoftware;
  const unavailableSides: ('a' | 'b')[] = [];
  if (aSoftware === null) unavailableSides.push('a');
  if (bSoftware === null) unavailableSides.push('b');
  if (aSoftware === null || bSoftware === null) {
    return { status: 'unknown', unavailableSides, onlyA: [], onlyB: [], both: [], versionDifferences: [] };
  }

  const aByName = softwareByName(aSoftware);
  const bByName = softwareByName(bSoftware);
  const diff: SetDiff = {
    onlyA: [...aByName.entries()]
      .filter(([key]) => !bByName.has(key))
      .map(([, entry]) => entry.name)
      .sort((x, y) => x.localeCompare(y)),
    onlyB: [...bByName.entries()]
      .filter(([key]) => !aByName.has(key))
      .map(([, entry]) => entry.name)
      .sort((x, y) => x.localeCompare(y)),
    both: [...aByName.entries()]
      .filter(([key]) => bByName.has(key))
      .map(([, entry]) => entry.name)
      .sort((x, y) => x.localeCompare(y)),
  };
  const versionDifferences = [...aByName.entries()].flatMap(([key, aEntry]) => {
    const bEntry = bByName.get(key);
    if (!bEntry || normalizeText(aEntry.version ?? '') === normalizeText(bEntry.version ?? '')) return [];
    return [{
      label: aEntry.name,
      a: aEntry.version?.trim() || 'Unknown',
      b: bEntry.version?.trim() || 'Unknown',
      same: false,
      match: 'different' as const,
      rule: 'Product names match; reported versions differ.',
    }];
  });

  return { status: 'available', unavailableSides, ...diff, versionDifferences };
}

const isCoverageNote = (finding: SecurityFinding) =>
  finding.findingId.endsWith('-LOCAL-ONLY') || finding.findingId.endsWith('-NOT-RUN');

/** Compare actual security problems (coverage notes excluded) by title. */
export function compareFindings(a: SecurityFinding[], b: SecurityFinding[]): SetDiff {
  const titles = (findings: SecurityFinding[]) =>
    findings.filter((finding) => !isCoverageNote(finding)).map((finding) => finding.title);
  return setDiff(titles(a), titles(b));
}
