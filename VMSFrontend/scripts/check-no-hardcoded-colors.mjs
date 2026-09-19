// Fails when a colour literal appears anywhere in src/ outside src/styles/tokens/.
//
// Why: the app has two themes, each with a light and a dark mode. A colour written directly in a
// component would look right in one combination and wrong in the other three. All colours belong
// in the token files; components use var(--vms-*).
//
// Run by hand:  npm run check:colors       (also runs automatically before start and build)

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const src = fileURLToPath(new URL('../src', import.meta.url));
const tokens = join(src, 'styles', 'tokens');
const extensions = new Set(['.ts', '.html', '.scss', '.css']);

const NAMED = (
  'aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown ' +
  'burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan ' +
  'darkgoldenrod darkgray darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid ' +
  'darkred darksalmon darkseagreen darkslateblue darkslategray darkslategrey darkturquoise darkviolet ' +
  'deeppink deepskyblue dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro ' +
  'ghostwhite gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki ' +
  'lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan lightgoldenrodyellow ' +
  'lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray ' +
  'lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine ' +
  'mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen ' +
  'mediumturquoise mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace ' +
  'olive olivedrab orange orangered orchid palegoldenrod palegreen paleturquoise palevioletred ' +
  'papayawhip peachpuff peru pink plum powderblue purple rebeccapurple red rosybrown royalblue ' +
  'saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue slateblue slategray slategrey ' +
  'snow springgreen steelblue tan teal thistle tomato turquoise violet wheat white whitesmoke yellow ' +
  'yellowgreen'
).split(' ');

const namedRe = new RegExp(`\\b(?:${NAMED.join('|')})\\b`, 'i');
const hexRe = /(^|[^\w&#-])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})(?![\w-])/;
const fnRe = /\b(?:rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch|color)\(/i;
// A colour-bearing CSS property, then its value (which may sit inside quotes in a TS style object).
const declRe =
  /(?:^|[\s;{"'])(?:color|background(?:-color|-image)?|border(?:-(?:top|right|bottom|left))?(?:-color)?|outline(?:-color)?|fill|stroke|box-shadow|text-shadow|caret-color|accent-color|text-decoration(?:-color)?|column-rule(?:-color)?)\s*:\s*([^;}]*)/g;

function* walk(dir) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (full === tokens) continue;
    if (statSync(full).isDirectory()) yield* walk(full);
    else if ([...extensions].some((e) => full.endsWith(e))) yield full;
  }
}

const problems = [];
for (const file of walk(src)) {
  readFileSync(file, 'utf8')
    .split(/\r?\n/)
    .forEach((line, i) => {
      const hits = [];
      const hex = line.match(hexRe);
      if (hex) hits.push(hex[0].trim());
      const fn = line.match(fnRe);
      if (fn) hits.push(fn[0]);
      for (const m of line.matchAll(declRe)) {
        const value = m[1].replace(/var\([^)]*\)/g, '');
        const named = value.match(namedRe);
        if (named) hits.push(named[0]);
      }
      for (const h of new Set(hits)) problems.push(`${relative(src, file)}:${i + 1}  ${h}`);
    });
}

if (problems.length) {
  console.error('\nHard-coded colours found. Use a var(--vms-*) token instead (colours live in src/styles/tokens/):\n');
  for (const p of problems) console.error('  ' + p);
  console.error(`\n${problems.length} problem(s).\n`);
  process.exit(1);
}
console.log('No hard-coded colours outside src/styles/tokens/.');
