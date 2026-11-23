// Compose exactly the variants declared by the pinned author. No name-derived variants.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { extractCompositions } from './extract-compositions.mjs';

const [source] = process.argv.slice(2);
const load = f => import(pathToFileURL(path.resolve(source, f)));
const pairs = [];
const activeBases = [];
const overlays = [];
const slash = (await load('badges/_slash.js')).default;
for (const name of fs
    .readdirSync(path.join(source, 'icons'))
    .filter(f => /^[a-z0-9-]+\.js$/.test(f))
    .sort()) {
    const def = (await load('icons/' + name)).default;
    if (def.name !== name.slice(0, -3)) {
        throw Error('Mismatched base ' + name);
    }
    if (def.tap) {
        activeBases.push(def.name);
    }
    for (const slug of def.variants ?? []) {
        pairs.push({ baseName: def.name, slug });
    }
    if (def.off) {
        overlays.push({
            name: def.name + '-off',
            base: def.name,
            badge: 'overlay-slash',
            overlay: true,
            x: 0,
            y: 0,
            label: def.zh + '·' + slash.zh,
        });
    }
}
const data = await extractCompositions(load, pairs);
data.entries.push(...overlays);
console.log(JSON.stringify({ ...data, activeBases }));
