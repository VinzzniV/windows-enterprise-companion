import type {
  EmployeeStatus,
  EmployeeSummary,
  LifecycleCaseType,
  LifecycleCaseStatus,
  LifecycleDepartment,
  LifecycleTask,
  LifecycleTaskArea,
  LifecycleTaskStatus,
} from '../../shared/api-types';

type BadgeTone = 'ok' | 'warn' | 'fail' | 'info' | 'neutral' | 'accent';

export const employeeStatusLabels: Record<EmployeeStatus, string> = {
  PLANNED: 'Planned',
  ONBOARDING: 'Onboarding',
  ACTIVE: 'Active',
  CHANGING: 'Changing',
  OFFBOARDING: 'Offboarding',
  DISABLED: 'Disabled',
};

export function employeeStatusTone(status: EmployeeStatus): BadgeTone {
  switch (status) {
    case 'ACTIVE':
      return 'ok';
    case 'PLANNED':
      return 'info';
    case 'ONBOARDING':
      return 'accent';
    case 'CHANGING':
    case 'OFFBOARDING':
      return 'warn';
    case 'DISABLED':
      return 'neutral';
  }
}

export const caseTypeLabels: Record<LifecycleCaseType, string> = {
  ONBOARDING: 'Onboarding',
  OFFBOARDING: 'Offboarding',
  CHANGE: 'Change',
};

export const caseStatusLabels: Record<LifecycleCaseStatus, string> = {
  ACTIVE: 'Active',
  COMPLETED: 'Completed',
  CANCELLED: 'Cancelled',
};

export function caseStatusTone(status: LifecycleCaseStatus): BadgeTone {
  switch (status) {
    case 'ACTIVE':
      return 'accent';
    case 'COMPLETED':
      return 'ok';
    case 'CANCELLED':
      return 'neutral';
  }
}

export const taskStatusLabels: Record<LifecycleTaskStatus, string> = {
  OPEN: 'Open',
  IN_PROGRESS: 'In progress',
  BLOCKED: 'Blocked',
  DONE: 'Done',
  SKIPPED: 'Skipped',
};

export const taskStatusOptions: LifecycleTaskStatus[] = ['OPEN', 'IN_PROGRESS', 'BLOCKED', 'DONE', 'SKIPPED'];

export function taskStatusTone(status: LifecycleTaskStatus): BadgeTone {
  switch (status) {
    case 'OPEN':
      return 'info';
    case 'IN_PROGRESS':
      return 'accent';
    case 'BLOCKED':
      return 'fail';
    case 'DONE':
      return 'ok';
    case 'SKIPPED':
      return 'neutral';
  }
}

export const taskAreaLabels: Record<LifecycleTaskArea, string> = {
  GENERAL: 'General',
  ACCOUNT: 'Account',
  HARDWARE: 'Hardware',
  SOFTWARE: 'Software',
  PERMISSIONS: 'Permissions',
  MAILBOX: 'Mailbox',
};

export function isTerminalTaskStatus(status: LifecycleTaskStatus): boolean {
  return status === 'DONE' || status === 'SKIPPED';
}

/** Display-only mirror of backend LifecycleRules.CanStartCase — the backend still validates. */
export function startableCaseTypes(status: EmployeeStatus): LifecycleCaseType[] {
  switch (status) {
    case 'PLANNED':
    case 'DISABLED':
      return ['ONBOARDING'];
    case 'ACTIVE':
      return ['OFFBOARDING', 'CHANGE'];
    default:
      return [];
  }
}

export function canCompleteCase(tasks: LifecycleTask[]): boolean {
  return tasks.every((task) => isTerminalTaskStatus(task.status));
}

export function filterEmployees(
  employees: EmployeeSummary[],
  search: string,
  status: EmployeeStatus | 'ALL',
): EmployeeSummary[] {
  const needle = search.trim().toLowerCase();
  return employees.filter((employee) => {
    if (status !== 'ALL' && employee.status !== status) {
      return false;
    }
    if (needle.length === 0) {
      return true;
    }
    return [
      employee.firstName,
      employee.lastName,
      `${employee.firstName} ${employee.lastName}`,
      employee.employeeNumber,
      employee.department,
      employee.samAccountName,
    ].some((value) => value?.toLowerCase().includes(needle));
  });
}

