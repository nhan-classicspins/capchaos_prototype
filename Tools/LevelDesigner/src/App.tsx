import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Braces, FlaskConical, RefreshCw, Redo2, Save, SaveAll, ShieldCheck, Undo2 } from 'lucide-react';
import type { ConveyorFile, DocKind, DocRef, LevelFile } from './lib/types';
import { readConveyor, readLevel, writeConveyor, writeIndex, writeLevel } from './lib/format';
import { validateConveyor, validateLevel, type Problem } from './lib/validate';
import { newLevel, nextLevelId } from './lib/levelOps';
import { api } from './lib/api';
import { Sidebar } from './components/Sidebar';
import { LevelEditor } from './components/LevelEditor';
import { ConveyorEditor } from './components/ConveyorEditor';
import { JsonModal, LevelToolModal, type ToolRun } from './components/Modals';

type Levels = Record<string, LevelFile>;
type Conveyors = Record<string, ConveyorFile>;
const keyOf = (r: DocRef) => `${r.kind}:${r.id}`;

/** A fresh layout to start a conveyor from when none is selected: a rounded stadium with the three feeders above it. */
const STARTER_CONVEYOR = (id: string): ConveyorFile => ({
  formatVersion: 2, id, rows: 32, width: 4, pickRows: 5, scale: 0.46,
  loop: { nodes: [
    { x: 0.8, z: 3.1, yRotation: 270 }, { x: -0.8, z: 3.1, yRotation: 270 }, { x: -1.45, z: 4.35, yRotation: 0 },
    { x: -0.8, z: 5.6, yRotation: 90 }, { x: 0.8, z: 5.6, yRotation: 90 }, { x: 1.45, z: 4.35, yRotation: 180 },
  ] },
  feeders: [
    { side: 'right', mergeAt: 26, nodes: [{ x: 2.7, z: 11.7, yRotation: 180 }, { x: 2.7, z: 6.5, yRotation: 180 }, { x: 1.91, z: 4.35, yRotation: 270 }] },
    { side: 'left', mergeAt: 10, nodes: [{ x: -2.7, z: 11.7, yRotation: 180 }, { x: -2.7, z: 6.5, yRotation: 180 }, { x: -1.91, z: 4.35, yRotation: 90 }] },
    { side: 'middle', mergeAt: 18, nodes: [{ x: 0, z: 11.7, yRotation: 180 }, { x: 0, z: 6.05, yRotation: 180 }] },
  ],
  meta: { name: 'New conveyor' },
});

