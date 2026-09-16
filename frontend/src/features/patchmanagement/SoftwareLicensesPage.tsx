import { Link, useSearchParams } from 'react-router-dom';
import { PageHeader } from '../../shared/ui/PageHeader';
import { LicenseWorkspace } from '../microsoft365/LicenseWorkspace';
import { PatchManagementPage } from './PatchManagementPage';

const sections = [['overview', 'Packages'], ['clients', 'Client versions'], ['licenses', 'Licenses'], ['winget', 'Winget packages'], ['history', 'History']] as const;

export function SoftwareLicensesPage() {
  const [parameters] = useSearchParams();
  const section = sections.find(([key]) => key === parameters.get('section'))?.[0] ?? 'overview';
  return <div className="space-y-4">
    <PageHeader title="Software & licenses" subtitle="Package maintenance, installed client versions and tenant license capacity" />
    <nav aria-label="Software and license sections" className="flex flex-wrap gap-4 text-sm">
      {sections.map(([key, label]) => <Link key={key} className="text-accent-400 underline" aria-current={key === section ? 'page' : undefined}
        to={`/software?section=${key}`}>{label}</Link>)}
    </nav>
    {section === 'licenses' ? <LicenseWorkspace /> : <PatchManagementPage />}
  </div>;
}
