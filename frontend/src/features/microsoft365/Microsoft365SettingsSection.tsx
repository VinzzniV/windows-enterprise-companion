import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type { Microsoft365Configuration, Microsoft365SettingsResult } from '../../shared/api-types.generated';
import { BridgeInvokeError, invoke } from '../../shared/bridge/bridgeClient';
import { presentError } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Input } from '../../shared/ui/Input';
import { Spinner } from '../../shared/ui/Spinner';

export function Microsoft365SettingsSection({ onDirtyChange }: { onDirtyChange(dirty: boolean): void }) {
  const [settings, setSettings] = useState<Microsoft365Configuration | null>(null);
  const [saved, setSaved] = useState<Microsoft365Configuration | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [restartRequired, setRestartRequired] = useState(false);
  const [reload, setReload] = useState(0);
  const dirty = settings !== null && JSON.stringify(settings) !== JSON.stringify(saved);
  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => {
    let active = true;
    setBusy(true); setError(null);
    void invoke<Microsoft365SettingsResult>('system', 'getMicrosoft365Settings')
      .then(result => { if (active && result) { setSettings(result.settings); setSaved(result.settings); } })
      .catch(caught => { if (active) setError(presentError(caught, { message: 'Microsoft 365 defaults could not be loaded.' }).message); })
      .finally(() => { if (active) setBusy(false); });
    return () => { active = false; };
  }, [reload]);

  const save = async () => {
    if (!settings) return;
    setBusy(true); setError(null); setRestartRequired(false);
    try {
      const result = await invoke<Microsoft365SettingsResult>('system', 'saveMicrosoft365Settings', { settings });
      setSettings(result.settings); setSaved(result.settings); setRestartRequired(result.restartRequired);
    } catch (caught) {
      setError(caught instanceof BridgeInvokeError ? caught.error.message
        : presentError(caught, { message: 'Microsoft 365 defaults could not be saved.' }).message);
    } finally { setBusy(false); }
  };

  return <Card title="Microsoft 365 connection defaults">
    <p className="mb-3 text-sm text-muted">Save non-secret connection defaults for the next WEC start. Sign-in and consent take place in the Microsoft 365 workspace. Saving does not connect to Microsoft Graph.</p>
    {busy && <Spinner label="Updating Microsoft 365 defaults…" />}
    {settings && <>
      <div className="grid gap-3 md:grid-cols-2">
        <label className="text-xs text-muted">Default tenant ID<Input disabled={busy} value={settings.tenantId}
          onChange={event => setSettings({ ...settings, tenantId: event.target.value })} /></label>
        <label className="text-xs text-muted">Default client ID<Input disabled={busy} value={settings.clientId}
          onChange={event => setSettings({ ...settings, clientId: event.target.value })} /></label>
      </div>
      <div className="my-3 flex flex-wrap gap-4 text-sm">
        <label><input type="checkbox" disabled={busy} checked={settings.enableIntune ?? false}
          onChange={event => setSettings({ ...settings, enableIntune: event.target.checked })} /> Include Intune by default</label>
        <label><input type="checkbox" disabled={busy} checked={settings.enableAuthenticationReports ?? false}
          onChange={event => setSettings({ ...settings, enableAuthenticationReports: event.target.checked })} /> Include sign-in and MFA reports by default</label>
      </div>
      <p className="mb-3 text-xs text-muted">Enter both IDs as GUIDs, or leave both empty to clear defaults. Optional data requires additional admin-consented read permissions. No passwords, secrets, tokens or cloud records are saved here.</p>
      <Button disabled={busy || !dirty} onClick={() => void save()}>Save Microsoft 365 defaults</Button>
      {dirty && <p className="mt-2 text-xs text-warn-400">Unsaved changes</p>}
    </>}
    {error && <div className="mt-3"><p role="alert" className="text-sm text-fail-400">{error}</p>
      {!settings && <Button disabled={busy} onClick={() => setReload(value => value + 1)}>Retry defaults</Button>}</div>}
    {restartRequired && <p role="status" className="mt-3 text-sm text-warn-400">Defaults saved. Restart WEC to apply them; the current session is unchanged.</p>}
    <Link className="mt-3 block text-sm text-accent-400 underline" to="/microsoft365">Open Microsoft 365</Link>
  </Card>;
}
