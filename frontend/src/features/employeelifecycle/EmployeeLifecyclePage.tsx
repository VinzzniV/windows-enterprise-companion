import { Navigate, useLocation } from 'react-router-dom';
import { clientPostureFilterFromUrl } from '../clients/clientPosture';

/** Compatibility route for bookmarks created before Fleet posture moved into Clients. */
export function EmployeeLifecyclePage() {
  const location = useLocation();
  const legacyParams = new URLSearchParams(location.search);
  const posture = clientPostureFilterFromUrl(legacyParams.get('filter'));
  const destination = posture === 'ALL'
    ? '/clients'
    : `/clients?posture=${encodeURIComponent(posture)}`;

  return <Navigate replace to={destination} />;
}
