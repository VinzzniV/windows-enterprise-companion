import { useCallback, useEffect, useRef, useState } from 'react';
import type { HygieneLoadProgress } from '../api-types';
import { subscribe } from '../bridge/bridgeClient';

export interface HygieneOperationState {
  operationId: string | null;
  progress: HygieneLoadProgress | null;
  elapsedSeconds: number;
  begin(): string;
  end(): void;
}

export function useHygieneOperation(): HygieneOperationState {
  const operationIdRef = useRef<string | null>(null);
  const startedAtRef = useRef<number | null>(null);
  const [operationId, setOperationId] = useState<string | null>(null);
  const [progress, setProgress] = useState<HygieneLoadProgress | null>(null);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);

  useEffect(() => subscribe('employeelifecycle', 'hygieneProgress', (payload) => {
    const next = payload as HygieneLoadProgress;
    if (next.operationId === operationIdRef.current) setProgress(next);
  }), []);

  useEffect(() => {
    if (!operationId) return;
    const update = () => setElapsedSeconds(Math.max(0, Math.floor((Date.now() - (startedAtRef.current ?? Date.now())) / 1000)));
    update();
    const timer = window.setInterval(update, 1_000);
    return () => window.clearInterval(timer);
  }, [operationId]);

  const begin = useCallback(() => {
    const next = crypto.randomUUID();
    operationIdRef.current = next;
    startedAtRef.current = Date.now();
    setOperationId(next);
    setProgress(null);
    setElapsedSeconds(0);
    return next;
  }, []);

  const end = useCallback(() => {
    operationIdRef.current = null;
    startedAtRef.current = null;
    setOperationId(null);
  }, []);

  return { operationId, progress, elapsedSeconds, begin, end };
}
