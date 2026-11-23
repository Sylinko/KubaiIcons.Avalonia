import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';

const registry = 'https://registry.npmjs.org/kubai-icons';
const stable = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const pinFile = 'catalog/upstream.json';
const pin = JSON.parse(await fs.readFile(pinFile, 'utf8'));
const current = pin.version.replace(/^kubai-icons@/, '');
const [command, requested] = process.argv.slice(2);

async function json(url) {
    const response = await fetch(url);
    if (!response.ok) throw Error(`GET ${url}: ${response.status}`);
    return response.json();
}

function compare(a, b) {
    const left = a.split('.').map(BigInt), right = b.split('.').map(BigInt);
    for (let i = 0; i < 3; i++) {
        if (left[i] !== right[i]) return left[i] < right[i] ? -1 : 1;
    }
    return 0;
}

if (command === 'next') {
    const { versions } = await json(registry);
    const next = Object.keys(versions).filter(v => stable.test(v) && compare(v, current) > 0).sort(compare)[0];
    if (requested && requested !== next && requested !== current) {
        throw Error(`Expected the next stable release (${next ?? 'none'}), not ${requested}.`);
    }
    const version = requested === current ? '' : next ?? '';
    console.log(version ? `Next: kubai-icons@${version}` : `Already at ${current}.`);
    if (process.env.GITHUB_OUTPUT) await fs.appendFile(process.env.GITHUB_OUTPUT, 'version=' + version + '\n');
} else if (command === 'fetch' || command === 'pin') {
    const version = command === 'pin' ? requested : current;
    assert.ok(stable.test(version), 'Expected X.Y.Z');
    const pkg = await json(registry + '/' + version);
    const source = 'artifacts/upstream';
    const commit = execFileSync('git', ['-C', source, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
    assert.equal(pkg.gitHead, commit, 'npm gitHead does not match the source checkout');
    assert.equal(pkg.name, 'kubai-icons');
    assert.equal(pkg.version, version);
    if (command === 'fetch') assert.equal(commit, pin.commit);
    const url = `https://registry.npmjs.org/kubai-icons/-/kubai-icons-${version}.tgz`;
    assert.equal(pkg.dist.tarball, url);
    const output = 'artifacts/studio-metadata';
    await fs.mkdir(output, { recursive: true });
    const tarball = path.join(output, 'package.tgz');
    const response = await fetch(url);
    if (!response.ok) throw Error(`GET ${url}: ${response.status}`);
    await fs.writeFile(tarball, Buffer.from(await response.arrayBuffer()));
    // Extract only the three data files; no install, package scripts or archive paths on disk.
    for (const [entry, target] of [
        ['package.json', 'package.json'],
        ['dist/icons.json', 'layered-icons.json'],
        ['dist/duotone/icons.json', 'duotone-icons.json'],
    ]) {
        const bytes = execFileSync('tar', ['-xzOf', tarball, 'package/' + entry], { maxBuffer: 64 * 1024 * 1024 });
        await fs.writeFile(path.join(output, target), bytes);
    }
    if (command === 'pin') {
        const count = async family => JSON.parse(await fs.readFile(path.join(output, family + '-icons.json'))).length;
        const updated = { ...pin, version: 'kubai-icons@' + version, commit,
            duotoneCount: await count('duotone'), layeredCount: await count('layered') };
        await fs.writeFile(pinFile, JSON.stringify(updated, null, 2) + '\n');
    }
    console.log(`Fetched kubai-icons@${version} at ${commit}.`);
} else {
    throw Error('Usage: upstream.mjs next [version] | fetch | pin <version>');
}
