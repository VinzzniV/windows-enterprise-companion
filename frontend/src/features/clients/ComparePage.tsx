import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import type {
  AdComputerSearchResult,
  AppInfoResponse,
  HardwareInfoResult,
  LatestScanResult,
  ListInventoryHostsResult,
  ListSecurityScanHostsResult,
  SecurityScanResult,
  StoredInventoryHost,
  StoredSecurityScanHost,
} from '../../shared/api-types';
import { useTargets } from '../../shared/targets/TargetContext';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Toolbar } from '../../shared/ui/Toolbar';
import { Button } from '../../shared/ui/Button';
import { Badge } from '../../shared/ui/Badge';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { CompactErrorState, EmptyState, ErrorState } from '../../shared/ui/States';
import { Spinner } from '../../shared/ui/Spinner';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { buildClientList, toClientTarget } from './clients';
import {
  compareFindings,
  compareInventory,
  compareSoftware,
  type DiffRow,
  type SetDiff,
  type SoftwareComparison,
} from './compare';
import { ClientComparePicker } from './ClientComparePicker';
import { loadRecentCompareHosts, recordRecentCompareHosts } from './recentCompareClients';

type StoredRead<T> =
  | { kind: 'data'; value: T }
  | { kind: 'missing' }
  | { kind: 'error'; error: ErrorPresentation };

interface SideData {
  host: string;
  inventory: StoredRead<HardwareInfoResult>;
  scan: StoredRead<SecurityScanResult>;
}

interface Comparison {
  a: SideData;
  b: SideData;
}

interface SelectionSourceErrors {
  directory?: ErrorPresentation;
  inventory?: ErrorPresentation;
  security?: ErrorPresentation;
  identity?: ErrorPresentation;
}

const sourceReloadAction = 'Reload the client sources. Already loaded client choices remain available.';
const comparisonRetryAction = 'Retry the comparison. It reads stored data only and does not start a scan.';

