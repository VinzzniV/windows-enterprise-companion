import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductMapping, UnmappedSoftware } from '../../shared/api-types';
import { PatchProductMappingsCard } from './PatchProductMappingsCard';

const { invokeMock } = vi.hoisted(() => ({ invokeMock: vi.fn() }));

vi.mock('../../shared/bridge/bridgeClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../shared/bridge/bridgeClient')>();
  return { ...original, invoke: invokeMock };
});

const unmappedSoftware: UnmappedSoftware[] = [
  { name: 'Notepad++', versions: ['8.6', '8.7'], hostCount: 3, suggestedProductId: null },
  { name: '7-Zip', versions: ['24.09'], hostCount: 8, suggestedProductId: '7zip' },
];

const existingMappings: ProductMapping[] = [
  { softwareName: 'Mozilla Firefox', opsiProductId: 'firefox' },
];

describe('PatchProductMappingsCard', () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation((_module: string, action: string, payload: Record<string, unknown>) => {
      if (action === 'listMappings') return Promise.resolve({ mappings: existingMappings });
      if (action === 'saveMapping') {
        return Promise.resolve({
          mappings: [...existingMappings, {
            softwareName: payload.softwareName,
            opsiProductId: payload.opsiProductId,
          }],
        });
      }
      if (action === 'deleteMapping') return Promise.resolve({ mappings: [] });
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });
  });

  it('owns the current mapping request, inputs and confirmed save contract', async () => {
    const onMappingsChanged = vi.fn();
    render(
      <PatchProductMappingsCard
        unmappedSoftware={unmappedSoftware}
        onMappingsChanged={onMappingsChanged}
      />,
    );

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'listMappings',
      {},
    ));
    expect(screen.getByText('8.6, 8.7')).toBeDefined();
    expect(screen.getByText('8')).toBeDefined();
    expect((screen.getByLabelText('opsi product id for 7-Zip') as HTMLInputElement).value)
      .toBe('7zip');

    const notepadRow = screen.getByText('Notepad++').closest('tr')!;
    const notepadInput = within(notepadRow).getByLabelText('opsi product id for Notepad++');
    expect(within(notepadRow).getByRole('button', { name: 'Map' }).hasAttribute('disabled'))
      .toBe(true);
    await userEvent.type(notepadInput, 'npp');
    await userEvent.click(within(notepadRow).getByRole('button', { name: 'Map' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'saveMapping',
      { softwareName: 'Notepad++', opsiProductId: 'npp' },
    ));
    expect(onMappingsChanged).toHaveBeenCalledTimes(1);
    expect(await screen.findByText('Existing mappings (2)')).toBeDefined();
  });

  it('keeps an unavailable mapping list distinct and retries only its read request', async () => {
    invokeMock
      .mockRejectedValueOnce(new Error('Mapping database could not be read'))
      .mockResolvedValueOnce({ mappings: existingMappings });

    render(
      <PatchProductMappingsCard
        unmappedSoftware={unmappedSoftware}
        onMappingsChanged={vi.fn()}
      />,
    );

    expect(await screen.findByText('The existing software mappings could not be loaded.'))
      .toBeDefined();
    await userEvent.click(screen.getByRole('button', { name: 'Reload mappings' }));

    expect(await screen.findByText('Existing mappings (1)')).toBeDefined();
    expect(invokeMock).toHaveBeenCalledTimes(2);
  });

  it('deletes an existing mapping with the exact contract and refreshes its owner', async () => {
    const onMappingsChanged = vi.fn();
    render(
      <PatchProductMappingsCard
        unmappedSoftware={unmappedSoftware}
        onMappingsChanged={onMappingsChanged}
      />,
    );

    await userEvent.click(await screen.findByText('Existing mappings (1)'));
    const mappingRow = screen.getByText('Mozilla Firefox').closest('tr')!;
    await userEvent.click(within(mappingRow).getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(invokeMock).toHaveBeenCalledWith(
      'patchmanagement',
      'deleteMapping',
      { softwareName: 'Mozilla Firefox' },
    ));
    expect(onMappingsChanged).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Existing mappings (1)')).toBeNull();
  });

  it('keeps an unconfirmed save reviewable without retrying the mutation', async () => {
    invokeMock.mockImplementation((_module: string, action: string) => {
      if (action === 'listMappings') return Promise.resolve({ mappings: existingMappings });
      if (action === 'saveMapping') return Promise.reject(new Error('Save outcome is unknown'));
      return Promise.reject(new Error(`Unexpected action ${action}`));
    });

    render(
      <PatchProductMappingsCard
        unmappedSoftware={unmappedSoftware}
        onMappingsChanged={vi.fn()}
      />,
    );
    const notepadRow = (await screen.findByText('Notepad++')).closest('tr')!;
    const input = within(notepadRow).getByLabelText('opsi product id for Notepad++');
    await userEvent.type(input, 'npp');
    await userEvent.click(within(notepadRow).getByRole('button', { name: 'Map' }));

    expect(await screen.findByText('The software mapping could not be confirmed as saved.'))
      .toBeDefined();
    expect(screen.getByText(/First check Existing mappings and the history/)).toBeDefined();
    expect((input as HTMLInputElement).value).toBe('npp');
    expect(invokeMock.mock.calls.filter((call) => call[1] === 'saveMapping')).toHaveLength(1);
  });
});
