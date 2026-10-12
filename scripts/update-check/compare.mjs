// Compares two walks made by inventory.mjs, before and after an update (docs/11, Updates keep every note): every card
// on every page, the storage line with its counts of notes and files, the tags and the saved appearance. Exits with
// 1 when anything differs. Relative times ("1 minute ago") on notes written minutes before do differ: read those
// lines, and nothing else should be listed.
//
//   node scripts/update-check/compare.mjs before.json after.json
import { readFileSync } from "node:fs";
const [a, b] = process.argv.slice(2, 4).map(f => JSON.parse(readFileSync(f)));
let bad = 0;
const storage = d => d.find(r => r.route === "/settings/account").text.match(/Storage used.*?Files [^)]*\)/)?.[0];
const tags = d => d.find(r => r.route === "/tags").text.replace(/^.*?Most used/, "");
for (const r of a) {
  const other = b.find(x => x.route === r.route);
  const same = JSON.stringify(r.cards) === JSON.stringify(other.cards);
  if (!same) {
    bad++;
    r.cards.forEach((c, i) => { if (c !== other.cards[i]) console.log(`  ${r.route} card ${i}\n   before: ${c.slice(0, 200)}\n   after:  ${(other.cards[i] ?? "(missing)").slice(0, 200)}`); });
  }
  console.log(r.route.padEnd(20), `${r.cards.length} -> ${other.cards.length} cards`, same ? "identical" : "DIFFERENT");
}
for (const [name, f] of [["storage", storage], ["tags", tags], ["appearance", d => d.at(-1).text]]) {
  const same = f(a) === f(b);
  if (!same) bad++;
  console.log(name.padEnd(20), same ? "identical" : "DIFFERENT", "|", f(b).slice(0, 110));
}
process.exit(bad ? 1 : 0);
