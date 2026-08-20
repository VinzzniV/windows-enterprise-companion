import { useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import type { AuditLogResult, PatchAuditEntry } from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import { ErrorState } from '../../shared/ui/States';
import { formatClientName } from './PatchClientFleetCard';
import { patchAuditResultStatus } from './patchStatus';

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

function AuditResultBadge({ result }: { result: string }) {
  const presentation = patchAuditResultStatus(result);
  return (
    <span
      className="inline-flex flex-wrap items-center gap-1.5"
      title={presentation.technicalDetail ?? undefined}
    >
      <SemanticStatusBadge status={presentation.status} />
      {presentation.context && (
        <span className="text-xs text-slate-400">{presentation.context}</span>
      )}
    </span>
  );
}

export function PatchAuditHistoryCard() {
  const [entries, setEntries] = useState<PatchAuditEntry[]>([]);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let ignore = false;
    setError(null);
    invoke<AuditLogResult>('patchmanagement', 'getAuditLog', {})
      .then((result) => { if (!ignore) setEntries(result.entries); })
      .catch((requestError: unknown) => {
        if (!ignore) {
          setError(presentError(requestError, {
            message: 'The patch history could not be loaded.',
          }));
        }
      });
    return () => { ignore = true; };
  }, [revision]);

  return (
    <Card title="Package and deployment history">
      {error && (
        <div className="mb-3">
          <ErrorState
            title="History unavailable"
            {...error}
            controls={(
              <Button onClick={() => setRevision((value) => value + 1)}>
                Reload history
              </Button>
            )}
          />
        </div>
      )}
      {(entries.length > 0 || !error) && (
        <DataTable
          columns={[
            {
              header: 'Time',
              cell: (entry: PatchAuditEntry) => formatTimestamp(entry.timestampUtc),
            },
            { header: 'User', cell: (entry: PatchAuditEntry) => entry.userName },
            { header: 'Action', cell: (entry: PatchAuditEntry) => entry.action },
            { header: 'Package', cell: (entry: PatchAuditEntry) => entry.productId ?? '—' },
            { header: 'Depot', cell: (entry: PatchAuditEntry) => entry.depotId ?? '—' },
            {
              header: 'Version',
              cell: (entry: PatchAuditEntry) =>
                entry.oldVersion || entry.newVersion
                  ? `${entry.oldVersion ?? 'missing'} → ${entry.newVersion ?? 'unknown'}`
                  : '—',
            },
            {
              header: 'Targets',
              cell: (entry: PatchAuditEntry) =>
                entry.targetClients.length > 0
                  ? `${entry.targetClients.length}: ${entry.targetClients.map(formatClientName).join(', ')}`
                  : '—',
            },
            {
              header: 'Result',
              cell: (entry: PatchAuditEntry) => <AuditResultBadge result={entry.result} />,
            },
            {
              header: 'Error',
              cell: (entry: PatchAuditEntry) => entry.errorMessage ?? '—',
            },
          ]}
          rows={entries}
          emptyMessage="No Patch Management actions have been recorded yet."
        />
      )}
    </Card>
  );
}
