// Independent raster reference: this entry point uses only the pinned author's renderer.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const [source, destination] = process.argv.slice(2);
const load = f => import(pathToFileURL(path.resolve(source, f)));
const { renderSVG } = await load('packages/core/src/render.js');
const { withBadge } = await load('packages/core/src/compose.js');
const { EASE, PHASE } = await load('packages/core/src/motion.js');
const { default: folder } = await load('icons/folder.js');
const { default: alert } = await load('badges/alert.js');
const def = withBadge(folder, alert);
const size = 155;
const frames = [
    [0, 0],
    [0, 0.16],
    [0, 0.34],
    [0, 0.54],
    [0, 0.75],
    [0, 1],
    [1, 0.2],
    [1, 0.55],
    [0, 0.3],
    [EASE(0.3), 0.1],
    [EASE(0.3), 0.25],
    [EASE(0.3), 0.8],
];
let sheet =
    '<svg xmlns="http://www.w3.org/2000/svg" width="620" height="465">' +
    '<defs><pattern id="board" width="20" height="20" patternUnits="userSpaceOnUse">' +
    '<rect width="20" height="20" fill="#192637"/>' +
    '<path d="M0 0h10v10H0zM10 10h10v10H10z" fill="#403149"/></pattern></defs>';
for (const [i, [start, time]] of frames.entries()) {
    const e = EASE(time);
    const out = start > 0;
    const h = start + ((out ? 0 : 1) - start) * e;
    globalThis[PHASE] = { out, e };
    const x = size * (i % 4);
    const y = size * Math.floor(i / 4);
    sheet +=
        '<g transform="translate(' +
        x +
        ' ' +
        y +
        ')"><rect width="155" height="155" fill="url(#board)"/>' +
        renderSVG(def, { k: 'ref' + i, size, pad: 0, progress: h, badge: '#F01487' }) +
        '</g>';
}
delete globalThis[PHASE];
fs.writeFileSync(destination, '<!doctype html><body style="margin:0">' + sheet + '</svg></body>');
