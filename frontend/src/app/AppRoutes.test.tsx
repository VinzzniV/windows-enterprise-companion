import { lazy, type ComponentType } from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AppRoutes } from './App';
import type { AppRouteDefinition } from './routeRegistry';

function routeFor(Component: AppRouteDefinition['Component']): readonly AppRouteDefinition[] {
  return [{ id: 'test', path: '/test', sectionLabel: 'Test', Component }];
}

describe('AppRoutes', () => {
  it('shows the shared route loading state until a lazy workspace is available', async () => {
    let resolveModule: ((module: { default: ComponentType }) => void) | undefined;
    const DeferredPage = lazy(() => new Promise<{ default: ComponentType }>((resolve) => {
      resolveModule = resolve;
    }));

    render(
      <MemoryRouter initialEntries={['/test']}>
        <AppRoutes routes={routeFor(DeferredPage)} />
      </MemoryRouter>,
    );

    expect(screen.getByRole('status').textContent).toContain('Loading workspace');
    resolveModule?.({ default: () => <div>Deferred workspace</div> });
    expect(await screen.findByText('Deferred workspace')).toBeDefined();
  });

  it('contains a rejected lazy import in the route error boundary', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    const BrokenPage = lazy(() => Promise.reject(new Error('Workspace chunk unavailable')));

    render(
      <MemoryRouter initialEntries={['/test']}>
        <AppRoutes routes={routeFor(BrokenPage)} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('This page crashed')).toBeDefined();
    expect(screen.getByText('Workspace chunk unavailable')).toBeDefined();
    consoleError.mockRestore();
  });
});
