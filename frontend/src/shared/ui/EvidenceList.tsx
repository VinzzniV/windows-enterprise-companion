/** Key/value evidence block shared by security findings and diagnostics. */
export function EvidenceList({ evidence }: { evidence: Record<string, string> }) {
  const entries = Object.entries(evidence);
  if (entries.length === 0) {
    return null;
  }

  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5 rounded bg-slate-950/60 p-2 text-xs">
      {entries.map(([key, value]) => (
        <div key={key} className="contents">
          <dt className="text-slate-500">{key}</dt>
          <dd className="break-all text-slate-300">{value}</dd>
        </div>
      ))}
    </dl>
  );
}
