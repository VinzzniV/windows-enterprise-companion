import { lazy, type ComponentType, type ReactNode } from 'react';
import { matchPath } from 'react-router-dom';
import { navIcons } from './navIcons';

const DevicesWorkingSetPage = lazy(() => import('../shared/objects/ObjectWorkingSetPage').then(module => ({ default: module.DevicesWorkingSetPage })));
const DataSourcesPage = lazy(() => import('../features/verwaltung/DataSourcesPage').then(module => ({ default: module.DataSourcesPage })));
const SoftwareLicensesPage = lazy(() => import('../features/patchmanagement/SoftwareLicensesPage').then(module => ({ default: module.SoftwareLicensesPage })));
const UsersWorkingSetPage = lazy(() => import('../shared/objects/ObjectWorkingSetPage').then(module => ({ default: module.UsersWorkingSetPage })));
const GroupsWorkingSetPage = lazy(() => import('../shared/objects/ObjectWorkingSetPage').then(module => ({ default: module.GroupsWorkingSetPage })));

const DashboardPage = lazy(() => import('../features/dashboard/DashboardPage')
  .then((module) => ({ default: module.DashboardPage })));
const ActionCenterPage = lazy(() => import('../features/actioncenter/ActionCenterPage')
  .then((module) => ({ default: module.ActionCenterPage })));
const DeviceCleanupPage = lazy(() => import('../features/devicecleanup/DeviceCleanupPage')
  .then((module) => ({ default: module.DeviceCleanupPage })));
const ClientsPage = lazy(() => import('../features/clients/ClientsPage')
  .then((module) => ({ default: module.ClientsPage })));
const ComparePage = lazy(() => import('../features/clients/ComparePage')
  .then((module) => ({ default: module.ComparePage })));
const ClientDetailPage = lazy(() => import('../features/clients/ClientEntryPage')
  .then((module) => ({ default: module.ClientEntryPage })));
const DeviceProfilePage = lazy(() => import('../features/clients/DeviceProfilePage')
  .then((module) => ({ default: module.DeviceProfilePage })));
const ManagementRecordPage = lazy(() => import('../features/clients/ManagementRecordPage').then(module => ({ default: module.ManagementRecordPage })));
const UsersPage = lazy(() => import('../features/users/UsersPage')
  .then((module) => ({ default: module.UsersPage })));
const Microsoft365Page = lazy(() => import('../features/microsoft365/Microsoft365Page')
  .then((module) => ({ default: module.Microsoft365Page })));
const UserDetailPage = lazy(() => import('../features/users/UserDetailPage')
  .then((module) => ({ default: module.UserDetailPage })));
const ScopedUserProfilePage = lazy(() => import('../features/users/ScopedUserProfilePage')
  .then((module) => ({ default: module.ScopedUserProfilePage })));
const ResolveObservedUserPage = lazy(() => import('../features/users/ResolveObservedUserPage')
  .then((module) => ({ default: module.ResolveObservedUserPage })));
const GroupProfilePage = lazy(() => import('../features/groups/GroupProfilePage')
  .then((module) => ({ default: module.GroupProfilePage })));
const GroupsPage = lazy(() => import('../features/groups/GroupsPage')
  .then((module) => ({ default: module.GroupsPage })));
const ResolveGroupPage = lazy(() => import('../features/groups/ResolveGroupPage')
  .then((module) => ({ default: module.ResolveGroupPage })));
const ActiveDirectoryPage = lazy(() => import('../features/activedirectory/ActiveDirectoryPage')
  .then((module) => ({ default: module.ActiveDirectoryPage })));
const EmployeeLifecyclePage = lazy(() => import('../features/employeelifecycle/EmployeeLifecyclePage')
  .then((module) => ({ default: module.EmployeeLifecyclePage })));
const VulnerabilitiesPage = lazy(() => import('../features/vulnerabilities/VulnerabilitiesPage')
  .then((module) => ({ default: module.VulnerabilitiesPage })));
const PatchManagementPage = lazy(() => import('../features/patchmanagement/PatchManagementPage')
  .then((module) => ({ default: module.PatchManagementPage })));
