import type { DeviceCleanupAssessment, HostProbe } from '../../shared/api-types';

export type DeviceCleanupDecision = 'KEEP' | 'RECHECK' | 'PREPARE_CLEANUP' | 'EXCLUDE';

const decisionLabel: Record<DeviceCleanupDecision, string> = {
  KEEP: 'Keep device',
  RECHECK: 'Recheck evidence',
  PREPARE_CLEANUP: 'Prepare controlled cleanup',
  EXCLUDE: 'Exclude from cleanup review',
};

function line(value: string | null | undefined): string {
  return value?.replace(/[\r\n]+/g, ' ').trim() || 'Not available';
}

function timestamp(value: string | null): string {
  return value || 'Not available';
}

function connectivity(probe: HostProbe | null): string {
  if (!probe) return 'Not checked in this session';
  if (probe.reachable && probe.manageable) return 'Ping responded; WinRM 5985 responded';
  if (probe.reachable) return 'Ping responded; WinRM 5985 did not respond';
  if (probe.manageable) return 'Ping did not respond; WinRM 5985 responded';
  return 'No Ping or WinRM response; this is not proof that the device is retired';
}

export function toDeviceCleanupMarkdown(
  assessment: DeviceCleanupAssessment,
  reviewedSources: ReadonlySet<string>,
  decision: DeviceCleanupDecision,
  reason: string,
  probe: HostProbe | null,
  exportedAtUtc: string,
): string {
  const output = [
    '# WEC Device Cleanup Assessment',
    '',
    '> Read-only evidence snapshot. This file does not disable, move or delete a directory object.',
    '',
    `- Device: ${line(assessment.candidate.host)}`,
    `- Description: ${line(assessment.candidate.description)}`,
    `- Description source: ${line(assessment.candidate.descriptionSource)}`,
    `- Classification: ${assessment.candidate.classification}`,
    `- Classification basis: ${line(assessment.candidate.classificationExplanation)}`,
    `- Manual decision: ${decisionLabel[decision]}`,
    `- Required reason: ${line(reason)}`,
    `- Exported UTC: ${line(exportedAtUtc)}`,
    '',
    '## Source checklist',
    '',
  ];

  for (const source of assessment.sources) {
    output.push(
      `- [${reviewedSources.has(source.source) ? 'x' : ' '}] ${line(source.source)} — ${line(source.state)}`,
      `  - Coverage: ${source.coverage}`,
      `  - Observed UTC: ${timestamp(source.observedAtUtc)}`,
      `  - Evidence: ${line(source.explanation)}`,
    );
  }

  output.push('', '## Connectivity', '', `- ${connectivity(probe)}`);
  output.push('', '## User/device observations', '');
  if (assessment.userObservations.length === 0) {
    output.push(`- ${line(assessment.userEvidenceExplanation)}`);
  } else {
    for (const observation of assessment.userObservations) {
      output.push(
        `- ${line(observation.accountDisplay ?? observation.sid)} — ${line(observation.relationshipType)}`,
        `  - Confidence: ${line(observation.confidence)}`,
        `  - Observed UTC: ${timestamp(observation.observedAtUtc)}`,
        `  - Profile last use UTC: ${timestamp(observation.profileLastUseAtUtc)}`,
        `  - Evidence: ${line(observation.explanation)}`,
      );
    }
  }

  output.push('', '## Findings', '');
  if (assessment.findings.length === 0) {
    output.push('- No hygiene finding was returned for this subject.');
  } else {
    for (const finding of assessment.findings) {
      output.push(`- ${line(finding.code)} (${line(finding.severity)}): ${line(finding.message)}`);
    }
  }

  output.push(
    '',
    '## Evidence boundaries',
    '',
    '- AD last logon evidence is replicated and can be stale.',
    '- Missing Ping and WinRM responses do not prove that a device is retired.',
    '- Inventory user observations do not prove ownership or assignment.',
    '- A recent observation does not silently invalidate another source fact.',
    '- Checklist marks, connectivity and the manual decision exist only in this review session.',
    '',
  );
  return output.join('\n');
}
