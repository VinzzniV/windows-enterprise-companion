interface DirectoryEndpointState {
  domain: string;
  server: string;
}

export interface WorkingSetNavigationState {
  directoryEndpoint?: DirectoryEndpointState;
  fromWorkingSet?: string;
  workingSetLocationKey?: string;
  restoreWorkingSetLocationKey?: string;
}

const workingSetPath = /^\/(devices|users|groups)(\/workspace)?(\?[^#]*)?$/;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null ? value as Record<string, unknown> : null;
}

function directoryEndpoint(value: unknown): DirectoryEndpointState | undefined {
  const candidate = record(value);
  return candidate && typeof candidate.domain === 'string' && typeof candidate.server === 'string'
    ? { domain: candidate.domain, server: candidate.server }
    : undefined;
}

export function workingSetEntryState(
  location: { pathname: string; search: string; key: string },
  endpoint: unknown,
): WorkingSetNavigationState {
  return {
    directoryEndpoint: directoryEndpoint(endpoint),
    fromWorkingSet: `${location.pathname}${location.search}`,
    workingSetLocationKey: location.key,
  };
}

export function continueWorkingSetNavigation(value: unknown): WorkingSetNavigationState {
  const candidate = record(value);
  if (!candidate) return {};
  const fromWorkingSet = typeof candidate.fromWorkingSet === 'string' && workingSetPath.test(candidate.fromWorkingSet)
    ? candidate.fromWorkingSet
    : undefined;
  const workingSetLocationKey = fromWorkingSet && typeof candidate.workingSetLocationKey === 'string'
    ? candidate.workingSetLocationKey
    : undefined;
  return {
    directoryEndpoint: directoryEndpoint(candidate.directoryEndpoint),
    fromWorkingSet,
    workingSetLocationKey,
  };
}

export function workingSetReturn(value: unknown, fallback: '/devices' | '/users' | '/groups') {
  const state = continueWorkingSetNavigation(value);
  return {
    to: state.fromWorkingSet ?? fallback,
    state: state.fromWorkingSet
      ? { directoryEndpoint: state.directoryEndpoint, restoreWorkingSetLocationKey: state.workingSetLocationKey }
      : undefined,
  };
}

export function workingSetRestoreKey(value: unknown): string | undefined {
  const candidate = record(value);
  return typeof candidate?.restoreWorkingSetLocationKey === 'string'
    ? candidate.restoreWorkingSetLocationKey
    : undefined;
}
