import { useEffect, useId, useMemo, useRef, useState, type FocusEvent, type KeyboardEvent } from 'react';
import { controlClass } from '../../shared/ui/Input';
import { semanticStatusPresentation } from '../../shared/ui/SemanticStatusBadge';
import { clientKey, type ClientEntry } from './clients';
import { ClientSemanticStatus, snapshotAvailabilityStatus } from './clientStatus';

const maximumVisibleMatches = 50;

interface ClientComparePickerProps {
  label: string;
  ariaLabel: string;
  clients: readonly ClientEntry[];
  recentHosts?: readonly string[];
  value: string;
  onChange: (host: string) => void;
  disabled?: boolean;
}

function matchesQuery(client: ClientEntry, query: string) {
  const needle = query.trim().toLocaleLowerCase();
  return needle === ''
    || client.name.toLocaleLowerCase().includes(needle)
    || client.host.toLocaleLowerCase().includes(needle);
}

function inventoryTimestamp(client: ClientEntry) {
  return client.capturedAtUtc ? new Date(client.capturedAtUtc).toLocaleString() : null;
}

function securityTimestamp(client: ClientEntry) {
  return client.securityCompletedAtUtc
    ? new Date(client.securityCompletedAtUtc).toLocaleString()
    : null;
}

export function ClientComparePicker({
  label,
  ariaLabel,
  clients,
  recentHosts = [],
  value,
  onChange,
  disabled = false,
}: ClientComparePickerProps) {
  const inputId = useId();
  const listboxId = useId();
  const wrapperRef = useRef<HTMLLabelElement>(null);
  const synchronizedValue = useRef(value);
  const selectedClient = clients.find((client) => client.host === value);
  const [query, setQuery] = useState(selectedClient?.name ?? '');
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);

  useEffect(() => {
    if (value === synchronizedValue.current) return;
    synchronizedValue.current = value;
    setQuery(clients.find((client) => client.host === value)?.name ?? '');
  }, [clients, value]);

  useEffect(() => {
    if (disabled) setOpen(false);
  }, [disabled]);

  const matches = useMemo(
    () => clients.filter((client) => matchesQuery(client, query)),
    [clients, query],
  );
  const recentClients = useMemo(() => {
    if (query.trim() !== '') return [];
    const byKey = new Map(clients.map((client) => [client.key, client]));
    const seen = new Set<string>();
    return recentHosts.flatMap((host) => {
      const key = clientKey(host);
      const client = byKey.get(key);
      if (!client || seen.has(key)) return [];
      seen.add(key);
      return [client];
    });
  }, [clients, query, recentHosts]);
  const orderedMatches = useMemo(() => {
    if (recentClients.length === 0) return matches;
    const recentKeys = new Set(recentClients.map((client) => client.key));
    return [...recentClients, ...matches.filter((client) => !recentKeys.has(client.key))];
  }, [matches, recentClients]);
  const visibleMatches = orderedMatches.slice(0, maximumVisibleMatches);
  const visibleRecentMatches = visibleMatches.slice(0, recentClients.length);
  const visibleOtherMatches = visibleMatches.slice(recentClients.length);

  const selectClient = (client: ClientEntry) => {
    synchronizedValue.current = client.host;
    setQuery(client.name);
    setOpen(false);
    setActiveIndex(-1);
    onChange(client.host);
  };

  const editQuery = (nextQuery: string) => {
    setQuery(nextQuery);
    setOpen(true);
    setActiveIndex(-1);
    if (value !== '') {
      synchronizedValue.current = '';
      onChange('');
    }
  };

  const closeAndRestoreSelection = () => {
    setOpen(false);
    setActiveIndex(-1);
    setQuery(clients.find((client) => client.host === synchronizedValue.current)?.name ?? '');
  };

  const handleBlur = (event: FocusEvent<HTMLLabelElement>) => {
    if (!wrapperRef.current?.contains(event.relatedTarget as Node | null)) {
      closeAndRestoreSelection();
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => Math.min(current + 1, visibleMatches.length - 1));
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((current) => current <= 0 ? visibleMatches.length - 1 : current - 1);
      return;
    }
    if (event.key === 'Enter' && open && activeIndex >= 0 && visibleMatches[activeIndex]) {
      event.preventDefault();
      selectClient(visibleMatches[activeIndex]);
      return;
    }
    if (event.key === 'Escape' && open) {
      event.preventDefault();
      closeAndRestoreSelection();
    }
  };

  const matchSummary = matches.length > maximumVisibleMatches
    ? `Showing ${maximumVisibleMatches} of ${matches.length} matches`
    : `${matches.length} ${matches.length === 1 ? 'match' : 'matches'}`;

  const renderOption = (client: ClientEntry, index: number) => {
    const inventoryCaptured = inventoryTimestamp(client);
    const securityCaptured = securityTimestamp(client);
    const inventoryStatus = snapshotAvailabilityStatus(client.scanned);
    const securityStatus = snapshotAvailabilityStatus(client.securityScanned);
    return (
      <span
        id={`${listboxId}-${client.key}`}
        key={client.key}
        role="option"
        aria-selected={client.host === value}
        className={`flex cursor-pointer items-center justify-between gap-3 rounded px-2.5 py-2 text-left ${
          index === activeIndex ? 'bg-accent-600/30 text-slate-100' : 'text-slate-200 hover:bg-slate-800'
        }`}
        onMouseDown={(event) => {
          event.preventDefault();
          selectClient(client);
        }}
        onMouseMove={() => setActiveIndex(index)}
      >
        <span className="min-w-0">
          <span className="block truncate text-sm font-medium">{client.name}</span>
          {client.host.toLocaleLowerCase() !== client.name.toLocaleLowerCase() && (
            <span className="block truncate text-xs text-slate-400">{client.host}</span>
          )}
        </span>
        <span className="flex shrink-0 flex-col items-end gap-1">
          <span className="flex flex-col items-end gap-0.5">
            <span
              role="group"
              aria-label={`Inventory: ${semanticStatusPresentation(inventoryStatus).label}`}
              className="flex items-center gap-1.5"
            >
              <span className="text-[11px] font-medium text-slate-400">Inventory</span>
              <ClientSemanticStatus status={inventoryStatus} />
            </span>
            {inventoryCaptured && (
              <span className="text-[11px] text-slate-400">{inventoryCaptured}</span>
            )}
          </span>
          <span className="flex flex-col items-end gap-0.5">
            <span
              role="group"
              aria-label={`Security: ${semanticStatusPresentation(securityStatus).label}`}
              className="flex items-center gap-1.5"
            >
              <span className="text-[11px] font-medium text-slate-400">Security</span>
              <ClientSemanticStatus status={securityStatus} />
            </span>
            {securityCaptured && (
              <span className="text-[11px] text-slate-400">{securityCaptured}</span>
            )}
          </span>
        </span>
      </span>
    );
  };

  return (
    <label
      ref={wrapperRef}
      htmlFor={inputId}
      className="flex items-center gap-2 text-sm text-slate-400"
      onBlur={handleBlur}
    >
      <span className="font-medium">{label}</span>
      <span className="relative block w-72 max-w-full">
        <input
          id={inputId}
          type="search"
          role="combobox"
          aria-label={ariaLabel}
          aria-autocomplete="list"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-activedescendant={activeIndex >= 0 ? `${listboxId}-${visibleMatches[activeIndex]?.key}` : undefined}
          autoComplete="off"
          placeholder="Search clients…"
          className={`${controlClass} w-full`}
          value={query}
          disabled={disabled}
          onChange={(event) => editQuery(event.target.value)}
          onFocus={(event) => {
            event.currentTarget.select();
            setOpen(true);
            setActiveIndex(-1);
          }}
          onClick={() => setOpen(true)}
          onKeyDown={handleKeyDown}
        />

        {open && !disabled && (
          <span
            id={listboxId}
            role="listbox"
            aria-label={`${ariaLabel} matches`}
            className="absolute left-0 top-full z-30 mt-1 flex max-h-80 w-full min-w-80 flex-col overflow-y-auto rounded-md border border-slate-700 bg-slate-900 p-1 shadow-xl"
          >
            {visibleRecentMatches.length > 0 ? (
              <>
                <span role="group" aria-label="Recently compared" className="flex flex-col">
                  <span aria-hidden="true" className="px-2.5 pb-1 pt-1.5 text-[11px] font-medium uppercase tracking-wide text-slate-400">
                    Recently compared
                  </span>
                  {visibleRecentMatches.map((client, index) => renderOption(client, index))}
                </span>
                {visibleOtherMatches.length > 0 && (
                  <span role="group" aria-label="All clients" className="flex flex-col border-t border-slate-800 pt-1">
                    <span aria-hidden="true" className="px-2.5 pb-1 pt-1 text-[11px] font-medium uppercase tracking-wide text-slate-400">
                      All clients
                    </span>
                    {visibleOtherMatches.map((client, index) => renderOption(client, recentClients.length + index))}
                  </span>
                )}
              </>
            ) : (
              visibleMatches.map((client, index) => renderOption(client, index))
            )}
            {visibleMatches.length === 0 && (
              <span className="px-2.5 py-3 text-sm text-slate-400">No matching clients.</span>
            )}
            <span className="sticky bottom-0 border-t border-slate-800 bg-slate-900 px-2.5 py-1.5 text-xs text-slate-400">
              {matchSummary}
            </span>
          </span>
        )}
      </span>
    </label>
  );
}
