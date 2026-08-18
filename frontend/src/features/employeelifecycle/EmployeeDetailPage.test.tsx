import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { EmployeeDetailsResult, LifecycleAuditListResult } from '../../shared/api-types';
import { EmployeeDetailPage } from './EmployeeDetailPage';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', () => ({
  invoke: invokeMock,
  BridgeInvokeError: class extends Error {},
}));

const details: EmployeeDetailsResult = {
  employee: {
    id: 1,
    firstName: 'Max',
    lastName: 'Muster',
    email: 'max@firma.local',
    employeeNumber: '1001',
    department: 'IT',
    title: 'Admin',
    manager: 'Chef',
    samAccountName: 'm.muster',
    userPrincipalName: 'm.muster@firma.local',
    distinguishedName: null,
    status: 'ONBOARDING',
    entryDate: '2026-08-01',
    exitDate: null,
    notes: null,
    createdUtc: '2026-07-24T12:00:00Z',
    updatedUtc: '2026-07-24T12:00:00Z',
  },
  cases: [
    {
      id: 10,
      employeeId: 1,
      type: 'ONBOARDING',
      status: 'ACTIVE',
      effectiveDate: '2026-08-01',
      note: null,
      cancelReason: null,
      createdUtc: '2026-07-24T12:00:00Z',
      closedUtc: null,
      tasks: [
        {
          id: 100,
          caseId: 10,
          title: 'AD-Benutzer anlegen',
          area: 'ACCOUNT',
          status: 'OPEN',
          dueDate: '2026-07-29',
          assignee: null,
          notes: '',
          sortOrder: 0,
          isOverdue: false,
        },
        {
          id: 101,
          caseId: 10,
          title: 'Mailbox anlegen',
          area: 'MAILBOX',
          status: 'DONE',
          dueDate: '2026-07-29',
          assignee: 'vinzent',
          notes: '',
          sortOrder: 1,
          isOverdue: false,
        },
      ],
    },
  ],
};

const audit: LifecycleAuditListResult = {
  entries: [
    {
      id: 1,
      timestampUtc: '2026-07-24T12:00:00Z',
      userName: 'vinzent',
      employeeId: 1,
      caseId: 10,
      taskId: null,
      eventType: 'case_started',
      oldValue: null,
      newValue: 'Onboarding',
      detail: null,
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/employeelifecycle/1']}>
      <Routes>
        <Route path="/employeelifecycle/:id" element={<EmployeeDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  invokeMock.mockReset();
  invokeMock.mockImplementation((_module: string, action: string) => {
    switch (action) {
      case 'getEmployee':
        return Promise.resolve(details);
      case 'listAuditEntries':
        return Promise.resolve(audit);
      case 'listDepartments':
        return Promise.resolve({ departments: [] });
      case 'updateTask':
        return Promise.resolve({ task: details.cases[0].tasks[0] });
      default:
        return Promise.reject(new Error(`unexpected action ${action}`));
    }
  });
});

describe('EmployeeDetailPage', () => {
  it('shows master data, tasks and audit log', async () => {
    renderPage();

    expect(await screen.findByText('Max Muster')).toBeTruthy();
    expect(screen.getByText('m.muster')).toBeTruthy();
    expect(screen.getByText('AD-Benutzer anlegen')).toBeTruthy();
    expect(screen.getByText('Mailbox anlegen')).toBeTruthy();
    expect(screen.getByText('case_started')).toBeTruthy();
  });

  it('disables case completion while tasks are open', async () => {
    renderPage();
    await screen.findByText('AD-Benutzer anlegen');

    const completeButton = screen.getByRole('button', { name: 'Complete case' }) as HTMLButtonElement;
    expect(completeButton.disabled).toBe(true);
  });

  it('updates a task status through the edit panel', async () => {
    renderPage();
    await screen.findByText('AD-Benutzer anlegen');

    await userEvent.click(screen.getByText('AD-Benutzer anlegen'));
    await userEvent.selectOptions(await screen.findByLabelText('Status'), 'DONE');
    await userEvent.type(screen.getByLabelText('Assignee'), 'vinzent');
    await userEvent.click(screen.getByRole('button', { name: 'Save task' }));

    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith('employeelifecycle', 'updateTask', {
        taskId: 100,
        status: 'DONE',
        assignee: 'vinzent',
        dueDate: '2026-07-29',
      }),
    );
  });

  it('starts no case while one is active', async () => {
    renderPage();
    await screen.findByText('AD-Benutzer anlegen');

    expect(screen.queryByText('Start lifecycle case')).toBeNull();
  });
});
