// Build a portable, script-free comparison from local Avalonia renders and the upstream oracle.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const review = 'artifacts/review';
const output = path.join(review, 'gallery');
const reference = 'artifacts/layered-validation';
const source = 'artifacts/upstream';
const read = async file => JSON.parse(await fs.readFile(file));
const selected = await read(path.join(review, 'changed.json'));
const jobs = await read(path.join(reference, 'poses.json'));
const { duotone } = await read('tests/KubaiIcons.Tests/Contracts/published-inventory.json');
const { duoMarkup, duoPose } = await import(pathToFileURL(path.resolve(source, 'packages/core/src/duotone.js')));
const assets = new Map();
const sections = [];
await fs.mkdir(output, { recursive: true });

async function crop(directory, filename, x, y, side) {
    if (!assets.has(filename)) {
        await fs.copyFile(path.join(directory, filename), path.join(output, filename));
        assets.set(filename, true);
    }
    return `<span role="img" aria-label="Artwork frame" style="display:inline-block;width:${side}px;height:${side}px;background:url('${filename}') -${x}px -${y}px"></span>`;
}

function poseSvg(def, t) {
    const tracks = duoPose(def, t);
    // Apply the upstream pose to every occurrence, including animated mask shapes.
    const body = duoMarkup(def).replaceAll('__K__', 'preview').replace(
        /<g data-l="([^"]+)"[^>]*>/g, (group, key) => {
            if (!Object.hasOwn(tracks, key)) return group;
            const attrs = Object.entries(tracks[key]).map(([attr, value]) => `${attr}="${value}"`).join(' ');
            return `<g ${attrs}>`;
        });
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width="64" height="64" color="#5393ff"><rect width="16" height="16" fill="white"/>${body}</svg>`;
}

function row(family, name, poses) {
    assert.match(name, /^[a-z0-9-]+$/);
    return `<tr><th scope="row">${name}<small>${family}</small></th><td>${poses.map(p => p[0]).join(' ')}</td><td>${poses.map(p => p[1]).join(' ')}</td></tr>`;
}

const layerSet = new Set(selected.Layered);
const byName = new Map();
for (const [i, job] of jobs.entries()) {
    if (!layerSet.has(job.name)) continue;
    const start = Math.floor(i / 100) * 100;
    const x = (i % 10) * 62, y = Math.floor(i % 100 / 10) * 62;
    const pair = await Promise.all(['reference', 'native'].map(prefix => crop(reference, `${prefix}-${start}.png`, x, y, 62)));
    if (!byName.has(job.name)) byName.set(job.name, []);
    byName.get(job.name).push(pair);
}
for (const name of selected.Layered) {
    assert.ok(byName.has(name), 'Missing Layered pose: ' + name);
    sections.push(row('Layered', name, byName.get(name)));
}
for (const name of selected.Duotone) {
    assert.match(name, /^[a-z0-9-]+$/);
    const i = duotone.indexOf(name);
    assert.ok(i >= 0, 'Missing Duotone pose: ' + name);
    const def = await read(path.join(source, 'icons-duotone', name + '.json'));
    const pairs = [];
    for (const [index, t] of [0, 0.37, 1].entries()) {
        const filename = `duotone-${name}-${index}.svg`;
        await fs.writeFile(path.join(output, filename), poseSvg(def, t));
        const native = await crop(review, `catalog-${Math.floor(i / 90)}-${index}.png`,
            (i % 10) * 100 + 18, Math.floor(i % 90 / 10) * 100 + 6, 64);
        pairs.push([`<img src="${filename}" alt="Upstream pose at ${t}" width="64" height="64">`, native]);
    }
    sections.push(row('Duotone', name, pairs));
}
const content = sections.length
    ? `<p>Layered bases: rest, Soft entry, interrupted return. Compositions: moving badge or overlay. Duotone: rest, 37% and held.</p>
<table><thead><tr><th>Icon</th><th>Upstream</th><th>Avalonia</th></tr></thead><tbody>${sections.join('\n')}</tbody></table>`
    : '<p>No artwork changes in this release.</p>';
await fs.writeFile(path.join(output, 'index.html'), `<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Upstream artwork review</title>
<style>body{font:16px system-ui;margin:2rem;color:#182331}table{border-collapse:collapse}th,td{padding:.6rem;text-align:left;border-bottom:1px solid #bcc6d0}small{display:block;font-weight:normal}</style>
<h1>Upstream artwork review</h1>${content}</html>\n`);
console.log(`Wrote ${sections.length} comparisons to ${output}/index.html`);
