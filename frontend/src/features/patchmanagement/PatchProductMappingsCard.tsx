import { useCallback, useEffect, useState } from 'react';
import type { MappingsResult, ProductMapping, UnmappedSoftware } from '../../shared/api-types';
import { invoke } from '../../shared/bridge/bridgeClient';
import { presentError, type ErrorPresentation } from '../../shared/bridge/errorPresentation';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { DataTable } from '../../shared/ui/DataTable';
import { DetailsDisclosure } from '../../shared/ui/DetailsDisclosure';
import { Input } from '../../shared/ui/Input';
import { ErrorState } from '../../shared/ui/States';

const verifyMappingSaveAction =
  'First check Existing mappings and the history to see whether the mapping was already saved. Repeat it only if no corresponding change is visible there.';
const verifyMappingDeleteAction =
  'First check the existing mappings and history to see whether the mapping was already removed. Repeat the deletion only if it is still present.';

interface PatchProductMappingsCardProps {
  unmappedSoftware: UnmappedSoftware[];
  onMappingsChanged: () => void;
}

export function PatchProductMappingsCard({
  unmappedSoftware,
  onMappingsChanged,
}: PatchProductMappingsCardProps) {
  const [mappings, setMappings] = useState<ProductMapping[]>([]);
  const [loadError, setLoadError] = useState<ErrorPresentation | null>(null);
  const [mappingInputs, setMappingInputs] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<ErrorPresentation | null>(null);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let ignore = false;
    setLoadError(null);
    invoke<MappingsResult>('patchmanagement', 'listMappings', {})
      .then((result) => { if (!ignore) setMappings(result.mappings); })
      .catch((error: unknown) => {
        if (!ignore) {
          setLoadError(presentError(error, {
            message: 'The existing software mappings could not be loaded.',
          }));
        }
      });
    return () => { ignore = true; };
  }, [revision]);

  const saveMapping = useCallback((softwareName: string, productId: string) => {
    setActionError(null);
    invoke<MappingsResult>('patchmanagement', 'saveMapping', {
      softwareName,
      opsiProductId: productId,
    })
      .then((result) => {
        setMappings(result.mappings);
        onMappingsChanged();
      })
      .catch((error: unknown) => {
        setActionError(presentError(error, {
          message: 'The software mapping could not be confirmed as saved.',
          action: verifyMappingSaveAction,
        }));
      });
  }, [onMappingsChanged]);

  const deleteMapping = useCallback((softwareName: string) => {
    setActionError(null);
    invoke<MappingsResult>('patchmanagement', 'deleteMapping', { softwareName })
      .then((result) => {
        setMappings(result.mappings);
        onMappingsChanged();
      })
      .catch((error: unknown) => {
        setActionError(presentError(error, {
          message: 'The software mapping could not be confirmed as removed.',
          action: verifyMappingDeleteAction,
        }));
      });
  }, [onMappingsChanged]);

  return (
    <Card title="Inventoried software without an opsi mapping">
      <div className="flex flex-col gap-3">
        <p className="text-sm text-slate-400">
          WEC inventoried this software, but it is not yet mapped to an opsi product.
          Suggestions are created only for exact name matches.
        </p>
        {loadError && (
          <ErrorState
            title="Mappings unavailable"
            {...loadError}
            controls={(
              <Button onClick={() => setRevision((value) => value + 1)}>
                Reload mappings
              </Button>
            )}
          />
        )}
        {actionError && <ErrorState title="Mapping action failed" {...actionError} />}
        <DataTable
          columns={[
            { header: 'Software', cell: (row: UnmappedSoftware) => row.name },
            { header: 'Versions', cell: (row: UnmappedSoftware) => row.versions.join(', ') || '—' },
            { header: 'Hosts', cell: (row: UnmappedSoftware) => row.hostCount },
            {
              header: 'opsi product id',
              cell: (row: UnmappedSoftware) => {
                const productId = mappingInputs[row.name] ?? row.suggestedProductId ?? '';
                return (
                  <div className="flex items-center gap-2">
                    <div className="w-40">
                      <Input
                        type="text"
                        aria-label={`opsi product id for ${row.name}`}
                        value={productId}
                        onChange={(event) => setMappingInputs((previous) => ({
                          ...previous,
                          [row.name]: event.target.value,
                        }))}
                        placeholder="productId"
                      />
                    </div>
                    <Button
                      onClick={() => saveMapping(row.name, productId)}
                      disabled={productId.trim().length === 0}
                    >
                      Map
                    </Button>
                  </div>
                );
              },
            },
          ]}
          rows={unmappedSoftware}
          emptyMessage="All inventoried programs are mapped, or no inventory data is available yet."
        />
        {mappings.length > 0 && (
          <DetailsDisclosure summary={`Existing mappings (${mappings.length})`}>
            <DataTable
              columns={[
                { header: 'Software', cell: (mapping: ProductMapping) => mapping.softwareName },
                { header: 'opsi product', cell: (mapping: ProductMapping) => mapping.opsiProductId },
                {
                  header: '',
                  cell: (mapping: ProductMapping) => (
                    <Button variant="ghost" onClick={() => deleteMapping(mapping.softwareName)}>
                      Remove
                    </Button>
                  ),
                },
              ]}
              rows={mappings}
              emptyMessage="No mappings."
            />
          </DetailsDisclosure>
        )}
      </div>
    </Card>
  );
}
