import { ArrowDown, ArrowUp, EyeOff, PaintBucket, Plus, Trash2, Wand2, X } from 'lucide-react';
import clsx from 'clsx';
import type { ConveyorFile, LevelFile } from '../lib/types';
import { FEEDER_SIDES } from '../lib/types';
import { rowsOf, shiftRowMarks } from '../lib/levelOps';
import { NumberField, Section, Swatch } from './ui';

interface Props {
  level: LevelFile;
  conveyor: ConveyorFile | undefined;
  brush: number;
  update: (fn: (l: LevelFile) => void) => void;
  onAutoFill: () => void;
}

const SIDE_TEXT = ['text-orange-400', 'text-sky-400', 'text-lime-400'];

export function FeedersPanel({ level, conveyor, brush, update, onAutoFill }: Props) {
  const width = conveyor?.width ?? 4;
  const max = Math.min(3, conveyor?.feeders.length ?? 3);
  const paint = brush === 0 ? 1 : brush; // a queue never carries an empty spot

  return (
    <Section
      title="Feeders (queues)"
      actions={
        <>
          <button className="btn" title="Rebuild every queue from the lanes (balanced; clears initial)" onClick={onAutoFill}><Wand2 size={12} /> From lanes</button>
          <button className="btn" disabled={level.feeders.length >= max} onClick={() => update((l) => { l.feeders.push({ bottles: Array(width).fill(paint) }); })}><Plus size={12} /> Feeder</button>
        </>
      }
    >
      <p className="mb-2 text-[11px] leading-snug text-slate-500">
        Row 0 joins first. One row = {width} bottles (conveyor width). Click a bottle to paint it with the brush.
      </p>
      <div className="space-y-3">
        {level.feeders.map((fd, f) => {
          const rows = rowsOf(fd.bottles, width);
          return (
            <div key={f} className="rounded-md border border-slate-800 bg-slate-950/50">
              <div className="flex items-center justify-between border-b border-slate-800 px-2 py-1.5">
                <span className={clsx('text-xs font-semibold capitalize', SIDE_TEXT[f])}>{FEEDER_SIDES[f]} <span className="font-normal text-slate-500">· {fd.bottles.length} bottles · {rows} rows</span></span>
                <button className="btn-icon" title="Remove feeder" onClick={() => update((l) => { l.feeders.splice(f, 1); })}><X size={13} /></button>
              </div>
              <div className="max-h-[340px] overflow-y-auto px-1 py-1">
                {Array.from({ length: rows }, (_, r) => {
                  const hidden = fd.hiddenRows?.includes(r) ?? false;
                  const lock = fd.lockedRows?.find((l) => l.row === r);
                  return (
                    <div key={r} className="group flex items-center gap-1 rounded px-1 py-0.5 hover:bg-slate-800/60">
                      <span className="w-5 text-right font-mono text-[10px] text-slate-500">{r}</span>
                      <div className="flex gap-1">
                        {Array.from({ length: width }, (_, k) => {
                          const i = r * width + k;
                          return i < fd.bottles.length ? (
                            <Swatch key={k} color={fd.bottles[i]} hidden={hidden} size={16}
                              onClick={() => update((l) => { l.feeders[f].bottles[i] = paint; })} />
                          ) : (
                            <button key={k} className="h-4 w-4 rounded-full border border-dashed border-slate-600" title="Add bottle"
                              onClick={() => update((l) => { const b = l.feeders[f].bottles; while (b.length <= i) b.push(paint); })} />
                          );
                        })}
                      </div>
                      <div className="ml-auto flex items-center gap-0.5">
                        <button className="btn-icon" title="Fill row with brush" onClick={() => update((l) => { const b = l.feeders[f].bottles; for (let k = 0; k < width; k++) if (r * width + k < b.length) b[r * width + k] = paint; })}><PaintBucket size={12} /></button>
                        <button className={clsx('btn-icon', hidden && 'bg-slate-600 text-white')} title="Hidden row (R23): grey until it joins the loop"
                          onClick={() => update((l) => { const fe = l.feeders[f]; const s = new Set(fe.hiddenRows ?? []); s.has(r) ? s.delete(r) : s.add(r); fe.hiddenRows = s.size ? [...s].sort((a, b) => a - b) : undefined; })}>
                          <EyeOff size={12} />
                        </button>
                        <NumberField className="!w-11 !px-1 !py-0 text-[11px]" placeholder="🔒" value={lock?.lockTurns} min={0} max={99}
                          onChange={(v) => update((l) => {
                            const fe = l.feeders[f];
                            const rest = (fe.lockedRows ?? []).filter((x) => x.row !== r);
                            if (v > 0) rest.push({ row: r, lockTurns: v });
                            fe.lockedRows = rest.length ? rest.sort((a, b) => a.row - b.row) : undefined;
                          })} />
                        <button className="btn-icon" title="Move row up" disabled={r === 0} onClick={() => update((l) => swapRows(l, f, r, r - 1, width))}><ArrowUp size={12} /></button>
                        <button className="btn-icon" title="Move row down" disabled={r >= rows - 1} onClick={() => update((l) => swapRows(l, f, r, r + 1, width))}><ArrowDown size={12} /></button>
                        <button className="btn-icon" title="Insert row above" onClick={() => update((l) => { const fe = l.feeders[f]; fe.bottles.splice(r * width, 0, ...Array(width).fill(paint)); shiftRowMarks(fe, r, 1); })}><Plus size={12} /></button>
                        <button className="btn-icon hover:!bg-rose-700" title="Delete row" onClick={() => update((l) => { const fe = l.feeders[f]; fe.bottles.splice(r * width, width); shiftRowMarks(fe, r, -1); })}><Trash2 size={12} /></button>
                      </div>
                    </div>
                  );
                })}
              </div>
              <div className="border-t border-slate-800 p-1.5">
                <button className="btn w-full justify-center" onClick={() => update((l) => { l.feeders[f].bottles.push(...Array(width).fill(paint)); })}>
                  <Plus size={12} /> Row of <Swatch color={paint} size={12} />
                </button>
              </div>
            </div>
          );
        })}
        {level.feeders.length === 0 && <p className="text-xs text-slate-500">No feeder: every bottle must start on the belt (initial).</p>}
      </div>
    </Section>
  );
}

function swapRows(l: LevelFile, f: number, a: number, b: number, width: number) {
  const fe = l.feeders[f];
  const bottles = fe.bottles;
  const ra = bottles.slice(a * width, a * width + width), rb = bottles.slice(b * width, b * width + width);
  if (ra.length !== width || rb.length !== width) return;
  bottles.splice(a * width, width, ...rb);
  bottles.splice(b * width, width, ...ra);
  const swap = (r: number) => (r === a ? b : r === b ? a : r);
  if (fe.hiddenRows) fe.hiddenRows = fe.hiddenRows.map(swap).sort((x, y) => x - y);
  if (fe.lockedRows) fe.lockedRows = fe.lockedRows.map((x) => ({ ...x, row: swap(x.row) })).sort((x, y) => x.row - y.row);
}
