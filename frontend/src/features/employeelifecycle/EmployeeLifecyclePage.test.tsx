import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { EmployeeLifecyclePage } from './EmployeeLifecyclePage';

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname}{location.search}</output>;
}

function renderRedirect(initialEntry: string) {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/employeelifecycle" element={<EmployeeLifecyclePage />} />
        <Route path="/clients" element={<LocationProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('EmployeeLifecyclePage legacy route', () => {
  it('preserves an allowlisted lifecycle filter as Clients posture', async () => {
    renderRedirect('/employeelifecycle?filter=OUTDATED');

    expect((await screen.findByTestId('location')).textContent).toBe('/clients?posture=OUTDATED');
  });

  it('drops unknown legacy filters instead of forwarding arbitrary URL state', async () => {
    renderRedirect('/employeelifecycle?filter=UNKNOWN&extra=unsafe');

    expect((await screen.findByTestId('location')).textContent).toBe('/clients');
  });
});
