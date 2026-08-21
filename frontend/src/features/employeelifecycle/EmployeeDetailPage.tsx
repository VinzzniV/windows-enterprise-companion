import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import type {
  EmployeeDetailsResult,
  LifecycleAuditEntry,
  LifecycleAuditListResult,
  LifecycleCase,
  LifecycleCaseType,
  LifecycleDepartment,
  LifecycleDepartmentListResult,
  LifecycleTask,
  LifecycleTaskStatus,
} from '../../shared/api-types';
import { Badge } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable, type DataColumn } from '../../shared/ui/DataTable';
import { Field } from '../../shared/ui/Field';
import { Input } from '../../shared/ui/Input';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Select } from '../../shared/ui/Select';
import { Spinner } from '../../shared/ui/Spinner';
import { ErrorState } from '../../shared/ui/States';
import {
  canCompleteCase,
  caseStatusLabels,
  caseStatusTone,
  caseTypeLabels,
  employeeStatusLabels,
  employeeStatusTone,
  formatDate,
  formatTimestamp,
  startableCaseTypes,
  taskAreaLabels,
  taskStatusLabels,
  taskStatusOptions,
  taskStatusTone,
} from './lifecycle';
import { EmployeeForm, formValueFromEmployee, toEmployeeInput, type EmployeeFormValue } from './EmployeeForm';

interface TaskDraft {
  status: LifecycleTaskStatus;
  assignee: string;
  dueDate: string;
  note: string;
}

function MasterDataRow({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs uppercase tracking-wide text-muted">{label}</span>
      <span className="text-sm text-slate-200">{value ?? '—'}</span>
    </div>
  );
}

