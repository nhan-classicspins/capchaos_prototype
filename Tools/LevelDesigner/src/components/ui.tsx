import { useEffect, useState, type ReactNode } from 'react';
import clsx from 'clsx';
import { flavor } from '../lib/colors';

export function Section({ title, actions, children, className }: { title: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={clsx('border-b border-slate-800 px-3 py-3', className)}>
      <div className="mb-2 flex items-center justify-between gap-2">
        <h3 className="label">{title}</h3>
        {actions && <div className="flex items-center gap-1">{actions}</div>}
      </div>
      {children}
    </section>
  );
}

export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <label className="block" title={hint}>
      <span className="mb-1 block text-[11px] text-slate-400">{label}</span>
      {children}
    </label>
  );
}

/** A number input that only commits finite numbers (on blur / Enter), so typing "-" or "0." never breaks the doc. */
export function NumberField({ value, onChange, min, max, step = 1, integer = true, className, placeholder }: {
  value: number | undefined; onChange: (v: number) => void; min?: number; max?: number; step?: number; integer?: boolean; className?: string; placeholder?: string;
}) {
  const [text, setText] = useState(value == null ? '' : String(value));
  useEffect(() => setText(value == null ? '' : String(value)), [value]);
  const commit = () => {
    let v = Number(text);
    if (text.trim() === '' || !Number.isFinite(v)) { setText(value == null ? '' : String(value)); return; }
    if (integer) v = Math.round(v);
    if (min != null) v = Math.max(min, v);
    if (max != null) v = Math.min(max, v);
    setText(String(v));
    if (v !== value) onChange(v);
  };
  return (
    <input
      className={clsx('input font-mono', className)}
      type="number"
      value={text}
      step={step}
      min={min}
      max={max}
      placeholder={placeholder}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
    />
  );
}

export function TextField({ value, onChange, placeholder, multiline }: { value: string; onChange: (v: string) => void; placeholder?: string; multiline?: boolean }) {
  const [text, setText] = useState(value);
  useEffect(() => setText(value), [value]);
  const commit = () => text !== value && onChange(text);
  return multiline ? (
    <textarea className="input min-h-[56px] resize-y" value={text} placeholder={placeholder} onChange={(e) => setText(e.target.value)} onBlur={commit} />
  ) : (
    <input className="input" value={text} placeholder={placeholder} onChange={(e) => setText(e.target.value)} onBlur={commit}
      onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }} />
  );
}

/** A round colour chip in the game's flavour colours (body fill, shade rim, cap highlight). */
export function Swatch({ color, size = 18, selected, onClick, title, hidden, dim }: {
  color: number; size?: number; selected?: boolean; onClick?: () => void; title?: string; hidden?: boolean; dim?: boolean;
}) {
  const f = flavor(color);
  const empty = color === 0;
  const props = {
    title: title ?? (empty ? '0 · empty spot' : `${color} · ${f.name}`),
    className: clsx('relative inline-block shrink-0 rounded-full transition-transform', onClick && 'hover:scale-110', selected && 'ring-2 ring-white ring-offset-2 ring-offset-slate-900', dim && 'opacity-30'),
    style: {
      width: size, height: size,
      background: empty ? 'transparent' : hidden ? '#787878' : `radial-gradient(circle at 35% 30%, ${f.cap} 0 18%, ${f.body} 45%, ${f.shade} 100%)`,
      border: empty ? '1.5px dashed #64748b' : `1px solid ${hidden ? '#555' : f.shade}`,
    },
  };
  // a swatch with no action is a span, so it can sit inside another button
  return onClick ? <button type="button" onClick={onClick} {...props} /> : <span {...props} />;
}

export function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <label className="flex cursor-pointer select-none items-center gap-2 text-xs text-slate-300">
      <input type="checkbox" className="accent-sky-500" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  );
}

export function Segmented<T extends string | number>({ value, options, onChange }: { value: T; options: { value: T; label: ReactNode; title?: string }[]; onChange: (v: T) => void }) {
  return (
    <div className="inline-flex overflow-hidden rounded border border-slate-700">
      {options.map((o) => (
        <button key={String(o.value)} type="button" title={o.title} onClick={() => onChange(o.value)}
          className={clsx('px-2 py-0.5 text-xs', o.value === value ? 'bg-sky-600 text-white' : 'bg-slate-800 text-slate-300 hover:bg-slate-700')}>
          {o.label}
        </button>
      ))}
    </div>
  );
}
