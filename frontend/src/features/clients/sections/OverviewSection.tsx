import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  ClientHealthOverview,
  ClientInventoryOverview,
  ClientOverviewResult,
  ClientOverviewSourceMetadata,
  ClientSecurityOverview,
  ClientSoftwareOverview,
  ClientUserOverview,
  HygieneDevice,
  InventorySourceState,
} from '../../../shared/api-types';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../../shared/bridge/errorPresentation';
import { useEnvironment } from '../../../shared/environment/EnvironmentContext';
import { HygieneLoadStatus } from '../../../shared/environment/HygieneLoadStatus';
import { inventorySourceStatus } from '../../../shared/environment/inventorySourceStatus';
import { Button } from '../../../shared/ui/Button';
import { Card } from '../../../shared/ui/Card';
import { CompactErrorState, ErrorState } from '../../../shared/ui/States';
import type { SemanticStatus } from '../../../shared/ui/SemanticStatusBadge';
import { clientKey } from '../clients';
import {
  ClientSemanticStatus,
  hygieneAssessmentStatus,
  sourceFreshnessStatus,
  sourcePresenceStatus,
} from '../clientStatus';
import { DeviceRelationshipMap } from '../DeviceRelationshipMap';

type OverviewLoadState =
  | { kind: 'loading' }
  | { kind: 'ready'; result: ClientOverviewResult; refreshing: boolean; refreshError: ErrorPresentation | null }
  | { kind: 'error'; error: ErrorPresentation };

const DETAIL_SECTIONS = new Set(['inventory', 'diagnostics', 'security']);

function value(entry: string | boolean | null | undefined): string {
  if (typeof entry === 'boolean') return entry ? 'Yes' : 'No';
  return entry == null || entry === '' ? '—' : String(entry);
}

function date(valueToFormat: string | null): string {
  return valueToFormat ? new Date(valueToFormat).toLocaleString() : '—';
}

function formatBytes(bytes: number): string {
  if (bytes <= 0) return '0 GB';
  return `${(bytes / 1024 ** 3).toFixed(bytes >= 100 * 1024 ** 3 ? 0 : 1)} GB`;
}

function formatAge(ageSeconds: number | null): string {
  if (ageSeconds === null) return 'Age unknown';
  if (ageSeconds < 60) return 'Less than a minute old';
  if (ageSeconds < 3600) return `${Math.floor(ageSeconds / 60)} min old`;
  if (ageSeconds < 86400) return `${Math.floor(ageSeconds / 3600)} h old`;
  return `${Math.floor(ageSeconds / 86400)} d old`;
}

function Rows({ entries }: { entries: Array<[string, string]> }) {
  return <dl className="grid grid-cols-[auto_1fr] gap-x-5 gap-y-1.5 text-sm">
    {entries.map(([label, entryValue]) => <div key={label} className="contents"><dt className="text-slate-400">{label}</dt><dd className="break-all text-slate-200">{entryValue}</dd></div>)}
  </dl>;
}

function storedSourceStatus(metadata: ClientOverviewSourceMetadata): SemanticStatus {
  switch (metadata.freshness) {
    case 'FRESH': return { dimension: 'freshness', value: 'fresh' };
    case 'STALE': return { dimension: 'freshness', value: 'stale' };
    case 'MISSING': return { dimension: 'availability', value: 'missing' };
    case 'UNKNOWN': return { dimension: 'availability', value: 'unknown' };
  }
}

function DetailLink({ host, metadata, children }: {
  host: string;
  metadata: ClientOverviewSourceMetadata;
  children: string;
}) {
  const section = DETAIL_SECTIONS.has(metadata.detailSection) ? metadata.detailSection : 'overview';
  const search = section === 'overview' ? '' : `?section=${encodeURIComponent(section)}`;
  return <Link className="text-sm font-medium text-accent-400 hover:text-accent-300" to={`/clients/${encodeURIComponent(host)}${search}`}>{children}</Link>;
}

