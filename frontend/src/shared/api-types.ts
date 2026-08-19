/**
 * Mirrors the C# DTOs crossing the bridge (manual sync, architecture plan §5).
 * C# sources of truth are noted per type — review on every DTO change.
 */

/** Wec.Host.Bridge.PingResponse */
export interface PingResponse {
  message: string;
  timestamp: string;
}

/** Wec.Host.Bridge.AppInfoResponse */
export interface AppInfoResponse {
  version: string;
  databasePath: string;
  logDirectory: string;
  isElevated: boolean;
  maxParallelScans: number;
  machineName: string;
  runtimeProfile: string;
}

/** Wec.Modules.Targets.Persistence.TargetRoles (wire values) */
export type TargetRole = 'Client' | 'PrintServer' | 'OpsiServer' | 'DomainController' | 'Generic';

/** Wec.Modules.Targets.Persistence.SavedTarget — host + role + optional user name, never a password (ADR 0007) */
export interface SavedTarget {
  id: number;
  label: string;
  host: string;
  role: TargetRole;
  userName: string | null;
  createdAtUtc: string;
}

/** Wec.Modules.Targets.Handlers.SavedTargetsResult */
export interface SavedTargetsResult {
  targets: SavedTarget[];
}

/** Wec.Core.Privileges.PrivilegeLevel (SCREAMING_SNAKE on the wire, ADR 0003) */
export type PrivilegeLevel = 'STANDARD_USER' | 'ADMINISTRATOR';

/** Wec.Core.Results.ErrorCode (SCREAMING_SNAKE on the wire, ADR 0003) */
export type ErrorCode =
  | 'INTERNAL_ERROR'
  | 'ACCESS_DENIED'
  | 'NOT_FOUND'
  | 'WMI_UNAVAILABLE'
  | 'INVALID_REQUEST'
  | 'UNKNOWN_ACTION'
  | 'NETWORK_PROBE_FAILED'
  | 'EVENT_LOG_UNAVAILABLE'
  | 'FILE_WRITE_FAILED'
  | 'DIRECTORY_UNAVAILABLE'
  | 'DNS_RESOLUTION_FAILED'
  | 'CONNECTION_TIMEOUT'
  | 'AUTHENTICATION_FAILED'
  | 'WIN_RM_UNAVAILABLE'
  | 'UNSUPPORTED_REMOTE_OPERATION';

