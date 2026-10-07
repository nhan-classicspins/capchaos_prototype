import { useEffect, useLayoutEffect, useMemo, useRef, useState, type PointerEvent as RPointerEvent, type ReactElement, type WheelEvent } from 'react';
import { Maximize2 } from 'lucide-react';
import type { ConveyorFile, ConveyorNode, LevelFile } from '../lib/types';
import { Belt, headingOf, outward, type P } from '../lib/spline';
import { flavor, ITEM_HIDDEN, DEBUG } from '../lib/colors';
import { round3 } from '../lib/format';

/** GameFeel defaults (Assets/CapsChaos/Views/GameFeel.cs) at scale 1, board units. */
const TRACK_SPACING = 0.42;
const FEEDER_ROW_PITCH = 0.44;
const HEAD_PROBE_STEP = 0.02;

export type BeltKey = 'loop' | 0 | 1 | 2;
export interface KnotSel { belt: BeltKey; index: number }

export type SpotHit =
  | { kind: 'initial'; row: number; track: number }
  | { kind: 'feeder'; feeder: number; index: number };

interface Props {
  conveyor: ConveyorFile;
  level?: LevelFile;
  /** Knots can be dragged / rotated. */
  editable?: boolean;
  selected?: KnotSel | null;
  onSelect?: (sel: KnotSel | null) => void;
  onChange?: (c: ConveyorFile) => void;
  /** Click a loop row (e.g. to set a feeder's mergeAt). */
  onPickRow?: (row: number) => void;
  pickRowHint?: string;
  onSpot?: (hit: SpotHit) => void;
  showRowNumbers?: boolean;
  snap?: number;
}

const SIDE_COLORS = ['#f97316', '#38bdf8', '#a3e635'];
const SIDE_LETTER = ['R', 'L', 'M'];