function SourceEvidence({ host, metadata }: { host: string; metadata: ClientOverviewSourceMetadata }) {
  return <li className="grid gap-2 border-b border-slate-800/80 py-3 last:border-b-0 sm:grid-cols-[minmax(9rem,0.7fr)_minmax(16rem,1.5fr)_auto] sm:items-center">
    <div>
      <p className="font-medium text-slate-200">{metadata.source}</p>
      <p className="mt-0.5 text-xs text-slate-500">{metadata.provenance}</p>
    </div>
    <div>
      <div className="flex flex-wrap items-center gap-2">
        <ClientSemanticStatus status={storedSourceStatus(metadata)} />
        {metadata.freshness !== 'MISSING' && !metadata.isComplete && (
          <ClientSemanticStatus status={{ dimension: 'execution', value: 'partial' }} />
        )}
        <span className="text-xs tabular-nums text-slate-500">{formatAge(metadata.ageSeconds)}</span>
      </div>
      <p className="mt-1 text-xs text-slate-400">{metadata.coverage}</p>
    </div>
    <DetailLink host={host} metadata={metadata}>Open details</DetailLink>
  </li>;
}

function SourceLedger({ host, sources }: { host: string; sources: ClientOverviewSourceMetadata[] }) {
  return <Card title="Stored source evidence">
    <p className="mb-1 text-sm text-slate-400">Latest saved evidence only. Opening this page does not start a scan.</p>
    <ul>{sources.map((source) => <SourceEvidence key={source.source} host={host} metadata={source} />)}</ul>
  </Card>;
}

function InventorySummary({ host, inventory, metadata }: {
  host: string;
  inventory: ClientInventoryOverview | null;
  metadata: ClientOverviewSourceMetadata;
}) {
  const totalStorage = inventory?.disks.reduce((sum, disk) => sum + disk.sizeBytes, 0) ?? 0;
  return <Card title="Device & operating system">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <ClientSemanticStatus status={storedSourceStatus(metadata)} />
        {!metadata.isComplete && metadata.freshness !== 'MISSING' && <ClientSemanticStatus status={{ dimension: 'execution', value: 'partial' }} />}
      </div>
      <DetailLink host={host} metadata={metadata}>Open Inventory</DetailLink>
    </div>
    {inventory ? <>
      <div className="mb-4">
        <p className="text-lg font-semibold tracking-tight text-slate-100">{inventory.operatingSystem}</p>
        <p className="mt-0.5 text-xs text-slate-400">Version {inventory.operatingSystemVersion} · Build {inventory.operatingSystemBuild} · {value(inventory.architecture)}</p>
      </div>
      <Rows entries={[
        ['Processor', `${inventory.cpuName} · ${inventory.physicalCores} cores / ${inventory.logicalProcessors} logical`],
        ['Memory', formatBytes(inventory.totalMemoryBytes)],
        ['Storage', `${formatBytes(totalStorage)} across ${inventory.disks.length} disk${inventory.disks.length === 1 ? '' : 's'}`],
        ['Captured', date(metadata.capturedAtUtc)],
      ]} />
    </> : <p className="text-sm text-slate-400">No stored hardware snapshot. Open Inventory to run an explicit scan.</p>}
  </Card>;
}

function HealthSummary({ host, health, metadata }: {
  host: string;
  health: ClientHealthOverview | null;
  metadata: ClientOverviewSourceMetadata;
}) {
  return <Card title="Health">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <ClientSemanticStatus status={storedSourceStatus(metadata)} />
        {!metadata.isComplete && metadata.freshness !== 'MISSING' && <ClientSemanticStatus status={{ dimension: 'execution', value: 'partial' }} />}
      </div>
      <DetailLink host={host} metadata={metadata}>Open Health</DetailLink>
    </div>
    {health ? <>
      <div className="mb-4 flex flex-wrap gap-x-5 gap-y-2 text-sm tabular-nums">
        <span className="text-fail-300"><strong>{health.criticalCount}</strong> critical</span>
        <span className="text-warn-300"><strong>{health.warningCount}</strong> warning</span>
        <span className="text-slate-300"><strong>{health.unknownCount}</strong> unknown</span>
        <span className="text-ok-300"><strong>{health.healthyCount}</strong> passed</span>
      </div>
      {health.issues.length > 0 ? <ul className="space-y-2">
        {health.issues.map((issue) => <li key={issue.diagnosticId} className="rounded border border-slate-800 bg-slate-950/30 px-3 py-2 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2"><span className="font-medium text-slate-200">{issue.title}</span><span className="text-xs uppercase tracking-wide text-slate-500">{issue.status}</span></div>
          <p className="mt-1 text-xs text-slate-400">{issue.affectedResource}</p>
        </li>)}
      </ul> : <p className="text-sm text-slate-400">{metadata.isComplete ? 'All observed Health checks passed.' : 'No issue detail is available from this incomplete run.'}</p>}
    </> : <p className="text-sm text-slate-400">No stored Health run. Open Health to start the four checks explicitly.</p>}
  </Card>;
}

