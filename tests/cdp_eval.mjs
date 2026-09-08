// Only connects to the isolated QA vault on localhost. No user vault is inspected.
import fs from 'node:fs';
const browser = process.argv[2] === 'browser';
const pages = await (await fetch(`http://127.0.0.1:${browser ? 9238 : 9237}/json`)).json();
const page = pages.find(p => p.type === 'page' && p.title.includes(browser ? 'PointCursor Browser QA' : 'ObsidianTestVault'));
if (!page) throw new Error('Isolated QA page is not open');
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
const expression = fs.readFileSync(0, 'utf8');
const result = await new Promise((resolve, reject) => {
  const timeout = setTimeout(() => reject(new Error('CDP timeout')), 10000);
  ws.onmessage = event => {
    const response = JSON.parse(event.data);
    if (response.id === 1) { clearTimeout(timeout); resolve(response); }
  };
  ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression, awaitPromise: true, returnByValue: true } }));
});
ws.close();
if (result.result?.exceptionDetails) throw new Error(JSON.stringify(result.result.exceptionDetails));
console.log(JSON.stringify(result.result?.result?.value ?? result));
