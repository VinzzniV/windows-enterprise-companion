import { useId, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Badge, type BadgeTone } from '../ui/Badge';
import type {
  RelationshipEdge,
  RelationshipMapModel,
  RelationshipNode,
  RelationshipStatus,
} from './relationshipModel';
import { MAX_RELATED_NODES } from './relationshipModel';

const statusPresentation: Record<RelationshipStatus, { label: string; tone: BadgeTone }> = {
  connected: { label: 'Connected', tone: 'ok' },
  stale: { label: 'Stale', tone: 'warn' },
  disconnected: { label: 'Disconnected', tone: 'fail' },
  unknown: { label: 'Unknown', tone: 'neutral' },
  partial: { label: 'Partial', tone: 'warn' },
};

function formatObservedAt(value: string | null): string {
  return value ? new Date(value).toLocaleString() : 'Not observed';
}

function NodeCard({ node, primary = false, detailed = false }: { node: RelationshipNode; primary?: boolean; detailed?: boolean }) {
  const content = <>
    <span className="flex min-w-0 flex-1 flex-col">
      <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-500">
        {primary ? 'Primary context' : node.entityType.replaceAll('-', ' ')}
      </span>
      <strong className="break-words text-sm text-slate-100">{node.label}</strong>
      <span className={`${detailed ? '' : 'line-clamp-2'} break-words text-xs text-slate-400`} title={node.context}>{node.context}</span>
      <span className="text-[11px] tabular-nums text-slate-500">Observed: {formatObservedAt(node.observedAtUtc)}</span>
    </span>
    <Badge tone={statusPresentation[node.status].tone}>{statusPresentation[node.status].label}</Badge>
  </>;
  const classes = `flex min-w-0 flex-col items-start gap-2 rounded-lg border px-3 py-2 text-left motion-reduce:transition-none ${primary
    ? 'border-accent-700/80 bg-accent-950/30 shadow-[0_0_24px_rgb(99_102_241_/_0.12)]'
    : 'border-slate-700 bg-slate-900/90 transition-colors hover:border-slate-500 hover:bg-slate-800/90'}`;

  return node.href
    ? <Link className={classes} to={node.href} data-testid={`relationship-node-${node.id}`}>{content}</Link>
    : <div className={classes} tabIndex={0} data-testid={`relationship-node-${node.id}`}>{content}</div>;
}

function EdgeEvidence({ edge }: { edge: RelationshipEdge }) {
  return <div className="min-w-0 border-l border-accent-700/60 pl-3">
    <p className="break-words text-xs font-medium text-accent-300">{edge.relationshipType}</p>
    <p className="break-words text-[11px] text-slate-500">{edge.evidenceSource} · {edge.confidence} confidence</p>
  </div>;
}

function edgeFor(primaryId: string, nodeId: string, edges: readonly RelationshipEdge[]): RelationshipEdge | undefined {
  return edges.find((edge) => (edge.fromNodeId === primaryId && edge.toNodeId === nodeId)
    || (edge.fromNodeId === nodeId && edge.toNodeId === primaryId));
}

function RelationshipMapView({ primary, related, edges }: {
  primary: RelationshipNode;
  related: readonly RelationshipNode[];
  edges: readonly RelationshipEdge[];
}) {
  return <div className="grid gap-4 rounded-lg border border-slate-800 bg-slate-950/35 p-4 lg:grid-cols-[minmax(12rem,0.72fr)_minmax(18rem,1.4fr)] lg:items-center">
    <NodeCard node={primary} primary />
    <ul className="grid min-w-0 gap-2 grid-cols-[repeat(auto-fit,minmax(min(100%,24rem),1fr))]" aria-label="Related entities">
      {related.map((node) => {
        const edge = edgeFor(primary.id, node.id, edges);
        return <li key={node.id} className="grid min-w-0 grid-cols-[minmax(5rem,0.65fr)_minmax(8rem,1fr)] items-center gap-2">
          {edge ? <EdgeEvidence edge={edge} /> : <span aria-hidden="true" />}
          <NodeCard node={node} />
        </li>;
      })}
    </ul>
  </div>;
}

