import { useCallback, useState } from 'react';
import { invoke } from '../../../shared/bridge/bridgeClient';
import { errorText } from '../../../shared/bridge/errorText';
import type { ClientPrinter, ClientPrinterScan, TargetRequest } from '../../../shared/api-types';
import { Button } from '../../../shared/ui/Button';
import { Badge } from '../../../shared/ui/Badge';
import { DataTable, type DataColumn } from '../../../shared/ui/DataTable';
import { Spinner } from '../../../shared/ui/Spinner';
import { EmptyState, ErrorState } from '../../../shared/ui/States';

type State =
  | { kind: 'idle' }
  | { kind: 'running' }
  | { kind: 'done'; scan: ClientPrinterScan }
  | { kind: 'error'; message: string };

const columns: DataColumn<ClientPrinter>[] = [
  { header: 'Printer', cell: (printer) => <span className="font-medium text-slate-100">{printer.name}</span> },
  { header: 'Driver', cell: (printer) => printer.driverName ?? '—' },
  { header: 'Port', mono: true, cell: (printer) => printer.portName ?? '—' },
  { header: 'Location', cell: (printer) => printer.location ?? '—' },
  {
    header: 'Type',
    cell: (printer) => (
      <div className="flex gap-1">
        <Badge tone={printer.isNetwork ? 'info' : 'neutral'}>
          {printer.isNetwork ? 'Network' : 'Local'}
        </Badge>
        {printer.shared && <Badge tone="accent">Shared</Badge>}
      </div>
    ),
  },
];

/** Printers installed on a client — the deliberate client-side counterpart to the print-server view. */
export function PrintersSection({ target }: { target: TargetRequest | null }) {
  const [state, setState] = useState<State>({ kind: 'idle' });

  const run = useCallback(() => {
    setState({ kind: 'running' });
    invoke<ClientPrinterScan>('printmanagement', 'scanClientPrinters', { target }, 120_000)
      .then((scan) => setState({ kind: 'done', scan }))
      .catch((error: unknown) => setState({ kind: 'error', message: errorText(error) }));
  }, [target]);

  if (state.kind === 'running') {
    return <Spinner label="Reading installed printers …" />;
  }

  if (state.kind === 'error') {
    return (
      <div className="flex flex-col gap-3">
        <ErrorState message={state.message} />
        <div>
          <Button onClick={run}>Retry</Button>
        </div>
      </div>
    );
  }

  if (state.kind === 'idle') {
    return (
      <EmptyState
        title="Installed printers"
        message="List the printers installed on this client (local devices and network connections to print servers). This reads the client's own configuration — no SNMP."
        action={<Button variant="primary" onClick={run}>Scan installed printers</Button>}
      />
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded border border-slate-800 bg-slate-900/50 px-3 py-2 text-sm">
        <span className="text-slate-400">
          {state.scan.printers.length} printer{state.scan.printers.length === 1 ? '' : 's'} · captured{' '}
          {new Date(state.scan.capturedAtUtc).toLocaleString()}
        </span>
        <Button onClick={run}>Re-scan</Button>
      </div>
      <DataTable
        columns={columns}
        rows={state.scan.printers}
        emptyMessage="No printers installed on this client."
        getRowKey={(printer) => printer.name}
      />
    </div>
  );
}
