import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type {
  ManagedWingetPackagesResult,
  PatchDepotSummary,
  SearchWingetPackagesResult,
  WingetManagedPackageView,
  WingetPackageInfo,
  WingetPackageOperationOutcome,
  WingetPackagePreview,
  WingetUpdateCheckResult,
  WingetUpdateOutcome,
  WingetUpdatePlan,
  WingetUpdateSelection,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { ErrorState } from '../../shared/ui/States';

function opsiIdFromWingetId(id: string): string {
  return id.toLocaleLowerCase().replace(/[^a-z0-9._-]+/g, '-').slice(0, 64);
}

function formatTimestamp(value: string | null): string {
  return value ? new Date(value).toLocaleString() : 'Never';
}

function operationError(error: unknown, message: string): ErrorPresentation {
  return presentError(error, { message });
}

function Outcome({ outcome }: { outcome: WingetPackageOperationOutcome }) {
  return (
    <p className={outcome.success ? 'text-ok-300' : 'text-fail-300'}>
      {outcome.opsiProductId}: {outcome.success
        ? `${outcome.oldVersion ?? 'new'} → ${outcome.newVersion}`
        : outcome.error ?? 'Operation failed'}
    </p>
  );
}

export function WingetPackagesWorkspace({
  connected,
  depots,
  preferredDepot,
  onChanged,
}: {
  connected: boolean;
  depots: PatchDepotSummary[];
  preferredDepot: string;
  onChanged: () => void;
}) {
  const [query, setQuery] = useState('');
  const [searchResults, setSearchResults] = useState<WingetPackageInfo[]>([]);
  const [managed, setManaged] = useState<WingetManagedPackageView[]>([]);
  const [opsiProductId, setOpsiProductId] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [wingetId, setWingetId] = useState('');
  const [depotId, setDepotId] = useState(preferredDepot || depots[0]?.id || '');
  const [preview, setPreview] = useState<WingetPackagePreview | null>(null);
  const [updatePlan, setUpdatePlan] = useState<WingetUpdatePlan | null>(null);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [selectedManagedProductId, setSelectedManagedProductId] = useState<string | null>(null);
  const [lastCheck, setLastCheck] = useState<WingetUpdateCheckResult | null>(null);
  const [outcomes, setOutcomes] = useState<WingetPackageOperationOutcome[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [packageSelectionRevision, setPackageSelectionRevision] = useState(0);
  const packageFormRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (preferredDepot) setDepotId(preferredDepot);
  }, [preferredDepot]);

  const loadManaged = useCallback(async () => {
    const result = await invoke<ManagedWingetPackagesResult>('patchmanagement', 'listManagedWingetPackages', {});
    setManaged(result.packages);
  }, []);

  const checkUpdates = useCallback(async (force: boolean) => {
    setBusy(force ? 'check' : 'daily-check');
    setError(null);
    try {
      const result = await invoke<WingetUpdateCheckResult>('patchmanagement', 'checkWingetUpdates', {
        productIds: null,
        force,
      });
      setManaged(result.packages);
      setLastCheck(result);
    } catch (requestError) {
      setError(operationError(requestError, 'Winget versions could not be checked. The opsi overview remains available.'));
      try { await loadManaged(); } catch { /* Preserve the Winget error above. */ }
    } finally {
      setBusy(null);
    }
  }, [loadManaged]);

  useEffect(() => {
    if (connected) void checkUpdates(false);
  }, [checkUpdates, connected]);

  useEffect(() => {
    if (packageSelectionRevision > 0) {
      packageFormRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, [packageSelectionRevision]);

  const search = async () => {
    if (!query.trim()) return;
    setBusy('search');
    setError(null);
    setPreview(null);
    try {
      const result = await invoke<SearchWingetPackagesResult>('patchmanagement', 'searchWingetPackages', {
        query: query.trim(),
        limit: 25,
      });
      setSearchResults(result.packages);
    } catch (requestError) {
      setError(operationError(requestError, 'The Winget catalog search failed.'));
    } finally {
      setBusy(null);
    }
  };

  const choosePackage = (item: WingetPackageInfo) => {
    setWingetId(item.id);
    setDisplayName(item.name);
    setOpsiProductId(opsiIdFromWingetId(item.id));
    setPreview(null);
    setOutcomes([]);
    setPackageSelectionRevision((current) => current + 1);
  };

  const createPreview = async () => {
    setBusy('preview');
    setError(null);
    try {
      const result = await invoke<WingetPackagePreview>('patchmanagement', 'previewWingetPackage', {
        opsiProductId,
        displayName,
        wingetId,
        depotId,
      });
      setPreview(result);
    } catch (requestError) {
      setPreview(null);
      setError(operationError(requestError, 'The opsi package preview could not be created.'));
    } finally {
      setBusy(null);
    }
  };

  const buildPackage = async () => {
    if (!preview) return;
    setBusy('build');
    setError(null);
    try {
      const result = await invoke<WingetPackageOperationOutcome>('patchmanagement', 'createOrAdoptWingetPackage', {
        opsiProductId: preview.opsiProductId,
        displayName: preview.displayName,
        wingetId: preview.wingetId,
        depotId: preview.depotId,
        expectedWingetVersion: preview.wingetVersion,
        confirmed: true,
      });
      setOutcomes([result]);
      setPreview(null);
      await loadManaged();
      onChanged();
    } catch (requestError) {
      setError(operationError(requestError, 'The Winget opsi package could not be built and installed.'));
    } finally {
      setBusy(null);
    }
  };

  const toggleUpdate = (productId: string) => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(productId)) next.delete(productId); else next.add(productId);
      return next;
    });
    setUpdatePlan(null);
  };

  const selections = useMemo<WingetUpdateSelection[]>(() => managed
    .filter((item) => selected.has(item.opsiProductId) && item.latestWingetVersion)
    .map((item) => ({ opsiProductId: item.opsiProductId, expectedWingetVersion: item.latestWingetVersion! })),
  [managed, selected]);

  const selectedManagedPackage = useMemo(
    () => managed.find((item) => item.opsiProductId === selectedManagedProductId) ?? null,
    [managed, selectedManagedProductId],
  );

  const prepareUpdates = async () => {
    setBusy('prepare');
    setError(null);
    try {
      setUpdatePlan(await invoke<WingetUpdatePlan>('patchmanagement', 'prepareWingetUpdates', { packages: selections }));
    } catch (requestError) {
      setError(operationError(requestError, 'The selected Winget updates could not be prepared.'));
    } finally {
      setBusy(null);
    }
  };

  const applyUpdates = async () => {
    if (!updatePlan) return;
    setBusy('apply');
    setError(null);
    try {
      const result = await invoke<WingetUpdateOutcome>('patchmanagement', 'applyWingetUpdates', {
        packages: updatePlan.packages.map((item) => ({
          opsiProductId: item.opsiProductId,
          expectedWingetVersion: item.wingetVersion,
        })),
        confirmed: true,
      });
      setOutcomes(result.packages);
      setSelected(new Set());
      setUpdatePlan(null);
      await loadManaged();
      onChanged();
    } catch (requestError) {
      setError(operationError(requestError, 'The selected Winget updates could not be applied.'));
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      {error && <ErrorState title="Winget unavailable" {...error} controls={<Button onClick={() => setError(null)}>Dismiss</Button>} />}
      <Card title="Find a Winget package">
        <p className="mb-3 text-sm text-slate-400">Only machine-wide packages from the Winget community source with a supported installer are eligible.</p>
        <div className="flex gap-2">
          <Input value={query} onChange={(event) => setQuery(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter') void search(); }} placeholder="Name or exact Winget ID" />
          <Button variant="primary" onClick={() => void search()} disabled={!connected || !!busy || !query.trim()}>Search</Button>
          {busy === 'search' && <Spinner label="Searching Winget" />}
        </div>
        {searchResults.length > 0 && (
          <div className="mt-4">
            <DataTable
              columns={[
                { header: 'Package', cell: (item: WingetPackageInfo) => <span><span className="block font-medium">{item.name}</span><span className="block font-mono text-xs text-muted">{item.id}</span></span> },
                { header: 'Publisher', cell: (item: WingetPackageInfo) => item.publisher || '—' },
                { header: 'Version', mono: true, cell: (item: WingetPackageInfo) => item.version },
                { header: 'Installer', cell: (item: WingetPackageInfo) => `${item.installerType} · ${item.architecture}` },
                { header: 'Eligibility', cell: (item: WingetPackageInfo) => item.isEligible ? <span className="text-ok-300">Eligible</span> : <span className="text-fail-300" title={item.ineligibilityReason ?? undefined}>Not eligible</span> },
                { header: '', cell: (item: WingetPackageInfo) => <Button onClick={() => choosePackage(item)} disabled={!item.isEligible}>Use package</Button> },
              ]}
              rows={searchResults}
              getRowKey={(item) => item.id}
              emptyMessage="No matching Winget packages were found."
            />
          </div>
        )}
      </Card>

      {wingetId && (
        <section ref={packageFormRef} className="scroll-mt-4">
          <Card title="Create or adopt an opsi package">
            <div className="grid gap-3 md:grid-cols-2">
              <label className="text-sm text-slate-300">opsi Product ID<Input className="mt-1" value={opsiProductId} onChange={(event) => { setOpsiProductId(event.target.value); setPreview(null); }} /></label>
              <label className="text-sm text-slate-300">Display name<Input className="mt-1" value={displayName} onChange={(event) => { setDisplayName(event.target.value); setPreview(null); }} /></label>
              <label className="text-sm text-slate-300">Winget ID<Input className="mt-1 font-mono" value={wingetId} readOnly /></label>
              <label className="text-sm text-slate-300">Managed depot<Select className="mt-1" value={depotId} onChange={(event) => { setDepotId(event.target.value); setPreview(null); }}>{depots.map((depot) => <option key={depot.id} value={depot.id}>{depot.description ? `${depot.description} (${depot.id})` : depot.id}</option>)}</Select></label>
            </div>
            <div className="mt-3"><Button variant="primary" disabled={!!busy || !opsiProductId || !displayName || !depotId} onClick={() => void createPreview()}>Preview package</Button></div>
          </Card>
        </section>
      )}

      {preview && (
        <Card title={preview.adoptsExistingProduct ? 'Confirm adoption of existing Product ID' : 'Confirm new opsi package'}>
          <p className="text-sm text-warn-200">{preview.confirmationText}</p>
          <dl className="mt-3 grid gap-2 text-sm md:grid-cols-2"><div><dt className="text-muted">Current depot version</dt><dd className="font-mono">{preview.currentDepotVersion ?? 'Not installed'}</dd></div><div><dt className="text-muted">Target depot version</dt><dd className="font-mono">{preview.targetDepotVersion}</dd></div><div><dt className="text-muted">Workbench</dt><dd className="break-all font-mono text-xs">{preview.workbenchPath}</dd></div><div><dt className="text-muted">Installer</dt><dd>{preview.installerType} · {preview.architecture} · {preview.scope}</dd></div></dl>
          <details className="mt-3 rounded border border-slate-800 p-3"><summary className="cursor-pointer text-sm text-slate-300">Generated Winget commands</summary><pre className="mt-2 overflow-auto whitespace-pre-wrap text-xs text-slate-400">{preview.commands.join('\n')}</pre></details>
          <div className="mt-3 flex gap-2"><Button variant="primary" disabled={!!busy} onClick={() => void buildPackage()}>{preview.adoptsExistingProduct ? 'Confirm adoption and build' : 'Confirm and build'}</Button><Button onClick={() => setPreview(null)} disabled={!!busy}>Cancel</Button>{busy === 'build' && <Spinner label="Building and installing opsi package" />}</div>
        </Card>
      )}

      <Card title="Managed Winget packages">
        <div className="mb-3 flex flex-wrap items-center gap-2"><Button onClick={() => void checkUpdates(true)} disabled={!connected || !!busy} title={!connected ? 'Connect to opsi before checking Winget versions.' : undefined}>Check now</Button><Button variant="primary" onClick={() => void prepareUpdates()} disabled={!!busy || selections.length === 0}>Preview selected updates</Button>{busy === 'check' || busy === 'daily-check' ? <Spinner label="Checking Winget versions" /> : null}<span className="text-xs text-muted">Checks are cached for 24 hours unless forced.</span></div>
        {lastCheck && busy !== 'check' && busy !== 'daily-check' && (
          <p className="mb-3 text-sm text-slate-300" role="status">
            Check complete: {lastCheck.checkedCount} checked, {lastCheck.updateCount} update{lastCheck.updateCount === 1 ? '' : 's'} available, {lastCheck.failedCount} failed.
          </p>
        )}
        <DataTable
          columns={[
            { header: '', cell: (item: WingetManagedPackageView) => <input type="checkbox" aria-label={`Select ${item.opsiProductId}`} checked={selected.has(item.opsiProductId)} disabled={!item.updateAvailable} onChange={() => toggleUpdate(item.opsiProductId)} /> },
            { header: 'opsi product', cell: (item: WingetManagedPackageView) => <span><span className="block font-medium">{item.displayName}</span><span className="block font-mono text-xs text-muted">{item.opsiProductId}</span></span> },
            { header: 'Winget ID', mono: true, cell: (item: WingetManagedPackageView) => item.wingetId },
            { header: 'Depot', cell: (item: WingetManagedPackageView) => item.depotId },
            { header: 'Depot version', mono: true, cell: (item: WingetManagedPackageView) => item.currentDepotVersion ?? '—' },
            { header: 'Latest Winget', mono: true, cell: (item: WingetManagedPackageView) => item.latestWingetVersion ?? '—' },
            { header: 'Status', cell: (item: WingetManagedPackageView) => item.lastError ? <span className="text-fail-300" title={item.lastError}>{item.checkStatus}</span> : item.updateAvailable ? <span className="text-warn-300">Update available</span> : <span className="text-ok-300">Current</span> },
            { header: 'Checked', cell: (item: WingetManagedPackageView) => formatTimestamp(item.checkedAtUtc) },
          ]}
          rows={managed}
          getRowKey={(item) => item.opsiProductId}
          onRowClick={(item) => setSelectedManagedProductId(item.opsiProductId)}
          isRowActive={(item) => item.opsiProductId === selectedManagedProductId}
          emptyMessage="No packages are registered as Winget-managed in WEC yet. Existing opsi packages remain Manual until they are searched and explicitly adopted."
        />
        {selectedManagedPackage && (
          <div className="mt-4 rounded border border-slate-700 bg-slate-950/40 p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div><h3 className="font-medium text-slate-100">{selectedManagedPackage.displayName}</h3><p className="font-mono text-xs text-muted">{selectedManagedPackage.opsiProductId} · {selectedManagedPackage.wingetId}</p></div>
              <Button variant="ghost" onClick={() => setSelectedManagedProductId(null)}>Close details</Button>
            </div>
            <dl className="mt-3 grid gap-2 text-sm md:grid-cols-3">
              <div><dt className="text-muted">Depot package</dt><dd className="font-mono">{selectedManagedPackage.currentDepotVersion ?? 'Not installed'}</dd></div>
              <div><dt className="text-muted">Last packaged Winget version</dt><dd className="font-mono">{selectedManagedPackage.lastPackagedWingetVersion ?? 'Never'}</dd></div>
              <div><dt className="text-muted">Latest Winget version</dt><dd className="font-mono">{selectedManagedPackage.latestWingetVersion ?? 'Unknown'}</dd></div>
            </dl>
            <p className={`mt-3 text-sm ${selectedManagedPackage.updateAvailable ? 'text-warn-200' : 'text-slate-300'}`}>
              {selectedManagedPackage.updateAvailable
                ? 'A newer Winget version is available. Select the checkbox in the row, preview the update and confirm the package build.'
                : 'This package is current. The update checkbox becomes available automatically when Check now finds a newer Winget version.'}
            </p>
            <p className="mt-2 text-xs text-muted">On confirmation, WEC generates a new opsi package whose product version matches Winget, resets the opsi package version to 1, and pins setup and update scripts to that exact Winget version. It only installs the package on the depot; client actions remain in opsi.</p>
          </div>
        )}
      </Card>

      {updatePlan && (
        <Card title="Confirm Winget package updates">
          <p className="text-sm text-warn-200">{updatePlan.confirmationText}</p>
          <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-slate-300">{updatePlan.packages.map((item) => <li key={item.opsiProductId}><span className="font-mono">{item.opsiProductId}</span>: {item.currentDepotVersion ?? 'missing'} → {item.targetDepotVersion} on {item.depotId}</li>)}</ul>
          <p className="mt-3 text-xs text-muted">This only updates packages on the depot. WEC will not request any client action.</p>
          <div className="mt-3 flex gap-2"><Button variant="primary" disabled={!!busy} onClick={() => void applyUpdates()}>Confirm and build selected updates</Button><Button disabled={!!busy} onClick={() => setUpdatePlan(null)}>Cancel</Button>{busy === 'apply' && <Spinner label="Applying Winget package updates" />}</div>
        </Card>
      )}

      {outcomes.length > 0 && <Card title="Last package operation">{outcomes.map((outcome) => <Outcome key={`${outcome.opsiProductId}-${outcome.depotId}`} outcome={outcome} />)}</Card>}
    </div>
  );
}
