import { useState } from 'react';
import { Button } from './Button';

interface ConfirmDangerActionProps {
  subject: string;
  triggerLabel: string;
  description: string;
  onConfirm: () => void;
  disabled?: boolean;
}

/**
 * Local two-step guard for destructive actions. The destructive callback is
 * unreachable from the first click; the user must confirm the named subject.
 */
export function ConfirmDangerAction({
  subject,
  triggerLabel,
  description,
  onConfirm,
  disabled = false,
}: ConfirmDangerActionProps) {
  const [confirming, setConfirming] = useState(false);

  if (!confirming) {
    return <Button
      variant="danger"
      aria-label={`Remove ${subject}`}
      onClick={() => setConfirming(true)}
      disabled={disabled}
    >
      {triggerLabel}
    </Button>;
  }

  return <div
    role="group"
    aria-label={`Confirm removal of ${subject}`}
    className="min-w-64 rounded border border-fail-700/70 bg-fail-950/30 p-3"
  >
    <p className="mb-3 text-sm text-fail-100">{description}</p>
    <div className="flex flex-wrap gap-2">
      <Button
        variant="danger"
        aria-label={`Confirm removal of ${subject}`}
        onClick={onConfirm}
        disabled={disabled}
      >
        Remove credential
      </Button>
      <Button
        variant="secondary"
        aria-label={`Cancel removing ${subject}`}
        onClick={() => setConfirming(false)}
        disabled={disabled}
        autoFocus
      >
        Cancel
      </Button>
    </div>
  </div>;
}
