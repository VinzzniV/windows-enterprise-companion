import { Component, type ErrorInfo, type ReactNode } from 'react';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/**
 * Last line of defense: a render error in one page shows an error card
 * instead of blanking the whole app. Mounted per route (keyed), so
 * navigating away resets the boundary.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    // eslint-disable-next-line no-console -- surfaces in the WebView2 DevTools console
    console.error('Unhandled render error', error, errorInfo.componentStack);
  }

  render(): ReactNode {
    if (this.state.error === null) {
      return this.props.children;
    }

    return (
      <section className="rounded-lg border border-fail-900 bg-fail-950/40 p-4">
        <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-fail-300">
          This page crashed
        </h2>
        <p className="mb-3 text-sm text-fail-200">{this.state.error.message}</p>
        <button
          type="button"
          onClick={() => this.setState({ error: null })}
          className="rounded border border-fail-700 px-3 py-1.5 text-sm text-fail-200 transition-colors hover:bg-fail-900/40"
        >
          Try again
        </button>
      </section>
    );
  }
}
