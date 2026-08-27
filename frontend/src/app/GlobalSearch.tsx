import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import type {
  AdComputerSearchResult,
  ListInventoryHostsResult,
  ListSecurityScanHostsResult,
  StoredInventoryHost,
  StoredSecurityScanHost,
  UserPageResult,
  UserSummary,
} from '../shared/api-types';
import { invoke } from '../shared/bridge/bridgeClient';
import { useTargets } from '../shared/targets/TargetContext';
import { loadView } from '../shared/viewCache';
import {
  emptyUserDirectoryEndpoint,
  toUserDirectoryConnection,
  userDirectoryViewKey,
  type UserDirectoryEndpoint,
} from '../features/users/users';
import {
  clientResults,
  navigationResults,
  savedTargetResults,
  userResults,
  type GlobalSearchCategory,
  type GlobalSearchResult,
} from './searchResults';

const categoryOrder: readonly GlobalSearchCategory[] = ['Navigation', 'Users', 'Clients', 'Saved targets'];
const remoteQueryMinimum = 2;
const remoteResultLimit = 6;
const debounceMilliseconds = 250;

interface GlobalSearchProps {
  open: boolean;
  onClose(): void;
}

export function GlobalSearch({ open, onClose }: GlobalSearchProps) {
  const navigate = useNavigate();
  const { adminCredentials, savedTargets } = useTargets();
  const inputRef = useRef<HTMLInputElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const searchRequestId = useRef(0);
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [inventoryHosts, setInventoryHosts] = useState<StoredInventoryHost[]>([]);
  const [securityHosts, setSecurityHosts] = useState<StoredSecurityScanHost[]>([]);
  const [directoryComputers, setDirectoryComputers] = useState<AdComputerSearchResult['computers']>([]);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [remoteLoading, setRemoteLoading] = useState(false);
  const [remoteFailures, setRemoteFailures] = useState<string[]>([]);
  const [storedSourcesFailed, setStoredSourcesFailed] = useState(false);

  useEffect(() => {
    if (!open) return;
    setQuery('');
    setActiveIndex(0);
    setDirectoryComputers([]);
    setUsers([]);
    setRemoteFailures([]);
    requestAnimationFrame(() => inputRef.current?.focus());

    let current = true;
    setStoredSourcesFailed(false);
    void Promise.allSettled([
      invoke<ListInventoryHostsResult>('inventory', 'listHosts'),
      invoke<ListSecurityScanHostsResult>('security', 'listHosts'),
    ]).then(([inventory, security]) => {
      if (!current) return;
      if (inventory.status === 'fulfilled') setInventoryHosts(inventory.value.hosts ?? []);
      else setInventoryHosts([]);
      if (security.status === 'fulfilled') setSecurityHosts(security.value.hosts ?? []);
      else setSecurityHosts([]);
      setStoredSourcesFailed(inventory.status === 'rejected' || security.status === 'rejected');
    });
    return () => { current = false; };
  }, [open]);

  useEffect(() => {
    if (!open || query.trim().length < remoteQueryMinimum) {
      searchRequestId.current += 1;
      setDirectoryComputers([]);
      setUsers([]);
      setRemoteLoading(false);
      setRemoteFailures([]);
      return;
    }

    const timer = window.setTimeout(() => {
      const currentRequest = ++searchRequestId.current;
      const trimmedQuery = query.trim();
      const endpoint = loadView<UserDirectoryEndpoint>(userDirectoryViewKey) ?? emptyUserDirectoryEndpoint;
      const connection = toUserDirectoryConnection(endpoint, adminCredentials);
      setRemoteLoading(true);
      setRemoteFailures([]);
      void Promise.allSettled([
        invoke<AdComputerSearchResult>('activedirectory', 'searchComputers', {
          nameFilter: trimmedQuery,
          includeDisabled: true,
          connection,
          resultLimit: remoteResultLimit,
        }),
        invoke<UserPageResult>('usermanagement', 'listUsers', {
          search: trimmedQuery,
          accountState: 'ALL',
          page: 1,
          pageSize: remoteResultLimit,
          sortField: 'DISPLAY_NAME',
          sortDirection: 'ASCENDING',
          connection,
        }),
      ]).then(([computers, userPage]) => {
        if (searchRequestId.current !== currentRequest) return;
        const failures: string[] = [];
        if (computers.status === 'fulfilled') setDirectoryComputers(computers.value.computers);
        else {
          setDirectoryComputers([]);
          failures.push('directory devices');
        }
        if (userPage.status === 'fulfilled') setUsers(userPage.value.users);
        else {
          setUsers([]);
          failures.push('directory users');
        }
        setRemoteFailures(failures);
      }).finally(() => {
        if (searchRequestId.current === currentRequest) setRemoteLoading(false);
      });
    }, debounceMilliseconds);
    return () => window.clearTimeout(timer);
  }, [adminCredentials, open, query]);

  const results = useMemo(() => [
    ...navigationResults(query).slice(0, 12),
    ...userResults(users),
    ...clientResults(query, directoryComputers, inventoryHosts, securityHosts, savedTargets),
    ...savedTargetResults(query, savedTargets),
  ], [directoryComputers, inventoryHosts, query, savedTargets, securityHosts, users]);

  useEffect(() => {
    setActiveIndex((current) => Math.min(current, Math.max(0, results.length - 1)));
  }, [results.length]);

  const choose = (result: GlobalSearchResult) => {
    onClose();
    navigate(result.to);
  };

  const onInputKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((current) => results.length === 0 ? 0 : (current + 1) % results.length);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((current) => results.length === 0 ? 0 : (current - 1 + results.length) % results.length);
    } else if (event.key === 'Home' && results.length > 0) {
      event.preventDefault();
      setActiveIndex(0);
    } else if (event.key === 'End' && results.length > 0) {
      event.preventDefault();
      setActiveIndex(results.length - 1);
    } else if (event.key === 'Enter' && results[activeIndex]) {
      event.preventDefault();
      choose(results[activeIndex]);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      onClose();
    }
  };

  const trapDialogFocus = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      onClose();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = [...(dialogRef.current?.querySelectorAll<HTMLElement>(
      'input:not([disabled]), button:not([disabled]):not([tabindex="-1"])',
    ) ?? [])];
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable.at(-1)!;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  if (!open) return null;

  return <div
    className="fixed inset-0 z-[70] flex items-start justify-center bg-slate-950/80 px-3 pt-[10vh] backdrop-blur-sm"
    onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}
  >
    <div
      ref={dialogRef}
      role="dialog"
      aria-modal="true"
      aria-labelledby="global-search-title"
      onKeyDown={trapDialogFocus}
      className="flex max-h-[76vh] w-full max-w-2xl flex-col overflow-hidden rounded-xl border border-slate-700 bg-slate-900 shadow-2xl"
    >
      <div className="flex items-center gap-3 border-b border-slate-800 p-3">
        <div className="min-w-0 flex-1">
          <h2 id="global-search-title" className="sr-only">Global search</h2>
          <input
            ref={inputRef}
            type="search"
            role="combobox"
            aria-label="Search navigation, users, clients and saved targets"
            aria-autocomplete="list"
            aria-expanded="true"
            aria-controls="global-search-results"
            aria-activedescendant={results[activeIndex] ? `global-search-result-${activeIndex}` : undefined}
            value={query}
            onChange={(event) => { setQuery(event.target.value); setActiveIndex(0); }}
            onKeyDown={onInputKeyDown}
            placeholder="Search workspaces, users, clients or saved targets…"
            className="w-full bg-transparent text-base text-slate-100 outline-none placeholder:text-slate-500"
          />
        </div>
        <kbd className="hidden rounded border border-slate-700 bg-slate-800 px-1.5 py-0.5 text-[11px] text-muted sm:inline">Ctrl K</kbd>
        <button type="button" aria-label="Close global search" onClick={onClose} className="rounded px-2 py-1 text-slate-400 hover:bg-slate-800 hover:text-white">Esc</button>
      </div>

      <div id="global-search-results" role="listbox" className="min-h-32 overflow-y-auto p-2">
        {categoryOrder.map((category) => {
          const categoryId = category.toLocaleLowerCase().replaceAll(' ', '-');
          const categoryResults = results.map((result, index) => ({ result, index }))
            .filter((entry) => entry.result.category === category);
          if (categoryResults.length === 0) return null;
          return <div key={category} role="group" aria-labelledby={`global-search-group-${categoryId}`} className="mb-2 last:mb-0">
            <div id={`global-search-group-${categoryId}`} className="px-2 py-1 text-[10px] font-semibold uppercase tracking-wider text-slate-500">
              {category}
            </div>
            {categoryResults.map(({ result, index }) => <button
              key={result.id}
              id={`global-search-result-${index}`}
              type="button"
              role="option"
              aria-selected={activeIndex === index}
              tabIndex={-1}
              onMouseMove={() => setActiveIndex(index)}
              onClick={() => choose(result)}
              className={`flex w-full items-center justify-between gap-4 rounded-md px-3 py-2 text-left ${activeIndex === index
                ? 'bg-accent-500/15 text-white'
                : 'text-slate-300 hover:bg-slate-800/70'}`}
            >
              <span className="min-w-0 truncate text-sm font-medium">{result.label}</span>
              <span className="min-w-0 truncate text-xs text-muted">{result.description}</span>
            </button>)}
          </div>;
        })}
        {results.length === 0 && !remoteLoading && <p className="px-3 py-8 text-center text-sm text-slate-400">
          {query.trim().length < remoteQueryMinimum
            ? 'Type at least two characters to search directory users and devices.'
            : 'No matching navigation, users, clients or saved targets.'}
        </p>}
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-800 px-3 py-2 text-xs text-muted" aria-live="polite">
        <span>{remoteLoading ? 'Searching directory…' : `${results.length} results`}</span>
        <span>
          {storedSourcesFailed && 'Some stored client sources are unavailable. '}
          {remoteFailures.length > 0 && `Could not search ${remoteFailures.join(' and ')}.`}
        </span>
      </div>
    </div>
  </div>;
}
