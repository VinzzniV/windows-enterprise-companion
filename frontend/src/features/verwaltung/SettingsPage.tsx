import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import type {
  AppInfoResponse,
  ItLifecycleSettingsResult,
  ItLifecycleSettingsValue,
  OpsiConnectionStatusResult,
  OpsiSettingsResult,
  OpsiSettingsValue,
  ServiceCredentialStatus,
  ServiceCredentialStatuses,
  NessusSettingsResult,
  NessusSettingsValue,
  NessusCredentialStatus,
  NessusCertificateResult,
} from '../../shared/api-types';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Card } from '../../shared/ui/Card';
import { Button } from '../../shared/ui/Button';
import { Field } from '../../shared/ui/Field';
import { Input } from '../../shared/ui/Input';
import { Spinner } from '../../shared/ui/Spinner';
import { ErrorState } from '../../shared/ui/States';
import { ConfirmDangerAction } from '../../shared/ui/ConfirmDangerAction';
import { CredentialFields, type CredentialValues } from '../../shared/targets/Credentials';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { useEnvironmentOptional } from '../../shared/environment/EnvironmentContext';
import {
  isSettingsSection,
  SettingsSectionNavigation,
  settingsSectionElementId,
  type SettingsSectionId,
} from './SettingsSectionNavigation';
import { SettingsValidationSummary } from './SettingsValidationSummary';
import {
  validateItLifecycleSettings,
  validateNessusSettings,
  validateOpsiSettings,
} from './settingsValidation';

const emptyKasperskyCredentials: CredentialValues = { userName: '', domain: '', password: '' };

function settingsMatch<T>(current: T | null, saved: T | null) {
  return current !== null && saved !== null && JSON.stringify(current) === JSON.stringify(saved);
}

