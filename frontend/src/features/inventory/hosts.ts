/**
 * Which stored hosts to restore as their own entries in the Inventory list.
 *
 * The local machine is always shown as the fixed "LOCAL" entry, but the store
 * keys it under the real machine name (ScanTarget.CacheKey =
 * Environment.MachineName). Restoring that verbatim lists this computer twice,
 * so drop the local machine name here (and any literal "LOCAL").
 */
export function hostsToRestore(
  storedHosts: readonly string[],
  localMachineName: string | null,
): string[] {
  const localKey = (localMachineName ?? '').trim().toUpperCase();
  return storedHosts.filter((host) => {
    const key = host.toUpperCase();
    return key !== 'LOCAL' && (localKey === '' || key !== localKey);
  });
}
