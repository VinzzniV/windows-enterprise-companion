import { useState, type FormEvent } from 'react';
import type {
  AdHygieneRule,
  AdHygieneRulePage,
  DirectoryConnectionRequest,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Input } from '../../shared/ui/Input';
import { ErrorState } from '../../shared/ui/States';
import { DirectoryIdentityTable, ruleIdentityRows } from './directoryIdentity';

const pageSize = 50;

interface DirectoryRuleBrowserProps {
  rule: AdHygieneRule;
  evaluatedAtUtc: string;
  connection: DirectoryConnectionRequest | null;
}

export function DirectoryRuleBrowser({
  rule,
  evaluatedAtUtc,
  connection,
}: DirectoryRuleBrowserProps) {
  const [result, setResult] = useState<AdHygieneRulePage | null>(null);
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
      const pageResult = await invoke<AdHygieneRulePage>(
        'activedirectory',
        'getHygieneRulePage',
        {
          connection,
          ruleId: rule.ruleId,
          evaluatedAtUtc,
          query: query || null,
          page,
          pageSize,
        },
      );
      setResult(pageResult);
      setActiveQuery(query);
    } catch (loadError) {
      setError(presentError(loadError, {
        message: 'The complete hygiene matches could not be loaded.',
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
    return <div className="flex items-center gap-3">
      <Button
        variant="secondary"
        disabled={loading}
        onClick={() => void loadPage(1, '')}
        aria-label={`Browse all ${rule.matchCount.toLocaleString()} matches`}
      >
        {loading ? 'Loading all matches …' : 'Browse all matches'}
      </Button>
      <span className="text-xs text-slate-400">Loads one read-only 50-account page from the directory.</span>
    </div>;
  }

  if (result === null && error) {
    return <ErrorState
      title="Full hygiene list unavailable"
      {...error}
      controls={<Button onClick={() => void loadPage(lastRequest.page, lastRequest.query)}>Retry full hygiene list</Button>}
    />;
  }

  const rows = ruleIdentityRows({ ...rule, examples: result?.items ?? [] });
  return <div className="flex flex-col gap-3">
    <form className="flex flex-wrap items-end gap-2" onSubmit={submitSearch}>
      <label className="flex min-w-72 flex-1 flex-col gap-1 text-xs text-slate-400">
        Search all matching names or accounts
        <Input
          type="search"
          value={draftQuery}
          onChange={(event) => setDraftQuery(event.target.value)}
          aria-label={`Search all matches for ${rule.title}`}
          maxLength={100}
        />
      </label>
      <Button type="submit" disabled={loading}>Search directory</Button>
    </form>
    {activeQuery && <p className="text-xs text-slate-400">Server filter: “{activeQuery}”</p>}
    {error && <ErrorState
      title="Full hygiene list unavailable"
      {...error}
      controls={<Button onClick={() => void loadPage(lastRequest.page, lastRequest.query)}>Retry current page</Button>}
    />}
    {result && <DirectoryIdentityTable
      rows={rows}
      totalCount={result.totalCount}
      totalLabel="matches"
      query=""
      loading={loading}
      pagination={{
        page: result.page,
        pageSize: result.pageSize,
        total: result.totalCount,
        onPageChange: (page) => void loadPage(page, activeQuery),
      }}
    />}
  </div>;
}
