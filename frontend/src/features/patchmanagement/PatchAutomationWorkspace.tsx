import { useCallback, useEffect, useState } from 'react';
import type {
  ProductVersionSource,
  VersionSourcesResult,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { Input } from '../../shared/ui/Input';
import { Select } from '../../shared/ui/Select';
import { SemanticStatusBadge } from '../../shared/ui/SemanticStatusBadge';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { ErrorState } from '../../shared/ui/States';
import { manufacturerCheckStatus, manufacturerSourcesStatus } from './patchStatus';

const verifyVersionSourceSaveAction =
  'First check the manufacturer sources to see whether the source was already saved. Repeat the save only if no corresponding source is visible there.';
const verifyVersionSourceDeleteAction =
  'First check the manufacturer sources and history to see whether the source was already removed. Repeat the deletion only if it is still present.';

interface PatchAutomationProduct {
  productId: string;
  name: string | null;
}

interface PatchAutomationWorkspaceProps {
  connected: boolean;
  products: readonly PatchAutomationProduct[];
  checkBusy: boolean;
  checkError: ErrorPresentation | null;
  onCheckAll: () => Promise<boolean>;
  onClearCheckError: () => void;
  onDashboardRefresh: () => void;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

export function ManufacturerStatusBadge({
  status,
  enabled = true,
}: {
  status: string;
  enabled?: boolean;
}) {
  const presentation = manufacturerCheckStatus(status, enabled);
  return (
    <span
      className="inline-flex flex-wrap items-center gap-1.5"
      title={presentation.technicalDetail ?? undefined}
    >
      <SemanticStatusBadge status={presentation.status} />
      {presentation.context && (
        <span className="text-xs text-slate-400">{presentation.context}</span>
      )}
    </span>
  );
}

export function PatchAutomationWorkspace({
  connected,
  products,
  checkBusy,
  checkError,
  onCheckAll,
  onClearCheckError,
  onDashboardRefresh,
}: PatchAutomationWorkspaceProps) {
  const [versionSources, setVersionSources] = useState<ProductVersionSource[]>([]);
  const [loadError, setLoadError] = useState<ErrorPresentation | null>(null);
  const [form, setForm] = useState({ productId: '', sourceUrl: '', versionPattern: '' });
  const [actionError, setActionError] = useState<ErrorPresentation | null>(null);

  const loadVersionSources = useCallback(() => {
    setLoadError(null);
    invoke<VersionSourcesResult>('patchmanagement', 'listVersionSources', {})
      .then((result) => setVersionSources(result.sources))
      .catch((error: unknown) => setLoadError(
        presentError(error, { message: 'The manufacturer sources could not be loaded.' }),
      ));
  }, []);

  useEffect(() => {
    loadVersionSources();
  }, [loadVersionSources]);

  const checkAll = useCallback(() => {
    setActionError(null);
    void onCheckAll().then((confirmed) => {
      if (confirmed) loadVersionSources();
    });
  }, [loadVersionSources, onCheckAll]);

  const saveVersionSource = useCallback(() => {
    onClearCheckError();
    setActionError(null);
    invoke<VersionSourcesResult>('patchmanagement', 'saveVersionSource', {
      ...form,
      enabled: true,
    })
      .then((result) => {
        setVersionSources(result.sources);
        setForm({ productId: '', sourceUrl: '', versionPattern: '' });
      })
      .catch((error: unknown) => setActionError(
        presentError(error, {
          message: 'The manufacturer source could not be confirmed as saved.',
          action: verifyVersionSourceSaveAction,
        }),
      ));
  }, [form, onClearCheckError]);

  const deleteVersionSource = useCallback((productId: string) => {
    onClearCheckError();
    setActionError(null);
    invoke<VersionSourcesResult>('patchmanagement', 'deleteVersionSource', { productId })
      .then((result) => {
        setVersionSources(result.sources);
        if (connected) onDashboardRefresh();
      })
      .catch((error: unknown) => setActionError(
        presentError(error, {
          message: 'The manufacturer source could not be confirmed as removed.',
          action: verifyVersionSourceDeleteAction,
        }),
      ));
  }, [connected, onClearCheckError, onDashboardRefresh]);

  const sourcesPresentation = manufacturerSourcesStatus(versionSources, loadError !== null);

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card title="Scheduled checks">
        <div className="flex flex-col gap-3 text-sm">
          <div className="flex items-center justify-between gap-3 rounded border border-slate-800 p-3">
            <div>
              <div className="font-medium text-slate-200">Depot comparison</div>
              <div className="text-xs text-muted">
                Compares package availability and versions across all depots.
              </div>
            </div>
            <StatusBadge variant="info">On refresh</StatusBadge>
          </div>
          <div className="flex items-center justify-between gap-3 rounded border border-slate-800 p-3">
            <div>
              <div className="font-medium text-slate-200">Manufacturer version check</div>
              <div className="text-xs text-muted">
                Due sources are checked daily when the connected overview opens.
              </div>
            </div>
            <span className="inline-flex flex-wrap items-center gap-1.5">
              <SemanticStatusBadge status={sourcesPresentation.status} />
              {sourcesPresentation.context && (
                <span className="text-xs text-slate-400">{sourcesPresentation.context}</span>
              )}
            </span>
          </div>
          <Button
            variant="primary"
            disabled={!connected || checkBusy}
            onClick={checkAll}
          >
            {checkBusy ? 'Running checks…' : 'Run all checks now'}
          </Button>
        </div>
      </Card>

      <Card title="Controlled rollout">
        <ol className="flex flex-col gap-3 text-sm">
          {[
            ['1', 'Check manufacturer version', 'Verify the version source for each package'],
            ['2', 'Update test depot', 'Confirm the SSH preview and install the repository package'],
            ['3', 'Deploy to the test group', 'Select test clients, install, and verify the application'],
            ['4', 'Approve and distribute', 'Approve the pilot and align the remaining depots'],
            ['5', 'Roll out broadly', 'Set outdated clients to setup in a controlled deployment'],
          ].map(([number, title, description]) => (
            <li key={number} className="flex gap-3">
              <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-slate-800 text-xs text-slate-300">
                {number}
              </span>
              <span>
                <span className="block font-medium text-slate-200">{title}</span>
                <span className="text-xs text-muted">{description}</span>
              </span>
            </li>
          ))}
        </ol>
      </Card>

      <div className="lg:col-span-2">
        <Card title="Manufacturer sources">
          <div className="flex flex-col gap-4">
            <p className="text-sm text-slate-400">
              Configure one HTTPS page or release API per package. The regular expression must
              capture the version in its first group, for example
              <code className="ml-1 text-accent-300">{'latest-version\\W+([0-9.]+)'}</code>.
            </p>
            <div className="grid gap-3 lg:grid-cols-[minmax(10rem,0.7fr)_minmax(16rem,1.3fr)_minmax(16rem,1.3fr)_auto]">
              <Select
                aria-label="opsi product for manufacturer source"
                value={form.productId}
                onChange={(event) => setForm((previous) => ({
                  ...previous,
                  productId: event.target.value,
                }))}
              >
                <option value="">Select a package</option>
                {products.map((product) => (
                  <option key={product.productId} value={product.productId}>
                    {product.name ?? product.productId}
                  </option>
                ))}
              </Select>
              <Input
                aria-label="Manufacturer source HTTPS URL"
                value={form.sourceUrl}
                onChange={(event) => setForm((previous) => ({
                  ...previous,
                  sourceUrl: event.target.value,
                }))}
                placeholder="https://vendor.example/releases"
              />
              <Input
                aria-label="Version pattern"
                value={form.versionPattern}
                onChange={(event) => setForm((previous) => ({
                  ...previous,
                  versionPattern: event.target.value,
                }))}
                placeholder={'Version\\s+([0-9.]+)'}
              />
              <Button
                variant="primary"
                onClick={saveVersionSource}
                disabled={!form.productId || !form.sourceUrl || !form.versionPattern}
              >
                Save source
              </Button>
            </div>
            {loadError && (
              <ErrorState
                title="Manufacturer sources unavailable"
                {...loadError}
                controls={<Button onClick={loadVersionSources}>Reload manufacturer sources</Button>}
              />
            )}
            {checkError && <ErrorState title="Manufacturer action failed" {...checkError} />}
            {actionError && <ErrorState title="Manufacturer action failed" {...actionError} />}
            {(versionSources.length > 0 || !loadError) && (
              <DataTable
                columns={[
                  { header: 'Package', cell: (item: ProductVersionSource) => item.productId },
                  { header: 'Latest version', mono: true, cell: (item) => item.latestVersion ?? '—' },
                  {
                    header: 'Last checked',
                    cell: (item) => item.lastCheckedUtc
                      ? formatTimestamp(item.lastCheckedUtc)
                      : 'Not checked yet',
                  },
                  {
                    header: 'Status',
                    cell: (item) => (
                      <ManufacturerStatusBadge status={item.checkStatus} enabled={item.enabled} />
                    ),
                  },
                  { header: 'Error', cell: (item) => item.lastError ?? '—' },
                  {
                    header: '',
                    cell: (item) => (
                      <Button variant="ghost" onClick={() => deleteVersionSource(item.productId)}>
                        Remove
                      </Button>
                    ),
                  },
                ]}
                rows={versionSources}
                getRowKey={(item) => item.productId}
                emptyMessage="No manufacturer sources configured yet."
              />
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}