/** Wec.Core.Messaging.TargetRequest — omit or leave host empty for the local machine */
export interface TargetRequest {
  host?: string | null;
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

/** Wec.Modules.Inventory.Handlers.GetHardwareInfoRequest */
export interface GetHardwareInfoRequest {
  forceRefresh?: boolean;
  target?: TargetRequest | null;
  /** Serve the stored snapshot without touching the network (NOT_FOUND if none). */
  cacheOnly?: boolean;
}

/** Wec.Modules.Inventory.Persistence.StoredInventoryHost */
export interface StoredInventoryHost {
  host: string;
  capturedAtUtc: string;
}

/** Wec.Modules.Inventory.Handlers.ListInventoryHostsResult */
export interface ListInventoryHostsResult {
  hosts: StoredInventoryHost[];
}

/** Wec.Modules.Inventory.Handlers.GetDiskEncryptionStatusRequest */
export interface GetDiskEncryptionStatusRequest {
  target?: TargetRequest | null;
}

/** Wec.Modules.Inventory.Domain.CpuInfo */
export interface CpuInfo {
  name: string;
  physicalCores: number;
  logicalProcessors: number;
  maxClockSpeedMhz: number;
}

/** Wec.Modules.Inventory.Domain.MemoryBank */
export interface MemoryBank {
  manufacturer: string | null;
  partNumber: string | null;
  capacityBytes: number;
  speedMtps: number | null;
}

/** Wec.Modules.Inventory.Domain.DiskDrive */
export interface DiskDrive {
  model: string;
  sizeBytes: number;
  interfaceType: string | null;
  mediaType: string | null;
}

/** Wec.Modules.Inventory.Domain.OperatingSystemInfo */
export interface OperatingSystemInfo {
  caption: string;
  version: string;
  buildNumber: string;
  architecture: string | null;
}

/** Wec.Modules.Inventory.Domain.PhysicalNetworkAdapter */
export interface PhysicalNetworkAdapter {
  name: string;
  macAddress: string | null;
  speedBitsPerSecond: number | null;
  connected: boolean | null;
  adapterType: string | null;
  ipAddresses: string[] | null;
}

/** Wec.Modules.Inventory.Domain.GpuInfo */
export interface GpuInfo {
  name: string;
  memoryBytes: number | null;
  driverVersion: string | null;
}

/** Wec.Modules.Inventory.Domain.MonitorInfo */
export interface MonitorInfo {
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
}

/** Wec.Modules.Inventory.Domain.InstalledSoftwareEntry */
export interface InstalledSoftwareEntry {
  name: string;
  version: string | null;
  publisher: string | null;
}

/** Wec.Modules.Inventory.Domain.HardwareSnapshot — optional sections are null when not captured (old cache entries; installed software on remote targets) */
export interface HardwareSnapshot {
  cpu: CpuInfo;
  memoryBanks: MemoryBank[];
  disks: DiskDrive[];
  operatingSystem: OperatingSystemInfo;
  networkAdapters?: PhysicalNetworkAdapter[] | null;
  gpus?: GpuInfo[] | null;
  monitors?: MonitorInfo[] | null;
  installedSoftware?: InstalledSoftwareEntry[] | null;
  installedSoftwareError?: SoftwareCaptureError | null;
}

/** Wec.Modules.Inventory.Domain.SoftwareCaptureError */
export interface SoftwareCaptureError {
  code: string;
  message: string;
}

/** Wec.Modules.Inventory.Application.HardwareInfoResult */
export interface HardwareInfoResult {
  host: string;
  snapshot: HardwareSnapshot;
  capturedAtUtc: string;
  fromCache: boolean;
}

/** Wec.Modules.Inventory.Domain.VolumeProtectionStatus (SCREAMING_SNAKE on the wire) */
export type VolumeProtectionStatus = 'UNPROTECTED' | 'PROTECTED' | 'UNKNOWN';

/** Wec.Modules.Inventory.Domain.EncryptableVolume */
export interface EncryptableVolume {
  driveLetter: string | null;
  protectionStatus: VolumeProtectionStatus;
}

/** Wec.Modules.Inventory.Application.DiskEncryptionStatus */
export interface DiskEncryptionStatus {
  host: string;
  volumes: EncryptableVolume[];
}

/** Wec.Modules.Security.Domain.FindingSeverity (SCREAMING_SNAKE on the wire) */
export type FindingSeverity = 'INFO' | 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL';

/** Wec.Modules.Security.Domain.FindingCategory (SCREAMING_SNAKE on the wire) */
export type FindingCategory =
  | 'FIREWALL'
  | 'MALWARE_PROTECTION'
  | 'NETWORK_SERVICES'
  | 'ENCRYPTION'
  | 'PLATFORM_INTEGRITY'
  | 'ACCOUNTS'
  | 'OPERATING_SYSTEM';

/** Wec.Modules.Security.Domain.ScanStatus (SCREAMING_SNAKE on the wire) */
export type ScanStatus = 'COMPLETED' | 'COMPLETED_WITH_ERRORS' | 'FAILED';

/** Wec.Core.Results.CheckStatus (SCREAMING_SNAKE on the wire, ADR 0003) */
export type CheckStatus = 'SUCCEEDED' | 'FAILED' | 'REQUIRES_ELEVATION' | 'NOT_APPLICABLE';

/** Wec.Modules.Security.Domain.SecurityFinding */
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

/** Wec.Modules.Security.Domain.SecurityCheckFailure */
export interface SecurityCheckFailure {
  code: ErrorCode;
  message: string;
  requiredPrivilege: PrivilegeLevel | null;
}

/** Wec.Modules.Security.Domain.SecurityCheckResult */
export interface SecurityCheckResult {
  checkId: string;
  status: CheckStatus;
  findings: SecurityFinding[];
  failure: SecurityCheckFailure | null;
}

/** Wec.Modules.Security.Domain.SecurityCoverage */
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

/** Wec.Modules.Security.Handlers — runScan/getLatestScan/getScanHistory payloads */
export interface SecurityScanRequest {
  target?: TargetRequest | null;
}

/** Wec.Modules.Security.Domain.SecurityScanResult */
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

/** Wec.Modules.Security.Application.LatestScanResult */
export interface LatestScanResult {
  scan: SecurityScanResult | null;
}

/** Wec.Modules.Security.Domain.HostScanStatus (SCREAMING_SNAKE on the wire) */
export type HostScanStatus =
  | 'QUEUED'
  | 'CONNECTING'
  | 'RUNNING'
  | 'COMPLETED'
  | 'COMPLETED_WITH_ERRORS'
  | 'FAILED';

/** Wec.Core.Targets.ScanPhase (SCREAMING_SNAKE on the wire) */
export type ScanPhase = 'RESOLVE' | 'CONNECT' | 'AUTHENTICATE' | 'QUERY';

/** Wec.Core.Targets.ScanError */
export interface ScanError {
  host: string;
  phase: ScanPhase;
  code: ErrorCode;
  message: string;
  details: string | null;
}

/** Wec.Modules.Security.Domain.HostScanOutcome */
export interface HostScanOutcome {
  host: string;
  status: HostScanStatus;
  scan: SecurityScanResult | null;
  error: ScanError | null;
}

/** Wec.Modules.Security.Domain.BatchScanResult */
export interface BatchScanResult {
  startedAtUtc: string;
  completedAtUtc: string;
  hosts: HostScanOutcome[];
}

/** Wec.Modules.Security.Handlers.RunBatchSecurityScanRequest */
export interface RunBatchSecurityScanRequest {
  hosts: string[];
  userName?: string | null;
  domain?: string | null;
  password?: string | null;
}

/** security/batchScanProgress event payload */
export interface BatchScanProgress {
  host: string;
  status: HostScanStatus;
}

/** Wec.Modules.Diagnostics.Domain.DiagnosticStatus (SCREAMING_SNAKE on the wire) */
export type DiagnosticStatus = 'PASS' | 'WARNING' | 'FAIL' | 'NOT_RUN';

/** Wec.Modules.Diagnostics.Domain.DiagnosticCategory (SCREAMING_SNAKE on the wire) */
export type DiagnosticCategory =
  | 'NETWORK'
  | 'DNS'
  | 'DOMAIN'
  | 'TIME_SYNCHRONIZATION'
  | 'EVENT_LOG'
  | 'SERVICES'
  | 'SYSTEM';

/** Wec.Modules.Diagnostics.Domain.DiagnosticResult */
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

/** Wec.Modules.ActiveDirectory.Handlers.TestDirectoryConnectionResult */
export interface TestDirectoryConnectionResult {
  domainJoined: boolean;
  domainName: string | null;
  defaultNamingContext: string | null;
}

/** Wec.Modules.Diagnostics.Handlers.RunDiagnosticsRequest */
export interface RunDiagnosticsRequest {
  target?: TargetRequest | null;
}

/** Wec.Modules.Diagnostics.Domain.DiagnosticRunResult */
export interface DiagnosticRunResult {
  startedAtUtc: string;
  completedAtUtc: string;
  results: DiagnosticResult[];
}

/** Wec.Modules.Diagnostics.Handlers.LatestDiagnosticRunResult */
export interface LatestDiagnosticRunResult {
  run: DiagnosticRunResult | null;
}

/** Wec.Modules.Diagnostics.Application.RemoteEventLogEntry */
export interface RemoteEventLogEntry {
  timeGenerated: string | null;
  level: string;
  source: string;
  eventCode: number;
  message: string;
}

/** Wec.Modules.Diagnostics.Application.EventLogQueryResult */
export interface EventLogQueryResult {
  presetKey: string;
  totalMatched: number;
  truncated: boolean;
  entries: RemoteEventLogEntry[];
}

/** Wec.Host.Bridge.LogEntry */
export interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
}

