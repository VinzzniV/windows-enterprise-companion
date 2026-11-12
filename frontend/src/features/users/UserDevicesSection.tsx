import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type {
  UserDeviceEvidenceCoverage,
  UserDeviceRelationshipObservation,
  UserLinkedDeviceProfile,
  UserProfileResult,
} from '../../shared/api-types';
import { RelationshipMap } from '../../shared/relationships/RelationshipMap';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { EmptyState } from '../../shared/ui/States';
import { buildUserRelationshipModel } from './userRelationships';
import { formatDirectoryTimestamp } from './users';

const coveragePresentation: Record<UserDeviceEvidenceCoverage, { label: string; tone: BadgeTone }> = {
  AVAILABLE: { label: 'Evidence evaluated', tone: 'ok' },
  PARTIAL: { label: 'Partial evidence', tone: 'warn' },
  NOT_CAPTURED: { label: 'Not captured', tone: 'neutral' },
  NOT_EVALUATED: { label: 'Not evaluated', tone: 'neutral' },
};

const relationshipLabels = {
  LAST_INTERACTIVE_USER: 'Last interactive user',
  PROFILE_PRESENT: 'Profile present',
} as const;

function SourceState({ available, complete }: { available: boolean; complete: boolean }) {
  if (!available) return <Badge tone="neutral">No stored data</Badge>;
  return complete ? <Badge tone="ok">Complete</Badge> : <Badge tone="warn">Partial</Badge>;
}

function SourcePanel({ title, state, children }: {
  title: string;
  state: ReactNode;
  children: ReactNode;
}) {
  return <section className="rounded border border-slate-800 bg-slate-950/35 p-3">
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">{title}</h4>
      {state}
    </div>
    <div className="mt-2 text-sm text-slate-300">{children}</div>
  </section>;
}

function EvidenceList({ evidence }: { evidence: readonly UserDeviceRelationshipObservation[] }) {
  return <ul className="mt-3 grid gap-2" aria-label="Relationship evidence">
    {evidence.map((observation) => <li
      key={`${observation.relationshipType}:${observation.observedAtUtc}`}
      className="border-l border-accent-700/70 pl-3 text-xs text-slate-400"
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium text-accent-300">{relationshipLabels[observation.relationshipType]}</span>
        <Badge tone={observation.confidence === 'HIGH' ? 'ok' : 'warn'}>{observation.confidence.toLocaleLowerCase()} confidence</Badge>
      </div>
      <p className="mt-1">{observation.source} · observed {formatDirectoryTimestamp(observation.observedAtUtc)}</p>
      <p className="mt-0.5 text-slate-500">{observation.explanation}</p>
      {observation.profileLastUseAtUtc && <p className="mt-0.5 text-slate-500">
        Profile last-use evidence: {formatDirectoryTimestamp(observation.profileLastUseAtUtc)}
      </p>}
    </li>)}
  </ul>;
}

