import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { ExportLeaverReviewResult, UserProfileResult } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Checkbox } from '../../shared/ui/Checkbox';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { buildLeaverAssessment, type LeaverEvidenceState } from './leaverReview';
import { toLeaverReviewMarkdown } from './leaverReviewExport';
import { formatDirectoryTimestamp } from './users';

const statePresentation: Record<LeaverEvidenceState, { label: string; tone: BadgeTone }> = {
  attention: { label: 'Needs review', tone: 'warn' },
  verified: { label: 'Evidence clear', tone: 'ok' },
  unknown: { label: 'Unknown', tone: 'neutral' },
  information: { label: 'Information', tone: 'info' },
};

export function LeaverReviewSection({ profile, subjectKey = profile.identity.objectId }: { profile: UserProfileResult; subjectKey?: string }) {
  const assessment = useMemo(() => buildLeaverAssessment(profile), [profile]);
  const [reviewedItemIds, setReviewedItemIds] = useState<ReadonlySet<string>>(new Set());
  const [exporting, setExporting] = useState(false);
  const [exportMessage, setExportMessage] = useState<string | null>(null);

  useEffect(() => {
    setReviewedItemIds(new Set());
    setExportMessage(null);
  }, [profile.identity.objectId, subjectKey]);

  const toggleReviewed = (itemId: string) => {
    setReviewedItemIds((current) => {
      const next = new Set(current);
      if (next.has(itemId)) next.delete(itemId);
      else next.add(itemId);
      return next;
    });
  };

  const exportChecklist = () => {
    setExporting(true);
    setExportMessage(null);
    const markdown = toLeaverReviewMarkdown(
      profile,
      assessment,
      reviewedItemIds,
      new Date().toISOString(),
    );
    void invoke<ExportLeaverReviewResult>('usermanagement', 'exportLeaverReview', { markdown })
      .then((result) => setExportMessage(result.cancelled
        ? 'Export cancelled.'
        : `Exported to ${result.filePath}`))
      .catch((caught: unknown) => setExportMessage(errorText(caught)))
      .finally(() => setExporting(false));
  };

  return <div className="flex flex-col gap-4">
    <section className="rounded-lg border border-warn-800/70 bg-warn-950/20 p-4" aria-labelledby="leaver-review-heading">
      <div className="flex flex-wrap items-center gap-2">
        <h2 id="leaver-review-heading" className="font-semibold text-slate-100">Read-only Leaver review</h2>
        <Badge tone="info">No write actions</Badge>
      </div>
      <p className="mt-2 text-sm text-slate-300">
        This view organizes current evidence for the deliberately selected directory user. It does not disable the account, remove groups, change devices or persist a workflow case.
      </p>
      <p className="mt-2 text-xs text-muted">This assessment and its export include AD and stored Windows evidence only. Microsoft 365 accounts, licenses, groups and device relationships are excluded from the assessment and export.</p>
      <div className="mt-3 flex flex-wrap items-center gap-3">
        <Button variant="primary" disabled={exporting} onClick={exportChecklist}>
          {exporting ? 'Exporting…' : 'Export Markdown checklist'}
        </Button>
        <span className="text-xs text-muted">{reviewedItemIds.size} of {assessment.items.length} evidence items reviewed in this session</span>
      </div>
      {exportMessage && <p className="mt-2 text-sm text-slate-300" role="status">{exportMessage}</p>}
      <dl className="mt-4 grid gap-3 text-sm sm:grid-cols-3">
        <div><dt className="text-xs uppercase tracking-wide text-muted">User</dt><dd className="mt-1 text-slate-100">{assessment.userDisplayName}</dd></div>
        <div><dt className="text-xs uppercase tracking-wide text-muted">Needs review</dt><dd className="mt-1 font-mono text-xl text-warn-300">{assessment.attentionCount}</dd></div>
        <div><dt className="text-xs uppercase tracking-wide text-muted">Unknown evidence</dt><dd className="mt-1 font-mono text-xl text-slate-300">{assessment.unknownCount}</dd></div>
      </dl>
    </section>

    <section aria-labelledby="leaver-evidence-heading">
      <div>
        <h2 id="leaver-evidence-heading" className="font-semibold text-slate-100">Review evidence</h2>
        <p className="mt-1 text-xs text-muted">Statuses describe the available evidence, not completion of an offboarding process.</p>
      </div>
      <ol className="mt-3 grid gap-3">
        {assessment.items.map((item) => {
          const state = statePresentation[item.state];
          return <li key={item.id} className="rounded-lg border border-slate-800 bg-slate-900/70 p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h3 className="font-medium text-slate-100">{item.title}</h3>
                <p className="mt-1 text-sm text-slate-300">{item.summary}</p>
              </div>
              <Badge tone={state.tone}>{state.label}</Badge>
            </div>
            <p className="mt-2 text-xs text-slate-500">{item.evidence}</p>
            <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-slate-800 pt-2 text-xs">
              <span className="text-muted">Source: {item.source}</span>
              <div className="flex flex-wrap items-center gap-3">
                <Checkbox
                  label="Reviewed in this session"
                  checked={reviewedItemIds.has(item.id)}
                  onChange={() => toggleReviewed(item.id)}
                />
                {item.href && <Link className="text-accent-300 hover:text-accent-200" to={item.href}>Inspect evidence →</Link>}
              </div>
            </div>
          </li>;
        })}
      </ol>
    </section>

    <section className="rounded-lg border border-slate-800 bg-slate-900/70 p-4" aria-labelledby="return-evidence-heading">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="return-evidence-heading" className="font-semibold text-slate-100">Device-return evidence</h2>
        <Badge tone={assessment.devices.length > 0 ? 'warn' : 'neutral'}>{assessment.devices.length} observed devices</Badge>
      </div>
      {assessment.devices.length === 0
        ? <p className="mt-3 text-sm text-slate-400">No exact SID-matched device observation is available. This does not prove that no device is assigned or outstanding.</p>
        : <ul className="mt-3 divide-y divide-slate-800 rounded border border-slate-800">
          {assessment.devices.map((device) => <li key={device.host.toLocaleLowerCase()} className="px-3 py-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <Link className="font-medium text-accent-300 hover:text-accent-200" to={device.href}>{device.host}</Link>
              <Badge tone="warn">Return unresolved</Badge>
            </div>
            <p className="mt-1 text-xs text-slate-400">{device.relationship} · {device.confidence} confidence · observed {formatDirectoryTimestamp(device.observedAtUtc)}</p>
            <p className="mt-1 text-xs text-slate-500">{device.explanation}</p>
          </li>)}
        </ul>}
      <div className="mt-3">
        <DetailsDisclosure summary="Evidence boundary">
          <p className="text-xs text-slate-400">WEC currently has no authoritative assignment, handover or physical-return source. Last interactive user and profile presence remain observations only.</p>
        </DetailsDisclosure>
      </div>
    </section>

    <section className="rounded-lg border border-slate-800 bg-slate-900/70 p-4" aria-labelledby="remaining-access-heading">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="remaining-access-heading" className="font-semibold text-slate-100">Remaining direct access</h2>
        <Link className="text-sm text-accent-300 hover:text-accent-200" to="?section=access">Open Access evidence →</Link>
      </div>
      {profile.access.directGroups.length === 0
        ? <p className="mt-3 text-sm text-slate-400">No direct groups were returned.</p>
        : <ul className="mt-3 grid gap-2 sm:grid-cols-2">
          {profile.access.directGroups.slice(0, 10).map((group) => <li key={group.distinguishedName} className="rounded border border-slate-800 px-3 py-2 text-sm text-slate-300">
            {group.name}
          </li>)}
        </ul>}
      {profile.access.directGroups.length > 10 && <p className="mt-2 text-xs text-warn-300">Showing 10 of {profile.access.directGroups.length} direct groups. Open Access evidence for the complete list.</p>}
    </section>
  </div>;
}
