import type { EmployeeDetails, EmployeeInput, LifecycleDepartment } from '../../shared/api-types';
import { Button } from '../../shared/ui/Button';
import { Field } from '../../shared/ui/Field';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { ErrorState } from '../../shared/ui/States';
import { withDerivedAdFields } from './lifecycle';

export interface EmployeeFormValue {
  firstName: string;
  lastName: string;
  email: string;
  employeeNumber: string;
  department: string;
  title: string;
  manager: string;
  samAccountName: string;
  userPrincipalName: string;
  distinguishedName: string;
  entryDate: string;
  notes: string;
}

export const emptyEmployeeForm: EmployeeFormValue = {
  firstName: '',
  lastName: '',
  email: '',
  employeeNumber: '',
  department: '',
  title: '',
  manager: '',
  samAccountName: '',
  userPrincipalName: '',
  distinguishedName: '',
  entryDate: '',
  notes: '',
};

export function formValueFromEmployee(employee: EmployeeDetails): EmployeeFormValue {
  return {
    firstName: employee.firstName,
    lastName: employee.lastName,
    email: employee.email ?? '',
    employeeNumber: employee.employeeNumber ?? '',
    department: employee.department ?? '',
    title: employee.title ?? '',
    manager: employee.manager ?? '',
    samAccountName: employee.samAccountName ?? '',
    userPrincipalName: employee.userPrincipalName ?? '',
    distinguishedName: employee.distinguishedName ?? '',
    entryDate: employee.entryDate ?? '',
    notes: employee.notes ?? '',
  };
}

export function toEmployeeInput(value: EmployeeFormValue): EmployeeInput {
  const orNull = (text: string) => (text.trim().length === 0 ? null : text.trim());
  return {
    firstName: value.firstName.trim(),
    lastName: value.lastName.trim(),
    email: orNull(value.email),
    employeeNumber: orNull(value.employeeNumber),
    department: orNull(value.department),
    title: orNull(value.title),
    manager: orNull(value.manager),
    samAccountName: orNull(value.samAccountName),
    userPrincipalName: orNull(value.userPrincipalName),
    distinguishedName: orNull(value.distinguishedName),
    entryDate: orNull(value.entryDate),
    notes: orNull(value.notes),
  };
}

interface TextFieldProps {
  label: string;
  field: keyof EmployeeFormValue;
  value: EmployeeFormValue;
  onChange: (value: EmployeeFormValue) => void;
  type?: string;
  placeholder?: string;
}

function TextField({ label, field, value, onChange, type = 'text', placeholder }: TextFieldProps) {
  return (
    <Field label={label}>
      {(id) => (
        <Input
          id={id}
          type={type}
          value={value[field]}
          placeholder={placeholder}
          onChange={(event) => onChange({ ...value, [field]: event.target.value })}
        />
      )}
    </Field>
  );
}

interface EmployeeFormProps {
  value: EmployeeFormValue;
  onChange: (value: EmployeeFormValue) => void;
  onSubmit: () => void;
  onCancel?: () => void;
  submitLabel: string;
  busy: boolean;
  error: string | null;
  departments?: LifecycleDepartment[];
}

/** Shared create/edit form; AD fields are preparation data only — WEC never writes to AD. */
export function EmployeeForm({
  value,
  onChange,
  onSubmit,
  onCancel,
  submitLabel,
  busy,
  error,
  departments = [],
}: EmployeeFormProps) {
  const valid = value.firstName.trim().length > 0 && value.lastName.trim().length > 0;
  // Every change runs through the derivation so manager/sAM/UPN/DN prefill from
  // names + department catalog — manual edits are never overwritten.
  const change = (next: EmployeeFormValue) => onChange(withDerivedAdFields(value, next, departments));
  const departmentOptions = [
    ...departments.map((department) => department.name),
    ...(value.department !== '' && !departments.some((d) => d.name === value.department) ? [value.department] : []),
  ];
  return (
    <div className="flex flex-col gap-3">
      <div className="grid gap-3 sm:grid-cols-3">
        <TextField label="First name" field="firstName" value={value} onChange={change} />
        <TextField label="Last name" field="lastName" value={value} onChange={change} />
        <TextField label="Employee no." field="employeeNumber" value={value} onChange={change} />
        <TextField label="Email" field="email" value={value} onChange={change} type="email" />
        {departments.length > 0 ? (
          <Field label="Department">
            {(id) => (
              <Select
                id={id}
                value={value.department}
                onChange={(event) => change({ ...value, department: event.target.value })}
              >
                <option value="">—</option>
                {departmentOptions.map((name) => (
                  <option key={name} value={name}>
                    {name}
                  </option>
                ))}
              </Select>
            )}
          </Field>
        ) : (
          <TextField label="Department" field="department" value={value} onChange={change} />
        )}
        <TextField label="Title" field="title" value={value} onChange={change} />
        <TextField label="Manager" field="manager" value={value} onChange={change} />
        <TextField label="Entry date" field="entryDate" value={value} onChange={change} type="date" />
      </div>
      <div className="grid gap-3 sm:grid-cols-3">
        <TextField label="AD sAMAccountName" field="samAccountName" value={value} onChange={change} placeholder="m.muster" />
        <TextField label="AD userPrincipalName" field="userPrincipalName" value={value} onChange={change} placeholder="m.muster@firma.local" />
        <TextField label="AD distinguishedName" field="distinguishedName" value={value} onChange={change} placeholder="CN=..." />
      </div>
      <TextField label="Notes" field="notes" value={value} onChange={change} />
      {error && <ErrorState title="Saving failed" message={error} />}
      <div className="flex gap-2">
        <Button variant="primary" onClick={onSubmit} disabled={busy || !valid}>
          {busy ? 'Saving…' : submitLabel}
        </Button>
        {onCancel && (
          <Button onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
        )}
      </div>
    </div>
  );
}
