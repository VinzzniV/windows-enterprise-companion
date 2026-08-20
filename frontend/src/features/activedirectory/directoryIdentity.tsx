import { useEffect, useMemo, useRef, useState } from 'react';
import type { AdHygieneRule, AdPrivilegedGroupMember, PrivilegedGroupInfo } from '../../shared/api-types';
import { Badge, type BadgeTone } from '../../shared/ui/Badge';
import { Button } from '../../shared/ui/Button';
import { DataTable, type DataColumn, type DataTablePagination } from '../../shared/ui/DataTable';
import { SemanticStatusBadge, type SemanticStatus } from '../../shared/ui/SemanticStatusBadge';

export type DirectoryEntityType = 'User' | 'Computer' | 'Group' | 'Foreign principal' | 'Directory object' | 'Member';

export interface DirectoryIdentityRow {
  id: string;
  commonName: string;
  identifier: string | null;
  entityType: DirectoryEntityType;
  path: string;
  finding: string;
  findingTone: BadgeTone;
  status: string | null;
  distinguishedName: string;
  lastActivityUtc: string | null;
  recommendation: string;
}

function splitDistinguishedName(value: string): string[] {
  const parts: string[] = [];
  let current = '';
  let escaped = false;
  for (const character of value) {
    if (escaped) {
      current += character;
      escaped = false;
    } else if (character === '\\') {
      current += character;
      escaped = true;
    } else if (character === ',') {
      parts.push(current.trim());
      current = '';
    } else {
      current += character;
    }
  }
  if (current.trim()) parts.push(current.trim());
  return parts;
}

function splitRdn(value: string): { attribute: string; value: string } {
  let escaped = false;
  for (let index = 0; index < value.length; index += 1) {
    const character = value[index];
    if (escaped) {
      escaped = false;
    } else if (character === '\\') {
      escaped = true;
    } else if (character === '=') {
      return {
        attribute: value.slice(0, index).trim().toUpperCase(),
        value: decodeDistinguishedNameValue(value.slice(index + 1).trim()),
      };
    }
  }
  return { attribute: '', value: decodeDistinguishedNameValue(value) };
}

function decodeDistinguishedNameValue(value: string): string {
  return value
    .replace(/\\([0-9a-fA-F]{2})/g, (_match, hex: string) => String.fromCharCode(Number.parseInt(hex, 16)))
    .replace(/\\(.)/g, '$1');
}

export function parseDistinguishedName(distinguishedName: string): { commonName: string; path: string } {
  const rdns = splitDistinguishedName(distinguishedName).map(splitRdn);
  const commonName = rdns[0]?.value || distinguishedName;
  const domain = rdns.filter((rdn) => rdn.attribute === 'DC').map((rdn) => rdn.value).join('.');
  const containers = rdns
    .slice(1)
    .filter((rdn) => rdn.attribute !== 'DC')
    .map((rdn) => rdn.value)
    .reverse();
  const path = [domain, ...containers].filter(Boolean).join(' / ');
  return { commonName, path: path || 'Directory root' };
}

export function directoryEntityTypeForRule(ruleId: string): DirectoryEntityType {
  return ruleId.toUpperCase().includes('COMPUTERS') ? 'Computer' : 'User';
}

function findingLabelForRule(ruleId: string): string {
  const normalized = ruleId.toUpperCase();
  if (normalized.includes('INACTIVE')) return 'Inactive';
  if (normalized.includes('PASSWORD-NEVER-EXPIRES')) return 'Password never expires';
  if (normalized.includes('DISABLED-PRIVILEGED')) return 'Disabled + privileged';
  return 'Hygiene finding';
}

export function privilegedIdentityRows(groups: PrivilegedGroupInfo[]): DirectoryIdentityRow[] {
  return groups.flatMap((group) => group.memberDistinguishedNames.map((distinguishedName) => {
    const identity = parseDistinguishedName(distinguishedName);
    return {
      id: `${group.distinguishedName}|${distinguishedName}`,
      commonName: identity.commonName,
      identifier: null,
      entityType: 'Member',
      path: identity.path,
      finding: group.groupName,
      findingTone: 'info',
      status: null,
      distinguishedName,
      lastActivityUtc: null,
      recommendation: `Review whether this account still needs direct membership in ${group.groupName}.`,
    };
  }));
}

