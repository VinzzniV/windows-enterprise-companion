import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { HygieneDevice, HygieneFindingCode, HygieneStatus, InventorySourceState } from '../../shared/api-types';
import { useEnvironment } from '../../shared/environment/EnvironmentContext';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { ErrorState } from '../../shared/ui/States';
import { SummaryMetric } from '../../shared/ui/SummaryMetric';

type HygieneFilter = 'ALL' | 'HEALTHY' | 'PROBLEMS' | 'INCOMPLETE'
  | 'MISSING_KASPERSKY' | 'ORPHAN_KASPERSKY' | 'MISSING_OPSI' | 'ORPHAN_OPSI'
  | 'STALE' | 'OUTDATED';

const filterLabels: Record<HygieneFilter, string> = {
  ALL: 'All devices', HEALTHY: 'Healthy', PROBLEMS: 'Problems', INCOMPLETE: 'Incomplete data',
  MISSING_KASPERSKY: 'Missing Kaspersky', ORPHAN_KASPERSKY: 'Orphan Kaspersky',
  MISSING_OPSI: 'Missing opsi', ORPHAN_OPSI: 'Orphan opsi', STALE: 'Stale', OUTDATED: 'Outdated',
};

export const findingLabels: Record<HygieneFindingCode, string> = {
  MISSING_KASPERSKY: 'Missing Kaspersky', ORPHAN_KASPERSKY: 'Orphan Kaspersky',
  STALE_AD: 'Stale AD', STALE_KASPERSKY: 'Stale Kaspersky',
  OUTDATED_AGENT: 'Outdated Agent', OUTDATED_KES: 'Outdated KES',
  MISSING_OPSI: 'Missing opsi', ORPHAN_OPSI: 'Orphan opsi', STALE_OPSI: 'Stale opsi',
};

const statusLabels: Record<HygieneStatus, string> = {
  HEALTHY: 'Healthy', WARNING: 'Warning', CLEANUP_CANDIDATE: 'Cleanup candidate', INCOMPLETE: 'Incomplete',
};

export function statusTone(status: HygieneStatus): BadgeTone {
  if (status === 'HEALTHY') return 'ok';
  if (status === 'CLEANUP_CANDIDATE') return 'fail';
  if (status === 'INCOMPLETE') return 'neutral';
  return 'warn';
}

function hasFinding(device: HygieneDevice, codes: HygieneFindingCode[]): boolean {
  return device.assessment.findings.some((finding) => codes.includes(finding.code));
}

function findingStatus(device: HygieneDevice, codes: HygieneFindingCode[], label: string): { label: string; tone: BadgeTone } {
  const findings = device.assessment.findings.filter((finding) => codes.includes(finding.code));
  if (!findings.length) return { label: 'OK', tone: 'ok' };
  return {
    label,
    tone: findings.some((finding) => finding.severity === 'CRITICAL') ? 'fail' : 'warn',
  };
}

function matchesFilter(device: HygieneDevice, filter: HygieneFilter): boolean {
  if (filter === 'ALL') return true;
  if (filter === 'HEALTHY') return device.assessment.status === 'HEALTHY';
  if (filter === 'PROBLEMS') return device.assessment.status === 'WARNING' || device.assessment.status === 'CLEANUP_CANDIDATE';
  if (filter === 'INCOMPLETE') return device.assessment.status === 'INCOMPLETE';
  if (filter === 'STALE') return hasFinding(device, ['STALE_AD', 'STALE_KASPERSKY', 'STALE_OPSI']);
  if (filter === 'OUTDATED') return hasFinding(device, ['OUTDATED_AGENT', 'OUTDATED_KES']);
  return hasFinding(device, [filter]);
}

function sourceLabel(state: InventorySourceState): string {
  return ({ AVAILABLE: 'Available', NOT_CONNECTED: 'Not connected', UNAVAILABLE: 'Unavailable', TRUNCATED: 'Truncated' })[state.availability];
}

function SourceAvailability({ name, state }: { name: string; state: InventorySourceState }) {
  const tone: BadgeTone = state.availability === 'AVAILABLE' ? 'ok' : state.availability === 'TRUNCATED' ? 'warn' : 'neutral';
  return <span title={state.error ?? undefined}><Badge tone={tone}>{name}: {sourceLabel(state)}</Badge></span>;
}

type DeviceSource = 'ad' | 'kaspersky' | 'opsi';

function SourceStatusBadge({ device, state, source }: { device: HygieneDevice; state: InventorySourceState; source: DeviceSource }) {
  if (state.availability !== 'AVAILABLE') return <Badge tone="neutral">{sourceLabel(state)}</Badge>;
  if (source === 'ad') {
    if (!device.activeDirectory.exists) return <Badge tone={hasFinding(device, ['ORPHAN_KASPERSKY', 'ORPHAN_OPSI']) ? 'warn' : 'neutral'}>{hasFinding(device, ['ORPHAN_KASPERSKY', 'ORPHAN_OPSI']) ? 'Missing' : 'N/A'}</Badge>;
    if (!device.activeDirectory.enabled) return <Badge tone="neutral">Disabled</Badge>;
    const status = findingStatus(device, ['STALE_AD'], 'Stale');
    return <Badge tone={status.tone}>{status.label}</Badge>;
  }
  if (source === 'kaspersky') {
    if (!device.kaspersky.exists) return <Badge tone={hasFinding(device, ['MISSING_KASPERSKY']) ? 'warn' : 'neutral'}>{hasFinding(device, ['MISSING_KASPERSKY']) ? 'Missing' : 'N/A'}</Badge>;
    const stale = hasFinding(device, ['STALE_KASPERSKY']);
    const status = findingStatus(
      device,
      ['STALE_KASPERSKY', 'OUTDATED_AGENT', 'OUTDATED_KES'],
      stale ? 'Stale' : 'Outdated',
    );
    return <Badge tone={status.tone}>{status.label}</Badge>;
  }
  if (!device.opsi.exists) return <Badge tone={hasFinding(device, ['MISSING_OPSI']) ? 'warn' : 'neutral'}>{hasFinding(device, ['MISSING_OPSI']) ? 'Missing' : 'N/A'}</Badge>;
  const status = findingStatus(device, ['STALE_OPSI'], 'Stale');
  return <Badge tone={status.tone}>{status.label}</Badge>;
}

