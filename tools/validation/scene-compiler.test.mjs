// Exercise failure paths with the same pinned front end used by layered-generate.
import assert from 'node:assert/strict';
import test from 'node:test';
import { compileScene } from '../KubaiIcons.Importer/scene/compile.mjs';
import * as render from '../../artifacts/upstream/packages/core/src/render.js';
import * as motion from '../../artifacts/upstream/packages/core/src/motion.js';
import * as kit from '../../artifacts/upstream/packages/core/src/kit.js';

const api = { ...render, ...motion, ...kit };
const icon = {
    name: 'fixture',
    svg: () => '<path data-n="mark" d="M0 0L10 0L0 10Z" fill="#fff"/>',
    target: on => ({ progress: on ? 1 : 0 }),
    draw() {},
};

test('unrepresented geometry semantics stop the import', () => {
    const unsupported = {
        ...icon,
        svg: () => icon.svg().replace('<path ', '<path vector-effect="non-scaling-stroke" '),
    };
    assert.throws(() => compileScene(unsupported, api), /Unsupported path attribute: vector-effect/);
});

test('an animated paint cannot silently compile as a static rest color', () => {
    const unsupported = {
        ...icon,
        draw: (p, nodes) => nodes.mark.setAttribute('fill', p.progress > 0.5 ? '#f00' : '#fff'),
    };
    assert.throws(() => compileScene(unsupported, api), /Unsupported animated attribute: mark.fill/);
});

test('a new persistent state requires a build-time adapter', () => {
    assert.throws(() => compileScene({ ...icon, tap: true }, api), /Missing active-state adapter/);
});
