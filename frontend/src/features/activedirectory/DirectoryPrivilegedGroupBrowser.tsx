import { useState, type FormEvent } from 'react';
import type {
  AdPrivilegedGroupMemberPage,
  DirectoryConnectionRequest,
  PrivilegedGroupInfo,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Input } from '../../shared/ui/Input';
import { ErrorState } from '../../shared/ui/States';
import { DirectoryIdentityTable, privilegedGroupMemberRows } from './directoryIdentity';

const pageSize = 50;

interface DirectoryPrivilegedGroupBrowserProps {
  group: PrivilegedGroupInfo;
  connection: DirectoryConnectionRequest | null;
}

export function DirectoryPrivilegedGroupBrowser({
  group,
  connection,
}: DirectoryPrivilegedGroupBrowserProps) {
  const [result, setResult] = useState<AdPrivilegedGroupMemberPage | null>(null);
  const [draftQuery, setDraftQuery] = useState('');
  const [activeQuery, setActiveQuery] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [lastRequest, setLastRequest] = useState({ page: 1, query: '' });

  const loadPage = async (page: number, query: string) => {
    setLastRequest({ page, query });
    setLoading(true);
    setError(null);
    try {
      const pageResult = await invoke<AdPrivilegedGroupMemberPage>(
        'activedirectory',
        'getPrivilegedGroupMemberPage',
        {
          connection,
          groupDistinguishedName: group.distinguishedName,
          query: query || null,
          page,
          pageSize,
        },
      );
      setResult(pageResult);
      setActiveQuery(query);
    } catch (loadError) {
      setError(presentError(loadError, {
        message: 'The complete privileged group membership could not be loaded.',
      }));
    } finally {
      setLoading(false);
    }
  };

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    void loadPage(1, draftQuery.trim());
  };

  if (result === null && error === null) {
    return <div className="flex flex-wrap items-center gap-3 rounded border border-slate-800 bg-slate-950/30 p-3">
      <div className="min-w-48 flex-1">
        <p className="text-sm font-medium text-slate-200">{group.groupName}</p>
        <p className="text-xs text-slate-400">{group.directMemberCount.toLocaleString()} direct members · loads one read-only page</p>
      </div>
      <Button
        variant="secondary"
        disabled={loading}
        onClick={() => void loadPage(1, '')}
        aria-label={`Browse all ${group.directMemberCount.toLocaleString()} direct members of ${group.groupName}`}
      >
        {loading ? 'Loading members …' : 'Browse all members'}
      </Button>
    </div>;
  }

  if (result === null && error) {
    return <ErrorState
      title={`${group.groupName} membership unavailable`}
      {...error}
      controls={<Button onClick={() => void loadPage(lastRequest.page, lastRequest.query)}>Retry full privileged group membership</Button>}
    />;
  }

  const rows = privilegedGroupMemberRows(group, result?.items ?? []);
  return <section aria-label={`All direct members of ${group.groupName}`} className="flex flex-col gap-3 rounded border border-slate-700 bg-slate-950/30 p-3">
    <div>
      <h3 className="text-sm font-medium text-slate-100">{group.groupName}</h3>
      <p className="text-xs text-slate-400">Direct membership only; nested groups are not expanded.</p>
    </div>
    <form className="flex flex-wrap items-end gap-2" onSubmit={submitSearch}>
      <label className="flex min-w-72 flex-1 flex-col gap-1 text-xs text-slate-400">
        Search all direct member names or accounts
        <Input
          type="search"
          value={draftQuery}
          onChange={(event) => setDraftQuery(event.target.value)}
          aria-label={`Search all direct members of ${group.groupName}`}
          maxLength={100}
        />
      </label>
      <Button type="submit" disabled={loading}>Search group</Button>
    </form>
    {activeQuery && <p className="text-xs text-slate-400">Server filter: “{activeQuery}”</p>}
    {error && <ErrorState
      title={`${group.groupName} membership unavailable`}
      {...error}
      controls={<Button onClick={() => void loadPage(lastRequest.page, lastRequest.query)}>Retry current group page</Button>}
    />}
    {result && <DirectoryIdentityTable
      rows={rows}
      totalCount={result.totalCount}
      totalLabel="direct members"
      query=""
      loading={loading}
      pagination={{
        page: result.page,
        pageSize: result.pageSize,
        total: result.totalCount,
        onPageChange: (page) => void loadPage(page, activeQuery),
      }}
    />}
  </section>;
}