export function EmployeeLifecyclePage() {
  const navigate = useNavigate();
  const environment = useEnvironment();
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<HygieneFilter>('ALL');
  useEffect(() => { void environment.ensureLoaded(); }, [environment.ensureLoaded]);

  const result = environment.result;
  const filtered = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return (result?.devices ?? []).filter((device) => {
      if (!matchesFilter(device, filter)) return false;
      if (!query) return true;
      return [device.computerName, device.hostName, device.activeDirectory.operatingSystem,
        device.activeDirectory.organizationalUnit, device.kaspersky.administrationGroup, device.opsi.depotId,
        ...device.assessment.findings.flatMap((finding) => [findingLabels[finding.code], finding.message])]
        .filter(Boolean).some((value) => value!.toLocaleLowerCase().includes(query));
    });
  }, [filter, result, search]);

  const columns: DataColumn<HygieneDevice>[] = result ? [
    { header: 'Device', cell: (row) => <div><div className="font-mono font-medium text-slate-100">{row.computerName}</div>
      {row.activeDirectory.operatingSystem && <div className="text-xs text-slate-500">{row.activeDirectory.operatingSystem}</div>}</div>, sortValue: (row) => row.computerName },
    { header: 'AD', cell: (row) => <SourceStatusBadge device={row} state={result.sources.activeDirectory} source="ad" /> },
    { header: 'Kaspersky', cell: (row) => <SourceStatusBadge device={row} state={result.sources.kaspersky} source="kaspersky" /> },
    { header: 'opsi', cell: (row) => <SourceStatusBadge device={row} state={result.sources.opsi} source="opsi" /> },
    { header: 'Overall', cell: (row) => <Badge tone={statusTone(row.assessment.status)}>{statusLabels[row.assessment.status]}</Badge> },
  ] : [];

  return <div className="flex flex-col gap-4">
    <PageHeader title="IT Lifecycle" subtitle="Environment health across Active Directory, Kaspersky and opsi — read-only.">
      <Button variant="primary" onClick={() => void environment.refresh()} disabled={environment.loading}>{environment.loading ? 'Loading…' : 'Refresh'}</Button>
    </PageHeader>
    {environment.error && <ErrorState title="Environment data could not be loaded" message={environment.error} />}
    {environment.loading && !result && <Spinner label="Loading environment inventory" />}
    {result && <>
      <div className="flex flex-wrap gap-2">
        <SourceAvailability name="AD" state={result.sources.activeDirectory} />
        <SourceAvailability name="Kaspersky" state={result.sources.kaspersky} />
        <SourceAvailability name="opsi" state={result.sources.opsi} />
      </div>
      <div className="flex flex-wrap gap-3">
        <SummaryMetric label="Devices total" value={result.summary.total} />
        <SummaryMetric label="Healthy" value={result.summary.healthy} tone="success" />
        <SummaryMetric label="Problems" value={result.summary.problems} tone={result.summary.problems ? 'warning' : 'success'} />
        <SummaryMetric label="Incomplete" value={result.summary.incomplete} />
        <SummaryMetric label="Stale" value={result.summary.stale} tone={result.summary.stale ? 'danger' : 'success'} />
        <SummaryMetric label="Missing Kaspersky" value={result.summary.missingKaspersky} tone={result.summary.missingKaspersky ? 'warning' : 'success'} />
        <SummaryMetric label="Missing opsi" value={result.summary.missingOpsi} tone={result.summary.missingOpsi ? 'warning' : 'success'} />
        <SummaryMetric label="Outdated" value={result.summary.outdated} tone={result.summary.outdated ? 'warning' : 'success'} />
      </div>
      <Card title={`Devices (${filtered.length} of ${result.devices.length})`}>
        <div className="mb-3 flex flex-wrap gap-3">
          <Input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search device, OU, group, depot or finding…" aria-label="Search devices" className="min-w-72" />
          <Select fullWidth={false} value={filter} onChange={(event) => setFilter(event.target.value as HygieneFilter)} aria-label="Filter devices">
            {(Object.keys(filterLabels) as HygieneFilter[]).map((value) => <option key={value} value={value}>{filterLabels[value]}</option>)}
          </Select>
        </div>
        <DataTable columns={columns} rows={filtered} getRowKey={(row) => row.computerName} onRowClick={(row) => navigate(`/clients/${encodeURIComponent(row.hostName)}`)} stickyHeader emptyMessage="No devices match the current search and filter." />
      </Card>
      <p className="text-xs text-slate-500">Assessed {new Date(result.assessedAtUtc).toLocaleString()}{result.domainName ? ` · AD domain ${result.domainName}` : ''}. No remediation actions are available.</p>
    </>}
  </div>;
}
