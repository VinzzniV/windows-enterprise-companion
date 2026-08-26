import type { ComponentType, ReactNode } from 'react';
import { ActiveDirectoryPage } from '../features/activedirectory/ActiveDirectoryPage';
import { ClientDetailPage } from '../features/clients/ClientDetailPage';
import { ClientsPage } from '../features/clients/ClientsPage';
import { ComparePage } from '../features/clients/ComparePage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { EmployeeLifecyclePage } from '../features/employeelifecycle/EmployeeLifecyclePage';
import { NetworkScanPage } from '../features/networkscan/NetworkScanPage';
import { PatchManagementPage } from '../features/patchmanagement/PatchManagementPage';
import { PrintManagementPage } from '../features/printmanagement/PrintManagementPage';
import { ReportingPage } from '../features/reporting/ReportingPage';
import { SettingsPage } from '../features/verwaltung/SettingsPage';
import { ErrorLogPage } from '../features/verwaltung/ErrorLogPage';
import { VulnerabilitiesPage } from '../features/vulnerabilities/VulnerabilitiesPage';
import { navIcons } from './navIcons';

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
  { id: 'clients', path: '/clients', sectionLabel: 'Clients', Component: ClientsPage, navigation: { group: 'fleet', label: 'Clients', icon: navIcons.clients, searchTerms: ['devices', 'fleet', 'computers'] } },
  { id: 'client-compare', path: '/clients/compare', sectionLabel: 'Clients', Component: ComparePage },
  { id: 'client-detail', path: '/clients/:host', sectionLabel: 'Clients', Component: ClientDetailPage },
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