function SoftwareSummary({ host, software, metadata }: {
  host: string;
  software: ClientSoftwareOverview | null;
  metadata: ClientOverviewSourceMetadata;
}) {
  return <Card title="Installed software">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <ClientSemanticStatus status={storedSourceStatus(metadata)} />
        {!metadata.isComplete && metadata.freshness !== 'MISSING' && <ClientSemanticStatus status={{ dimension: 'execution', value: 'partial' }} />}
      </div>
      <DetailLink host={host} metadata={metadata}>Open software inventory</DetailLink>
    </div>
    {software ? <>
      <p className="mb-3 text-lg font-semibold tabular-nums text-slate-100">{software.installedCount} installed applications</p>
      {software.sample.length > 0 ? <ul className="divide-y divide-slate-800/80">
        {software.sample.map((item) => <li key={`${item.name}\u0000${item.version ?? ''}`} className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1 py-2 text-sm">
          <span className="font-medium text-slate-200">{item.name}</span>
          <span className="text-xs text-slate-500">{value(item.version)}{item.publisher ? ` · ${item.publisher}` : ''}</span>
        </li>)}
      </ul> : <p className="text-sm text-slate-400">The saved capture contains no installed applications.</p>}
      {software.installedCount > software.sample.length && <p className="mt-3 text-xs text-slate-500">Showing {software.sample.length} of {software.installedCount}. Open Inventory for the complete list.</p>}
    </> : <p className="text-sm text-slate-400">No stored software capture. Open Inventory to run an explicit scan.</p>}
  </Card>;
}

function findingSeverityClass(severity: string): string {
  switch (severity.toUpperCase()) {
    case 'CRITICAL':
    case 'HIGH': return 'text-fail-300';
    case 'MEDIUM': return 'text-warn-300';
    default: return 'text-slate-400';
  }
}

function SecuritySummary({ host, security, metadata }: {
  host: string;
  security: ClientSecurityOverview | null;
  metadata: ClientOverviewSourceMetadata;
}) {
  return <Card title="Security posture">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <ClientSemanticStatus status={storedSourceStatus(metadata)} />
        {!metadata.isComplete && metadata.freshness !== 'MISSING' && <ClientSemanticStatus status={{ dimension: 'execution', value: 'partial' }} />}
      </div>
      <DetailLink host={host} metadata={metadata}>Open Security</DetailLink>
    </div>
    {security ? <>
      <div className="mb-4 flex flex-wrap gap-x-5 gap-y-2 text-sm tabular-nums">
        <span className="text-fail-300"><strong>{security.criticalCount}</strong> critical</span>
        <span className="text-fail-300"><strong>{security.highCount}</strong> high</span>
        <span className="text-warn-300"><strong>{security.mediumCount}</strong> medium</span>
        <span className="text-slate-300"><strong>{security.lowCount}</strong> low</span>
      </div>
      {security.topFindings.length > 0 ? <ul className="space-y-2">
        {security.topFindings.map((finding) => <li key={finding.findingId} className="rounded border border-slate-800 bg-slate-950/30 px-3 py-2 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2"><span className="font-medium text-slate-200">{finding.title}</span><span className={`text-xs font-medium uppercase tracking-wide ${findingSeverityClass(finding.severity)}`}>{finding.severity}</span></div>
          <p className="mt-1 text-xs text-slate-400">{finding.affectedResource}</p>
        </li>)}
      </ul> : <p className="text-sm text-slate-400">{metadata.isComplete ? 'No findings in the latest complete Security scan.' : 'No finding detail is available from this incomplete scan.'}</p>}
      <p className="mt-3 text-xs text-slate-500">Scan state: {value(security.scanStatus)} · {metadata.coverage}</p>
    </> : <p className="text-sm text-slate-400">No stored Security scan. Open Security to start an explicit scan.</p>}
  </Card>;
}

