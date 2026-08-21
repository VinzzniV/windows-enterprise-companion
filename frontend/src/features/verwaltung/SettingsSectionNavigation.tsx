import { Link, useLocation } from 'react-router-dom';

export const settingsSections = [
  { id: 'overview', label: 'Effective configuration' },
  { id: 'environment-health', label: 'Environment Health' },
  { id: 'vulnerability-management', label: 'Vulnerability Management' },
  { id: 'patch-management', label: 'Patch Management' },
  { id: 'policy', label: 'Configuration policy' },
] as const;

export type SettingsSectionId = (typeof settingsSections)[number]['id'];

const sectionIds = new Set<string>(settingsSections.map((section) => section.id));

export function isSettingsSection(value: string | null): value is SettingsSectionId {
  return value !== null && sectionIds.has(value);
}

export function settingsSectionElementId(section: SettingsSectionId) {
  return `settings-section-${section}`;
}

export function settingsSectionLocation(pathname: string, search: string, section: SettingsSectionId) {
  const parameters = new URLSearchParams(search);
  if (section === 'overview') parameters.delete('section');
  else parameters.set('section', section);
  const nextSearch = parameters.toString();
  return { pathname, search: nextSearch ? `?${nextSearch}` : '' };
}

interface SettingsSectionNavigationProps {
  activeSection: SettingsSectionId;
  dirtySections?: ReadonlySet<SettingsSectionId>;
  validationCounts?: ReadonlyMap<SettingsSectionId, number>;
}

export function SettingsSectionNavigation({
  activeSection,
  dirtySections,
  validationCounts,
}: SettingsSectionNavigationProps) {
  const location = useLocation();

  return (
    <nav
      aria-label="Settings sections"
      className="sticky top-0 z-20 -mx-1 overflow-x-auto rounded-lg border border-slate-700 bg-slate-950/95 p-1 shadow-lg backdrop-blur"
    >
      <div className="flex min-w-max gap-1">
        {settingsSections.map((section) => {
          const active = section.id === activeSection;
          const dirty = dirtySections?.has(section.id) ?? false;
          const validationCount = validationCounts?.get(section.id) ?? 0;
          return (
            <Link
              key={section.id}
              to={settingsSectionLocation(location.pathname, location.search, section.id)}
              aria-current={active ? 'location' : undefined}
              className={`rounded px-3 py-2 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500 ${
                active
                  ? 'bg-accent-600 text-white'
                  : 'text-slate-300 hover:bg-slate-800 hover:text-white'
              }`}
            >
              {section.label}
              {dirty && (
                <span className="ml-2 inline-flex items-center gap-1 rounded bg-warn-950/60 px-1.5 py-0.5 text-xs text-warn-200">
                  <span aria-hidden="true">●</span>
                  Unsaved changes
                </span>
              )}
              {validationCount > 0 && (
                <span className="ml-2 inline-flex items-center gap-1 rounded bg-fail-950/70 px-1.5 py-0.5 text-xs text-fail-200">
                  <span aria-hidden="true">!</span>
                  {validationCount} validation {validationCount === 1 ? 'issue' : 'issues'}
                </span>
              )}
            </Link>
          );
        })}
      </div>
    </nav>
  );
}
