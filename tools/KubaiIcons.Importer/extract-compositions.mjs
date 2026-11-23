// Lower withBadge's painted layers to a shared base, a user-space knockout and
// ordered local glyph transforms. Reject other mask/paint semantics rather than guessing.
export async function extractCompositions(load, pairs) {
    const { makeU, parse, serialize } = await load('packages/core/src/render.js');
    const { withBadge } = await load('packages/core/src/compose.js');
    const { interp, PHASE } = await load('packages/core/src/motion.js');
    const fail = reason => {
        throw new Error(reason);
    };
    const valid = s => /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(s);
    const attrs = (node, expected) =>
        node &&
        node.attrs.size === Object.keys(expected).length &&
        Object.entries(expected).every(([k, v]) => node.getAttribute(k) === v);
    const number = s => (s != null && s !== '' && Number.isFinite(+s) ? +s : fail('Invalid number: ' + s));
    const badges = {};
    const entries = [];
    for (const { baseName, slug } of pairs) {
        const name = baseName + '-' + slug;
        if (!valid(baseName) || !valid(slug) || entries.some(e => e.name === name)) {
            fail('Invalid composition: ' + name);
        }
        const { default: base } = await load('icons/' + baseName + '.js');
        const { default: badge } = await load('badges/' + slug + '.js');
        if (base.name !== baseName || badge.name !== slug || !base.variants?.includes(slug)) {
            fail('Undeclared pair: ' + name);
        }
        const def = withBadge(base, badge);
        const tree = parse(def.svg(makeU('K')));
        const [mask, body, anchor] = tree.children;
        const hole = mask?.children[1];
        const x = hole?.getAttribute('cx');
        const y = hole?.getAttribute('cy');
        if (
            tree.children.length !== 3 ||
            mask.tag !== 'mask' ||
            mask.children.length !== 2 ||
            !attrs(mask, {
                id: 'kbk-K',
                maskUnits: 'userSpaceOnUse',
                x: '-40',
                y: '-40',
                width: '111',
                height: '109',
            }) ||
            serialize(mask.children[0]) !== '<rect x="-40" y="-40" width="111" height="109" fill="#fff"/>' ||
            hole.tag !== 'circle' ||
            !attrs(hole, { 'data-n': 'kbK', cx: x, cy: y, r: hole.getAttribute('r'), fill: '#000' }) ||
            body.tag !== 'g' ||
            !attrs(body, { mask: 'url(#kbk-K)' }) ||
            body.children.map(serialize).join('') !==
                parse(base.svg(makeU('K')))
                    .children.map(serialize)
                    .join('') ||
            anchor.tag !== 'g' ||
            !attrs(anchor, { 'data-badge': '#33363D', transform: 'translate(' + x + ' ' + y + ')' }) ||
            !/^#[0-9A-F]{6}$/i.test(badge.tone)
        ) {
            fail('Unsupported composition mask or paint: ' + name);
        }

        function namedElements(root) {
            const nodes = Object.create(null);

            function index(n) {
                if (typeof n === 'string') {
                    return;
                } // Text content is not an addressable SVG node.
                const id = n.getAttribute('data-n');
                if (id) {
                    if (Object.hasOwn(nodes, id)) {
                        fail('Duplicate source node: ' + id);
                    }
                    nodes[id] = n;
                }
                n.children.forEach(index);
            }

            index(root);
            return nodes;
        }

        const nodes = namedElements(tree);
        let layout;

        function frame(h, out, e, active = false) {
            const state = { active };
            const p = interp(def.target(false, state), def.target(true, state), h);
            globalThis[PHASE] = { out, e };
            try {
                def.draw(p, nodes, {});
            } finally {
                delete globalThis[PHASE];
            }
            if (
                !attrs(hole, { 'data-n': 'kbK', cx: x, cy: y, r: hole.getAttribute('r'), fill: '#000' }) ||
                !attrs(body, { mask: 'url(#kbk-K)' }) ||
                !attrs(anchor, { 'data-badge': '#33363D', transform: 'translate(' + x + ' ' + y + ')' })
            ) {
                fail('Moving badge anchor or changing mask');
            }
            const values = [number(hole.getAttribute('r'))];
            const shapes = [];
            const slot = s => (values.push(number(s)), values.length - 1);

            function visit(node, parent) {
                const a = Object.fromEntries(node.attrs);
                const transforms = [...parent];
                delete a['data-n'];
                let remaining = (a.transform ?? '').trim();
                delete a.transform;
                while (remaining) {
                    const m = /^(translate|scale|rotate)\(([^()]*)\)\s*/.exec(remaining);
                    if (!m) {
                        fail('Unsupported transform: ' + remaining);
                    }
                    const args = m[2].trim().split(/[\s,]+/);
                    if (m[1] === 'rotate' ? args.length !== 1 : args.length > 2) {
                        fail('Unsupported transform arity');
                    }
                    transforms.push([m[1], ...args.map(slot)]);
                    remaining = remaining.slice(m[0].length);
                }
                if (node.tag === 'g') {
                    if (Object.keys(a).length) {
                        fail('Unsupported group paint');
                    }
                    node.children.forEach(child => visit(child, transforms));
                    return;
                }
                if (node.children.length) {
                    fail('Unexpected children of ' + node.tag);
                }
                const width = a.stroke ? number(a['stroke-width']) : 0;
                const disc = a['data-t'] === 'fill:#5393FF';
                if (
                    disc
                        ? node.tag !== 'circle' || a.fill || a.stroke
                        : width
                          ? a.fill !== 'none' || a.stroke !== '#FFFFFF'
                          : a.fill !== '#FFFFFF'
                ) {
                    fail('Unsupported glyph paint');
                }
                if (
                    (a['stroke-linecap'] && a['stroke-linecap'] !== 'round') ||
                    (a['stroke-linejoin'] && a['stroke-linejoin'] !== 'round') ||
                    (a['fill-rule'] && a['fill-rule'] !== 'evenodd')
                ) {
                    fail('Unsupported stroke or fill rule');
                }
                const shape = {
                    transforms,
                    width,
                    disc,
                    round: !!a['stroke-linecap'],
                    join: !!a['stroke-linejoin'],
                    even: !!a['fill-rule'],
                };
                if (a['stroke-dasharray']) {
                    shape.dash = number(a['stroke-dasharray']);
                    shape.offset = slot(a['stroke-dashoffset']);
                    delete a['stroke-dasharray'];
                    delete a['stroke-dashoffset'];
                }
                for (const k of [
                    'fill',
                    'stroke',
                    'stroke-width',
                    'stroke-linecap',
                    'stroke-linejoin',
                    'fill-rule',
                ]) {
                    delete a[k];
                }
                if (disc) {
                    delete a['data-t'];
                }
                if (node.tag === 'circle') {
                    shape.circle = [number(a.cx ?? 0), number(a.cy ?? 0), number(a.r)];
                    delete a.cx;
                    delete a.cy;
                    delete a.r;
                } else if (node.tag === 'path') {
                    const line = /^M(-?[\d.]+) 0H(-?[\d.]+)$/.exec(a.d);
                    if (line && width) {
                        shape.line = [slot(line[1]), slot(line[2])];
                    } else {
                        shape.path = a.d || fail('Missing path');
                    }
                    delete a.d;
                } else {
                    fail('Unsupported badge element: ' + node.tag);
                }
                if (Object.keys(a).length) {
                    fail('Unsupported attributes: ' + Object.keys(a));
                }
                shapes.push(shape);
            }

            anchor.children.forEach(n => visit(n, []));
            if (!shapes[0]?.disc || shapes.filter(s => s.disc).length !== 1) {
                fail('Missing or duplicated badge disc');
            }
            const key = JSON.stringify(shapes);
            if (layout && layout !== key) {
                fail('Changing badge topology: ' + name);
            }
            layout = key;
            return values;
        }

        const cache = new Map();

        function sample(t) {
            if (!cache.has(t)) {
                const normal = frame(t, true, 1 - t);
                const enter = frame(t, false, t);
                cache.set(
                    t,
                    normal.map((v, i) => [v, enter[i] - v]),
                );
            }
            return cache.get(t);
        }

        function fit(i, j) {
            const get = t => sample(t)[i][j];
            const keys = [[0, get(0)]];

            function split(a, b, depth) {
                const left = get(a);
                const right = get(b);
                if (
                    [0.25, 0.5, 0.75].every(
                        q => Math.abs(get(a + (b - a) * q) - left - (right - left) * q) < 0.007,
                    )
                ) {
                    keys.push([b, right]);
                    return;
                }
                if (depth >= 24) {
                    fail('Unresolved curve: ' + name);
                }
                const m = (a + b) / 2;
                split(a, m, depth + 1);
                split(m, b, depth + 1);
            }

            split(0, 1, 0);
            return keys.every(k => k[1] === keys[0][1]) ? [keys[0]] : keys;
        }

        // Reusing a base also reuses its player. Prove the wrapper passes its pose through
        // unchanged, including saved state, rather than silently freezing it like withSlash.
        const standalone = parse(base.svg(makeU('K')));
        const standaloneNodes = namedElements(standalone);
        for (const active of [false, true]) {
            for (const out of [false, true]) {
                for (const h of [0, 0.27, 0.71, 1]) {
                    const e = out ? 1 - h : h;
                    frame(h, out, e, active);
                    globalThis[PHASE] = { out, e };
                    try {
                        base.draw(
                            interp(base.target(false, { active }), base.target(true, { active }), h),
                            standaloneNodes,
                            {},
                        );
                    } finally {
                        delete globalThis[PHASE];
                    }
                    if (
                        standalone.children.map(serialize).join('') !== body.children.map(serialize).join('')
                    ) {
                        fail('Composition does not preserve base motion: ' + name);
                    }
                }
            }
        }
        if (sample(0).length > 128) {
            fail('Badge channel limit exceeded: ' + name);
        }
        const existing = badges[slug];
        const tracks = existing?.tracks ?? sample(0).map((_, i) => [fit(i, 0), fit(i, 1)]);

        function at(keys, t) {
            const i = keys.findIndex(k => k[0] > t);
            if (i < 0) {
                return keys.at(-1)[1];
            }
            const [a, v] = keys[i - 1];
            const [b, w] = keys[i];
            return v + ((w - v) * (t - a)) / (b - a);
        }

        // Independent phases, including a reversal, exercise the residual decomposition.
        for (const start of [0, 0.37, 0.86, 1]) {
            for (const out of [true, false]) {
                for (let i = 0; i < 113; i += existing ? 17 : 1) {
                    const e = (i + 0.5) / 113;
                    const h = start + ((out ? 0 : 1) - start) * e;
                    const actual = frame(h, out, e, i % 2 === 0);
                    const wave = out ? start : h > e + 0.001 ? e : h;
                    const weight = out ? 1 - e : 1;
                    if (
                        tracks.some(
                            ([normal, res], c) =>
                                Math.abs(at(normal, h) + weight * at(res, wave) - actual[c]) > 0.025,
                        )
                    ) {
                        fail('Off-grid badge phase differs: ' + name + ' at ' + h);
                    }
                }
            }
        }
        const art = { tone: badge.tone, shapes: JSON.parse(layout), tracks };
        if (badges[slug] && JSON.stringify(badges[slug]) !== JSON.stringify(art)) {
            fail('Base-dependent badge: ' + name);
        }
        badges[slug] = art;
        entries.push({
            name,
            base: baseName,
            badge: slug,
            x: number(x),
            y: number(y),
            label: base.zh + '·' + badge.zh,
        });
    }
    return { badges, entries };
}
