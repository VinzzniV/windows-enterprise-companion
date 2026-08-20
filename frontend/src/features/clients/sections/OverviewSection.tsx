import { useEffect } from 'react';
import type { HygieneDevice, InventorySourceState } from '../../../shared/api-types';
import { useEnvironment } from '../../../shared/environment/EnvironmentContext';
import { HygieneLoadStatus } from '../../../shared/environment/HygieneLoadStatus';
import { inventorySourceStatus } from '../../../shared/environment/inventorySourceStatus';
import { Button } from '../../../shared/ui/Button';
import { Card } from '../../../shared/ui/Card';
import { EmptyState, ErrorState } from '../../../shared/ui/States';
import { clientKey } from '../clients';
import { ClientSemanticStatus, hygieneAssessmentStatus, sourcePresenceStatus } from '../clientStatus';
import { ClientIntegrationMap } from '../ClientIntegrationMap';

function value(value: string | boolean | null | undefined): string {
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return value == null || value === '' ? '—' : String(value);
}

function date(valueToFormat: string | null): string {
  return valueToFormat ? new Date(valueToFormat).toLocaleString() : '—';
}

function Rows({ entries }: { entries: Array<[string, string]> }) {
  return <dl className="grid grid-cols-[auto_1fr] gap-x-5 gap-y-1.5 text-sm">
    {entries.map(([label, entryValue]) => <div key={label} className="contents"><dt className="text-slate-400">{label}</dt><dd className="break-all text-slate-200">{entryValue}</dd></div>)}
  </dl>;
}

function SourceHeader({ name, state, present, missingApplies }: { name: string; state: InventorySourceState; present: boolean; missingApplies: boolean }) {
  const unavailable = state.availability !== 'AVAILABLE';
  const presentation = unavailable ? inventorySourceStatus(state.availability) : null;
  return <div className="mb-3 flex flex-wrap items-center gap-2"><h3 className="font-semibold text-slate-200">{name}</h3>
    {presentation
      ? <ClientSemanticStatus {...presentation} />
      : <ClientSemanticStatus status={sourcePresenceStatus(present, missingApplies)} />}
  </div>;
}

function SourceError({ state }: { state: InventorySourceState }) {
  return state.error ? <p className="mb-3 text-sm text-slate-400">{state.error}</p> : null;
}

function DeviceOverview({ device }: { device: HygieneDevice }) {
  const environment = useEnvironment();
  const sources = environment.result!.sources;
  const nessus = device.nessus ?? { exists: false, assetId: null, ipAddress: null, lastCompletedScanUtc: null, critical: 0, high: 0, medium: 0, low: 0, info: 0, ports: [], scanSources: [] };
  const nessusSource = sources.nessus ?? { availability: 'NOT_CONNECTED' as const, error: 'Nessus is not configured.' };
  const hasFinding = (...codes: string[]) => device.assessment.findings.some((finding) => codes.includes(finding.code));
  return <div className="flex flex-col gap-4">
    <ClientIntegrationMap device={device} sources={{ ...sources, nessus: nessusSource }} />
    <Card title="Environment assessment">
      <div className="mb-3"><ClientSemanticStatus {...hygieneAssessmentStatus(device.assessment.status)} /></div>
      {device.assessment.findings.length ? <ul className="space-y-2">{device.assessment.findings.map((finding) => <li key={finding.code} className="rounded border border-slate-800 px-3 py-2 text-sm text-slate-300">
        <span className={finding.severity === 'CRITICAL' ? 'text-danger-300' : 'text-warn-300'}>{finding.code.replaceAll('_', ' ')}</span> — {finding.message}
      </li>)}</ul> : <p className="text-sm text-slate-400">No hygiene findings from the available sources.</p>}
    </Card>
    <div className="grid gap-4 xl:grid-cols-2">
      <Card title="Active Directory"><SourceHeader name="Active Directory" state={sources.activeDirectory} present={device.activeDirectory.exists} missingApplies={hasFinding('ORPHAN_KASPERSKY', 'ORPHAN_OPSI')} /><SourceError state={sources.activeDirectory} />
        <Rows entries={[["Enabled", value(device.activeDirectory.enabled)], ["DNS host", value(device.activeDirectory.dnsHostName)], ["Operating system", value(device.activeDirectory.operatingSystem)], ["Description", value(device.activeDirectory.description)], ["OU", value(device.activeDirectory.organizationalUnit)], ["Last logon", date(device.activeDirectory.lastLogonDate)]]} /></Card>
      <Card title="Kaspersky"><SourceHeader name="Kaspersky" state={sources.kaspersky} present={device.kaspersky.exists} missingApplies={hasFinding('MISSING_KASPERSKY')} /><SourceError state={sources.kaspersky} />
        <Rows entries={[["Last seen", date(device.kaspersky.lastSeen)], ["Network Agent", value(device.kaspersky.agentVersion)], ["KES", value(device.kaspersky.kesVersion)], ["Group", value(device.kaspersky.administrationGroup)]]} /></Card>
      <Card title="opsi"><SourceHeader name="opsi" state={sources.opsi} present={device.opsi.exists} missingApplies={hasFinding('MISSING_OPSI')} /><SourceError state={sources.opsi} />
        <Rows entries={[["Client ID", value(device.opsi.clientId)], ["Description", value(device.opsi.description)], ["Depot", value(device.opsi.depotId)], ["Last seen", date(device.opsi.lastSeen)], ["Client Agent", value(device.opsi.clientAgentVersion)]]} /></Card>
      <Card title="Nessus"><SourceHeader name="Nessus" state={nessusSource} present={nessus.exists} missingApplies={hasFinding('MISSING_NESSUS')} /><SourceError state={nessusSource} />
        <Rows entries={[["Last completed scan", date(nessus.lastCompletedScanUtc)], ["Critical", String(nessus.critical)], ["High", String(nessus.high)], ["Medium", String(nessus.medium)], ["Low", String(nessus.low)], ["Ports", nessus.ports.join(', ') || '—'], ["Scans", nessus.scanSources.join(', ') || '—']]} />
        {nessus.exists && <a className="mt-3 inline-block text-sm text-accent-400 hover:text-accent-300" href={`#/vulnerabilities?tab=findings&asset=${encodeURIComponent(device.computerName)}`}>Open findings in Vulnerabilities</a>}</Card>
    </div>
  </div>;
}

export function OverviewSection({ host }: { host: string }) {
  const environment = useEnvironment();
  useEffect(() => { void environment.ensureLoaded(); }, [environment.ensureLoaded]);
  if (environment.loading && !environment.result) return <HygieneLoadStatus progress={environment.progress} elapsedSeconds={environment.elapsedSeconds} onCancel={environment.cancel} />;
  if (environment.cancelled && !environment.result) return <div className="flex items-center gap-3 rounded-lg border border-slate-800 p-4"><p className="text-sm text-slate-300">Environment load cancelled.</p><Button variant="secondary" onClick={() => { void environment.refresh(); }}>Retry</Button></div>;
  if (environment.error && !environment.result) return <ErrorState
    title="Environment overview failed"
    {...environment.error}
    message="The environment overview could not be loaded."
    controls={<Button variant="secondary" onClick={() => { void environment.refresh(); }}>Retry environment load</Button>}
  />;
  const device = environment.result?.devices.find((entry) => clientKey(entry.hostName) === clientKey(host) || clientKey(entry.computerName) === clientKey(host));
  return device ? <DeviceOverview device={device} /> : <EmptyState title="Unmanaged device" message="This saved or scanned device was not found in AD, Kaspersky, opsi or Nessus." />;
}
