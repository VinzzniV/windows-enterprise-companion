/**
 * GENERATED FILE. Do not edit by hand.
 * Run: dotnet run --project tools/Wec.ContractGenerator
 *
 * These declarations are generated from every payload/result type reachable
 * from IActionHandler<TPayload, TResult> implementations.
 */
export type ActionEvidenceAvailability = 'AVAILABLE' | 'PARTIAL' | 'NOT_CONNECTED' | 'UNAVAILABLE' | 'TRUNCATED';

export interface ActionEvidenceSourceState {
  source: string;
  availability: ActionEvidenceAvailability;
  explanation: string | null;
}

export interface ClientObservedUserEvidence {
  directorySid: string;
  accountDisplay: string;
  relationshipType: UserDeviceRelationshipType;
  source: string;
  observedAtUtc: string;
  confidence: UserDeviceRelationshipConfidence;
  explanation: string;
}

export interface DeviceCleanupFindingEvidence {
  code: string;
  severity: string;
  message: string;
}

export type DeviceCleanupUserEvidenceAvailability = 'AVAILABLE' | 'PARTIAL' | 'NOT_CAPTURED' | 'UNAVAILABLE' | 'TRUNCATED';

export interface DeviceCleanupUserObservation {
  relationshipType: string;
  sid: string;
  accountDisplay: string | null;
  observedAtUtc: string;
  profileLastUseAtUtc: string | null;
  confidence: string;
  explanation: string;
}

export type DirectoryUserAccessCoverage = 'NOT_EVALUATED' | 'AVAILABLE' | 'UNAVAILABLE';

export type DirectoryUserAccountStateFilter = 'ALL' | 'ENABLED' | 'DISABLED';

export interface DirectoryUserGroup {
  distinguishedName: string;
  name: string;
}

export type DirectoryUserSortDirection = 'ASCENDING' | 'DESCENDING';

export type DirectoryUserSortField = 'DISPLAY_NAME' | 'SAM_ACCOUNT_NAME' | 'DEPARTMENT' | 'CREATED_AT' | 'LAST_LOGON';

export interface HygieneActionDirectoryConnection {
  domain?: string | null;
  server?: string | null;
  userName?: string | null;
  userDomain?: string | null;
  password?: string | null;
}

