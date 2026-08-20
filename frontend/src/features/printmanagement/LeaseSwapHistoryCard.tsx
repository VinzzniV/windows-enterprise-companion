import { useCallback, useState } from 'react';
import type { PrintHistoryResult, PrintServerDiff, PrintSnapshotStamp } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { Select } from '../../shared/ui/Select';
import { ErrorState } from '../../shared/ui/States';

interface LeaseSwapHistoryCardProps {
  servers: readonly string[];
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

export function LeaseSwapHistoryCard({ servers }: LeaseSwapHistoryCardProps) {
  const [server, setServer] = useState('');
  const [history, setHistory] = useState<PrintSnapshotStamp[]>([]);
  const [baselineId, setBaselineId] = useState('');
  const [diff, setDiff] = useState<PrintServerDiff | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadHistory = useCallback((nextServer: string) => {
    setServer(nextServer);
    setDiff(null);
    setError(null);
    setBaselineId('');
    setHistory([]);
    if (nextServer === '') return;

    invoke<PrintHistoryResult>('printmanagement', 'getHistory', { server: nextServer })
      .then((result) => setHistory(result.snapshots))
      .catch((loadError: unknown) => setError(errorText(loadError)));
  }, []);

  const loadDiff = useCallback(() => {
    setDiff(null);
    setError(null);
    invoke<PrintServerDiff>('printmanagement', 'getDiff', {
      server,
      baselineSnapshotId: baselineId === '' ? null : Number(baselineId),
    })
      .then(setDiff)
      .catch((loadError: unknown) => setError(errorText(loadError)));
  }, [server, baselineId]);

  return (
    <Card title="Lease swap history">
      <div className="flex flex-col gap-3">
        <p className="text-sm text-slate-400">
          Serial-number-based comparison between two scans of the same server — new,
          returned and swapped devices for the lease renewal.
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <Select
            fullWidth={false}
            value={server}
            onChange={(event) => loadHistory(event.target.value)}
            aria-label="Diff server"
          >
            <option value="">Select server…</option>
            {servers.map((availableServer) => (
              <option key={availableServer} value={availableServer}>
                {availableServer}
              </option>
            ))}
          </Select>
          {server !== '' && (
            <>
              <Select
                fullWidth={false}
                value={baselineId}
                onChange={(event) => setBaselineId(event.target.value)}
                aria-label="Baseline snapshot"
              >
                <option value="">Previous scan (default)</option>
                {history.slice(1).map((stamp) => (
                  <option key={stamp.id} value={stamp.id}>
                    {formatTimestamp(stamp.capturedAtUtc)}
                  </option>
                ))}
              </Select>
              <Button onClick={loadDiff}>Compare</Button>
            </>
          )}
        </div>

        {error && <ErrorState message={error} />}
        {diff && (
          <div className="flex flex-col gap-3">
            <p className="text-sm text-slate-400">
              {formatTimestamp(diff.baselineAtUtc)} → {formatTimestamp(diff.latestAtUtc)}
              {diff.devicesWithoutSerialNumber > 0 &&
                ` — ${diff.devicesWithoutSerialNumber} device(s) without a readable serial (not compared)`}
            </p>
            <div className="grid gap-4 lg:grid-cols-3">
              <div>
                <h3 className="mb-1 text-sm font-medium text-ok-400">
                  New ({diff.newDevices.length})
                </h3>
                <DataTable
                  columns={[
                    { header: 'Serial', mono: true, cell: (device) => device.serialNumber },
                    { header: 'Model', cell: (device) => device.model ?? '—' },
                    { header: 'Queue', cell: (device) => device.queueName ?? '—' },
                  ]}
                  rows={diff.newDevices}
                  emptyMessage="No new devices."
                />
              </div>
              <div>
                <h3 className="mb-1 text-sm font-medium text-fail-400">
                  Gone ({diff.goneDevices.length})
                </h3>
                <DataTable
                  columns={[
                    { header: 'Serial', mono: true, cell: (device) => device.serialNumber },
                    { header: 'Model', cell: (device) => device.model ?? '—' },
                    { header: 'Queue', cell: (device) => device.queueName ?? '—' },
                  ]}
                  rows={diff.goneDevices}
                  emptyMessage="No devices gone."
                />
              </div>
              <div>
                <h3 className="mb-1 text-sm font-medium text-warn-400">
                  Swapped ({diff.swappedQueues.length})
                </h3>
                <DataTable
                  columns={[
                    { header: 'Queue', cell: (swap) => swap.queueName },
                    {
                      header: 'Old → new',
                      cell: (swap) => `${swap.oldSerialNumber} → ${swap.newSerialNumber}`,
                    },
                  ]}
                  rows={diff.swappedQueues}
                  emptyMessage="No swapped queues."
                />
              </div>
            </div>
          </div>
        )}
      </div>
    </Card>
  );
}