export function ruleIdentityRows(rule: AdHygieneRule): DirectoryIdentityRow[] {
  return rule.examples.map((account) => {
    const identity = parseDistinguishedName(account.distinguishedName);
    return {
      id: `${rule.ruleId}|${account.distinguishedName}`,
      commonName: identity.commonName,
      identifier: account.name === identity.commonName ? null : account.name,
      entityType: directoryEntityTypeForRule(rule.ruleId),
      path: identity.path,
      finding: findingLabelForRule(rule.ruleId),
      findingTone: 'warn',
      status: null,
      distinguishedName: account.distinguishedName,
      lastActivityUtc: account.lastLogonUtc,
      recommendation: rule.recommendation,
    };
  });
}

function normalizeEntityType(entityType: string): DirectoryEntityType {
  const knownTypes: DirectoryEntityType[] = ['User', 'Computer', 'Group', 'Foreign principal', 'Directory object'];
  return knownTypes.includes(entityType as DirectoryEntityType)
    ? entityType as DirectoryEntityType
    : 'Directory object';
}

export interface DirectoryAccountStatusPresentation {
  status: SemanticStatus;
  context: string;
  technicalDetail: string | null;
}

export function directoryAccountStatus(rawStatus: string | null): DirectoryAccountStatusPresentation {
  const providerStatus = rawStatus?.trim() || null;
  const technicalDetail = providerStatus ? `Directory account status: ${providerStatus}` : null;
  switch (providerStatus?.toUpperCase()) {
    case 'ENABLED':
      return {
        status: { dimension: 'lifecycle', value: 'current' },
        context: 'Account enabled',
        technicalDetail,
      };
    case 'DISABLED':
      return {
        status: { dimension: 'lifecycle', value: 'disabled' },
        context: 'Account disabled',
        technicalDetail,
      };
    case 'UNKNOWN':
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Account status unavailable',
        technicalDetail,
      };
    case undefined:
      return {
        status: { dimension: 'availability', value: 'not-applicable' },
        context: 'No account lifecycle state',
        technicalDetail: null,
      };
    default:
      return {
        status: { dimension: 'availability', value: 'unknown' },
        context: 'Unmapped account status',
        technicalDetail,
      };
  }
}

function DirectoryAccountStatusBadge({ rawStatus }: { rawStatus: string | null }) {
  const presentation = directoryAccountStatus(rawStatus);
  return <span
    className="flex flex-col items-start gap-0.5"
    title={presentation.technicalDetail ?? undefined}
  >
    <SemanticStatusBadge status={presentation.status} />
    <span className="text-xs text-slate-400">{presentation.context}</span>
  </span>;
}

export function privilegedGroupMemberRows(
  group: PrivilegedGroupInfo,
  members: AdPrivilegedGroupMember[],
): DirectoryIdentityRow[] {
  return members.map((member) => {
    const identity = parseDistinguishedName(member.distinguishedName);
    return {
      id: `${group.distinguishedName}|${member.distinguishedName}`,
      commonName: identity.commonName,
      identifier: member.accountName && member.accountName !== identity.commonName ? member.accountName : null,
      entityType: normalizeEntityType(member.entityType),
      path: identity.path,
      finding: group.groupName,
      findingTone: 'info',
      status: member.accountStatus,
      distinguishedName: member.distinguishedName,
      lastActivityUtc: member.lastLogonUtc,
      recommendation: `Review whether this account still needs direct membership in ${group.groupName}.`,
    };
  });
}

function matchesQuery(row: DirectoryIdentityRow, query: string): boolean {
  const normalized = query.trim().toLocaleLowerCase();
  if (!normalized) return true;
  return [
    row.commonName,
    row.identifier,
    row.entityType,
    row.path,
    row.finding,
    row.status,
    row.distinguishedName,
  ].some((value) => value?.toLocaleLowerCase().includes(normalized));
}

interface DirectoryIdentityTableProps {
  rows: DirectoryIdentityRow[];
  totalCount: number;
  totalLabel: string;
  query: string;
  pagination?: DataTablePagination;
  loading?: boolean;
}