function relationshipLabel(valueToFormat: string): string {
  return valueToFormat.toLocaleLowerCase().replaceAll('_', ' ').replace(/^./, (value) => value.toLocaleUpperCase());
}

function UserSummary({ host, users, metadata }: {
  host: string;
  users: ClientUserOverview | null;
  metadata: ClientOverviewSourceMetadata;
}) {
  return <Card title="Linked users">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <ClientSemanticStatus status={storedSourceStatus(metadata)} />
        {!metadata.isComplete && metadata.freshness !== 'MISSING' && <ClientSemanticStatus status={{ dimension: 'execution', value: 'partial' }} />}
      </div>
      <DetailLink host={host} metadata={metadata}>Open Inventory evidence</DetailLink>
    </div>
    {users ? <>
      {users.observations.length > 0 ? <ul className="space-y-2" aria-label="Observed user relationships">
        {users.observations.map((observation) => <li key={`${observation.directorySid}:${observation.relationshipType}`} className="rounded border border-slate-800 bg-slate-950/30 px-3 py-2 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="font-medium text-slate-200">{observation.accountDisplay}</span>
            <span className="text-xs text-slate-400">{observation.confidence.toLocaleLowerCase()} confidence</span>
          </div>
          <p className="mt-1 text-xs text-slate-400">{relationshipLabel(observation.relationshipType)} · observed {date(observation.observedAtUtc)} · {observation.source}</p>
          <p className="mt-1 text-xs text-slate-500">{observation.explanation}</p>
        </li>)}
      </ul> : <p className="text-sm text-slate-400">The latest Inventory scan contains no named interactive-user observation.</p>}
      {users.unresolvedProfileCount > 0 && <p className="mt-3 text-xs text-slate-400">{users.unresolvedProfileCount} additional local profile{users.unresolvedProfileCount === 1 ? '' : 's'} cannot be linked to a displayed directory identity from stored evidence alone.</p>}
      <p className="mt-3 text-xs text-warn-300">These are timestamped observations, not device ownership or assignment claims.</p>
    </> : <p className="text-sm text-slate-400">No stored user/device relationship evidence. Run an explicit Inventory scan to collect the approved evidence.</p>}
  </Card>;
}

function SourceHeader({ name, state, present, missingApplies, stale = false }: { name: string; state: InventorySourceState; present: boolean; missingApplies: boolean; stale?: boolean }) {
  const unavailable = state.availability !== 'AVAILABLE';
  const presentation = unavailable ? inventorySourceStatus(state.availability) : null;
  return <div className="mb-3 flex flex-wrap items-center gap-2"><h3 className="font-semibold text-slate-200">{name}</h3>
    {presentation
      ? <ClientSemanticStatus {...presentation} />
      : stale
        ? <ClientSemanticStatus status={sourceFreshnessStatus(true, null)} />
        : <ClientSemanticStatus status={sourcePresenceStatus(present, missingApplies)} />}
  </div>;
}

function SourceError({ state }: { state: InventorySourceState }) {
  return state.error ? <p className="mb-3 text-sm text-slate-400">{state.error}</p> : null;
}

