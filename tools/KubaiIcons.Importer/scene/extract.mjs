import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { compileScene } from './compile.mjs';

const [source, ...names] = process.argv.slice(2);
const load = f => import(pathToFileURL(path.resolve(source, f)));
const api = {
    ...(await load('packages/core/src/render.js')),
    ...(await load('packages/core/src/motion.js')),
    ...(await load('packages/core/src/kit.js')),
};
const selection = names.length
    ? names
    : fs
          .readdirSync(path.join(source, 'icons'))
          .filter(f => /^[a-z0-9-]+\.js$/.test(f))
          .sort()
          .map(f => f.slice(0, -3));
if (!names.length) {
    selection.push('overlay-slash');
}
for (const name of selection) {
    const def = (await load(name === 'overlay-slash' ? 'badges/_slash.js' : 'icons/' + name + '.js')).default;
    try {
        const block = compileScene(def, api);
        console.log(JSON.stringify([name, def.zh, block.toString('base64')]));
    } catch (e) {
        throw new Error(name + ': ' + e.message, { cause: e });
    }
}
