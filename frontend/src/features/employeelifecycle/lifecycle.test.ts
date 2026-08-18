import { describe, expect, it } from 'vitest';
import type { EmployeeSummary, LifecycleDepartment, LifecycleTask } from '../../shared/api-types';
import {
  canCompleteCase,
  computeOverview,
  deriveDistinguishedName,
  deriveSamAccountName,
  deriveUserPrincipalName,
  domainFromDn,
  filterEmployees,
  normalizeNamePart,
  startableCaseTypes,
  withDerivedAdFields,
  type AdDerivableFields,
} from './lifecycle';

function employee(overrides: Partial<EmployeeSummary>): EmployeeSummary {
  return {
    id: 1,
    firstName: 'Max',
    lastName: 'Muster',
    employeeNumber: null,
    department: null,
    title: null,
    samAccountName: null,
    status: 'ACTIVE',
    entryDate: null,
    exitDate: null,
    activeCaseId: null,
    activeCaseType: null,
    openTaskCount: 0,
    overdueTaskCount: 0,
    ...overrides,
  };
}

function task(status: LifecycleTask['status']): LifecycleTask {
  return {
    id: 1,
    caseId: 1,
    title: 'AD-Benutzer anlegen',
    area: 'ACCOUNT',
    status,
    dueDate: null,
    assignee: null,
    notes: '',
    sortOrder: 0,
    isOverdue: false,
  };
}

describe('startableCaseTypes', () => {
  it('mirrors the backend transition rules', () => {
    expect(startableCaseTypes('PLANNED')).toEqual(['ONBOARDING']);
    expect(startableCaseTypes('DISABLED')).toEqual(['ONBOARDING']);
    expect(startableCaseTypes('ACTIVE')).toEqual(['OFFBOARDING', 'CHANGE']);
    expect(startableCaseTypes('ONBOARDING')).toEqual([]);
    expect(startableCaseTypes('OFFBOARDING')).toEqual([]);
    expect(startableCaseTypes('CHANGING')).toEqual([]);
  });
});

describe('canCompleteCase', () => {
  it('requires every task to be done or skipped', () => {
    expect(canCompleteCase([task('DONE'), task('SKIPPED')])).toBe(true);
    expect(canCompleteCase([task('DONE'), task('OPEN')])).toBe(false);
    expect(canCompleteCase([task('IN_PROGRESS')])).toBe(false);
    expect(canCompleteCase([task('BLOCKED')])).toBe(false);
    expect(canCompleteCase([])).toBe(true);
  });
});

describe('filterEmployees', () => {
  const employees = [
    employee({ id: 1, firstName: 'Max', lastName: 'Muster', department: 'IT', status: 'ACTIVE' }),
    employee({ id: 2, firstName: 'Erika', lastName: 'Beispiel', samAccountName: 'e.beispiel', status: 'PLANNED' }),
  ];

  it('matches name, account and department case-insensitively', () => {
    expect(filterEmployees(employees, 'muster', 'ALL')).toHaveLength(1);
    expect(filterEmployees(employees, 'E.BEISPIEL', 'ALL')).toHaveLength(1);
    expect(filterEmployees(employees, 'it', 'ALL').map((e) => e.id)).toEqual([1]);
    expect(filterEmployees(employees, 'max muster', 'ALL')).toHaveLength(1);
  });

  it('filters by status', () => {
    expect(filterEmployees(employees, '', 'PLANNED').map((e) => e.id)).toEqual([2]);
    expect(filterEmployees(employees, 'muster', 'PLANNED')).toHaveLength(0);
  });
});

describe('AD derivations', () => {
  const departments: LifecycleDepartment[] = [
    { id: 1, name: 'IT', managerName: 'Chef Admin', ouDistinguishedName: 'OU=IT,DC=firma,DC=local' },
    { id: 2, name: 'HR', managerName: null, ouDistinguishedName: null },
  ];

  const empty: AdDerivableFields = {
    firstName: '',
    lastName: '',
    department: '',
    manager: '',
    samAccountName: '',
    userPrincipalName: '',
    distinguishedName: '',
  };

  it('normalizes German names for account derivation', () => {
    expect(normalizeNamePart('Müller-Lüdenscheidt')).toBe('mueller-luedenscheidt');
    expect(normalizeNamePart('José')).toBe('jose');
    expect(deriveSamAccountName('Max', 'Müller')).toBe('max.mueller');
    expect(deriveSamAccountName('', 'Müller')).toBe('');
  });

  it('derives the UPN domain from the OU DC components', () => {
    expect(domainFromDn('OU=IT,DC=firma,DC=local')).toBe('firma.local');
    expect(deriveUserPrincipalName('max.mueller', 'OU=IT,DC=firma,DC=local')).toBe('max.mueller@firma.local');
    expect(deriveUserPrincipalName('max.mueller', '')).toBe('');
  });

  it('derives the DN from name and OU', () => {
    expect(deriveDistinguishedName('Max', 'Müller', 'OU=IT,DC=firma,DC=local')).toBe(
      'CN=Max Müller,OU=IT,DC=firma,DC=local',
    );
  });

  it('prefills manager, sam, upn and dn when a department is selected', () => {
    const withNames = withDerivedAdFields(empty, { ...empty, firstName: 'Max', lastName: 'Muster' }, departments);
    expect(withNames.samAccountName).toBe('max.muster');

    const withDepartment = withDerivedAdFields(withNames, { ...withNames, department: 'IT' }, departments);
    expect(withDepartment.manager).toBe('Chef Admin');
    expect(withDepartment.userPrincipalName).toBe('max.muster@firma.local');
    expect(withDepartment.distinguishedName).toBe('CN=Max Muster,OU=IT,DC=firma,DC=local');
  });

  it('never overwrites manually edited fields', () => {
    const manual = { ...empty, firstName: 'Max', lastName: 'Muster', samAccountName: 'mmuster' };
    const result = withDerivedAdFields(manual, { ...manual, department: 'IT' }, departments);
    expect(result.samAccountName).toBe('mmuster');
    expect(result.userPrincipalName).toBe('mmuster@firma.local');

    const editedManager = { ...result, manager: 'Jemand Anderes' };
    const afterRename = withDerivedAdFields(
      editedManager,
      { ...editedManager, firstName: 'Moritz' },
      departments,
    );
    expect(afterRename.manager).toBe('Jemand Anderes');
    expect(afterRename.samAccountName).toBe('mmuster');
  });
});

describe('computeOverview', () => {
  it('aggregates case types and task counts', () => {
    const overview = computeOverview([
      employee({ activeCaseType: 'ONBOARDING', openTaskCount: 5, overdueTaskCount: 2 }),
      employee({ activeCaseType: 'OFFBOARDING', openTaskCount: 3, overdueTaskCount: 0 }),
      employee({ activeCaseType: 'CHANGE', openTaskCount: 1, overdueTaskCount: 1 }),
      employee({}),
    ]);

    expect(overview).toEqual({
      onboardings: 1,
      offboardings: 1,
      changes: 1,
      openTasks: 9,
      overdueTasks: 3,
    });
  });
});
