import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Copy, Link2, Trash2, Unlink } from 'lucide-react';
import type { LevelFile, TrayRef } from '../lib/types';
import { FLAVORS } from '../lib/colors';
import { insertTray, linkOf, moveTray, removeTray, unlink } from '../lib/levelOps';
import { NumberField, Section, Segmented, Swatch, Toggle } from './ui';

interface Props {
  level: LevelFile;
  at: TrayRef;
  linking: boolean;
  setLinking: (v: boolean) => void;
  setLevel: (l: LevelFile) => void;
  update: (fn: (l: LevelFile) => void) => void;
  select: (t: TrayRef | null) => void;
}

export function TrayInspector({ level, at, linking, setLinking, setLevel, update, select }: Props) {
  const tray = level.lanes[at.lane]?.[at.tray];
  if (!tray) return null;
  const link = linkOf(level, at);
  const lane = level.lanes[at.lane];
  const move = (to: TrayRef) => { setLevel(moveTray(level, at, to)); select(to); };

  return (
    <Section title={`Tray · lane ${at.lane} · #${at.tray}${at.tray === 0 ? ' (front)' : ''}`}>
      <div className="space-y-2.5">
        <div className="flex flex-wrap gap-1.5">
          {FLAVORS.map((f) => (
            <Swatch key={f.id} color={f.id} size={20} selected={tray.color === f.id} dim={!level.colors.includes(f.id)}
              title={`${f.id} · ${f.name}${level.colors.includes(f.id) ? '' : ' (not in colors)'} — key ${f.id}`}
              onClick={() => update((l) => { l.lanes[at.lane][at.tray].color = f.id; })} />
          ))}
        </div>
        <div className="flex items-center justify-between">
          <span className="text-[11px] text-slate-400">size (R21)</span>
          <Segmented value={tray.size ?? 1} onChange={(v) => update((l) => { const t = l.lanes[at.lane][at.tray]; if (v === 1) delete t.size; else t.size = v; })}
            options={[1, 2, 3, 4].map((v) => ({ value: v, label: ['S', 'M', 'L', 'XL'][v - 1], title: `${v * level.trayCapacity} bottles` }))} />
        </div>
        <Toggle label="Hidden (R17): '?' until it reaches the front" checked={!!tray.hidden}
          onChange={(v) => update((l) => { const t = l.lanes[at.lane][at.tray]; if (v) t.hidden = true; else delete t.hidden; })} />
        <div className="flex items-center justify-between gap-2">
          <span className="text-[11px] text-slate-400">lockTurns (R18) · 0 = none</span>
          <NumberField className="!w-16" value={tray.lockTurns ?? 0} min={0} max={99}
            onChange={(v) => update((l) => { const t = l.lanes[at.lane][at.tray]; if (v > 0) t.lockTurns = v; else delete t.lockTurns; })} />
        </div>
        <div className="flex items-center justify-between gap-2 rounded border border-slate-800 bg-slate-950/50 px-2 py-1.5">
          <span className="text-[11px] text-slate-300">
            {link ? <>Linked (R19) ↔ lane {link.partner.lane} #{link.partner.tray}</> : linking ? <span className="text-amber-300">Click a tray on another lane…</span> : 'Not linked'}
          </span>
          {link ? (
            <button className="btn" onClick={() => setLevel(unlink(level, at))}><Unlink size={12} /> Unlink</button>
          ) : (
            <button className={linking ? 'btn btn-primary' : 'btn'} onClick={() => setLinking(!linking)}><Link2 size={12} /> {linking ? 'Cancel' : 'Link…'}</button>
          )}
        </div>
        <div className="flex flex-wrap gap-1">
          <button className="btn" title="Toward the front" disabled={at.tray === 0} onClick={() => move({ lane: at.lane, tray: at.tray - 1 })}><ArrowUp size={12} /></button>
          <button className="btn" title="Away from the front" disabled={at.tray >= lane.length - 1} onClick={() => move({ lane: at.lane, tray: at.tray + 1 })}><ArrowDown size={12} /></button>
          <button className="btn" title="To the lane on the left" disabled={at.lane === 0} onClick={() => move({ lane: at.lane - 1, tray: Math.min(at.tray, level.lanes[at.lane - 1].length) })}><ArrowLeft size={12} /></button>
          <button className="btn" title="To the lane on the right" disabled={at.lane >= level.lanes.length - 1} onClick={() => move({ lane: at.lane + 1, tray: Math.min(at.tray, level.lanes[at.lane + 1].length) })}><ArrowRight size={12} /></button>
          <button className="btn" title="Duplicate behind" onClick={() => { const { lockTurns: _l, ...copy } = tray; setLevel(insertTray(level, at.lane, at.tray + 1, { ...copy })); select({ lane: at.lane, tray: at.tray + 1 }); }}><Copy size={12} /></button>
          <button className="btn btn-danger" title="Delete (Del)" disabled={lane.length <= 1 && level.lanes.length <= 1} onClick={() => { setLevel(removeTray(level, at)); select(null); }}><Trash2 size={12} /></button>
        </div>
        <p className="text-[10px] text-slate-500">Keys: 1–8 colour · Del delete · Esc deselect</p>
      </div>
    </Section>
  );
}
