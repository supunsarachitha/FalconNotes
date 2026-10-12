// The check that an update keeps every note (docs/11, Updates keep every note): walks the app's pages on the
// emulator or phone and writes down what each one shows, so the same walk before and after installing a new version
// over an old one can be compared with compare.mjs.
//
//   node scripts/update-check/inventory.mjs before.json     (the old version running, Debug build)
//   adb install -r <the new version's APK>, start the app
//   node scripts/update-check/inventory.mjs after.json
//   node scripts/update-check/compare.mjs before.json after.json
//
// It reads the page through the WebView's DevTools socket, which only Debug builds open, and needs Node 22 or later
// and adb. Cards are read by textContent: `content-visibility` leaves innerText empty for cards off the screen.
// The files hold note text: keep them out of the repository and delete them afterwards.
import { execSync } from "node:child_process";
import { writeFileSync } from "node:fs";
const adb = `${process.env.HOME}/Library/Android/sdk/platform-tools/adb`;
const pid = execSync(`${adb} shell pidof lk.stechbuzz.falconnotes`).toString().trim();
execSync(`${adb} forward tcp:9222 localabstract:webview_devtools_remote_${pid}`);
const page = (await (await fetch("http://127.0.0.1:9222/json")).json()).find(p => p.type === "page");
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((ok, fail) => { ws.onopen = ok; ws.onerror = fail; });
let next = 0;
const pending = new Map();
ws.onmessage = m => { const d = JSON.parse(m.data); pending.get(d.id)?.(d); };
const evaluate = expression => new Promise((ok, fail) => {
  const id = ++next;
  pending.set(id, d => d.result.exceptionDetails ? fail(new Error(JSON.stringify(d.result.exceptionDetails))) : ok(d.result.result.value));
  ws.send(JSON.stringify({ id, method: "Runtime.evaluate", params: { expression, awaitPromise: true, returnByValue: true } }));
});
const walk = route => `(async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  Blazor.navigateTo(${JSON.stringify(route)});
  await sleep(2500);
  const root = () => document.querySelector("main") ?? document.body;
  let last = -1, same = 0;
  while (same < 3) {
    window.scrollTo(0, document.body.scrollHeight);
    root().scrollTo?.(0, root().scrollHeight);
    [...document.querySelectorAll("button")].find(b => b.textContent.trim() === "Load more")?.click();
    await sleep(900);
    const n = root().textContent.length;
    same = n === last ? same + 1 : 0;
    last = n;
  }
  return {
    route: ${JSON.stringify(route)},
    cards: [...document.querySelectorAll("article")].map(a => a.textContent.replace(/\\s+/g, " ").trim()),
    text: root().textContent.replace(/\\s+/g, " ").trim(),
    images: document.querySelectorAll("article img").length,
  };
})()`;
const routes = ["/", "/todo", "/quick", "/habits", "/archive", "/trash", "/tags", "/settings/account", "/settings/labels", "/settings/security", "/settings/data"];
const out = [];
for (const route of routes) {
  const r = await evaluate(walk(route));
  out.push(r);
  console.log(route.padEnd(20), String(r.cards.length).padStart(3), "cards", String(r.images).padStart(3), "images", r.text.length, "chars");
}
out.push({ route: "localStorage", text: await evaluate("JSON.stringify(Object.fromEntries(Object.entries(localStorage)))"), cards: [] });
writeFileSync(process.argv[2], JSON.stringify(out, null, 1));
ws.close();
