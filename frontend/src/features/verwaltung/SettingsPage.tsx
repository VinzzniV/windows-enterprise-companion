import { useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import type {
  AppInfoResponse,
  ItLifecycleSettingsResult,
  ItLifecycleSettingsValue,
  OpsiConnectionStatusResult,
  OpsiSettingsResult,
  OpsiSettingsValue,
  ServiceCredentialStatus,
  ServiceCredentialStatuses,
} from '../../shared/api-types';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Card } from '../../shared/ui/Card';
import { Button } from '../../shared/ui/Button';
import { Field } from '../../shared/ui/Field';
import { Input } from '../../shared/ui/Input';
import { Spinner } from '../../shared/ui/Spinner';
import { ErrorState } from '../../shared/ui/States';
import { CredentialFields, type CredentialValues } from '../../shared/targets/TargetSelector';
import { useTargetsOptional } from '../../shared/targets/TargetContext';
import { useEnvironmentOptional } from '../../shared/environment/EnvironmentContext';

const emptyKasperskyCredentials: CredentialValues = { userName: '', domain: '', password: '' };

export function SettingsPage() {
  const targets = useTargetsOptional();
  const environment = useEnvironmentOptional();
  const [appInfo, setAppInfo] = useState<AppInfoResponse | null>(null);
  const [itLifecycle, setItLifecycle] = useState<ItLifecycleSettingsValue | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [restartRequired, setRestartRequired] = useState(false);
  const [kasperskyDraft, setKasperskyDraft] = useState<CredentialValues>(emptyKasperskyCredentials);
  const [opsi, setOpsi] = useState<OpsiSettingsValue | null>(null);
  const [opsiStatus, setOpsiStatus] = useState<OpsiConnectionStatusResult | null>(null);
  const [opsiUserName, setOpsiUserName] = useState('');
  const [opsiPassword, setOpsiPassword] = useState('');
  const [rememberOpsi, setRememberOpsi] = useState(true);
  const [credentialStatuses, setCredentialStatuses] = useState<ServiceCredentialStatuses | null>(null);
  const [opsiBusy, setOpsiBusy] = useState(false);
  const [opsiError, setOpsiError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      invoke<AppInfoResponse>('system', 'getAppInfo'),
      invoke<ItLifecycleSettingsResult>('system', 'getItLifecycleSettings'),
      invoke<OpsiSettingsResult>('system', 'getOpsiSettings'),
      invoke<OpsiConnectionStatusResult>('patchmanagement', 'getConnectionStatus'),
      invoke<ServiceCredentialStatuses>('system', 'getServiceCredentialStatuses'),
    ])
      .then(([info, settings, opsiSettings, connection, credentials]) => {
        setAppInfo(info);
        setItLifecycle(settings.settings);
        setOpsi(opsiSettings.settings);
        setOpsiStatus(connection);
        setOpsiError(connection.connectionError ?? null);
        setCredentialStatuses(credentials);
        if (credentials.kaspersky.saved) {
          setKasperskyDraft((current) => ({ ...current, userName: credentials.kaspersky.userName ?? '', domain: credentials.kaspersky.domain ?? '' }));
        }
        if (credentials.opsi.saved) setOpsiUserName(credentials.opsi.userName ?? '');
      })
      .catch((error: unknown) => setLoadError(errorText(error)))
      .finally(() => setLoading(false));
  }, []);

  const saveItLifecycle = () => {
    if (!itLifecycle) return;
    setSaving(true);
    setSaveError(null);
    invoke<ItLifecycleSettingsResult>('system', 'saveItLifecycleSettings', { settings: itLifecycle })
      .then((result) => {
        setItLifecycle(result.settings);
        setRestartRequired(result.restartRequired);
      })
      .catch((error: unknown) => setSaveError(errorText(error)))
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
    if (!opsi) return;
    setOpsiBusy(true); setOpsiError(null);
    invoke<OpsiSettingsResult>('system', 'saveOpsiSettings', { settings: opsi })
      .then((next) => { setOpsi(next.settings); setRestartRequired(next.restartRequired); })
      .catch((caught: unknown) => setOpsiError(errorText(caught)))
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
    }).catch((caught: unknown) => setOpsiError(errorText(caught))).finally(() => setOpsiBusy(false));
  };

  const connectStoredOpsi = () => {
    if (!opsi) return;
    setOpsiBusy(true); setOpsiError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'connect', {
      server: opsi.server, userName: '', password: null,
      trustServerCertificate: opsi.trustServerCertificate, useStoredCredential: true,
    }).then((status) => {
      setOpsiStatus(status); environment?.invalidate();
    }).catch((caught: unknown) => setOpsiError(errorText(caught))).finally(() => setOpsiBusy(false));
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
    }).catch((caught: unknown) => setSaveError(errorText(caught))).finally(() => setSaving(false));
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
      }).catch((caught: unknown) => setSaveError(errorText(caught))).finally(() => setSaving(false));
  };

  const disconnectOpsi = () => {
    setOpsiBusy(true); setOpsiError(null);
    invoke<OpsiConnectionStatusResult>('patchmanagement', 'disconnect', {})
      .then((status) => { setOpsiStatus(status); environment?.invalidate(); })
      .catch((caught: unknown) => setOpsiError(errorText(caught))).finally(() => setOpsiBusy(false));
  };

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Settings" subtitle="App-wide configuration for this machine" />

      {loading && <Spinner label="Loading settings …" />}
      {loadError && <ErrorState title="Settings could not be loaded" message={loadError} />}

      {!loading && appInfo && (
        <Card title="Effective configuration">
          <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1.5 text-sm">
            <dt className="text-slate-400">Version</dt>
            <dd className="font-mono text-slate-200">{appInfo.version}</dd>
            <dt className="text-slate-400">Machine</dt>
            <dd className="font-mono text-slate-200">{appInfo.machineName}</dd>
            <dt className="text-slate-400">Privilege</dt>
            <dd className="text-slate-200">{appInfo.isElevated ? 'Administrator' : 'Standard user'}</dd>
            <dt className="text-slate-400">Max parallel scans</dt>
            <dd className="font-mono text-slate-200">{appInfo.maxParallelScans}</dd>
            <dt className="text-slate-400">Database</dt>
            <dd className="break-all font-mono text-xs text-slate-300">{appInfo.databasePath}</dd>
            <dt className="text-slate-400">Log directory</dt>
            <dd className="break-all font-mono text-xs text-slate-300">{appInfo.logDirectory}</dd>
          </dl>
          <div className="mt-3">
            <Button variant="secondary" onClick={() => invoke('system', 'openLogsFolder').catch(() => {})}>
              Open log folder
            </Button>
          </div>
        </Card>
      )}

      {itLifecycle && (
        <Card title="IT Lifecycle / Environment Health">
          <div className="mb-5 rounded-md border border-slate-700 bg-slate-900/40 p-4">
            <h3 className="mb-1 text-sm font-semibold text-slate-200">Kaspersky session account</h3>
            {credentialStatuses?.kaspersky.saved && (
              <div className="mb-3 flex flex-wrap items-center gap-3 rounded border border-emerald-800/60 bg-emerald-950/20 px-3 py-2">
                <p className="text-sm text-slate-300">Saved securely in Windows Credential Manager as <span className="font-mono text-slate-100">{credentialStatuses.kaspersky.domain ? `${credentialStatuses.kaspersky.domain}\\` : ''}{credentialStatuses.kaspersky.userName}</span>. Environment Health uses it automatically.</p>
                <Button variant="secondary" onClick={() => deleteCredential('KASPERSKY')} disabled={saving}>Remove saved KSC credential</Button>
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
                  placeholder="Nicht für Kaspersky geeignete Geräte"
                />
              )}
            </Field>
          </div>

          <div className="mt-4 flex flex-wrap items-center gap-3">
            <Button variant="primary" onClick={saveItLifecycle} disabled={saving}>
              {saving ? 'Saving…' : 'Save IT Lifecycle settings'}
            </Button>
            {restartRequired && (
              <p role="status" className="text-sm text-warn-300">
                Saved. Restart WEC to apply these settings.
              </p>
            )}
          </div>
          {saveError && <div className="mt-3"><ErrorState title="Settings could not be saved" message={saveError} /></div>}
        </Card>
      )}

      {opsi && (
        <Card title="opsi / Patch Management">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <Field label="opsi server" hint="Host name or HTTPS URL used by all WEC areas.">{(id) => <Input id={id} value={opsi.server} onChange={(event) => setOpsi({ ...opsi, server: event.target.value })} placeholder="opsi.example.local" />}</Field>
            <Field label="opsi port">{(id) => <Input id={id} type="number" min={1} max={65535} value={opsi.port} onChange={(event) => setOpsi({ ...opsi, port: Number(event.target.value) })} />}</Field>
            <Field label="opsi timeout (seconds)">{(id) => <Input id={id} type="number" min={1} max={600} value={opsi.requestTimeoutSeconds} onChange={(event) => setOpsi({ ...opsi, requestTimeoutSeconds: Number(event.target.value) })} />}</Field>
            <Field label="Default depot filter">{(id) => <Input id={id} value={opsi.defaultDepotFilter} onChange={(event) => setOpsi({ ...opsi, defaultDepotFilter: event.target.value })} placeholder="Optional" />}</Field>
          </div>
          <div className="mt-4"><Button variant="primary" onClick={saveOpsi} disabled={opsiBusy}>{opsiBusy ? 'Saving…' : 'Save opsi settings'}</Button></div>

          <div className="mt-5 rounded-md border border-slate-700 bg-slate-900/40 p-4">
            <h3 className="mb-2 text-sm font-semibold text-slate-200">opsi session account</h3>
            {credentialStatuses?.opsi.saved && <div className="mb-3 flex flex-wrap items-center gap-3 rounded border border-emerald-800/60 bg-emerald-950/20 px-3 py-2"><p className="text-sm text-slate-300">Saved securely in Windows Credential Manager as <span className="font-mono text-slate-100">{credentialStatuses.opsi.userName}</span>.</p><Button variant="secondary" onClick={() => deleteCredential('OPSI')} disabled={saving}>Remove saved opsi credential</Button></div>}
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
          {opsiError && <div className="mt-3"><ErrorState title="opsi settings or connection failed" message={opsiError} /></div>}
        </Card>
      )}

      <Card title="Configuration policy">
        <p className="text-sm text-slate-400">
          User-configurable operating parameters belong on this page. Future modules should add their settings here.
          Deployment defaults remain in <span className="font-mono text-slate-300">appsettings.json</span>; saved values are
          merged into <span className="font-mono text-slate-300">%APPDATA%\Wec\usersettings.json</span>. KSC and opsi passwords
          can be stored in the current Windows user's Credential Manager; the normal admin sign-in remains session-only.
        </p>
      </Card>
    </div>
  );
}
