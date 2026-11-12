import type { ManagementDeviceRecordReference, ManagementDeviceSource } from '../api-types.generated';

const sources: Record<ManagementDeviceSource, string> = { ACTIVE_DIRECTORY: 'ad', KASPERSKY: 'ksc', OPSI: 'opsi', NESSUS: 'nessus' };
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function managementRecordPath(reference: ManagementDeviceRecordReference): string {
  return `/devices/records/${sources[reference.source]}/${encodeURIComponent(reference.workspace)}/${reference.snapshotId}/${reference.recordIndex}`;
}

export function managementRecordReference(source?: string, workspace?: string, snapshot?: string, index?: string): ManagementDeviceRecordReference | null {
  const key = Object.entries(sources).find(([, value]) => value === source)?.[0] as ManagementDeviceSource | undefined;
  if (!key || !workspace?.trim() || workspace.length > 253 || /[\x00-\x1f\x7f]/.test(workspace)
    || !snapshot || !guid.test(snapshot) || !index || !/^(0|[1-9]\d{0,9})$/.test(index) || Number(index) > 2_147_483_647) return null;
  return { source: key, workspace, snapshotId: snapshot.toLowerCase(), recordIndex: Number(index) };
}
