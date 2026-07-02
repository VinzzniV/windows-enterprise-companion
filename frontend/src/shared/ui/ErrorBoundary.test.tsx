import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ErrorBoundary } from './ErrorBoundary';

function Bomb(): never {
  throw new Error('kaboom');
}

describe('ErrorBoundary', () => {
  it('renders children when nothing throws', () => {
    render(
      <ErrorBoundary>
        <p>healthy content</p>
      </ErrorBoundary>,
    );

    expect(screen.getByText('healthy content')).toBeDefined();
  });

  it('shows the error card instead of blanking the app when a child throws', () => {
    // React logs the error even when a boundary catches it; keep output clean
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});

    render(
      <ErrorBoundary>
        <Bomb />
      </ErrorBoundary>,
    );

    expect(screen.getByText('This page crashed')).toBeDefined();
    expect(screen.getByText('kaboom')).toBeDefined();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    consoleError.mockRestore();
  });
});
