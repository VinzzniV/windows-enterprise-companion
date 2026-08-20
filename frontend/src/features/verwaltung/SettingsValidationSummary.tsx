import { Link, useLocation } from 'react-router-dom';
import { settingsSectionLocation } from './SettingsSectionNavigation';
import type { SettingsValidationIssue } from './settingsValidation';

interface SettingsValidationSummaryProps {
  issues: SettingsValidationIssue[];
}

export function SettingsValidationSummary({ issues }: SettingsValidationSummaryProps) {
  const location = useLocation();
  if (issues.length === 0) return null;

  return (
    <div
      role="alert"
      aria-label="Settings validation"
      className="rounded-lg border border-fail-700 bg-fail-950/30 p-4"
    >
      <h2 className="text-sm font-semibold text-fail-200">Settings validation</h2>
      <p className="mt-1 text-sm text-fail-300">
        {issues.length} {issues.length === 1 ? 'settings problem must' : 'settings problems must'} be fixed before saving.
      </p>
      <ul className="mt-3 space-y-1.5 text-sm">
        {issues.map((validationIssue) => (
          <li key={`${validationIssue.section}:${validationIssue.field}`}>
            <Link
              to={settingsSectionLocation(location.pathname, location.search, validationIssue.section)}
              className="text-fail-200 underline decoration-fail-600 underline-offset-2 hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500"
            >
              <span className="font-semibold">{validationIssue.sectionLabel}:</span>{' '}
              {validationIssue.message}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