/** Wec.Host.Bridge.RecentLogEntriesResponse */
export interface RecentLogEntriesResult {
  entries: LogEntry[];
  source: string | null;
  clearedAtUtc: string | null;
}

/** Wec.Host.Bridge.ClearRecentLogEntriesResponse */
export interface ClearRecentLogEntriesResult {
  clearedAtUtc: string;
}

/** Wec.Host.Bridge.HostProbeResult — reachable = ICMP ping, manageable = TCP 5985 (WinRM) */
export interface HostProbe {
  host: string;
  reachable: boolean;
  manageable: boolean;
}

/** Wec.Host.Bridge.ProbeHostsResponse */
export interface ProbeHostsResult {
  results: HostProbe[];
}

/** Wec.Modules.Reporting.Application.ReportOverview */
export interface ReportOverview {
  inventoryCapturedAtUtc: string | null;
  securityScanCompletedAtUtc: string | null;
  securityScanStatus: string | null;
  securityFindingCount: number | null;
  securityCoverage: SecurityCoverage | null;
}

/** Wec.Modules.Reporting.Handlers.GetReportOverviewRequest — host null/omitted = local machine */
export interface ReportOverviewRequest {
  host?: string | null;
}

/** Wec.Modules.Reporting.Handlers.ExportHtmlReportRequest / ExportJsonReportRequest — host null/omitted = local machine */
export interface ExportReportRequest {
  openAfterExport?: boolean;
  host?: string | null;
}

/** Wec.Modules.Reporting.Application.ReportExportResult */
export interface ReportExportResult {
  cancelled: boolean;
  filePath: string | null;
}

/** Wec.Modules.ActiveDirectory.Application.DirectoryConnectionRequest — all fields optional; empty = own domain, current user */
export interface DirectoryConnectionRequest {
  domain?: string | null;
  server?: string | null;
  userName?: string | null;
  userDomain?: string | null;
  password?: string | null;
}

/** Wec.Modules.ActiveDirectory.Handlers.GetAdOverviewRequest / GetAdHygieneRequest */
export interface AdAnalysisRequest {
  connection?: DirectoryConnectionRequest | null;
}

/** Wec.Modules.ActiveDirectory.Domain.DomainControllerInfo */
export interface DomainControllerInfo {
  hostName: string;
  distinguishedName: string;
}

/** Wec.Modules.ActiveDirectory.Domain.AdOverviewResult */
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

/** Wec.Modules.ActiveDirectory.Domain.AdAccountInfo */
export interface AdAccountInfo {
  name: string;
  distinguishedName: string;
  lastLogonUtc: string | null;
}

/** Wec.Modules.ActiveDirectory.Domain.AdHygieneRule */
export interface AdHygieneRule {
  ruleId: string;
  title: string;
  matchCount: number;
  examples: AdAccountInfo[];
  recommendation: string;
}

/** Wec.Modules.ActiveDirectory.Domain.PrivilegedGroupInfo */
export interface PrivilegedGroupInfo {
  groupName: string;
  distinguishedName: string;
  directMemberCount: number;
  memberDistinguishedNames: string[];
}

/** Wec.Modules.ActiveDirectory.Domain.AdHygieneResult */
export interface AdHygieneResult {
  domainJoined: boolean;
  domainName: string | null;
  privilegedGroups: PrivilegedGroupInfo[];
  rules: AdHygieneRule[];
  capturedAtUtc: string;
}

/** Wec.Modules.Security.Domain.SeverityCount */
export interface SeverityCount {
  severity: FindingSeverity;
  count: number;
}

/** Wec.Modules.Security.Domain.ScanSummary */
export interface ScanSummary {
  scanId: number;
  startedAtUtc: string;
  completedAtUtc: string;
  status: ScanStatus;
  findingCount: number;
  severityCounts: SeverityCount[];
  coverage: SecurityCoverage;
}

