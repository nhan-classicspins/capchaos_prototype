import { useState, type ReactNode } from 'react';
import { Copy, X } from 'lucide-react';
import clsx from 'clsx';

export function Modal({ title, onClose, children, wide }: { title: ReactNode; onClose: () => void; children: ReactNode; wide?: boolean }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/70 p-6" onMouseDown={onClose}>
      <div className={clsx('panel flex max-h-full w-full flex-col shadow-2xl', wide ? 'max-w-5xl' : 'max-w-2xl')} onMouseDown={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-slate-800 px-4 py-2.5">
          <h2 className="text-sm font-semibold">{title}</h2>
          <button className="btn-icon" onClick={onClose}><X size={15} /></button>
        </div>
        <div className="min-h-0 flex-1 overflow-auto">{children}</div>
      </div>
    </div>
  );
}

export function JsonModal({ title, text, onClose }: { title: string; text: string; onClose: () => void }) {
  return (
    <Modal title={title} onClose={onClose} wide>
      <div className="flex justify-end border-b border-slate-800 px-4 py-1.5">
        <button className="btn" onClick={() => navigator.clipboard.writeText(text)}><Copy size={12} /> Copy</button>
      </div>
      <pre className="whitespace-pre p-4 font-mono text-[11px] leading-relaxed text-slate-200">{text}</pre>
    </Modal>
  );
}

export interface ToolRun { title: string; running: boolean; code?: number; stdout?: string; stderr?: string }

const CODE_TEXT: Record<number, string> = { 0: 'Ok', 1: 'Drift', 2: 'Error' };

export function LevelToolModal({ run, onClose }: { run: ToolRun; onClose: () => void }) {
  const [onlyFail, setOnlyFail] = useState(true);
  // dotnet's build chatter (warnings from compiling the engine-free sources) is not about the levels
  const lines = (run.stdout ?? '').split('\n').filter((l) => !/\bwarning [A-Z]+\d+:/.test(l));
  // a FAIL header is followed by its indented problem lines
  const shown: string[] = [];
  let keep = false;
  for (const l of lines) {
    if (!l.startsWith(' ')) keep = !onlyFail || !l.startsWith('ok');
    if (keep && l.trim()) shown.push(l);
  }
  const fails = lines.filter((l) => l.startsWith('FAIL')).length;
  return (
    <Modal title={run.title} onClose={onClose} wide>
      <div className="flex items-center gap-3 border-b border-slate-800 px-4 py-2 text-xs">
        {run.running ? (
          <span className="animate-pulse text-sky-300">Running dotnet LevelTool… (first build can take a while)</span>
        ) : (
          <span className={run.code === 0 ? 'text-emerald-400' : 'text-rose-400'}>exit {run.code} · {CODE_TEXT[run.code ?? 2] ?? '?'} · {fails} FAIL</span>
        )}
        <label className="ml-auto flex items-center gap-1.5 text-slate-300">
          <input type="checkbox" className="accent-sky-500" checked={onlyFail} onChange={(e) => setOnlyFail(e.target.checked)} /> only problems
        </label>
      </div>
      <pre className="whitespace-pre-wrap p-4 font-mono text-[11px] leading-relaxed">
        {shown.map((l, i) => (
          <div key={i} className={l.startsWith('FAIL') ? 'text-rose-300' : l.startsWith('ok') ? 'text-emerald-300' : l.startsWith(' ') ? 'text-slate-400' : 'text-slate-200'}>{l}</div>
        ))}
        {!run.running && shown.length === 0 && <div className="text-emerald-300">Everything passed.</div>}
        {run.stderr && <div className="mt-3 text-amber-300">{run.stderr}</div>}
      </pre>
    </Modal>
  );
}
