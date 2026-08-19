import { PageHeader } from '../../shared/ui/PageHeader';
import { StatusBadge } from '../../shared/ui/StatusBadge';
import { ReportingSection } from './ReportingSection';

export function ReportingPage() {
  return (
    <div className="flex flex-col gap-4">
      <PageHeader title="Report export" subtitle="Executive summary from saved Inventory and Security data">
        <StatusBadge variant="neutral">This machine</StatusBadge>
      </PageHeader>
      <ReportingSection host={null} />
    </div>
  );
}