function LinkedDeviceContext({ device }: { device: UserLinkedDeviceProfile }) {
  const clientPath = `/clients/${encodeURIComponent(device.host)}`;
  const vulnerabilitiesAvailable = device.vulnerabilities.availability === 'AVAILABLE'
    || device.vulnerabilities.availability === 'PARTIAL';
  return <article className="rounded-lg border border-slate-800 bg-slate-900/70 p-4">
    <header className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-800 pb-3">
      <div>
        <h3 className="font-semibold text-slate-100">
          <Link className="hover:text-accent-300" to={clientPath}>{device.host}</Link>
        </h3>
        <p className="mt-1 text-xs text-muted">Inventory evidence captured {formatDirectoryTimestamp(device.inventoryCapturedAtUtc)}</p>
      </div>
      <Badge tone="accent">Observed relationship</Badge>
    </header>

    <EvidenceList evidence={device.relationshipEvidence} />

    <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
      <SourcePanel title="Installed software" state={<SourceState available={device.software.isAvailable} complete={device.software.isComplete} />}>
        {device.software.isAvailable
          ? <><p><span className="font-semibold text-slate-100">{device.software.installedCount}</span> applications</p>
            <p className="mt-1 text-xs text-muted">Captured {formatDirectoryTimestamp(device.software.capturedAtUtc)}</p></>
          : <p className="text-xs text-muted">{device.software.explanation}</p>}
        {device.software.isAvailable && !device.software.isComplete && <p className="mt-1 text-xs text-warn-300">{device.software.explanation}</p>}
        {device.software.isAvailable && device.software.sample.length > 0 && <div className="mt-2">
          <DetailsDisclosure summary={`Show ${device.software.sample.length} sample entries`}>
            <ul className="space-y-1 text-xs text-slate-400">
              {device.software.sample.map((entry) => <li key={`${entry.name}:${entry.version ?? ''}`}>
                <span className="text-slate-200">{entry.name}</span>{entry.version ? ` · ${entry.version}` : ''}
              </li>)}
            </ul>
          </DetailsDisclosure>
        </div>}
      </SourcePanel>

      <SourcePanel title="Health" state={<SourceState available={device.health.isAvailable} complete={device.health.isComplete} />}>
        {device.health.isAvailable
          ? <><p>{device.health.criticalCount} critical · {device.health.warningCount} warning</p>
            <p className="mt-1 text-xs text-muted">{device.health.healthyCount} passed · {device.health.unknownCount} not run</p>
            <p className="mt-1 text-xs text-muted">Captured {formatDirectoryTimestamp(device.health.capturedAtUtc)}</p></>
          : <p className="text-xs text-muted">{device.health.explanation}</p>}
        {device.health.isAvailable && !device.health.isComplete && <p className="mt-1 text-xs text-warn-300">{device.health.explanation}</p>}
      </SourcePanel>

      <SourcePanel title="Security" state={<SourceState available={device.security.isAvailable} complete={device.security.isComplete} />}>
        {device.security.isAvailable
          ? <><p>{device.security.criticalCount} critical · {device.security.highCount} high</p>
            <p className="mt-1 text-xs text-muted">{device.security.mediumCount} medium · {device.security.lowCount} low</p>
            <p className="mt-1 text-xs text-muted">Captured {formatDirectoryTimestamp(device.security.capturedAtUtc)}</p></>
          : <p className="text-xs text-muted">{device.security.explanation}</p>}
        {device.security.isAvailable && !device.security.isComplete && <p className="mt-1 text-xs text-warn-300">{device.security.explanation}</p>}
      </SourcePanel>

      <SourcePanel title="Nessus" state={<Badge tone={device.vulnerabilities.deviceMatched ? 'ok' : vulnerabilitiesAvailable ? 'warn' : 'neutral'}>
        {device.vulnerabilities.deviceMatched ? 'Device matched' : 'No device match'}
      </Badge>}>
        {device.vulnerabilities.deviceMatched
          ? <><p>{device.vulnerabilities.criticalCount} critical · {device.vulnerabilities.highCount} high</p>
            <p className="mt-1 text-xs text-muted">{device.vulnerabilities.mediumCount} medium · {device.vulnerabilities.lowCount} low</p>
            <p className="mt-1 text-xs text-muted">Captured {formatDirectoryTimestamp(device.vulnerabilities.capturedAtUtc)}</p></>
          : <p className="text-xs text-muted">{device.vulnerabilities.explanation}</p>}
        {device.vulnerabilities.deviceMatched && device.vulnerabilities.availability !== 'AVAILABLE'
          && <p className="mt-1 text-xs text-warn-300">{device.vulnerabilities.explanation}</p>}
      </SourcePanel>
    </div>

    <nav className="mt-4 flex flex-wrap gap-x-4 gap-y-2 border-t border-slate-800 pt-3 text-sm" aria-label={`${device.host} details`}>
      <Link className="text-accent-300 hover:text-accent-200" to={clientPath}>Client 360</Link>
      <Link className="text-accent-300 hover:text-accent-200" to={`${clientPath}?section=inventory`}>Software & inventory</Link>
      <Link className="text-accent-300 hover:text-accent-200" to={`${clientPath}?section=diagnostics`}>Health</Link>
      <Link className="text-accent-300 hover:text-accent-200" to={`${clientPath}?section=security`}>Security</Link>
      <Link className="text-accent-300 hover:text-accent-200" to={`/vulnerabilities?tab=findings&asset=${encodeURIComponent(device.host)}`}>Vulnerabilities</Link>
    </nav>
  </article>;
}

