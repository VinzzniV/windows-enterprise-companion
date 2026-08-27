import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  BatchScanProgress,
  BatchScanResult,
  DiagnosticBatchProgress,
  DiagnosticBatchResult,
  DiagnosticBatchHostStatus,
  HostScanStatus,
  InventoryBatchProgress,
  InventoryBatchResult,
  InventoryBatchHostStatus,
  ScanError,
} from '../../shared/api-types';
import {
  BridgeCancelledError,
  invokeCancellable,
  subscribe,
  type CancellableBridgeInvocation,
} from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Select } from '../../shared/ui/Select';
import { ErrorState } from '../../shared/ui/States';
import { BatchHostRow } from '../security/SecurityResults';
import { CategorySections, RunSummary } from '../diagnostics/HealthResults';

type BulkOperation = 'inventory' | 'security' | 'health';
type BulkHostStatus = InventoryBatchHostStatus | DiagnosticBatchHostStatus | HostScanStatus;
type BulkResult = InventoryBatchResult | BatchScanResult | DiagnosticBatchResult;

type BulkState =
  | { kind: 'idle' }
  | { kind: 'running'; operation: BulkOperation; statuses: Record<string, BulkHostStatus> }
  | { kind: 'done'; operation: BulkOperation; result: BulkResult }
  | { kind: 'cancelled'; operation: BulkOperation }
  | { kind: 'error'; operation: BulkOperation; error: ErrorPresentation };

const operationLabels: Record<BulkOperation, string> = {
  inventory: 'Inventory',
  security: 'Security',
  health: 'Health',
};

const operationBridge: Record<BulkOperation, { module: string; action: string }> = {
  inventory: { module: 'inventory', action: 'runBatchScan' },
  security: { module: 'security', action: 'runBatchScan' },
  health: { module: 'diagnostics', action: 'runBatchDiagnostics' },
};

function statusTone(status: BulkHostStatus): BadgeTone {
  if (status === 'FAILED') return 'fail';
  if (status === 'COMPLETED_WITH_ERRORS') return 'warn';
  if (status === 'COMPLETED') return 'ok';
  if (status === 'RUNNING' || status === 'CONNECTING') return 'info';
  return 'neutral';
}

function StatusBadge({ status }: { status: BulkHostStatus }) {
  return <Badge tone={statusTone(status)}>{status.replaceAll('_', ' ')}</Badge>;
}

function FailureText({ error }: { error: ScanError }) {
  return (
    <p role="alert" className="mt-2 break-words text-xs text-fail-400">
      {error.phase}: {error.code} — {error.message}{error.details ? ` ${error.details}` : ''}
    </p>
  );
}

function InventoryResults({ result }: { result: InventoryBatchResult }) {
  return (
    <ul className="flex flex-col gap-2">
      {result.hosts.map((outcome) => (
        <li key={outcome.host} className="rounded border border-slate-800 bg-slate-950/50 p-3">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            <Link
              className="min-w-32 font-medium text-accent-300 hover:text-accent-200"
              to={`/clients/${encodeURIComponent(outcome.host)}?section=inventory`}
            >
              {outcome.host}
            </Link>
            <StatusBadge status={outcome.status} />
            {outcome.inventory && (
              <>
                <span className="text-slate-300">{outcome.inventory.snapshot.operatingSystem.caption}</span>
                <span className="text-xs text-muted">
                  {new Date(outcome.inventory.capturedAtUtc).toLocaleString()}
                </span>
                <span className="text-xs text-muted">
                  {outcome.inventory.snapshot.installedSoftware?.length ?? '—'} software entries
                </span>
              </>
            )}
          </div>
          {outcome.error && <FailureText error={outcome.error} />}
        </li>
      ))}
    </ul>
  );
}