export function SettingsPage() {
  const [searchParameters, setSearchParameters] = useSearchParams();
  const handledInitialSettingsSection = useRef(false);
  const requestedSection = searchParameters.get('section');
  const activeSection: SettingsSectionId = isSettingsSection(requestedSection) ? requestedSection : 'overview';
  const targets = useTargetsOptional();
  const environment = useEnvironmentOptional();
  const [appInfo, setAppInfo] = useState<AppInfoResponse | null>(null);
  const [itLifecycle, setItLifecycle] = useState<ItLifecycleSettingsValue | null>(null);
  const [savedItLifecycle, setSavedItLifecycle] = useState<ItLifecycleSettingsValue | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<ErrorPresentation | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<ErrorPresentation | null>(null);
  const [restartRequired, setRestartRequired] = useState(false);
  const [kasperskyDraft, setKasperskyDraft] = useState<CredentialValues>(emptyKasperskyCredentials);
  const [opsi, setOpsi] = useState<OpsiSettingsValue | null>(null);
  const [savedOpsi, setSavedOpsi] = useState<OpsiSettingsValue | null>(null);
  const [opsiStatus, setOpsiStatus] = useState<OpsiConnectionStatusResult | null>(null);
  const [opsiUserName, setOpsiUserName] = useState('');
  const [opsiPassword, setOpsiPassword] = useState('');
  const [rememberOpsi, setRememberOpsi] = useState(true);
  const [credentialStatuses, setCredentialStatuses] = useState<ServiceCredentialStatuses | null>(null);
  const [opsiBusy, setOpsiBusy] = useState(false);
  const [opsiError, setOpsiError] = useState<ErrorPresentation | null>(null);
  const [nessus, setNessus] = useState<NessusSettingsValue | null>(null);
  const [savedNessus, setSavedNessus] = useState<NessusSettingsValue | null>(null);
  const [nessusCredential, setNessusCredential] = useState<NessusCredentialStatus | null>(null);
  const [nessusAccessKey, setNessusAccessKey] = useState('');
  const [nessusSecretKey, setNessusSecretKey] = useState('');
  const [nessusBusy, setNessusBusy] = useState(false);
  const [nessusError, setNessusError] = useState<ErrorPresentation | null>(null);
  const [editingNessusCredential, setEditingNessusCredential] = useState(false);
  const [nessusCertificate, setNessusCertificate] = useState<NessusCertificateResult | null>(null);
  const [openLogsError, setOpenLogsError] = useState<ErrorPresentation | null>(null);

  useEffect(() => {
    Promise.all([
      invoke<AppInfoResponse>('system', 'getAppInfo'),
      invoke<ItLifecycleSettingsResult>('system', 'getItLifecycleSettings'),
      invoke<OpsiSettingsResult>('system', 'getOpsiSettings'),
      invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus'),
      invoke<ServiceCredentialStatuses>('system', 'getServiceCredentialStatuses'),
      invoke<NessusSettingsResult>('system', 'getNessusSettings'),
      invoke<NessusCredentialStatus>('vulnerabilitymanagement', 'getCredentialStatus', {}),
    ])
      .then(([info, settings, opsiSettings, connection, credentials, nessusSettings, nessusStatus]) => {
        setAppInfo(info);
        setItLifecycle(settings.settings);
        setSavedItLifecycle(settings.settings);
        setOpsi(opsiSettings.settings);
        setSavedOpsi(opsiSettings.settings);
        setOpsiStatus(connection);
        setOpsiError(connection.connectionError
          ? presentError(new Error(connection.connectionError), { message: 'The opsi settings or connection could not be updated.' })
          : null);
        setCredentialStatuses(credentials);
        setNessus(nessusSettings.settings);
        setSavedNessus(nessusSettings.settings);
        setNessusCredential(nessusStatus);
        if (credentials.kaspersky.saved) {
          setKasperskyDraft((current) => ({ ...current, userName: credentials.kaspersky.userName ?? '', domain: credentials.kaspersky.domain ?? '' }));
        }
        if (credentials.opsi.saved) setOpsiUserName(credentials.opsi.userName ?? '');
      })
      .catch((error: unknown) => setLoadError(presentError(error, {
        message: 'Application settings could not be loaded.',
      })))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    if (requestedSection !== null && !isSettingsSection(requestedSection)) {
      setSearchParameters((current) => {
        const canonical = new URLSearchParams(current);
        canonical.delete('section');
        return canonical;
      }, { replace: true });
      return;
    }
    if (loading) return;

    const isInitialSectionResolution = !handledInitialSettingsSection.current;
    handledInitialSettingsSection.current = true;
    if (isInitialSectionResolution && requestedSection === null) return;

    const target = document.getElementById(settingsSectionElementId(activeSection));
    target?.scrollIntoView({
      behavior: window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
      block: 'start',
    });
  }, [activeSection, loading, requestedSection, setSearchParameters]);

  const saveItLifecycle = () => {
    if (!itLifecycle || itLifecycleIssues.length > 0) return;
    setSaving(true);
    setSaveError(null);
    invoke<ItLifecycleSettingsResult>('system', 'saveItLifecycleSettings', { settings: itLifecycle })
      .then((result) => {
        setItLifecycle(result.settings);
        setSavedItLifecycle(result.settings);
        setRestartRequired(result.restartRequired);
      })
      .catch((error: unknown) => setSaveError(presentError(error, {
        message: 'The settings change could not be saved.',
      })))
      .finally(() => setSaving(false));
  };

  const update = <K extends keyof ItLifecycleSettingsValue>(key: K, value: ItLifecycleSettingsValue[K]) => {
    setItLifecycle((current) => current && { ...current, [key]: value });
    setRestartRequired(false);
  };

  const updateKaspersky = <K extends keyof ItLifecycleSettingsValue['kaspersky']>(
    key: K,
    value: ItLifecycleSettingsValue['kaspersky'][K],
  ) => {
    setItLifecycle((current) =>
      current && { ...current, kaspersky: { ...current.kaspersky, [key]: value } },
    );
    setRestartRequired(false);
  };

  const saveOpsi = () => {
    if (!opsi || opsiIssues.length > 0) return;
    setOpsiBusy(true); setOpsiError(null);
    invoke<OpsiSettingsResult>('system', 'saveOpsiSettings', { settings: opsi })
      .then((next) => { setOpsi(next.settings); setSavedOpsi(next.settings); setRestartRequired(next.restartRequired); })
      .catch((caught: unknown) => setOpsiError(presentError(caught, {
        message: 'The opsi settings or connection could not be updated.',
      })))
      .finally(() => setOpsiBusy(false));
  };

  const connectOpsi = () => {
    if (!opsi) return;
    setOpsiBusy(true); setOpsiError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'connect', {
      server: opsi.server, userName: opsiUserName, password: opsiPassword,
      trustServerCertificate: opsi.trustServerCertificate,
      rememberCredential: rememberOpsi,
    }).then((status) => {
      setOpsiStatus(status); setOpsiPassword(''); environment?.invalidate();
      if (rememberOpsi) setCredentialStatuses((current) => current && ({ ...current, opsi: { saved: true, userName: status.userName, domain: null } }));
    }).catch((caught: unknown) => setOpsiError(presentError(caught, {
      message: 'The opsi settings or connection could not be updated.',
    }))).finally(() => setOpsiBusy(false));
  };

  const connectStoredOpsi = () => {
    if (!opsi) return;
    setOpsiBusy(true); setOpsiError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'connect', {
      server: opsi.server, userName: '', password: null,
      trustServerCertificate: opsi.trustServerCertificate, useStoredCredential: true,
    }).then((status) => {
      setOpsiStatus(status); environment?.invalidate();
    }).catch((caught: unknown) => setOpsiError(presentError(caught, {
      message: 'The opsi settings or connection could not be updated.',
    }))).finally(() => setOpsiBusy(false));
  };

  const saveKasperskyCredential = () => {
    if (!targets) return;
    setSaving(true); setSaveError(null);
    invoke<ServiceCredentialStatus>('system', 'saveServiceCredential', {
      kind: 'KASPERSKY', userName: kasperskyDraft.userName,
      domain: kasperskyDraft.domain.trim() || null, password: kasperskyDraft.password,
    }).then((status) => {
      targets.signInKaspersky(kasperskyDraft);
      setKasperskyDraft((current) => ({ ...current, password: '' }));
      setCredentialStatuses((current) => current && ({ ...current, kaspersky: status }));
      environment?.invalidate();
    }).catch((caught: unknown) => setSaveError(presentError(caught, {
      message: 'The settings change could not be saved.',
    }))).finally(() => setSaving(false));
  };

  const deleteCredential = (kind: 'KASPERSKY' | 'OPSI') => {
    setSaving(true); setSaveError(null);
    invoke<ServiceCredentialStatus>('system', 'deleteServiceCredential', { kind })
      .then((status) => {
        setCredentialStatuses((current) => current && ({
          ...current,
          [kind === 'KASPERSKY' ? 'kaspersky' : 'opsi']: status,
        }));
        if (kind === 'KASPERSKY') targets?.signOutKaspersky();
        environment?.invalidate();
      }).catch((caught: unknown) => setSaveError(presentError(caught, {
        message: 'The settings change could not be saved.',
      }))).finally(() => setSaving(false));
  };

  const disconnectOpsi = () => {
    setOpsiBusy(true); setOpsiError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'disconnect', {})
      .then((status) => { setOpsiStatus(status); environment?.invalidate(); })
      .catch((caught: unknown) => setOpsiError(presentError(caught, {
        message: 'The opsi settings or connection could not be updated.',
      }))).finally(() => setOpsiBusy(false));
  };

  const saveNessusSettings = () => {
    if (!nessus || nessusIssues.length > 0) return;
    setNessusBusy(true); setNessusError(null);
    invoke<NessusSettingsResult>('system', 'saveNessusSettings', { settings: nessus })
      .then((result) => { setNessus(result.settings); setSavedNessus(result.settings); setRestartRequired(result.restartRequired); })
      .catch((caught: unknown) => setNessusError(presentError(caught, {
        message: 'The Nessus settings or connection could not be updated.',
      }))).finally(() => setNessusBusy(false));
  };

  const saveNessusCredential = () => {
    if (!nessus) return;
    setNessusBusy(true); setNessusError(null);
    invoke<NessusCredentialStatus>('vulnerabilitymanagement', 'saveCredential', {
      accessKey: nessusAccessKey,
      secretKey: nessusSecretKey,
      serverUrl: nessus.serverUrl,
      requestTimeoutSeconds: nessus.requestTimeoutSeconds,
      trustedCertificateThumbprint: nessus.trustedCertificateThumbprint,
    })
      .then((status) => { setNessusCredential(status); setEditingNessusCredential(false); setNessusAccessKey(''); setNessusSecretKey(''); environment?.invalidate(); })
      .catch((caught: unknown) => setNessusError(presentError(caught, {
        message: 'The Nessus settings or connection could not be updated.',
      }))).finally(() => setNessusBusy(false));
  };

  const readNessusCertificate = () => {
    if (!nessus) return;
    setNessusBusy(true); setNessusError(null); setNessusCertificate(null);
    invoke<NessusCertificateResult>('vulnerabilitymanagement', 'getCertificate', {
      serverUrl: nessus.serverUrl,
      requestTimeoutSeconds: Math.min(nessus.requestTimeoutSeconds, 30),
    }, 35_000)
      .then((certificate) => {
        setNessusCertificate(certificate);
        setNessus({ ...nessus, trustedCertificateThumbprint: certificate.sha256Fingerprint });
      })
      .catch((caught: unknown) => setNessusError(presentError(caught, {
        message: 'The Nessus settings or connection could not be updated.',
      }))).finally(() => setNessusBusy(false));
  };

  const deleteNessusCredential = () => {
    setNessusBusy(true); setNessusError(null);
    invoke<NessusCredentialStatus>('vulnerabilitymanagement', 'deleteCredential', {})
      .then((status) => { setNessusCredential(status); environment?.invalidate(); })
      .catch((caught: unknown) => setNessusError(presentError(caught, {
        message: 'The Nessus settings or connection could not be updated.',
      }))).finally(() => setNessusBusy(false));
  };

  const itLifecycleDirty = itLifecycle !== null && savedItLifecycle !== null && !settingsMatch(itLifecycle, savedItLifecycle);
  const nessusDirty = nessus !== null && savedNessus !== null && !settingsMatch(nessus, savedNessus);
  const opsiDirty = opsi !== null && savedOpsi !== null && !settingsMatch(opsi, savedOpsi);
  const dirtySections = new Set<SettingsSectionId>([
    ...(itLifecycleDirty ? ['environment-health' as const] : []),
    ...(nessusDirty ? ['vulnerability-management' as const] : []),
    ...(opsiDirty ? ['patch-management' as const] : []),
  ]);
  const itLifecycleIssues = itLifecycle ? validateItLifecycleSettings(itLifecycle) : [];
  const nessusIssues = nessus ? validateNessusSettings(nessus) : [];
  const opsiIssues = opsi ? validateOpsiSettings(opsi) : [];
  const validationIssues = [...itLifecycleIssues, ...nessusIssues, ...opsiIssues];
  const validationCounts = new Map<SettingsSectionId, number>();
  for (const validationIssue of validationIssues) {
    validationCounts.set(validationIssue.section, (validationCounts.get(validationIssue.section) ?? 0) + 1);
  }

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Settings" subtitle="App-wide configuration for this machine" />
      <SettingsSectionNavigation
        activeSection={activeSection}
        dirtySections={dirtySections}
        validationCounts={validationCounts}
      />
      <SettingsValidationSummary issues={validationIssues} />

      {loading && <Spinner label="Loading settings …" />}
      {loadError && <ErrorState title="Settings could not be loaded" {...loadError} />}

      {!loading && appInfo && (
        <div id={settingsSectionElementId('overview')} className="scroll-mt-20">
          <Card title="Effective configuration">
          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1.5 text-sm">
            <dt className="text-slate-400">Version</dt>
            <dd className="font-mono text-slate-200">{appInfo.version}</dd>
            <dt className="text-slate-400">Machine</dt>
            <dd className="font-mono text-slate-200">{appInfo.machineName}</dd>
            <dt className="text-slate-400">Runtime profile</dt>
            <dd className="font-mono text-slate-200">{appInfo.runtimeProfile}</dd>
            <dt className="text-slate-400">Privilege</dt>
            <dd className="text-slate-200">{appInfo.isElevated ? 'Administrator' : 'Standard user'}</dd>
            <dt className="text-slate-400">Max parallel scans</dt>
            <dd className="font-mono text-slate-200">{appInfo.maxParallelScans}</dd>
            <dt className="text-slate-400">Max batch hosts</dt>
            <dd className="font-mono text-slate-200">{appInfo.maxBatchHosts}</dd>
            <dt className="text-slate-400">Database</dt>
            <dd className="break-all font-mono text-xs text-slate-300">{appInfo.databasePath}</dd>
            <dt className="text-slate-400">Log directory</dt>
            <dd className="break-all font-mono text-xs text-slate-300">{appInfo.logDirectory}</dd>
          </dl>
          <div className="mt-3">
            <Button
              variant="secondary"
              onClick={() => {
                setOpenLogsError(null);
                invoke('system', 'openLogsFolder').catch((caught: unknown) =>
                  setOpenLogsError(presentError(caught, {
                    message: 'The log folder could not be opened.',
                  })),
                );
              }}
            >
              Open log folder
            </Button>
            {openLogsError && (
              <div className="mt-3">
                <ErrorState title="Log folder unavailable" {...openLogsError} />
              </div>
            )}
          </div>
          </Card>
        </div>
      )}

      {itLifecycle && (
        <div id={settingsSectionElementId('environment-health')} className="scroll-mt-20">
          <Card title="IT Lifecycle / Environment Health">
          <div className="mb-5 rounded-md border border-slate-700 bg-slate-900/40 p-4">
            <h3 className="mb-1 text-sm font-semibold text-slate-200">Kaspersky session account</h3>
            {credentialStatuses?.kaspersky.saved && (
              <div className="mb-3 flex flex-wrap items-center gap-3 rounded border border-emerald-800/60 bg-emerald-950/20 px-3 py-2">
                <p className="text-sm text-slate-300">Saved securely in Windows Credential Manager as <span className="font-mono text-slate-100">{credentialStatuses.kaspersky.domain ? `${credentialStatuses.kaspersky.domain}\\` : ''}{credentialStatuses.kaspersky.userName}</span>. Environment Health uses it automatically.</p>
                <ConfirmDangerAction
                  subject="saved KSC credential"
                  triggerLabel="Remove saved KSC credential"
                  description="Removes the KSC account from Windows Credential Manager. Automatic Environment Health access will stop until a credential is saved again."
                  onConfirm={() => deleteCredential('KASPERSKY')}
                  disabled={saving}
                />
              </div>
            )}
            {targets?.kasperskyCredentials ? (
              <div className="flex flex-wrap items-center gap-3">
                <p className="text-sm text-slate-300">
                  Signed in as{' '}
                  <span className="font-mono text-slate-100">
                    {targets.kasperskyCredentials.domain
                      ? `${targets.kasperskyCredentials.domain}\\`
                      : ''}
                    {targets.kasperskyCredentials.userName}
                  </span>
                </p>
                <Button variant="secondary" onClick={targets.signOutKaspersky}>Sign out KSC</Button>
              </div>
            ) : (
              <>
                <CredentialFields
                  values={kasperskyDraft}
                  onChange={(patch) => setKasperskyDraft((current) => ({ ...current, ...patch }))}
                />
                <div className="mt-3 flex flex-wrap items-center gap-3">
                  <Button
                    variant="secondary"
                    disabled={!targets || !kasperskyDraft.userName.trim() || !kasperskyDraft.password}
                    onClick={saveKasperskyCredential}
                  >
                    Save securely and sign in KSC
                  </Button>
                  <p className="text-xs text-slate-400">
                    Stored for the current Windows user; independent from the admin account in the top bar.
                  </p>
                </div>
              </>
            )}
          </div>

          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <Field label="Kaspersky Administration Server" hint="Host name or HTTPS URL; credentials use the separate KSC session above.">
              {(id) => (
                <Input
                  id={id}
                  value={itLifecycle.kaspersky.server}
                  onChange={(event) => updateKaspersky('server', event.target.value)}
                  placeholder="ksc.example.local"
                />
              )}
            </Field>
            <Field label="KSC OpenAPI port">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={1}
                  max={65535}
                  value={itLifecycle.kaspersky.port}
                  onChange={(event) => updateKaspersky('port', Number(event.target.value))}
                />
              )}
            </Field>
            <Field label="KSC timeout (seconds)">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={1}
                  max={600}
                  value={itLifecycle.kaspersky.requestTimeoutSeconds}
                  onChange={(event) => updateKaspersky('requestTimeoutSeconds', Number(event.target.value))}
                />
              )}
            </Field>
            <Field label="Target Network Agent version" hint="Empty disables the outdated-agent rule.">
              {(id) => (
                <Input
                  id={id}
                  value={itLifecycle.targetAgentVersion}
                  onChange={(event) => update('targetAgentVersion', event.target.value)}
                  placeholder="16.0.0.254"
                />
              )}
            </Field>
            <Field label="Target KES version" hint="Empty disables the outdated-KES rule.">
              {(id) => (
                <Input
                  id={id}
                  value={itLifecycle.targetKesVersion}
                  onChange={(event) => update('targetKesVersion', event.target.value)}
                  placeholder="21.25.7.504"
                />
              )}
            </Field>
            <Field label="Inventory limit per source">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={1}
                  max={100000}
                  value={itLifecycle.inventoryLimit}
                  onChange={(event) => update('inventoryLimit', Number(event.target.value))}
                />
              )}
            </Field>
            <Field label="Stale warning (days)">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={1}
                  max={3650}
                  value={itLifecycle.staleWarningDays}
                  onChange={(event) => update('staleWarningDays', Number(event.target.value))}
                />
              )}
            </Field>
            <Field label="Stale cleanup candidate (days)" hint="Must be greater than the warning threshold.">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={1}
                  max={3650}
                  value={itLifecycle.staleCriticalDays}
                  onChange={(event) => update('staleCriticalDays', Number(event.target.value))}
                />
              )}
            </Field>
            <Field label="KSC certificate thumbprint" hint="Optional SHA-1 or SHA-256 pin for a private KSC certificate.">
              {(id) => (
                <Input
                  id={id}
                  value={itLifecycle.kaspersky.trustedCertificateThumbprint}
                  onChange={(event) => updateKaspersky('trustedCertificateThumbprint', event.target.value)}
                  placeholder="Optional"
                  className="font-mono"
                />
              )}
            </Field>
            <Field label="Ignored KSC administration groups" hint="Comma-separated group names. Devices below these groups are excluded from inventory and orphan checks.">
              {(id) => (
                <Input
                  id={id}
                  value={itLifecycle.kaspersky.excludedAdministrationGroups.join(', ')}
                  onChange={(event) => updateKaspersky(
                    'excludedAdministrationGroups',
                    event.target.value.split(',').map((group) => group.trim()).filter(Boolean),
                  )}
                  placeholder="Devices not suitable for Kaspersky"
                />
              )}
            </Field>
          </div>

          <div className="mt-4 flex flex-wrap items-center gap-3">
            <Button variant="primary" onClick={saveItLifecycle} disabled={saving || itLifecycleIssues.length > 0}>
              {saving ? 'Saving…' : 'Save IT Lifecycle settings'}
            </Button>
            {itLifecycleDirty && <p role="status" className="text-sm font-medium text-warn-300">Unsaved changes</p>}
            {restartRequired && (
              <p role="status" className="text-sm text-warn-300">
                Saved. Restart WEC to apply these settings.
              </p>
            )}
          </div>
          {saveError && <div className="mt-3"><ErrorState title="Settings could not be saved" {...saveError} /></div>}
          </Card>
        </div>
      )}

      {nessus && (
        <div id={settingsSectionElementId('vulnerability-management')} className="scroll-mt-20">
          <Card title="Nessus / Vulnerability Management">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <Field label="Nessus HTTPS URL" hint="Local Nessus Professional/Expert; default port 8834.">{(id) => <Input id={id} value={nessus.serverUrl} onChange={(event) => setNessus({ ...nessus, serverUrl: event.target.value })} placeholder="https://nessus.example.local:8834" />}</Field>
            <Field label="Timeout (seconds)">{(id) => <Input id={id} type="number" min={1} max={600} value={nessus.requestTimeoutSeconds} onChange={(event) => setNessus({ ...nessus, requestTimeoutSeconds: Number(event.target.value) })} />}</Field>
            <Field label="Cache TTL (minutes)">{(id) => <Input id={id} type="number" min={1} max={1440} value={nessus.cacheTtlMinutes} onChange={(event) => setNessus({ ...nessus, cacheTtlMinutes: Number(event.target.value) })} />}</Field>
            <Field label="Certificate fingerprint" hint="Optional SHA-1/SHA-256 pin for a private certificate.">{(id) => <Input id={id} className="font-mono" value={nessus.trustedCertificateThumbprint} onChange={(event) => setNessus({ ...nessus, trustedCertificateThumbprint: event.target.value })} />}</Field>
            <Field label="Backfill (days)">{(id) => <Input id={id} type="number" min={0} max={365} value={nessus.backfillDays} onChange={(event) => setNessus({ ...nessus, backfillDays: Number(event.target.value) })} />}</Field>
            <Field label="Retention (days)">{(id) => <Input id={id} type="number" min={7} max={3650} value={nessus.retentionDays} onChange={(event) => setNessus({ ...nessus, retentionDays: Number(event.target.value) })} />}</Field>
            <Field label="Stale warning (days)">{(id) => <Input id={id} type="number" min={1} max={3650} value={nessus.staleWarningDays} onChange={(event) => setNessus({ ...nessus, staleWarningDays: Number(event.target.value) })} />}</Field>
            <Field label="Stale critical (days)">{(id) => <Input id={id} type="number" min={1} max={3650} value={nessus.staleCriticalDays} onChange={(event) => setNessus({ ...nessus, staleCriticalDays: Number(event.target.value) })} />}</Field>
            <Field label="Excluded scan IDs" hint="Comma-separated numeric Nessus scan IDs.">{(id) => <Input id={id} value={nessus.excludedScanIds.join(', ')} onChange={(event) => setNessus({ ...nessus, excludedScanIds: event.target.value.split(',').map(Number).filter((x) => Number.isInteger(x) && x > 0) })} />}</Field>
            <Field label="Missing-Nessus OU exclusions" hint="Comma-separated text patterns.">{(id) => <Input id={id} value={nessus.missingNessusExcludedOuPatterns.join(', ')} onChange={(event) => setNessus({ ...nessus, missingNessusExcludedOuPatterns: event.target.value.split(',').map((x) => x.trim()).filter(Boolean) })} />}</Field>
            <Field label="Missing-Nessus host exclusions" hint="Comma-separated host patterns.">{(id) => <Input id={id} value={nessus.missingNessusExcludedHostPatterns.join(', ')} onChange={(event) => setNessus({ ...nessus, missingNessusExcludedHostPatterns: event.target.value.split(',').map((x) => x.trim()).filter(Boolean) })} />}</Field>
          </div>
          <div className="mt-4 flex flex-wrap items-center gap-3"><Button variant="primary" onClick={saveNessusSettings} disabled={nessusBusy || nessusIssues.length > 0}>{nessusBusy ? 'Saving…' : 'Save Nessus settings'}</Button><Button variant="secondary" onClick={readNessusCertificate} disabled={nessusBusy || !nessus.serverUrl}>{nessusBusy ? 'Reading…' : 'Read HTTPS certificate fingerprint'}</Button>{nessusDirty && <p role="status" className="text-sm font-medium text-warn-300">Unsaved changes</p>}</div>
          {nessusCertificate && <div className="mt-3 rounded border border-amber-700/60 bg-amber-950/20 px-3 py-2 text-xs text-slate-300"><p>Certificate received from the configured server. Verify it before saving.</p><p className="mt-1 break-all font-mono">SHA-256: {nessusCertificate.sha256Fingerprint}</p><p className="mt-1">{nessusCertificate.subject} · valid until {new Date(nessusCertificate.validToUtc).toLocaleDateString()}</p></div>}
          <div className="mt-5 rounded-md border border-slate-700 bg-slate-900/40 p-4">
            <h3 className="mb-2 text-sm font-semibold text-slate-200">Nessus API keys</h3>
            {nessusCredential?.saved && !editingNessusCredential ? <div className="flex flex-wrap items-center gap-3"><p className="text-sm text-slate-300">Access Key and Secret Key are stored securely in Windows Credential Manager.</p><Button variant="secondary" onClick={() => setEditingNessusCredential(true)} disabled={nessusBusy}>Replace API keys</Button><ConfirmDangerAction subject="Nessus API keys" triggerLabel="Remove API keys" description="Removes both Nessus API keys from Windows Credential Manager. Nessus synchronization will stop until new keys are saved." onConfirm={deleteNessusCredential} disabled={nessusBusy} /></div> : <><div className="grid gap-3 sm:grid-cols-2"><Input type="password" value={nessusAccessKey} onChange={(event) => setNessusAccessKey(event.target.value)} placeholder="Access Key" autoComplete="off" /><Input type="password" value={nessusSecretKey} onChange={(event) => setNessusSecretKey(event.target.value)} placeholder="Secret Key" autoComplete="off" /></div><div className="mt-3 flex items-center gap-3"><Button variant="secondary" onClick={saveNessusCredential} disabled={nessusBusy || !nessusAccessKey || !nessusSecretKey}>{nessusBusy ? 'Testing…' : 'Test connection and save keys'}</Button>{nessusCredential?.saved && <Button variant="secondary" onClick={() => setEditingNessusCredential(false)}>Cancel</Button>}<p className="text-xs text-slate-400">Neither key is written to usersettings.json, logs, local storage or bridge responses.</p></div></>}
          </div>
          {nessusError && <div className="mt-3"><ErrorState title="Nessus settings or connection failed" {...nessusError} /></div>}
          </Card>
        </div>
      )}

      {opsi && (
        <div id={settingsSectionElementId('patch-management')} className="scroll-mt-20">
          <Card title="opsi / Patch Management">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <Field label="opsi server" hint="Host name or HTTPS URL used by all WEC areas.">{(id) => <Input id={id} value={opsi.server} onChange={(event) => setOpsi({ ...opsi, server: event.target.value })} placeholder="opsi.example.local" />}</Field>
            <Field label="opsi port">{(id) => <Input id={id} type="number" min={1} max={65535} value={opsi.port} onChange={(event) => setOpsi({ ...opsi, port: Number(event.target.value) })} />}</Field>
            <Field label="opsi timeout (seconds)">{(id) => <Input id={id} type="number" min={1} max={600} value={opsi.requestTimeoutSeconds} onChange={(event) => setOpsi({ ...opsi, requestTimeoutSeconds: Number(event.target.value) })} />}</Field>
            <Field label="Default depot filter">{(id) => <Input id={id} value={opsi.defaultDepotFilter} onChange={(event) => setOpsi({ ...opsi, defaultDepotFilter: event.target.value })} placeholder="Optional" />}</Field>
          </div>
          <div className="mt-4 flex flex-wrap items-center gap-3"><Button variant="primary" onClick={saveOpsi} disabled={opsiBusy || opsiIssues.length > 0}>{opsiBusy ? 'Saving…' : 'Save opsi settings'}</Button>{opsiDirty && <p role="status" className="text-sm font-medium text-warn-300">Unsaved changes</p>}</div>

          <div className="mt-5 rounded-md border border-slate-700 bg-slate-900/40 p-4">
            <h3 className="mb-2 text-sm font-semibold text-slate-200">opsi session account</h3>
            {credentialStatuses?.opsi.saved && <div className="mb-3 flex flex-wrap items-center gap-3 rounded border border-emerald-800/60 bg-emerald-950/20 px-3 py-2"><p className="text-sm text-slate-300">Saved securely in Windows Credential Manager as <span className="font-mono text-slate-100">{credentialStatuses.opsi.userName}</span>.</p><ConfirmDangerAction subject="saved opsi credential" triggerLabel="Remove saved opsi credential" description="Removes the opsi account from Windows Credential Manager. Automatic opsi connections will stop until a credential is saved again." onConfirm={() => deleteCredential('OPSI')} disabled={saving} /></div>}
            {opsiStatus?.connected ? <div className="flex flex-wrap items-center gap-3"><p className="text-sm text-slate-300">Connected to <span className="font-mono text-slate-100">{opsiStatus.serverUrl}</span> as <span className="font-mono text-slate-100">{opsiStatus.userName}</span>{opsiStatus.opsiVersion ? ` · opsi ${opsiStatus.opsiVersion}` : ''}</p>
              <Button variant="secondary" onClick={disconnectOpsi} disabled={opsiBusy}>Disconnect opsi</Button></div> : <>
              <div className="grid gap-3 sm:grid-cols-2">
                <Input value={opsiUserName} onChange={(event) => setOpsiUserName(event.target.value)} aria-label="opsi user name" placeholder="User name" autoComplete="off" />
                <Input type="password" value={opsiPassword} onChange={(event) => setOpsiPassword(event.target.value)} aria-label="opsi password" placeholder="Password" autoComplete="off" />
              </div>
              <label className="mt-3 flex items-center gap-2 text-sm text-slate-300"><input type="checkbox" checked={opsi.trustServerCertificate} onChange={(event) => setOpsi({ ...opsi, trustServerCertificate: event.target.checked })} className="accent-accent-500" />Trust the opsi server certificate for saved and automatic connections</label>
              <label className="mt-3 flex items-center gap-2 text-sm text-slate-300"><input type="checkbox" checked={rememberOpsi} onChange={(event) => setRememberOpsi(event.target.checked)} className="accent-accent-500" />Save the opsi account securely in Windows Credential Manager</label>
              <div className="mt-3 flex flex-wrap items-center gap-3"><Button variant="secondary" onClick={connectOpsi} disabled={opsiBusy || !opsi.server.trim() || !opsiUserName.trim() || !opsiPassword}>{opsiBusy ? 'Connecting…' : 'Connect opsi'}</Button>
                {credentialStatuses?.opsi.saved && <Button variant="secondary" onClick={connectStoredOpsi} disabled={opsiBusy || !opsi.server.trim()}>Connect saved opsi account</Button>}
                <p className="text-xs text-slate-400">A saved account is connected automatically when an opsi-backed view or action needs it.</p></div>
            </>}
          </div>
          {opsiError && <div className="mt-3"><ErrorState title="opsi settings or connection failed" {...opsiError} /></div>}
          </Card>
        </div>
      )}

      <div id={settingsSectionElementId('policy')} className="scroll-mt-20">
        <Card title="Configuration policy">
          <p className="text-sm text-slate-400">
            User-configurable operating parameters belong on this page. Future modules should add their settings here.
            Deployment defaults remain in <span className="font-mono text-slate-300">appsettings.json</span>; saved values are
            merged into <span className="font-mono text-slate-300">%APPDATA%\Wec\usersettings.json</span>. KSC, opsi and Nessus secrets
            can be stored in the current Windows user's Credential Manager; the normal admin sign-in remains session-only.
          </p>
        </Card>
      </div>
    </div>
  );
}