/** Wec.Modules.Security.Domain.ScanDiff */
export interface ScanDiff {
  latestScanId: number;
  previousScanId: number;
  newFindings: SecurityFinding[];
  resolvedFindings: SecurityFinding[];
  isFullyComparable: boolean;
  uncomparedCheckIds: string[];
}

/** Wec.Modules.Security.Domain.ScanHistoryResult */
export interface ScanHistoryResult {
  scans: ScanSummary[];
  changesSinceLastScan: ScanDiff | null;
}

/** Wec.Host.Bridge.RestartElevatedResult */
export interface RestartElevatedResult {
  cancelled: boolean;
}

/** Wec.Modules.PatchManagement.Domain.PatchWorkflowState (ADR 0008) */
export type PatchWorkflowState =
  | 'DETECTED'
  | 'UPDATE_AVAILABLE'
  | 'DOWNLOAD_NEEDED'
  | 'PACKAGE_PREPARED'
  | 'UPLOADED'
  | 'READY_FOR_PILOT'
  | 'APPROVED'
  | 'ROLLOUT_REQUESTED'
  | 'COMPLETED'
  | 'FAILED';

/** Wec.Modules.PatchManagement.Handlers.OpsiConnectRequest */
export interface OpsiConnectRequest {
  server: string;
  userName: string;
  password: string;
  trustServerCertificate: boolean;
}

/** Wec.Modules.PatchManagement.Handlers.OpsiConnectionStatusResult */
export interface OpsiConnectionStatusResult {
  connected: boolean;
  serverUrl: string | null;
  userName: string | null;
  opsiVersion: string | null;
  defaultDepotFilter: string;
  connectionError?: string | null;
}

/** Wec.Modules.PatchManagement.Application.PatchDepotSummary */
export interface PatchDepotSummary {
  id: string;
  description: string | null;
  isConfigServer: boolean;
  clientCount: number;
}

/** Wec.Modules.PatchManagement.Application.PatchDepotVersion */
export interface PatchDepotVersion {
  depotId: string;
  version: string;
}

/** Wec.Modules.PatchManagement.Application.PatchClientState */
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

/** Wec.Modules.PatchManagement.Application.InventoryDetection */
export interface InventoryDetection {
  host: string;
  version: string | null;
}

/** Wec.Modules.PatchManagement.Application.UnmappedSoftware */
export interface UnmappedSoftware {
  name: string;
  versions: string[];
  hostCount: number;
  suggestedProductId: string | null;
}

/** Wec.Modules.PatchManagement.Application.PatchProductRow */
export interface PatchProductRow {
  productId: string;
  name: string | null;
  availableVersion: string | null;
  referenceVersion: string | null;
  manufacturerVersion: string | null;
  manufacturerCheckStatus: 'NOT_CONFIGURED' | 'NOT_CHECKED' | 'SUCCESS' | 'FAILED';
  manufacturerCheckedAtUtc: string | null;
  manufacturerCheckError: string | null;
  manufacturerUpdateAvailable: boolean;
  depotVersions: PatchDepotVersion[];
  missingDepotIds: string[];
  packageStatus: PatchPackageStatus;
  state: PatchWorkflowState;
  installedClientCount: number;
  outdatedClientCount: number;
  failedClientCount: number;
  pendingActionCount: number;
  lastError: string | null;
  clients: PatchClientState[];
  mappedSoftwareNames: string[];
  inventoryDetections: InventoryDetection[];
}

export type PatchPackageStatus =
  | 'CURRENT'
  | 'UPDATE_AVAILABLE'
  | 'DEPOT_DEVIATION'
  | 'MISSING_ON_DEPOT'
  | 'CHECK_FAILED'
  | 'DEPLOYMENT_PENDING';

/** Wec.Modules.PatchManagement.Application.PatchDashboardSummary */
export interface PatchDashboardSummary {
  productCount: number;
  productsWithUpdates: number;
  productsWithDepotDeviation: number;
  productsMissingOnDepots: number;
  productsWithFailures: number;
  pendingRolloutCount: number;
  outdatedClientCount: number;
  clientCount: number;
  depotCount: number;
  unmappedSoftwareCount: number;
}

/** Wec.Modules.PatchManagement.Application.PatchDashboardResult */
export interface PatchDashboardResult {
  serverUrl: string;
  depotFilter: string | null;
  generatedAtUtc: string;
  summary: PatchDashboardSummary;
  depots: PatchDepotSummary[];
  products: PatchProductRow[];
  unmappedSoftware: UnmappedSoftware[];
}

/** Wec.Modules.PatchManagement.Application.RolloutPreviewClient */
export interface RolloutPreviewClient {
  clientId: string;
  depotId: string | null;
  installedVersion: string | null;
  targetVersion: string | null;
  currentState: PatchWorkflowState;
}

/** Wec.Modules.PatchManagement.Application.RolloutPreview */
export interface RolloutPreview {
  productId: string;
  productName: string | null;
  depotFilter: string | null;
  plannedAction: string;
  clients: RolloutPreviewClient[];
  generatedAtUtc: string;
}

/** Wec.Modules.PatchManagement.Application.RolloutRequestOutcome */
export interface RolloutRequestOutcome {
  requestedClientCount: number;
}

