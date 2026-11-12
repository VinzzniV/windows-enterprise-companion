import type { UserProfileResult } from '../../shared/api-types';
import type { LeaverAssessment } from './leaverReview';

function line(value: string | null | undefined): string {
  return value?.replace(/[\r\n]+/g, ' ').trim() || 'Not available';
}

function checklistMark(reviewedItemIds: ReadonlySet<string>, id: string): string {
  return reviewedItemIds.has(id) ? 'x' : ' ';
}

export function toLeaverReviewMarkdown(
  profile: UserProfileResult,
  assessment: LeaverAssessment,
  reviewedItemIds: ReadonlySet<string>,
  exportedAtUtc: string,
): string {
  const output = [
    '# WEC Leaver Review',
    '',
    '> Read-only evidence snapshot. This file does not prove that offboarding actions were completed.',
    '',
    `- User: ${line(profile.identity.displayName)}`,
    `- Account: ${line(profile.identity.samAccountName ?? profile.identity.userPrincipalName)}`,
    `- Object GUID: ${line(profile.identity.objectId)}`,
    `- Exported UTC: ${line(exportedAtUtc)}`,
    '',
    '## Evidence checklist',
    '',
  ];

  for (const item of assessment.items) {
    output.push(
      `- [${checklistMark(reviewedItemIds, item.id)}] ${line(item.title)} — ${item.state}`,
      `  - Summary: ${line(item.summary)}`,
      `  - Evidence: ${line(item.evidence)}`,
      `  - Source: ${line(item.source)}`,
    );
  }

  output.push('', '## Direct access', '');
  if (profile.access.directGroups.length === 0) {
    output.push('- No direct group memberships were returned.');
  } else {
    for (const group of profile.access.directGroups) {
      output.push(`- ${line(group.name)} — ${line(group.distinguishedName)}`);
    }
  }

  output.push('', '## Linked-device return evidence', '');
  if (assessment.devices.length === 0) {
    output.push('- No exact SID-matched device observation is available. Assignment and return remain unknown.');
  } else {
    for (const device of assessment.devices) {
      output.push(
        `- [ ] ${line(device.host)} — RETURN UNRESOLVED`,
        `  - Relationship: ${line(device.relationship)} (${device.confidence} confidence)`,
        `  - Observed UTC: ${line(device.observedAtUtc)}`,
        `  - Evidence: ${line(device.explanation)}`,
      );
    }
  }

  output.push(
    '',
    '## Evidence boundaries',
    '',
    '- AD lastLogonTimestamp is replicated and can be stale.',
    '- This assessment contains AD and stored Windows evidence only. Microsoft 365 accounts, licenses, groups and device relationships are excluded.',
    '- Direct groups do not include all nested or external authorization.',
    '- Inventory user observations do not prove ownership, assignment or physical return.',
    '- Checked items represent only this review session; WEC persists no workflow state.',
    '',
  );
  return output.join('\n');
}