export function UserDevicesSection({ profile }: { profile: UserProfileResult }) {
  const { devices } = profile;
  const coverage = coveragePresentation[devices.coverage];
  return <div className="flex flex-col gap-4">
    <section className="rounded-lg border border-slate-800 bg-slate-900/70 p-4" aria-labelledby="user-device-coverage">
      <div className="flex flex-wrap items-center gap-2">
        <h2 id="user-device-coverage" className="font-semibold text-slate-100">Device evidence coverage</h2>
        <Badge tone={coverage.tone}>{coverage.label}</Badge>
      </div>
      <p className="mt-2 text-sm text-slate-300">{devices.explanation}</p>
      <dl className="mt-3 grid grid-cols-2 gap-3 text-xs sm:grid-cols-3 lg:grid-cols-5">
        <div><dt className="text-muted">Stored devices</dt><dd className="mt-0.5 font-mono text-slate-200">{devices.sourceCoverage.storedDeviceCount}</dd></div>
        <div><dt className="text-muted">Evaluated devices</dt><dd className="mt-0.5 font-mono text-slate-200">{devices.sourceCoverage.evaluatedDeviceCount ?? 'Unknown'}</dd></div>
        <div><dt className="text-muted">Evidence captured</dt><dd className="mt-0.5 font-mono text-slate-200">{devices.sourceCoverage.evidenceCapturedDeviceCount}</dd></div>
        <div><dt className="text-muted">Older snapshots</dt><dd className="mt-0.5 font-mono text-slate-200">{devices.sourceCoverage.notCapturedDeviceCount}</dd></div>
        <div><dt className="text-muted">Unavailable</dt><dd className="mt-0.5 font-mono text-slate-200">{devices.sourceCoverage.unavailableDeviceCount}</dd></div>
        <div><dt className="text-muted">Source truncated</dt><dd className="mt-0.5 font-mono text-slate-200">{devices.sourceCoverage.truncatedDeviceCount}</dd></div>
      </dl>
      {devices.sourceCoverage.workingSetTruncated && <p className="mt-2 text-xs text-warn-300">The stored-record limit was reached. Counts and relationships describe the evaluated working set.</p>}
      {devices.sourceCoverage.multipleLatestSnapshotDeviceCount > 0 && <p className="mt-2 text-xs text-warn-300">{devices.sourceCoverage.multipleLatestSnapshotDeviceCount} evaluated devices have multiple equally recent snapshots; all matching observations are retained.</p>}
      <p className="mt-3 text-xs text-muted">Stored evidence only. Opening this view does not run Inventory, Health, Security or Nessus scans.</p>
    </section>

    {devices.linkedDevices.length === 0
      ? <EmptyState title="No linked devices" message="No exact SID-matched Inventory observation is available for this user. WEC does not infer device ownership from names, e-mail addresses or host naming conventions." />
      : <>
        {devices.linkedDevicesTruncated && <p className="rounded border border-warn-800 bg-warn-950/25 px-3 py-2 text-sm text-warn-300" role="status">
          Showing {devices.linkedDevices.length} of {devices.totalLinkedDeviceCount} linked devices. Interactive-user evidence is prioritized, then the newest Inventory evidence.
        </p>}
        <RelationshipMap model={buildUserRelationshipModel(profile)} />
        <section aria-labelledby="linked-device-context" className="flex flex-col gap-3">
          <div>
            <h2 id="linked-device-context" className="font-semibold text-slate-100">Linked-device context</h2>
            <p className="mt-1 text-xs text-muted">Software remains device-scoped. These summaries describe linked endpoints, not user-owned applications.</p>
          </div>
          {devices.linkedDevices.map((device) => <LinkedDeviceContext key={device.host.toLocaleLowerCase()} device={device} />)}
        </section>
      </>}
  </div>;
}