export interface LifecycleOverview {
  onboardings: number;
  offboardings: number;
  changes: number;
  openTasks: number;
  overdueTasks: number;
}

export function computeOverview(employees: EmployeeSummary[]): LifecycleOverview {
  return {
    onboardings: employees.filter((employee) => employee.activeCaseType === 'ONBOARDING').length,
    offboardings: employees.filter((employee) => employee.activeCaseType === 'OFFBOARDING').length,
    changes: employees.filter((employee) => employee.activeCaseType === 'CHANGE').length,
    openTasks: employees.reduce((sum, employee) => sum + employee.openTaskCount, 0),
    overdueTasks: employees.reduce((sum, employee) => sum + employee.overdueTaskCount, 0),
  };
}

/** Date-only values stay in their wire format (yyyy-MM-dd); null becomes a dash. */
export function formatDate(value: string | null): string {
  return value ?? '—';
}

/** "Müller-Lüdenscheidt" → "mueller-luedenscheidt": lowercase, umlauts transliterated, accents stripped. */
export function normalizeNamePart(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replaceAll('ä', 'ae')
    .replaceAll('ö', 'oe')
    .replaceAll('ü', 'ue')
    .replaceAll('ß', 'ss')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9-]/g, '');
}

export function deriveSamAccountName(firstName: string, lastName: string): string {
  const first = normalizeNamePart(firstName);
  const last = normalizeNamePart(lastName);
  return first && last ? `${first}.${last}` : '';
}

/** "OU=IT,DC=firma,DC=local" → "firma.local" */
export function domainFromDn(distinguishedName: string): string {
  return distinguishedName
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part.toUpperCase().startsWith('DC='))
    .map((part) => part.slice(3))
    .join('.');
}

export function deriveUserPrincipalName(samAccountName: string, ouDistinguishedName: string): string {
  if (!samAccountName) {
    return '';
  }
  const domain = ouDistinguishedName ? domainFromDn(ouDistinguishedName) : '';
  return domain ? `${samAccountName}@${domain}` : '';
}

export function deriveDistinguishedName(firstName: string, lastName: string, ouDistinguishedName: string): string {
  const first = firstName.trim();
  const last = lastName.trim();
  return first && last && ouDistinguishedName ? `CN=${first} ${last},${ouDistinguishedName}` : '';
}

/** The subset of the employee form the AD derivations read and write. */
export interface AdDerivableFields {
  firstName: string;
  lastName: string;
  department: string;
  manager: string;
  samAccountName: string;
  userPrincipalName: string;
  distinguishedName: string;
}

/**
 * Prefills manager, sAMAccountName, UPN and DN from names + department catalog.
 * A field is only overwritten while it is empty or still equals its previous
 * derivation — manual edits always win.
 */
export function withDerivedAdFields<T extends AdDerivableFields>(
  previous: T,
  next: T,
  departments: LifecycleDepartment[],
): T {
  const departmentOf = (name: string) => departments.find((department) => department.name === name);
  const previousDepartment = departmentOf(previous.department);
  const nextDepartment = departmentOf(next.department);
  const result = { ...next };

  const setIfUntouched = (
    field: 'manager' | 'samAccountName' | 'userPrincipalName' | 'distinguishedName',
    previousDerived: string,
    nextDerived: string,
  ) => {
    if (result[field] === '' || result[field] === previousDerived) {
      result[field] = nextDerived;
    }
  };

  if (nextDepartment) {
    setIfUntouched('manager', previousDepartment?.managerName ?? '', nextDepartment.managerName ?? '');
  }
  setIfUntouched(
    'samAccountName',
    deriveSamAccountName(previous.firstName, previous.lastName),
    deriveSamAccountName(next.firstName, next.lastName),
  );
  setIfUntouched(
    'userPrincipalName',
    deriveUserPrincipalName(previous.samAccountName, previousDepartment?.ouDistinguishedName ?? ''),
    deriveUserPrincipalName(result.samAccountName, nextDepartment?.ouDistinguishedName ?? ''),
  );
  setIfUntouched(
    'distinguishedName',
    deriveDistinguishedName(previous.firstName, previous.lastName, previousDepartment?.ouDistinguishedName ?? ''),
    deriveDistinguishedName(next.firstName, next.lastName, nextDepartment?.ouDistinguishedName ?? ''),
  );
  return result;
}

export function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}