export interface HygieneActionKasperskyConnection {
  server?: string | null;
  port?: number | null;
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

export type NessusInventoryAvailability = 'AVAILABLE' | 'PARTIAL' | 'NOT_CONNECTED' | 'UNAVAILABLE';

export interface SecurityCoverageReportData {
  isKnown: boolean;
  isComplete: boolean;
  totalChecks: number;
  applicableChecks: number;
  succeededChecks: number;
  failedChecks: number;
  requiresElevationChecks: number;
  notApplicableChecks: number;
}

export type ServiceCredentialKind = 'KASPERSKY' | 'OPSI' | 'NESSUS';

export type UserDeviceRelationshipConfidence = 'HIGH' | 'MEDIUM';

export interface UserDeviceRelationshipCoverage {
  storedDeviceCount: number;
  evidenceCapturedDeviceCount: number;
  notCapturedDeviceCount: number;
  unavailableDeviceCount: number;
  truncatedDeviceCount: number;
}

export interface UserDeviceRelationshipObservation {
  relationshipType: UserDeviceRelationshipType;
  source: string;
  observedAtUtc: string;
  confidence: UserDeviceRelationshipConfidence;
  explanation: string;
  profileLastUseAtUtc: string | null;
}

export type UserDeviceRelationshipType = 'LAST_INTERACTIVE_USER' | 'PROFILE_PRESENT';

export interface TargetRequest {
  host?: string | null;
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

export interface Microsoft365Activity {
  lastSignInAtUtc: string | null;
  lastSuccessfulSignInAtUtc: string | null;
  mfaRegistered: boolean | null;
  mfaCapable: boolean | null;
  methodsRegistered: string[] | null;
}

export interface Microsoft365AssignedLicense {
  skuId: string | null;
  disabledPlans: string[] | null;
}

export interface Microsoft365Configuration {
  tenantId: string;
  clientId: string;
  enableIntune?: boolean;
  enableAuthenticationReports?: boolean;
}

export interface Microsoft365Connection {
  configuration: Microsoft365Configuration;
  connected: boolean;
  account: string | null;
  permissions: Microsoft365ScopeGrant[];
}

export interface Microsoft365Data {
  tenants: Microsoft365Tenant[];
  users: Microsoft365User[];
  groups: Microsoft365Group[];
  devices: Microsoft365Device[];
  managedDevices: Microsoft365ManagedDevice[];
  licenses: Microsoft365License[];
  members: Microsoft365Member[];
  activity: Microsoft365Activity | null;
  totalCount: number | null;
  truncated: boolean;
}

export interface Microsoft365Device {
  id: string | null;
  deviceId: string | null;
  displayName: string | null;
  operatingSystem: string | null;
  operatingSystemVersion: string | null;
  trustType: string | null;
  accountEnabled: boolean | null;
  approximateLastSignInAtUtc: string | null;
}

export interface Microsoft365Group {
  id: string | null;
  displayName: string | null;
  securityEnabled: boolean | null;
  mailEnabled: boolean | null;
  groupTypes: string[] | null;
  membershipRule: string | null;
  membershipRuleProcessingState: string | null;
  visibility: string | null;
}

export interface Microsoft365License {
  id: string | null;
  skuId: string | null;
  skuPartNumber: string | null;
  capabilityStatus: string | null;
  appliesTo: string | null;
  enabledSeats: number | null;
  consumedSeats: number | null;
  servicePlans: Microsoft365ServicePlan[] | null;
}

export interface Microsoft365ManagedDevice {
  id: string | null;
  deviceName: string | null;
  userId: string | null;
  userPrincipalName: string | null;
  operatingSystem: string | null;
  operatingSystemVersion: string | null;
  complianceState: string | null;
  managementState: string | null;
  enrollmentType: string | null;
  lastSyncAtUtc: string | null;
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  entraDeviceId: string | null;
}

export interface Microsoft365Member {
  id: string | null;
  displayName: string | null;
  objectType: string | null;
  userPrincipalName: string | null;
}

export interface Microsoft365Query {
  resource: Microsoft365Resource;
  objectId: string | null;
}

export type Microsoft365Resource = 'TENANT' | 'USERS' | 'USER' | 'GROUPS' | 'GROUP' | 'DEVICES' | 'DEVICE' | 'MANAGED_DEVICES' | 'LICENSES' | 'USER_LICENSES' | 'USER_GROUPS' | 'USER_DEVICES' | 'GROUP_MEMBERS' | 'DEVICE_OWNERS' | 'USER_ACTIVITY' | 'USER_REGISTRATION';

export interface Microsoft365ScopeGrant {
  scope: string;
  granted: boolean;
}

export interface Microsoft365ServicePlan {
  id: string | null;
  name: string | null;
  status: string | null;
}

export interface Microsoft365Tenant {
  id: string | null;
  displayName: string | null;
}

export interface Microsoft365User {
  id: string | null;
  displayName: string | null;
  userPrincipalName: string | null;
  mail: string | null;
  accountEnabled: boolean | null;
  userType: string | null;
  department: string | null;
  jobTitle: string | null;
  officeLocation: string | null;
  createdAtUtc: string | null;
  onPremisesSid: string | null;
  onPremisesImmutableId: string | null;
  assignedLicenses: Microsoft365AssignedLicense[] | null;
}

export interface PortRemovalResult {
  name: string;
  removed: boolean;
  error: string | null;
}

export type PrivilegeLevel = 'STANDARD_USER' | 'ADMINISTRATOR';

export type CheckStatus = 'SUCCEEDED' | 'FAILED' | 'REQUIRES_ELEVATION' | 'NOT_APPLICABLE';

export interface Error {
  code: ErrorCode;
  message: string;
  details: string | null;
  requiredPrivilege: PrivilegeLevel | null;
}

export type ErrorCode = 'INTERNAL_ERROR' | 'ACCESS_DENIED' | 'NOT_FOUND' | 'WMI_UNAVAILABLE' | 'INVALID_REQUEST' | 'UNKNOWN_ACTION' | 'NETWORK_PROBE_FAILED' | 'EVENT_LOG_UNAVAILABLE' | 'FILE_WRITE_FAILED' | 'DIRECTORY_UNAVAILABLE' | 'DNS_RESOLUTION_FAILED' | 'CONNECTION_TIMEOUT' | 'AUTHENTICATION_FAILED' | 'WIN_RM_UNAVAILABLE' | 'UNSUPPORTED_REMOTE_OPERATION' | 'SERVICE_UNAVAILABLE' | 'REMOTE_COMMAND_FAILED' | 'MICROSOFT365_NOT_CONNECTED' | 'MICROSOFT365_AUTHENTICATION_REQUIRED' | 'MICROSOFT365_CONSENT_REQUIRED' | 'MICROSOFT365_PERMISSION_MISSING' | 'MICROSOFT365_ACCESS_DENIED' | 'MICROSOFT365_NOT_FOUND' | 'MICROSOFT365_THROTTLED' | 'MICROSOFT365_UNAVAILABLE' | 'MICROSOFT365_OFFLINE' | 'MICROSOFT365_UNSUPPORTED' | 'MICROSOFT365_CONFIGURATION_INVALID' | 'MICROSOFT365_TIMEOUT' | 'MICROSOFT365_INVALID_RESPONSE';

export interface WingetPackageInfo {
  id: string;
  name: string;
  publisher: string;
  version: string;
  source: string;
  installerType: string;
  architecture: string;
  scope: string;
  isEligible: boolean;
  ineligibilityReason: string | null;
}

export interface ScanError {
  host: string;
  phase: ScanPhase;
  code: ErrorCode;
  message: string;
  details: string | null;
}

export type ScanPhase = 'RESOLVE' | 'CONNECT' | 'AUTHENTICATE' | 'QUERY';

export interface AppInfoResponse {
  version: string;
  databasePath: string;
  logDirectory: string;
  isElevated: boolean;
  maxParallelScans: number;
  maxBatchHosts: number;
  machineName: string;
  runtimeProfile: string;
}

export interface ClearRecentLogEntriesRequest {
}

export interface ClearRecentLogEntriesResponse {
  clearedAtUtc: string;
}

export interface DeleteServiceCredentialRequest {
  kind: ServiceCredentialKind;
}

export interface GetAppInfoRequest {
}

export interface GetItLifecycleSettingsRequest {
}

export interface GetNessusSettingsRequest {
}

export interface GetOpsiSettingsRequest {
}

export interface GetServiceCredentialStatusesRequest {
}

export interface HostProbeResult {
  host: string;
  reachable: boolean;
  manageable: boolean;
}

export interface ItLifecycleSettingsResult {
  settings: ItLifecycleSettingsValue;
  restartRequired: boolean;
}

export interface ItLifecycleSettingsValue {
  inventoryLimit: number;
  staleWarningDays: number;
  staleCriticalDays: number;
  targetAgentVersion: string;
  targetKesVersion: string;
  kaspersky: KasperskySettingsValue;
}

export interface KasperskySettingsValue {
  server: string;
  port: number;
  requestTimeoutSeconds: number;
  trustedCertificateThumbprint: string;
  excludedAdministrationGroups: string[];
}

export interface LogEntry {
  timestamp: string;
  level: string;
  source: string;
  summary: string;
  technicalDetails: string;
}

export interface NessusSettingsResult {
  settings: NessusSettingsValue;
  restartRequired: boolean;
}

export interface NessusSettingsValue {
  serverUrl: string;
  requestTimeoutSeconds: number;
  trustedCertificateThumbprint: string;
  cacheTtlMinutes: number;
  backfillDays: number;
  retentionDays: number;
  staleWarningDays: number;
  staleCriticalDays: number;
  excludedScanIds: number[];
  missingNessusExcludedOuPatterns: string[];
  missingNessusExcludedHostPatterns: string[];
}

export interface OpenLogsFolderRequest {
}

export interface OpenLogsFolderResponse {
  logDirectory: string;
}

export interface OpenPsSessionRequest {
  host: string;
  userName: string | null;
  domain: string | null;
  password: string | null;
}

export interface OpenPsSessionResponse {
  launched: boolean;
}

export interface OpsiSettingsResult {
  settings: OpsiSettingsValue;
  restartRequired: boolean;
}

export interface OpsiSettingsValue {
  server: string;
  port: number;
  requestTimeoutSeconds: number;
  defaultDepotFilter: string;
  trustServerCertificate: boolean;
}

export interface PingRequest {
}

export interface PingResponse {
  message: string;
  timestamp: string;
}

export interface ProbeHostsRequest {
  hosts: string[] | null;
}

export interface ProbeHostsResponse {
  results: HostProbeResult[];
}

export interface RecentLogEntriesRequest {
  limit: number | null;
}

export interface RecentLogEntriesResponse {
  entries: LogEntry[];
  source: string | null;
  clearedAtUtc: string | null;
}

export interface RestartElevatedRequest {
}

export interface RestartElevatedResult {
  cancelled: boolean;
}

export interface SaveItLifecycleSettingsRequest {
  settings: ItLifecycleSettingsValue;
}

export interface SaveNessusSettingsRequest {
  settings: NessusSettingsValue;
}

export interface SaveOpsiSettingsRequest {
  settings: OpsiSettingsValue;
}

export interface SaveServiceCredentialRequest {
  kind: ServiceCredentialKind;
  userName: string;
  domain: string | null;
  password: string;
}

export interface ServiceCredentialStatus {
  saved: boolean;
  userName: string | null;
  domain: string | null;
}

export interface ServiceCredentialStatuses {
  kaspersky: ServiceCredentialStatus;
  opsi: ServiceCredentialStatus;
}

export interface ActionCenterPage {
  items: ActionCenterWorkItem[];
  total: number;
  page: number;
  pageSize: number;
  summary: ActionCenterSummary;
  assessedAtUtc: string;
  sources: ActionEvidenceSourceState[];
  itemsTruncated: boolean;
}

export type ActionCenterSeverity = 'CRITICAL' | 'HIGH' | 'WARNING' | 'MEDIUM' | 'LOW' | 'INFORMATION' | 'UNKNOWN';

export type ActionCenterSortDirection = 'ASCENDING' | 'DESCENDING';

export type ActionCenterSortField = 'SEVERITY' | 'DEVICE' | 'SOURCE' | 'EVIDENCE_AGE' | 'PROBLEM';

export interface ActionCenterSummary {
  total: number;
  critical: number;
  high: number;
  warning: number;
  unknownCoverage: number;
}

export interface ActionCenterWorkItem {
  id: string;
  subjectType: string;
  subjectKey: string;
  device: string;
  userObjectId: string | null;
  userDisplayName: string | null;
  problemCode: string;
  problem: string;
  explanation: string;
  source: string;
  severity: ActionCenterSeverity;
  evidenceAtUtc: string | null;
  assessedAtUtc: string;
  evidenceAgeDays: number | null;
  coverage: ActionEvidenceAvailability;
  reliability: string;
  coverageExplanation: string;
  recommendedAction: string;
  href: string;
}

export interface ListActionCenterItemsRequest {
  activeDirectory?: HygieneActionDirectoryConnection | null;
  kaspersky?: HygieneActionKasperskyConnection | null;
  operationId?: string | null;
  force?: boolean;
  search?: string | null;
  severity?: ActionCenterSeverity | null;
  source?: string | null;
  page?: number;
  pageSize?: number;
  sortField?: ActionCenterSortField;
  sortDirection?: ActionCenterSortDirection;
}

export interface AdComputer {
  name: string;
  dnsHostName: string | null;
  operatingSystem: string | null;
  enabled: boolean;
  description: string | null;
  distinguishedName: string | null;
  lastLogonDate: string | null;
}

export interface AdComputerSearchResult {
  domainJoined: boolean;
  domainName: string | null;
  computers: AdComputer[];
  truncated: boolean;
}

export interface AdUser {
  name: string;
  samAccountName: string | null;
  userPrincipalName: string | null;
  enabled: boolean;
  distinguishedName: string;
  groups: string[];
}

export interface AdUserSearchResult {
  domainJoined: boolean;
  domainName: string | null;
  baseDistinguishedName: string | null;
  users: AdUser[];
  truncated: boolean;
}

export interface DirectoryConnectionRequest {
  domain?: string | null;
  server?: string | null;
  userName?: string | null;
  userDomain?: string | null;
  password?: string | null;
}

export interface AdAccountInfo {
  name: string;
  distinguishedName: string;
  lastLogonUtc: string | null;
}

export interface AdHygieneResult {
  domainJoined: boolean;
  domainName: string | null;
  privilegedGroups: PrivilegedGroupInfo[];
  rules: AdHygieneRule[];
  capturedAtUtc: string;
}

export interface AdHygieneRule {
  ruleId: string;
  title: string;
  matchCount: number;
  examples: AdAccountInfo[];
  recommendation: string;
}

export interface AdHygieneRulePage {
  ruleId: string;
  page: number;
  pageSize: number;
  totalCount: number;
  items: AdAccountInfo[];
  evaluatedAtUtc: string;
}

export interface AdOverviewResult {
  domainJoined: boolean;
  domainName: string | null;
  defaultNamingContext: string | null;
  domainControllers: DomainControllerInfo[];
  userCount: number;
  disabledUserCount: number;
  groupCount: number;
  computerCount: number;
  capturedAtUtc: string;
}

export interface AdPrivilegedGroupMember {
  accountName: string | null;
  distinguishedName: string;
  entityType: string;
  accountStatus: string | null;
  lastLogonUtc: string | null;
}

export interface AdPrivilegedGroupMemberPage {
  groupName: string;
  groupDistinguishedName: string;
  page: number;
  pageSize: number;
  totalCount: number;
  items: AdPrivilegedGroupMember[];
}

export interface DomainControllerInfo {
  hostName: string;
  distinguishedName: string;
}

export interface PrivilegedGroupInfo {
  groupName: string;
  distinguishedName: string;
  directMemberCount: number;
  memberDistinguishedNames: string[];
}

export interface ExportActiveDirectoryCsvRequest {
  csv: string;
}

export interface ExportActiveDirectoryCsvResult {
  cancelled: boolean;
  filePath: string | null;
}

export interface GetAdHygieneRequest {
  connection?: DirectoryConnectionRequest | null;
}

export interface GetAdHygieneRulePageRequest {
  ruleId: string;
  evaluatedAtUtc: string;
  query?: string | null;
  page?: number;
  pageSize?: number;
  connection?: DirectoryConnectionRequest | null;
}

export interface GetAdOverviewRequest {
  connection?: DirectoryConnectionRequest | null;
}

export interface GetAdPrivilegedGroupMemberPageRequest {
  groupDistinguishedName: string;
  query?: string | null;
  page?: number;
  pageSize?: number;
  connection?: DirectoryConnectionRequest | null;
}

export interface SearchAdComputersRequest {
  nameFilter?: string | null;
  includeDisabled?: boolean;
  connection?: DirectoryConnectionRequest | null;
  resultLimit?: number | null;
}

export interface SearchAdUsersRequest {
  baseDistinguishedName?: string | null;
  includeDisabled?: boolean;
  connection?: DirectoryConnectionRequest | null;
}

export interface TestDirectoryConnectionRequest {
  connection?: DirectoryConnectionRequest | null;
}

export interface TestDirectoryConnectionResult {
  domainJoined: boolean;
  domainName: string | null;
  defaultNamingContext: string | null;
}

export interface DeviceCleanupAssessment {
  candidate: DeviceCleanupCandidate;
  sources: DeviceCleanupSourceFact[];
  findings: DeviceCleanupFindingEvidence[];
  userEvidenceAvailability: DeviceCleanupUserEvidenceAvailability;
  userEvidenceExplanation: string;
  userObservations: DeviceCleanupUserObservation[];
}

export interface DeviceCleanupCandidate {
  subjectKey: string;
  host: string;
  description: string | null;
  descriptionSource: string | null;
  classification: DeviceCleanupClassification;
  classificationExplanation: string;
  activeDirectoryExists: boolean | null;
  activeDirectoryEnabled: boolean | null;
  activeDirectoryLastLogonAtUtc: string | null;
  kasperskyExists: boolean | null;
  kasperskyLastSeenAtUtc: string | null;
  opsiExists: boolean | null;
  opsiLastSeenAtUtc: string | null;
  nessusExists: boolean | null;
  nessusLastScanAtUtc: string | null;
  inventoryExists: boolean;
  inventoryCapturedAtUtc: string | null;
  relevantFindingCount: number;
}

export type DeviceCleanupClassification = 'POTENTIAL_CLEANUP' | 'REVIEW' | 'INSUFFICIENT_EVIDENCE' | 'NO_CLEANUP_SIGNAL';

export interface DeviceCleanupPage {
  candidates: DeviceCleanupCandidate[];
  total: number;
  page: number;
  pageSize: number;
  assessedAtUtc: string;
  sources: ActionEvidenceSourceState[];
  selectedAssessment: DeviceCleanupAssessment | null;
  subjectsTruncated: boolean;
}

export interface DeviceCleanupSourceFact {
  source: string;
  coverage: ActionEvidenceAvailability;
  exists: boolean | null;
  state: string;
  observedAtUtc: string | null;
  explanation: string;
}

export interface ListDeviceCleanupCandidatesRequest {
  activeDirectory?: HygieneActionDirectoryConnection | null;
  kaspersky?: HygieneActionKasperskyConnection | null;
  operationId?: string | null;
  force?: boolean;
  search?: string | null;
  selectedHost?: string | null;
  includeWithoutSignals?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ExportDeviceCleanupAssessmentRequest {
  markdown: string;
}

export interface ExportDeviceCleanupAssessmentResult {
  cancelled: boolean;
  filePath: string | null;
}

export interface ExportDeviceCleanupWorkbookRequest {
  activeDirectory?: HygieneActionDirectoryConnection | null;
  kaspersky?: HygieneActionKasperskyConnection | null;
  operationId?: string | null;
  search?: string | null;
  includeWithoutSignals?: boolean;
}

export interface ExportDeviceCleanupWorkbookResult {
  cancelled: boolean;
  filePath: string | null;
  exportedCount: number;
  subjectsTruncated: boolean;
}

export interface DiagnosticBatchProgress {
  host: string;
  status: DiagnosticBatchHostStatus;
}

export interface EventLogQueryResult {
  presetKey: string;
  totalMatched: number;
  truncated: boolean;
  entries: RemoteEventLogEntry[];
}

export interface RemoteEventLogEntry {
  timeGenerated: string | null;
  level: string;
  source: string;
  eventCode: number;
  message: string;
}

export interface DiagnosticBatchHostOutcome {
  host: string;
  status: DiagnosticBatchHostStatus;
  run: DiagnosticRunResult | null;
  error: ScanError | null;
}

export type DiagnosticBatchHostStatus = 'QUEUED' | 'RUNNING' | 'COMPLETED' | 'FAILED';

export interface DiagnosticBatchResult {
  startedAtUtc: string;
  completedAtUtc: string;
  hosts: DiagnosticBatchHostOutcome[];
}

export type DiagnosticCategory = 'EVENT_LOG' | 'SERVICES' | 'SYSTEM';

export interface DiagnosticResult {
  diagnosticId: string;
  title: string;
  status: DiagnosticStatus;
  category: DiagnosticCategory;
  affectedResource: string;
  evidence: Record<string, string>;
  suggestedNextSteps: string[];
  requiredPrivilege: PrivilegeLevel | null;
  capturedAtUtc: string;
}

export interface DiagnosticRunResult {
  startedAtUtc: string;
  completedAtUtc: string;
  results: DiagnosticResult[];
}

export type DiagnosticStatus = 'PASS' | 'WARNING' | 'FAIL' | 'NOT_RUN';

export interface GetLatestDiagnosticsRequest {
  target?: TargetRequest | null;
}

export interface LatestDiagnosticRunResult {
  run: DiagnosticRunResult | null;
}

export interface QueryEventLogRequest {
  preset: string;
  target?: TargetRequest | null;
}

export interface RunBatchDiagnosticsRequest {
  hosts?: string[] | null;
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

export interface RunDiagnosticsRequest {
  target?: TargetRequest | null;
}

export interface AdDeviceData {
  exists: boolean;
  enabled: boolean | null;
  dnsHostName: string | null;
  operatingSystem: string | null;
  description: string | null;
  distinguishedName: string | null;
  organizationalUnit: string | null;
  lastLogonDate: string | null;
}

export interface AuditListResult {
  entries: LifecycleAuditEntry[];
}

export interface CaseDetails {
  id: number;
  employeeId: number;
  type: CaseType;
  status: CaseStatus;
  effectiveDate: string | null;
  note: string | null;
  cancelReason: string | null;
  createdUtc: string;
  closedUtc: string | null;
  tasks: TaskDetails[];
}

export interface CaseResult {
  case: CaseDetails;
  employee: EmployeeDetails;
}

export interface ClientHealthIssue {
  diagnosticId: string;
  title: string;
  status: string;
  affectedResource: string;
}

export interface ClientHealthOverview {
  metadata: ClientOverviewSourceMetadata;
  criticalCount: number;
  warningCount: number;
  unknownCount: number;
  healthyCount: number;
  issues: ClientHealthIssue[];
}

export interface ClientInventoryOverview {
  metadata: ClientOverviewSourceMetadata;
  cpuName: string;
  physicalCores: number;
  logicalProcessors: number;
  totalMemoryBytes: number;
  operatingSystem: string;
  operatingSystemVersion: string;
  operatingSystemBuild: string;
  architecture: string | null;
  disks: ClientOverviewDisk[];
}

export interface ClientOverviewDisk {
  model: string;
  sizeBytes: number;
  interfaceType: string | null;
}

export type ClientOverviewFreshness = 'MISSING' | 'FRESH' | 'STALE' | 'UNKNOWN';

export interface ClientOverviewResult {
  host: string;
  inventory: ClientInventoryOverview | null;
  software: ClientSoftwareOverview | null;
  health: ClientHealthOverview | null;
  security: ClientSecurityOverview | null;
  users: ClientUserOverview | null;
  sources: ClientOverviewSourceMetadata[];
}

export interface ClientOverviewSourceMetadata {
  source: string;
  provenance: string;
  freshness: ClientOverviewFreshness;
  capturedAtUtc: string | null;
  ageSeconds: number | null;
  isComplete: boolean;
  coverage: string;
  detailSection: string;
}

export interface ClientSecurityFinding {
  findingId: string;
  title: string;
  severity: string;
  affectedResource: string;
}

export interface ClientSecurityOverview {
  metadata: ClientOverviewSourceMetadata;
  scanStatus: string | null;
  criticalCount: number;
  highCount: number;
  mediumCount: number;
  lowCount: number;
  topFindings: ClientSecurityFinding[];
}

export interface ClientSoftwareOverview {
  metadata: ClientOverviewSourceMetadata;
  installedCount: number;
  sample: ClientSoftwareOverviewItem[];
}

export interface ClientSoftwareOverviewItem {
  name: string;
  version: string | null;
  publisher: string | null;
}

export interface ClientUserOverview {
  metadata: ClientOverviewSourceMetadata;
  unresolvedProfileCount: number;
  observations: ClientObservedUserEvidence[];
}

export interface ClientWorkspaceListItem {
  host: string;
  key: string;
  name: string;
  os: string | null;
  description: string | null;
  enabled: boolean;
  scanned: boolean;
  capturedAtUtc: string | null;
  saved: boolean;
  inAd: boolean;
  environment: HygieneDevice | null;
  groupLabel: string | null;
  groupTotal: number | null;
}

export interface ClientWorkspacePage {
  items: ClientWorkspaceListItem[];
  total: number;
  scannedTotal: number;
  snapshotTotal: number;
  page: number;
  pageSize: number;
  groupCount: number | null;
  assessedAtUtc: string;
  domainName: string | null;
  summary: HygieneSummary;
  sources: EnvironmentSourceStates;
}

export interface DepartmentInfo {
  id: number;
  name: string;
  managerName: string | null;
  ouDistinguishedName: string | null;
}

export interface DepartmentListResult {
  departments: DepartmentInfo[];
}

export interface DirectoryInventoryConnection {
  domain?: string | null;
  server?: string | null;
  userName?: string | null;
  userDomain?: string | null;
  password?: string | null;
}

export interface EmployeeDetails {
  id: number;
  firstName: string;
  lastName: string;
  email: string | null;
  employeeNumber: string | null;
  department: string | null;
  title: string | null;
  manager: string | null;
  samAccountName: string | null;
  userPrincipalName: string | null;
  distinguishedName: string | null;
  status: EmployeeStatus;
  entryDate: string | null;
  exitDate: string | null;
  notes: string | null;
  createdUtc: string;
  updatedUtc: string;
}

export interface EmployeeDetailsResult {
  employee: EmployeeDetails;
  cases: CaseDetails[];
}

export interface EmployeeInput {
  firstName?: string;
  lastName?: string;
  email?: string | null;
  employeeNumber?: string | null;
  department?: string | null;
  title?: string | null;
  manager?: string | null;
  samAccountName?: string | null;
  userPrincipalName?: string | null;
  distinguishedName?: string | null;
  entryDate?: string | null;
  notes?: string | null;
}

export interface EmployeeListResult {
  employees: EmployeeSummary[];
}

export interface EmployeeSummary {
  id: number;
  firstName: string;
  lastName: string;
  employeeNumber: string | null;
  department: string | null;
  title: string | null;
  samAccountName: string | null;
  status: EmployeeStatus;
  entryDate: string | null;
  exitDate: string | null;
  activeCaseId: number | null;
  activeCaseType: CaseType | null;
  openTaskCount: number;
  overdueTaskCount: number;
}

export interface EnvironmentSourceStates {
  activeDirectory: InventorySourceState;
  kaspersky: InventorySourceState;
  opsi: InventorySourceState;
  nessus: InventorySourceState;
}

export interface GetItHygieneOverviewRequest {
  activeDirectory?: DirectoryInventoryConnection | null;
  kaspersky?: KasperskyInventoryConnection | null;
  force?: boolean;
  operationId?: string | null;
}

export interface HygieneAssessment {
  status: HygieneStatus;
  findings: HygieneFinding[];
}

export interface HygieneDevice {
  computerName: string;
  hostName: string;
  activeDirectory: AdDeviceData;
  kaspersky: KasperskyDeviceData;
  opsi: OpsiDeviceData;
  nessus: NessusDeviceData;
  assessment: HygieneAssessment;
}

export interface HygieneDevicePage {
  items: HygieneDevice[];
  total: number;
  page: number;
  pageSize: number;
}

export interface HygieneFinding {
  code: HygieneFindingCode;
  severity: HygieneFindingSeverity;
  message: string;
}

export type HygieneFindingCode = 'MISSING_KASPERSKY' | 'ORPHAN_KASPERSKY' | 'STALE_AD' | 'STALE_KASPERSKY' | 'OUTDATED_AGENT' | 'OUTDATED_KES' | 'MISSING_OPSI' | 'ORPHAN_OPSI' | 'STALE_OPSI' | 'MISSING_NESSUS' | 'STALE_NESSUS' | 'NESSUS_CRITICAL_VULNERABILITIES' | 'NESSUS_HIGH_VULNERABILITIES' | 'MISSING_KASPERSKY_AGENT' | 'MISSING_KES';

export type HygieneFindingSeverity = 'WARNING' | 'CRITICAL';

export type HygieneLoadPhase = 'LOADING_SOURCES' | 'CORRELATING' | 'COMPLETED' | 'CANCELLED';

export interface HygieneLoadProgress {
  operationId: string;
  phase: HygieneLoadPhase;
  startedAtUtc: string;
  completedSources: number;
  totalSources: number;
  partialDeviceCount: number;
  partialSummary: HygieneSummary | null;
  sources: HygieneSourceProgress[];
}

export interface HygieneSourceProgress {
  source: string;
  status: HygieneSourceProgressStatus;
  itemCount: number | null;
  message: string | null;
}

export type HygieneSourceProgressStatus = 'RUNNING' | 'AVAILABLE' | 'PARTIAL' | 'NOT_CONNECTED' | 'UNAVAILABLE' | 'TRUNCATED';

export type HygieneStatus = 'HEALTHY' | 'WARNING' | 'CLEANUP_CANDIDATE' | 'INCOMPLETE' | 'CRITICAL';

export interface HygieneSummary {
  total: number;
  adComputers: number;
  kasperskyComputers: number;
  opsiComputers: number;
  nessusComputers: number;
  healthy: number;
  problems: number;
  incomplete: number;
  stale: number;
  missingKaspersky: number;
  orphanKaspersky: number;
  missingOpsi: number;
  orphanOpsi: number;
  outdated: number;
  missingNessus: number;
  staleNessus: number;
  nessusCritical: number;
  nessusHigh: number;
}

export type InventorySourceAvailability = 'AVAILABLE' | 'NOT_CONNECTED' | 'UNAVAILABLE' | 'TRUNCATED' | 'PARTIAL';

export interface InventorySourceState {
  availability: InventorySourceAvailability;
  error: string | null;
}

export interface ItHygieneOverview {
  assessedAtUtc: string;
  domainName: string | null;
  sources: EnvironmentSourceStates;
  summary: HygieneSummary;
  knownHosts: string[];
}

export interface ItHygieneRequest {
  activeDirectory?: DirectoryInventoryConnection | null;
  kaspersky?: KasperskyInventoryConnection | null;
  operationId?: string | null;
}

export interface ItHygieneResult {
  assessedAtUtc: string;
  domainName: string | null;
  sources: EnvironmentSourceStates;
  summary: HygieneSummary;
  devices: HygieneDevice[];
}

export interface KasperskyDeviceData {
  exists: boolean;
  lastSeen: string | null;
  agentVersion: string | null;
  kesVersion: string | null;
  administrationGroup: string | null;
}

export interface KasperskyInventoryConnection {
  server?: string | null;
  port?: number | null;
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

export interface LifecycleAuditEntry {
  id: number;
  timestampUtc: string;
  userName: string;
  employeeId: number;
  caseId: number | null;
  taskId: number | null;
  eventType: string;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}

export interface ListClientWorkspaceRequest {
  activeDirectory?: DirectoryInventoryConnection | null;
  kaspersky?: KasperskyInventoryConnection | null;
  search?: string | null;
  statusFilter?: string | null;
  sourceFilter?: string | null;
  groupMode?: string | null;
  page?: number;
  pageSize?: number;
  sortColumn?: string | null;
  sortDirection?: string | null;
  force?: boolean;
  operationId?: string | null;
}

export interface ListHygieneDevicesRequest {
  activeDirectory?: DirectoryInventoryConnection | null;
  kaspersky?: KasperskyInventoryConnection | null;
  search?: string | null;
  filter?: string | null;
  page?: number;
  pageSize?: number;
  sortColumn?: string | null;
  sortDirection?: string | null;
  operationId?: string | null;
}

export interface NessusDeviceData {
  exists: boolean;
  assetId: string | null;
  ipAddress: string | null;
  lastCompletedScanUtc: string | null;
  critical: number;
  high: number;
  medium: number;
  low: number;
  info: number;
  ports: number[];
  scanSources: string[];
}

export interface OpsiDeviceData {
  exists: boolean;
  clientId: string | null;
  description: string | null;
  depotId: string | null;
  lastSeen: string | null;
  clientAgentVersion: string | null;
}

export interface TaskDetails {
  id: number;
  caseId: number;
  title: string;
  area: TaskArea;
  status: LifecycleTaskStatus;
  dueDate: string | null;
  assignee: string | null;
  notes: string;
  sortOrder: number;
  isOverdue: boolean;
}

export interface TaskResult {
  task: TaskDetails;
}

export type CaseStatus = 'ACTIVE' | 'COMPLETED' | 'CANCELLED';

export type CaseType = 'ONBOARDING' | 'OFFBOARDING' | 'CHANGE';

export type EmployeeStatus = 'PLANNED' | 'ONBOARDING' | 'ACTIVE' | 'CHANGING' | 'OFFBOARDING' | 'DISABLED';

export type LifecycleTaskStatus = 'OPEN' | 'IN_PROGRESS' | 'BLOCKED' | 'DONE' | 'SKIPPED';

export type TaskArea = 'GENERAL' | 'ACCOUNT' | 'HARDWARE' | 'SOFTWARE' | 'PERMISSIONS' | 'MAILBOX';

export interface AddTaskNoteRequest {
  taskId?: number;
  note?: string;
}

export interface CancelCaseRequest {
  caseId?: number;
  reason?: string | null;
}

export interface CompleteCaseRequest {
  caseId?: number;
}

export interface DeleteDepartmentRequest {
  departmentId?: number;
}

export interface GetCaseRequest {
  caseId?: number;
}

export interface GetClientOverviewRequest {
  host: string;
}

export interface GetEmployeeRequest {
  employeeId?: number;
}

export interface ListAuditEntriesRequest {
  employeeId?: number | null;
  limit?: number | null;
}

export interface ListDepartmentsRequest {
}

export interface ListEmployeesRequest {
}

export interface SaveDepartmentRequest {
  name?: string;
  managerName?: string | null;
  ouDistinguishedName?: string | null;
}

export interface StartCaseRequest {
  employeeId?: number;
  type?: CaseType;
  effectiveDate?: string | null;
  note?: string | null;
}

export interface UpdateEmployeeRequest {
  employeeId?: number;
  firstName?: string;
  lastName?: string;
  email?: string | null;
  employeeNumber?: string | null;
  department?: string | null;
  title?: string | null;
  manager?: string | null;
  samAccountName?: string | null;
  userPrincipalName?: string | null;
  distinguishedName?: string | null;
  entryDate?: string | null;
  notes?: string | null;
}

export interface UpdateTaskRequest {
  taskId?: number;
  status?: LifecycleTaskStatus;
  assignee?: string | null;
  dueDate?: string | null;
}

export interface DiskEncryptionStatus {
  host: string;
  volumes: EncryptableVolume[];
}

export interface HardwareInfoResult {
  host: string;
  snapshot: HardwareSnapshot;
  capturedAtUtc: string;
  fromCache: boolean;
}

export interface InventoryBatchProgress {
  host: string;
  status: InventoryBatchHostStatus;
}

export interface CpuInfo {
  name: string;
  physicalCores: number;
  logicalProcessors: number;
  maxClockSpeedMhz: number;
}

export interface DeviceUserEvidence {
  interactiveUserState: UserEvidenceSourceState;
  interactiveUser: InteractiveDomainUserEvidence | null;
  interactiveUserError: UserEvidenceCaptureError | null;
  localProfilesState: UserEvidenceSourceState;
  localProfiles: LocalUserProfileEvidence[] | null;
  localProfilesError: UserEvidenceCaptureError | null;
  localProfilesTruncated: boolean;
}

export interface DiskDrive {
  model: string;
  sizeBytes: number;
  interfaceType: string | null;
  mediaType: string | null;
}

export interface EncryptableVolume {
  driveLetter: string | null;
  protectionStatus: VolumeProtectionStatus;
}

export interface GpuInfo {
  name: string;
  memoryBytes: number | null;
  driverVersion: string | null;
}

export interface HardwareSnapshot {
  cpu: CpuInfo;
  memoryBanks: MemoryBank[];
  disks: DiskDrive[];
  operatingSystem: OperatingSystemInfo;
  networkAdapters: PhysicalNetworkAdapter[] | null;
  gpus: GpuInfo[] | null;
  monitors: MonitorInfo[] | null;
  installedSoftware: InstalledSoftwareEntry[] | null;
  installedSoftwareError: SoftwareCaptureError | null;
  userEvidence: DeviceUserEvidence | null;
}

export interface InstalledSoftwareEntry {
  name: string;
  version: string | null;
  publisher: string | null;
}

export interface InteractiveDomainUserEvidence {
  sid: string;
  domain: string;
  accountName: string;
}

export interface InventoryBatchHostOutcome {
  host: string;
  status: InventoryBatchHostStatus;
  inventory: HardwareInfoResult | null;
  error: ScanError | null;
}

export type InventoryBatchHostStatus = 'QUEUED' | 'RUNNING' | 'COMPLETED' | 'FAILED';

export interface InventoryBatchResult {
  startedAtUtc: string;
  completedAtUtc: string;
  hosts: InventoryBatchHostOutcome[];
}

export interface LocalUserProfileEvidence {
  sid: string;
  lastUseAtUtc: string | null;
}

export interface MemoryBank {
  manufacturer: string | null;
  partNumber: string | null;
  capacityBytes: number;
  speedMtps: number | null;
}

export interface MonitorInfo {
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
}

export interface OperatingSystemInfo {
  caption: string;
  version: string;
  buildNumber: string;
  architecture: string | null;
}

export interface PhysicalNetworkAdapter {
  name: string;
  macAddress: string | null;
  speedBitsPerSecond: number | null;
  connected: boolean | null;
  adapterType: string | null;
  ipAddresses: string[] | null;
}

export interface SoftwareCaptureError {
  code: string;
  message: string;
}

export interface UserEvidenceCaptureError {
  code: string;
  message: string;
}

export type UserEvidenceSourceState = 'NOT_CAPTURED' | 'AVAILABLE' | 'UNAVAILABLE';

export type VolumeProtectionStatus = 'UNPROTECTED' | 'PROTECTED' | 'UNKNOWN';

export interface DeleteHostSnapshotRequest {
  host: string;
}

export interface DeleteHostSnapshotResult {
  host: string;
}

export interface GetDiskEncryptionStatusRequest {
  target?: TargetRequest | null;
}

export interface GetHardwareInfoRequest {
  forceRefresh?: boolean;
  target?: TargetRequest | null;
  cacheOnly?: boolean;
}

export interface ListInventoryHostsRequest {
}

export interface ListInventoryHostsResult {
  hosts: StoredInventoryHost[];
}

export interface RunBatchInventoryRequest {
  hosts?: string[] | null;
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

export interface StoredInventoryHost {
  host: string;
  capturedAtUtc: string;
}

export interface Microsoft365Correlation {
  state: string;
  explanation: string;
  user: Microsoft365User | null;
  device: Microsoft365Device | null;
  managedDevice: Microsoft365ManagedDevice | null;
  observedAtUtc: string | null;
  stale: boolean;
}

export interface Microsoft365LicenseCapacity {
  skuId: string | null;
  remainingSeats: number | null;
  nearlyExhausted: boolean | null;
  overAssigned: boolean | null;
}

export interface Microsoft365Snapshot {
  query: Microsoft365Query;
  data: Microsoft365Data | null;
  updatedAtUtc: string | null;
  stale: boolean;
  refreshError: Error | null;
  licenseCapacity: Microsoft365LicenseCapacity[];
}

export interface Microsoft365SourceStatus {
  resource: Microsoft365Resource;
  updatedAtUtc: string | null;
  stale: boolean;
  truncated: boolean;
  totalCount: number | null;
  loadedCount: number | null;
  lastRefreshError: Error | null;
}

export interface Microsoft365Status {
  connection: Microsoft365Connection;
  sources: Microsoft365SourceStatus[];
}

export interface Microsoft365ContextRequest {
  sid?: string | null;
  userPrincipalName?: string | null;
  host?: string | null;
  entraDeviceId?: string | null;
}

export interface Microsoft365EmptyRequest {
}

export interface Microsoft365ReadRequest {
  resource: Microsoft365Resource;
  objectId?: string | null;
  refresh?: boolean;
}

export type DeviceKind = 'UNKNOWN' | 'PRINTER' | 'COMPUTER' | 'NETWORK_DEVICE';

export interface NetworkHostRow {
  ip: string;
  hostname: string | null;
  isUp: boolean;
  kind: DeviceKind;
  openPorts: number[];
  macAddress: string | null;
  macVendor: string | null;
  hasReservation: boolean;
  reservationName: string | null;
}

export interface NetworkScanResult {
  target: string;
  portsScanned: boolean;
  dhcpChecked: boolean;
  hosts: NetworkHostRow[];
}

export interface ScanNetworkRequest {
  target: string | null;
  scanPorts: boolean;
  dhcp: TargetRequest | null;
}

export interface ListPatchClientStatesRequest {
  depotFilter?: string | null;
  productId?: string | null;
  clientSearch?: string | null;
  productSearch?: string | null;
  state?: PatchWorkflowState | null;
  installationStatus?: string | null;
  page?: number;
  pageSize?: number;
  sortColumn?: string | null;
  sortDirection?: string | null;
}

export interface PatchClientListItem {
  productId: string;
  productName: string | null;
  client: PatchClientState;
}

export interface PatchClientState {
  clientId: string;
  depotId: string | null;
  installedVersion: string | null;
  targetVersion: string | null;
  installationStatus: string | null;
  actionRequest: string | null;
  actionResult: string | null;
  state: PatchWorkflowState;
}

export interface PatchClientStatePage {
  items: PatchClientListItem[];
  total: number;
  snapshotTotal: number;
  page: number;
  pageSize: number;
}

export interface PatchDashboardOverview {
  serverUrl: string;
  depotFilter: string | null;
  generatedAtUtc: string;
  summary: PatchDashboardSummary;
  depots: PatchDepotSummary[];
  products: PatchProductOverviewRow[];
}

export interface PatchDashboardSummary {
  productCount: number;
  productsWithUpdates: number;
  productsWithDepotDeviation: number;
  productsMissingOnDepots: number;
  productsWithFailures: number;
  outdatedClientCount: number;
  clientCount: number;
  depotCount: number;
  wingetManagedCount: number;
  wingetUpdatesAvailable: number;
}

export interface PatchDepotSummary {
  id: string;
  description: string | null;
  isConfigServer: boolean;
  clientCount: number;
}

export interface PatchDepotVersion {
  depotId: string;
  version: string;
}

export type PatchPackageStatus = 'CURRENT' | 'UPDATE_AVAILABLE' | 'DEPOT_DEVIATION' | 'MISSING_ON_DEPOT' | 'CHECK_FAILED' | 'ACTION_PENDING';

export interface PatchProductOverviewRow {
  productId: string;
  name: string | null;
  availableVersion: string | null;
  referenceVersion: string | null;
  depotVersions: PatchDepotVersion[];
  missingDepotIds: string[];
  packageStatus: PatchPackageStatus;
  state: PatchWorkflowState;
  installedClientCount: number;
  outdatedClientCount: number;
  failedClientCount: number;
  pendingActionCount: number;
  lastError: string | null;
  wingetManaged: boolean;
  wingetId: string | null;
  latestWingetVersion: string | null;
  wingetCheckStatus: string;
  wingetCheckedAtUtc: string | null;
  wingetCheckError: string | null;
  wingetUpdateAvailable: boolean;
}

export interface WingetManagedPackageView {
  opsiProductId: string;
  displayName: string;
  wingetId: string;
  source: string;
  scope: string;
  depotId: string;
  currentDepotVersion: string | null;
  lastPackagedWingetVersion: string | null;
  latestWingetVersion: string | null;
  updateAvailable: boolean;
  checkStatus: string;
  checkedAtUtc: string | null;
  lastError: string | null;
}

export interface WingetPackageOperationOutcome {
  opsiProductId: string;
  depotId: string;
  success: boolean;
  oldVersion: string | null;
  newVersion: string | null;
  error: string | null;
}

export interface WingetPackagePreview {
  opsiProductId: string;
  displayName: string;
  wingetId: string;
  wingetVersion: string;
  source: string;
  scope: string;
  installerType: string;
  architecture: string;
  depotId: string;
  currentDepotVersion: string | null;
  targetDepotVersion: string;
  packageVersion: number;
  adoptsExistingProduct: boolean;
  workbenchPath: string;
  commands: string[];
  confirmationText: string;
  generatedAtUtc: string;
}

export interface WingetUpdateCheckResult {
  checkedCount: number;
  updateCount: number;
  failedCount: number;
  packages: WingetManagedPackageView[];
}

export interface WingetUpdateOutcome {
  succeededCount: number;
  failedCount: number;
  packages: WingetPackageOperationOutcome[];
}

export interface WingetUpdatePlan {
  packages: WingetPackagePreview[];
  confirmationText: string;
  generatedAtUtc: string;
}

export interface WingetUpdateSelection {
  opsiProductId: string;
  expectedWingetVersion: string;
}

export type PatchWorkflowState = 'DETECTED' | 'UPDATE_AVAILABLE' | 'ACTION_PENDING' | 'COMPLETED' | 'FAILED';

export interface ApplyWingetUpdatesRequest {
  packages: WingetUpdateSelection[];
  confirmed?: boolean;
}

export interface AuditLogResult {
  entries: PatchAuditEntry[];
}

export interface CheckWingetUpdatesRequest {
  productIds?: string[] | null;
  force?: boolean;
}

export interface CreateOrAdoptWingetPackageRequest {
  opsiProductId: string;
  displayName: string;
  wingetId: string;
  depotId: string;
  expectedWingetVersion: string;
  confirmed?: boolean;
}

export interface GetAuditLogRequest {
  limit?: number | null;
}

export interface GetPatchDashboardRequest {
  depotFilter?: string | null;
}

export interface ListManagedWingetPackagesRequest {
}

export interface ManagedWingetPackagesResult {
  packages: WingetManagedPackageView[];
}

export interface OpsiConnectRequest {
  server: string;
  userName: string;
  password: string | null;
  trustServerCertificate: boolean;
  useStoredCredential?: boolean;
  rememberCredential?: boolean;
}

export interface OpsiConnectionStatusRequest {
}

export interface OpsiConnectionStatusResult {
  connected: boolean;
  serverUrl: string | null;
  userName: string | null;
  opsiVersion: string | null;
  defaultDepotFilter: string;
  connectionError: string | null;
}

export interface OpsiDisconnectRequest {
}

export interface PrepareWingetUpdatesRequest {
  packages: WingetUpdateSelection[];
}

export interface PreviewWingetPackageRequest {
  opsiProductId: string;
  displayName: string;
  wingetId: string;
  depotId: string;
}

export interface SearchWingetPackagesRequest {
  query: string;
  limit?: number;
}

export interface SearchWingetPackagesResult {
  packages: WingetPackageInfo[];
}

export interface PatchAuditEntry {
  id: number;
  timestampUtc: string;
  userName: string;
  action: string;
  productId: string | null;
  depotId: string | null;
  targetClients: string[];
  previewJson: string | null;
  result: string;
  errorMessage: string | null;
  oldVersion: string | null;
  newVersion: string | null;
}

export interface DhcpCheckResult {
  reserved: DhcpReservationInfo[];
}

export interface DhcpReservationInfo {
  ip: string;
  mac: string | null;
  name: string | null;
}

export interface LeaseDiffDevice {
  serialNumber: string;
  model: string | null;
  queueName: string | null;
  deviceAddress: string | null;
}

export interface LeaseQueueSwap {
  queueName: string;
  oldSerialNumber: string;
  newSerialNumber: string;
  oldModel: string | null;
  newModel: string | null;
}

export interface PrintHint {
  category: string;
  message: string;
}

export interface PrintServerDiff {
  server: string;
  baselineAtUtc: string;
  latestAtUtc: string;
  newDevices: LeaseDiffDevice[];
  goneDevices: LeaseDiffDevice[];
  swappedQueues: LeaseQueueSwap[];
  devicesWithoutSerialNumber: number;
}

export interface ClientPrinter {
  name: string;
  driverName: string | null;
  portName: string | null;
  location: string | null;
  shared: boolean;
  isNetwork: boolean;
}

export interface ClientPrinterScan {
  host: string;
  capturedAtUtc: string;
  printers: ClientPrinter[];
}

export interface DeviceQueryError {
  code: string;
  message: string;
}

export type NotificationCheckStatus = 'OK' | 'WARNING' | 'NOT_CHECKED';

export interface NotificationRule {
  id: string;
  title: string;
  passed: boolean;
  detail: string;
}

export interface PrintServerSnapshot {
  server: string;
  capturedAtUtc: string;
  printers: PrinterEntry[];
  unusedPorts: UnusedPort[];
  unusedDrivers: UnusedDriver[];
}

export interface PrinterDevice {
  serialNumber: string | null;
  model: string | null;
  sysName: string | null;
  sysLocation: string | null;
  status: string | null;
  pageCount: number | null;
  supplies: TonerSupply[];
}

export interface PrinterEntry {
  queueName: string;
  shareName: string | null;
  driverName: string | null;
  driverVersion: string | null;
  portName: string | null;
  deviceAddress: string | null;
  location: string | null;
  comment: string | null;
  device: PrinterDevice | null;
  deviceError: DeviceQueryError | null;
  deviceIp: string | null;
  deviceDataFromUtc: string | null;
}

export interface PrinterNotificationCheck {
  host: string;
  status: NotificationCheckStatus;
  rules: NotificationRule[];
  error: string | null;
}

export interface TonerSupply {
  description: string;
  percent: number | null;
  isLow: boolean;
}

export interface UnusedDriver {
  name: string;
  version: string | null;
}

export interface UnusedPort {
  name: string;
  hostAddress: string | null;
}

export interface CheckDhcpRequest {
  target: TargetRequest;
  ips: string[];
}

export interface CheckNotificationConfigRequest {
  host: string;
  siteCode?: string | null;
  userName?: string | null;
  password?: string | null;
}

export interface DeletePrintServerRequest {
  server: string;
}

export interface DeletePrintServerResult {
  server: string;
}

export interface DeleteUnusedPortsRequest {
  target: TargetRequest;
  portNames: string[];
  confirmed: boolean;
}

export interface DeleteUnusedPortsResult {
  results: PortRemovalResult[];
}

export interface ExportPrintCsvRequest {
  csv: string;
}

export interface ExportPrintCsvResult {
  cancelled: boolean;
  filePath: string | null;
}

export interface GetLatestClientPrintersRequest {
  target?: TargetRequest | null;
}

export interface GetLatestPrintSnapshotRequest {
  server: string;
}

export interface GetLeaseDiffRequest {
  server: string;
  baselineSnapshotId?: number | null;
}

export interface GetNetworkPolicyRequest {
}

export interface GetPrintHintsRequest {
}

export interface GetPrintHistoryRequest {
  server: string;
}

export interface LatestClientPrinterScanResult {
  scan: ClientPrinterScan | null;
}

export interface ListPrintServersRequest {
}

export interface ListPrintServersResult {
  servers: StoredPrintServer[];
}

export interface NetworkPolicyResult {
  printerSubnets: string[];
  legacySubnets: string[];
  dhcpServer: string | null;
}

export interface OpenDeviceWebUiRequest {
  address: string;
}

export interface OpenDeviceWebUiResult {
  opened: boolean;
  url: string;
}

export interface PrintHintsResult {
  hints: PrintHint[];
}

export interface PrintHistoryResult {
  snapshots: PrintSnapshotStamp[];
}

export interface ScanClientPrintersRequest {
  target?: TargetRequest | null;
}

export interface ScanPrintServerRequest {
  target?: TargetRequest | null;
}

export interface PrintSnapshotStamp {
  id: number;
  capturedAtUtc: string;
}

export interface StoredPrintServer {
  server: string;
  capturedAtUtc: string;
  snapshotCount: number;
}

export interface ReportExportResult {
  cancelled: boolean;
  filePath: string | null;
  openError: string | null;
}

export interface ReportOverview {
  subjectHost: string;
  inventoryCapturedAtUtc: string | null;
  securityScanCompletedAtUtc: string | null;
  securityScanStatus: string | null;
  securityFindingCount: number | null;
  securityCoverage: SecurityCoverageReportData | null;
  readiness: ReportReadiness;
}

export interface ReportReadiness {
  evaluatedAtUtc: string;
  isReady: boolean;
  sources: ReportSourceReadiness[];
}

export interface ReportSourceReadiness {
  source: string;
  provenance: string;
  state: string;
  capturedAtUtc: string | null;
  ageSeconds: number | null;
  isComplete: boolean;
  summary: string;
}

export interface ExportHtmlReportRequest {
  openAfterExport?: boolean;
  host?: string | null;
}

export interface ExportJsonReportRequest {
  openAfterExport?: boolean;
  host?: string | null;
}

export interface GetReportOverviewRequest {
  host?: string | null;
}

export interface GetReportReadinessPolicyRequest {
}

export interface ReportReadinessPolicy {
  maximumInventoryAgeSeconds: number;
  maximumSecurityScanAgeSeconds: number;
}

export interface BatchScanProgress {
  host: string;
  status: HostScanStatus;
}

export interface LatestScanResult {
  scan: SecurityScanResult | null;
}

export interface BatchScanResult {
  startedAtUtc: string;
  completedAtUtc: string;
  hosts: HostScanOutcome[];
}

export type FindingCategory = 'FIREWALL' | 'MALWARE_PROTECTION' | 'NETWORK_SERVICES' | 'ENCRYPTION' | 'PLATFORM_INTEGRITY' | 'ACCOUNTS' | 'OPERATING_SYSTEM' | 'UNKNOWN';

export type FindingSeverity = 'INFO' | 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL' | 'UNKNOWN';

export interface HostScanOutcome {
  host: string;
  status: HostScanStatus;
  scan: SecurityScanResult | null;
  error: ScanError | null;
}

export type HostScanStatus = 'QUEUED' | 'CONNECTING' | 'RUNNING' | 'COMPLETED' | 'COMPLETED_WITH_ERRORS' | 'FAILED';

export interface ScanDiff {
  latestScanId: number;
  previousScanId: number;
  newFindings: SecurityFinding[];
  resolvedFindings: SecurityFinding[];
  isFullyComparable: boolean;
  uncomparedCheckIds: string[];
}

export interface ScanHistoryResult {
  scans: ScanSummary[];
  changesSinceLastScan: ScanDiff | null;
}

export type ScanStatus = 'COMPLETED' | 'COMPLETED_WITH_ERRORS' | 'FAILED';

export interface ScanSummary {
  scanId: number;
  startedAtUtc: string;
  completedAtUtc: string;
  status: ScanStatus;
  findingCount: number;
  severityCounts: SeverityCount[];
  coverage: SecurityCoverage;
}

export interface SecurityCheckFailure {
  code: ErrorCode;
  message: string;
  requiredPrivilege: PrivilegeLevel | null;
}

export interface SecurityCheckResult {
  checkId: string;
  status: CheckStatus;
  findings: SecurityFinding[];
  failure: SecurityCheckFailure | null;
}

export interface SecurityCoverage {
  isKnown: boolean;
  totalChecks: number;
  succeededChecks: number;
  failedChecks: number;
  requiresElevationChecks: number;
  notApplicableChecks: number;
  applicableChecks: number;
  isComplete: boolean;
}

export interface SecurityFinding {
  findingId: string;
  title: string;
  description: string;
  severity: FindingSeverity;
  category: FindingCategory;
  affectedResource: string;
  evidence: Record<string, string>;
  recommendation: string;
  requiredPrivilege: PrivilegeLevel | null;
  capturedAtUtc: string;
}

export interface SecurityScanResult {
  scanId: number;
  host: string;
  startedAtUtc: string;
  completedAtUtc: string;
  status: ScanStatus;
  findings: SecurityFinding[];
  checkResults: SecurityCheckResult[];
  coverageVersion: number | null;
  coverage: SecurityCoverage;
}

export interface SeverityCount {
  severity: FindingSeverity;
  count: number;
}

export interface GetLatestSecurityScanRequest {
  target?: TargetRequest | null;
}

export interface GetScanHistoryRequest {
  target?: TargetRequest | null;
}

export interface ListSecurityScanHostsRequest {
}

export interface ListSecurityScanHostsResult {
  hosts: StoredSecurityScanHost[];
}

export interface RunBatchSecurityScanRequest {
  hosts?: string[] | null;
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

export interface RunSecurityScanRequest {
  target?: TargetRequest | null;
}

export interface StoredSecurityScanHost {
  host: string;
  completedAtUtc: string;
}

export interface DeleteSavedTargetRequest {
  id: number;
}

export interface ListSavedTargetsRequest {
}

export interface SaveTargetRequest {
  label: string;
  host: string;
  role: string;
  userName: string | null;
}

export interface SavedTargetsResult {
  targets: SavedTarget[];
}

export interface SavedTarget {
  id: number;
  label: string;
  host: string;
  role: string;
  userName: string | null;
  createdAtUtc: string;
}

export interface UserDirectoryConnectionRequest {
  domain?: string | null;
  server?: string | null;
  userName?: string | null;
  userDomain?: string | null;
  password?: string | null;
}

export interface UserAccessProfile {
  directGroups: DirectoryUserGroup[];
  privilegedCoverage: DirectoryUserAccessCoverage;
  privilegedCoverageExplanation: string;
  directPrivilegedGroups: DirectoryUserGroup[];
}

export type UserDeviceEvidenceCoverage = 'NOT_EVALUATED' | 'AVAILABLE' | 'PARTIAL' | 'NOT_CAPTURED';

export interface UserDeviceHealthProfile {
  isAvailable: boolean;
  isComplete: boolean;
  capturedAtUtc: string | null;
  criticalCount: number;
  warningCount: number;
  unknownCount: number;
  healthyCount: number;
  explanation: string;
}

export interface UserDeviceProfile {
  coverage: UserDeviceEvidenceCoverage;
  explanation: string;
  sourceCoverage: UserDeviceRelationshipCoverage;
  totalLinkedDeviceCount: number;
  linkedDevicesTruncated: boolean;
  linkedDevices: UserLinkedDeviceProfile[];
}

export interface UserDeviceSecurityProfile {
  isAvailable: boolean;
  isComplete: boolean;
  capturedAtUtc: string | null;
  scanStatus: string | null;
  criticalCount: number;
  highCount: number;
  mediumCount: number;
  lowCount: number;
  explanation: string;
}

export interface UserDeviceSoftwareItem {
  name: string;
  version: string | null;
  publisher: string | null;
}

export interface UserDeviceSoftwareProfile {
  isAvailable: boolean;
  isComplete: boolean;
  capturedAtUtc: string | null;
  installedCount: number;
  sample: UserDeviceSoftwareItem[];
  explanation: string;
}

export interface UserDeviceVulnerabilityProfile {
  availability: NessusInventoryAvailability;
  deviceMatched: boolean;
  capturedAtUtc: string | null;
  criticalCount: number;
  highCount: number;
  mediumCount: number;
  lowCount: number;
  explanation: string;
}

export interface UserIdentityProfile {
  objectId: string;
  sid: string | null;
  displayName: string;
  samAccountName: string | null;
  userPrincipalName: string | null;
  mail: string | null;
  employeeId: string | null;
  department: string | null;
  title: string | null;
  managerDistinguishedName: string | null;
  distinguishedName: string;
  organizationalUnitPath: string;
}

export interface UserLifecycleProfile {
  enabled: boolean | null;
  createdAtUtc: string | null;
  accountExpiresAtUtc: string | null;
  replicatedLastLogonAtUtc: string | null;
  passwordLastSetAtUtc: string | null;
  passwordExpiresAtUtc: string | null;
  passwordNeverExpires: boolean | null;
}

export interface UserLinkedDeviceProfile {
  host: string;
  inventoryCapturedAtUtc: string;
  relationshipEvidence: UserDeviceRelationshipObservation[];
  software: UserDeviceSoftwareProfile;
  health: UserDeviceHealthProfile;
  security: UserDeviceSecurityProfile;
  vulnerabilities: UserDeviceVulnerabilityProfile;
}

export interface UserPageResult {
  domainJoined: boolean;
  domainName: string | null;
  baseDistinguishedName: string | null;
  page: number;
  pageSize: number;
  totalCount: number;
  users: UserSummary[];
}

export interface UserProfileResult {
  identity: UserIdentityProfile;
  lifecycle: UserLifecycleProfile;
  access: UserAccessProfile;
  devices: UserDeviceProfile;
}

export interface UserSummary {
  objectId: string;
  displayName: string;
  samAccountName: string | null;
  userPrincipalName: string | null;
  employeeId: string | null;
  department: string | null;
  title: string | null;
  organizationalUnitPath: string;
  enabled: boolean | null;
  replicatedLastLogonAtUtc: string | null;
}

export interface ExportLeaverReviewRequest {
  markdown: string;
}

export interface ExportLeaverReviewResult {
  cancelled: boolean;
  filePath: string | null;
}

export interface GetUserProfileRequest {
  objectId: string;
  connection?: UserDirectoryConnectionRequest | null;
}

export interface ListUsersRequest {
  search?: string | null;
  baseDistinguishedName?: string | null;
  department?: string | null;
  accountState?: DirectoryUserAccountStateFilter;
  page?: number;
  pageSize?: number;
  sortField?: DirectoryUserSortField;
  sortDirection?: DirectoryUserSortDirection;
  connection?: UserDirectoryConnectionRequest | null;
}

export interface PageResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface TrendPoint {
  dayUtc: string;
  critical: number;
  high: number;
  medium: number;
  low: number;
  info: number;
  assets: number;
}

export interface VulnerabilityAssetRow {
  asset: NessusAsset;
  matched: boolean;
}

export interface VulnerabilityFindingDetails {
  pluginId: number;
  name: string;
  severity: NessusSeverity;
  cves: string[];
  synopsis: string | null;
  solution: string | null;
  instances: NessusFinding[];
}

export interface VulnerabilityFindingRow {
  pluginId: number;
  name: string;
  severity: NessusSeverity;
  cves: string[];
  affectedAssets: number;
  instances: number;
}

export interface VulnerabilityOverview {
  sync: NessusSyncStatus;
  includedScans: number;
  excludedScans: number;
  assets: number;
  matchedAssets: number;
  unmatchedAssets: number;
  criticalAssets: number;
  highAssets: number;
  criticalInstances: number;
  highInstances: number;
  staleScans: number;
}

export interface VulnerabilityTrend {
  verdict: TrendVerdict;
  points: TrendPoint[];
  commonAssets: number;
  newAssets: number;
  removedAssets: number;
}

export interface NessusAsset {
  assetKey: string;
  displayName: string;
  hostName: string | null;
  fqdn: string | null;
  ipAddress: string | null;
  assetId: string | null;
  lastScanUtc: string;
  critical: number;
  high: number;
  medium: number;
  low: number;
  info: number;
  ports: number[];
  scanSources: string[];
}

export interface NessusFinding {
  assetKey: string;
  pluginId: number;
  port: number;
  protocol: string;
  severity: NessusSeverity;
  name: string;
  cves: string[];
  synopsis: string | null;
  solution: string | null;
  lastObservedUtc: string;
  scanSources: string[];
}

export interface NessusScan {
  id: number;
  name: string;
  excluded: boolean;
  status: string | null;
  latestCompletedHistoryId: number | null;
  latestCompletedUtc: string | null;
  error: string | null;
}

export type NessusSeverity = 'INFO' | 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL' | 'UNKNOWN';

export type NessusSyncPhase = 'IDLE' | 'DISCOVERING_SCANS' | 'IMPORTING_CURRENT_RUNS' | 'PUBLISHING_CURRENT_INVENTORY' | 'IMPORTING_HISTORY' | 'COMPLETED' | 'FAILED';

export interface NessusSyncStatus {
  phase: NessusSyncPhase;
  running: boolean;
  startedAtUtc: string | null;
  lastSuccessfulSyncUtc: string | null;
  error: string | null;
  completedScans: number;
  totalScans: number;
  historySupported: boolean;
  serverVersion: string | null;
}

export type TrendVerdict = 'INSUFFICIENT_DATA' | 'BETTER' | 'WORSE' | 'STABLE';

export interface DeleteNessusCredentialRequest {
}

export interface FindingDetailsRequest {
  pluginId: number;
  asset?: string | null;
}

export interface GetNessusCertificateRequest {
  serverUrl: string;
  requestTimeoutSeconds?: number;
}

export interface KnownHostsRequest {
  knownHosts?: string[] | null;
}

export interface ListAssetsRequest {
  search?: string | null;
  severity?: string | null;
  matching?: string | null;
  knownHosts?: string[] | null;
  page?: number;
  pageSize?: number;
  sortColumn?: string | null;
  sortDirection?: string | null;
}

export interface ListFindingsRequest {
  search?: string | null;
  severity?: string | null;
  asset?: string | null;
  page?: number;
  pageSize?: number;
  sortColumn?: string | null;
  sortDirection?: string | null;
}

export interface NessusCertificateResult {
  sha256Fingerprint: string;
  subject: string;
  validFromUtc: string;
  validToUtc: string;
}

export interface NessusCredentialStatus {
  saved: boolean;
}

export interface SaveNessusCredentialRequest {
  accessKey: string;
  secretKey: string;
  serverUrl?: string | null;
  requestTimeoutSeconds?: number | null;
  trustedCertificateThumbprint?: string | null;
}

export interface StartSyncRequest {
}

export interface StartSyncResult {
  started: boolean;
}

export interface TrendRequest {
  days?: number;
}
