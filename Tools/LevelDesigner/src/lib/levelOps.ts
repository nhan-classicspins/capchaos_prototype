import type { ConveyorFile, LevelFile, Tray, TrayRef } from './types';

/** Pure edits of a level that must keep the tray references (links) pointing at the same trays. */

export const clone = <T,>(v: T): T => structuredClone(v);

const sameRef = (a: TrayRef, b: TrayRef) => a.lane === b.lane && a.tray === b.tray;

/** Re-map every link after a tray list change; `map` returns the new ref of an old ref, or null when it is gone. */
function remapLinks(level: LevelFile, map: (r: TrayRef) => TrayRef | null) {
  if (!level.links) return;
  const next: { a: TrayRef; b: TrayRef }[] = [];
  for (const l of level.links) {
    const a = map(l.a), b = map(l.b);
    if (a && b) next.push({ a, b });
  }
  level.links = next.length ? next : undefined;
}

export function linkOf(level: LevelFile, ref: TrayRef): { index: number; partner: TrayRef } | null {
  const links = level.links ?? [];
  for (let i = 0; i < links.length; i++) {
    if (sameRef(links[i].a, ref)) return { index: i, partner: links[i].b };
    if (sameRef(links[i].b, ref)) return { index: i, partner: links[i].a };
  }
  return null;
}

export function insertTray(src: LevelFile, lane: number, at: number, tray: Tray): LevelFile {
  const level = clone(src);
  level.lanes[lane].splice(at, 0, tray);
  remapLinks(level, (r) => (r.lane === lane && r.tray >= at ? { lane, tray: r.tray + 1 } : r));
  return level;
}

export function removeTray(src: LevelFile, ref: TrayRef): LevelFile {
  const level = clone(src);
  level.lanes[ref.lane].splice(ref.tray, 1);
  remapLinks(level, (r) => {
    if (sameRef(r, ref)) return null;
    return r.lane === ref.lane && r.tray > ref.tray ? { lane: r.lane, tray: r.tray - 1 } : r;
  });
  return level;
}

/** Move a tray to (lane, index) — index counted in the destination lane after removal. */
export function moveTray(src: LevelFile, from: TrayRef, to: TrayRef): LevelFile {
  if (sameRef(from, to)) return src;
  const level = clone(src);
  const [tray] = level.lanes[from.lane].splice(from.tray, 1);
  level.lanes[to.lane].splice(to.tray, 0, tray);
  remapLinks(level, (r) => {
    if (sameRef(r, from)) return to;
    let { lane, tray: t } = r;
    if (lane === from.lane && t > from.tray) t--;
    if (lane === to.lane && t >= to.tray) t++;
    return { lane, tray: t };
  });
  return level;
}

export function removeLane(src: LevelFile, lane: number): LevelFile {
  const level = clone(src);
  level.lanes.splice(lane, 1);
  remapLinks(level, (r) => (r.lane === lane ? null : r.lane > lane ? { lane: r.lane - 1, tray: r.tray } : r));
  return level;
}

export function moveLane(src: LevelFile, lane: number, dir: -1 | 1): LevelFile {
  const other = lane + dir;
  if (other < 0 || other >= src.lanes.length) return src;
  const level = clone(src);
  [level.lanes[lane], level.lanes[other]] = [level.lanes[other], level.lanes[lane]];
  remapLinks(level, (r) => (r.lane === lane ? { ...r, lane: other } : r.lane === other ? { ...r, lane } : r));
  return level;
}

export function setLink(src: LevelFile, a: TrayRef, b: TrayRef): LevelFile {
  const level = clone(src);
  const links = (level.links ?? []).filter((l) => ![l.a, l.b].some((r) => sameRef(r, a) || sameRef(r, b)));
  links.push(a.lane < b.lane ? { a, b } : { a: b, b: a });
  level.links = links;
  return level;
}

export function unlink(src: LevelFile, ref: TrayRef): LevelFile {
  const level = clone(src);
  level.links = (level.links ?? []).filter((l) => !sameRef(l.a, ref) && !sameRef(l.b, ref));
  if (level.links.length === 0) level.links = undefined;
  return level;
}

/** Feeder rows (`width` bottles each) of a queue. */
export const rowsOf = (bottles: number[], width: number) => Math.ceil(bottles.length / width);

/** Shift hidden / locked row indices after inserting (delta +1) or removing (delta −1) queue row `at`. */
export function shiftRowMarks(feeder: LevelFile['feeders'][number], at: number, delta: 1 | -1) {
  const shift = (r: number) => (r > at || (delta === 1 && r === at) ? r + delta : r);
  if (feeder.hiddenRows) {
    feeder.hiddenRows = feeder.hiddenRows.filter((r) => !(delta === -1 && r === at)).map(shift);
    if (!feeder.hiddenRows.length) delete feeder.hiddenRows;
  }
  if (feeder.lockedRows) {
    feeder.lockedRows = feeder.lockedRows.filter((l) => !(delta === -1 && l.row === at)).map((l) => ({ ...l, row: shift(l.row) }));
    if (!feeder.lockedRows.length) delete feeder.lockedRows;
  }
}

/**
 * A starting point for the feeders, built from the lanes: trays in tap order (front row of every lane, then the
 * next…) each ask for size × trayCapacity bottles of their colour, queued a whole row at a time and dealt to the
 * feeders round-robin. Balanced by construction (V4); solvability is still LevelTool's to prove.
 */
export function feedersFromLanes(src: LevelFile, conveyor: ConveyorFile, feederCount: number): LevelFile {
  const level = clone(src);
  const width = conveyor.width;
  const stream: number[] = [];
  const depth = Math.max(0, ...level.lanes.map((l) => l.length));
  for (let t = 0; t < depth; t++)
    for (const lane of level.lanes) {
      const tray = lane[t];
      if (tray) for (let i = 0; i < (tray.size ?? 1) * level.trayCapacity; i++) stream.push(tray.color);
    }
  const n = Math.max(1, Math.min(3, feederCount));
  const queues: number[][] = Array.from({ length: n }, () => []);
  for (let i = 0, row = 0; i < stream.length; i += width, row++) queues[row % n].push(...stream.slice(i, i + width));
  level.feeders = queues.filter((q) => q.length > 0).map((bottles) => ({ bottles }));
  level.initial = undefined;
  if (level.meta) delete level.meta.solution;
  return level;
}

export function nextLevelId(ids: string[]): string {
  const max = ids.reduce((m, id) => Math.max(m, Number(id.slice(6)) || 0), 0);
  return `level_${String(max + 1).padStart(4, '0')}`;
}

export function newLevel(id: string, conveyor: ConveyorFile): LevelFile {
  const w = conveyor.width;
  return {
    formatVersion: 4, id, conveyor: conveyor.id, slots: 4, extraSlots: 2, trayCapacity: 4, colors: [1, 2],
    feeders: [{ bottles: [...Array(w).fill(1), ...Array(w).fill(2)] }],
    lanes: [[{ color: 1 }], [{ color: 2 }]],
    view: { cameraPreset: 'default' },
    meta: { name: 'New level', difficulty: 'easy' },
  };
}
