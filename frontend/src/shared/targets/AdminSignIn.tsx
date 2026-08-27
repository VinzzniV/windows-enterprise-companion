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
          <Badge tone="accent">Admin: {displayName}</Badge>
          <button
            type="button"
            className="text-xs text-slate-400 underline-offset-2 hover:text-slate-200 hover:underline"
            onClick={signOutAdmin}
          >
            Sign out
          </button>
        </div>
      ) : (
        <Button variant="secondary" onClick={() => setOpen((value) => !value)} aria-expanded={open}>
          Sign in as admin
        </Button>
      )}

      {open && !displayName && (
        <div
          role="dialog"
          aria-label="Admin sign-in"
          className="absolute right-0 top-full z-30 mt-2 w-80 rounded-lg border border-slate-700 bg-slate-900 p-3 shadow-xl"
        >
          <p className="mb-2 text-xs text-slate-400">
            Used for every remote target this session. Kept in memory only — never stored.
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
                Sign in
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
