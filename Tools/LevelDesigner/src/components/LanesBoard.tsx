import { ArrowLeft, ArrowRight, Lock, Plus, Trash2 } from 'lucide-react';
import clsx from 'clsx';
import type { LevelFile, TrayRef } from '../lib/types';
import { flavor, MYSTERY, ROPE, SIZE_NAMES } from '../lib/colors';
import { linkOf, moveLane, removeLane } from '../lib/levelOps';
import { Swatch } from './ui';

interface Props {
  level: LevelFile;
  selected: TrayRef | null;
  linking: boolean;
  brush: number;
  onSelect: (t: TrayRef) => void;
  setLevel: (l: LevelFile) => void;
  update: (fn: (l: LevelFile) => void) => void;
  errorTrays: Set<string>;
}

const LINK_HUES = ['#EBD5A4', '#f472b6', '#38bdf8', '#a3e635', '#fb923c', '#c084fc'];

/** The bottom half of the game screen: the slot row, then the lanes (front tray on top, next to the slots). */
export function LanesBoard({ level, selected, linking, brush, onSelect, setLevel, update, errorTrays }: Props) {
  const lockOf = (slot: number) => level.slotLocks?.find((l) => l.slot === slot)?.lockTurns;
  const paint = brush === 0 ? 1 : brush;

  return (
    <div className="flex h-full flex-col bg-board-ground">
      {/* slots */}
      <div className="flex items-center justify-center gap-2 border-b border-slate-900/40 bg-board-band px-4 py-2">
        {Array.from({ length: level.slots + level.extraSlots }, (_, s) => {
          const open = s < level.slots;
          const turns = lockOf(s);
          return (
            <div key={s} title={open ? (turns ? `Slot ${s} locked for ${turns} trays (R22)` : `Slot ${s}`) : `Extra slot (R20): coins / rewarded ad`}
              className={clsx('relative flex h-9 w-12 items-center justify-center rounded-md border text-xs font-bold',
                open ? 'border-[#5A6090] bg-board-slot text-slate-400' : 'border-dashed border-[#5A6090] bg-board-locked text-[#5BD45B]')}>
              {open ? (turns ? <span className="flex items-center gap-0.5 text-slate-100"><Lock size={11} />{turns}</span> : s) : '+'}
            </div>
          );
        })}
      </div>

      {/* lanes */}
      <div className="min-h-0 flex-1 overflow-auto">
        <div className="flex min-h-full items-start justify-center gap-3 px-4 py-3">
          {level.lanes.map((lane, j) => (
            <div key={j} className="flex w-[118px] shrink-0 flex-col rounded-lg bg-[#25272E]/80 p-1.5">
              <div className="mb-1.5 flex items-center justify-between px-0.5">
                <span className="font-mono text-[10px] text-slate-400">lane {j} · {lane.length}</span>
                <div className="flex">
                  <button className="btn-icon !h-5 !w-5" disabled={j === 0} title="Move lane left" onClick={() => setLevel(moveLane(level, j, -1))}><ArrowLeft size={11} /></button>
                  <button className="btn-icon !h-5 !w-5" disabled={j === level.lanes.length - 1} title="Move lane right" onClick={() => setLevel(moveLane(level, j, 1))}><ArrowRight size={11} /></button>
                  <button className="btn-icon !h-5 !w-5 hover:!bg-rose-700" disabled={level.lanes.length <= 1} title="Delete lane" onClick={() => setLevel(removeLane(level, j))}><Trash2 size={11} /></button>
                </div>
              </div>
              <div className="flex flex-col gap-1.5">
                {lane.map((t, i) => {
                  const ref = { lane: j, tray: i };
                  const sel = selected?.lane === j && selected.tray === i;
                  const link = linkOf(level, ref);
                  const f = flavor(t.color);
                  const body = t.hidden ? MYSTERY.body : f.body;
                  const shade = t.hidden ? MYSTERY.shade : f.shade;
                  const size = t.size ?? 1;
                  const bad = errorTrays.has(`${j}:${i}`);
                  return (
                    <button key={i} type="button" onClick={() => onSelect(ref)}
                      className={clsx('relative flex w-full flex-col items-center justify-center rounded-md border-2 text-[10px] font-bold text-white shadow transition',
                        sel ? 'border-white ring-2 ring-sky-400' : bad ? 'border-rose-500' : 'border-transparent',
                        linking && !sel && 'hover:ring-2 hover:ring-amber-300')}
                      style={{ background: `linear-gradient(180deg, ${body} 0%, ${body} 62%, ${shade} 100%)`, height: 26 + (size - 1) * 18 }}>
                      <span className="pointer-events-none absolute left-1 top-0.5 font-mono text-[9px] text-white/80">{i}</span>
                      <div className="flex items-center gap-1 drop-shadow">
                        {t.hidden && <span className="text-sm leading-none">?</span>}
                        {size > 1 && <span className="rounded bg-black/30 px-1">{SIZE_NAMES[size]}</span>}
                        {t.hidden && <span className="h-2.5 w-2.5 rounded-full border border-white/60" style={{ background: f.body }} title="real colour" />}
                      </div>
                      {(t.lockTurns ?? 0) > 0 && (
                        <span className="absolute -right-1 -top-1.5 flex items-center gap-0.5 rounded bg-[#2F3554] px-1 text-[9px] text-white ring-1 ring-[#D4DBEA]"><Lock size={8} />{t.lockTurns}</span>
                      )}
                      {link && (
                        <span className="absolute -left-1 -bottom-1.5 rounded-full px-1.5 text-[9px] font-bold text-slate-900 ring-1 ring-[#5A4630]" style={{ background: LINK_HUES[link.index % LINK_HUES.length] ?? ROPE }}
                          title={`Linked with lane ${link.partner.lane} tray ${link.partner.tray}`}>
                          ∞{link.index + 1}
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
              <button className="mt-1.5 flex items-center justify-center gap-1 rounded border border-dashed border-slate-600 py-1 text-[10px] text-slate-400 hover:border-slate-400 hover:text-slate-200"
                onClick={() => update((l) => { l.lanes[j].push({ color: paint }); })}>
                <Plus size={10} /> tray <Swatch color={paint} size={10} />
              </button>
            </div>
          ))}
          {level.lanes.length < 4 && (
            <button className="flex h-24 w-16 shrink-0 flex-col items-center justify-center gap-1 rounded-lg border border-dashed border-slate-500 text-[10px] text-slate-300 hover:border-slate-300"
              onClick={() => update((l) => { l.lanes.push([{ color: paint }]); })}>
              <Plus size={14} /> lane
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
