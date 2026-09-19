// Compiles the token files, resolves every token for each theme and mode (a/b x light/dark) and
// checks WCAG contrast for the pairs the UI actually uses. Also checks that every theme and mode
// defines the same set of tokens, so a component never meets an undefined variable.
//
// Run:  npm run check:contrast

import { fileURLToPath } from 'node:url';
import * as sass from 'sass';

const tokensDir = fileURLToPath(new URL('../src/styles/tokens', import.meta.url));
const css = sass.compileString(
  "@use 'shared'; @use 'theme-a'; @use 'theme-b';",
  { loadPaths: [tokensDir] },
).css;

// selector -> { --prop: value }
const blocks = [];
for (const m of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
  const props = {};
  for (const d of m[2].matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) props[d[1]] = d[2].trim();
  blocks.push({ selector: m[1].trim(), props });
}

const has = (sel, theme, mode) => {
  const t = new RegExp(`data-theme=['"]?${theme}['"]?`).test(sel);
  const mo = new RegExp(`data-mode=['"]?${mode}['"]?`).test(sel);
  if (mode === null) return t && !/data-mode/.test(sel);
  return t && mo;
};

function tokensFor(theme, mode) {
  const out = {};
  for (const b of blocks) {
    const shared = b.selector === ':root';
    if (shared || has(b.selector, theme, null) || has(b.selector, theme, mode)) Object.assign(out, b.props);
  }
  return out;
}

// ---- colour maths -------------------------------------------------------------------------
function resolveVars(value, vars, depth = 0) {
  if (depth > 12) throw new Error('var() cycle in ' + value);
  return value.replace(/var\((--[\w-]+)\)/g, (_, name) => {
    if (!(name in vars)) throw new Error('undefined token ' + name);
    return resolveVars(vars[name], vars, depth + 1);
  });
}

function parseHex(h) {
  let s = h.replace('#', '');
  if (s.length === 3) s = [...s].map((c) => c + c).join('');
  return { r: parseInt(s.slice(0, 2), 16), g: parseInt(s.slice(2, 4), 16), b: parseInt(s.slice(4, 6), 16), a: 1 };
}

function parseColor(expr) {
  const e = expr.trim();
  if (e === 'transparent') return { r: 0, g: 0, b: 0, a: 0 };
  if (e.startsWith('#')) return parseHex(e);
  const mix = e.match(/^color-mix\(in srgb,\s*(#[0-9a-fA-F]{3,6})\s+(\d+(?:\.\d+)?)%,\s*transparent\)$/);
  if (mix) return { ...parseHex(mix[1]), a: Number(mix[2]) / 100 };
  throw new Error('cannot parse colour: ' + e);
}

const over = (fg, bg) => ({
  r: fg.r * fg.a + bg.r * (1 - fg.a),
  g: fg.g * fg.a + bg.g * (1 - fg.a),
  b: fg.b * fg.a + bg.b * (1 - fg.a),
  a: 1,
});

const lum = ({ r, g, b }) => {
  const f = (v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
};
const ratio = (a, b) => {
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

// ---- checks -------------------------------------------------------------------------------
const STATUSES = ['success', 'warning', 'danger', 'info', 'neutral'];
const failures = [];
const rows = [];
let parityReference = null;

for (const theme of ['a', 'b']) {
  for (const mode of ['light', 'dark']) {
    const vars = tokensFor(theme, mode);
    const label = `theme ${theme} / ${mode}`;

    // Same token set everywhere (ignoring the raw ramps, which differ by design).
    const semantic = Object.keys(vars).filter((k) => !/^--vms-(neutral|primary|red|orange|amber|yellow|green|sky|blue|purple)-\d+$/.test(k)).sort();
    if (!parityReference) parityReference = { label, keys: semantic };
    else {
      const missing = parityReference.keys.filter((k) => !semantic.includes(k));
      const extra = semantic.filter((k) => !parityReference.keys.includes(k));
      if (missing.length || extra.length) failures.push(`${label}: token set differs from ${parityReference.label} (missing: ${missing.join(', ') || 'none'}; extra: ${extra.join(', ') || 'none'})`);
    }

    const get = (name) => parseColor(resolveVars(vars['--vms-' + name], vars));
    const solid = (name, base) => over(get(name), base);
    const surface = get('surface');
    const bg = get('bg');
    const soft = get('surface-soft');
    const chrome = get('chrome-bg');

    const check = (what, fg, back, min) => {
      const r = ratio(fg, back);
      rows.push({ label, what, r, min, ok: r >= min });
      if (r < min) failures.push(`${label}: ${what} is ${r.toFixed(2)}:1, needs ${min}:1`);
    };

    for (const [n, base] of [['bg', bg], ['surface', surface], ['surface-soft', soft]]) {
      check(`text on ${n}`, get('text'), base, 4.5);
      check(`muted on ${n}`, get('muted'), base, 4.5);
    }
    check('brand-text on surface', get('brand-text'), surface, 4.5);
    check('brand-text on bg', get('brand-text'), bg, 4.5);
    check('brand-text on brand-tint', get('brand-text'), solid('brand-tint', surface), 4.5);
    check('on-brand on brand', get('on-brand'), get('brand'), 4.5);
    check('on-brand on brand-hover', get('on-brand'), get('brand-hover'), 4.5);
    check('focus-ring on surface', get('focus-ring'), surface, 3);
    check('brand on surface (non-text)', get('brand'), surface, 3);

    check('chrome-strong on chrome', get('chrome-strong'), chrome, 4.5);
    check('chrome-text on chrome', get('chrome-text'), chrome, 4.5);
    check('chrome-muted on chrome', get('chrome-muted'), chrome, 4.5);
    const activeBg = solid('chrome-active-bg', chrome);
    check('chrome-active-text on chrome-active-bg', get('chrome-active-text'), activeBg, 4.5);
    check('chrome-accent on chrome (non-text)', get('chrome-accent'), chrome, 3);

    for (const s of STATUSES) {
      const fill = solid(`${s}-bg`, surface);
      check(`${s}-text on ${s}-bg (over surface)`, get(`${s}-text`), fill, 4.5);
      check(`${s}-text on ${s}-bg (over page bg)`, get(`${s}-text`), solid(`${s}-bg`, bg), 4.5);
    }
    for (const s of ['success', 'warning', 'danger']) {
      check(`${s} on surface`, get(s), surface, 4.5);
      check(`${s} on bg`, get(s), bg, 4.5);
    }
  }
}

const worst = rows.reduce((m, r) => (r.r - r.min < m.r - m.min ? r : m));
console.log(`${rows.length} contrast checks across 4 theme/mode combinations.`);
console.log(`Tightest margin: ${worst.label}: ${worst.what} = ${worst.r.toFixed(2)}:1 (needs ${worst.min}:1)`);
if (failures.length) {
  console.error('\nFAILED:\n' + failures.map((f) => '  - ' + f).join('\n'));
  process.exit(1);
}
console.log('All checks pass.');
