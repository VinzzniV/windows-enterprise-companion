import { useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react';
import { invoke } from '../../shared/bridge/bridgeClient';
import type { HygieneDevice, InventorySourceState, ProbeHostsResponse } from '../../shared/api-types';

type IntegrationStatus = 'connected' | 'stale' | 'disconnected' | 'unknown' | 'partial';
type IntegrationKey = 'activeDirectory' | 'kaspersky' | 'opsi' | 'nessus';
type DraggableKey = IntegrationKey | 'client';
type ClientReachability = 'unknown' | 'checking' | 'online' | 'offline' | 'failed';
interface IntegrationNode { key: IntegrationKey; label: string; status: IntegrationStatus; detail: string; }

const STATUS_LABELS: Record<IntegrationStatus, string> = { connected: 'Connected', stale: 'Stale', disconnected: 'Disconnected', unknown: 'Unknown', partial: 'Partial' };
const FINDINGS: Record<IntegrationKey, { stale?: string; missing?: readonly string[] }> = {
  activeDirectory: { stale: 'STALE_AD' }, kaspersky: { stale: 'STALE_KASPERSKY', missing: ['MISSING_KASPERSKY', 'MISSING_KASPERSKY_AGENT', 'MISSING_KES'] },
  opsi: { stale: 'STALE_OPSI', missing: ['MISSING_OPSI'] }, nessus: { stale: 'STALE_NESSUS', missing: ['MISSING_NESSUS'] },
};
const PATHS: Record<IntegrationKey, [string, string, string]> = {
  activeDirectory: ['M500 190 C480 155 520 125 500 76', 'M500 190 C525 155 475 120 500 76', 'M500 190 C490 150 510 120 500 76'],
  kaspersky: ['M430 210 C350 190 275 230 185 210', 'M430 210 C350 235 270 180 185 210', 'M430 210 C345 200 275 220 185 210'],
  opsi: ['M570 210 C655 185 730 235 815 210', 'M570 210 C650 238 730 180 815 210', 'M570 210 C655 200 730 220 815 210'],
  nessus: ['M500 230 C520 265 475 295 500 344', 'M500 230 C475 265 525 300 500 344', 'M500 230 C510 270 490 300 500 344'],
};
const NODE_ANCHORS: Record<IntegrationKey, { x: number; y: number }> = {
  activeDirectory: { x: 500, y: 76 }, kaspersky: { x: 185, y: 210 }, opsi: { x: 815, y: 210 }, nessus: { x: 500, y: 344 },
};
type NodeOffsets = Record<DraggableKey, { x: number; y: number; viewX: number; viewY: number }>;
const EMPTY_OFFSETS: NodeOffsets = {
  client: { x: 0, y: 0, viewX: 0, viewY: 0 },
  activeDirectory: { x: 0, y: 0, viewX: 0, viewY: 0 }, kaspersky: { x: 0, y: 0, viewX: 0, viewY: 0 },
  opsi: { x: 0, y: 0, viewX: 0, viewY: 0 }, nessus: { x: 0, y: 0, viewX: 0, viewY: 0 },
};

export function integrationStatus(key: IntegrationKey, source: InventorySourceState, present: boolean, findingCodes: ReadonlySet<string>): IntegrationStatus {
  if (source.availability === 'PARTIAL' || source.availability === 'TRUNCATED') return 'partial';
  if (source.availability !== 'AVAILABLE') return 'unknown';
  if (FINDINGS[key].stale && findingCodes.has(FINDINGS[key].stale!)) return 'stale';
  if (!present || FINDINGS[key].missing?.some((code) => findingCodes.has(code))) return 'disconnected';
  return 'connected';
}

function StatusIcon({ status }: { status: IntegrationStatus }) {
  if (status === 'connected') return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="m7.7 13.8-3.1-3.1 1.4-1.4 1.7 1.7 6.1-6.1 1.4 1.4z" /></svg>;
  if (status === 'stale') return <svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="7" fill="none"/><path d="M10 6v4l2.8 1.8" fill="none"/></svg>;
  if (status === 'disconnected') return <svg viewBox="0 0 20 20" aria-hidden="true"><path d="m6.3 5 8.7 8.7-1.3 1.3-2.1-2.1-1.2 1.2a4 4 0 0 1-5.6-5.6L6 7.3 5 6.3zm2 4.3-1.9 1.9a2 2 0 0 0 2.8 2.8l1-1zm3.4 1.4 1.9-1.9A2 2 0 0 0 10.8 6L9.6 7.2 8.3 5.9l1.1-1.2A4 4 0 1 1 15 10.3l-2 2z" /></svg>;
  return <svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="7" fill="none"/><path d="M10 6.2v4.3M10 13.5v.3" fill="none"/></svg>;
}

function SystemIcon({ system }: { system: IntegrationKey }) {
  if (system === 'activeDirectory') return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="6" r="2.5"/><circle cx="6" cy="17" r="2.5"/><circle cx="18" cy="17" r="2.5"/><path d="M12 8.5v3M7.8 14.8 10.5 12h3l2.7 2.8"/></svg>;
  if (system === 'kaspersky') return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 5 6v5c0 4.7 2.8 8 7 10 4.2-2 7-5.3 7-10V6z"/><path d="m9 12 2 2 4-5"/></svg>;
  if (system === 'opsi') return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="4" width="6" height="6" rx="1"/><rect x="14" y="4" width="6" height="6" rx="1"/><rect x="9" y="14" width="6" height="6" rx="1"/><path d="M7 10v2h10v-2M12 12v2"/></svg>;
  return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 15c3-7 6-10 9-10 4 0 4 5 7 5-3 6-7 9-12 9-2 0-3-1-4-4Z"/><path d="M8 16c2-2 4-4 8-6"/></svg>;
}

function draggedPath(key: IntegrationKey, offset: NodeOffsets[IntegrationKey], clientOffset: NodeOffsets['client']): string {
  const origin = key === 'activeDirectory' ? { x: 500, y: 190 } : key === 'nessus' ? { x: 500, y: 230 } : key === 'kaspersky' ? { x: 430, y: 210 } : { x: 570, y: 210 };
  const start = { x: origin.x + clientOffset.viewX, y: origin.y + clientOffset.viewY };
  const anchor = NODE_ANCHORS[key];
  const end = { x: anchor.x + offset.viewX, y: anchor.y + offset.viewY };
  const vertical = key === 'activeDirectory' || key === 'nessus';
  const bend = key === 'activeDirectory' || key === 'opsi' ? 16 : -16;
  const first = { x: start.x + (end.x - start.x) * .35 + (vertical ? bend : 0), y: start.y + (end.y - start.y) * .35 + (vertical ? 0 : bend) };
  const second = { x: start.x + (end.x - start.x) * .7 - (vertical ? bend : 0), y: start.y + (end.y - start.y) * .7 - (vertical ? 0 : bend) };
  return `M${start.x} ${start.y} C${first.x} ${first.y} ${second.x} ${second.y} ${end.x} ${end.y}`;
}

function ConnectionBranch({ node, offset, clientOffset, moving }: { node: IntegrationNode; offset: NodeOffsets[IntegrationKey]; clientOffset: NodeOffsets['client']; moving: boolean }) {
  const paths = PATHS[node.key];
  const path = moving ? draggedPath(node.key, offset, clientOffset) : paths[0];
  return <g className={`integration-map__branch integration-map__branch--${node.key} integration-map__branch--${node.status}${moving ? ' integration-map__branch--moving' : ''}`} data-testid={`integration-branch-${node.key}`}>
    <path className="integration-map__path integration-map__path--base" d={path} />
    <path className="integration-map__path integration-map__path--status" d={path} />
    <path className="integration-map__path integration-map__path--energy" d={path} />
  </g>;
}

function Node({ node, offset, moving, onPointerDown, onPointerMove, onPointerEnd }: { node: IntegrationNode; offset: NodeOffsets[IntegrationKey]; moving: boolean; onPointerDown: (event: ReactPointerEvent<HTMLDivElement>, key: DraggableKey) => void; onPointerMove: (event: ReactPointerEvent<HTMLDivElement>) => void; onPointerEnd: (event: ReactPointerEvent<HTMLDivElement>) => void }) {
  const style = { '--drag-x': `${offset.x}px`, '--drag-y': `${offset.y}px` } as CSSProperties;
  return <div data-integration={node.key} data-testid={`integration-node-${node.key}`} style={style} onPointerDown={(event) => onPointerDown(event, node.key)} onPointerMove={onPointerMove} onPointerUp={onPointerEnd} onPointerCancel={onPointerEnd} className={`integration-map__node integration-map__node--${node.key} integration-map__node--${node.status}${moving ? ' integration-map__node--moving' : ''}`}>
    <span className="integration-map__system-icon" data-testid={`integration-icon-${node.key}`}><SystemIcon system={node.key} /></span>
    <span className="integration-map__node-copy"><span className="integration-map__node-title">{node.label}</span><span className="integration-map__detail">{node.detail}</span></span>
    <span className="integration-map__status"><StatusIcon status={node.status} />{STATUS_LABELS[node.status]}</span>
  </div>;
}

export function ClientIntegrationMap({ device, sources }: { device: HygieneDevice; sources: Record<IntegrationKey, InventorySourceState> }) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ key: DraggableKey; pointerId: number; startX: number; startY: number } | null>(null);
  const offsetsRef = useRef<NodeOffsets>(EMPTY_OFFSETS);
  const animationRef = useRef<number | null>(null);
  const springKeyRef = useRef<DraggableKey | null>(null);
  const probeRequestRef = useRef(0);
  const lastProbedHostRef = useRef<string | null>(null);
  const [offsets, setOffsets] = useState<NodeOffsets>(EMPTY_OFFSETS);
  const [movingKey, setMovingKey] = useState<DraggableKey | null>(null);
  const [reachability, setReachability] = useState<ClientReachability>('unknown');
  useEffect(() => () => { if (animationRef.current != null) cancelAnimationFrame(animationRef.current); }, []);
  useEffect(() => {
    if (lastProbedHostRef.current === device.hostName) return;
    lastProbedHostRef.current = device.hostName;
    const request = ++probeRequestRef.current;
    setReachability('checking');
    void invoke<ProbeHostsResponse>('connectivity', 'probeHosts', { hosts: [device.hostName] })
      .then((response) => {
        if (probeRequestRef.current !== request) return;
        setReachability(response.results[0]?.reachable ? 'online' : 'offline');
      })
      .catch(() => { if (probeRequestRef.current === request) setReachability('failed'); });
  }, [device.hostName]);

  const updateOffset = (key: DraggableKey, x: number, y: number) => {
    const rect = canvasRef.current?.getBoundingClientRect();
    const next = { ...offsetsRef.current, [key]: { x, y, viewX: rect ? x * 1000 / rect.width : 0, viewY: rect ? y * 420 / rect.height : 0 } };
    offsetsRef.current = next;
    setOffsets(next);
  };
  const startDrag = (event: ReactPointerEvent<HTMLDivElement>, key: DraggableKey) => {
    if (matchMedia('(max-width: 700px)').matches) return;
    if (animationRef.current != null) {
      cancelAnimationFrame(animationRef.current);
      if (springKeyRef.current) updateOffset(springKeyRef.current, 0, 0);
      animationRef.current = null;
      springKeyRef.current = null;
    }
    dragRef.current = { key, pointerId: event.pointerId, startX: event.clientX - offsetsRef.current[key].x, startY: event.clientY - offsetsRef.current[key].y };
    event.currentTarget.setPointerCapture(event.pointerId);
    setMovingKey(key);
  };
  const moveDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    updateOffset(drag.key, event.clientX - drag.startX, event.clientY - drag.startY);
  };
  const endDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
    const initial = offsetsRef.current[drag.key];
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) { updateOffset(drag.key, 0, 0); setMovingKey(null); return; }
    const started = performance.now();
    springKeyRef.current = drag.key;
    const spring = (now: number) => {
      const progress = Math.min((now - started) / 720, 1);
      const displacement = progress === 1 ? 0 : Math.exp(-6 * progress) * Math.cos(11 * progress);
      updateOffset(drag.key, initial.x * displacement, initial.y * displacement);
      if (progress < 1) animationRef.current = requestAnimationFrame(spring); else { animationRef.current = null; springKeyRef.current = null; setMovingKey(null); }
    };
    animationRef.current = requestAnimationFrame(spring);
  };
  const findingCodes = new Set(device.assessment.findings.map((finding) => finding.code));
  const definitions: Array<[IntegrationKey, string, boolean]> = [['activeDirectory', 'Active Directory', device.activeDirectory.exists], ['kaspersky', 'Kaspersky', device.kaspersky.exists], ['opsi', 'opsi', device.opsi.exists], ['nessus', 'Nessus', Boolean(device.nessus?.exists && device.nessus.lastCompletedScanUtc !== null)]];
  const nodes = definitions.map(([key, label, present]) => ({ key, label, status: integrationStatus(key, sources[key], present, findingCodes), detail: sources[key].error ?? (present ? 'Client found' : 'No matching client') }));
  const reachabilityLabel = reachability === 'online' ? 'Online' : reachability === 'offline' ? 'Offline' : reachability === 'checking' ? 'Pinging…' : reachability === 'failed' ? 'Ping failed' : 'Not checked';
  return <section className={`integration-map${reachability === 'offline' ? ' integration-map--offline' : ''}`} aria-labelledby="client-integration-map-title">
    <div className="integration-map__grid" aria-hidden="true" />
    <div className="integration-map__heading"><div><h2 id="client-integration-map-title"><span />Client integration map</h2><p>Connection state across management systems</p></div><span className="integration-map__legend"><i />Live topology</span></div>
    <div ref={canvasRef} className="integration-map__canvas">
      <svg className="integration-map__lines" viewBox="0 0 1000 420" preserveAspectRatio="none" aria-hidden="true">{nodes.map((node) => <ConnectionBranch key={node.key} node={node} offset={offsets[node.key]} clientOffset={offsets.client} moving={movingKey === node.key || movingKey === 'client'} />)}</svg>
      <div data-testid="integration-node-client" style={{ '--drag-x': `${offsets.client.x}px`, '--drag-y': `${offsets.client.y}px` } as CSSProperties} onPointerDown={(event) => startDrag(event, 'client')} onPointerMove={moveDrag} onPointerUp={endDrag} onPointerCancel={endDrag} className={`integration-map__client integration-map__client--${reachability}${movingKey === 'client' ? ' integration-map__client--moving' : ''}`}><span className="integration-map__client-orbit" aria-hidden="true"/><span className="integration-map__client-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="13" rx="2" fill="none"/><path d="M8 21h8M12 17v4" fill="none"/></svg></span><span className="integration-map__client-copy"><span className="integration-map__client-label">Primary client</span><strong>{device.computerName}</strong><span className="integration-map__client-host" title={device.hostName}>{device.hostName}</span><span className="integration-map__client-presence"><i />{reachabilityLabel}</span></span></div>
      {nodes.map((node) => <Node key={node.key} node={node} offset={offsets[node.key]} moving={movingKey === node.key} onPointerDown={startDrag} onPointerMove={moveDrag} onPointerEnd={endDrag} />)}
    </div>
  </section>;
}