export function DirectoryIdentityTable({
  rows,
  totalCount,
  totalLabel,
  query,
  pagination,
  loading = false,
}: DirectoryIdentityTableProps) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [copyStatus, setCopyStatus] = useState<'copied' | 'failed' | null>(null);
  const detailsRef = useRef<HTMLElement>(null);
  const filteredRows = useMemo(() => rows.filter((row) => matchesQuery(row, query)), [query, rows]);
  const selected = filteredRows.find((row) => row.id === selectedId) ?? null;
  const hasStatus = rows.some((row) => row.status !== null);
  const columns = useMemo<DataColumn<DirectoryIdentityRow>[]>(() => [
    {
      header: 'Entity',
      cell: (row) => <div><div className="font-medium text-slate-100">{row.commonName}</div>{row.identifier && <div className="font-mono text-xs text-slate-400">{row.identifier}</div>}</div>,
      sortValue: (row) => row.commonName,
    },
    { header: 'Type', cell: (row) => <Badge tone="neutral">{row.entityType}</Badge>, sortValue: (row) => row.entityType },
    { header: 'Directory path', cell: (row) => <span className="text-slate-300">{row.path}</span>, sortValue: (row) => row.path },
    { header: 'Finding', cell: (row) => <Badge tone={row.findingTone}>{row.finding}</Badge>, sortValue: (row) => row.finding },
    ...(hasStatus ? [{ header: 'Status', cell: (row: DirectoryIdentityRow) => <DirectoryAccountStatusBadge rawStatus={row.status} />, sortValue: (row: DirectoryIdentityRow) => row.status }] : []),
    { header: 'Last activity', cell: (row) => row.lastActivityUtc ? new Date(row.lastActivityUtc).toLocaleDateString() : 'Unknown', sortValue: (row) => row.lastActivityUtc },
  ], [hasStatus]);

  const copyDistinguishedName = async () => {
    if (!selected) return;
    try {
      if (!navigator.clipboard?.writeText) throw new Error('Clipboard API unavailable');
      await navigator.clipboard.writeText(selected.distinguishedName);
      setCopyStatus('copied');
    } catch {
      setCopyStatus('failed');
    }
  };

  useEffect(() => {
    if (selected) detailsRef.current?.scrollIntoView({ block: 'nearest' });
  }, [selected]);

  const rangeStart = pagination && totalCount > 0
    ? (pagination.page - 1) * pagination.pageSize + 1
    : 0;
  const rangeEnd = pagination ? rangeStart + filteredRows.length - 1 : 0;

  return <div className="flex flex-col gap-3">
    <div className="text-xs text-slate-400">
      {pagination ? (
        <p>{rangeStart}–{Math.max(rangeStart, rangeEnd)} of {totalCount.toLocaleString()} {totalLabel}</p>
      ) : (
        <>
          <p>
            Showing {rows.length} loaded example{rows.length === 1 ? '' : 's'} of {totalCount.toLocaleString()} {totalLabel}.
          </p>
          {totalCount > rows.length && <p>Search is limited to these loaded examples.</p>}
        </>
      )}
    </div>
    <DataTable
      columns={columns}
      rows={filteredRows}
      getRowKey={(row) => row.id}
      onRowClick={(row) => { setSelectedId(row.id); setCopyStatus(null); }}
      isRowActive={(row) => row.id === selectedId}
      emptyMessage="No loaded identities match this search."
      pagination={pagination}
      loading={loading}
      sort={pagination ? null : undefined}
      onSortChange={pagination ? () => undefined : undefined}
    />
    {selected && <section
      ref={detailsRef}
      aria-label={`Directory identity details for ${selected.commonName}`}
      className="rounded border border-slate-700 bg-slate-950/50 p-3"
    >
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div><h3 className="font-medium text-slate-100">{selected.commonName}</h3><p className="text-xs text-slate-400">Finding context and complete directory identity</p></div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={() => void copyDistinguishedName()} aria-label={`Copy DN for ${selected.commonName}`}>Copy DN</Button>
          {copyStatus && <span className={`text-xs ${copyStatus === 'copied' ? 'text-ok-400' : 'text-fail-400'}`} role="status">{copyStatus === 'copied' ? 'DN copied.' : 'Copy failed.'}</span>}
        </div>
      </div>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 text-sm">
        <dt className="text-slate-400">Type</dt><dd>{selected.entityType}</dd>
        {selected.identifier && <><dt className="text-slate-400">Account identifier</dt><dd className="font-mono text-xs">{selected.identifier}</dd></>}
        <dt className="text-slate-400">Directory path</dt><dd>{selected.path}</dd>
        <dt className="text-slate-400">Finding</dt><dd>{selected.finding}</dd>
        {selected.status && <><dt className="text-slate-400">Status</dt><dd>{selected.status}</dd></>}
        <dt className="text-slate-400">Last activity</dt><dd>{selected.lastActivityUtc ? new Date(selected.lastActivityUtc).toLocaleString() : 'Unknown'}</dd>
        <dt className="text-slate-400">Distinguished name</dt><dd className="break-all font-mono text-xs text-slate-300">{selected.distinguishedName}</dd>
      </dl>
      <p className="mt-3 text-sm text-slate-300"><span className="font-medium">Recommended action:</span> {selected.recommendation}</p>
    </section>}
  </div>;
}
