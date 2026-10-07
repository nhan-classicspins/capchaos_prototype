import type { ConveyorFile, LevelFile, TrayRef } from './types';
import { FEEDER_SIDES } from './types';
import { flavor } from './colors';

/**
 * A live port of the structural (V1) and semantic (V4, V5, V7, V8, V9, V10) checks of Game.Domain — enough to keep
 * the designer honest while editing. V6 (solvability) and the exact V1 parser are LevelTool's: run "Validate".
 */
export interface Problem {
  code: string; // V1, V4 ...
  message: string;
  severity: 'error' | 'warning';
  tray?: TrayRef;
}

const MIN_BEND_ROWS = 6;
const MIN_KNOT_GAP = 0.01;
const colorName = (c: number) => `${c} (${flavor(c).name})`;
const err = (code: string, message: string, tray?: TrayRef): Problem => ({ code, message, severity: 'error', tray });
const warn = (code: string, message: string): Problem => ({ code, message, severity: 'warning' });
const isInt = (v: unknown) => typeof v === 'number' && Number.isInteger(v);

export function validateConveyor(c: ConveyorFile, fileId?: string): Problem[] {
  const out: Problem[] = [];
  const where = `conveyor ${c.id}`;
  if (!/^[a-z][a-z0-9_]{0,47}$/.test(c.id)) out.push(err('V1', `$.id: '${c.id}' must be lowercase letters, digits and _`));
  if (fileId && c.id !== fileId) out.push(err('V1', `$.id: '${c.id}' ≠ file name '${fileId}'`));
  if (!isInt(c.rows) || c.rows < 8 || c.rows > 64) out.push(err('V1', `$.rows: ${c.rows} outside 8..64`));
  if (!isInt(c.width) || c.width < 1 || c.width > 6) out.push(err('V1', `$.width: ${c.width} outside 1..6`));
  if (!isInt(c.pickRows) || c.pickRows < 1) out.push(err('V1', `$.pickRows: must be an integer ≥ 1`));
  if (!(c.scale >= 0.1 && c.scale <= 4)) out.push(err('V1', `$.scale: ${c.scale} outside 0.1..4`));
  if (c.pickRows * 2 + MIN_BEND_ROWS > c.rows)
    out.push(err('V8', `${where}.pickRows: ${c.pickRows} pick rows need rows ≥ ${c.pickRows * 2 + MIN_BEND_ROWS}, not ${c.rows}`));
  const merges = new Set<number>();
  c.feeders.forEach((f, i) => {
    if (f.side !== FEEDER_SIDES[i]) out.push(err('V1', `$.feeders[${i}].side: must be '${FEEDER_SIDES[i]}'`));
    const p = `${where}.feeders[${i}].mergeAt`;
    if (!isInt(f.mergeAt) || f.mergeAt < 0 || f.mergeAt >= c.rows) out.push(err('V8', `${p}: ${f.mergeAt} is not a track position 0..${c.rows - 1}`));
    else if (f.mergeAt < c.pickRows) out.push(err('V8', `${p}: ${f.mergeAt} is inside the pick zone 0..${c.pickRows - 1}`));
    if (merges.has(f.mergeAt)) out.push(err('V8', `${p}: ${f.mergeAt} is another feeder's merge point`));
    merges.add(f.mergeAt);
  });
  checkSpline(c.loop.nodes, true, 3, `${where}.loop`, out);
  c.feeders.forEach((f, i) => checkSpline(f.nodes, false, 2, `${where}.feeders[${i}]`, out));
  return out;
}

function checkSpline(nodes: ConveyorFile['loop']['nodes'], closed: boolean, min: number, path: string, out: Problem[]) {
  if (nodes.length < min) { out.push(err('V9', `${path}.nodes: at least ${min} nodes`)); return; }
  nodes.forEach((n, i) => {
    if (![n.x, n.z, n.yRotation ?? 0].every(Number.isFinite)) { out.push(err('V9', `${path}.nodes[${i}]: x, z, yRotation must be finite`)); return; }
    if (!closed && i === nodes.length - 1) return;
    const b = nodes[(i + 1) % nodes.length];
    if (Math.hypot(b.x - n.x, b.z - n.z) < MIN_KNOT_GAP)
      out.push(err('V9', `${path}.nodes[${i}]: it and node ${(i + 1) % nodes.length} stand on the same spot`));
  });
}

