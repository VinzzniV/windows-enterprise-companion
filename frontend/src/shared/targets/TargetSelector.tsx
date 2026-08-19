import { useState } from 'react';
import type { AdComputer, AdComputerSearchResult, TargetRequest } from '../api-types';
import { BridgeInvokeError, invoke } from '../bridge/bridgeClient';
import { Button } from '../ui/Button';
import { Input, controlClass } from '../ui/Input';

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

// When admin is passed (global sign-in), it overrides the per-selection
// credentials; undefined keeps the legacy selection-based credential fields.
function credentialPatch(
  selection: TargetSelection,
  admin: CredentialValues | null | undefined,
): Pick<TargetRequest, 'userName' | 'domain' | 'password'> {
  if (admin === undefined) {
    return credentialFields(selection);
  }
  return admin && admin.userName.trim() !== ''
    ? { userName: admin.userName.trim(), domain: admin.domain.trim() || null, password: admin.password }
    : {};
}

/** null = local machine with the current user (no target payload needed). */
export function toTargetRequest(
  selection: TargetSelection,
  admin?: CredentialValues | null,
): TargetRequest | null {
  if (selection.mode !== 'remote' || selection.host.trim() === '') {
    return null;
  }
  return { host: selection.host.trim(), ...credentialPatch(selection, admin) };
}

/** Case-insensitive identity of a target ('LOCAL' for the local machine). */
export function hostKeyOf(target: TargetRequest | null): string {
  return (target?.host ?? 'LOCAL').toUpperCase();
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

export function toTargetRequestForHost(
  selection: TargetSelection,
  host: string,
  admin?: CredentialValues | null,
): TargetRequest {
  return { host, ...credentialPatch(selection, admin) };
}

interface TargetSelectorProps {
  selection: TargetSelection;
  onChange(selection: TargetSelection): void;
  disabled?: boolean;
  allowMultiple?: boolean;
  /** Hide the per-target credential fields — the global admin sign-in is used instead. */
  hideCredentials?: boolean;
}

export interface CredentialValues {
  userName: string;
  domain: string;
  password: string;
}

/** The user/domain/password grid shared by all pages that take explicit credentials. */
export function CredentialFields({
  values,
  onChange,
  disabled,
  domainPlaceholder = 'Domain (optional)',
  domainAriaLabel = 'Domain',
}: {
  values: CredentialValues;
  onChange(patch: Partial<CredentialValues>): void;
  disabled?: boolean;
  domainPlaceholder?: string;
  domainAriaLabel?: string;
}) {
  return (
    <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
      <Input
        type="text"
        value={values.userName}
        onChange={(event) => onChange({ userName: event.target.value })}
        placeholder="User name"
        aria-label="User name"
        disabled={disabled}
      />
      <Input
        type="text"
        value={values.domain}
        onChange={(event) => onChange({ domain: event.target.value })}
        placeholder={domainPlaceholder}
        aria-label={domainAriaLabel}
        disabled={disabled}
      />
      <Input
        type="password"
        value={values.password}
        onChange={(event) => onChange({ password: event.target.value })}
        placeholder="Password"
        aria-label="Password"
        autoComplete="off"
        disabled={disabled}
      />
    </div>
  );
}

const radioClass = 'accent-accent-500';

export function TargetSelector({ selection, onChange, disabled, allowMultiple, hideCredentials }: TargetSelectorProps) {
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
          <label key={mode.value} className="flex cursor-pointer items-center gap-1.5 text-sm">
            <input
              type="radio"
              name="target-mode"
              className={radioClass}
              checked={selection.mode === mode.value}
              onChange={() => set({ mode: mode.value })}
              disabled={disabled}
            />
            {mode.label}
          </label>
        ))}
      </div>

      {selection.mode === 'remote' && (
        <Input
          type="text"
          value={selection.host}
          onChange={(event) => set({ host: event.target.value })}
          placeholder="Hostname, FQDN or IP address"
          aria-label="Remote host"
          disabled={disabled}
        />
      )}

      {selection.mode === 'multiple' && (
        <>
          <textarea
            value={selection.hosts}
            onChange={(event) => set({ hosts: event.target.value })}
            placeholder="One host per line (hostname, FQDN or IP address)"
            aria-label="Remote hosts"
            rows={3}
            disabled={disabled}
            className={`${controlClass} w-full`}
          />
          <AdComputerPicker
            disabled={disabled}
            onAdd={(hosts) =>
              set({ hosts: [...new Set([...toHostList(selection), ...hosts])].join('\n') })
            }
          />
        </>
      )}

      {selection.mode !== 'local' && !hideCredentials && (
        <div className="flex flex-col gap-2">
          <div className="flex flex-wrap gap-4">
            <label className="flex cursor-pointer items-center gap-1.5 text-sm">
              <input
                type="radio"
                name="credential-mode"
                className={radioClass}
                checked={selection.credentialMode === 'currentUser'}
                onChange={() => set({ credentialMode: 'currentUser' })}
                disabled={disabled}
              />
              Current user
            </label>
            <label className="flex cursor-pointer items-center gap-1.5 text-sm">
              <input
                type="radio"
                name="credential-mode"
                className={radioClass}
                checked={selection.credentialMode === 'explicit'}
                onChange={() => set({ credentialMode: 'explicit' })}
                disabled={disabled}
              />
              Explicit credentials
            </label>
          </div>

          {selection.credentialMode === 'explicit' && (
            <CredentialFields
              values={selection}
              onChange={(patch) => set(patch)}
              disabled={disabled}
            />
          )}
        </div>
      )}

      {selection.mode !== 'local' && hideCredentials && (
        <p className="text-xs text-slate-500">
          Runs as the signed-in admin (top right), or the current user when not signed in.
        </p>
      )}
    </fieldset>
  );
}

