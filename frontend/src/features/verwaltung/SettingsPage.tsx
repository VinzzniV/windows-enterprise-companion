import { useEffect, useState } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { AppInfoResponse } from '../../shared/api-types';
import { PageHeader } from '../../shared/ui/PageHeader';
import { Card } from '../../shared/ui/Card';
import { Button } from '../../shared/ui/Button';
import { Spinner } from '../../shared/ui/Spinner';

/**
 * App-wide settings surface. For now it shows the effective configuration
 * read-only; the tunables are edited in `usersettings.json` (a write path is a
 * planned follow-up). The log directory opens from here.
 */
export function SettingsPage() {
  const [appInfo, setAppInfo] = useState<AppInfoResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    invoke<AppInfoResponse>('system', 'getAppInfo')
      .then(setAppInfo)
      .catch(() => setAppInfo(null))
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Settings" subtitle="App-wide configuration for this machine" />

      {loading && <Spinner label="Loading settings …" />}

      {!loading && appInfo && (
        <>
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

          <Card title="Editing settings">
            <p className="text-sm text-slate-400">
              These tunables (cache TTL, thresholds, log level, max parallel scans) are edited in{' '}
              <span className="font-mono text-slate-300">%APPDATA%\Wec\usersettings.json</span> and applied on
              restart. An in-app editor is a planned follow-up.
            </p>
          </Card>
        </>
      )}
    </div>
  );
}
