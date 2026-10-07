import type { ConveyorFile, ConveyorNode, LevelFile, Tray } from './types';
import { FEEDER_SIDES } from './types';

/**
 * Writers that match Game.Domain.LevelJson.Write / ConveyorJson.Write line for line, so a file saved here keeps
 * `LevelTool migrate --check` clean. Readers fill the schema defaults so the editor always holds a complete document.
 */

// ── helpers (LevelJson.Q / ConveyorJson.Num) ──────────────────────────────────────────────
export function Q(s: string): string {
  let out = '"';
  for (const ch of s) {
    const c = ch.codePointAt(0)!;
    if (ch === '"') out += '\\"';
    else if (ch === '\\') out += '\\\\';
    else if (ch === '\n') out += '\\n';
    else if (ch === '\r') out += '\\r';
    else if (ch === '\t') out += '\\t';
    else if (c < 0x20) out += '\\u' + c.toString(16).padStart(4, '0');
    else out += ch;
  }
  return out + '"';
}

/** "0.###": three decimals at most, trailing zeros dropped. */
export function num(d: number): string {
  const r = Math.round(d * 1000) / 1000;
  if (Object.is(r, -0) || r === 0) return '0';
  return String(r);
}

export const round3 = (d: number) => {
  const r = Math.round(d * 1000) / 1000;
  return r === 0 ? 0 : r;
};

const numbers = (xs: number[]) => xs.join(', ');

// ── level ────────────────────────────────────────────────────────────────────────────────
export function writeLevel(level: LevelFile, width: number): string {
  let sb = '{\n';
  sb += '  "$schema": "../../../../../docs/design/level.schema.json",\n';
  sb += '  "formatVersion": 4,\n';
  sb += `  "id": ${Q(level.id)},\n`;
  sb += `  "conveyor": ${Q(level.conveyor)},\n`;
  sb += `  "slots": ${level.slots},\n`;
  sb += `  "extraSlots": ${level.extraSlots},\n`;
  const slotLocks = [...(level.slotLocks ?? [])];
  if (slotLocks.length > 0)
    sb += '  "slotLocks": [' + slotLocks.map((l) => `{ "slot": ${l.slot}, "lockTurns": ${l.lockTurns} }`).join(', ') + '],\n';
  sb += `  "trayCapacity": ${level.trayCapacity},\n`;
  sb += '  "colors": [' + numbers(level.colors) + '],\n';

  sb += '  "feeders": [';
  if (level.feeders.length === 0) sb += ']';
  else {
    sb += '\n';
    level.feeders.forEach((fd, f) => {
      sb += '    { "bottles": [\n';
      for (let i = 0; i < fd.bottles.length; i += width) {
        const row = fd.bottles.slice(i, Math.min(i + width, fd.bottles.length));
        sb += '      ' + numbers(row) + (i + width < fd.bottles.length ? ',\n' : '\n');
      }
      sb += '    ]';
      if (fd.hiddenRows && fd.hiddenRows.length > 0)
        sb += ', "hiddenRows": [' + [...new Set(fd.hiddenRows)].sort((a, b) => a - b).join(', ') + ']';
      if (fd.lockedRows && fd.lockedRows.length > 0) {
        const rows = [...fd.lockedRows].sort((a, b) => a.row - b.row);
        sb += ', "lockedRows": [' + rows.map((r) => `{ "row": ${r.row}, "lockTurns": ${r.lockTurns} }`).join(', ') + ']';
      }
      sb += ' }' + (f < level.feeders.length - 1 ? ',\n' : '\n');
    });
    sb += '  ]';
  }
  sb += ',\n';
  if (level.initial) {
    sb += '  "initial": [\n';
    level.initial.forEach((row, r) => {
      sb += '    [' + numbers(row) + (r < level.initial!.length - 1 ? '],\n' : ']\n');
    });
    sb += '  ],\n';
  }

  sb += '  "lanes": [\n';
  level.lanes.forEach((lane, j) => {
    const trays = lane.map((t) => {
      let s = `{ "color": ${t.color}`;
      if (t.hidden) s += ', "hidden": true';
      if (t.size && t.size !== 1) s += `, "size": ${t.size}`;
      if (t.lockTurns && t.lockTurns > 0) s += `, "lockTurns": ${t.lockTurns}`;
      return s + ' }';
    });
    sb += '    [' + trays.join(', ') + (j < level.lanes.length - 1 ? '],\n' : ']\n');
  });
  sb += '  ],\n';
  const links = level.links ?? [];
  if (links.length > 0) {
    sb += '  "links": [\n';
    links.forEach((l, i) => {
      sb += `    { "a": { "lane": ${l.a.lane}, "tray": ${l.a.tray} }, "b": { "lane": ${l.b.lane}, "tray": ${l.b.tray} } }` +
        (i < links.length - 1 ? ',\n' : '\n');
    });
    sb += '  ],\n';
  }
  sb += `  "view": { "cameraPreset": ${Q(level.view?.cameraPreset ?? 'default')} }`;
  const meta: string[] = [];
  const m = level.meta ?? {};
  if (m.name != null) meta.push(`"name": ${Q(m.name)}`);
  if (m.difficulty != null) meta.push(`"difficulty": ${Q(m.difficulty)}`);
  if (m.notes != null) meta.push(`"notes": ${Q(m.notes)}`);
  if (m.solution != null) meta.push('"solution": [' + m.solution.join(', ') + ']');
  if (meta.length > 0) sb += ',\n  "meta": { ' + meta.join(', ') + ' }';
  sb += '\n}\n';
  return sb;
}

