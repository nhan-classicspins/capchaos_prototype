// Flavour colours — DesignTokens.Flavors (Body / Shade / Cap), in CapColor order 1..8. Keep in sync by hand.
export interface Flavor {
  id: number;
  code: string;
  name: string;
  body: string;
  shade: string;
  cap: string;
}

export const FLAVORS: Flavor[] = [
  { id: 1, code: 'R', name: 'Red', body: '#E8314D', shade: '#A22236', cap: '#F498A6' },
  { id: 2, code: 'O', name: 'Orange', body: '#F97610', shade: '#AE530B', cap: '#FCBA88' },
  { id: 3, code: 'B', name: 'Blue', body: '#2977F7', shade: '#1D53AD', cap: '#94BBFB' },
  { id: 4, code: 'G', name: 'Green', body: '#64E917', shade: '#46A310', cap: '#B2F48B' },
  { id: 5, code: 'P', name: 'Purple', body: '#9141D8', shade: '#662E97', cap: '#C8A0EC' },
  { id: 6, code: 'Y', name: 'Yellow', body: '#FBC40F', shade: '#B0890B', cap: '#FDE287' },
  { id: 7, code: 'C', name: 'Cyan', body: '#1AD1ED', shade: '#1292A6', cap: '#8DE8F6' },
  { id: 8, code: 'N', name: 'Pink ("Brown")', body: '#FE79C0', shade: '#B25586', cap: '#FFBCE0' },
];

const MISSING: Flavor = { id: 0, code: '.', name: 'Empty', body: '#FF00FF', shade: '#FF00FF', cap: '#FF00FF' };

export const flavor = (id: number): Flavor => FLAVORS[id - 1] ?? MISSING;

/** Hidden item (R23) — DesignTokens.ItemHidden. */
export const ITEM_HIDDEN = '#787878';
/** Hidden tray (R17) — DesignTokens.MysteryColors. */
export const MYSTERY = { body: '#5F6A8F', shade: '#434C6E', cap: '#8792B8' };
export const LOCK_BODY = '#2F3554';
export const ROPE = '#EBD5A4';
/** Conveyor debug colours — DesignTokens (Entrance / Reach / Head / Land). */
export const DEBUG = { entrance: '#FFD400', reach: '#00D1FF', head: '#3DFF6E', land: '#FF3DD8' };
export const SIZE_NAMES = ['', 'S', 'M', 'L', 'XL'];