/** Wec.Modules.PatchManagement.Application.PreparePackagesPlan */
export interface PreparePackagesPlan {
  productId: string;
  stage: 'TEST' | 'DEPOT_SYNC';
  mode: 'REPOSITORY' | 'CUSTOM_BUILD' | 'CUSTOM_PROMOTION';
  artifactVersion: string | null;
  targets: PackageUpdateTarget[];
  note: string;
  confirmationText: string;
  generatedAtUtc: string;
}

export interface PackageUpdateTarget {
  depotId: string;
  host: string;
  currentVersion: string | null;
  command: string;
}

export interface PackageUpdateTargetOutcome {
  depotId: string;
  success: boolean;
  exitCode: number | null;
  oldVersion: string | null;
  newVersion: string | null;
  error: string | null;
}

export interface PackageUpdateOutcome {
  productId: string;
  stage: 'TEST' | 'DEPOT_SYNC';
  succeededTargetCount: number;
  failedTargetCount: number;
  targets: PackageUpdateTargetOutcome[];
}

export interface PackageWorkflowStatus {
  productId: string;
  testDepotId: string | null;
  testedVersion: string | null;
  testUpdateSucceededAtUtc: string | null;
  pilotApprovedAtUtc: string | null;
  pilotApproved: boolean;
  lastSynchronizationResult: string | null;
  lastSynchronizationAtUtc: string | null;
  lastError: string | null;
}

export interface ProductVersionSource {
  productId: string;
  sourceUrl: string;
  versionPattern: string;
  enabled: boolean;
  latestVersion: string | null;
  lastCheckedUtc: string | null;
  checkStatus: 'NOT_CHECKED' | 'SUCCESS' | 'FAILED';
  lastError: string | null;
}

export interface VersionSourcesResult {
  sources: ProductVersionSource[];
}

export interface VersionCheckOutcome {
  productId: string;
  previousVersion: string | null;
  latestVersion: string | null;
  status: 'SUCCESS' | 'FAILED';
  checkedAtUtc: string;
  error: string | null;
}

export interface VersionCheckResult {
  outcomes: VersionCheckOutcome[];
}

/** Wec.Modules.PatchManagement.Persistence.ProductMapping */
export interface ProductMapping {
  softwareName: string;
  opsiProductId: string;
}

/** Wec.Modules.PatchManagement.Handlers.MappingsResult */
export interface MappingsResult {
  mappings: ProductMapping[];
}

/** Wec.Modules.PatchManagement.Persistence.PatchAuditEntry */
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

/** Wec.Modules.PatchManagement.Handlers.AuditLogResult */
export interface AuditLogResult {
  entries: PatchAuditEntry[];
}

/** Wec.Modules.ActiveDirectory.Application.AdComputer */
export interface AdComputer {
  name: string;
  dnsHostName: string | null;
  operatingSystem: string | null;
  enabled: boolean;
  description: string | null;
  distinguishedName?: string | null;
  lastLogonDate?: string | null;
}

export type HygieneStatus = 'HEALTHY' | 'WARNING' | 'CLEANUP_CANDIDATE' | 'INCOMPLETE' | 'CRITICAL';

export type HygieneFindingCode =
  | 'MISSING_KASPERSKY'
  | 'ORPHAN_KASPERSKY'
  | 'STALE_AD'
  | 'STALE_KASPERSKY'
  | 'OUTDATED_AGENT'
  | 'OUTDATED_KES'
  | 'MISSING_OPSI'
  | 'ORPHAN_OPSI'
  | 'STALE_OPSI'
  | 'MISSING_NESSUS'
  | 'STALE_NESSUS'
  | 'NESSUS_CRITICAL_VULNERABILITIES'
  | 'NESSUS_HIGH_VULNERABILITIES';

export type HygieneFindingSeverity = 'WARNING' | 'CRITICAL';

