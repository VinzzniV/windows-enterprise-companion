import { Link, useLocation } from 'react-router-dom';
import type { UserDirectoryEndpoint } from '../../features/users/users';
import type { ObjectRelationship } from '../api-types.generated';
import { Card } from '../ui/Card';
import { objectPath, objectSourceLabel } from './objectRoutes';

export function ObjectRelationships({ title, links }: { title: string; links: ObjectRelationship[] }) {
  const location = useLocation();
  const directoryEndpoint = (location.state as { directoryEndpoint?: UserDirectoryEndpoint } | null)?.directoryEndpoint;
  return <Card title={title}>{links.length === 0 ? <p className="text-sm text-muted">No relationships established in the loaded evidence.</p>
    : <ul className="space-y-3">{links.map((link, index) => <li key={`${objectPath(link.target)}:${index}`}>
      <Link className="text-accent-400 underline" to={objectPath(link.target)} state={{ directoryEndpoint }}>{link.label}</Link>
      <p className="text-xs">{link.relation} · {objectSourceLabel[link.target.source]} · {link.target.scope}</p>
      <p className="text-xs text-muted">{link.evidence.replaceAll('_', ' ')} · {link.explanation}</p>
    </li>)}</ul>}</Card>;
}
