import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { RelationshipMap } from './RelationshipMap';
import type { RelationshipMapModel } from './relationshipModel';

const model: RelationshipMapModel = {
  title: 'Device relationships',
  description: 'Stored and explicitly loaded evidence.',
  primaryNodeId: 'device:pc-42',
  nodes: [
    { id: 'device:pc-42', entityType: 'device', label: 'PC-42', context: 'pc-42.corp.local', status: 'unknown', observedAtUtc: null, href: '/clients/PC-42' },
    { id: 'system:ad', entityType: 'management-system', label: 'Active Directory', context: 'Computer object found', status: 'connected', observedAtUtc: '2026-08-20T12:00:00Z', href: '/activedirectory' },
  ],
  edges: [{
    id: 'device:pc-42:ad',
    fromNodeId: 'device:pc-42',
    toNodeId: 'system:ad',
    relationshipType: 'Represented in',
    evidenceSource: 'Active Directory computer inventory',
    observedAtUtc: '2026-08-20T12:00:00Z',
    confidence: 'high',
    explanation: 'Matched by normalized DNS host identity.',
  }],
};

describe('RelationshipMap', () => {
  it('renders keyboard-accessible linked nodes and compact edge evidence', () => {
    render(<MemoryRouter><RelationshipMap model={model} /></MemoryRouter>);

    expect(screen.getByRole('heading', { name: 'Device relationships' })).toBeDefined();
    expect(screen.getByRole('link', { name: /PC-42/ }).getAttribute('href')).toBe('/clients/PC-42');
    expect(screen.getByRole('link', { name: /Active Directory/ }).getAttribute('href')).toBe('/activedirectory');
    expect(screen.getByText('Represented in')).toBeDefined();
    expect(screen.getByText(/high confidence/)).toBeDefined();
  });

  it('offers a semantic list with the same relationship evidence', () => {
    render(<MemoryRouter><RelationshipMap model={model} /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'List' }));

    const table = screen.getByRole('table');
    expect(within(table).getByText('Active Directory')).toBeDefined();
    expect(within(table).getByText('Active Directory computer inventory')).toBeDefined();
    expect(within(table).getByText('Matched by normalized DNS host identity.')).toBeDefined();
    expect(within(table).getByText('high')).toBeDefined();
  });

  it('reports an invalid primary context instead of rendering a broken map', () => {
    render(<MemoryRouter><RelationshipMap model={{ ...model, primaryNodeId: 'missing' }} /></MemoryRouter>);
    expect(screen.getByRole('alert').textContent).toContain('Relationship context is unavailable');
  });
});
