import type { ObjectKind, ObjectReference, ObjectSource } from '../api-types.generated';

const sources: Record<ObjectSource, string> = { ACTIVE_DIRECTORY: 'ad', ENTRA: 'entra', INTUNE: 'intune', WEC: 'wec' };
const kinds: Record<ObjectKind, string> = { DEVICE: 'devices', USER: 'users', GROUP: 'groups' };
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function objectReference(kind: ObjectKind, source: string | undefined, scope: string | undefined, id: string | undefined): ObjectReference | null {
  const entry = Object.entries(sources).find(([, route]) => route === source);
  if (!entry || !scope?.trim() || !id?.trim() || scope.length > 253 || id.length > 253 || /[\x00-\x1f\x7f]/.test(scope + id)) return null;
  const objectSource = entry[0] as ObjectSource;
  if (objectSource !== 'WEC' && !guid.test(id)) return null;
  if ((objectSource === 'ENTRA' || objectSource === 'INTUNE') && !guid.test(scope)) return null;
  if (kind !== 'DEVICE' && (objectSource === 'WEC' || objectSource === 'INTUNE')) return null;
  return { kind, source: objectSource, scope, id };
}

export function objectPath(reference: ObjectReference): string {
  return `/${kinds[reference.kind]}/${sources[reference.source]}/${encodeURIComponent(reference.scope)}/${encodeURIComponent(reference.id)}`;
}

export const objectSourceLabel: Record<ObjectSource, string> = { ACTIVE_DIRECTORY: 'Active Directory', ENTRA: 'Entra', INTUNE: 'Intune', WEC: 'Stored Windows target' };