const PrintManagementPage = lazy(() => import('../features/printmanagement/PrintManagementPage')
  .then((module) => ({ default: module.PrintManagementPage })));
const NetworkScanPage = lazy(() => import('../features/networkscan/NetworkScanPage')
  .then((module) => ({ default: module.NetworkScanPage })));
const ReportingPage = lazy(() => import('../features/reporting/ReportingPage')
  .then((module) => ({ default: module.ReportingPage })));
const SettingsPage = lazy(() => import('../features/verwaltung/SettingsPage')
  .then((module) => ({ default: module.SettingsPage })));
const ErrorLogPage = lazy(() => import('../features/verwaltung/ErrorLogPage')
  .then((module) => ({ default: module.ErrorLogPage })));

export type NavigationGroupKey = 'work' | 'objects' | 'operations' | 'administration';

export interface AppRouteDefinition {
  id: string;
  path: string;
  sectionLabel: string;
  Component: ComponentType;
  navigation?: {
    group: NavigationGroupKey;
    label: string;
    icon: ReactNode;
    searchTerms: readonly string[];
  };
}

export interface NavigationItem {
  to: string;
  label: string;
  icon: ReactNode;
  searchTerms: readonly string[];
}

export interface NavigationGroup {
  key: NavigationGroupKey;
  label: string;
  items: readonly NavigationItem[];
}