function HealthResults({ result }: { result: DiagnosticBatchResult }) {
  return (
    <ul className="flex flex-col gap-2">
      {result.hosts.map((outcome) => (
        <li key={outcome.host} className="rounded border border-slate-800 bg-slate-950/50 p-3">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            <Link
              className="min-w-32 font-medium text-accent-300 hover:text-accent-200"
              to={`/clients/${encodeURIComponent(outcome.host)}?section=diagnostics`}
            >
              {outcome.host}
            </Link>
            <StatusBadge status={outcome.status} />
            {outcome.run && <span className="text-xs text-muted">{new Date(outcome.run.completedAtUtc).toLocaleString()}</span>}
          </div>
          {outcome.error && <FailureText error={outcome.error} />}
          {outcome.run && (
            <div className="mt-2">
              <RunSummary results={outcome.run.results} />
              <div className="mt-2">
                <DetailsDisclosure summary={`Show ${outcome.run.results.length} Health results`}>
                  <div className="flex flex-col gap-3">
                    <CategorySections results={outcome.run.results} />
                  </div>
                </DetailsDisclosure>
              </div>
            </div>
          )}
        </li>
      ))}
    </ul>
  );
}

function CompletedResults({ state }: { state: Extract<BulkState, { kind: 'done' }> }) {
  if (state.operation === 'inventory') {
    return <InventoryResults result={state.result as InventoryBatchResult} />;
  }
  if (state.operation === 'health') {
    return <HealthResults result={state.result as DiagnosticBatchResult} />;
  }
  const result = state.result as BatchScanResult;
  return (
    <ul className="flex flex-col gap-2">
      {result.hosts.map((outcome) => (
        <BatchHostRow
          key={outcome.host}
          outcome={outcome}
          hostHref={`/clients/${encodeURIComponent(outcome.host)}?section=security`}
        />
      ))}
    </ul>
  );
}

interface ClientBulkActionsProps {
  selectedHosts: readonly string[];
  maxBatchHosts: number | null;
  onRunningChange(running: boolean): void;
  onCompleted(): void;
}