export interface ColorBalance {
  color: number;
  bottles: number;
  places: number;
  declared: boolean;
}

export function colorBalance(level: LevelFile): ColorBalance[] {
  const bottles = new Map<number, number>();
  const places = new Map<number, number>();
  const add = (m: Map<number, number>, c: number, n: number) => m.set(c, (m.get(c) ?? 0) + n);
  level.initial?.forEach((row) => row.forEach((c) => c !== 0 && add(bottles, c, 1)));
  level.feeders.forEach((f) => f.bottles.forEach((c) => c !== 0 && add(bottles, c, 1)));
  level.lanes.forEach((lane) => lane.forEach((t) => add(places, t.color, (t.size ?? 1) * level.trayCapacity)));
  const all = new Set<number>([...level.colors, ...bottles.keys(), ...places.keys()]);
  return [...all].sort((a, b) => a - b).map((color) => ({
    color, bottles: bottles.get(color) ?? 0, places: places.get(color) ?? 0, declared: level.colors.includes(color),
  }));
}

export function validateLevel(level: LevelFile, conveyor: ConveyorFile | undefined, fileId?: string): Problem[] {
  const out: Problem[] = [];
  // ── V1 (structure, the parts an editor can get wrong) ──
  if (!/^level_[0-9]{4}$/.test(level.id)) out.push(err('V1', `$.id: '${level.id}' must be level_NNNN`));
  if (fileId && level.id !== fileId) out.push(err('V1', `$.id: '${level.id}' ≠ file name '${fileId}'`));
  if (!conveyor) out.push(err('V1', `$.conveyor: no conveyor '${level.conveyor}' in ConveyorConfig/`));
  if (level.slots < 1 || level.slots > 6) out.push(err('V1', `$.slots: ${level.slots} outside 1..6`));
  if (level.extraSlots < 0 || level.extraSlots > 5) out.push(err('V1', `$.extraSlots: ${level.extraSlots} outside 0..5`));
  if (level.slots + level.extraSlots > 6) out.push(err('V1', `slots + extraSlots = ${level.slots + level.extraSlots} > 6`));
  if (level.trayCapacity < 2 || level.trayCapacity > 6) out.push(err('V1', `$.trayCapacity: ${level.trayCapacity} outside 2..6`));
  if (level.colors.length < 1 || level.colors.length > 8) out.push(err('V1', `$.colors: 1..8 colours`));
  if (new Set(level.colors).size !== level.colors.length) out.push(err('V1', `$.colors: a colour is listed twice`));
  if (level.feeders.length > 3) out.push(err('V1', `$.feeders: at most 3`));
  if (level.lanes.length < 1 || level.lanes.length > 4) out.push(err('V1', `$.lanes: 1..4 lanes`));
  level.lanes.forEach((lane, j) => {
    if (lane.length === 0) out.push(err('V1', `$.lanes[${j}]: a lane has at least one tray`));
    lane.forEach((t, i) => {
      if (!(t.color >= 1 && t.color <= 8)) out.push(err('V1', `$.lanes[${j}][${i}].color: ${t.color} is not a colour 1..8`, { lane: j, tray: i }));
      if (t.lockTurns != null && (t.lockTurns < 1 || t.lockTurns > 99)) out.push(err('V1', `$.lanes[${j}][${i}].lockTurns: 1..99`, { lane: j, tray: i }));
    });
  });
  if (conveyor) {
    if (level.feeders.length > conveyor.feeders.length) out.push(err('V1', `$.feeders: ${level.feeders.length} feeders, the conveyor has ${conveyor.feeders.length}`));
    if (level.initial) {
      if (level.initial.length !== conveyor.rows) out.push(err('V1', `$.initial: ${level.initial.length} rows, the conveyor has ${conveyor.rows}`));
      level.initial.forEach((row, r) => {
        if (row.length !== conveyor.width) out.push(err('V1', `$.initial[${r}]: ${row.length} spots, the conveyor's width is ${conveyor.width}`));
      });
    }
    for (const p of validateConveyor(conveyor)) out.push(p);
  }

  // ── V4 / V5: colour balance ──
  for (const b of colorBalance(level)) {
    if (!b.declared) {
      if (b.bottles > 0) out.push(err('V5', `bottles of colour ${colorName(b.color)} — not in colors`));
      if (b.places > 0) out.push(err('V5', `trays of colour ${colorName(b.color)} — not in colors`));
      continue;
    }
    if (b.bottles === 0 && b.places === 0) out.push(err('V5', `colors: ${colorName(b.color)} is declared but never used`));
    else if (b.bottles !== b.places)
      out.push(err('V4', `colour ${colorName(b.color)}: ${b.bottles} bottles vs ${b.places} tray places (trays × size × trayCapacity ${level.trayCapacity})`));
  }

  // ── V8: feeder rows ──
  const width = conveyor?.width ?? 4;
  level.feeders.forEach((f, i) => {
    if (f.bottles.length === 0) out.push(err('V8', `feeders[${i}].bottles: a feeder carries at least one bottle`));
    const rows = Math.ceil(f.bottles.length / width);
    f.hiddenRows?.forEach((r) => r >= rows && out.push(err('V8', `feeders[${i}].hiddenRows: row ${r} is past the queue's ${rows} row(s)`)));
    f.lockedRows?.forEach((l) => l.row >= rows && out.push(err('V8', `feeders[${i}].lockedRows: row ${l.row} is past the queue's ${rows} row(s)`)));
    if (f.bottles.length % width !== 0) out.push(warn('V8', `feeders[${i}]: ${f.bottles.length} bottles is not whole rows of ${width}`));
  });

  // ── V7: tray modifiers ──
  const exists = (t: TrayRef) => t.lane >= 0 && t.lane < level.lanes.length && t.tray >= 0 && t.tray < level.lanes[t.lane].length;
  const key = (t: TrayRef) => `${t.lane}:${t.tray}`;
  const linked = new Set<string>();
  (level.links ?? []).forEach((l, i) => {
    const where = `links[${i}]`;
    if (!exists(l.a) || !exists(l.b)) { out.push(err('V7', `${where}: a tray does not exist`)); return; }
    if (l.a.lane === l.b.lane) out.push(err('V7', `${where}: both trays are on lane ${l.a.lane} — a link joins two different lanes`, l.a));
    else if (l.a.tray === 0 && l.b.tray === 0) out.push(err('V7', `${where}: both trays start at the front`, l.a));
    for (const t of [l.a, l.b]) {
      if (linked.has(key(t))) out.push(err('V7', `${where}: lane ${t.lane} tray ${t.tray} is already in another link`, t));
      linked.add(key(t));
      if ((level.lanes[t.lane][t.tray].lockTurns ?? 0) > 0) out.push(err('V7', `${where}: lane ${t.lane} tray ${t.tray} is locked — a linked tray can not also be locked`, t));
    }
  });

  // ── V10: slot locks ──
  const seen = new Set<number>();
  (level.slotLocks ?? []).forEach((l, i) => {
    if (l.slot < 0 || l.slot >= level.slots) out.push(err('V10', `slotLocks[${i}]: slot ${l.slot} is not an open slot 0..${level.slots - 1}`));
    else if (seen.has(l.slot)) out.push(err('V10', `slotLocks[${i}]: slot ${l.slot} is locked twice`));
    seen.add(l.slot);
  });
  if (level.slots > 0 && seen.size >= level.slots) out.push(err('V10', `slotLocks: all ${level.slots} open slots are locked`));

  if (level.meta?.solution) out.push(warn('V6', 'meta.solution is recorded — LevelTool replays it; clear it after changing the level so the solver searches again'));
  return out;
}
