import { useState } from 'react';
import { ArrowDown, ArrowUp, Copy, FilePlus2, Search, Trash2 } from 'lucide-react';
import clsx from 'clsx';
import type { ConveyorFile, DocKind, DocRef, LevelFile } from '../lib/types';

interface Props {
  tab: DocKind;
  setTab: (t: DocKind) => void;
  levels: LevelFile[]; // in play order (index first, then the rest)
  conveyors: ConveyorFile[];
  inIndex: Set<string>;
  current: DocRef | null;
  dirty: (ref: DocRef) => boolean;
  broken: (ref: DocRef) => boolean;
  onOpen: (ref: DocRef) => void;
  onNew: (kind: DocKind) => void;
  onDuplicate: (ref: DocRef) => void;
  onDelete: (ref: DocRef) => void;
  onMoveLevel: (id: string, dir: -1 | 1) => void;
}

const DIFF_COLORS: Record<string, string> = {
  tutorial: 'bg-sky-900 text-sky-200', easy: 'bg-emerald-900 text-emerald-200', medium: 'bg-amber-900 text-amber-200',
  hard: 'bg-rose-900 text-rose-200', breather: 'bg-violet-900 text-violet-200',
};

export function Sidebar(p: Props) {
  const [q, setQ] = useState('');
  const match = (s: string) => s.toLowerCase().includes(q.toLowerCase());
  const isCur = (kind: DocKind, id: string) => p.current?.kind === kind && p.current.id === id;
  const sel = p.current?.kind === p.tab ? p.current : null;

  return (
    <aside className="flex w-64 shrink-0 flex-col border-r border-slate-800 bg-slate-900">
      <div className="flex gap-1 border-b border-slate-800 p-1.5">
        {(['level', 'conveyor'] as DocKind[]).map((t) => (
          <button key={t} onClick={() => p.setTab(t)}
            className={clsx('flex-1 rounded px-2 py-1 text-xs font-semibold', p.tab === t ? 'bg-slate-700 text-white' : 'text-slate-400 hover:bg-slate-800')}>
            {t === 'level' ? `LevelConfig (${p.levels.length})` : `ConveyorConfig (${p.conveyors.length})`}
          </button>
        ))}
      </div>
      <div className="flex items-center gap-1 border-b border-slate-800 p-1.5">
        <div className="relative flex-1">
          <Search size={12} className="absolute left-2 top-1/2 -translate-y-1/2 text-slate-500" />
          <input className="input !py-0.5 pl-6 text-xs" placeholder="Filter…" value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <button className="btn-icon" title={`New ${p.tab}`} onClick={() => p.onNew(p.tab)}><FilePlus2 size={14} /></button>
        <button className="btn-icon" title="Duplicate selected" disabled={!sel} onClick={() => sel && p.onDuplicate(sel)}><Copy size={14} /></button>
        <button className="btn-icon hover:!bg-rose-700" title="Delete selected file" disabled={!sel} onClick={() => sel && p.onDelete(sel)}><Trash2 size={14} /></button>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto py-1">
        {p.tab === 'level'
          ? p.levels.filter((l) => match(l.id) || match(l.meta?.name ?? '') || match(l.conveyor)).map((l, i) => {
              const ref: DocRef = { kind: 'level', id: l.id };
              return (
                <div key={l.id} onClick={() => p.onOpen(ref)}
                  className={clsx('group flex cursor-pointer items-center gap-2 px-2 py-1.5', isCur('level', l.id) ? 'bg-sky-900/50' : 'hover:bg-slate-800/70')}>
                  <span className="w-6 text-right font-mono text-[10px] text-slate-500">{p.inIndex.has(l.id) ? i + 1 : '–'}</span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-1.5">
                      <span className="font-mono text-xs text-slate-100">{l.id.slice(6)}</span>
                      <span className="truncate text-xs text-slate-300">{l.meta?.name}</span>
                    </div>
                    <div className="flex items-center gap-1 text-[10px] text-slate-500">
                      {l.meta?.difficulty && <span className={clsx('rounded px-1', DIFF_COLORS[l.meta.difficulty])}>{l.meta.difficulty}</span>}
                      <span className="truncate font-mono">{l.conveyor}</span>
                      {!p.inIndex.has(l.id) && <span className="text-amber-400">not in index</span>}
                    </div>
                  </div>
                  <Dots dirty={p.dirty(ref)} broken={p.broken(ref)} />
                  <div className="hidden flex-col group-hover:flex">
                    <button className="btn-icon !h-4" title="Earlier in play order" onClick={(e) => { e.stopPropagation(); p.onMoveLevel(l.id, -1); }}><ArrowUp size={11} /></button>
                    <button className="btn-icon !h-4" title="Later in play order" onClick={(e) => { e.stopPropagation(); p.onMoveLevel(l.id, 1); }}><ArrowDown size={11} /></button>
                  </div>
                </div>
              );
            })
          : p.conveyors.filter((c) => match(c.id) || match(c.meta?.name ?? '')).map((c) => {
              const ref: DocRef = { kind: 'conveyor', id: c.id };
              const uses = p.levels.filter((l) => l.conveyor === c.id).length;
              return (
                <div key={c.id} onClick={() => p.onOpen(ref)}
                  className={clsx('flex cursor-pointer items-center gap-2 px-3 py-1.5', isCur('conveyor', c.id) ? 'bg-sky-900/50' : 'hover:bg-slate-800/70')}>
                  <div className="min-w-0 flex-1">
                    <div className="font-mono text-xs text-slate-100">{c.id}</div>
                    <div className="text-[10px] text-slate-500">{c.meta?.name ?? '—'} · {c.rows}×{c.width} · {uses} level{uses === 1 ? '' : 's'}</div>
                  </div>
                  <Dots dirty={p.dirty(ref)} broken={p.broken(ref)} />
                </div>
              );
            })}
      </div>
    </aside>
  );
}

function Dots({ dirty, broken }: { dirty: boolean; broken: boolean }) {
  return (
    <div className="flex items-center gap-1">
      {broken && <span className="h-2 w-2 rounded-full bg-rose-500" title="Has problems" />}
      {dirty && <span className="h-2 w-2 rounded-full bg-amber-400" title="Unsaved changes" />}
    </div>
  );
}
