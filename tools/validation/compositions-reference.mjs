// Independent batch reference. The published manifest supplies the base/badge
// relationship; neither the extractor nor its compiled motion tracks run here.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const [source, manifest, destination] = process.argv.slice(2);
const load = f => import(pathToFileURL(path.resolve(source, f)));
const { renderSVG } = await load('packages/core/src/render.js');
const { withBadge } = await load('packages/core/src/compose.js');
const { EASE, PHASE } = await load('packages/core/src/motion.js');
const names = JSON.parse(fs.readFileSync('tests/KubaiIcons.Tests/Contracts/compositions-reference.json'));
const published = new Map(JSON.parse(fs.readFileSync(manifest)).map(e => [e.name, e]));
const side = 124;
let sheet =
    '<svg xmlns="http://www.w3.org/2000/svg" width="744" height="744"><rect width="100%" height="100%" fill="#192637"/>';
for (const [i, name] of names.entries()) {
    const { base, badge } = published.get(name);
    const def = withBadge(
        (await load('icons/' + base + '.js')).default,
        (await load('badges/' + badge + '.js')).default,
    );
    const e = EASE([0.07, 0.26, 0.54][i % 3]);
    globalThis[PHASE] = { out: false, e };
    sheet += renderSVG(def, {
        k: 'ref' + i,
        size: side,
        pad: 0,
        progress: e,
        state: { active: true },
        badge: '#F01487',
    }).replace('<svg ', '<svg x="' + (i % 6) * side + '" y="' + Math.floor(i / 6) * side + '" ');
}
delete globalThis[PHASE];
fs.writeFileSync(destination, '<!doctype html><body style="margin:0">' + sheet + '</svg></body>');
