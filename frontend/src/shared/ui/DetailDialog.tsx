import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { Button } from './Button';

interface DetailDialogProps {
  title: string;
  description?: string;
  closeLabel: string;
  onClose(): void;
  children: ReactNode;
  wide?: boolean;
}

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

/** A visible, keyboard-contained detail surface that preserves the list position behind it. */
export function DetailDialog({
  title,
  description,
  closeLabel,
  onClose,
  children,
  wide = false,
}: DetailDialogProps) {
  const titleId = useId();
  const descriptionId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const returnFocusRef = useRef<HTMLElement | null>(
    document.activeElement instanceof HTMLElement ? document.activeElement : null,
  );

  useEffect(() => {
    dialogRef.current?.focus({ preventScroll: true });
    return () => {
      const returnTarget = returnFocusRef.current;
      if (returnTarget?.isConnected) returnTarget.focus({ preventScroll: true });
    };
  }, []);

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      onClose();
      return;
    }
    if (event.key !== 'Tab') return;

    const focusable = [...(dialogRef.current?.querySelectorAll<HTMLElement>(focusableSelector) ?? [])];
    if (focusable.length === 0) {
      event.preventDefault();
      dialogRef.current?.focus();
      return;
    }
    const first = focusable[0];
    const last = focusable.at(-1)!;
    if (event.shiftKey && (document.activeElement === first || document.activeElement === dialogRef.current)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-950/80 p-2 backdrop-blur-[1px] sm:p-4"
      onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}
    >
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
        onKeyDown={handleKeyDown}
        className={`flex max-h-[calc(100dvh-1rem)] w-full flex-col overflow-hidden rounded-xl border border-slate-700 bg-slate-900 shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-accent-400 sm:max-h-[calc(100dvh-2rem)] ${wide ? 'max-w-6xl' : 'max-w-3xl'}`}
      >
        <header className="flex shrink-0 flex-wrap items-start justify-between gap-3 border-b border-slate-800 bg-slate-900 px-4 py-3">
          <div className="min-w-0">
            <h2 id={titleId} className="break-words text-lg font-semibold text-slate-100">{title}</h2>
            {description && <p id={descriptionId} className="mt-1 break-words text-sm text-slate-300">{description}</p>}
          </div>
          <Button variant="ghost" aria-label={closeLabel} onClick={onClose}>Close</Button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto p-4">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
