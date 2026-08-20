import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type {
  PackageUpdateOutcome,
  PackageWorkflowStatus,
  PatchDepotSummary,
  PreparePackagesPlan,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Field } from '../../shared/ui/Field';
import { Select } from '../../shared/ui/Select';
import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { ErrorState } from '../../shared/ui/States';
import { presentOpsiError } from './patchErrors';
import { packageApprovalStatus } from './patchStatus';

const verifyPackageAction =
  'First check the history and package status on the target depots. Repeat the action only if no running or completed update is visible there.';
const verifyPilotAction =
  'First check the history and current approval status. Repeat the pilot approval only if it has not been accepted there.';

interface PatchPackageApprovalWorkflowProps {
  connected: boolean;
  productId: string;
  depots: readonly PatchDepotSummary[];
  onDashboardRefresh: () => void;
  children?: ReactNode;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

/** Owns package approval, test-depot update, pilot approval and depot synchronization. */
export function PatchPackageApprovalWorkflow({
  connected,
  productId,
  depots,
  onDashboardRefresh,
  children,
}: PatchPackageApprovalWorkflowProps) {
  const preferredDepotId = useMemo(() => (
    depots.find((depot) => depot.description?.toLocaleLowerCase().includes('test'))
      ?? depots.find((depot) => !depot.isConfigServer)
      ?? depots[0]
  )?.id ?? '', [depots]);
  const [workflow, setWorkflow] = useState<PackageWorkflowStatus | null>(null);
  const [workflowLoading, setWorkflowLoading] = useState(false);
  const [workflowError, setWorkflowError] = useState<ErrorPresentation | null>(null);
  const workflowRequestId = useRef(0);
  const scopeGeneration = useRef(0);
  const [plan, setPlan] = useState<PreparePackagesPlan | null>(null);
  const [reviewed, setReviewed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<PackageUpdateOutcome | null>(null);
  const [actionError, setActionError] = useState<ErrorPresentation | null>(null);
  const [testDepotId, setTestDepotId] = useState(preferredDepotId);
  const [pilotReviewed, setPilotReviewed] = useState(false);

  const loadWorkflow = useCallback(() => {
    if (!connected) return;
    const requestId = ++workflowRequestId.current;
    setWorkflowLoading(true);
    setWorkflow(null);
    setWorkflowError(null);
    invoke<PackageWorkflowStatus>('patchmanagement', 'getPackageWorkflowStatus', { productId })
      .then((result) => {
        if (requestId !== workflowRequestId.current) return;
        setWorkflow(result);
        if (result.testDepotId) setTestDepotId(result.testDepotId);
      })
      .catch((requestError: unknown) => {
        if (requestId !== workflowRequestId.current) return;
        setWorkflow(null);
        setWorkflowError(
          presentError(requestError, { message: 'The package approval status could not be loaded.' }),
        );
      })
      .finally(() => {
        if (requestId === workflowRequestId.current) setWorkflowLoading(false);
      });
  }, [connected, productId]);

  useEffect(() => {
    scopeGeneration.current += 1;
    workflowRequestId.current += 1;
    setWorkflow(null);
    setWorkflowLoading(false);
    setWorkflowError(null);
    setPlan(null);
    setReviewed(false);
    setBusy(false);
    setOutcome(null);
    setActionError(null);
    setTestDepotId(preferredDepotId);
    setPilotReviewed(false);
    if (connected) loadWorkflow();
    return () => {
      scopeGeneration.current += 1;
      workflowRequestId.current += 1;
    };
  }, [connected, loadWorkflow, preferredDepotId, productId]);

  const planPackages = (stage: 'TEST' | 'DEPOT_SYNC') => {
    const generation = scopeGeneration.current;
    setActionError(null);
    setPlan(null);
    setReviewed(false);
    setOutcome(null);
    const depotIds = stage === 'TEST'
      ? [testDepotId]
      : depots
          .map((depot) => depot.id)
          .filter((depotId) => depotId !== workflow?.testDepotId);
    invoke<PreparePackagesPlan>('patchmanagement', 'preparePackages', {
      productId,
      stage,
      depotIds,
    })
      .then((result) => {
        if (generation === scopeGeneration.current) setPlan(result);
      })
      .catch((requestError: unknown) => {
        if (generation !== scopeGeneration.current) return;
        setActionError(
          presentOpsiError(requestError, 'The package action plan could not be created.'),
        );
      });
  };

  const executePackageUpdate = () => {
    if (plan === null) return;
    const generation = scopeGeneration.current;
    setBusy(true);
    setOutcome(null);
    setActionError(null);
    invoke<PackageUpdateOutcome>('patchmanagement', 'executePackageUpdate', {
      productId: plan.productId,
      stage: plan.stage,
      depotIds: plan.targets.map((target) => target.depotId),
      confirmed: true,
    })
      .then((result) => {
        if (generation !== scopeGeneration.current) return;
        setOutcome(result);
        setPlan(null);
        setReviewed(false);
        loadWorkflow();
        onDashboardRefresh();
      })
      .catch((requestError: unknown) => {
        if (generation !== scopeGeneration.current) return;
        setActionError(presentError(requestError, {
          message: 'The package action could not be confirmed as completed.',
          action: verifyPackageAction,
        }));
      })
      .finally(() => {
        if (generation === scopeGeneration.current) setBusy(false);
      });
  };

  const approvePilot = () => {
    const generation = scopeGeneration.current;
    setBusy(true);
    setActionError(null);
    invoke<PackageWorkflowStatus>('patchmanagement', 'approvePackagePilot', {
      productId,
      confirmed: true,
    })
      .then((result) => {
        if (generation !== scopeGeneration.current) return;
        setWorkflow(result);
        setPilotReviewed(false);
      })
      .catch((requestError: unknown) => {
        if (generation !== scopeGeneration.current) return;
        setActionError(presentError(requestError, {
          message: 'The pilot approval could not be confirmed as completed.',
          action: verifyPilotAction,
        }));
      })
      .finally(() => {
        if (generation === scopeGeneration.current) setBusy(false);
      });
  };

  const approvalPresentation = packageApprovalStatus(
    workflowLoading
      ? { kind: 'loading' }
      : workflowError
        ? { kind: 'failed' }
        : workflow
          ? { kind: 'loaded', workflow }
          : { kind: 'unavailable' },
  );

  return (
    <>
      {workflowError && (
        <ErrorState
          title="Approval status unavailable"
          {...workflowError}
          controls={<Button onClick={loadWorkflow}>Reload approval status</Button>}
        />
      )}

      <div className="rounded border border-slate-800 bg-slate-950/40 p-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="flex items-center gap-2 text-sm font-medium text-slate-200">
              Package approval chain
              <span className="inline-flex flex-wrap items-center gap-1.5">
                <SemanticStatusBadge status={approvalPresentation.status} />
                {approvalPresentation.context && (
                  <span className="text-xs text-slate-400">{approvalPresentation.context}</span>
                )}
              </span>
            </div>
            <p className="mt-1 text-xs text-muted">
              {workflowLoading
                ? 'Loading the current approval status…'
                : workflowError
                  ? 'The current approval status is not verified.'
                  : workflow?.testUpdateSucceededAtUtc
                    ? `${workflow.testDepotId}: ${workflow.testedVersion ?? 'Version confirmed'} · ${formatTimestamp(workflow.testUpdateSucceededAtUtc)}`
                    : workflow
                      ? 'Update one depot first, deploy to test clients, and verify the result.'
                      : 'Connect to opsi to verify the current approval status.'}
            </p>
            {workflow?.lastError && (
              <p className="mt-1 text-xs text-fail-400">Latest package error: {workflow.lastError}</p>
            )}
          </div>
          <div className="min-w-56">
            <Field label="Test depot">
              {(controlId) => (
                <Select
                  id={controlId}
                  aria-label="Test depot"
                  value={testDepotId}
                  disabled={busy}
                  onChange={(event) => {
                    setTestDepotId(event.target.value);
                    setPlan(null);
                    setReviewed(false);
                  }}
                >
                  {depots.map((depot) => (
                    <option key={depot.id} value={depot.id}>
                      {depot.id}{depot.description ? ` · ${depot.description}` : ''}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
          </div>
        </div>
      </div>

      {children}

      <div className="flex flex-wrap items-center gap-3">
        <Button
          disabled={!connected || busy || testDepotId === ''}
          onClick={() => planPackages('TEST')}
        >
          Prepare test update
        </Button>
        <Button
          disabled={!connected || busy || !workflow?.pilotApproved || depots.length < 2}
          onClick={() => planPackages('DEPOT_SYNC')}
        >
          Distribute to additional depots
        </Button>
      </div>

      {actionError && <ErrorState title="Action failed" {...actionError} />}

      {workflow?.testUpdateSucceededAtUtc && !workflow.pilotApproved && (
        <div className="flex flex-col gap-3 rounded border border-warn-700 bg-warn-950/30 p-3">
          <p className="text-sm text-warn-300">
            The package update on <strong>{workflow.testDepotId}</strong> succeeded.
            Now deploy it to selected test clients and verify the result before approving depot
            distribution.
          </p>
          <label className="flex cursor-pointer items-center gap-2 text-sm text-warn-300">
            <input
              type="checkbox"
              className="accent-accent-500"
              checked={pilotReviewed}
              onChange={(event) => setPilotReviewed(event.target.checked)}
            />
            The test installation and application test succeeded; I approve this package version.
          </label>
          <div>
            <Button variant="primary" disabled={!pilotReviewed || busy} onClick={approvePilot}>
              Approve pilot
            </Button>
          </div>
        </div>
      )}

      {outcome && (
        <div className={`rounded border p-3 ${outcome.failedTargetCount > 0 ? 'border-fail-700 bg-fail-950/20' : 'border-ok-700 bg-ok-950/20'}`}>
          <p className="text-sm text-slate-200">
            Package action completed: {outcome.succeededTargetCount} succeeded,{' '}
            {outcome.failedTargetCount} failed.
          </p>
          <ul className="mt-2 space-y-1 text-xs text-slate-400">
            {outcome.targets.map((target) => (
              <li key={target.depotId}>
                {target.depotId}: {target.success
                  ? `${target.oldVersion ?? 'missing'} → ${target.newVersion ?? 'unknown'}`
                  : target.error}
              </li>
            ))}
          </ul>
        </div>
      )}

      {plan && (
        <div className="flex flex-col gap-3 rounded border border-warn-700 bg-warn-950/30 p-3">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge variant={plan.mode === 'REPOSITORY' ? 'info' : 'elevation'}>
              {plan.mode === 'CUSTOM_BUILD'
                ? 'Build manufacturer package'
                : plan.mode === 'CUSTOM_PROMOTION'
                  ? 'Distribute approved package'
                  : 'Repository package'}
            </StatusBadge>
            {plan.artifactVersion && (
              <span className="text-xs text-slate-400">Target version {plan.artifactVersion}</span>
            )}
          </div>
          <p className="text-sm text-slate-300">{plan.note}</p>
          {plan.targets.map((target) => (
            <div key={target.depotId} className="grid gap-1">
              <span className="text-xs text-slate-400">
                {target.depotId} · current {target.currentVersion ?? 'package missing'}
              </span>
              <code className="overflow-x-auto rounded bg-slate-900 p-2 text-sm text-accent-300">
                {target.command}
              </code>
            </div>
          ))}
          <p className="text-xs text-muted">
            Downloads and SSH/SCP use bounded transfers, key/agent authentication, BatchMode,
            and previously trusted host keys. No password is stored or requested interactively.
          </p>
          <label className="flex cursor-pointer items-center gap-2 text-sm text-warn-300">
            <input
              type="checkbox"
              className="accent-accent-500"
              checked={reviewed}
              onChange={(event) => setReviewed(event.target.checked)}
            />
            {plan.confirmationText}
          </label>
          <div>
            <Button
              variant="primary"
              disabled={!reviewed || busy}
              onClick={executePackageUpdate}
            >
              {busy
                ? 'Running package action…'
                : plan.mode === 'CUSTOM_BUILD'
                  ? 'Download manufacturer file and build test package'
                  : 'Confirm and run over SSH'}
            </Button>
          </div>
        </div>
      )}
    </>
  );
}