function SetDiffCard({
  title,
  diff,
  labelA,
  labelB,
}: {
  title: string;
  diff: SetDiff;
  labelA: string;
  labelB: string;
}) {
  return (
    <Card title={title}>
      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap gap-2 text-sm">
          <Badge tone="ok">{diff.both.length} shared</Badge>
          <Badge tone="warn">{diff.onlyA.length} only on {labelA}</Badge>
          <Badge tone="warn">{diff.onlyB.length} only on {labelB}</Badge>
        </div>
        {(diff.onlyA.length > 0 || diff.onlyB.length > 0) && (
          <DetailsDisclosure summary="Show differences">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">
                  Only on {labelA}
                </h4>
                <ul className="flex flex-col gap-0.5 text-sm text-slate-300">
                  {diff.onlyA.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                  {diff.onlyA.length === 0 && <li className="text-muted">—</li>}
                </ul>
              </div>
              <div>
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">
                  Only on {labelB}
                </h4>
                <ul className="flex flex-col gap-0.5 text-sm text-slate-300">
                  {diff.onlyB.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                  {diff.onlyB.length === 0 && <li className="text-muted">—</li>}
                </ul>
              </div>
            </div>
          </DetailsDisclosure>
        )}
      </div>
    </Card>
  );
}

function EvidenceUsed({ comparison }: { comparison: Comparison }) {
  const describeSide = (side: SideData) => {
    const inventory = side.inventory.kind === 'data'
      ? `Inventory captured ${new Date(side.inventory.value.capturedAtUtc).toLocaleString()}.`
      : side.inventory.kind === 'missing' ? 'No stored Inventory snapshot.' : 'Inventory read failed.';
    const software = side.inventory.kind === 'data'
      ? side.inventory.value.snapshot.installedSoftware === null
        ? `Software coverage unavailable${side.inventory.value.snapshot.installedSoftwareError
          ? `: ${side.inventory.value.snapshot.installedSoftwareError.message}`
          : ' in this legacy snapshot'}.`
        : `Software coverage complete: ${side.inventory.value.snapshot.installedSoftware.length} reported products.`
      : 'Software coverage unavailable because Inventory is unavailable.';
    const security = side.scan.kind === 'data'
      ? `Security captured ${new Date(side.scan.value.completedAtUtc).toLocaleString()}; ${
        side.scan.value.coverage.isComplete ? 'coverage complete' : 'coverage incomplete or legacy'}.`
      : side.scan.kind === 'missing' ? 'No stored Security scan.' : 'Security read failed.';
    return { inventory, software, security };
  };

  return (
    <Card title="Evidence used">
      <div className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
        {[comparison.a, comparison.b].map((side) => {
          const evidence = describeSide(side);
          return (
            <section key={side.host} aria-label={`Evidence for ${side.host}`} className="rounded border border-slate-800 p-3">
              <h3 className="font-medium text-slate-200">{side.host}</h3>
              <ul className="mt-1 flex flex-col gap-1 text-slate-400">
                <li>{evidence.inventory}</li>
                <li>{evidence.software}</li>
                <li>{evidence.security}</li>
              </ul>
            </section>
          );
        })}
      </div>
    </Card>
  );
}

export function ComparePage() {
  const navigate = useNavigate();
  const { savedTargets } = useTargets();
  const [adResult, setAdResult] = useState<AdComputerSearchResult | null>(null);
  const [scannedHosts, setScannedHosts] = useState<StoredInventoryHost[]>([]);
  const [securityHosts, setSecurityHosts] = useState<StoredSecurityScanHost[]>([]);
  const [machineName, setMachineName] = useState<string | null>(null);
  const [machineFqdn, setMachineFqdn] = useState<string | null>(null);
  const [selectionSourceErrors, setSelectionSourceErrors] = useState<SelectionSourceErrors>({});
  const [selectionSourcesLoading, setSelectionSourcesLoading] = useState(true);
  const [hostA, setHostA] = useState('');
  const [hostB, setHostB] = useState('');
  const [recentHosts, setRecentHosts] = useState(loadRecentCompareHosts);
  const [loading, setLoading] = useState(false);
  const [comparison, setComparison] = useState<Comparison | null>(null);

  const selectHostA = (host: string) => {
    setHostA(host);
    setComparison(null);
  };

  const selectHostB = (host: string) => {
    setHostB(host);
    setComparison(null);
  };

  const loadSelectionSources = useCallback(async () => {
    setSelectionSourcesLoading(true);
    const [directoryResult, inventoryResult, securityResult, appInfoResult] = await Promise.allSettled([
      invoke<AdComputerSearchResult>(
        'activedirectory',
        'searchComputers',
        { nameFilter: null, includeDisabled: false },
      ),
      invoke<ListInventoryHostsResult>('inventory', 'listHosts'),
      invoke<ListSecurityScanHostsResult>('security', 'listHosts'),
      invoke<AppInfoResponse>('system', 'getAppInfo'),
    ]);

    const errors: SelectionSourceErrors = {};
    if (directoryResult.status === 'fulfilled') {
      setAdResult(directoryResult.value);
    } else {
      errors.directory = presentError(directoryResult.reason, {
        message: 'The Active Directory client list could not be loaded.',
        action: sourceReloadAction,
      });
    }

    if (inventoryResult.status === 'fulfilled') {
      setScannedHosts(inventoryResult.value.hosts);
    } else {
      errors.inventory = presentError(inventoryResult.reason, {
        message: 'The stored client inventory list could not be loaded.',
        action: sourceReloadAction,
      });
    }

    if (securityResult.status === 'fulfilled') {
      setSecurityHosts(securityResult.value.hosts);
    } else {
      errors.security = presentError(securityResult.reason, {
        message: 'The stored client security list could not be loaded.',
        action: sourceReloadAction,
      });
    }

    if (appInfoResult.status === 'fulfilled' && appInfoResult.value.machineName.trim() !== '') {
      setMachineName(appInfoResult.value.machineName);
      setMachineFqdn(appInfoResult.value.machineFqdn);
    } else {
      const reason = appInfoResult.status === 'rejected'
        ? appInfoResult.reason
        : new Error('The desktop host returned an empty machine name.');
      setMachineName(null);
      setMachineFqdn(null);
      errors.identity = presentError(reason, {
        message: 'The local machine identity could not be verified.',
        cause: 'The comparison cannot safely distinguish the local device from a remote target.',
        action: 'Reload the client sources before comparing devices.',
      });
    }

    setSelectionSourceErrors(errors);
    setSelectionSourcesLoading(false);
  }, []);

  useEffect(() => {
    void loadSelectionSources();
  }, [loadSelectionSources]);

  const savedClients = useMemo(
    () => savedTargets.filter((target) => target.role === 'Client'),
    [savedTargets],
  );
  const clients = useMemo(
    () => buildClientList(adResult?.computers ?? [], scannedHosts, savedClients, securityHosts),
    [adResult, scannedHosts, savedClients, securityHosts],
  );

  const loadSide = useCallback(
    async (host: string): Promise<SideData> => {
      const target = toClientTarget(host, machineName, undefined, machineFqdn);
      const [inventory, scan] = await Promise.all([
        invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', {
          target,
          forceRefresh: false,
          cacheOnly: true,
        })
          .then<StoredRead<HardwareInfoResult>>((value) => ({ kind: 'data', value }))
          .catch((error: unknown): StoredRead<HardwareInfoResult> =>
            error instanceof BridgeInvokeError && error.error.code === 'NOT_FOUND'
              ? { kind: 'missing' }
              : {
                  kind: 'error',
                  error: presentError(error, {
                    message: `Stored hardware inventory for ${host} could not be read.`,
                    action: comparisonRetryAction,
                  }),
                }),
        invoke<LatestScanResult>('security', 'getLatestScan', { target })
          .then<StoredRead<SecurityScanResult>>((result) =>
            result.scan === null
              ? { kind: 'missing' }
              : { kind: 'data', value: result.scan })
          .catch((error: unknown): StoredRead<SecurityScanResult> => ({
            kind: 'error',
            error: presentError(error, {
              message: `Stored security scan for ${host} could not be read.`,
              action: comparisonRetryAction,
            }),
          })),
      ]);
      return { host, inventory, scan };
    },
    [machineFqdn, machineName],
  );

  const compare = useCallback(async () => {
    setLoading(true);
    try {
      const [a, b] = await Promise.all([loadSide(hostA), loadSide(hostB)]);
      setComparison({ a, b });
      setRecentHosts(recordRecentCompareHosts([hostA, hostB]));
    } finally {
      setLoading(false);
    }
  }, [hostA, hostB, loadSide]);

  const invDiff: DiffRow[] | null =
    comparison?.a.inventory.kind === 'data' && comparison.b.inventory.kind === 'data'
      ? compareInventory(comparison.a.inventory.value.snapshot, comparison.b.inventory.value.snapshot)
      : null;
  const softwareComparison: SoftwareComparison | null =
    comparison?.a.inventory.kind === 'data' && comparison.b.inventory.kind === 'data'
      ? compareSoftware(comparison.a.inventory.value.snapshot, comparison.b.inventory.value.snapshot)
      : null;

  const columns: DataColumn<DiffRow>[] = comparison
    ? [
        {
          header: 'Property',
          cell: (row) => (
            <span className="text-slate-400">
              <span className="block">{row.label}</span>
              {row.rule && <span className="block text-xs text-muted">{row.rule}</span>}
            </span>
          ),
        },
        { header: comparison.a.host, cell: (row) => row.a },
        { header: comparison.b.host, cell: (row) => row.b },
        {
          header: 'Match',
          align: 'center',
          cell: (row) => row.match === 'exact'
            ? <Badge tone="ok">same</Badge>
            : row.match === 'equivalent'
              ? <Badge tone="ok">within tolerance</Badge>
              : <Badge tone="warn">differs</Badge>,
        },
      ]
    : [];

  const selectionErrors = Object.entries(selectionSourceErrors) as [keyof SelectionSourceErrors, ErrorPresentation][];
  const inventoryErrors = comparison
    ? [comparison.a, comparison.b].filter(
        (side): side is SideData & { inventory: { kind: 'error'; error: ErrorPresentation } } =>
          side.inventory.kind === 'error',
      )
    : [];
  const securityErrors = comparison
    ? [comparison.a, comparison.b].filter(
        (side): side is SideData & { scan: { kind: 'error'; error: ErrorPresentation } } =>
          side.scan.kind === 'error',
      )
    : [];
  const missingInventoryHosts = comparison
    ? [comparison.a, comparison.b]
        .filter((side) => side.inventory.kind === 'missing')
        .map((side) => side.host)
    : [];
  const missingSecurityHosts = comparison
    ? [comparison.a, comparison.b]
        .filter((side) => side.scan.kind === 'missing')
        .map((side) => side.host)
    : [];
  const comparisonHasErrors = inventoryErrors.length > 0 || securityErrors.length > 0;
  const canCompare = hostA !== ''
    && hostB !== ''
    && hostA !== hostB
    && !loading
    && !selectionSourcesLoading
    && selectionSourceErrors.identity === undefined;

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Compare clients" subtitle="Diff two clients from their stored inventory and security scans">
        <Button variant="ghost" onClick={() => navigate('/clients')}>
          ← All clients
        </Button>
      </PageHeader>

      <Toolbar
        actions={
          <Button variant="primary" onClick={() => void compare()} disabled={!canCompare}>
            {loading ? 'Comparing …' : 'Compare'}
          </Button>
        }
      >
        <ClientComparePicker
          label="A"
          ariaLabel="First client"
          clients={clients}
          recentHosts={recentHosts}
          value={hostA}
          onChange={selectHostA}
          disabled={selectionSourcesLoading || loading}
        />
        <ClientComparePicker
          label="B"
          ariaLabel="Second client"
          clients={clients}
          recentHosts={recentHosts}
          value={hostB}
          onChange={selectHostB}
          disabled={selectionSourcesLoading || loading}
        />
      </Toolbar>

      <p className="text-xs text-slate-400">
        Choices combine enabled computers from the current AD search, stored Inventory and Security hosts, and saved client
        targets. Unlike Clients posture, this picker excludes disabled AD computers and does not add Kaspersky-, opsi- or
        Nessus-only devices unless they also occur in a stored or saved source. Availability reflects the latest stored
        timestamps; comparison reads stored data and never starts a scan.
      </p>

      {selectionSourcesLoading && <Spinner label="Loading client sources …" />}

      {selectionErrors.length > 0 && (
        <div className="flex flex-col gap-3">
          {selectionErrors.map(([source, error]) => (
            <ErrorState
              key={source}
              title={source === 'identity' ? 'Target identity unavailable' : 'Client selection incomplete'}
              {...error}
            />
          ))}
          <div>
            <Button variant="secondary" onClick={() => void loadSelectionSources()} disabled={selectionSourcesLoading}>
              Reload client sources
            </Button>
          </div>
        </div>
      )}

      {loading && <Spinner label="Loading stored scans for both clients …" />}

      {comparison && !loading && comparisonHasErrors && (
        <div>
          <Button variant="secondary" onClick={() => void compare()} disabled={!canCompare}>
            Retry comparison
          </Button>
        </div>
      )}

      {!loading && !comparison && (
        <EmptyState
          title="Pick two clients"
          message="Select two clients and compare their stored inventory and security scans. Open a client first if it has not been scanned yet — comparison reads stored snapshots, it does not scan."
        />
      )}

      {comparison && !loading && (
        <>
          <EvidenceUsed comparison={comparison} />

          <Card title="Hardware inventory">
            {inventoryErrors.length > 0 ? (
              <div className="flex flex-col gap-3">
                {inventoryErrors.map((side) => (
                  <CompactErrorState
                    key={side.host}
                    title={`Inventory unavailable — ${side.host}`}
                    {...side.inventory.error}
                  />
                ))}
              </div>
            ) : invDiff ? (
              <DataTable columns={columns} rows={invDiff} emptyMessage="No inventory to compare." />
            ) : (
              <p className="text-sm text-slate-400">
                {missingInventoryHosts.join(', ')} {missingInventoryHosts.length === 1 ? 'has' : 'have'} no stored inventory
                snapshot. Open it and run an inventory scan first.
              </p>
            )}
          </Card>

          {softwareComparison?.status === 'available' ? (
            <>
              <SetDiffCard
                title="Installed software"
                diff={softwareComparison}
                labelA={comparison.a.host}
                labelB={comparison.b.host}
              />
              {softwareComparison.versionDifferences.length > 0 && (
                <Card title="Software version differences">
                  <DataTable
                    columns={columns}
                    rows={softwareComparison.versionDifferences}
                    emptyMessage="No software version differences."
                  />
                </Card>
              )}
            </>
          ) : softwareComparison?.status === 'unknown' ? (
            <Card title="Installed software">
              <p className="text-sm text-warn-400">
                Software comparison is unavailable because capture coverage is unknown for {' '}
                {softwareComparison.unavailableSides.map((side) => side === 'a' ? comparison.a.host : comparison.b.host).join(', ')}.
                No product is classified as present on only one client.
              </p>
            </Card>
          ) : null}

          {securityErrors.length > 0 ? (
            <Card title="Security findings">
              <div className="flex flex-col gap-3">
                {securityErrors.map((side) => (
                  <CompactErrorState
                    key={side.host}
                    title={`Security scan unavailable — ${side.host}`}
                    {...side.scan.error}
                  />
                ))}
              </div>
            </Card>
          ) : comparison.a.scan.kind === 'data' && comparison.b.scan.kind === 'data' ? (
            <>
              {(!comparison.a.scan.value.coverage.isComplete || !comparison.b.scan.value.coverage.isComplete) && (
                <Card title="Security comparison coverage">
                  <p className="text-sm text-warn-400">
                    The finding comparison is observational only: at least one scan has incomplete or legacy
                    coverage, so missing findings are not evidence that a condition is absent.
                  </p>
                </Card>
              )}
              <SetDiffCard
                title="Security findings"
                diff={compareFindings(comparison.a.scan.value.findings, comparison.b.scan.value.findings)}
                labelA={comparison.a.host}
                labelB={comparison.b.host}
              />
            </>
          ) : (
            <Card title="Security findings">
              <p className="text-sm text-slate-400">
                {missingSecurityHosts.join(', ')} {missingSecurityHosts.length === 1 ? 'has' : 'have'} no stored security scan.
                Open it and run a security scan to compare findings.
              </p>
            </Card>
          )}
        </>
      )}
    </div>
  );
}
