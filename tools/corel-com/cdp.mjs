// Drives the REAL Mantos Extract page inside CorelDRAW through the WebView2 DevTools port (dev
// machine only — needs %LOCALAPPDATA%\MantosExtract\dev-tools.on before CorelDRAW starts;
// Deploy-MantosExtract.ps1 -DevTools creates it; see docs/COREL-AUTOMACAO.md). Node 22+ (global
// WebSocket/fetch), no packages. Ported from siscut/tools/corel-com/cdp.mjs (2026-10-13). Ports:
// AiSten 9477, SisCut 9478, Mantos Extract 9479 (MantosExtractDevTools.Port).
//
//   node cdp.mjs targets                        list pages
//   node cdp.mjs eval "<js expression>"         run in the Mantos Extract page, print the JSON result
//   node cdp.mjs wait "<js boolean>" [seconds]  poll until true (exit 1 on timeout)
//   node cdp.mjs shot <file.png>                screenshot of the Mantos Extract page
//   Options: --port 9479
//
// C#→JS messages arrive in window.mantosExtractReceive(msg); to wait for one, wrap it and keep
// the messages (see Test-StraightAngleE2E.ps1). JS→C#: window.chrome.webview.postMessage(obj).

const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf('--' + name); if (i < 0) return def; const v = args[i + 1]; args.splice(i, 2); return v; };
const port = Number(opt('port', '9479'));
const [cmd, ...rest] = args;

async function targets() {
  const res = await fetch(`http://127.0.0.1:${port}/json/list`);
  return (await res.json()).filter(t => t.type === 'page');
}

async function connect() {
  const list = await targets();
  const t = list.find(x => x.url.startsWith('https://mantosextract.app/'));
  if (!t) throw new Error(`página do Mantos Extract não encontrada. Abertas: ${list.map(x => x.url).join(', ') || '(nenhuma)'}`);
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = () => fail(new Error('falha ao conectar no DevTools')); });
  let seq = 0; const pending = new Map();
  ws.onmessage = ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  const send = (method, params = {}) => new Promise(ok => { const id = ++seq; pending.set(id, ok); ws.send(JSON.stringify({ id, method, params })); });
  return { send, close: () => ws.close() };
}

async function evaluate(c, expression) {
  const r = await c.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true, userGesture: true });
  if (r.error) throw new Error(r.error.message);
  if (r.result.exceptionDetails) throw new Error('JS: ' + (r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text));
  return r.result.result.value;
}

async function waitFor(c, expr, seconds) {
  const until = Date.now() + seconds * 1000;
  while (Date.now() < until) {
    try { if (await evaluate(c, `!!(${expr})`)) return true; } catch { /* page reloading */ }
    await new Promise(r => setTimeout(r, 400));
  }
  return false;
}

(async () => {
  if (cmd === 'targets') { for (const t of await targets()) console.log(t.url + '  (' + t.title + ')'); return; }
  const c = await connect();
  try {
    if (cmd === 'eval') {
      console.log(JSON.stringify(await evaluate(c, rest.join(' ')), null, 2));
    } else if (cmd === 'wait') {
      const ok = await waitFor(c, rest[0], Number(rest[1] || 20));
      console.log(ok ? 'ok' : 'tempo esgotado'); process.exitCode = ok ? 0 : 1;
    } else if (cmd === 'shot') {
      const r = await c.send('Page.captureScreenshot', { format: 'png' });
      const fs = await import('node:fs');
      fs.writeFileSync(rest[0], Buffer.from(r.result.data, 'base64'));
      console.log('salvo: ' + rest[0]);
    } else {
      console.log('uso: node cdp.mjs targets | eval "<js>" | wait "<js>" [s] | shot <png>  [--port 9479]');
      process.exitCode = 2;
    }
  } finally { c.close(); }
})().catch(e => { console.error('ERRO: ' + e.message); process.exitCode = 1; });