export function ClientBulkActions({
  selectedHosts,
  maxBatchHosts,
  onRunningChange,
  onCompleted,
}: ClientBulkActionsProps) {
  const targets = useTargetsOptional();
  const [operation, setOperation] = useState<BulkOperation>('inventory');
  const [state, setState] = useState<BulkState>({ kind: 'idle' });
  const activeInvocation = useRef<CancellableBridgeInvocation<BulkResult> | null>(null);
  const selectedKey = selectedHosts.map((host) => host.toUpperCase()).sort().join('\n');

  useEffect(() => {
    onRunningChange(state.kind === 'running');
  }, [onRunningChange, state.kind]);

  useEffect(() => () => activeInvocation.current?.cancel(), []);

  useEffect(() => {
    if (state.kind !== 'running') {
      setState({ kind: 'idle' });
    }
  }, [operation, selectedKey]);

  useEffect(() => {
    const registrations: Array<{
      module: string;
      event: string;
      operation: BulkOperation;
    }> = [
      { module: 'inventory', event: 'batchScanProgress', operation: 'inventory' },
      { module: 'security', event: 'batchScanProgress', operation: 'security' },
      { module: 'diagnostics', event: 'batchRunProgress', operation: 'health' },
    ];
    const unsubscribe = registrations.flatMap((registration) => {
      try {
        return [subscribe(registration.module, registration.event, (payload) => {
          const progress = payload as InventoryBatchProgress | BatchScanProgress | DiagnosticBatchProgress;
          setState((current) => current.kind === 'running' && current.operation === registration.operation
            ? {
                ...current,
                statuses: { ...current.statuses, [progress.host.toUpperCase()]: progress.status },
              }
            : current);
        })];
      } catch {
        return [];
      }
    });
    return () => unsubscribe.forEach((stop) => stop());
  }, []);

  const start = () => {
    if (!selectedHosts.length || maxBatchHosts === null || selectedHosts.length > maxBatchHosts) return;
    const bridge = operationBridge[operation];
    const credentials = targets?.adminCredentials;
    const payload = {
      hosts: [...selectedHosts],
      ...(credentials ? {
        userName: credentials.userName,
        domain: credentials.domain || null,
        password: credentials.password,
      } : {}),
    };
    setState({
      kind: 'running',
      operation,
      statuses: Object.fromEntries(selectedHosts.map((host) => [host.toUpperCase(), 'QUEUED'])),
    });
    const invocation = invokeCancellable<BulkResult>(bridge.module, bridge.action, payload);
    activeInvocation.current = invocation;
    void invocation.promise
      .then((result) => {
        setState({ kind: 'done', operation, result });
        onCompleted();
      })
      .catch((caught) => {
        if (caught instanceof BridgeCancelledError) {
          setState({ kind: 'cancelled', operation });
          return;
        }
        setState({
          kind: 'error',
          operation,
          error: presentError(caught, {
            message: `${operationLabels[operation]} batch could not be completed.`,
            action: 'Review the selected hosts and session credentials, then retry.',
          }),
        });
      })
      .finally(() => {
        activeInvocation.current = null;
      });
  };

  const running = state.kind === 'running';
  const credentialsLabel = targets?.adminCredentials
    ? `Session admin ${targets.adminCredentials.domain ? `${targets.adminCredentials.domain}\\` : ''}${targets.adminCredentials.userName}`
    : 'Current Windows identity';

  return (
    <Card title="Bulk scan workbench">
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1 text-xs font-medium uppercase tracking-wide text-muted">
            Operation
            <Select
              fullWidth={false}
              value={operation}
              disabled={running}
              onChange={(event) => setOperation(event.target.value as BulkOperation)}
              aria-label="Bulk scan operation"
            >
              <option value="inventory">Inventory</option>
              <option value="security">Security</option>
              <option value="health">Health</option>
            </Select>
          </label>
          <div className="min-w-48 flex-1">
            <p className="text-sm font-medium text-slate-200">
              {selectedHosts.length} of {maxBatchHosts ?? '—'} hosts selected
            </p>
            <p className="text-xs text-muted">
              {credentialsLabel} · read-only · no scan starts from selection alone
            </p>
          </div>
          {running ? (
            <Button variant="secondary" onClick={() => activeInvocation.current?.cancel()}>
              Cancel batch
            </Button>
          ) : (
            <Button
              variant="primary"
              onClick={start}
              disabled={!selectedHosts.length || maxBatchHosts === null || selectedHosts.length > maxBatchHosts}
            >
              Run {operationLabels[operation]}
            </Button>
          )}
        </div>

        {maxBatchHosts === null && (
          <p className="text-xs text-warn-400">The configured batch limit is unavailable. Reload Clients before starting a batch.</p>
        )}
        {selectedHosts.length === 0 && (
          <p className="text-sm text-slate-400">Select clients in the table below to prepare a bounded batch.</p>
        )}
        {selectedHosts.length > 0 && (
          <DetailsDisclosure summary={`Review selected hosts (${selectedHosts.length})`}>
            <ul className="grid grid-cols-1 gap-x-4 gap-y-1 text-xs text-slate-300 sm:grid-cols-2 xl:grid-cols-3">
              {selectedHosts.map((host) => <li key={host} className="font-mono">{host}</li>)}
            </ul>
          </DetailsDisclosure>
        )}

        {state.kind === 'running' && (
          <div aria-live="polite">
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted">
              {operationLabels[state.operation]} progress
            </p>
            <ul className="grid grid-cols-1 gap-1 sm:grid-cols-2 xl:grid-cols-3">
              {selectedHosts.map((host) => (
                <li key={host} className="flex items-center justify-between gap-2 rounded border border-slate-800 px-2 py-1 text-sm">
                  <span className="truncate font-mono text-xs">{host}</span>
                  <StatusBadge status={state.statuses[host.toUpperCase()] ?? 'QUEUED'} />
                </li>
              ))}
            </ul>
          </div>
        )}
        {state.kind === 'cancelled' && (
          <p role="status" className="text-sm text-warn-400">
            {operationLabels[state.operation]} batch cancelled. Completed host results remain stored by their owning module.
          </p>
        )}
        {state.kind === 'error' && <ErrorState title={`${operationLabels[state.operation]} batch failed`} {...state.error} />}
        {state.kind === 'done' && (
          <div>
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted">
              {operationLabels[state.operation]} results
            </p>
            <CompletedResults state={state} />
          </div>
        )}
      </div>
    </Card>
  );
}
