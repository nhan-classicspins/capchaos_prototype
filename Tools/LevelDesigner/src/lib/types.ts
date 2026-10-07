// Mirrors docs/design/level.schema.json (format v4) and docs/design/conveyor.schema.json (format v2).

export type ColorId = number; // 1..8 — Game.Domain.CapColor; 0 = an empty belt spot (initial only)

export interface ConveyorNode {
  x: number;
  z: number;
  yRotation?: number;
  tangentMode?: 0 | 1;
}

export type FeederSide = 'right' | 'left' | 'middle';
export const FEEDER_SIDES: FeederSide[] = ['right', 'left', 'middle'];

export interface ConveyorFeeder {
  side: FeederSide;
  mergeAt: number;
  nodes: ConveyorNode[];
}

export interface ConveyorFile {
  $schema?: string;
  formatVersion: 2;
  id: string;
  rows: number;
  width: number;
  pickRows: number;
  scale: number;
  loop: { nodes: ConveyorNode[] };
  feeders: ConveyorFeeder[];
  meta?: { name?: string; notes?: string };
}

export interface Tray {
  color: ColorId;
  hidden?: boolean;
  size?: number; // 1 S · 2 M · 3 L · 4 XL
  lockTurns?: number;
}

export interface TrayRef {
  lane: number;
  tray: number;
}

export interface LevelFeeder {
  bottles: ColorId[];
  hiddenRows?: number[];
  lockedRows?: { row: number; lockTurns: number }[];
}

export type Difficulty = 'tutorial' | 'easy' | 'medium' | 'hard' | 'breather';
export const DIFFICULTIES: Difficulty[] = ['tutorial', 'easy', 'medium', 'hard', 'breather'];
export const CAMERA_PRESETS = ['default', 'tall', 'wide'] as const;

export interface LevelFile {
  $schema?: string;
  formatVersion: 4;
  id: string;
  conveyor: string;
  slots: number;
  extraSlots: number;
  slotLocks?: { slot: number; lockTurns: number }[];
  trayCapacity: number;
  colors: ColorId[];
  feeders: LevelFeeder[];
  initial?: ColorId[][];
  lanes: Tray[][];
  links?: { a: TrayRef; b: TrayRef }[];
  view?: { cameraPreset?: string };
  meta?: { name?: string; difficulty?: Difficulty; notes?: string; solution?: number[] };
}

export type DocKind = 'level' | 'conveyor';
export interface DocRef {
  kind: DocKind;
  id: string;
}
