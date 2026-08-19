import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import type {
  AdComputerSearchResult,
  AppInfoResponse,
  HardwareInfoResult,
  LatestScanResult,
  ListInventoryHostsResult,
  SecurityScanResult,
  StoredInventoryHost,
} from '../../shared/api-types';
import { useTargets } from '../../shared/targets/TargetContext';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Toolbar } from '../../shared/ui/Toolbar';
import { Select } from '../../shared/ui/Select';
import { Button } from '../../shared/ui/Button';
import { Badge } from '../../shared/ui/Badge';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { EmptyState } from '../../shared/ui/States';
import { Spinner } from '../../shared/ui/Spinner';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { buildClientList, toClientTarget } from './clients';
import { compareFindings, compareInventory, compareSoftware, type DiffRow, type SetDiff } from './compare';

interface SideData {
  host: string;
  inventory: HardwareInfoResult | null;
  scan: SecurityScanResult | null;
}

interface Comparison {
  a: SideData;
  b: SideData;
}

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
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-500">
                  Only on {labelA}
                </h4>
                <ul className="flex flex-col gap-0.5 text-sm text-slate-300">
                  {diff.onlyA.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                  {diff.onlyA.length === 0 && <li className="text-slate-500">—</li>}
                </ul>
              </div>
              <div>
                <h4 className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-500">
                  Only on {labelB}
                </h4>
                <ul className="flex flex-col gap-0.5 text-sm text-slate-300">
                  {diff.onlyB.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                  {diff.onlyB.length === 0 && <li className="text-slate-500">—</li>}
                </ul>
              </div>
            </div>
          </DetailsDisclosure>
        )}
      </div>
    </Card>
  );
}

export function ComparePage() {
  const navigate = useNavigate();
  const { savedTargets } = useTargets();
  const [adResult, setAdResult] = useState<AdComputerSearchResult | null>(null);
  const [scannedHosts, setScannedHosts] = useState<StoredInventoryHost[]>([]);
  const [machineName, setMachineName] = useState<string | null>(null);
  const [hostA, setHostA] = useState('');
  const [hostB, setHostB] = useState('');
  const [loading, setLoading] = useState(false);
  const [comparison, setComparison] = useState<Comparison | null>(null);

  useEffect(() => {
    invoke<AdComputerSearchResult>('activedirectory', 'searchComputers', { nameFilter: null, includeDisabled: false })
      .then(setAdResult)
      .catch(() => setAdResult(null));
    invoke<ListInventoryHostsResult>('inventory', 'listHosts')
      .then((result) => setScannedHosts(result.hosts))
      .catch(() => setScannedHosts([]));
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then((info) => setMachineName(info.machineName))
      .catch(() => setMachineName(null));
  }, []);

  const savedClients = useMemo(
    () => savedTargets.filter((target) => target.role === 'Client'),
    [savedTargets],
  );
  const clients = useMemo(
    () => buildClientList(adResult?.computers ?? [], scannedHosts, savedClients),
    [adResult, scannedHosts, savedClients],
  );

  const loadSide = useCallback(
    async (host: string): Promise<SideData> => {
      const target = toClientTarget(host, machineName, undefined);
      const inventory = await invoke<HardwareInfoResult>('inventory', 'getHardwareInfo', {
        target,
        forceRefresh: false,
        cacheOnly: true,
      }).catch(() => null);
      const scan = await invoke<LatestScanResult>('security', 'getLatestScan', { target })
        .then((result) => result.scan)
        .catch(() => null);
      return { host, inventory, scan };
    },
    [machineName],
  );

  const compare = useCallback(async () => {
    setLoading(true);
    try {
      const [a, b] = await Promise.all([loadSide(hostA), loadSide(hostB)]);
      setComparison({ a, b });
    } finally {
      setLoading(false);
    }
  }, [hostA, hostB, loadSide]);

  const invDiff: DiffRow[] | null =
    comparison?.a.inventory && comparison.b.inventory
      ? compareInventory(comparison.a.inventory.snapshot, comparison.b.inventory.snapshot)
      : null;

  const columns: DataColumn<DiffRow>[] = comparison
    ? [
        { header: 'Property', cell: (row) => <span className="text-slate-400">{row.label}</span> },
        { header: comparison.a.host, cell: (row) => row.a },
        { header: comparison.b.host, cell: (row) => row.b },
        {
          header: 'Match',
          align: 'center',
          cell: (row) => (row.same ? <Badge tone="ok">same</Badge> : <Badge tone="warn">differs</Badge>),
        },
      ]
    : [];

  const canCompare = hostA !== '' && hostB !== '' && hostA !== hostB && !loading;

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
        <label className="flex items-center gap-2 text-sm text-slate-400">
          A
          <Select fullWidth={false} value={hostA} onChange={(event) => setHostA(event.target.value)} aria-label="First client">
            <option value="">Select a client…</option>
            {clients.map((client) => (
              <option key={client.key} value={client.host}>
                {client.name}
              </option>
            ))}
          </Select>
        </label>
        <label className="flex items-center gap-2 text-sm text-slate-400">
          B
          <Select fullWidth={false} value={hostB} onChange={(event) => setHostB(event.target.value)} aria-label="Second client">
            <option value="">Select a client…</option>
            {clients.map((client) => (
              <option key={client.key} value={client.host}>
                {client.name}
              </option>
            ))}
          </Select>
        </label>
      </Toolbar>

      {loading && <Spinner label="Loading stored scans for both clients …" />}

      {!loading && !comparison && (
        <EmptyState
          title="Pick two clients"
          message="Select two clients and compare their stored inventory and security scans. Open a client first if it has not been scanned yet — comparison reads stored snapshots, it does not scan."
        />
      )}

      {comparison && !loading && (
        <>
          <Card title="Hardware inventory">
            {invDiff ? (
              <DataTable columns={columns} rows={invDiff} emptyMessage="No inventory to compare." />
            ) : (
              <p className="text-sm text-slate-400">
                {comparison.a.inventory ? comparison.b.host : comparison.a.host} has no stored inventory
                snapshot. Open it and run an inventory scan first.
              </p>
            )}
          </Card>

          {comparison.a.inventory && comparison.b.inventory && (
            <SetDiffCard
              title="Installed software"
              diff={compareSoftware(comparison.a.inventory.snapshot, comparison.b.inventory.snapshot)}
              labelA={comparison.a.host}
              labelB={comparison.b.host}
            />
          )}

          {comparison.a.scan && comparison.b.scan ? (
            <>
              {(!comparison.a.scan.coverage.isComplete || !comparison.b.scan.coverage.isComplete) && (
                <Card title="Security comparison coverage">
                  <p className="text-sm text-warn-400">
                    The finding comparison is observational only: at least one scan has incomplete or legacy
                    coverage, so missing findings are not evidence that a condition is absent.
                  </p>
                </Card>
              )}
              <SetDiffCard
                title="Security findings"
                diff={compareFindings(comparison.a.scan.findings, comparison.b.scan.findings)}
                labelA={comparison.a.host}
                labelB={comparison.b.host}
              />
            </>
          ) : (
            <Card title="Security findings">
              <p className="text-sm text-slate-400">
                {comparison.a.scan ? comparison.b.host : comparison.a.host} has no stored security scan.
                Open it and run a security scan to compare findings.
              </p>
            </Card>
          )}
        </>
      )}
    </div>
  );
}
