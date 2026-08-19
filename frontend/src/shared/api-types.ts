/**
 * Public frontend facade for the generated C# bridge contracts.
 *
 * Keep only intentional UI aliases or event payloads here. DTO and enum shapes
 * belong in api-types.generated.ts and are refreshed by Wec.ContractGenerator.
 */
export * from './api-types.generated';

export type TargetRole = 'Client' | 'PrintServer' | 'OpsiServer' | 'DomainController' | 'Generic';

export type SecurityScanRequest = import('./api-types.generated').RunSecurityScanRequest;

export type RecentLogEntriesResult = import('./api-types.generated').RecentLogEntriesResponse;
export type ClearRecentLogEntriesResult = import('./api-types.generated').ClearRecentLogEntriesResponse;
export type HostProbe = import('./api-types.generated').HostProbeResult;
export type ProbeHostsResult = import('./api-types.generated').ProbeHostsResponse;
export type ReportOverviewRequest = import('./api-types.generated').GetReportOverviewRequest;
export type ExportReportRequest = import('./api-types.generated').ExportHtmlReportRequest;
export type AdAnalysisRequest = import('./api-types.generated').GetAdOverviewRequest;

// Stable UI names retained while the generated declarations use their C# names.
export type LifecycleCaseType = import('./api-types.generated').CaseType;
export type LifecycleCaseStatus = import('./api-types.generated').CaseStatus;
export type LifecycleTaskArea = import('./api-types.generated').TaskArea;
export type LifecycleTask = import('./api-types.generated').TaskDetails;
export type LifecycleCase = import('./api-types.generated').CaseDetails;
export type LifecycleCaseResult = import('./api-types.generated').CaseResult;
export type LifecycleTaskResult = import('./api-types.generated').TaskResult;
export type LifecycleAuditListResult = import('./api-types.generated').AuditListResult;
export type LifecycleDepartment = import('./api-types.generated').DepartmentInfo;
export type LifecycleDepartmentListResult = import('./api-types.generated').DepartmentListResult;
