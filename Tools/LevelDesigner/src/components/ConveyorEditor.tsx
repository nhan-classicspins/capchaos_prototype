import { useMemo, useState } from 'react';
import { Plus, Trash2, Crosshair, CornerDownRight, ArrowUpToLine } from 'lucide-react';
import clsx from 'clsx';
import type { ConveyorFile, ConveyorNode, LevelFile } from '../lib/types';
import { FEEDER_SIDES } from '../lib/types';
import { Belt, headingOf } from '../lib/spline';
import { round3 } from '../lib/format';
import { ConveyorCanvas, type BeltKey, type KnotSel } from './ConveyorCanvas';
import { Field, NumberField, Section, Segmented, TextField, Toggle } from './ui';
import { ProblemsList } from './ProblemsList';
import type { Problem } from '../lib/validate';

interface Props {
  conveyor: ConveyorFile;
  onChange: (c: ConveyorFile) => void;
  levels: LevelFile[];
  problems: Problem[];
  onOpenLevel: (id: string) => void;
}

const SIDE_COLORS = ['text-orange-400', 'text-sky-400', 'text-lime-400'];
const beltName = (b: BeltKey) => (b === 'loop' ? 'Loop' : `Feeder ${FEEDER_SIDES[b]}`);

export function ConveyorEditor({ conveyor, onChange, levels, problems, onOpenLevel }: Props) {
  const [sel, setSel] = useState<KnotSel | null>(null);
  const [pickFeeder, setPickFeeder] = useState<number | null>(null);
  const [snap, setSnap] = useState(0);
  const [previewId, setPreviewId] = useState<string>('');
  const users = useMemo(() => levels.filter((l) => l.conveyor === conveyor.id), [levels, conveyor.id]);
  const preview = users.find((l) => l.id === previewId);

  const set = (patch: Partial<ConveyorFile>) => onChange({ ...conveyor, ...patch });
  const nodesOf = (c: ConveyorFile, b: BeltKey) => (b === 'loop' ? c.loop.nodes : c.feeders[b].nodes);
  const edit = (fn: (c: ConveyorFile) => void) => { const c = structuredClone(conveyor); fn(c); onChange(c); };
  const node = sel ? nodesOf(conveyor, sel.belt)[sel.index] : undefined;
  const patchNode = (p: Partial<ConveyorNode>) => sel && edit((c) => { const n = nodesOf(c, sel.belt); n[sel.index] = { ...n[sel.index], ...p }; });

  const insertAfter = () => {
    if (!sel) return;
    const closed = sel.belt === 'loop';
    const nodes = nodesOf(conveyor, sel.belt);
    const belt = new Belt(nodes, closed);
    const i = sel.index;
    const last = !closed && i === nodes.length - 1;
    let n: ConveyorNode;
    if (last) {
      const { p, t } = belt.sample(belt.length + 0.6);
      n = { x: round3(p.x), z: round3(p.z), yRotation: round3(headingOf(t)) };
    } else {
      const from = belt.knotAt[i], to = i + 1 < belt.knotAt.length ? belt.knotAt[i + 1] : belt.length;
      const { p, t } = belt.sample((from + to) / 2);
      n = { x: round3(p.x), z: round3(p.z), yRotation: round3(headingOf(t)) };
    }
    edit((c) => nodesOf(c, sel.belt).splice(i + 1, 0, n));
    setSel({ belt: sel.belt, index: i + 1 });
  };

  const remove = () => {
    if (!sel) return;
    edit((c) => nodesOf(c, sel.belt).splice(sel.index, 1));
    setSel(null);
  };

  const makeFirst = () => {
    if (!sel || sel.belt !== 'loop') return;
    edit((c) => { const n = c.loop.nodes; c.loop.nodes = [...n.slice(sel.index), ...n.slice(0, sel.index)]; });
    setSel({ belt: 'loop', index: 0 });
  };

  return (
    <div className="flex min-h-0 flex-1">
      {/* settings */}
      <aside className="w-72 shrink-0 overflow-y-auto border-r border-slate-800 bg-slate-900/70">
        <Section title="Conveyor">
          <div className="grid grid-cols-2 gap-2">
            <div className="col-span-2"><Field label="id (file name)"><input className="input font-mono opacity-70" value={conveyor.id} readOnly /></Field></div>
            <div className="col-span-2"><Field label="name"><TextField value={conveyor.meta?.name ?? ''} onChange={(v) => set({ meta: { ...conveyor.meta, name: v || undefined } })} /></Field></div>
            <Field label="rows" hint="Rows of belt round the loop (8..64)"><NumberField value={conveyor.rows} min={8} max={64} onChange={(v) => set({ rows: v })} /></Field>
            <Field label="width" hint="Bottle spots per row (1..6)"><NumberField value={conveyor.width} min={1} max={6} onChange={(v) => set({ width: v })} /></Field>
            <Field label="pickRows" hint="Rows of the pick zone; rows ≥ 2 × pickRows + 6"><NumberField value={conveyor.pickRows} min={1} max={29} onChange={(v) => set({ pickRows: v })} /></Field>
            <Field label="scale" hint="Drawing size of belts and bottles (0.1..4)"><NumberField value={conveyor.scale} min={0.1} max={4} step={0.01} integer={false} onChange={(v) => set({ scale: round3(v) })} /></Field>
            <div className="col-span-2"><Field label="notes"><TextField multiline value={conveyor.meta?.notes ?? ''} onChange={(v) => set({ meta: { ...conveyor.meta, notes: v || undefined } })} /></Field></div>
          </div>
        </Section>
        <Section title="Feeders · mergeAt">
          <div className="space-y-2">
            {conveyor.feeders.map((f, i) => (
              <div key={f.side} className="flex items-center gap-2">
                <span className={clsx('w-14 text-xs font-semibold capitalize', SIDE_COLORS[i])}>{f.side}</span>
                <NumberField className="w-16" value={f.mergeAt} min={0} max={conveyor.rows - 1} onChange={(v) => edit((c) => { c.feeders[i].mergeAt = v; })} />
                <button className={clsx('btn', pickFeeder === i && 'btn-primary')} title="Click a row on the loop to set mergeAt" onClick={() => setPickFeeder(pickFeeder === i ? null : i)}>
                  <Crosshair size={12} /> pick
                </button>
                <span className="text-[11px] text-slate-500">{f.nodes.length} knots</span>
              </div>
            ))}
          </div>
          <p className="mt-2 text-[11px] leading-snug text-slate-500">Outside the pick zone (0..{conveyor.pickRows - 1}), one feeder per row. Levels use the first N: right → left → middle.</p>
        </Section>
        <Section title={`Used by ${users.length} level${users.length === 1 ? '' : 's'}`}>
          <div className="mb-2"><Field label="preview bottles of">
            <select className="input" value={previewId} onChange={(e) => setPreviewId(e.target.value)}>
              <option value="">— none —</option>
              {users.map((l) => <option key={l.id} value={l.id}>{l.id} {l.meta?.name ? `· ${l.meta.name}` : ''}</option>)}
            </select>
          </Field></div>
          <div className="flex flex-wrap gap-1">
            {users.map((l) => <button key={l.id} className="btn font-mono" onClick={() => onOpenLevel(l.id)}>{l.id.slice(6)}</button>)}
          </div>
        </Section>
        <Section title="Problems"><ProblemsList problems={problems} /></Section>
      </aside>

      {/* canvas */}
      <main className="relative min-w-0 flex-1">
        <ConveyorCanvas
          conveyor={conveyor}
          level={preview}
          editable
          selected={sel}
          onSelect={setSel}
          onChange={onChange}
          snap={snap}
          onPickRow={pickFeeder != null ? (row) => { edit((c) => { c.feeders[pickFeeder].mergeAt = row; }); setPickFeeder(null); } : undefined}
          pickRowHint={pickFeeder != null ? `Click a loop row → ${FEEDER_SIDES[pickFeeder]} feeder mergeAt` : undefined}
        />
      </main>

      {/* knots */}
      <aside className="w-80 shrink-0 overflow-y-auto border-l border-slate-800 bg-slate-900/70">
        <Section title="Knot" actions={<Segmented value={snap} onChange={setSnap} options={[{ value: 0, label: 'free' }, { value: 0.05, label: '.05' }, { value: 0.1, label: '.1' }]} />}>
          {node && sel ? (
            <div className="space-y-2">
              <div className="text-xs text-slate-300">{beltName(sel.belt)} · node {sel.index}{sel.belt === 'loop' && sel.index === 0 && <span className="ml-1 text-cyan-300">(track position 0)</span>}</div>
              <div className="grid grid-cols-3 gap-2">
                <Field label="x"><NumberField integer={false} step={0.05} value={node.x} onChange={(v) => patchNode({ x: round3(v) })} /></Field>
                <Field label="z"><NumberField integer={false} step={0.05} value={node.z} onChange={(v) => patchNode({ z: round3(v) })} /></Field>
                <Field label="yRotation"><NumberField integer={false} step={15} value={node.yRotation ?? 0} onChange={(v) => patchNode({ yRotation: round3(((v % 360) + 360) % 360) })} /></Field>
              </div>
              <div className="flex flex-wrap gap-1">
                {[0, 90, 180, 270].map((d) => <button key={d} className="btn font-mono" onClick={() => patchNode({ yRotation: d })}>{d}°</button>)}
              </div>
              <Toggle label="Sharp corner (tangentMode 1)" checked={node.tangentMode === 1} onChange={(v) => patchNode({ tangentMode: v ? 1 : undefined })} />
              <div className="flex flex-wrap gap-1 pt-1">
                <button className="btn" onClick={insertAfter}><Plus size={12} /> Insert after</button>
                {sel.belt === 'loop' && sel.index !== 0 && <button className="btn" onClick={makeFirst} title="Rotate the loop so this knot is track position 0 (where the pick zone starts)"><ArrowUpToLine size={12} /> Make node 0</button>}
                <button className="btn btn-danger" onClick={remove}><Trash2 size={12} /> Delete</button>
              </div>
            </div>
          ) : (
            <p className="text-xs text-slate-500">Click a knot on the canvas. Drag it to move; drag its round handle to turn its heading. Shift snaps (0.05 u / 15°).</p>
          )}
        </Section>
        {(['loop', 0, 1, 2] as BeltKey[]).map((b) => {
          const nodes = nodesOf(conveyor, b);
          return (
            <Section key={String(b)} title={beltName(b)}>
              <div className="space-y-0.5 font-mono text-[11px]">
                {nodes.map((n, i) => (
                  <button key={i} onClick={() => setSel({ belt: b, index: i })}
                    className={clsx('flex w-full items-center gap-2 rounded px-1.5 py-0.5 text-left hover:bg-slate-800', sel?.belt === b && sel.index === i && 'bg-sky-900/60 text-white')}>
                    <span className="w-5 text-slate-500">{i}</span>
                    <span className="w-14">x {n.x}</span><span className="w-14">z {n.z}</span><span>{n.yRotation ?? 0}°</span>
                    {n.tangentMode === 1 && <CornerDownRight size={11} className="text-amber-400" />}
                  </button>
                ))}
              </div>
            </Section>
          );
        })}
      </aside>
    </div>
  );
}
