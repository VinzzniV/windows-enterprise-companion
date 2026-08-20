import { useState } from 'react';
import type { ExportPrintCsvResult } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { errorText } from '../../shared/bridge/errorText';
import { toCsv, type CsvColumn } from '../../shared/csv';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { Checkbox } from '../../shared/ui/Checkbox';
import { displayedLocation, type MergedPrinter } from './printers';

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString();
}

const distinctJoin = (values: readonly (string | null)[]): string =>
  [...new Set(values.filter((value): value is string => !!value))].join(' | ');

/** One physical-device row; queue variants are already merged upstream. */
const CSV_COLUMNS: CsvColumn<MergedPrinter>[] = [
  { key: 'printer', header: 'Printer', value: (printer) => printer.name, defaultOn: true },
  { key: 'serial', header: 'SerialNumber', value: (printer) => printer.serialNumber, defaultOn: true },
  { key: 'model', header: 'Model', value: (printer) => printer.model, defaultOn: true },
  { key: 'location', header: 'Location', value: displayedLocation, defaultOn: true },
  { key: 'ip', header: 'IPAddress', value: (printer) => printer.deviceIp, defaultOn: true },
  {
    key: 'status',
    header: 'Status',
    value: (printer) => (printer.deviceError ? printer.deviceError.code : printer.status),
    defaultOn: true,
  },
  {
    key: 'toner',
    header: 'Toner',
    value: (printer) => printer.supplies
      .map((supply) => supply.percent != null
        ? `${supply.description} ${supply.percent}%`
        : supply.description)
      .join(' | '),
  },
  {
    key: 'dataFrom',
    header: 'DeviceDataFrom',
    value: (printer) => printer.deviceDataFromUtc
      ? formatTimestamp(printer.deviceDataFromUtc)
      : '',
  },
  { key: 'deviceLocation', header: 'DeviceLocation', value: (printer) => printer.deviceLocation },
  { key: 'serverLocation', header: 'ServerLocation', value: (printer) => printer.serverLocation },
  { key: 'host', header: 'PortAddress', value: (printer) => printer.deviceAddress },
  { key: 'server', header: 'PrintServer', value: (printer) => printer.servers.join(' | ') },
  { key: 'site', header: 'Site', value: (printer) => printer.site },
  {
    key: 'driver',
    header: 'Driver',
    value: (printer) => distinctJoin(printer.queues.map((queue) => queue.driverName)),
  },
  {
    key: 'driverVersion',
    header: 'DriverVersion',
    value: (printer) => distinctJoin(printer.queues.map((queue) => queue.driverVersion)),
  },
  {
    key: 'queues',
    header: 'Queues',
    value: (printer) => printer.queues.map((queue) => queue.queueName).join(' | '),
  },
  { key: 'queueCount', header: 'QueueCount', value: (printer) => printer.queues.length },
];

interface PrintCsvExportCardProps {
  open: boolean;
  printers: readonly MergedPrinter[];
  onClose: () => void;
}

export function PrintCsvExportCard({ open, printers, onClose }: PrintCsvExportCardProps) {
  const [selectedColumns, setSelectedColumns] = useState<ReadonlySet<string>>(
    () => new Set(CSV_COLUMNS.filter((column) => column.defaultOn).map((column) => column.key)),
  );
  const [message, setMessage] = useState<string | null>(null);

  const toggleColumn = (key: string) => {
    setSelectedColumns((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const exportCsv = () => {
    const columns = CSV_COLUMNS.filter((column) => selectedColumns.has(column.key));
    if (columns.length === 0 || printers.length === 0) return;
    setMessage(null);
    invoke<ExportPrintCsvResult>('printmanagement', 'exportCsv', {
      csv: toCsv(printers, columns),
    })
      .then((result) => {
        setMessage(result.cancelled ? 'Export cancelled.' : `Exported to ${result.filePath}`);
        if (!result.cancelled) onClose();
      })
      .catch((error: unknown) => setMessage(errorText(error)));
  };

  return (
    <>
      {open && (
        <Card title="CSV export">
          <div className="flex flex-col gap-3">
            <p className="text-sm text-slate-400">
              Exports the {printers.length} displayed devices — one row per device. Queue
              variants (_B, _A5, _PCL …) are consolidated, and the name is shortened to the
              primary printer. Select columns:
            </p>
            <div className="grid grid-cols-2 gap-x-6 gap-y-1 sm:grid-cols-3 lg:grid-cols-4">
              {CSV_COLUMNS.map((column) => (
                <Checkbox
                  key={column.key}
                  label={column.header}
                  checked={selectedColumns.has(column.key)}
                  onChange={() => toggleColumn(column.key)}
                />
              ))}
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                variant="primary"
                onClick={exportCsv}
                disabled={selectedColumns.size === 0 || printers.length === 0}
              >
                Export
              </Button>
              <Button variant="ghost" onClick={onClose}>
                Cancel
              </Button>
              <span className="text-xs text-muted">
                {selectedColumns.size} column{selectedColumns.size === 1 ? '' : 's'}
              </span>
            </div>
          </div>
        </Card>
      )}
      {message && <p className="text-sm text-slate-300">{message}</p>}
    </>
  );
}
