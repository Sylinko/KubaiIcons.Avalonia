// Reference images use the upstream renderer and target interpolation, never the scene compiler.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const [source, destination] = process.argv.slice(2);
const load = f => import(pathToFileURL(path.resolve(source, f)));
const { renderSVG } = await load('packages/core/src/render.js');
const { interp, EASE, PHASE } = await load('packages/core/src/motion.js');
const steps = [
    [1, 0, 0.28],
    [0, 0, 0.13],
    [1, 0, 0.42],
    [1, 1, 0.22],
    [0, 1, 0.37],
    [0, 1, 1],
    [0, 0, 0.16],
    [1, 1, 0.33],
];
let html =
    '<body style="margin:0;background:#192637"><svg xmlns="http://www.w3.org/2000/svg" width="992" height="496">';
for (let row = 0; row < 4; row++) {
    const d = (await load('icons/' + (row % 2 ? 'bookmark' : 'folder') + '.js')).default;
    let p = d.target(false, { active: false });
    for (const [i, [on, active, t]] of steps.entries()) {
        const e = EASE(t);
        p = interp(p, d.target(!!on, { active: !!active }), e);
        const snapshot = {
            ...d,
            draw(_, els, inst) {
                globalThis[PHASE] = { out: !on, e };
                try {
                    d.draw(p, els, inst);
                } finally {
                    delete globalThis[PHASE];
                }
            },
        };
        html += renderSVG(snapshot, {
            k: 'ref' + row + 'x' + i,
            pad: 0,
            size: 124,
            accent: '#5393FF',
            variant: row < 2 ? 'color' : 'soft',
        }).replace('<svg ', '<svg x="' + i * 124 + '" y="' + row * 124 + '" ');
    }
}
fs.writeFileSync(destination, html + '</svg></body>');
