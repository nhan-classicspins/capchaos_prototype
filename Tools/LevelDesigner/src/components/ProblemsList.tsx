import { AlertTriangle, CheckCircle2, XCircle } from 'lucide-react';
import type { Problem } from '../lib/validate';
import type { TrayRef } from '../lib/types';

export function ProblemsList({ problems, onTray }: { problems: Problem[]; onTray?: (t: TrayRef) => void }) {
  if (problems.length === 0)
    return (
      <div className="flex items-center gap-2 text-xs text-emerald-400">
        <CheckCircle2 size={14} /> No problems (V6 solvability: run Validate)
      </div>
    );
  return (
    <ul className="space-y-1.5">
      {problems.map((p, i) => (
        <li key={i}>
          <button
            type="button"
            disabled={!p.tray || !onTray}
            onClick={() => p.tray && onTray?.(p.tray)}
            className="flex w-full items-start gap-1.5 text-left text-[11px] leading-snug disabled:cursor-default"
          >
            {p.severity === 'error' ? <XCircle size={13} className="mt-px shrink-0 text-rose-400" /> : <AlertTriangle size={13} className="mt-px shrink-0 text-amber-400" />}
            <span>
              <span className="mr-1 rounded bg-slate-800 px-1 font-mono text-[10px] text-slate-300">{p.code}</span>
              <span className={p.severity === 'error' ? 'text-rose-200' : 'text-amber-200'}>{p.message}</span>
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}
