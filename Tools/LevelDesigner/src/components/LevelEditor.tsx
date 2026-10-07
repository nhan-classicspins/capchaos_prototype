import { useEffect, useMemo, useState } from 'react';
import { Eraser, Lock, Wand2 } from 'lucide-react';
import clsx from 'clsx';
import type { ConveyorFile, LevelFile, TrayRef } from '../lib/types';
import { CAMERA_PRESETS, DIFFICULTIES } from '../lib/types';
import { FLAVORS, flavor } from '../lib/colors';
import { clone, feedersFromLanes, removeTray, setLink } from '../lib/levelOps';
import { colorBalance, type Problem } from '../lib/validate';
import { ConveyorCanvas, type SpotHit } from './ConveyorCanvas';
import { LanesBoard } from './LanesBoard';
import { FeedersPanel } from './FeedersPanel';
import { TrayInspector } from './TrayInspector';
import { ProblemsList } from './ProblemsList';
import { Field, NumberField, Section, Swatch, TextField, Toggle } from './ui';

interface Props {
  level: LevelFile;
  conveyors: ConveyorFile[];
  onChange: (l: LevelFile) => void;
  problems: Problem[];
  onOpenConveyor: (id: string) => void;
}

/** Fields that change how the level plays: editing any of them makes a recorded meta.solution stale. */
export function LevelEditor({ level, conveyors, onChange, problems, onOpenConveyor }: Props) {
  const conveyor = conveyors.find((c) => c.id === level.conveyor);
  const [brush, setBrush] = useState<number>(level.colors[0] ?? 1);
  const [sel, setSel] = useState<TrayRef | null>(null);
  const [linking, setLinking] = useState(false);

  useEffect(() => { setSel(null); setLinking(false); }, [level.id]);

  /** Gameplay edit: drops a now-stale recorded solution. */
  const setLevel = (l: LevelFile) => {
    if (l.meta?.solution) l = { ...l, meta: { ...l.meta, solution: undefined } };
    onChange(l);
  };
  const update = (fn: (l: LevelFile) => void) => { const l = clone(level); fn(l); setLevel(l); };
  const updateMeta = (fn: (l: LevelFile) => void) => { const l = clone(level); fn(l); onChange(l); };

  const selectTray = (t: TrayRef | null) => {
    if (t && linking && sel && t.lane !== sel.lane) {
      setLevel(setLink(level, sel, t));
      setLinking(false);
      return;
    }
    setLinking(false);
    setSel(t);
  };

  // keyboard: 1..8 recolour, Del remove, Esc deselect
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || e.metaKey || e.ctrlKey) return;
      if (e.key === 'Escape') { setSel(null); setLinking(false); return; }
      if (!sel || !level.lanes[sel.lane]?.[sel.tray]) return;
      if ((e.key === 'Delete' || e.key === 'Backspace') && level.lanes.flat().length > 1) { setLevel(removeTray(level, sel)); setSel(null); return; }
      const n = Number(e.key);
      if (n >= 1 && n <= 8) update((l) => { l.lanes[sel.lane][sel.tray].color = n; });
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  const balance = useMemo(() => colorBalance(level), [level]);
  const errorTrays = useMemo(() => new Set(problems.filter((p) => p.tray).map((p) => `${p.tray!.lane}:${p.tray!.tray}`)), [problems]);
  const used = new Set(balance.filter((b) => b.bottles + b.places > 0).map((b) => b.color));

  const onSpot = (hit: SpotHit) => {
    if (hit.kind === 'initial') update((l) => { l.initial![hit.row][hit.track] = brush; });
    else if (brush !== 0) update((l) => { l.feeders[hit.feeder].bottles[hit.index] = brush; });
  };

  const setConveyor = (id: string) => {
    const c = conveyors.find((x) => x.id === id);
    update((l) => {
      l.conveyor = id;
      if (l.initial && c) l.initial = Array.from({ length: c.rows }, (_, r) => Array.from({ length: c.width }, (_, k) => l.initial![r]?.[k] ?? 0));
      if (c && l.feeders.length > c.feeders.length) l.feeders.length = c.feeders.length;
    });
  };

  const slotLock = (s: number) => level.slotLocks?.find((x) => x.slot === s)?.lockTurns ?? 0;

  return (
    <div className="flex min-h-0 flex-1">
      {/* ── left: settings ── */}
      <aside className="w-72 shrink-0 overflow-y-auto border-r border-slate-800 bg-slate-900/70">
        <Section title="Level">
          <div className="grid grid-cols-2 gap-2">
            <Field label="id (file name)"><input className="input font-mono opacity-70" value={level.id} readOnly /></Field>
            <Field label="difficulty">
              <select className="input" value={level.meta?.difficulty ?? ''} onChange={(e) => updateMeta((l) => { l.meta = { ...l.meta, difficulty: (e.target.value || undefined) as any }; })}>
                <option value="">—</option>
                {DIFFICULTIES.map((d) => <option key={d}>{d}</option>)}
              </select>
            </Field>
            <div className="col-span-2"><Field label="name"><TextField value={level.meta?.name ?? ''} onChange={(v) => updateMeta((l) => { l.meta = { ...l.meta, name: v || undefined }; })} /></Field></div>
            <div className="col-span-2">
              <Field label="conveyor">
                <div className="flex gap-1">
                  <select className="input font-mono" value={level.conveyor} onChange={(e) => setConveyor(e.target.value)}>
                    {!conveyor && <option value={level.conveyor}>{level.conveyor} (missing)</option>}
                    {conveyors.map((c) => <option key={c.id} value={c.id}>{c.id}</option>)}
                  </select>
                  {conveyor && <button className="btn" onClick={() => onOpenConveyor(conveyor.id)}>Edit</button>}
                </div>
              </Field>
            </div>
            <Field label="slots (open)"><NumberField value={level.slots} min={1} max={6} onChange={(v) => update((l) => { l.slots = v; if (l.slotLocks) { l.slotLocks = l.slotLocks.filter((x) => x.slot < v); if (!l.slotLocks.length) delete l.slotLocks; } })} /></Field>
            <Field label="extraSlots (R20)"><NumberField value={level.extraSlots} min={0} max={5} onChange={(v) => update((l) => { l.extraSlots = v; })} /></Field>
            <Field label="trayCapacity"><NumberField value={level.trayCapacity} min={2} max={6} onChange={(v) => update((l) => { l.trayCapacity = v; })} /></Field>
            <Field label="camera">
              <select className="input" value={level.view?.cameraPreset ?? 'default'} onChange={(e) => updateMeta((l) => { l.view = { cameraPreset: e.target.value }; })}>
                {CAMERA_PRESETS.map((p) => <option key={p}>{p}</option>)}
              </select>
            </Field>
            <div className="col-span-2"><Field label="notes"><TextField multiline value={level.meta?.notes ?? ''} onChange={(v) => updateMeta((l) => { l.meta = { ...l.meta, notes: v || undefined }; })} /></Field></div>
          </div>
        </Section>

        <Section title="Slot locks (R22)">
          <div className="flex flex-wrap gap-2">
            {Array.from({ length: level.slots }, (_, s) => (
              <div key={s} className="flex items-center gap-1">
                <span className="flex items-center gap-0.5 text-[11px] text-slate-400"><Lock size={10} />{s}</span>
                <NumberField className="!w-12 !px-1 !py-0.5 text-xs" value={slotLock(s)} min={0} max={99}
                  onChange={(v) => update((l) => {
                    const rest = (l.slotLocks ?? []).filter((x) => x.slot !== s);
                    if (v > 0) rest.push({ slot: s, lockTurns: v });
                    l.slotLocks = rest.length ? rest.sort((a, b) => a.slot - b.slot) : undefined;
                  })} />
              </div>
            ))}
          </div>
        </Section>

        <Section title="Colours" actions={<button className="btn" title="colors = every colour the level uses" onClick={() => update((l) => { l.colors = [...used].sort((a, b) => a - b); })}><Wand2 size={12} /> From use</button>}>
          <div className="flex flex-wrap gap-1.5">
            {FLAVORS.map((f) => (
              <Swatch key={f.id} color={f.id} size={22} dim={!level.colors.includes(f.id)}
                title={`${f.id} · ${f.name} — click to ${level.colors.includes(f.id) ? 'remove from' : 'add to'} colors`}
                onClick={() => update((l) => { l.colors = l.colors.includes(f.id) ? l.colors.filter((c) => c !== f.id) : [...l.colors, f.id].sort((a, b) => a - b); })} />
            ))}
          </div>
        </Section>

        <Section title="Balance (V4: bottles = tray places)">
          <table className="w-full text-[11px]">
            <thead className="text-slate-500"><tr><th className="text-left font-normal">colour</th><th className="text-right font-normal">bottles</th><th className="text-right font-normal">places</th><th className="text-right font-normal">Δ</th></tr></thead>
            <tbody>
              {balance.map((b) => {
                const d = b.bottles - b.places;
                return (
                  <tr key={b.color} className={clsx(d !== 0 || !b.declared ? 'text-rose-300' : 'text-slate-300')}>
                    <td className="flex items-center gap-1.5 py-0.5"><Swatch color={b.color} size={11} /> {flavor(b.color).name}{!b.declared && ' *'}</td>
                    <td className="text-right font-mono">{b.bottles}</td>
                    <td className="text-right font-mono">{b.places}</td>
                    <td className="text-right font-mono">{d > 0 ? `+${d}` : d}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <div className="mt-2 text-[11px] text-slate-500">
            {level.lanes.reduce((n, l) => n + l.length, 0)} trays · {balance.reduce((n, b) => n + b.bottles, 0)} bottles
          </div>
        </Section>

        <Section title="Solution (meta.solution)">
          {level.meta?.solution ? (
            <div className="space-y-1.5">
              <div className="break-all font-mono text-[11px] text-slate-300">{level.meta.solution.join(' ')}</div>
              <button className="btn" onClick={() => updateMeta((l) => { if (l.meta) delete l.meta.solution; })}><Eraser size={12} /> Clear (solver searches)</button>
            </div>
          ) : (
            <p className="text-[11px] text-slate-500">None recorded — LevelTool validate searches for one (V6). Any gameplay edit clears a recorded solution.</p>
          )}
        </Section>

        <Section title="Problems"><ProblemsList problems={problems} onTray={(t) => setSel(t)} /></Section>
      </aside>

      {/* ── centre: conveyor + board ── */}
      <main className="flex min-w-0 flex-1 flex-col">
        <div className="relative min-h-0 flex-[1.15]">
          {conveyor ? (
            <ConveyorCanvas conveyor={conveyor} level={level} onSpot={onSpot} showRowNumbers />
          ) : (
            <div className="flex h-full items-center justify-center bg-board-ground text-sm text-slate-200">Conveyor “{level.conveyor}” not found in ConveyorConfig/</div>
          )}
        </div>
        <div className="min-h-0 flex-1 border-t border-slate-900">
          <LanesBoard level={level} selected={sel} linking={linking} brush={brush} onSelect={selectTray} setLevel={setLevel} update={update} errorTrays={errorTrays} />
        </div>
      </main>

      {/* ── right: brush, tray, feeders ── */}
      <aside className="w-[360px] shrink-0 overflow-y-auto border-l border-slate-800 bg-slate-900/70">
        <Section title="Brush">
          <div className="flex flex-wrap items-center gap-1.5">
            {FLAVORS.map((f) => (
              <Swatch key={f.id} color={f.id} size={22} selected={brush === f.id} dim={!level.colors.includes(f.id) && brush !== f.id} onClick={() => setBrush(f.id)} />
            ))}
            {level.initial && <Swatch color={0} size={22} selected={brush === 0} onClick={() => setBrush(0)} title="0 · empty belt spot (initial only)" />}
          </div>
          <p className="mt-2 text-[11px] text-slate-500">Paints feeder bottles (here or on the canvas), initial belt spots and new trays.</p>
        </Section>

        {sel && <TrayInspector level={level} at={sel} linking={linking} setLinking={setLinking} setLevel={setLevel} update={update} select={setSel} />}

        <FeedersPanel level={level} conveyor={conveyor} brush={brush} update={update}
          onAutoFill={() => conveyor && confirm('Rebuild every feeder queue from the lanes? Current queues and initial belt are replaced.') && setLevel(feedersFromLanes(level, conveyor, Math.max(1, level.feeders.length)))} />

        <Section title="Initial belt">
          <Toggle label={`Belt starts filled (initial: ${conveyor?.rows ?? '?'} rows × ${conveyor?.width ?? '?'})`} checked={!!level.initial}
            onChange={(v) => conveyor && update((l) => { l.initial = v ? Array.from({ length: conveyor.rows }, () => Array(conveyor.width).fill(0)) : undefined; })} />
          {level.initial && <p className="mt-1.5 text-[11px] text-slate-500">Paint the spots on the loop in the canvas; the empty brush (dashed) clears a spot.</p>}
        </Section>
      </aside>
    </div>
  );
}
