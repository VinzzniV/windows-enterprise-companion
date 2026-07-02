import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// Without vitest globals, Testing Library does not auto-cleanup between
// tests — stale renders accumulate and break role queries
afterEach(cleanup);
