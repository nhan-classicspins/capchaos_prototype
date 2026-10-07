import type { ConveyorNode } from './types';

/**
 * The belt's centre line, built the way ConveyorBeltView.SetRoute builds its Unity Spline: a smooth knot gets
 * collinear handles along its heading (yRotation: 0 = +z, 90 = +x), each half the larger X/Z distance to that
 * neighbour; a sharp knot (tangentMode 1, Linear) has zero handles. Baked to an arc-length table.
 */
export interface P { x: number; z: number }

const dirOf = (deg: number): P => ({ x: Math.sin((deg * Math.PI) / 180), z: Math.cos((deg * Math.PI) / 180) });
const halfMaxAxis = (a: P, b: P) => Math.max(Math.abs(a.x - b.x), Math.abs(a.z - b.z)) * 0.5;

function tangentLengths(nodes: ConveyorNode[], closed: boolean, i: number) {
  const n = nodes.length;
  const hasPrev = closed || i > 0, hasNext = closed || i < n - 1;
  let tin = hasPrev ? halfMaxAxis(nodes[i], nodes[(i - 1 + n) % n]) : 0;
  let tout = hasNext ? halfMaxAxis(nodes[i], nodes[(i + 1) % n]) : 0;
  if (!hasPrev) tin = tout;
  if (!hasNext) tout = tin;
  return { tin, tout };
}

export type Segment = [P, P, P, P];

export function segments(nodes: ConveyorNode[], closed: boolean): Segment[] {
  const n = nodes.length;
  if (n < 2) return [];
  const segs: Segment[] = [];
  const count = closed ? n : n - 1;
  for (let i = 0; i < count; i++) {
    const a = nodes[i], b = nodes[(i + 1) % n];
    const ta = tangentLengths(nodes, closed, i), tb = tangentLengths(nodes, closed, (i + 1) % n);
    const da = dirOf(a.yRotation ?? 0), db = dirOf(b.yRotation ?? 0);
    const p1 = a.tangentMode === 1 ? { x: a.x, z: a.z } : { x: a.x + da.x * ta.tout, z: a.z + da.z * ta.tout };
    const p2 = b.tangentMode === 1 ? { x: b.x, z: b.z } : { x: b.x - db.x * tb.tin, z: b.z - db.z * tb.tin };
    segs.push([{ x: a.x, z: a.z }, p1, p2, { x: b.x, z: b.z }]);
  }
  return segs;
}

function bez(s: Segment, t: number): P {
  const u = 1 - t;
  const a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
  return { x: a * s[0].x + b * s[1].x + c * s[2].x + d * s[3].x, z: a * s[0].z + b * s[1].z + c * s[2].z + d * s[3].z };
}

export class Belt {
  readonly points: P[] = [];
  readonly arc: number[] = [];
  readonly length: number;
  /** Arc length at each knot (where segment i starts). */
  readonly knotAt: number[] = [];

  constructor(readonly nodes: ConveyorNode[], readonly closed: boolean) {
    const segs = segments(nodes, closed);
    const steps = 48;
    let total = 0;
    segs.forEach((s, si) => {
      this.knotAt.push(total);
      for (let k = si === 0 ? 0 : 1; k <= steps; k++) {
        const p = bez(s, k / steps);
        const q = this.points[this.points.length - 1];
        if (q) total += Math.hypot(p.x - q.x, p.z - q.z);
        this.points.push(p);
        this.arc.push(total);
      }
    });
    if (!closed && segs.length > 0) this.knotAt.push(total);
    this.length = total;
  }

  /** Point and unit direction of travel `d` along the belt. A loop wraps; an open belt runs on straight past its ends. */
  sample(d: number): { p: P; t: P } {
    const n = this.points.length;
    if (n < 2) return { p: this.points[0] ?? { x: 0, z: 0 }, t: { x: 0, z: 1 } };
    if (this.closed) d = ((d % this.length) + this.length) % this.length;
    else if (d <= 0 || d >= this.length) {
      const [i0, i1] = d <= 0 ? [0, 1] : [n - 2, n - 1];
      const t = norm({ x: this.points[i1].x - this.points[i0].x, z: this.points[i1].z - this.points[i0].z });
      const base = d <= 0 ? this.points[0] : this.points[n - 1];
      const over = d <= 0 ? d : d - this.length;
      return { p: { x: base.x + t.x * over, z: base.z + t.z * over }, t };
    }
    let lo = 0, hi = n - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (this.arc[mid] <= d) lo = mid; else hi = mid;
    }
    const span = this.arc[hi] - this.arc[lo];
    const k = span > 1e-9 ? (d - this.arc[lo]) / span : 0;
    const a = this.points[lo], b = this.points[hi];
    return { p: { x: a.x + (b.x - a.x) * k, z: a.z + (b.z - a.z) * k }, t: norm({ x: b.x - a.x, z: b.z - a.z }) };
  }

  /** Shortest distance from `q` to the centre line. */
  distanceTo(q: P): number {
    let best = Infinity;
    for (let i = 1; i < this.points.length; i++) best = Math.min(best, segDist(q, this.points[i - 1], this.points[i]));
    return best;
  }

  /** Arc length of the closest point on the centre line to `q`. */
  project(q: P): number {
    let best = Infinity, at = 0;
    for (let i = 1; i < this.points.length; i++) {
      const a = this.points[i - 1], b = this.points[i];
      const dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
      const t = l2 > 0 ? Math.max(0, Math.min(1, ((q.x - a.x) * dx + (q.z - a.z) * dz) / l2)) : 0;
      const d = Math.hypot(q.x - (a.x + dx * t), q.z - (a.z + dz * t));
      if (d < best) { best = d; at = this.arc[i - 1] + (this.arc[i] - this.arc[i - 1]) * t; }
    }
    return at;
  }
}

export const norm = (v: P): P => {
  const l = Math.hypot(v.x, v.z);
  return l > 1e-12 ? { x: v.x / l, z: v.z / l } : { x: 0, z: 1 };
};

/** Outward side of travel: left of travel — the outside of a loop running clockwise seen from above. */
export const outward = (t: P): P => ({ x: -t.z, z: t.x });

function segDist(q: P, a: P, b: P) {
  const dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
  const t = l2 > 0 ? Math.max(0, Math.min(1, ((q.x - a.x) * dx + (q.z - a.z) * dz) / l2)) : 0;
  return Math.hypot(q.x - (a.x + dx * t), q.z - (a.z + dz * t));
}

/** Heading (yRotation degrees) of a direction. */
export const headingOf = (v: P) => {
  const deg = (Math.atan2(v.x, v.z) * 180) / Math.PI;
  return deg < 0 ? deg + 360 : deg;
};
