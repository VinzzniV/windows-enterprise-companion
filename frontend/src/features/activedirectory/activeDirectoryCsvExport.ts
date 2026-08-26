import type {
  AdHygieneResult,
  AdHygieneRule,
  AdHygieneRulePage,
  AdOverviewResult,
  AdPrivilegedGroupMemberPage,
  DirectoryConnectionRequest,
  ExportActiveDirectoryCsvResult,
  PrivilegedGroupInfo,
} from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { toCsv, type CsvColumn } from '../../shared/csv';

const pageSize = 100;

interface ActiveDirectoryExportRow {
  section: string;
  domain: string | null;
  capturedAtUtc: string;
  objectType: string;
  name: string | null;
  accountName: string | null;
  status: string | null;
  finding: string | null;
  value: string | number | null;
  lastActivityUtc: string | null;
  distinguishedName: string | null;
  recommendation: string | null;
}

const columns: CsvColumn<ActiveDirectoryExportRow>[] = [
  { key: 'section', header: 'Section', value: (row) => row.section },
  { key: 'domain', header: 'Domain', value: (row) => row.domain },
  { key: 'capturedAtUtc', header: 'CapturedAtUtc', value: (row) => row.capturedAtUtc },
  { key: 'objectType', header: 'ObjectType', value: (row) => row.objectType },
  { key: 'name', header: 'Name', value: (row) => row.name },
  { key: 'accountName', header: 'AccountName', value: (row) => row.accountName },
  { key: 'status', header: 'Status', value: (row) => row.status },
  { key: 'finding', header: 'FindingOrMetric', value: (row) => row.finding },
  { key: 'value', header: 'Value', value: (row) => row.value },
  { key: 'lastActivityUtc', header: 'LastActivityUtc', value: (row) => row.lastActivityUtc },
  { key: 'distinguishedName', header: 'DistinguishedName', value: (row) => row.distinguishedName },
  { key: 'recommendation', header: 'Recommendation', value: (row) => row.recommendation },
];

function overviewRows(overview: AdOverviewResult): ActiveDirectoryExportRow[] {
  const common = {
    section: 'Overview',
    domain: overview.domainName,
    capturedAtUtc: overview.capturedAtUtc,
    accountName: null,
    status: null,
    lastActivityUtc: null,
    recommendation: null,
  };
  return [
    {
      ...common,
      objectType: 'Directory',
      name: 'Default naming context',
      finding: 'Naming context',
      value: overview.defaultNamingContext,
      distinguishedName: overview.defaultNamingContext,
    },
    ...[
      ['Users', overview.userCount],
      ['Disabled users', overview.disabledUserCount],
      ['Groups', overview.groupCount],
      ['Computers', overview.computerCount],
    ].map(([name, value]) => ({
      ...common,
      objectType: 'Metric',
      name: String(name),
      finding: 'Count',
      value: Number(value),
      distinguishedName: null,
    })),
    ...overview.domainControllers.map((controller) => ({
      ...common,
      objectType: 'Domain controller',
      name: controller.hostName,
      finding: null,
      value: null,
      distinguishedName: controller.distinguishedName,
    })),
  ];
}

async function allRuleMatches(
  rule: AdHygieneRule,
  evaluatedAtUtc: string,
  connection: DirectoryConnectionRequest | null,
  onProgress: (message: string) => void,
): Promise<AdHygieneRulePage['items']> {
  if (rule.matchCount === 0) return [];

  const items: AdHygieneRulePage['items'] = [];
  let page = 1;
  let total = rule.matchCount;
  do {
    onProgress(`Loading ${rule.title}: ${Math.min((page - 1) * pageSize + 1, total)}–${Math.min(page * pageSize, total)} of ${total} …`);
    const result = await invoke<AdHygieneRulePage>('activedirectory', 'getHygieneRulePage', {
      connection,
      ruleId: rule.ruleId,
      evaluatedAtUtc,
      query: null,
      page,
      pageSize,
    });
    total = result.totalCount;
    items.push(...result.items);
    if (result.items.length === 0 && items.length < total) {
      throw new Error(`The directory returned an incomplete page for ${rule.title}.`);
    }
    page += 1;
  } while (items.length < total);

  return items;
}

