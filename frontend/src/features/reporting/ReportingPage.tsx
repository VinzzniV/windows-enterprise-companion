import { PageHeader } from '../../shared/ui/PageHeader';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { ReportingSection } from './ReportingSection';

export function ReportingPage() {
  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Reporting" subtitle="Executive summary export">
        <StatusBadge variant="neutral">This machine</StatusBadge>
      </PageHeader>
      <ReportingSection host={null} />
    </div>
  );
}
