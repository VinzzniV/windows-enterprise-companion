import type { ProbeState } from './useDeviceConnectivity';

const presentation: Record<ProbeState, { label: string; classes: string; dot: string }> = {
  responding: {
    label: 'Ping or WinRM responded',
    classes: 'border-ok-700 bg-ok-900/50 text-ok-200',
    dot: 'bg-ok-400',
  },
  'no-response': {
    label: 'No Ping or WinRM response',
    classes: 'border-fail-700 bg-fail-900/50 text-fail-200',
    dot: 'bg-fail-400',
  },
  failed: {
    label: 'Connectivity check failed',
    classes: 'border-fail-700 bg-fail-900/50 text-fail-200',
    dot: 'bg-fail-400',
  },
  checking: {
    label: 'Checking…',
    classes: 'border-warn-700 bg-warn-900/50 text-warn-200',
    dot: 'bg-warn-400',
  },
  'not-checked': {
    label: 'Not checked',
    classes: 'border-slate-700 bg-slate-800 text-slate-200',
    dot: 'bg-slate-400',
  },
};

export function DeviceConnectivityStatus({ status }: { status: ProbeState }) {
  const view = presentation[status];
  return <span role="status" className={`inline-flex items-center gap-2 rounded-md border px-3 py-2 text-base font-semibold ${view.classes}`}>
    <span aria-hidden="true" className={`h-2.5 w-2.5 shrink-0 rounded-full ${view.dot}`} />
    {view.label}
  </span>;
}
