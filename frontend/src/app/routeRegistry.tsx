import { lazy, type ComponentType, type ReactNode } from 'react';
import { navIcons } from './navIcons';

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
const ClientDetailPage = lazy(() => import('../features/clients/ClientDetailPage')
  .then((module) => ({ default: module.ClientDetailPage })));
const DeviceProfilePage = lazy(() => import('../features/clients/DeviceProfilePage')
  .then((module) => ({ default: module.DeviceProfilePage })));
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

export type NavigationGroupKey = 'fleet' | 'administration';

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
  { id: 'dashboard', path: '/', sectionLabel: 'Dashboard', Component: DashboardPage, navigation: { group: 'fleet', label: 'Dashboard', icon: navIcons.dashboard, searchTerms: ['overview', 'status'] } },
  { id: 'action-center', path: '/actions', sectionLabel: 'Action Center', Component: ActionCenterPage, navigation: { group: 'fleet', label: 'Action Center', icon: navIcons.actioncenter, searchTerms: ['work list', 'findings', 'attention', 'issues'] } },
  { id: 'device-cleanup', path: '/cleanup', sectionLabel: 'Device Cleanup', Component: DeviceCleanupPage, navigation: { group: 'fleet', label: 'Device Cleanup', icon: navIcons.devicecleanup, searchTerms: ['stale devices', 'old computers', 'retirement', 'cleanup assistant'] } },
  { id: 'clients', path: '/clients', sectionLabel: 'Clients', Component: ClientsPage, navigation: { group: 'fleet', label: 'Clients', icon: navIcons.clients, searchTerms: ['devices', 'fleet', 'computers'] } },
  { id: 'client-compare', path: '/clients/compare', sectionLabel: 'Clients', Component: ComparePage },
  { id: 'client-detail', path: '/clients/:host', sectionLabel: 'Clients', Component: ClientDetailPage },
  { id: 'device-profile', path: '/devices/:source/:scope/:objectId', sectionLabel: 'Devices', Component: DeviceProfilePage },
  { id: 'users', path: '/users', sectionLabel: 'Users', Component: UsersPage, navigation: { group: 'fleet', label: 'Users', icon: navIcons.users, searchTerms: ['people', 'accounts', 'identity', 'lifecycle'] } },
  { id: 'user-detail', path: '/users/:objectId', sectionLabel: 'Users', Component: UserDetailPage },
  { id: 'resolve-user-sid', path: '/users/resolve', sectionLabel: 'Users', Component: ResolveObservedUserPage },
  { id: 'user-profile', path: '/users/:source/:scope/:objectId', sectionLabel: 'Users', Component: ScopedUserProfilePage },
  { id: 'group-profile', path: '/groups/:source/:scope/:objectId', sectionLabel: 'Groups', Component: GroupProfilePage },
  { id: 'groups', path: '/groups', sectionLabel: 'Groups', Component: GroupsPage },
  { id: 'resolve-group', path: '/groups/resolve', sectionLabel: 'Groups', Component: ResolveGroupPage },
  { id: 'microsoft365', path: '/microsoft365', sectionLabel: 'Microsoft 365', Component: Microsoft365Page, navigation: { group: 'fleet', label: 'Microsoft 365', icon: navIcons.activedirectory, searchTerms: ['m365', 'entra', 'azure ad', 'intune', 'licenses', 'cloud'] } },
  { id: 'active-directory', path: '/activedirectory', sectionLabel: 'Active Directory', Component: ActiveDirectoryPage, navigation: { group: 'fleet', label: 'Active Directory', icon: navIcons.activedirectory, searchTerms: ['ad', 'directory', 'users', 'groups'] } },
  { id: 'employee-lifecycle-legacy', path: '/employeelifecycle', sectionLabel: 'Clients', Component: EmployeeLifecyclePage },
  { id: 'vulnerabilities', path: '/vulnerabilities', sectionLabel: 'Vulnerabilities', Component: VulnerabilitiesPage, navigation: { group: 'fleet', label: 'Vulnerabilities', icon: navIcons.vulnerabilities, searchTerms: ['nessus', 'findings', 'cve'] } },
  { id: 'patch-management', path: '/patchmanagement', sectionLabel: 'Patch Management', Component: PatchManagementPage, navigation: { group: 'fleet', label: 'Patch Management', icon: navIcons.patchmanagement, searchTerms: ['opsi', 'winget', 'updates', 'packages'] } },
  { id: 'print-management', path: '/printmanagement', sectionLabel: 'Print Management', Component: PrintManagementPage, navigation: { group: 'fleet', label: 'Print Management', icon: navIcons.printmanagement, searchTerms: ['printers', 'print servers', 'dhcp'] } },
  { id: 'network-scan', path: '/networkscan', sectionLabel: 'Network Scan', Component: NetworkScanPage, navigation: { group: 'fleet', label: 'Network Scan', icon: navIcons.networkscan, searchTerms: ['discovery', 'subnet', 'hosts'] } },
  { id: 'reporting', path: '/reporting', sectionLabel: 'Report export', Component: ReportingPage, navigation: { group: 'fleet', label: 'Report export', icon: navIcons.reporting, searchTerms: ['reports', 'html', 'json'] } },
  { id: 'settings', path: '/settings', sectionLabel: 'Settings', Component: SettingsPage, navigation: { group: 'administration', label: 'Settings', icon: navIcons.settings, searchTerms: ['configuration', 'connections'] } },
  { id: 'logs', path: '/logs', sectionLabel: 'Error log', Component: ErrorLogPage, navigation: { group: 'administration', label: 'Error log', icon: navIcons.logs, searchTerms: ['errors', 'diagnostics', 'logs'] } },
];

const navigationGroupLabels: Record<NavigationGroupKey, string> = {
  fleet: 'Fleet',
  administration: 'Administration',
};

export const navigationGroups: readonly NavigationGroup[] = (['fleet', 'administration'] as const).map((key) => ({
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

const navigableRoutes = appRoutes.filter((route) => route.navigation);

export function sectionLabelFor(pathname: string): string {
  if (pathname === '/') return 'Dashboard';
  return navigableRoutes.find((route) => route.path !== '/' && pathname.startsWith(route.path))
    ?.sectionLabel ?? 'Overview';
}
