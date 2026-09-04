// check-ui-js.js — static sanity check of the WebView2 page's JavaScript.
//
// WHY THIS EXISTS. Optimus's docker once called esc() 34 times and defined it ZERO
// times: every render function threw ReferenceError on its first call and died
// silently, for weeks, while 414 C# unit tests stayed green the whole time because
// none of them can see JavaScript (O18, ../CLAUDE.md). This closes exactly that gap:
// parses the page's script and fails the build on a syntax error, a call to a
// function nobody defines, or an on*= handler in the markup with no matching
// function. Mirrors optimus/scripts/check-ui-js.js structurally (not shared —
// each addin owns its copy).
//
//   node scripts/check-ui-js.js
//
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const PAGES = [
  'src/MantosExtract.AddIn/wwwroot/index.html',
  'installer/wwwroot/index.html',
];

// Provided by the host before the page script runs (I18nScript.Build) or by the browser.
const HOST_GLOBALS = new Set([
  'T', 'applyI18n', 'MANTOSEXTRACT_I18N', 'MANTOSEXTRACT_LANG', 'MANTOSEXTRACT_LANGS',
  'MANTOSEXTRACT_LANG_KEY', 'mantosExtractReceive',
]);
const BROWSER_GLOBALS = new Set([
  'window', 'document', 'console', 'navigator', 'location', 'chrome', 'JSON', 'Math', 'Date',
  'Number', 'String', 'Boolean', 'Array', 'Object', 'RegExp', 'Error', 'Promise', 'Set', 'Map',
  'parseInt', 'parseFloat', 'isNaN', 'isFinite', 'encodeURIComponent', 'decodeURIComponent',
  'setTimeout', 'setInterval', 'clearTimeout', 'clearInterval', 'requestAnimationFrame',
  'Blob', 'FileReader', 'alert',
]);

let failures = 0;
const root = path.resolve(__dirname, '..');

for (const rel of PAGES) {
  const file = path.join(root, rel);
  if (!fs.existsSync(file)) continue;

  const html = fs.readFileSync(file, 'utf8');

  const dupClass = [...html.matchAll(/<\w+[^>]*?\sclass="[^"]*"[^>]*?\sclass="[^"]*"[^>]*>/g)];
  if (dupClass.length) {
    console.error(`FALHOU  ${rel}: ${dupClass.length} elemento(s) com class= duplicado`);
    dupClass.slice(0, 3).forEach(m => console.error(`          ${m[0].slice(0, 90)}`));
    failures++;
    continue;
  }

  const blocks = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  if (blocks.length === 0) { console.log(`- ${rel}: sem <script>, ignorado`); continue; }
  const js = blocks.join('\n;\n');

  // 1. Syntax. A stray brace used to be discoverable only by loading CorelDRAW.
  try {
    new vm.Script(js, { filename: rel });
  } catch (e) {
    console.error(`FALHOU  ${rel}: erro de sintaxe — ${e.message}`);
    failures++;
    continue;
  }

  // 2. Every called name must be declared somewhere, or be a known global.
  const declared = new Set();
  for (const m of js.matchAll(/function\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
  for (const m of js.matchAll(/(?:var|let|const)\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
  for (const m of js.matchAll(/window\.([A-Za-z_$][\w$]*)\s*=/g)) declared.add(m[1]);
  for (const m of js.matchAll(/function\s*[A-Za-z_$\w]*\s*\(([^)]*)\)/g)) {
    for (const p of m[1].split(',')) {
      const name = p.trim().split(/[=\s]/)[0];
      if (/^[A-Za-z_$][\w$]*$/.test(name)) declared.add(name);
    }
  }
  for (const m of js.matchAll(/catch\s*\(\s*([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);

  // Comments and string literals must NOT be scanned for calls: prose like
  // "a peça (ex.: logo(2))" would otherwise read as a call to `logo()`.
  const code = js
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/(^|[^:\\])\/\/[^\n]*/g, '$1')
    .replace(/'(?:\\.|[^'\\])*'/g, "''")
    .replace(/"(?:\\.|[^"\\])*"/g, '""');

  const called = new Map();
  const lines = code.split('\n');
  lines.forEach((line, i) => {
    for (const m of line.matchAll(/(^|[^.\w$])([A-Za-z_$][\w$]*)\s*\(/g)) {
      const name = m[2];
      if (/^(if|for|while|switch|catch|return|typeof|function|new|else|do|delete|void|in|of|case|throw)$/.test(name)) continue;
      if (!called.has(name)) called.set(name, i + 1);
    }
  });

  const missing = [];
  for (const [name, line] of called) {
    if (declared.has(name) || HOST_GLOBALS.has(name) || BROWSER_GLOBALS.has(name)) continue;
    missing.push(`${name}() — primeira chamada na linha ${line} do script`);
  }

  // 3. Every onclick/oninput/onchange handler in the markup must resolve to a declared function.
  const handlers = new Set();
  for (const m of html.matchAll(/\son(?:click|input|change)\s*=\s*"([A-Za-z_$][\w$]*)\s*\(/g)) {
    handlers.add(m[1]);
  }
  for (const name of handlers) {
    if (declared.has(name) || HOST_GLOBALS.has(name) || BROWSER_GLOBALS.has(name)) continue;
    missing.push(`${name}() — usado num on*= do HTML, mas nao existe no script`);
  }

  if (missing.length) {
    console.error(`FALHOU  ${rel}: ${missing.length} funcao(oes) nao definida(s):`);
    for (const m of missing) console.error(`          ${m}`);
    failures++;
  } else {
    console.log(`ok      ${rel}  (${declared.size} declaracoes, ${called.size} chamadas)`);
  }
}

if (failures) {
  console.error(`\n${failures} pagina(s) com problema. O build nao deve seguir.`);
  process.exit(1);
}
console.log('\nUI JavaScript ok.');
