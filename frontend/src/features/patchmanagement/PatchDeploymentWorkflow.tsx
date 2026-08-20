import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { RolloutPreview, RolloutRequestOutcome } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { DataTable } from '../../shared/ui/DataTable';
import { ErrorState } from '../../shared/ui/States';
import {
  PatchClientName as ClientName,
  PatchWorkflowBadge as WorkflowBadge,
} from './PatchClientFleetCard';
import { presentOpsiError } from './patchErrors';

const verifyRolloutAction =
  'First check the history and current opsi state to see whether the deployment request was already accepted. Repeat it only if no corresponding request is visible there.';

interface PatchDeploymentWorkflowProps {
  connected: boolean;
  productId: string;
  depotFilter: string;
  selectedClients: ReadonlySet<string>;
  onDashboardRefresh: () => void;
  children?: ReactNode;
}

/** Owns preview, confirmation and rollout request state for one selected package. */
export function PatchDeploymentWorkflow({
  connected,
  productId,
  depotFilter,
  selectedClients,
  onDashboardRefresh,
  children,
}: PatchDeploymentWorkflowProps) {
  const [preview, setPreview] = useState<RolloutPreview | null>(null);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewReviewed, setPreviewReviewed] = useState(false);
  const [rolloutBusy, setRolloutBusy] = useState(false);
  const [outcome, setOutcome] = useState<string | null>(null);
  const previewRequestId = useRef(0);
  const selectedClientIds = useMemo(() => [...selectedClients], [selectedClients]);
  const selectionKey = useMemo(
    () => [...selectedClients].sort((left, right) => left.localeCompare(right)).join('\u0000'),
    [selectedClients],
  );

  useEffect(() => {
    previewRequestId.current += 1;
    setPreview(null);
    setError(null);
    setPreviewLoading(false);
    setPreviewReviewed(false);
    setOutcome(null);
    return () => { previewRequestId.current += 1; };
  }, [connected, depotFilter, productId, selectionKey]);

  const loadPreview = () => {
    const requestId = ++previewRequestId.current;
    setError(null);
    setPreview(null);
    setPreviewLoading(true);
    setPreviewReviewed(false);
    setOutcome(null);
    invoke<RolloutPreview>('patchmanagement', 'getRolloutPreview', {
      productId,
      depotFilter: depotFilter.length > 0 ? depotFilter : null,
      clientIds: selectedClientIds.length > 0 ? selectedClientIds : null,
    })
      .then((result) => {
        if (requestId === previewRequestId.current) setPreview(result);
      })
      .catch((requestError: unknown) => {
        if (requestId === previewRequestId.current) {
          setError(presentOpsiError(requestError, 'The deployment preview could not be created.'));
        }
      })
      .finally(() => {
        if (requestId === previewRequestId.current) setPreviewLoading(false);
      });
  };

  const requestRollout = () => {
    if (preview === null) return;
    setError(null);
    setRolloutBusy(true);
    invoke<RolloutRequestOutcome>('patchmanagement', 'requestRollout', {
      productId: preview.productId,
      clientIds: preview.clients.map((client) => client.clientId),
      depotFilter: preview.depotFilter,
      confirmed: true,
    })
      .then((result) => {
        setOutcome(
          `Deployment requested for ${result.requestedClientCount} client(s) — opsi installs it during the next action check.`,
        );
        setPreview(null);
        setPreviewReviewed(false);
        onDashboardRefresh();
      })
      .catch((requestError: unknown) => {
        setError(presentError(requestError, {
          message: 'The deployment request could not be confirmed as completed.',
          action: verifyRolloutAction,
        }));
      })
      .finally(() => setRolloutBusy(false));
  };

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <Button
          variant="primary"
          disabled={!connected || previewLoading}
          onClick={loadPreview}
        >
          {previewLoading
            ? 'Preparing deployment…'
            : (
              <>
                Prepare deployment
                {selectedClientIds.length > 0
                  ? ` (${selectedClientIds.length} selected)`
                  : ' (outdated and failed clients)'}
              </>
            )}
        </Button>
        {children}
        {!connected && (
          <span className="text-xs text-muted">
            Connect to opsi for previews and deployments.
          </span>
        )}
      </div>

      {error && <ErrorState title="Action failed" {...error} />}
      {outcome && <p className="text-sm text-ok-400">{outcome}</p>}

      {preview && (
        <div className="flex flex-col gap-3 rounded border border-warn-700 bg-warn-950/30 p-3">
          <p className="text-sm font-medium text-warn-300">
            Deployment preview — action “{preview.plannedAction}” for{' '}
            {preview.clients.length} client(s)
            {preview.depotFilter ? ` on ${preview.depotFilter}` : ' across all depots'}.
            Nothing has been sent to opsi yet.
          </p>
          <DataTable
            columns={[
              {
                header: 'Client',
                cell: (client) => <ClientName clientId={client.clientId} />,
              },
              { header: 'Depot', cell: (client) => client.depotId ?? '—' },
              { header: 'Installed', cell: (client) => client.installedVersion ?? '—' },
              { header: 'Target', cell: (client) => client.targetVersion ?? '—' },
              {
                header: 'State',
                cell: (client) => <WorkflowBadge state={client.currentState} />,
              },
            ]}
            rows={preview.clients}
            emptyMessage="No clients need this update — nothing to roll out."
          />
          {preview.clients.length > 0 && (
            <>
              <label className="flex cursor-pointer items-center gap-2 text-sm text-warn-300">
                <input
                  type="checkbox"
                  className="accent-accent-500"
                  checked={previewReviewed}
                  onChange={(event) => setPreviewReviewed(event.target.checked)}
                />
                I have reviewed the affected clients and want to request this deployment.
              </label>
              <div>
                <Button
                  variant="primary"
                  disabled={!previewReviewed || rolloutBusy}
                  onClick={requestRollout}
                >
                  {rolloutBusy
                    ? 'Requesting deployment…'
                    : `Request deployment for ${preview.clients.length} client(s)`}
                </Button>
              </div>
            </>
          )}
        </div>
      )}
    </>
  );
}
