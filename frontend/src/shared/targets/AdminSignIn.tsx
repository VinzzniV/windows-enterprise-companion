import { useState } from 'react';
import { Button } from '../ui/Button';
import { Badge } from '../ui/Badge';
import { CredentialFields, type CredentialValues } from './Credentials';
import { useTargetsOptional } from './TargetContext';

const emptyCredentials: CredentialValues = { userName: '', domain: '', password: '' };

/**
 * Global admin sign-in for the top bar. Enter the admin account once per
 * session; every remote target reuses it (no per-page credential fields). The
 * password lives in memory only — never persisted, never logged (ADR 0011).
 */
export function AdminSignIn() {
  const targets = useTargetsOptional();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<CredentialValues>(emptyCredentials);

  // Outside a TargetProvider (e.g. isolated previews) there is nothing to sign into.
  if (targets === null) {
    return null;
  }

  const { adminCredentials, signInAdmin, signOutAdmin } = targets;
  const displayName = adminCredentials
    ? `${adminCredentials.domain ? `${adminCredentials.domain}\\` : ''}${adminCredentials.userName}`
    : null;

  const submit = () => {
    signInAdmin(draft);
    setDraft(emptyCredentials);
    setOpen(false);
  };

  return (
    <div className="relative">
      {displayName ? (
        <div className="flex items-center gap-2">
          <Badge tone="accent">Remote account: {displayName}</Badge>
          <button
            type="button"
            className="text-xs text-slate-400 underline-offset-2 hover:text-slate-200 hover:underline"
            onClick={signOutAdmin}
          >
            Clear remote account
          </button>
        </div>
      ) : (
        <Button variant="secondary" onClick={() => setOpen((value) => !value)} aria-expanded={open}>
          Set remote account
        </Button>
      )}

      {open && !displayName && (
        <div
          role="dialog"
          aria-label="Remote access account"
          className="absolute right-0 top-full z-30 mt-2 w-80 max-w-[calc(100vw-2rem)] rounded-lg border border-slate-700 bg-slate-900 p-3 shadow-xl"
        >
          <p className="mb-2 text-xs text-slate-400">
            Used for remote requests this session. This does not elevate the local app.
            Credentials stay in memory and are checked when a request runs.
          </p>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              submit();
            }}
            className="flex flex-col gap-3"
          >
            <CredentialFields values={draft} onChange={(patch) => setDraft({ ...draft, ...patch })} />
            <div className="flex gap-2">
              <Button type="submit" variant="primary" disabled={draft.userName.trim() === ''}>
                Use remote account
              </Button>
              <Button type="button" variant="ghost" onClick={() => setOpen(false)}>
                Cancel
              </Button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