export function EmployeeDetailPage() {
  const navigate = useNavigate();
  const { id } = useParams();
  const employeeId = Number(id);

  const [data, setData] = useState<EmployeeDetailsResult | null>(null);
  const [audit, setAudit] = useState<LifecycleAuditEntry[]>([]);
  const [departments, setDepartments] = useState<LifecycleDepartment[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState<EmployeeFormValue | null>(null);

  const [caseType, setCaseType] = useState<LifecycleCaseType | ''>('');
  const [effectiveDate, setEffectiveDate] = useState('');
  const [caseNote, setCaseNote] = useState('');

  const [selectedTask, setSelectedTask] = useState<LifecycleTask | null>(null);
  const [taskDraft, setTaskDraft] = useState<TaskDraft | null>(null);
  const [cancellingCaseId, setCancellingCaseId] = useState<number | null>(null);
  const [cancelReason, setCancelReason] = useState('');

  const load = useCallback(() => {
    setLoadError(null);
    Promise.all([
      invoke<EmployeeDetailsResult>('employeelifecycle', 'getEmployee', { employeeId }),
      invoke<LifecycleAuditListResult>('employeelifecycle', 'listAuditEntries', { employeeId }),
      invoke<LifecycleDepartmentListResult>('employeelifecycle', 'listDepartments').catch(
        () => ({ departments: [] }) as LifecycleDepartmentListResult,
      ),
    ])
      .then(([details, auditResult, departmentResult]) => {
        setData(details);
        setAudit(auditResult.entries);
        setDepartments(departmentResult.departments);
      })
      .catch((error: unknown) => setLoadError(errorText(error)));
  }, [employeeId]);

  useEffect(load, [load]);

  const runAction = (action: Promise<unknown>) => {
    setBusy(true);
    setActionError(null);
    action
      .then(() => {
        setSelectedTask(null);
        setTaskDraft(null);
        setCancellingCaseId(null);
        setCancelReason('');
        load();
      })
      .catch((error: unknown) => setActionError(errorText(error)))
      .finally(() => setBusy(false));
  };

  if (Number.isNaN(employeeId)) {
    return <ErrorState title="Invalid employee id" message={`"${id}" is not a valid employee id.`} />;
  }
  if (loadError) {
    return (
      <div className="flex flex-col gap-4">
        <ErrorState title="Loading failed" message={loadError} />
        <Button onClick={() => navigate('/employeelifecycle')}>Back to list</Button>
      </div>
    );
  }
  if (!data) {
    return <Spinner label="Loading employee" />;
  }

  const { employee, cases } = data;
  const activeCase = cases.find((lifecycleCase) => lifecycleCase.status === 'ACTIVE') ?? null;
  const startable = activeCase ? [] : startableCaseTypes(employee.status);

  const saveEmployee = () => {
    if (!form) {
      return;
    }
    runAction(
      invoke('employeelifecycle', 'updateEmployee', { employeeId: employee.id, ...toEmployeeInput(form) }).then(() =>
        setEditing(false),
      ),
    );
  };

  const startCase = () => {
    runAction(
      invoke('employeelifecycle', 'startCase', {
        employeeId: employee.id,
        type: caseType,
        effectiveDate: effectiveDate.length > 0 ? effectiveDate : null,
        note: caseNote.length > 0 ? caseNote : null,
      }).then(() => {
        setCaseType('');
        setEffectiveDate('');
        setCaseNote('');
      }),
    );
  };

  const selectTask = (lifecycleCase: LifecycleCase, task: LifecycleTask) => {
    if (lifecycleCase.status !== 'ACTIVE') {
      return;
    }
    setSelectedTask(task);
    setTaskDraft({
      status: task.status,
      assignee: task.assignee ?? '',
      dueDate: task.dueDate ?? '',
      note: '',
    });
  };

  const saveTask = () => {
    if (!selectedTask || !taskDraft) {
      return;
    }
    runAction(
      invoke('employeelifecycle', 'updateTask', {
        taskId: selectedTask.id,
        status: taskDraft.status,
        assignee: taskDraft.assignee.length > 0 ? taskDraft.assignee : null,
        dueDate: taskDraft.dueDate.length > 0 ? taskDraft.dueDate : null,
      }),
    );
  };

  const addTaskNote = () => {
    if (!selectedTask || !taskDraft || taskDraft.note.trim().length === 0) {
      return;
    }
    runAction(invoke('employeelifecycle', 'addTaskNote', { taskId: selectedTask.id, note: taskDraft.note }));
  };

  const taskColumns: DataColumn<LifecycleTask>[] = [
    { header: 'Task', cell: (row) => row.title },
    { header: 'Area', cell: (row) => taskAreaLabels[row.area] },
    {
      header: 'Due',
      cell: (row) =>
        row.isOverdue ? <span className="font-medium text-rose-400">{formatDate(row.dueDate)}</span> : formatDate(row.dueDate),
      sortValue: (row) => row.dueDate,
      mono: true,
    },
    { header: 'Assignee', cell: (row) => row.assignee ?? '—' },
    {
      header: 'Status',
      cell: (row) => <Badge tone={taskStatusTone(row.status)}>{taskStatusLabels[row.status]}</Badge>,
      sortValue: (row) => row.status,
    },
    {
      header: 'Notes',
      cell: (row) =>
        row.notes.length > 0 ? <span className="whitespace-pre-line text-xs text-slate-400">{row.notes}</span> : '—',
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title={`${employee.firstName} ${employee.lastName}`}
        subtitle={`Employee lifecycle record${employee.employeeNumber ? ` · No. ${employee.employeeNumber}` : ''}`}
      >
        <Badge tone={employeeStatusTone(employee.status)}>{employeeStatusLabels[employee.status]}</Badge>
        <Button onClick={() => navigate('/employeelifecycle')}>Back to list</Button>
      </PageHeader>

      {actionError && <ErrorState title="Action failed" message={actionError} />}

      <Card title="Master data">
        {editing && form ? (
          <EmployeeForm
            value={form}
            onChange={setForm}
            onSubmit={saveEmployee}
            onCancel={() => setEditing(false)}
            submitLabel="Save changes"
            busy={busy}
            error={null}
            departments={departments}
          />
        ) : (
          <div className="flex flex-col gap-3">
            <div className="grid gap-3 sm:grid-cols-3 lg:grid-cols-4">
              <MasterDataRow label="Email" value={employee.email} />
              <MasterDataRow label="Department" value={employee.department} />
              <MasterDataRow label="Title" value={employee.title} />
              <MasterDataRow label="Manager" value={employee.manager} />
              <MasterDataRow label="Entry date" value={formatDate(employee.entryDate)} />
              <MasterDataRow label="Exit date" value={formatDate(employee.exitDate)} />
              <MasterDataRow label="AD sAMAccountName" value={employee.samAccountName} />
              <MasterDataRow label="AD userPrincipalName" value={employee.userPrincipalName} />
              <MasterDataRow label="AD distinguishedName" value={employee.distinguishedName} />
              <MasterDataRow label="Notes" value={employee.notes} />
            </div>
            <div>
              <Button
                onClick={() => {
                  setForm(formValueFromEmployee(employee));
                  setEditing(true);
                }}
              >
                Edit master data
              </Button>
            </div>
          </div>
        )}
      </Card>

      {startable.length > 0 && (
        <Card title="Start lifecycle case">
          <div className="grid gap-3 sm:grid-cols-4">
            <Field label="Case type">
              {(fieldId) => (
                <Select
                  id={fieldId}
                  value={caseType}
                  onChange={(event) => setCaseType(event.target.value as LifecycleCaseType | '')}
                >
                  <option value="">Select…</option>
                  {startable.map((type) => (
                    <option key={type} value={type}>
                      {caseTypeLabels[type]}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
            <Field
              label="Effective date"
              hint="Entry date (onboarding), last working day (offboarding) or change date."
            >
              {(fieldId) => (
                <Input id={fieldId} type="date" value={effectiveDate} onChange={(event) => setEffectiveDate(event.target.value)} />
              )}
            </Field>
            <Field label="Note (optional)">
              {(fieldId) => (
                <Input id={fieldId} type="text" value={caseNote} onChange={(event) => setCaseNote(event.target.value)} />
              )}
            </Field>
            <div className="flex items-end">
              <Button variant="primary" onClick={startCase} disabled={busy || caseType === ''}>
                {busy ? 'Starting…' : 'Start case'}
              </Button>
            </div>
          </div>
        </Card>
      )}

      {cases.length === 0 && (
        <Card title="Lifecycle cases">
          <p className="text-sm text-slate-400">No lifecycle case yet. Start one above.</p>
        </Card>
      )}

      {cases.map((lifecycleCase) => (
        <Card
          key={lifecycleCase.id}
          title={`${caseTypeLabels[lifecycleCase.type]} · ${caseStatusLabels[lifecycleCase.status]}`}
        >
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap items-center gap-3 text-sm text-slate-400">
              <Badge tone={caseStatusTone(lifecycleCase.status)}>{caseStatusLabels[lifecycleCase.status]}</Badge>
              <span>Effective: {formatDate(lifecycleCase.effectiveDate)}</span>
              <span>Started: {formatTimestamp(lifecycleCase.createdUtc)}</span>
              {lifecycleCase.closedUtc && <span>Closed: {formatTimestamp(lifecycleCase.closedUtc)}</span>}
              {lifecycleCase.note && <span>Note: {lifecycleCase.note}</span>}
              {lifecycleCase.cancelReason && <span>Cancel reason: {lifecycleCase.cancelReason}</span>}
            </div>

            <DataTable
              columns={taskColumns}
              rows={lifecycleCase.tasks}
              getRowKey={(row) => row.id}
              onRowClick={
                lifecycleCase.status === 'ACTIVE' ? (row) => selectTask(lifecycleCase, row) : undefined
              }
              isRowActive={(row) => selectedTask?.id === row.id}
              emptyMessage="This case has no tasks."
            />

            {lifecycleCase.status === 'ACTIVE' && selectedTask && taskDraft &&
              lifecycleCase.tasks.some((task) => task.id === selectedTask.id) && (
              <div className="flex flex-col gap-3 rounded border border-slate-700 bg-slate-900/60 p-3">
                <span className="text-sm font-medium text-slate-200">{selectedTask.title}</span>
                <div className="grid gap-3 sm:grid-cols-4">
                  <Field label="Status">
                    {(fieldId) => (
                      <Select
                        id={fieldId}
                        value={taskDraft.status}
                        onChange={(event) =>
                          setTaskDraft({ ...taskDraft, status: event.target.value as LifecycleTaskStatus })
                        }
                      >
                        {taskStatusOptions.map((status) => (
                          <option key={status} value={status}>
                            {taskStatusLabels[status]}
                          </option>
                        ))}
                      </Select>
                    )}
                  </Field>
                  <Field label="Assignee">
                    {(fieldId) => (
                      <Input
                        id={fieldId}
                        type="text"
                        value={taskDraft.assignee}
                        onChange={(event) => setTaskDraft({ ...taskDraft, assignee: event.target.value })}
                        placeholder="Admin name"
                      />
                    )}
                  </Field>
                  <Field label="Due date">
                    {(fieldId) => (
                      <Input
                        id={fieldId}
                        type="date"
                        value={taskDraft.dueDate}
                        onChange={(event) => setTaskDraft({ ...taskDraft, dueDate: event.target.value })}
                      />
                    )}
                  </Field>
                  <div className="flex items-end">
                    <Button variant="primary" onClick={saveTask} disabled={busy}>
                      {busy ? 'Saving…' : 'Save task'}
                    </Button>
                  </div>
                </div>
                <div className="grid gap-3 sm:grid-cols-4">
                  <div className="sm:col-span-3">
                    <Field label="Add note">
                      {(fieldId) => (
                        <Input
                          id={fieldId}
                          type="text"
                          value={taskDraft.note}
                          onChange={(event) => setTaskDraft({ ...taskDraft, note: event.target.value })}
                          placeholder="What happened?"
                        />
                      )}
                    </Field>
                  </div>
                  <div className="flex items-end">
                    <Button onClick={addTaskNote} disabled={busy || taskDraft.note.trim().length === 0}>
                      Add note
                    </Button>
                  </div>
                </div>
              </div>
            )}

            {lifecycleCase.status === 'ACTIVE' && (
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  variant="primary"
                  onClick={() => runAction(invoke('employeelifecycle', 'completeCase', { caseId: lifecycleCase.id }))}
                  disabled={busy || !canCompleteCase(lifecycleCase.tasks)}
                  title={canCompleteCase(lifecycleCase.tasks) ? undefined : 'All tasks must be done or skipped first.'}
                >
                  Complete case
                </Button>
                {cancellingCaseId === lifecycleCase.id ? (
                  <>
                    <Input
                      type="text"
                      value={cancelReason}
                      onChange={(event) => setCancelReason(event.target.value)}
                      placeholder="Cancellation reason (optional)"
                      aria-label="Cancellation reason"
                    />
                    <Button
                      onClick={() =>
                        runAction(
                          invoke('employeelifecycle', 'cancelCase', {
                            caseId: lifecycleCase.id,
                            reason: cancelReason.length > 0 ? cancelReason : null,
                          }),
                        )
                      }
                      disabled={busy}
                    >
                      Confirm cancel
                    </Button>
                    <Button variant="ghost" onClick={() => setCancellingCaseId(null)} disabled={busy}>
                      Keep case
                    </Button>
                  </>
                ) : (
                  <Button onClick={() => setCancellingCaseId(lifecycleCase.id)} disabled={busy}>
                    Cancel case…
                  </Button>
                )}
              </div>
            )}
          </div>
        </Card>
      ))}

      <Card title="Audit log">
        <DataTable
          columns={[
            {
              header: 'Time',
              cell: (row: LifecycleAuditEntry) => formatTimestamp(row.timestampUtc),
              sortValue: (row: LifecycleAuditEntry) => row.timestampUtc,
              mono: true,
            },
            { header: 'Event', cell: (row: LifecycleAuditEntry) => row.eventType, mono: true },
            {
              header: 'Change',
              cell: (row: LifecycleAuditEntry) =>
                row.oldValue || row.newValue ? `${row.oldValue ?? '—'} → ${row.newValue ?? '—'}` : '—',
            },
            { header: 'Detail', cell: (row: LifecycleAuditEntry) => row.detail ?? '—' },
            { header: 'User', cell: (row: LifecycleAuditEntry) => row.userName, mono: true },
          ]}
          rows={audit}
          getRowKey={(row) => row.id}
          emptyMessage="No audit entries yet."
        />
      </Card>
    </div>
  );
}