export function App() {
  const [levels, setLevels] = useState<Levels>({});
  const [conveyors, setConveyors] = useState<Conveyors>({});
  const [saved, setSaved] = useState<Record<string, string>>({});
  const [order, setOrder] = useState<string[]>([]);
  const [savedIndex, setSavedIndex] = useState<string | null>(null);
  const [current, setCurrent] = useState<DocRef | null>(null);
  const [tab, setTab] = useState<DocKind>('level');
  const [root, setRoot] = useState('');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; bad?: boolean } | null>(null);
  const [json, setJson] = useState<{ title: string; text: string } | null>(null);
  const [run, setRun] = useState<ToolRun | null>(null);
  const history = useRef<Record<string, { past: string[]; future: string[]; at: number }>>({});
  const [, bump] = useState(0);

  const say = (text: string, bad = false) => { setToast({ text, bad }); window.setTimeout(() => setToast(null), 3500); };

  // ── load ──
  const load = useCallback(async () => {
    try {
      const p = await api.load();
      const lv: Levels = {}, cv: Conveyors = {}, sv: Record<string, string> = {};
      const fails: string[] = [];
      for (const c of p.conveyors) { try { cv[c.id] = readConveyor(c.text); sv[`conveyor:${c.id}`] = c.text; } catch (e: any) { fails.push(`${c.id}: ${e.message}`); } }
      for (const l of p.levels) { try { lv[l.id] = readLevel(l.text); sv[`level:${l.id}`] = l.text; } catch (e: any) { fails.push(`${l.id}: ${e.message}`); } }
      let ord: string[] = [];
      try { ord = p.index ? JSON.parse(p.index).order ?? [] : []; } catch { fails.push('levels.index.json: not JSON'); }
      setLevels(lv); setConveyors(cv); setSaved(sv); setOrder(ord); setSavedIndex(p.index); setRoot(p.root);
      history.current = {};
      setLoadError(fails.length ? `Could not read: ${fails.join('; ')}` : null);
      setCurrent((cur) => cur && (cur.kind === 'level' ? lv[cur.id] : cv[cur.id]) ? cur : (ord[0] && lv[ord[0]] ? { kind: 'level', id: ord[0] } : null));
    } catch (e: any) {
      setLoadError(`Cannot reach the config API (${e.message}). Start the tool with \`npm run dev\` in Tools/LevelDesigner.`);
    }
  }, []);
  useEffect(() => { load(); }, [load]);

  // ── derived ──
  const textOf = useCallback((r: DocRef): string | null => {
    if (r.kind === 'level') { const l = levels[r.id]; return l ? writeLevel(l, conveyors[l.conveyor]?.width ?? 4) : null; }
    const c = conveyors[r.id]; return c ? writeConveyor(c) : null;
  }, [levels, conveyors]);
  const dirty = useCallback((r: DocRef) => textOf(r) !== saved[keyOf(r)], [textOf, saved]);
  const indexText = writeIndex(order);
  const indexDirty = indexText !== savedIndex;

  const problems = useMemo(() => {
    const out: Record<string, Problem[]> = {};
    for (const c of Object.values(conveyors)) out[`conveyor:${c.id}`] = validateConveyor(c);
    for (const l of Object.values(levels)) out[`level:${l.id}`] = validateLevel(l, conveyors[l.conveyor]);
    return out;
  }, [levels, conveyors]);
  const broken = (r: DocRef) => (problems[keyOf(r)] ?? []).some((p) => p.severity === 'error');

  const allRefs = (): DocRef[] => [
    ...Object.keys(levels).map((id) => ({ kind: 'level' as const, id })),
    ...Object.keys(conveyors).map((id) => ({ kind: 'conveyor' as const, id })),
  ];
  const anyDirty = allRefs().some(dirty) || indexDirty;

  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (anyDirty) e.preventDefault(); };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [anyDirty]);

  const levelList = useMemo(() => {
    const listed = order.filter((id) => levels[id]).map((id) => levels[id]);
    const rest = Object.values(levels).filter((l) => !order.includes(l.id)).sort((a, b) => a.id.localeCompare(b.id));
    return [...listed, ...rest];
  }, [order, levels]);
  const conveyorList = useMemo(() => Object.values(conveyors).sort((a, b) => a.id.localeCompare(b.id)), [conveyors]);

  // ── edit + undo ──
  const put = (r: DocRef, doc: LevelFile | ConveyorFile) => {
    if (r.kind === 'level') setLevels((m) => ({ ...m, [r.id]: doc as LevelFile }));
    else setConveyors((m) => ({ ...m, [r.id]: doc as ConveyorFile }));
  };
  const change = (r: DocRef, doc: LevelFile | ConveyorFile) => {
    const prev = r.kind === 'level' ? levels[r.id] : conveyors[r.id];
    const h = (history.current[keyOf(r)] ??= { past: [], future: [], at: 0 });
    const now = Date.now();
    if (prev && now - h.at > 350) { h.past.push(JSON.stringify(prev)); if (h.past.length > 200) h.past.shift(); } // a drag is one step
    h.at = now;
    h.future = [];
    put(r, doc);
  };
  const step = (dir: 'undo' | 'redo') => {
    if (!current) return;
    const h = history.current[keyOf(current)];
    const cur = current.kind === 'level' ? levels[current.id] : conveyors[current.id];
    if (!h || !cur) return;
    const [from, to] = dir === 'undo' ? [h.past, h.future] : [h.future, h.past];
    const snap = from.pop();
    if (!snap) return;
    to.push(JSON.stringify(cur));
    h.at = 0;
    put(current, JSON.parse(snap));
    bump((n) => n + 1);
  };

  // ── save ──
  const saveDoc = async (r: DocRef) => {
    const text = textOf(r);
    if (text == null) return;
    if (r.kind === 'level') await api.saveLevel(r.id, text); else await api.saveConveyor(r.id, text);
    setSaved((s) => ({ ...s, [keyOf(r)]: text }));
  };
  const saveIndex = async (ord = order) => {
    const text = writeIndex(ord);
    await api.saveIndex(text);
    setSavedIndex(text);
  };
  const save = async () => {
    if (!current) return;
    try {
      let ord = order;
      if (current.kind === 'level' && !order.includes(current.id)) { ord = [...order, current.id]; setOrder(ord); }
      await saveDoc(current);
      if (writeIndex(ord) !== savedIndex) await saveIndex(ord);
      say(`Saved ${current.id}.json`);
    } catch (e: any) { say(e.message, true); }
  };
  const saveAll = async () => {
    try {
      const refs = allRefs().filter(dirty);
      for (const r of refs) await saveDoc(r);
      if (indexDirty) await saveIndex();
      say(`Saved ${refs.length} file${refs.length === 1 ? '' : 's'}${indexDirty ? ' + levels.index.json' : ''}`);
    } catch (e: any) { say(e.message, true); }
  };

  // ── keyboard ──
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const mod = e.metaKey || e.ctrlKey;
      if (!mod) return;
      const k = e.key.toLowerCase();
      if (k === 's') { e.preventDefault(); e.shiftKey ? saveAll() : save(); }
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if (k === 'z') { e.preventDefault(); step(e.shiftKey ? 'redo' : 'undo'); }
      if (k === 'y') { e.preventDefault(); step('redo'); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  // ── file ops ──
  const open = (r: DocRef) => { setCurrent(r); setTab(r.kind); };

  const askConveyorId = (suggest: string) => {
    const id = prompt('Conveyor id (file name): lowercase letters, digits, _ — e.g. oval_32', suggest)?.trim();
    if (!id) return null;
    if (!/^[a-z][a-z0-9_]{0,47}$/.test(id)) { say(`'${id}' is not a conveyor id`, true); return null; }
    if (conveyors[id]) { say(`${id} already exists`, true); return null; }
    return id;
  };

  const onNew = (kind: DocKind) => {
    if (kind === 'level') {
      const id = nextLevelId(Object.keys(levels));
      const conv = (current?.kind === 'level' && conveyors[levels[current.id]?.conveyor]) || conveyorList[0];
      if (!conv) { say('Create a conveyor first', true); return; }
      put({ kind, id }, newLevel(id, conv));
      setOrder((o) => [...o, id]);
      open({ kind, id });
    } else {
      const id = askConveyorId('custom_32');
      if (!id) return;
      put({ kind, id }, STARTER_CONVEYOR(id));
      open({ kind, id });
    }
  };

  const onDuplicate = (r: DocRef) => {
    if (r.kind === 'level') {
      const src = levels[r.id];
      const id = nextLevelId(Object.keys(levels));
      const copy: LevelFile = { ...structuredClone(src), id, meta: { ...src.meta, name: `${src.meta?.name ?? src.id} copy` } };
      put({ kind: 'level', id }, copy);
      const at = order.indexOf(r.id);
      setOrder((o) => (at >= 0 ? [...o.slice(0, at + 1), id, ...o.slice(at + 1)] : [...o, id]));
      open({ kind: 'level', id });
    } else {
      const id = askConveyorId(r.id + '_b');
      if (!id) return;
      put({ kind: 'conveyor', id }, { ...structuredClone(conveyors[r.id]), id });
      open({ kind: 'conveyor', id });
    }
  };

  const onDelete = async (r: DocRef) => {
    if (r.kind === 'conveyor') {
      const users = Object.values(levels).filter((l) => l.conveyor === r.id).map((l) => l.id);
      if (users.length) { say(`${r.id} is used by ${users.join(', ')}`, true); return; }
    }
    if (!confirm(`Delete ${r.id}.json (and its .meta) from disk? This can not be undone here (git can restore it).`)) return;
    try {
      if (saved[keyOf(r)] != null) await (r.kind === 'level' ? api.deleteLevel(r.id) : api.deleteConveyor(r.id));
      if (r.kind === 'level') {
        setLevels(({ [r.id]: _gone, ...rest }) => rest);
        const ord = order.filter((x) => x !== r.id);
        setOrder(ord);
        if (savedIndex != null && savedIndex.includes(`"${r.id}"`)) await saveIndex(ord);
      } else setConveyors(({ [r.id]: _gone, ...rest }) => rest);
      setSaved(({ [keyOf(r)]: _gone, ...rest }) => rest);
      setCurrent(null);
      say(`Deleted ${r.id}.json`);
    } catch (e: any) { say(e.message, true); }
  };

  const onMoveLevel = (id: string, dir: -1 | 1) => {
    const i = order.indexOf(id), j = i + dir;
    if (i < 0 || j < 0 || j >= order.length) return;
    const o = [...order];
    [o[i], o[j]] = [o[j], o[i]];
    setOrder(o);
  };

  const runTool = async (args: string[], title: string) => {
    if (anyDirty && confirm('LevelTool reads the files on disk. Save all changes first?')) await saveAll();
    setRun({ title, running: true });
    try {
      const r = await api.levelTool(args);
      setRun({ title, running: false, ...r });
    } catch (e: any) {
      setRun({ title, running: false, code: 2, stderr: e.message });
    }
  };

  const reload = () => { if (!anyDirty || confirm('Discard unsaved changes and reload every file from disk?')) load(); };

  // ── render ──
  const level = current?.kind === 'level' ? levels[current.id] : undefined;
  const conveyor = current?.kind === 'conveyor' ? conveyors[current.id] : undefined;
  const h = current ? history.current[keyOf(current)] : undefined;
  const curDirty = current ? dirty(current) : false;

  return (
    <div className="flex h-screen flex-col bg-slate-950">
      <header className="flex h-11 shrink-0 items-center gap-3 border-b border-slate-800 bg-slate-900 px-3">
        <div className="flex items-center gap-2">
          <div className="flex h-6 w-6 items-center justify-center rounded-full" style={{ background: 'radial-gradient(circle at 35% 30%, #FDE287 0 18%, #FBC40F 45%, #B0890B 100%)' }} />
          <span className="text-sm font-semibold tracking-tight">Cap Chaos <span className="font-normal text-slate-400">Level Designer</span></span>
        </div>
        <span className="hidden truncate font-mono text-[11px] text-slate-500 lg:block">{root}</span>
        <div className="ml-4 flex items-center gap-2 text-xs">
          {current && <span className="font-mono text-slate-200">{current.kind === 'level' ? 'LevelConfig' : 'ConveyorConfig'}/{current.id}.json</span>}
          {curDirty && <span className="rounded bg-amber-500/20 px-1.5 text-[10px] text-amber-300">unsaved</span>}
          {indexDirty && <span className="rounded bg-amber-500/20 px-1.5 text-[10px] text-amber-300">index changed</span>}
        </div>
        <div className="ml-auto flex items-center gap-1.5">
          <button className="btn" title="Undo (⌘Z)" disabled={!h?.past.length} onClick={() => step('undo')}><Undo2 size={13} /></button>
          <button className="btn" title="Redo (⇧⌘Z)" disabled={!h?.future.length} onClick={() => step('redo')}><Redo2 size={13} /></button>
          <button className="btn" title="The file as it will be written" disabled={!current} onClick={() => current && setJson({ title: `${current.id}.json`, text: textOf(current) ?? '' })}><Braces size={13} /> JSON</button>
          <div className="mx-1 h-5 w-px bg-slate-700" />
          <button className="btn btn-primary" title="Save (⌘S)" disabled={!current || (!curDirty && !indexDirty)} onClick={save}><Save size={13} /> Save</button>
          <button className="btn" title="Save all (⇧⌘S)" disabled={!anyDirty} onClick={saveAll}><SaveAll size={13} /> Save all</button>
          <div className="mx-1 h-5 w-px bg-slate-700" />
          <button className="btn" title="dotnet run --project Tools/LevelTool -- validate (V1–V6 incl. solver)" onClick={() => runTool(['validate'], 'LevelTool validate')}><ShieldCheck size={13} /> Validate</button>
          <button className="btn" title="LevelTool migrate --check: every file in the current format" onClick={() => runTool(['migrate', '--check'], 'LevelTool migrate --check')}><FlaskConical size={13} /> Format check</button>
          <button className="btn" title="Reload every file from disk" onClick={reload}><RefreshCw size={13} /></button>
        </div>
      </header>

      {loadError && <div className="border-b border-rose-900 bg-rose-950 px-4 py-1.5 text-xs text-rose-200">{loadError}</div>}

      <div className="flex min-h-0 flex-1">
        <Sidebar tab={tab} setTab={setTab} levels={levelList} conveyors={conveyorList} inIndex={new Set(order)} current={current}
          dirty={dirty} broken={broken} onOpen={open} onNew={onNew} onDuplicate={onDuplicate} onDelete={onDelete} onMoveLevel={onMoveLevel} />
        {level ? (
          <LevelEditor key={level.id} level={level} conveyors={conveyorList} problems={problems[`level:${level.id}`] ?? []}
            onChange={(l) => change({ kind: 'level', id: level.id }, l)} onOpenConveyor={(id) => open({ kind: 'conveyor', id })} />
        ) : conveyor ? (
          <ConveyorEditor key={conveyor.id} conveyor={conveyor} levels={levelList} problems={problems[`conveyor:${conveyor.id}`] ?? []}
            onChange={(c) => change({ kind: 'conveyor', id: conveyor.id }, c)} onOpenLevel={(id) => open({ kind: 'level', id })} />
        ) : (
          <div className="flex flex-1 items-center justify-center text-sm text-slate-500">Pick a level or a conveyor on the left.</div>
        )}
      </div>

      {toast && (
        <div className={`fixed bottom-4 left-1/2 z-50 -translate-x-1/2 rounded-md px-4 py-2 text-sm shadow-lg ${toast.bad ? 'bg-rose-700 text-white' : 'bg-emerald-600 text-white'}`}>{toast.text}</div>
      )}
      {json && <JsonModal title={json.title} text={json.text} onClose={() => setJson(null)} />}
      {run && <LevelToolModal run={run} onClose={() => setRun(null)} />}
    </div>
  );
}
