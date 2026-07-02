import { useEffect, useState } from 'react';
import { LogoMark } from '../shared/ui/LogoMark';

interface SplashIntroProps {
  onDone: () => void;
}

const INTRO_DURATION_MS = 1900;
const REDUCED_MOTION_DURATION_MS = 600;
const FADE_OUT_MS = 450;

/**
 * Full-screen intro shown once per app start. Purely decorative: it never
 * blocks startup work (the app renders and loads data underneath) and it
 * collapses to a short static frame when the user prefers reduced motion.
 */
export function SplashIntro({ onDone }: SplashIntroProps) {
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const visibleFor = reducedMotion ? REDUCED_MOTION_DURATION_MS : INTRO_DURATION_MS;

    const startFadeOut = window.setTimeout(() => setLeaving(true), visibleFor);
    const finish = window.setTimeout(onDone, visibleFor + FADE_OUT_MS);
    return () => {
      window.clearTimeout(startFadeOut);
      window.clearTimeout(finish);
    };
  }, [onDone]);

  return (
    <div
      aria-hidden="true"
      className={`fixed inset-0 z-50 flex items-center justify-center bg-slate-950 ${
        leaving ? 'wec-splash-leave pointer-events-none' : ''
      }`}
    >
      <div className="flex flex-col items-center gap-5">
        <LogoMark animated className="wec-splash-logo h-20 w-20 text-slate-200" />
        <div className="flex flex-col items-center gap-1.5 overflow-hidden">
          <span className="wec-splash-title text-lg font-semibold tracking-[0.18em] text-slate-100">
            WINDOWS ENTERPRISE COMPANION
          </span>
          <span className="wec-splash-subtitle text-xs tracking-[0.3em] text-slate-500">
            INVENTORY · SECURITY · DIAGNOSTICS
          </span>
        </div>
        <div className="wec-splash-track h-px w-48 overflow-hidden rounded bg-slate-800">
          <div className="wec-splash-sweep h-full w-1/3 bg-gradient-to-r from-transparent via-sky-400 to-transparent" />
        </div>
      </div>
    </div>
  );
}