function RelationshipListView({ primary, related, edges }: {
  primary: RelationshipNode;
  related: readonly RelationshipNode[];
  edges: readonly RelationshipEdge[];
}) {
  return <div className="overflow-x-auto rounded-lg border border-slate-800">
    <table className="w-full min-w-[52rem] table-fixed border-collapse break-words text-left text-sm">
      <colgroup><col className="w-[28%]" /><col className="w-[14%]" /><col className="w-[26%]" /><col className="w-[18%]" /><col className="w-[14%]" /></colgroup>
      <thead className="bg-slate-950/60 text-xs uppercase tracking-wide text-slate-500">
        <tr><th className="px-3 py-2">Entity</th><th className="px-3 py-2">Relationship</th><th className="px-3 py-2">Evidence</th><th className="px-3 py-2">Observed</th><th className="px-3 py-2">Confidence</th></tr>
      </thead>
      <tbody className="divide-y divide-slate-800">
        {related.map((node) => {
          const edge = edgeFor(primary.id, node.id, edges);
          return <tr key={node.id} className="align-top">
            <td className="px-3 py-3"><NodeCard node={node} detailed /></td>
            <td className="px-3 py-3 text-slate-300">{edge?.relationshipType ?? 'Related to'}<p className="mt-1 text-xs text-slate-500">{primary.label}</p></td>
            <td className="max-w-sm px-3 py-3 text-slate-300">{edge?.evidenceSource ?? 'Unknown'}<p className="mt-1 text-xs text-slate-500">{edge?.explanation ?? 'No relationship explanation is available.'}</p></td>
            <td className="px-3 py-3 tabular-nums text-slate-400">{formatObservedAt(edge?.observedAtUtc ?? node.observedAtUtc)}</td>
            <td className="px-3 py-3 capitalize text-slate-300">{edge?.confidence ?? 'unknown'}</td>
          </tr>;
        })}
      </tbody>
    </table>
  </div>;
}

export function RelationshipMap({ model, actions }: { model: RelationshipMapModel; actions?: ReactNode }) {
  const [view, setView] = useState<'map' | 'list'>('map');
  const headingId = useId();
  const primary = model.nodes.find((node) => node.id === model.primaryNodeId);
  if (!primary) return <p className="text-sm text-fail-300" role="alert">Relationship context is unavailable.</p>;

  const allRelated = model.nodes.filter((node) => node.id !== primary.id);
  const related = allRelated.slice(0, MAX_RELATED_NODES);
  const visibleNodeIds = new Set([primary.id, ...related.map((node) => node.id)]);
  const edges = model.edges.filter((edge) => visibleNodeIds.has(edge.fromNodeId) && visibleNodeIds.has(edge.toNodeId));

  return <section className="rounded-xl border border-slate-800 bg-slate-900/55 p-4" aria-labelledby={headingId}>
    <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div><h2 id={headingId} className="font-semibold text-slate-100">{model.title}</h2><p className="mt-1 text-xs text-slate-400">{model.description}</p></div>
      <div className="flex flex-wrap items-center gap-2">
        {actions}
        <div className="inline-flex rounded-lg border border-slate-700 p-0.5" aria-label="Relationship presentation">
          <button type="button" aria-pressed={view === 'map'} onClick={() => setView('map')} className={`rounded px-2.5 py-1 text-xs ${view === 'map' ? 'bg-slate-700 text-slate-100' : 'text-slate-400 hover:text-slate-200'}`}>Map</button>
          <button type="button" aria-pressed={view === 'list'} onClick={() => setView('list')} className={`rounded px-2.5 py-1 text-xs ${view === 'list' ? 'bg-slate-700 text-slate-100' : 'text-slate-400 hover:text-slate-200'}`}>List</button>
        </div>
      </div>
    </div>
    {allRelated.length > MAX_RELATED_NODES && <p className="mb-3 text-xs text-warn-300" role="status">Showing {MAX_RELATED_NODES} of {allRelated.length} relationships. Aggregate the remaining context before display.</p>}
    {view === 'map'
      ? <RelationshipMapView primary={primary} related={related} edges={edges} />
      : <RelationshipListView primary={primary} related={related} edges={edges} />}
  </section>;
}