function DeviceOverview({ device, overview }: { device: HygieneDevice; overview: ClientOverviewResult }) {
  const environment = useEnvironment();
  const sources = environment.result!.sources;
  const nessus = device.nessus ?? { exists: false, assetId: null, ipAddress: null, lastCompletedScanUtc: null, critical: 0, high: 0, medium: 0, low: 0, info: 0, ports: [], scanSources: [] };
  const nessusSource = sources.nessus ?? { availability: 'NOT_CONNECTED' as const, error: 'Nessus is not configured.' };
  const hasFinding = (...codes: string[]) => device.assessment.findings.some((finding) => codes.includes(finding.code));
  return <div className="flex flex-col gap-4">
    <DeviceRelationshipMap
      device={device}
      sources={{ ...sources, nessus: nessusSource }}
      overview={overview}
      managementObservedAtUtc={environment.result!.assessedAtUtc}
    />
    <Card title="Environment assessment">
      <div className="mb-3"><ClientSemanticStatus {...hygieneAssessmentStatus(device.assessment.status)} /></div>
      {device.assessment.findings.length ? <ul className="space-y-2">{device.assessment.findings.map((finding) => <li key={finding.code} className="rounded border border-slate-800 px-3 py-2 text-sm text-slate-300">
        <span className={finding.severity === 'CRITICAL' ? 'text-fail-300' : 'text-warn-300'}>{finding.code.replaceAll('_', ' ')}</span> — {finding.message}
      </li>)}</ul> : <p className="text-sm text-slate-400">No hygiene findings from the available sources.</p>}
    </Card>
    <div className="grid gap-4 xl:grid-cols-2">
      <Card title="Active Directory"><SourceHeader name="Active Directory" state={sources.activeDirectory} present={device.activeDirectory.exists} missingApplies={hasFinding('ORPHAN_KASPERSKY', 'ORPHAN_OPSI')} stale={hasFinding('STALE_AD')} /><SourceError state={sources.activeDirectory} />
        <Rows entries={[["Enabled", value(device.activeDirectory.enabled)], ["DNS host", value(device.activeDirectory.dnsHostName)], ["Operating system", value(device.activeDirectory.operatingSystem)], ["Description", value(device.activeDirectory.description)], ["OU", value(device.activeDirectory.organizationalUnit)], ["Last logon", date(device.activeDirectory.lastLogonDate)]]} /></Card>
      <Card title="Kaspersky"><SourceHeader name="Kaspersky" state={sources.kaspersky} present={device.kaspersky.exists && !hasFinding('MISSING_KASPERSKY_AGENT', 'MISSING_KES')} missingApplies={hasFinding('MISSING_KASPERSKY', 'MISSING_KASPERSKY_AGENT', 'MISSING_KES')} stale={hasFinding('STALE_KASPERSKY')} /><SourceError state={sources.kaspersky} />
        <Rows entries={[["Last seen", date(device.kaspersky.lastSeen)], ["Network Agent", value(device.kaspersky.agentVersion)], ["KES", value(device.kaspersky.kesVersion)], ["Group", value(device.kaspersky.administrationGroup)]]} /></Card>
      <Card title="opsi"><SourceHeader name="opsi" state={sources.opsi} present={device.opsi.exists} missingApplies={hasFinding('MISSING_OPSI')} stale={hasFinding('STALE_OPSI')} /><SourceError state={sources.opsi} />
        <Rows entries={[["Client ID", value(device.opsi.clientId)], ["Description", value(device.opsi.description)], ["Depot", value(device.opsi.depotId)], ["Last seen", date(device.opsi.lastSeen)], ["Client Agent", value(device.opsi.clientAgentVersion)]]} /></Card>
      <Card title="Nessus"><SourceHeader name="Nessus" state={nessusSource} present={nessus.exists && nessus.lastCompletedScanUtc !== null} missingApplies={hasFinding('MISSING_NESSUS')} stale={hasFinding('STALE_NESSUS')} /><SourceError state={nessusSource} />
        <Rows entries={[["Last completed scan", date(nessus.lastCompletedScanUtc)], ["Critical", String(nessus.critical)], ["High", String(nessus.high)], ["Medium", String(nessus.medium)], ["Low", String(nessus.low)], ["Ports", nessus.ports.join(', ') || '—'], ["Scans", nessus.scanSources.join(', ') || '—']]} />
        {nessus.exists && <a className="mt-3 inline-block text-sm text-accent-400 hover:text-accent-300" href={`#/vulnerabilities?tab=findings&asset=${encodeURIComponent(device.computerName)}`}>Open findings in Vulnerabilities</a>}</Card>
    </div>
  </div>;
}

