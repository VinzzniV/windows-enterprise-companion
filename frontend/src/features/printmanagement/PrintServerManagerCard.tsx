import type { PrintServerSnapshot } from '../../shared/api-types';
import type { ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Input } from '../../shared/ui/Input';
import { ErrorState } from '../../shared/ui/States';
import { ContextualPrintStatus } from './PrintStatusView';

export interface ServerScanState {
  status: 'loading' | 'done' | 'error';
  error?: ErrorPresentation;
}

interface PrintServerManagerCardProps {
  newServer: string;
  managedServers: readonly string[];
  snapshots: Readonly<Record<string, PrintServerSnapshot>>;
  scanStates: Readonly<Record<string, ServerScanState>>;
  scanning: boolean;
  restoring: boolean;
  onNewServerChange: (value: string) => void;
  onAddServer: () => void;
  onScanServer: (server: string) => void;
  onRemoveServer: (server: string) => void;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

export function PrintServerManagerCard({
  newServer,
  managedServers,
  snapshots,
  scanStates,
  scanning,
  restoring,
  onNewServerChange,
  onAddServer,
  onScanServer,
  onRemoveServer,
}: PrintServerManagerCardProps) {
  return (
    <Card title="Print servers">
      <div className="flex flex-col gap-3">
        {restoring ? (
          <p className="text-sm text-slate-400" role="status">
            Loading saved print servers and their latest snapshots…
          </p>
        ) : (
          <>
            <p className="text-sm text-slate-400">
              {managedServers.length === 0
                ? 'Start with the print server that owns your queues. Enter its hostname below; WEC saves it, runs the first scan, and then shows captured printers here.'
                : 'Add each print server once — they stay saved until you remove them. Scanning reads the queues over WinRM and enriches each device over SNMP (as the signed-in admin).'}
            </p>
            <form
              className="flex flex-wrap items-center gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                onAddServer();
              }}
            >
              <Input
                type="text"
                value={newServer}
                onChange={(event) => onNewServerChange(event.target.value)}
                placeholder="Print server hostname (e.g. pk-srvprint01)"
                aria-label="Print server hostname"
                className="w-80"
              />
              <Button type="submit" disabled={newServer.trim() === '' || scanning}>
                Add &amp; scan
              </Button>
            </form>

            {managedServers.length > 0 && (
          <ul className="flex flex-col divide-y divide-slate-800/70 rounded border border-slate-800">
            {managedServers.map((server) => {
              const state = scanStates[server.toUpperCase()];
              const snapshot = snapshots[server];
              return (
                <li key={server} className="flex flex-col gap-2 px-3 py-2 text-sm">
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="font-medium text-slate-100">{server}</span>
                    {state?.status === 'loading' && (
                      <ContextualPrintStatus
                        presentation={{
                          status: { dimension: 'execution', value: 'running' },
                          context: 'Scanning',
                        }}
                      />
                    )}
                    {snapshot ? (
                      <span className="text-xs text-muted">
                        {snapshot.printers.length} printer{snapshot.printers.length === 1 ? '' : 's'} · captured{' '}
                        {formatTimestamp(snapshot.capturedAtUtc)}
                      </span>
                    ) : (
                      !state && <span className="text-xs text-muted">not scanned yet</span>
                    )}
                    <div className="ml-auto flex items-center gap-2">
                      {state?.status !== 'error' && (
                        <Button variant="ghost" onClick={() => onScanServer(server)} disabled={scanning}>
                          Rescan
                        </Button>
                      )}
                      <Button variant="ghost" onClick={() => onRemoveServer(server)}>
                        Remove
                      </Button>
                    </div>
                  </div>
                  {state?.status === 'error' && state.error && (
                    <ErrorState
                      title={`Scan of ${server} failed`}
                      {...state.error}
                      controls={(
                        <Button variant="secondary" onClick={() => onScanServer(server)} disabled={scanning}>
                          Retry scan
                        </Button>
                      )}
                    />
                  )}
                </li>
              );
            })}
          </ul>
            )}
          </>
        )}
      </div>
    </Card>
  );
}