async function allPrivilegedGroupMembers(
  group: PrivilegedGroupInfo,
  connection: DirectoryConnectionRequest | null,
  onProgress: (message: string) => void,
): Promise<AdPrivilegedGroupMemberPage['items']> {
  if (group.directMemberCount === 0) return [];

  const items: AdPrivilegedGroupMemberPage['items'] = [];
  let page = 1;
  let total = group.directMemberCount;
  do {
    onProgress(`Loading ${group.groupName} members: ${Math.min((page - 1) * pageSize + 1, total)}–${Math.min(page * pageSize, total)} of ${total} …`);
    const result = await invoke<AdPrivilegedGroupMemberPage>(
      'activedirectory',
      'getPrivilegedGroupMemberPage',
      {
        connection,
        groupDistinguishedName: group.distinguishedName,
        query: null,
        page,
        pageSize,
      },
    );
    total = result.totalCount;
    items.push(...result.items);
    if (result.items.length === 0 && items.length < total) {
      throw new Error(`The directory returned an incomplete page for ${group.groupName}.`);
    }
    page += 1;
  } while (items.length < total);

  return items;
}

/** Retrieves every paged detail row, then sends one Excel-friendly CSV to the host save dialog. */
export async function exportActiveDirectoryCsv(
  overview: AdOverviewResult,
  hygiene: AdHygieneResult,
  connection: DirectoryConnectionRequest | null,
  onProgress: (message: string) => void,
): Promise<ExportActiveDirectoryCsvResult> {
  const rows = overviewRows(overview);
  const domain = hygiene.domainName;

  for (const group of hygiene.privilegedGroups) {
    rows.push({
      section: 'Privileged groups',
      domain,
      capturedAtUtc: hygiene.capturedAtUtc,
      objectType: 'Privileged group',
      name: group.groupName,
      accountName: null,
      status: null,
      finding: 'Direct member count',
      value: group.directMemberCount,
      lastActivityUtc: null,
      distinguishedName: group.distinguishedName,
      recommendation: 'Review direct membership in this privileged group.',
    });
    const members = await allPrivilegedGroupMembers(group, connection, onProgress);
    rows.push(...members.map((member) => ({
      section: 'Privileged groups',
      domain,
      capturedAtUtc: hygiene.capturedAtUtc,
      objectType: member.entityType,
      name: member.accountName ?? member.distinguishedName,
      accountName: member.accountName,
      status: member.accountStatus,
      finding: group.groupName,
      value: null,
      lastActivityUtc: member.lastLogonUtc,
      distinguishedName: member.distinguishedName,
      recommendation: `Review whether this account still needs direct membership in ${group.groupName}.`,
    })));
  }

  for (const rule of hygiene.rules) {
    rows.push({
      section: 'Hygiene',
      domain,
      capturedAtUtc: hygiene.capturedAtUtc,
      objectType: 'Hygiene rule',
      name: rule.title,
      accountName: null,
      status: null,
      finding: rule.ruleId,
      value: rule.matchCount,
      lastActivityUtc: null,
      distinguishedName: null,
      recommendation: rule.recommendation,
    });
    const matches = await allRuleMatches(rule, hygiene.capturedAtUtc, connection, onProgress);
    rows.push(...matches.map((match) => ({
      section: 'Hygiene',
      domain,
      capturedAtUtc: hygiene.capturedAtUtc,
      objectType: 'Directory account',
      name: match.name,
      accountName: match.name,
      status: null,
      finding: rule.title,
      value: rule.ruleId,
      lastActivityUtc: match.lastLogonUtc,
      distinguishedName: match.distinguishedName,
      recommendation: rule.recommendation,
    })));
  }

  onProgress(`Saving ${rows.length.toLocaleString()} rows …`);
  return invoke<ExportActiveDirectoryCsvResult>('activedirectory', 'exportCsv', {
    csv: toCsv(rows, columns),
  });
}
