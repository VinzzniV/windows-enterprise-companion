import { Input } from '../ui/Input';

export interface CredentialValues {
  userName: string;
  domain: string;
  password: string;
}

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