/**
 * "Get-ADComputer with filter" for the UI: search domain computers over the
 * read-only LDAP seam and add the selection to the multi-host list. Runs as
 * the current user against the machine's own domain — for explicit
 * directory credentials use the Active Directory page.
 */
function AdComputerPicker({
  disabled,
  onAdd,
}: {
  disabled?: boolean;
  onAdd(hosts: string[]): void;
}) {
  const [filter, setFilter] = useState('');
  const [includeDisabled, setIncludeDisabled] = useState(false);
  const [searching, setSearching] = useState(false);
  const [result, setResult] = useState<AdComputerSearchResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [checked, setChecked] = useState<ReadonlySet<string>>(new Set());

  const hostOf = (computer: AdComputer) => computer.dnsHostName ?? computer.name;

  const search = () => {
    setSearching(true);
    setError(null);
    setResult(null);
    setChecked(new Set());
    invoke<AdComputerSearchResult>(
      'activedirectory',
      'searchComputers',
      { nameFilter: filter.trim() || null, includeDisabled },
    )
      .then((searchResult) => {
        setResult(searchResult);
        // Preselect what people almost always want: every enabled machine found
        setChecked(new Set(
          searchResult.computers.filter((computer) => computer.enabled).map(hostOf),
        ));
      })
      .catch((searchError: unknown) => {
        setError(
          searchError instanceof BridgeInvokeError
            ? `${searchError.error.code}: ${searchError.error.message}`
            : searchError instanceof Error
              ? searchError.message
              : String(searchError),
        );
      })
      .finally(() => setSearching(false));
  };

  const toggle = (host: string) =>
    setChecked((previous) => {
      const next = new Set(previous);
      if (next.has(host)) {
        next.delete(host);
      } else {
        next.add(host);
      }
      return next;
    });

  return (
    <div className="flex flex-col gap-2 rounded border border-slate-800 bg-slate-900/60 p-2">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs font-medium uppercase tracking-wide text-slate-400">
          Add from Active Directory
        </span>
        <Input
          type="text"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              search();
            }
          }}
          placeholder="Name filter (substring or * wildcard, empty = all)"
          aria-label="AD computer name filter"
          disabled={disabled || searching}
          className="min-w-52 flex-1"
        />
        <label className="flex cursor-pointer items-center gap-1.5 text-xs text-slate-400">
          <input
            type="checkbox"
            className="accent-accent-500"
            checked={includeDisabled}
            onChange={(event) => setIncludeDisabled(event.target.checked)}
            disabled={disabled || searching}
          />
          Include disabled
        </label>
        <Button variant="secondary" onClick={search} disabled={disabled || searching}>
          {searching ? 'Searching…' : 'Search AD'}
        </Button>
      </div>

      {error && (
        <p role="alert" className="text-xs text-fail-400">
          {error} — for explicit directory credentials use the Active Directory page.
        </p>
      )}

      {result && !result.domainJoined && (
        <p className="text-xs text-slate-400">
          This machine is not domain-joined — the AD search needs a domain.
        </p>
      )}

      {result?.domainJoined && (
        <>
          <p className="text-xs text-slate-400">
            {result.computers.length} computer(s) in {result.domainName}
            {result.truncated ? ' (list truncated — refine the filter)' : ''}
          </p>
          {result.computers.length > 0 && (
            <>
              <ul className="flex max-h-48 flex-col gap-1 overflow-y-auto">
                {result.computers.map((computer) => {
                  const host = hostOf(computer);
                  return (
                    <li key={host}>
                      <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-200">
                        <input
                          type="checkbox"
                          className="accent-accent-500"
                          checked={checked.has(host)}
                          onChange={() => toggle(host)}
                          disabled={disabled}
                        />
                        <span className="min-w-0 truncate">
                          {host}
                          {!computer.enabled && <span className="ml-1.5 text-xs text-warn-400">disabled</span>}
                          {computer.operatingSystem && (
                            <span className="ml-1.5 text-xs text-slate-500">{computer.operatingSystem}</span>
                          )}
                        </span>
                      </label>
                    </li>
                  );
                })}
              </ul>
              <div>
                <Button
                  variant="primary"
                  onClick={() => onAdd([...checked])}
                  disabled={disabled || checked.size === 0}
                >
                  Add {checked.size} computer(s) to the scan list
                </Button>
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
