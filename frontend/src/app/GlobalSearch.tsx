import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTargets } from '../shared/targets/TargetContext';
import { useOptionalWorkingSet } from '../shared/objects/WorkingSetContext';
import { workingSetSearch } from './workingSetSearch';
import { navigationResults, savedTargetResults, type GlobalSearchCategory, type GlobalSearchResult } from './searchResults';

const categoryOrder: readonly GlobalSearchCategory[] = ['Navigation', 'Users', 'Devices', 'Groups', 'Saved targets'];
interface GlobalSearchProps {
  open: boolean;
  onClose(): void;
}

export function GlobalSearch({ open, onClose }: GlobalSearchProps) {
  const navigate = useNavigate();
  const { savedTargets } = useTargets();
  const workspace = useOptionalWorkingSet();
  const inputRef = useRef<HTMLInputElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);

  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  useEffect(() => {
    if (!open) return;
    setQuery(''); setActiveIndex(0);
    requestAnimationFrame(() => inputRef.current?.focus());
  }, [open]);

  const results = useMemo(() => [
    ...navigationResults(query).slice(0, 12),
    ...workingSetSearch(query, workspace?.displayed ?? null),
    ...savedTargetResults(query, savedTargets.filter(target => target.role !== 'Client')),
  ], [query, savedTargets, workspace?.displayed]);
  useEffect(() => {
    setActiveIndex((current) => Math.min(current, Math.max(0, results.length - 1)));
  }, [results.length]);

  const choose = (result: GlobalSearchResult) => {
    onClose();
    navigate(result.to, { state: { directoryEndpoint: workspace?.directoryEndpoint } });
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
            aria-label="Search navigation, users, devices, groups and saved targets"
            aria-autocomplete="list"
            aria-expanded="true"
            aria-controls="global-search-results"
            aria-activedescendant={results[activeIndex] ? `global-search-result-${activeIndex}` : undefined}
            value={query}
            onChange={(event) => { setQuery(event.target.value); setActiveIndex(0); }}
            onKeyDown={onInputKeyDown}
            placeholder="Search loaded users, devices, groups or workspaces…"
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
        {results.length === 0 && <p className="px-3 py-8 text-center text-sm text-slate-400">No matching loaded objects or workspaces. Load another source page from an object workspace to extend this search.</p>}      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-800 px-3 py-2 text-xs text-muted" aria-live="polite">
        <span>{results.length} results · up to six per object kind</span>
        <span>{workspace?.displayed ? `${workspace.displayed.loadedSourceRecords} loaded source records${workspace.displayed.limited ? ' · Limited working set' : ''}` : 'Working set not loaded'}</span>
        <span>Search uses the displayed working set; missing results do not prove absence.</span>
        {workspace?.hasUpdates && <button type="button" onClick={workspace.applyUpdates} className="text-accent-400 underline">Apply available source updates</button>}
        {workspace?.error && <span role="alert">{workspace.error}</span>}      </div>
    </div>
  </div>;
}
