// Independent oracles: the published package defines availability, the upstream engine defines poses.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';

const [mode, source, metadata] = process.argv.slice(2);
assert.ok(['check', 'generate'].includes(mode), 'Expected check|generate <upstream> <metadata>');
const read = async file => JSON.parse(await fs.readFile(file, 'utf8'));
const pin = await read('catalog/upstream.json');
const commit = execFileSync('git', ['-C', source, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
assert.equal(commit, pin.commit);
for (const file of [path.join(metadata, 'package.json'), path.join(source, 'packages/core/package.json')]) {
    const pkg = await read(file);
    assert.equal(pkg.name + '@' + pkg.version, pin.version);
}
const layered = await read(path.join(metadata, 'layered-icons.json'));
const duo = await read(path.join(metadata, 'duotone-icons.json'));
const names = entries => entries.map(row => row.name).sort();
const bases = names(layered.filter(row => !row.base));
const compositions = names(layered.filter(row => row.base));
assert.deepEqual(await read('catalog/layered-bases.json'), bases, 'Layered bases differ from npm');
assert.deepEqual(await read('catalog/layered-compositions.json'), compositions, 'Layered compositions differ from npm');
const badges = [...new Set(layered.filter(row => row.base).map(row => row.badge))];
const categories = await read(path.join(source, 'icons/categories.json'));
const duotone = names(duo);
assert.equal(new Set(duotone).size, pin.duotoneCount);
assert.equal(duotone.length, pin.duotoneCount);
assert.equal(new Set(names(layered)).size, pin.layeredCount);
assert.equal(layered.length, pin.layeredCount);
const sources = (await fs.readdir(path.join(source, 'icons-duotone')))
    .filter(name => name.endsWith('.json')).map(name => name.slice(0, -5)).sort();
assert.deepEqual(sources, duotone, 'Duotone source and npm inventory differ');

const modulePath = 'packages/core/src/duotone.js';
const { duoDuration, duoPose } = await import(pathToFileURL(path.resolve(source, modulePath)));

function matrix(transform) {
    const parts = /^translate\(([^)]+)\) rotate\(([^)]+)\) scale\(([^)]+)\) translate\(([^)]+)\)$/.exec(transform);
    assert.ok(parts, 'Unsupported upstream transform: ' + transform);
    const values = parts.slice(1).flatMap(group => group.split(/\s+/).map(Number));
    assert.ok(values.length === 7 && values.every(Number.isFinite), transform);
    const [tx, ty, angle, sx, sy, x, y] = values;
    const c = Math.cos(angle * Math.PI / 180), s = Math.sin(angle * Math.PI / 180);
    return [c * sx, s * sx, -s * sy, c * sy, tx + c * sx * x - s * sy * y, ty + s * sx * x + c * sy * y];
}

const icons = [];
// Match upstream source-file order (e.g. bell-off.json precedes bell.json).
for (const file of duotone.map(name => name + '.json').sort()) {
    const name = file.slice(0, -5);
    const def = await read(path.join(source, 'icons-duotone', name + '.json'));
    const frames = [0, 0.13, 0.37, 0.7, 1].map(t => ({
        t,
        tracks: Object.fromEntries(Object.entries(duoPose(def, t)).map(([key, attributes]) => [key, {
            matrix: matrix(attributes.transform),
            opacity: attributes.opacity ?? 1,
            stroke: Number(attributes['stroke-dasharray']?.split(' ')[0] ?? 1),
        }])),
    }));
    icons.push({ name, duration: duoDuration(def), frames });
}

async function contract(name, data) {
    const file = 'tests/KubaiIcons.Tests/Contracts/' + name + '.json';
    const json = JSON.stringify(data, null, 2);
    if (mode === 'generate') {
        await fs.writeFile(file, json + '\n');
    } else {
        assert.deepEqual(await read(file), JSON.parse(json), 'Outdated upstream contract: ' + file);
    }
}

await contract('published-inventory', {
    bases, compositions, duotone, badges: badges.filter(badge => badge !== 'off').sort(),
    hasSlash: badges.includes('off'), categories: categories.map(row => row.id).sort(),
});
await contract('duotone-motion', { source: 'kubai087/kubai-icons@' + commit + '/' + modulePath, icons });
console.log(`Verified upstream inputs for ${duotone.length} Duotone and ${layered.length} Layered icons.`);
