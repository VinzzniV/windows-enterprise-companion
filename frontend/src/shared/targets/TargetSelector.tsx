import type { TargetRequest } from '../api-types';

export interface TargetSelection {
  mode: 'local' | 'remote' | 'multiple';
  host: string;
  hosts: string;
  credentialMode: 'currentUser' | 'explicit';
  userName: string;
  domain: string;
  password: string;
}

export const LOCAL_TARGET_SELECTION: TargetSelection = {
  mode: 'local',
  host: '',
  hosts: '',
  credentialMode: 'currentUser',
  userName: '',
  domain: '',
  password: '',
};

function credentialFields(selection: TargetSelection): Pick<TargetRequest, 'userName' | 'domain' | 'password'> {
  if (selection.credentialMode !== 'explicit' || selection.userName.trim() === '') {
    return {};
  }
  return {
    userName: selection.userName.trim(),
    domain: selection.domain.trim() || null,
    password: selection.password,
  };
}

/** null = local machine with the current user (no target payload needed). */
export function toTargetRequest(selection: TargetSelection): TargetRequest | null {
  if (selection.mode !== 'remote' || selection.host.trim() === '') {
    return null;
  }
  return { host: selection.host.trim(), ...credentialFields(selection) };
}

/** Distinct, non-empty host list for a multi-computer scan. */
export function toHostList(selection: TargetSelection): string[] {
  return [
    ...new Set(
      selection.hosts
        .split(/[\s,;]+/)
        .map((host) => host.trim())
        .filter((host) => host !== ''),
    ),
  ];
}

export function toTargetRequestForHost(selection: TargetSelection, host: string): TargetRequest {
  return { host, ...credentialFields(selection) };
}

interface TargetSelectorProps {
  selection: TargetSelection;
  onChange(selection: TargetSelection): void;
  disabled?: boolean;
  allowMultiple?: boolean;
}

const inputClass =
  'rounded border border-slate-700 bg-slate-900 px-2 py-1 text-sm text-slate-100 ' +
  'placeholder:text-slate-500 focus:border-sky-500 focus:outline-none disabled:opacity-50';

export function TargetSelector({ selection, onChange, disabled, allowMultiple }: TargetSelectorProps) {
  const set = (patch: Partial<TargetSelection>) => onChange({ ...selection, ...patch });

  const modes: { value: TargetSelection['mode']; label: string }[] = [
    { value: 'local', label: 'Local machine' },
    { value: 'remote', label: 'Remote computer' },
    ...(allowMultiple ? [{ value: 'multiple' as const, label: 'Multiple computers' }] : []),
  ];

  return (
    <fieldset className="flex flex-col gap-3 rounded-lg border border-slate-800 bg-slate-900/40 p-3" data-testid="target-selector">
      <legend className="px-1 text-xs font-medium uppercase tracking-wide text-slate-400">Scan target</legend>

      <div className="flex flex-wrap gap-4">
        {modes.map((mode) => (
          <label key={mode.value} className="flex items-center gap-1.5 text-sm">
            <input
              type="radio"
              name="target-mode"
              checked={selection.mode === mode.value}
              onChange={() => set({ mode: mode.value })}
              disabled={disabled}
            />
            {mode.label}
          </label>
        ))}
      </div>

      {selection.mode === 'remote' && (
        <input
          type="text"
          value={selection.host}
          onChange={(event) => set({ host: event.target.value })}
          placeholder="Hostname, FQDN or IP address"
          aria-label="Remote host"
          disabled={disabled}
          className={inputClass}
        />
      )}

      {selection.mode === 'multiple' && (
        <textarea
          value={selection.hosts}
          onChange={(event) => set({ hosts: event.target.value })}
          placeholder="One host per line (hostname, FQDN or IP address)"
          aria-label="Remote hosts"
          rows={3}
          disabled={disabled}
          className={inputClass}
        />
      )}

      {selection.mode !== 'local' && (
        <div className="flex flex-col gap-2">
          <div className="flex flex-wrap gap-4">
            <label className="flex items-center gap-1.5 text-sm">
              <input
                type="radio"
                name="credential-mode"
                checked={selection.credentialMode === 'currentUser'}
                onChange={() => set({ credentialMode: 'currentUser' })}
                disabled={disabled}
              />
              Current user
            </label>
            <label className="flex items-center gap-1.5 text-sm">
              <input
                type="radio"
                name="credential-mode"
                checked={selection.credentialMode === 'explicit'}
                onChange={() => set({ credentialMode: 'explicit' })}
                disabled={disabled}
              />
              Explicit credentials
            </label>
          </div>

          {selection.credentialMode === 'explicit' && (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
              <input
                type="text"
                value={selection.userName}
                onChange={(event) => set({ userName: event.target.value })}
                placeholder="User name"
                aria-label="User name"
                disabled={disabled}
                className={inputClass}
              />
              <input
                type="text"
                value={selection.domain}
                onChange={(event) => set({ domain: event.target.value })}
                placeholder="Domain (optional)"
                aria-label="Domain"
                disabled={disabled}
                className={inputClass}
              />
              <input
                type="password"
                value={selection.password}
                onChange={(event) => set({ password: event.target.value })}
                placeholder="Password"
                aria-label="Password"
                autoComplete="off"
                disabled={disabled}
                className={inputClass}
              />
            </div>
          )}
        </div>
      )}
    </fieldset>
  );
}
