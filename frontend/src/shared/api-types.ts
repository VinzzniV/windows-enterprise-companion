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

/** Wec.Modules.Reporting.Application.ReportOverview */
export interface ReportOverview {
  inventoryCapturedAtUtc: string | null;
  securityScanCompletedAtUtc: string | null;
  securityScanStatus: string | null;
  securityFindingCount: number | null;
}

/** Wec.Modules.Reporting.Handlers.ExportHtmlReportRequest / ExportJsonReportRequest */
export interface ExportReportRequest {
  openAfterExport?: boolean;
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
}

/** Wec.Modules.Security.Domain.ScanDiff */
export interface ScanDiff {
  latestScanId: number;
  previousScanId: number;
  newFindings: SecurityFinding[];
  resolvedFindings: SecurityFinding[];
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
  depotVersions: PatchDepotVersion[];
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

/** Wec.Modules.PatchManagement.Application.PatchDashboardSummary */
export interface PatchDashboardSummary {
  productCount: number;
  productsWithUpdates: number;
  productsWithFailures: number;
  pendingRolloutCount: number;
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
  command: string;
  note: string;
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
  location: string | null;
  comment: string | null;
  device: PrinterDevice | null;
  deviceError: DeviceQueryError | null;
}

/** Wec.Modules.PrintManagement.Domain.PrintServerSnapshot */
export interface PrintServerSnapshot {
  server: string;
  capturedAtUtc: string;
  printers: PrinterEntry[];
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