export function readLevel(text: string): LevelFile {
  const j = JSON.parse(text);
  const lanes: Tray[][] = Array.isArray(j.lanes) ? j.lanes.map((l: any[]) => (Array.isArray(l) ? l.map((t) => ({ ...t })) : [])) : [];
  return {
    ...j,
    formatVersion: 4,
    id: String(j.id ?? ''),
    conveyor: String(j.conveyor ?? ''),
    slots: j.slots ?? 4,
    extraSlots: j.extraSlots ?? 2,
    trayCapacity: j.trayCapacity ?? 4,
    colors: Array.isArray(j.colors) ? j.colors : [],
    feeders: Array.isArray(j.feeders) ? j.feeders.map((f: any) => ({ ...f, bottles: Array.isArray(f.bottles) ? f.bottles : [] })) : [],
    lanes,
  };
}

// ── conveyor ─────────────────────────────────────────────────────────────────────────────
function writeNodes(nodes: ConveyorNode[], indent: string): string {
  if (nodes.length === 0) return '[]';
  let sb = '[\n';
  nodes.forEach((n, i) => {
    sb += indent + `{ "x": ${num(n.x)}, "z": ${num(n.z)}, "yRotation": ${num(n.yRotation ?? 0)}`;
    if (n.tangentMode === 1) sb += ', "tangentMode": 1';
    sb += ' }' + (i < nodes.length - 1 ? ',\n' : '\n');
  });
  return sb + indent.slice(0, indent.length - 2) + ']';
}

export function writeConveyor(c: ConveyorFile): string {
  let sb = '{\n';
  sb += '  "$schema": "../../../../../docs/design/conveyor.schema.json",\n';
  sb += '  "formatVersion": 2,\n';
  sb += `  "id": ${Q(c.id)},\n`;
  sb += `  "rows": ${c.rows},\n`;
  sb += `  "width": ${c.width},\n`;
  sb += `  "pickRows": ${c.pickRows},\n`;
  sb += `  "scale": ${num(c.scale)},\n`;
  sb += '  "loop": { "nodes": ' + writeNodes(c.loop.nodes, '    ') + ' },\n';
  sb += '  "feeders": [\n';
  c.feeders.forEach((fd, f) => {
    sb += `    { "side": ${Q(fd.side)}, "mergeAt": ${fd.mergeAt}, "nodes": ` + writeNodes(fd.nodes, '      ');
    sb += ' }' + (f < c.feeders.length - 1 ? ',\n' : '\n');
  });
  sb += '  ]';
  const meta: string[] = [];
  if (c.meta?.name != null) meta.push(`"name": ${Q(c.meta.name)}`);
  if (c.meta?.notes != null) meta.push(`"notes": ${Q(c.meta.notes)}`);
  if (meta.length > 0) sb += ',\n  "meta": { ' + meta.join(', ') + ' }';
  return sb + '\n}\n';
}

export function readConveyor(text: string): ConveyorFile {
  const j = JSON.parse(text);
  const feeders = Array.isArray(j.feeders) ? j.feeders : [];
  return {
    ...j,
    formatVersion: 2,
    id: String(j.id ?? ''),
    rows: j.rows ?? 16,
    width: j.width ?? 4,
    pickRows: j.pickRows ?? 4,
    scale: j.scale ?? 1,
    loop: { nodes: Array.isArray(j.loop?.nodes) ? j.loop.nodes : [] },
    feeders: FEEDER_SIDES.map((side, i) => ({
      side,
      mergeAt: feeders[i]?.mergeAt ?? 0,
      nodes: Array.isArray(feeders[i]?.nodes) ? feeders[i].nodes : [],
    })),
  };
}

export function writeIndex(order: string[]): string {
  let sb = '{\n  "order": [\n';
  order.forEach((id, i) => (sb += '    "' + id + (i < order.length - 1 ? '",\n' : '"\n')));
  return sb + '  ]\n}\n';
}