export const appRoutes: readonly AppRouteDefinition[] = [
  { id: 'data-sources', path: '/sources', sectionLabel: 'Data sources', Component: DataSourcesPage, navigation: { group: 'administration', label: 'Data sources', icon: navIcons.activedirectory, searchTerms: ['connections', 'ad', 'microsoft365', 'entra', 'ksc', 'opsi', 'nessus'] } },
  { id: 'software-licenses', path: '/software', sectionLabel: 'Software & licenses', Component: SoftwareLicensesPage, navigation: { group: 'operations', label: 'Software & licenses', icon: navIcons.patchmanagement, searchTerms: ['patch', 'packages', 'winget', 'licenses', 'sku', 'updates'] } },
  { id: 'dashboard', path: '/', sectionLabel: 'Overview', Component: DashboardPage, navigation: { group: 'work', label: 'Overview', icon: navIcons.dashboard, searchTerms: ['dashboard', 'status'] } },
  { id: 'action-center', path: '/actions', sectionLabel: 'Action Center', Component: ActionCenterPage, navigation: { group: 'work', label: 'Action Center', icon: navIcons.actioncenter, searchTerms: ['work list', 'findings', 'attention', 'issues'] } },
  { id: 'device-cleanup', path: '/cleanup', sectionLabel: 'Devices', Component: DeviceCleanupPage },
  { id: 'clients', path: '/clients', sectionLabel: 'Devices', Component: ClientsPage },
  { id: 'client-compare', path: '/clients/compare', sectionLabel: 'Devices', Component: ComparePage },
  { id: 'client-detail', path: '/clients/:host', sectionLabel: 'Devices', Component: ClientDetailPage },
  { id: 'device-profile', path: '/devices/:source/:scope/:objectId', sectionLabel: 'Devices', Component: DeviceProfilePage },
  { id: 'management-record', path: '/devices/records/:source/:workspace/:snapshot/:index', sectionLabel: 'Devices', Component: ManagementRecordPage },
  { id: 'devices-workspace', path: '/devices', sectionLabel: 'Devices', Component: DevicesWorkingSetPage, navigation: { group: 'objects', label: 'Devices', icon: navIcons.clients, searchTerms: ['clients', 'computers', 'inventory', 'intune', 'cleanup', 'compare', 'posture'] } },
  { id: 'users-workspace', path: '/users/workspace', sectionLabel: 'Users', Component: UsersWorkingSetPage },
  { id: 'groups-workspace', path: '/groups/workspace', sectionLabel: 'Groups', Component: GroupsWorkingSetPage },
  { id: 'users', path: '/users', sectionLabel: 'Users', Component: UsersWorkingSetPage, navigation: { group: 'objects', label: 'Users', icon: navIcons.users, searchTerms: ['accounts', 'identity', 'leaver', 'lifecycle'] } },
  { id: 'directory-users', path: '/users/directory', sectionLabel: 'Users', Component: UsersPage },
  { id: 'user-detail', path: '/users/:objectId', sectionLabel: 'Users', Component: UserDetailPage },
  { id: 'resolve-user-sid', path: '/users/resolve', sectionLabel: 'Users', Component: ResolveObservedUserPage },
  { id: 'user-profile', path: '/users/:source/:scope/:objectId', sectionLabel: 'Users', Component: ScopedUserProfilePage },
  { id: 'group-profile', path: '/groups/:source/:scope/:objectId', sectionLabel: 'Groups', Component: GroupProfilePage },
  { id: 'groups', path: '/groups', sectionLabel: 'Groups', Component: GroupsWorkingSetPage, navigation: { group: 'objects', label: 'Groups', icon: navIcons.activedirectory, searchTerms: ['membership', 'access', 'security groups'] } },
  { id: 'directory-groups', path: '/groups/directory', sectionLabel: 'Groups', Component: GroupsPage },
  { id: 'resolve-group', path: '/groups/resolve', sectionLabel: 'Groups', Component: ResolveGroupPage },
  { id: 'microsoft365', path: '/microsoft365', sectionLabel: 'Data sources', Component: Microsoft365Page },
  { id: 'active-directory', path: '/activedirectory', sectionLabel: 'Data sources', Component: ActiveDirectoryPage },
  { id: 'employee-lifecycle-legacy', path: '/employeelifecycle', sectionLabel: 'Devices', Component: EmployeeLifecyclePage },
  { id: 'vulnerabilities', path: '/vulnerabilities', sectionLabel: 'Vulnerabilities', Component: VulnerabilitiesPage, navigation: { group: 'operations', label: 'Vulnerabilities', icon: navIcons.vulnerabilities, searchTerms: ['nessus', 'findings', 'cve'] } },
  { id: 'patch-management', path: '/patchmanagement', sectionLabel: 'Software & licenses', Component: PatchManagementPage },
  { id: 'print-management', path: '/printmanagement', sectionLabel: 'Print Management', Component: PrintManagementPage, navigation: { group: 'operations', label: 'Print Management', icon: navIcons.printmanagement, searchTerms: ['printers', 'print servers', 'dhcp'] } },
  { id: 'network-scan', path: '/networkscan', sectionLabel: 'Network Scan', Component: NetworkScanPage, navigation: { group: 'operations', label: 'Network Scan', icon: navIcons.networkscan, searchTerms: ['discovery', 'subnet', 'hosts'] } },
  { id: 'reporting', path: '/reporting', sectionLabel: 'Reports', Component: ReportingPage, navigation: { group: 'operations', label: 'Reports', icon: navIcons.reporting, searchTerms: ['export', 'html', 'json'] } },
  { id: 'settings', path: '/settings', sectionLabel: 'Settings', Component: SettingsPage, navigation: { group: 'administration', label: 'Settings', icon: navIcons.settings, searchTerms: ['configuration', 'connections'] } },
  { id: 'logs', path: '/logs', sectionLabel: 'Error log', Component: ErrorLogPage, navigation: { group: 'administration', label: 'Error log', icon: navIcons.logs, searchTerms: ['errors', 'diagnostics', 'logs'] } },
];

const navigationGroupLabels: Record<NavigationGroupKey, string> = {
  work: 'Work',
  objects: 'Objects',
  operations: 'Operations',
  administration: 'Administration',
};

export const navigationGroups: readonly NavigationGroup[] = (['work', 'objects', 'operations', 'administration'] as const).map((key) => ({
  key,
  label: navigationGroupLabels[key],
  items: appRoutes
    .filter((route) => route.navigation?.group === key)
    .map((route) => ({
      to: route.path,
      label: route.navigation!.label,
      icon: route.navigation!.icon,
      searchTerms: route.navigation!.searchTerms,
    })),
}));

export function sectionLabelFor(pathname: string): string {
  return appRoutes.find(route => matchPath({ path: route.path, end: true }, pathname))?.sectionLabel ?? 'Overview';
}
