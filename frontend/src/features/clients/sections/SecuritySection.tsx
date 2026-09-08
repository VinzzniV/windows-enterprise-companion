import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../../shared/bridge/errorPresentation';
import type { LatestScanResult, SecurityScanResult, TargetRequest } from '../../../shared/api-types';
import {
  CoverageNotes,
  FindingList,
  ResultContext,
  SeveritySummary,
} from '../../security/SecurityResults';
import { ScanHistory } from '../../security/ScanHistory';
import { Button } from '../../../shared/ui/Button';
import { Card } from '../../../shared/ui/Card';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'missing' }
  | { kind: 'loading'; scanning: boolean }
  | { kind: 'loaded'; scan: SecurityScanResult; scanning: boolean; refreshError: ErrorPresentation | null }
  | { kind: 'readError'; error: ErrorPresentation }
  | { kind: 'scanError'; error: ErrorPresentation };

/** Security section of a client: last saved scan on open, run on demand. */
export function SecuritySection({
  target,
  onDataChanged,
}: {
  target: TargetRequest | null;
  onDataChanged?: () => void;
}) {
  const [state, setState] = useState<State>({ kind: 'loading', scanning: false });

  const loadLatest = useCallback(() => {
    setState({ kind: 'loading', scanning: false });
    invoke<LatestScanResult>('security', 'getLatestScan', { target })
      .then((result) =>
        result.scan
          ? setState({ kind: 'loaded', scan: result.scan, scanning: false, refreshError: null })
          : setState({ kind: 'missing' }),
      )
      .catch((error: unknown) => setState({
        kind: 'readError',
        error: presentError(error, { message: 'The stored security scan could not be loaded.' }),
      }));
  }, [target]);

  // Load the latest stored scan on open (no network)
  useEffect(() => {
    loadLatest();
  }, [loadLatest]);

  const runScan = useCallback(() => {
    setState((current) => current.kind === 'loaded'
      ? { ...current, scanning: true, refreshError: null }
      : { kind: 'loading', scanning: true });
    invoke<SecurityScanResult>('security', 'runScan', { target })
      .then((scan) => {
        setState({ kind: 'loaded', scan, scanning: false, refreshError: null });
        onDataChanged?.();
      })
      .catch((error: unknown) => {
        const presentation = presentError(error, { message: 'Security checks could not be completed.' });
        setState((current) => current.kind === 'loaded'
          ? { ...current, scanning: false, refreshError: presentation }
          : { kind: 'scanError', error: presentation });
      });
  }, [target, onDataChanged]);

  if (state.kind === 'loading') {
    return <Spinner label={state.scanning ? 'Running security checks …' : 'Loading last scan …'} />;
  }

  if (state.kind === 'readError') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={loadLatest}>Reload stored security scan</Button>}
      />
    );
  }

  if (state.kind === 'scanError') {
    return (
      <ErrorState
        {...state.error}
        controls={<Button onClick={runScan}>Retry scan</Button>}
      />
    );
  }

  if (state.kind === 'missing') {
    return (
      <EmptyState
        title="No security scan yet"
        message="No stored security scan for this client. Run 13 read-only checks live against this client; WEC saves the result locally without changing the client."
        action={<Button variant="primary" onClick={runScan}>Run security scan</Button>}
      />
    );
  }

  const problems = state.scan.findings;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded border border-slate-800 bg-slate-900/50 px-3 py-2">
        <span className="text-xs text-slate-400">
          Runs 13 read-only checks live against this client and replaces the locally saved Security result. It makes no client changes.
        </span>
        <Button onClick={runScan} disabled={state.scanning}>
          {state.scanning ? 'Running scan …' : 'Re-run scan'}
        </Button>
      </div>
      {state.refreshError && (
        <ErrorState
          {...state.refreshError}
          title="Scan failed; the previous saved scan is still shown."
          controls={<Button onClick={runScan}>Retry scan</Button>}
        />
      )}
      <ResultContext scan={state.scan} problemCount={problems.length} />
      <SeveritySummary problems={problems} />
      {problems.length === 0 ? (
        <Card title="Result">
          <p className={state.scan.coverage.isComplete ? 'text-sm text-ok-400' : 'text-sm text-warn-400'}>
            {state.scan.coverage.isComplete
              ? 'No findings — all applicable checks completed.'
              : state.scan.coverage.isKnown
                ? 'No findings observed — scan coverage is incomplete.'
                : 'No findings observed — legacy scan coverage is unavailable.'}
          </p>
        </Card>
      ) : (
        <FindingList key={state.scan.scanId} findings={problems} />
      )}
      <CoverageNotes results={state.scan.checkResults} />
      <ScanHistory refreshToken={state.scan.scanId} target={target} />
    </div>
  );
}
