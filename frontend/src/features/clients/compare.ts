import type { HardwareSnapshot, SecurityFinding } from '../../shared/api-types';

export interface DiffRow {
  label: string;
  a: string;
  b: string;
  same: boolean;
}

export interface SetDiff {
  onlyA: string[];
  onlyB: string[];
  both: string[];
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

/** Side-by-side property comparison of two hardware snapshots. */
export function compareInventory(a: HardwareSnapshot, b: HardwareSnapshot): DiffRow[] {
  const rows: DiffRow[] = [];
  const add = (label: string, av: string, bv: string) => rows.push({ label, a: av, b: bv, same: av === bv });

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
  add('Memory', formatBytes(totalMemoryBytes(a)), formatBytes(totalMemoryBytes(b)));
  add('Memory banks', String(a.memoryBanks.length), String(b.memoryBanks.length));
  add('Storage', formatBytes(totalDiskBytes(a)), formatBytes(totalDiskBytes(b)));
  add('Disks', String(a.disks.length), String(b.disks.length));
  add(
    'GPU',
    (a.gpus ?? []).map((gpu) => gpu.name).join(', ') || '—',
    (b.gpus ?? []).map((gpu) => gpu.name).join(', ') || '—',
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

export function compareSoftware(a: HardwareSnapshot, b: HardwareSnapshot): SetDiff {
  return setDiff(
    (a.installedSoftware ?? []).map((entry) => entry.name),
    (b.installedSoftware ?? []).map((entry) => entry.name),
  );
}

const isCoverageNote = (finding: SecurityFinding) =>
  finding.findingId.endsWith('-LOCAL-ONLY') || finding.findingId.endsWith('-NOT-RUN');

/** Compare actual security problems (coverage notes excluded) by title. */
export function compareFindings(a: SecurityFinding[], b: SecurityFinding[]): SetDiff {
  const titles = (findings: SecurityFinding[]) =>
    findings.filter((finding) => !isCoverageNote(finding)).map((finding) => finding.title);
  return setDiff(titles(a), titles(b));
}