export function ConveyorCanvas({ conveyor, level, editable, selected, onSelect, onChange, onPickRow, pickRowHint, onSpot, showRowNumbers = true, snap = 0 }: Props) {
  const wrap = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 800, h: 600 });
  const [view, setView] = useState({ cx: 0, cz: 6, zoom: 60 });
  const drag = useRef<null | { type: 'pan'; x: number; y: number; cx: number; cz: number } | { type: 'knot' | 'rot'; sel: KnotSel; moved: boolean }>(null);

  useLayoutEffect(() => {
    const el = wrap.current!;
    const ro = new ResizeObserver(() => setSize({ w: el.clientWidth, h: el.clientHeight }));
    ro.observe(el);
    setSize({ w: el.clientWidth, h: el.clientHeight });
    return () => ro.disconnect();
  }, []);

  const scale = conveyor.scale || 1;
  const width = conveyor.width;
  const spacing = TRACK_SPACING * scale;
  const across = (k: number) => ((width - 1) * 0.5 - k) * spacing;
  const beltWidth = width * spacing + 0.14 * scale;
  const bottleR = 0.18 * scale;

  const loop = useMemo(() => new Belt(conveyor.loop.nodes, true), [conveyor.loop.nodes]);
  const feeders = useMemo(() => conveyor.feeders.map((f) => new Belt(f.nodes, false)), [conveyor.feeders]);
  const pitch = loop.length / Math.max(1, conveyor.rows);
  const feederCount = level ? level.feeders.length : 3;

  // ── fit ──
  const fit = () => {
    const loopTop = Math.max(...conveyor.loop.nodes.map((n) => n.z));
    // a level frames the loop and the queues' near end; the conveyor editor frames every knot
    const feederPts = conveyor.feeders.slice(0, Math.max(1, feederCount)).flatMap((f) => f.nodes).filter((n) => !level || n.z <= loopTop + 3);
    const pts: P[] = [...conveyor.loop.nodes, ...feederPts];
    if (pts.length === 0) return;
    const xs = pts.map((p) => p.x), zs = pts.map((p) => p.z);
    const minX = Math.min(...xs) - 1, maxX = Math.max(...xs) + 1, minZ = Math.min(...zs) - 1, maxZ = Math.max(...zs) + 1;
    const zoom = Math.min(size.w / (maxX - minX), size.h / (maxZ - minZ));
    setView({ cx: (minX + maxX) / 2, cz: (minZ + maxZ) / 2, zoom: Math.max(10, Math.min(400, zoom)) });
  };
  const fitKey = `${conveyor.id}|${size.w > 0 && size.h > 0}|${feederCount}`;
  useEffect(fit, [fitKey]); // eslint-disable-line react-hooks/exhaustive-deps

  const S = (p: P) => ({ X: (p.x - view.cx) * view.zoom + size.w / 2, Y: -(p.z - view.cz) * view.zoom + size.h / 2 });
  const W = (X: number, Y: number): P => ({ x: (X - size.w / 2) / view.zoom + view.cx, z: -(Y - size.h / 2) / view.zoom + view.cz });
  const pathOf = (belt: Belt, closed: boolean) =>
    belt.points.length < 2 ? '' : belt.points.map((p, i) => { const s = S(p); return `${i ? 'L' : 'M'}${s.X.toFixed(1)} ${s.Y.toFixed(1)}`; }).join('') + (closed ? 'Z' : '');
  const subPath = (belt: Belt, from: number, to: number) => {
    const pts: string[] = [];
    for (let d = from; d <= to + 1e-6; d += Math.max(0.02, (to - from) / 80)) { const s = S(belt.sample(d).p); pts.push(`${pts.length ? 'L' : 'M'}${s.X.toFixed(1)} ${s.Y.toFixed(1)}`); }
    return pts.join('');
  };

  // ── loop spots ──
  const spot = (row: number, track: number) => {
    const { p, t } = loop.sample((row + 0.5) * pitch);
    const o = outward(t);
    return { x: p.x + o.x * across(track), z: p.z + o.z * across(track) };
  };

  // ── feeder queues (LoopBeltView.Finish / LayoutFeeder) ──
  const queues = useMemo(() => {
    if (!level || loop.points.length < 2) return [];
    const clear = 2 * across(0) + spacing;
    return level.feeders.slice(0, conveyor.feeders.length).map((fd, f) => {
      const belt = feeders[f];
      if (!belt || belt.points.length < 2) return null;
      let head = 0;
      for (let s = belt.length; s > 0; s -= HEAD_PROBE_STEP) if (loop.distanceTo(belt.sample(s).p) >= clear) { head = s; break; }
      const ft = belt.sample(belt.length).t;
      const lt = loop.sample((conveyor.feeders[f].mergeAt + 0.5) * pitch).t;
      const of = outward(ft), ol = outward(lt);
      const side = of.x * ol.x + of.z * ol.z < 0 ? -1 : 1;
      const rows = Math.ceil(fd.bottles.length / width);
      const items: { index: number; p: P; color: number; hidden: boolean }[] = [];
      const locks: { row: number; turns: number; p: P }[] = [];
      let lastDrawn = -1;
      for (let r = 0; r < rows; r++) {
        const along = head - r * FEEDER_ROW_PITCH * scale;
        if (along < -FEEDER_ROW_PITCH * scale * 0.5) break;
        lastDrawn = r;
        const { p, t } = belt.sample(along);
        const o = outward(t);
        const hidden = fd.hiddenRows?.includes(r) ?? false;
        for (let k = 0; k < width; k++) {
          const i = r * width + k;
          if (i >= fd.bottles.length) break;
          const a = across(k) * side;
          items.push({ index: i, p: { x: p.x + o.x * a, z: p.z + o.z * a }, color: fd.bottles[i], hidden });
        }
        const lock = fd.lockedRows?.find((l) => l.row === r);
        if (lock) locks.push({ row: r, turns: lock.lockTurns, p: { x: p.x + o.x * (across(0) + spacing), z: p.z + o.z * (across(0) + spacing) } });
      }
      return { f, items, locks, more: rows - 1 - lastDrawn, start: belt.sample(0).p };
    });
  }, [level, loop, feeders, conveyor, pitch, width, scale, spacing]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── interaction ──
  const nodesOf = (c: ConveyorFile, b: BeltKey): ConveyorNode[] => (b === 'loop' ? c.loop.nodes : c.feeders[b].nodes);
  const withNode = (b: BeltKey, i: number, patch: Partial<ConveyorNode>): ConveyorFile => {
    const c = structuredClone(conveyor);
    const nodes = nodesOf(c, b);
    nodes[i] = { ...nodes[i], ...patch };
    return c;
  };
  const local = (e: { clientX: number; clientY: number }) => {
    const r = wrap.current!.getBoundingClientRect();
    return { X: e.clientX - r.left, Y: e.clientY - r.top };
  };

  const onDown = (e: RPointerEvent) => {
    (e.currentTarget as Element).setPointerCapture(e.pointerId);
    const target = (e.target as Element).closest('[data-knot],[data-rot],[data-spot],[data-loop]') as HTMLElement | null;
    if (target?.dataset.spot && onSpot) {
      const [kind, a, b] = target.dataset.spot.split(':');
      onSpot(kind === 'i' ? { kind: 'initial', row: +a, track: +b } : { kind: 'feeder', feeder: +a, index: +b });
      return;
    }
    if (editable && (target?.dataset.knot || target?.dataset.rot)) {
      const [b, i] = (target.dataset.knot ?? target.dataset.rot)!.split(':');
      const sel: KnotSel = { belt: b === 'loop' ? 'loop' : (Number(b) as 0 | 1 | 2), index: Number(i) };
      onSelect?.(sel);
      drag.current = { type: target.dataset.rot ? 'rot' : 'knot', sel, moved: false };
      return;
    }
    if (onPickRow && target?.dataset.loop != null) {
      const { X, Y } = local(e);
      onPickRow(Math.floor(loop.project(W(X, Y)) / pitch) % conveyor.rows);
      return;
    }
    drag.current = { type: 'pan', x: e.clientX, y: e.clientY, cx: view.cx, cz: view.cz };
  };

  const onMove = (e: RPointerEvent) => {
    const d = drag.current;
    if (!d) return;
    if (d.type === 'pan') {
      setView((v) => ({ ...v, cx: d.cx - (e.clientX - d.x) / v.zoom, cz: d.cz + (e.clientY - d.y) / v.zoom }));
      return;
    }
    const { X, Y } = local(e);
    const w = W(X, Y);
    const node = nodesOf(conveyor, d.sel.belt)[d.sel.index];
    if (!node) return;
    d.moved = true;
    if (d.type === 'knot') {
      const g = e.shiftKey ? 0.05 : snap;
      const q = (v: number) => round3(g > 0 ? Math.round(v / g) * g : v);
      onChange?.(withNode(d.sel.belt, d.sel.index, { x: q(w.x), z: q(w.z) }));
    } else {
      let h = headingOf({ x: w.x - node.x, z: w.z - node.z });
      h = e.shiftKey ? (Math.round(h / 15) * 15) % 360 : Math.round(h * 10) / 10;
      onChange?.(withNode(d.sel.belt, d.sel.index, { yRotation: round3(h) }));
    }
  };

  const onUp = () => (drag.current = null);

  const onWheel = (e: WheelEvent) => {
    const { X, Y } = local(e);
    const before = W(X, Y);
    const zoom = Math.max(10, Math.min(500, view.zoom * Math.exp(-e.deltaY * 0.0015)));
    setView({ zoom, cx: before.x - (X - size.w / 2) / zoom, cz: before.z + (Y - size.h / 2) / zoom });
  };

  // ── draw ──
  const px = (u: number) => u * view.zoom;
  const beltStroke = (key: string, d: string, active: boolean) => (
    <g key={key} opacity={active ? 1 : 0.28}>
      <path d={d} fill="none" stroke="#25272E" strokeWidth={px(beltWidth + 0.08 * scale)} strokeLinejoin="round" strokeLinecap="round" />
      <path d={d} fill="none" stroke="#CAD2DF" strokeWidth={px(beltWidth - 0.04 * scale)} strokeLinejoin="round" strokeLinecap="butt" />
    </g>
  );

  const grid = useMemo(() => {
    const lines: ReactElement[] = [];
    const tl = W(0, 0), br = W(size.w, size.h);
    const step = view.zoom > 80 ? 0.5 : 1;
    for (let x = Math.ceil(tl.x / step) * step; x <= br.x; x += step) {
      const X = S({ x, z: 0 }).X;
      lines.push(<line key={'x' + x} x1={X} x2={X} y1={0} y2={size.h} stroke={Math.abs(x) < 1e-6 ? '#7d86b5' : '#5A6090'} strokeWidth={Math.abs(x) < 1e-6 ? 1.2 : 0.5} />);
    }
    for (let z = Math.ceil(br.z / step) * step; z <= tl.z; z += step) {
      const Y = S({ x: 0, z }).Y;
      lines.push(<line key={'z' + z} y1={Y} y2={Y} x1={0} x2={size.w} stroke={Math.abs(z) < 1e-6 ? '#7d86b5' : '#5A6090'} strokeWidth={Math.abs(z) < 1e-6 ? 1.2 : 0.5} />);
    }
    return lines;
  }, [view, size]); // eslint-disable-line react-hooks/exhaustive-deps

  const knotEls = (b: BeltKey, nodes: ConveyorNode[], color: string) =>
    nodes.map((n, i) => {
      const s = S(n);
      const isSel = selected && selected.belt === b && selected.index === i;
      const dir = { x: Math.sin(((n.yRotation ?? 0) * Math.PI) / 180), z: Math.cos(((n.yRotation ?? 0) * Math.PI) / 180) };
      const hl = 40 / view.zoom;
      const h = S({ x: n.x + dir.x * hl, z: n.z + dir.z * hl });
      return (
        <g key={`${b}-${i}`}>
          <line x1={s.X} y1={s.Y} x2={h.X} y2={h.Y} stroke={color} strokeWidth={isSel ? 2 : 1.2} opacity={isSel ? 1 : 0.7} />
          {isSel && <circle data-rot={`${b}:${i}`} cx={h.X} cy={h.Y} r={7} fill="#0f172a" stroke={color} strokeWidth={2} className="cursor-crosshair" />}
          <g data-knot={`${b}:${i}`} className="cursor-move">
            {n.tangentMode === 1 ? (
              <rect x={s.X - 6} y={s.Y - 6} width={12} height={12} fill={isSel ? '#fff' : color} stroke="#0f172a" strokeWidth={2} />
            ) : (
              <circle cx={s.X} cy={s.Y} r={isSel ? 8 : 6} fill={isSel ? '#fff' : color} stroke="#0f172a" strokeWidth={2} />
            )}
            <text x={s.X + 9} y={s.Y - 8} fontSize={10} fill="#fff" fontFamily="JetBrains Mono" style={{ paintOrder: 'stroke' }} stroke="#0f172a" strokeWidth={3}>
              {b === 'loop' ? i : `${SIDE_LETTER[b]}${i}`}
            </text>
          </g>
        </g>
      );
    });

  const bottle = (key: string, p: P, color: number, hidden: boolean, spotKey?: string) => {
    const s = S(p);
    const f = flavor(color);
    const r = px(bottleR);
    if (color === 0)
      return <circle key={key} data-spot={spotKey} cx={s.X} cy={s.Y} r={Math.max(2, r * 0.55)} fill="none" stroke="#94a3b8" strokeOpacity={0.5} strokeDasharray="2 2" className={spotKey && onSpot ? 'cursor-pointer' : undefined} />;
    return (
      <g key={key} data-spot={spotKey} className={spotKey && onSpot ? 'cursor-pointer' : undefined}>
        <circle cx={s.X} cy={s.Y} r={r} fill={hidden ? ITEM_HIDDEN : f.body} stroke={hidden ? '#555' : f.shade} strokeWidth={Math.max(1, r * 0.22)} />
        {!hidden && r > 4 && <circle cx={s.X - r * 0.3} cy={s.Y - r * 0.3} r={r * 0.3} fill={f.cap} opacity={0.8} />}
      </g>
    );
  };

  const pickPath = loop.points.length > 1 ? subPath(loop, 0, conveyor.pickRows * pitch) : '';
  const usedFeeders = level ? level.feeders.length : 3;

  return (
    <div ref={wrap} className="relative h-full w-full select-none overflow-hidden bg-board-ground">
      <svg width={size.w} height={size.h} className="absolute inset-0 block touch-none" onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp} onWheel={onWheel}>
        <g>{grid}</g>
        {/* feeders first: the loop rides over their ends */}
        {feeders.map((b, f) => beltStroke('f' + f, pathOf(b, false), f < usedFeeders))}
        <g data-loop="">{beltStroke('loop', pathOf(loop, true), true)}</g>
        {pickPath && <path d={pickPath} fill="none" stroke="#00D1FF" strokeOpacity={0.35} strokeWidth={px(beltWidth)} pointerEvents="none" />}

        {/* row boundaries + numbers */}
        {loop.points.length > 1 && Array.from({ length: conveyor.rows }, (_, r) => {
          const { p, t } = loop.sample(r * pitch);
          const o = outward(t);
          const half = beltWidth / 2;
          const a = S({ x: p.x + o.x * half, z: p.z + o.z * half }), b2 = S({ x: p.x - o.x * half, z: p.z - o.z * half });
          const mid = loop.sample((r + 0.5) * pitch);
          const mo = outward(mid.t);
          const lab = S({ x: mid.p.x + mo.x * (half + 0.22 * Math.max(scale, 0.6)), z: mid.p.z + mo.z * (half + 0.22 * Math.max(scale, 0.6)) });
          return (
            <g key={'row' + r} pointerEvents="none">
              <line x1={a.X} y1={a.Y} x2={b2.X} y2={b2.Y} stroke={r === 0 ? '#00D1FF' : '#8792B8'} strokeWidth={r === 0 ? 2 : 0.7} />
              {showRowNumbers && (r % 5 === 0 || r === conveyor.rows - 1 || r < conveyor.pickRows) && view.zoom * pitch > 6 && (
                <text x={lab.X} y={lab.Y} fontSize={9} textAnchor="middle" dominantBaseline="middle" fill={r < conveyor.pickRows ? '#00D1FF' : '#cbd5e1'} fontFamily="JetBrains Mono">{r}</text>
              )}
            </g>
          );
        })}

        {/* merge points */}
        {loop.points.length > 1 && conveyor.feeders.map((fd, f) => {
          const { p, t } = loop.sample((fd.mergeAt + 0.5) * pitch);
          const o = outward(t);
          const s = S({ x: p.x + o.x * (beltWidth / 2), z: p.z + o.z * (beltWidth / 2) });
          return (
            <g key={'m' + f} pointerEvents="none" opacity={f < usedFeeders ? 1 : 0.35}>
              <circle cx={s.X} cy={s.Y} r={9} fill={DEBUG.entrance} stroke="#0f172a" strokeWidth={2} />
              <text x={s.X} y={s.Y + 0.5} fontSize={10} fontWeight={700} textAnchor="middle" dominantBaseline="middle" fill="#0f172a">{SIDE_LETTER[f]}</text>
            </g>
          );
        })}

        {/* initial belt */}
        {level?.initial && loop.points.length > 1 && level.initial.map((row, r) => row.map((c, k) => bottle(`i${r}-${k}`, spot(r, k), c, false, `i:${r}:${k}`)))}

        {/* queues */}
        {queues.map((q) => q && (
          <g key={'q' + q.f}>
            {q.items.map((it) => bottle(`q${q.f}-${it.index}`, it.p, it.color, it.hidden, `f:${q.f}:${it.index}`))}
            {q.locks.map((l) => {
              const s = S(l.p);
              return (
                <g key={'lk' + l.row} pointerEvents="none">
                  <rect x={s.X - 13} y={s.Y - 8} width={26} height={16} rx={4} fill="#2F3554" stroke="#D4DBEA" />
                  <text x={s.X} y={s.Y + 0.5} fontSize={10} fontWeight={700} textAnchor="middle" dominantBaseline="middle" fill="#fff">{l.turns}</text>
                </g>
              );
            })}
            {q.more > 0 && (() => { const s = S(q.start); return (
              <g pointerEvents="none"><rect x={s.X - 22} y={s.Y - 9} width={44} height={18} rx={9} fill="#0f172a" opacity={0.85} />
                <text x={s.X} y={s.Y + 0.5} fontSize={10} textAnchor="middle" dominantBaseline="middle" fill="#e2e8f0">+{q.more} rows</text></g>); })()}
          </g>
        ))}

        {/* knots */}
        {editable && (
          <>
            {conveyor.feeders.map((fd, f) => <g key={'kf' + f}>{knotEls(f as 0 | 1 | 2, fd.nodes, SIDE_COLORS[f])}</g>)}
            {knotEls('loop', conveyor.loop.nodes, '#facc15')}
          </>
        )}
      </svg>

      <div className="pointer-events-none absolute left-2 top-2 flex flex-col gap-1 text-[11px] text-slate-200">
        <span className="rounded bg-slate-950/70 px-2 py-0.5 font-mono">
          {conveyor.id} · {conveyor.rows} rows × {width} · pick {conveyor.pickRows} · loop {loop.length.toFixed(2)} u · pitch {pitch.toFixed(3)}
        </span>
        {pickRowHint && <span className="rounded bg-amber-500/90 px-2 py-0.5 font-medium text-slate-950">{pickRowHint}</span>}
      </div>
      <div className="absolute bottom-2 right-2 flex items-center gap-1">
        <span className="rounded bg-slate-950/70 px-2 py-0.5 text-[10px] text-slate-400">wheel: zoom · drag: pan{editable ? ' · shift: snap' : ''}</span>
        <button className="btn" onClick={fit} title="Fit to view"><Maximize2 size={13} /></button>
      </div>
    </div>
  );
}
