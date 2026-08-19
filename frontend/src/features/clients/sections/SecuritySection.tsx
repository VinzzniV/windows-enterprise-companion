import { useCallback, useEffect, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { errorText } from '../../../shared/bridge/errorText';
import type { LatestScanResult, SecurityScanResult, TargetRequest } from '../../../shared/api-types';
import {
  CoverageNotes,
  FindingList,
  ResultContext,
  SeveritySummary,
} from '../../security/SecurityPage';
import { ScanHistory } from '../../security/ScanHistory';
import { Button } from '../../../shared/ui/Button';
import { Card } from '../../../shared/ui/Card';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'idle' }
  | { kind: 'loading'; scanning: boolean }
  | { kind: 'loaded'; scan: SecurityScanResult }
  | { kind: 'error'; message: string };

/** Security section of a client: last saved scan on open, run on demand. */
export function SecuritySection({ target }: { target: TargetRequest | null }) {
  const [state, setState] = useState<State>({ kind: 'loading', scanning: false });

  // Load the latest stored scan on open (no network)
  useEffect(() => {
    setState({ kind: 'loading', scanning: false });
    invoke<LatestScanResult>('security', 'getLatestScan', { target })
      .then((result) =>
        result.scan ? setState({ kind: 'loaded', scan: result.scan }) : setState({ kind: 'idle' }),
      )
      .catch(() => setState({ kind: 'idle' }));
  }, [target]);

  const runScan = useCallback(() => {
    setState({ kind: 'loading', scanning: true });
    invoke<SecurityScanResult>('security', 'runScan', { target })
      .then((scan) => setState({ kind: 'loaded', scan }))
      .catch((error: unknown) => setState({ kind: 'error', message: errorText(error) }));
  }, [target]);

  if (state.kind === 'loading') {
    return <Spinner label={state.scanning ? 'Running security checks …' : 'Loading last scan …'} />;
  }

  if (state.kind === 'error') {
    return (
      <div className="flex flex-col gap-3">
        <ErrorState message={state.message} />
        <div>
          <Button onClick={runScan}>Retry scan</Button>
        </div>
      </div>
    );
  }

  if (state.kind === 'idle') {
    return (
      <EmptyState
        title="No security scan yet"
        message="No stored security scan for this client. Run the 13 read-only checks now."
        action={<Button variant="primary" onClick={runScan}>Run security scan</Button>}
      />
    );
  }

  const problems = state.scan.findings;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-end">
        <Button onClick={runScan}>Re-run scan</Button>
      </div>
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