function ManagementContext({ host, overview }: { host: string; overview: ClientOverviewResult }) {
  const environment = useEnvironment();
  if (environment.loading && !environment.result) return <HygieneLoadStatus progress={environment.progress} elapsedSeconds={environment.elapsedSeconds} onCancel={environment.cancel} />;
  if (environment.cancelled && !environment.result) return <Card title="Management systems"><div className="flex flex-wrap items-center justify-between gap-3"><p className="text-sm text-slate-400">Management-source loading was cancelled.</p><Button onClick={() => { void environment.refresh(); }}>Retry</Button></div></Card>;
  if (environment.error && !environment.result) return <ErrorState
    title="Management systems unavailable"
    {...environment.error}
    message="AD, Kaspersky, opsi and Nessus context could not be loaded."
    controls={<Button onClick={() => { void environment.refresh(); }}>Retry management sources</Button>}
  />;
  if (!environment.result) return <Card title="Management systems">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div><p className="text-sm font-medium text-slate-200">AD, Kaspersky, opsi and Nessus are not loaded</p><p className="mt-1 text-xs text-slate-400">Load these read-only sources only when their current posture is needed.</p></div>
      <Button onClick={() => { void environment.ensureLoaded(); }}>Load management sources</Button>
    </div>
  </Card>;
  const device = environment.result.devices.find((entry) => clientKey(entry.hostName) === clientKey(host) || clientKey(entry.computerName) === clientKey(host));
  return device ? <DeviceOverview device={device} overview={overview} /> : <Card title="Management systems"><p className="text-sm text-slate-400">The loaded management sources contain no matching device.</p></Card>;
}

export function OverviewSection({ host }: { host: string }) {
  const [state, setState] = useState<OverviewLoadState>({ kind: 'loading' });
  const requestGeneration = useRef(0);

  const loadOverview = useCallback((refresh: boolean) => {
    const generation = ++requestGeneration.current;
    setState((current) => current.kind === 'ready'
      ? { ...current, refreshing: true, refreshError: null }
      : { kind: 'loading' });
    void invoke<ClientOverviewResult>('clients', 'getOverview', { host })
      .then((result) => {
        if (requestGeneration.current === generation) setState({ kind: 'ready', result, refreshing: false, refreshError: null });
      })
      .catch((caught: unknown) => {
        if (requestGeneration.current !== generation) return;
        const error = presentError(caught, { message: 'The stored client overview could not be loaded.' });
        setState((current) => refresh && current.kind === 'ready'
          ? { ...current, refreshing: false, refreshError: error }
          : { kind: 'error', error });
      });
  }, [host]);

  useEffect(() => {
    loadOverview(false);
    return () => { requestGeneration.current += 1; };
  }, [loadOverview]);

  if (state.kind === 'loading') return <p className="py-6 text-sm text-slate-400" role="status">Loading stored client evidence …</p>;
  if (state.kind === 'error') return <ErrorState {...state.error} controls={<Button onClick={() => loadOverview(false)}>Retry overview</Button>} />;

  return <div className="flex flex-col gap-4">
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-slate-800 bg-slate-900/40 px-4 py-3">
      <div><p className="text-sm font-medium text-slate-200">Client 360 evidence snapshot</p><p className="mt-0.5 text-xs text-slate-400">Stored data is read-only and never refreshed remotely on open.</p></div>
      <Button disabled={state.refreshing} onClick={() => loadOverview(true)}>{state.refreshing ? 'Refreshing …' : 'Refresh stored summaries'}</Button>
    </div>
    {state.refreshError && <CompactErrorState {...state.refreshError} />}
    <StoredClientEvidence host={host} result={state.result} />
    <ManagementContext host={host} overview={state.result} />
  </div>;
}

export function StoredClientEvidence({ host, result }: { host: string; result: ClientOverviewResult }) {
  const inventoryMetadata = result.sources.find((source) => source.source === 'Inventory')!;
  const softwareMetadata = result.sources.find((source) => source.source === 'Installed software')!;
  const healthMetadata = result.sources.find((source) => source.source === 'Health')!;
  const securityMetadata = result.sources.find((source) => source.source === 'Security')!;
  const usersMetadata = result.sources.find((source) => source.source === 'Linked users')!;
  return <div className="flex flex-col gap-4">
    <SourceLedger host={host} sources={result.sources} />
    <div className="grid gap-4 xl:grid-cols-2">
      <InventorySummary host={host} inventory={result.inventory} metadata={inventoryMetadata} />
      <HealthSummary host={host} health={result.health} metadata={healthMetadata} />
      <SoftwareSummary host={host} software={result.software} metadata={softwareMetadata} />
      <SecuritySummary host={host} security={result.security} metadata={securityMetadata} />
      <UserSummary host={host} users={result.users} metadata={usersMetadata} />
    </div>
  </div>;
}