export interface HygieneFinding {
  code: HygieneFindingCode;
  severity: HygieneFindingSeverity;
  message: string;
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

export interface KasperskyDeviceData {
  exists: boolean;
  lastSeen: string | null;
  agentVersion: string | null;
  kesVersion: string | null;
  administrationGroup: string | null;
}

export interface OpsiDeviceData {
  exists: boolean;
  clientId: string | null;
  description: string | null;
  depotId: string | null;
  lastSeen: string | null;
  clientAgentVersion: string | null;
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

export type InventorySourceAvailability = 'AVAILABLE' | 'NOT_CONNECTED' | 'UNAVAILABLE' | 'TRUNCATED' | 'PARTIAL';

export interface InventorySourceState {
  availability: InventorySourceAvailability;
  error: string | null;
}

export interface EnvironmentSourceStates {
  activeDirectory: InventorySourceState;
  kaspersky: InventorySourceState;
  opsi: InventorySourceState;
  nessus: InventorySourceState;
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

export type NessusSeverity = 'INFO' | 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL';
export type NessusSyncPhase = 'IDLE' | 'DISCOVERING_SCANS' | 'IMPORTING_CURRENT_RUNS' | 'PUBLISHING_CURRENT_INVENTORY' | 'IMPORTING_HISTORY' | 'COMPLETED' | 'FAILED';
export type TrendVerdict = 'INSUFFICIENT_DATA' | 'BETTER' | 'WORSE' | 'STABLE';
export interface NessusSyncStatus { phase: NessusSyncPhase; running: boolean; startedAtUtc: string | null; lastSuccessfulSyncUtc: string | null; error: string | null; completedScans: number; totalScans: number; historySupported: boolean; serverVersion: string | null; }
export interface VulnerabilityOverview { sync: NessusSyncStatus; includedScans: number; excludedScans: number; assets: number; matchedAssets: number; unmatchedAssets: number; criticalAssets: number; highAssets: number; criticalInstances: number; highInstances: number; staleScans: number; }
export interface NessusAsset { assetKey: string; displayName: string; hostName: string | null; fqdn: string | null; ipAddress: string | null; assetId: string | null; lastScanUtc: string; critical: number; high: number; medium: number; low: number; info: number; ports: number[]; scanSources: string[]; }
export interface VulnerabilityAssetRow { asset: NessusAsset; matched: boolean; }
export interface VulnerabilityFindingRow { pluginId: number; name: string; severity: NessusSeverity; cves: string[]; affectedAssets: number; instances: number; }
export interface NessusFinding { assetKey: string; pluginId: number; port: number; protocol: string; severity: NessusSeverity; name: string; cves: string[]; synopsis: string | null; solution: string | null; lastObservedUtc: string; scanSources: string[]; }
export interface VulnerabilityFindingDetails { pluginId: number; name: string; severity: NessusSeverity; cves: string[]; synopsis: string | null; solution: string | null; instances: NessusFinding[]; }
export interface NessusScan { id: number; name: string; excluded: boolean; status: string | null; latestCompletedHistoryId: number | null; latestCompletedUtc: string | null; error: string | null; }
export interface PageResult<T> { items: T[]; total: number; page: number; pageSize: number; }
export interface TrendPoint { dayUtc: string; critical: number; high: number; medium: number; low: number; info: number; assets: number; }
export interface VulnerabilityTrend { verdict: TrendVerdict; points: TrendPoint[]; commonAssets: number; newAssets: number; removedAssets: number; }
export interface NessusSettingsValue { serverUrl: string; requestTimeoutSeconds: number; trustedCertificateThumbprint: string; cacheTtlMinutes: number; backfillDays: number; retentionDays: number; staleWarningDays: number; staleCriticalDays: number; excludedScanIds: number[]; missingNessusExcludedOuPatterns: string[]; missingNessusExcludedHostPatterns: string[]; }
export interface NessusSettingsResult { settings: NessusSettingsValue; restartRequired: boolean; }
export interface NessusCredentialStatus { saved: boolean; }
export interface NessusCertificateResult { sha256Fingerprint: string; subject: string; validFromUtc: string; validToUtc: string; }

export interface ItHygieneResult {
  assessedAtUtc: string;
  domainName: string | null;
  sources: EnvironmentSourceStates;
  summary: HygieneSummary;
  devices: HygieneDevice[];
}

export interface KasperskySettingsValue {
  server: string;
  port: number;
  requestTimeoutSeconds: number;
  trustedCertificateThumbprint: string;
  excludedAdministrationGroups: string[];
}

export interface ItLifecycleSettingsValue {
  inventoryLimit: number;
  staleWarningDays: number;
  staleCriticalDays: number;
  targetAgentVersion: string;
  targetKesVersion: string;
  kaspersky: KasperskySettingsValue;
}

export interface ItLifecycleSettingsResult {
  settings: ItLifecycleSettingsValue;
  restartRequired: boolean;
}

export interface OpsiSettingsValue {
  server: string;
  port: number;
  requestTimeoutSeconds: number;
  defaultDepotFilter: string;
  trustServerCertificate: boolean;
}

export interface OpsiSettingsResult {
  settings: OpsiSettingsValue;
  restartRequired: boolean;
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

/** Wec.Modules.ActiveDirectory.Application.AdComputerSearchResult */
export interface AdComputerSearchResult {
  domainJoined: boolean;
  domainName: string | null;
  computers: AdComputer[];
  truncated: boolean;
}

/** Wec.Modules.PrintManagement.Domain.TonerSupply */
export interface TonerSupply {
  description: string;
  percent: number | null;
  isLow: boolean;
}

/** Wec.Modules.PrintManagement.Domain.PrinterDevice */
export interface PrinterDevice {
  serialNumber: string | null;
  model: string | null;
  sysName: string | null;
  sysLocation: string | null;
  status: string | null;
  pageCount: number | null;
  supplies: TonerSupply[];
}

/** Wec.Modules.PrintManagement.Domain.DeviceQueryError */
export interface DeviceQueryError {
  code: string;
  message: string;
}

/** Wec.Modules.PrintManagement.Domain.PrinterEntry */
export interface PrinterEntry {
  queueName: string;
  shareName: string | null;
  driverName: string | null;
  driverVersion: string | null;
  portName: string | null;
  deviceAddress: string | null;
  deviceIp: string | null;
  location: string | null;
  comment: string | null;
  device: PrinterDevice | null;
  deviceError: DeviceQueryError | null;
  /** Set when `device` was carried over from an earlier scan: capture time of that scan. */
  deviceDataFromUtc?: string | null;
}

/** Wec.Modules.PrintManagement.Domain.UnusedPort */
export interface UnusedPort {
  name: string;
  hostAddress: string | null;
}

/** Wec.Modules.PrintManagement.Domain.UnusedDriver */
export interface UnusedDriver {
  name: string;
  version: string | null;
}

/** Wec.Modules.PrintManagement.Domain.PrintServerSnapshot */
export interface PrintServerSnapshot {
  server: string;
  capturedAtUtc: string;
  printers: PrinterEntry[];
  unusedPorts?: UnusedPort[];
  unusedDrivers?: UnusedDriver[];
}

/** Wec.Core.Printing.PortRemovalResult */
export interface PortRemovalResult {
  name: string;
  removed: boolean;
  error: string | null;
}

/** Wec.Modules.PrintManagement.Handlers.DeleteUnusedPortsRequest */
export interface DeleteUnusedPortsRequest {
  target: TargetRequest;
  portNames: string[];
  confirmed: boolean;
}

/** Wec.Modules.PrintManagement.Handlers.DeleteUnusedPortsResult */
export interface DeleteUnusedPortsResult {
  results: PortRemovalResult[];
}

/** Wec.Modules.PrintManagement.Domain.NotificationCheckStatus */
export type NotificationCheckStatus = 'OK' | 'WARNING' | 'NOT_CHECKED';

/** Wec.Modules.PrintManagement.Domain.NotificationRule */
export interface NotificationRule {
  id: string;
  title: string;
  passed: boolean;
  detail: string;
}

/** Wec.Modules.PrintManagement.Domain.PrinterNotificationCheck */
export interface PrinterNotificationCheck {
  host: string;
  status: NotificationCheckStatus;
  rules: NotificationRule[];
  error: string | null;
}

/** Wec.Modules.PrintManagement.Domain.ClientPrinter */
export interface ClientPrinter {
  name: string;
  driverName: string | null;
  portName: string | null;
  location: string | null;
  shared: boolean;
  isNetwork: boolean;
}

/** Wec.Modules.PrintManagement.Domain.ClientPrinterScan */
export interface ClientPrinterScan {
  host: string;
  capturedAtUtc: string;
  printers: ClientPrinter[];
}

/** Wec.Modules.PrintManagement.Handlers.LatestClientPrinterScanResult */
export interface LatestClientPrinterScanResult {
  scan: ClientPrinterScan | null;
}

/** Wec.Modules.PrintManagement.Persistence.StoredPrintServer */
export interface StoredPrintServer {
  server: string;
  capturedAtUtc: string;
  snapshotCount: number;
}

/** Wec.Modules.PrintManagement.Handlers.ListPrintServersResult */
export interface ListPrintServersResult {
  servers: StoredPrintServer[];
}

/** Wec.Modules.PrintManagement.Persistence.PrintSnapshotStamp */
export interface PrintSnapshotStamp {
  id: number;
  capturedAtUtc: string;
}

/** Wec.Modules.PrintManagement.Handlers.PrintHistoryResult */
export interface PrintHistoryResult {
  snapshots: PrintSnapshotStamp[];
}

/** Wec.Modules.PrintManagement.Application.LeaseDiffDevice */
export interface LeaseDiffDevice {
  serialNumber: string;
  model: string | null;
  queueName: string | null;
  deviceAddress: string | null;
}

/** Wec.Modules.PrintManagement.Application.LeaseQueueSwap */
export interface LeaseQueueSwap {
  queueName: string;
  oldSerialNumber: string;
  newSerialNumber: string;
  oldModel: string | null;
  newModel: string | null;
}

/** Wec.Modules.PrintManagement.Application.PrintServerDiff */
export interface PrintServerDiff {
  server: string;
  baselineAtUtc: string;
  latestAtUtc: string;
  newDevices: LeaseDiffDevice[];
  goneDevices: LeaseDiffDevice[];
  swappedQueues: LeaseQueueSwap[];
  devicesWithoutSerialNumber: number;
}

/** Wec.Modules.PrintManagement.Application.PrintHint */
export interface PrintHint {
  category: string;
  message: string;
}

/** Wec.Modules.PrintManagement.Handlers.PrintHintsResult */
export interface PrintHintsResult {
  hints: PrintHint[];
}

/** Wec.Modules.PrintManagement.Handlers.NetworkPolicyResult */
export interface NetworkPolicyResult {
  printerSubnets: string[];
  legacySubnets: string[];
  dhcpServer: string | null;
}

/** Wec.Modules.PrintManagement.Application.DhcpReservationInfo */
export interface DhcpReservationInfo {
  ip: string;
  mac: string | null;
  name: string | null;
}

/** Wec.Modules.PrintManagement.Application.DhcpCheckResult */
export interface DhcpCheckResult {
  reserved: DhcpReservationInfo[];
}

/** Wec.Modules.PrintManagement.Handlers.ExportPrintCsvResult */
export interface ExportPrintCsvResult {
  cancelled: boolean;
  filePath: string | null;
}

/** Wec.Modules.PrintManagement.Handlers.OpenDeviceWebUiResult */
export interface OpenDeviceWebUiResult {
  opened: boolean;
  url: string;
}

/** Wec.Modules.NetworkScan.Domain.DeviceKind (SCREAMING_SNAKE on the wire) */
export type DeviceKind = 'UNKNOWN' | 'PRINTER' | 'COMPUTER' | 'NETWORK_DEVICE';

/** Wec.Modules.NetworkScan.Domain.NetworkHostRow — isUp=false means a reserved-but-dead IP (stale reservation) */
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

/** Wec.Modules.NetworkScan.Domain.NetworkScanResult */
export interface NetworkScanResult {
  target: string;
  portsScanned: boolean;
  dhcpChecked: boolean;
  hosts: NetworkHostRow[];
}

/** Wec.Modules.NetworkScan.Handlers.ScanNetworkRequest — dhcp optional; set its host to reconcile reservations */
export interface ScanNetworkRequest {
  target: string;
  scanPorts: boolean;
  dhcp?: TargetRequest | null;
}

/** Wec.Modules.EmployeeLifecycle.Domain.EmployeeStatus (SCREAMING_SNAKE on the wire) */
export type EmployeeStatus = 'PLANNED' | 'ONBOARDING' | 'ACTIVE' | 'CHANGING' | 'OFFBOARDING' | 'DISABLED';

/** Wec.Modules.EmployeeLifecycle.Domain.CaseType */
export type LifecycleCaseType = 'ONBOARDING' | 'OFFBOARDING' | 'CHANGE';

/** Wec.Modules.EmployeeLifecycle.Domain.CaseStatus */
export type LifecycleCaseStatus = 'ACTIVE' | 'COMPLETED' | 'CANCELLED';

/** Wec.Modules.EmployeeLifecycle.Domain.LifecycleTaskStatus */
export type LifecycleTaskStatus = 'OPEN' | 'IN_PROGRESS' | 'BLOCKED' | 'DONE' | 'SKIPPED';

/** Wec.Modules.EmployeeLifecycle.Domain.TaskArea */
export type LifecycleTaskArea = 'GENERAL' | 'ACCOUNT' | 'HARDWARE' | 'SOFTWARE' | 'PERMISSIONS' | 'MAILBOX';

/** Wec.Modules.EmployeeLifecycle.Application.EmployeeSummary — dates are yyyy-MM-dd */
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
  activeCaseType: LifecycleCaseType | null;
  openTaskCount: number;
  overdueTaskCount: number;
}

/** Wec.Modules.EmployeeLifecycle.Application.EmployeeDetails */
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

/** Wec.Modules.EmployeeLifecycle.Application.TaskDetails */
export interface LifecycleTask {
  id: number;
  caseId: number;
  title: string;
  area: LifecycleTaskArea;
  status: LifecycleTaskStatus;
  dueDate: string | null;
  assignee: string | null;
  notes: string;
  sortOrder: number;
  isOverdue: boolean;
}

/** Wec.Modules.EmployeeLifecycle.Application.CaseDetails */
export interface LifecycleCase {
  id: number;
  employeeId: number;
  type: LifecycleCaseType;
  status: LifecycleCaseStatus;
  effectiveDate: string | null;
  note: string | null;
  cancelReason: string | null;
  createdUtc: string;
  closedUtc: string | null;
  tasks: LifecycleTask[];
}

/** Wec.Modules.EmployeeLifecycle.Application.LifecycleAuditEntry */
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

/** Wec.Modules.EmployeeLifecycle.Application.EmployeeInput — payload of createEmployee */
export interface EmployeeInput {
  firstName: string;
  lastName: string;
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

/** Wec.Modules.EmployeeLifecycle.Application.EmployeeListResult */
export interface EmployeeListResult {
  employees: EmployeeSummary[];
}

/** Wec.Modules.EmployeeLifecycle.Application.EmployeeDetailsResult */
export interface EmployeeDetailsResult {
  employee: EmployeeDetails;
  cases: LifecycleCase[];
}

/** Wec.Modules.EmployeeLifecycle.Application.CaseResult */
export interface LifecycleCaseResult {
  case: LifecycleCase;
  employee: EmployeeDetails;
}

/** Wec.Modules.EmployeeLifecycle.Application.TaskResult */
export interface LifecycleTaskResult {
  task: LifecycleTask;
}

/** Wec.Modules.EmployeeLifecycle.Application.AuditListResult */
export interface LifecycleAuditListResult {
  entries: LifecycleAuditEntry[];
}

/** Wec.Modules.EmployeeLifecycle.Application.DepartmentInfo */
export interface LifecycleDepartment {
  id: number;
  name: string;
  managerName: string | null;
  ouDistinguishedName: string | null;
}

/** Wec.Modules.EmployeeLifecycle.Application.DepartmentListResult */
export interface LifecycleDepartmentListResult {
  departments: LifecycleDepartment[];
}

/** Wec.Modules.ActiveDirectory.Application.AdUser — groups are memberOf CNs */
export interface AdUser {
  name: string;
  samAccountName: string | null;
  userPrincipalName: string | null;
  enabled: boolean;
  distinguishedName: string;
  groups: string[];
}

/** Wec.Modules.ActiveDirectory.Application.AdUserSearchResult */
export interface AdUserSearchResult {
  domainJoined: boolean;
  domainName: string | null;
  baseDistinguishedName: string | null;
  users: AdUser[];
  truncated: boolean;
}

/** Wec.Modules.ActiveDirectory.Handlers.SearchAdUsersRequest */
export interface SearchAdUsersRequest {
  baseDistinguishedName?: string | null;
  includeDisabled?: boolean;
  connection?: DirectoryConnectionRequest | null;
}
