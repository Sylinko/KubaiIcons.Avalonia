import { Scene, encode } from './format.mjs';
import { pathData, matrix, numbers } from './geometry.mjs';
import { motion } from './motion.mjs';
import { validateTree } from './schema.mjs';

export function compileScene(def, api) {
    const { parse, renderInner, interp, PHASE, EASE, bump } = api;
    const tree = parse(renderInner(def, { k: 'K', paint: 'template', names: true }));
    validateTree(tree);
    const all = [];
    const named = Object.create(null);
    const ids = new Map();
    const under = new Map();
    const over = new Map();
    const links = new Map();

    function index(n) {
        if (typeof n === 'string') {
            return;
        }
        all.push(n);
        const id = n.getAttribute('id');
        const name = n.getAttribute('data-n');
        if (id) {
            ids.set(id, n);
        }
        if (name) {
            named[name] = n;
        }
        for (const [k, m] of [
            ['data-under', under],
            ['data-over', over],
        ]) {
            const s = n.getAttribute(k);
            if (s !== null) {
                m.set(s, n);
            }
        }
        n.children.forEach(index);
    }

    index(tree);
    const url = s => /^url\(#(.+)\)$/.exec(s ?? '')?.[1];
    for (const [k, n] of under) {
        links.set(n, over.get(k));
    }
    for (const n of all) {
        const mask = ids.get(url(n.getAttribute('mask')));
        const p = mask?.children.find(c => c.tag === 'path');
        if (p && p.getAttribute('d') === n.getAttribute('d') && n.getAttribute('d') !== null) {
            links.set(p, n);
        }
    }
    const names = Object.keys(named);
    const numberByNode = new Map(names.map((k, i) => [named[k], i]));
    const initial = names.map(k => Object.fromEntries(named[k].attrs));
    const states = [def.target(false, {}), def.target(true, {})];
    const cache = new Map();

    function sample(h, out, e) {
        const key = h + ',' + out + ',' + e;
        if (cache.has(key)) {
            return cache.get(key);
        }
        if (cache.size >= 12000) {
            cache.clear();
        } // Bound temporary DOM snapshots independently of icon complexity.
        globalThis[PHASE] = { out, e };
        try {
            def.draw(interp(...states, h), named, {});
        } finally {
            delete globalThis[PHASE];
        }
        const frame = names.map(k => Object.fromEntries(named[k].attrs));
        cache.set(key, frame);
        return frame;
    }

    const sc = new Scene();
    const C = x => sc.constant(x);
    const track = motion(sc, sample, EASE);
    const probes = [];
    for (const h of [0.001, 0.9999, ...Array.from({ length: 65 }, (_, i) => i / 64)]) {
        for (const out of [true, false]) {
            probes.push(sample(h, out, out ? 1 - h : h));
        }
    }
    // Refuse dynamic paint, clip targets or other channels until the IR can
    // represent them. A static rest frame alone cannot reveal these features.
    const channels = new Set(
        (
            'd transform opacity stroke-width stroke-dasharray stroke-dashoffset ' +
            'cx cy r rx ry x y width height font-size pathLength fill-opacity stroke-opacity'
        ).split(' '),
    );
    for (const frame of probes) {
        for (let i = 0; i < names.length; i++) {
            for (const key of [...Object.keys(initial[i]), ...Object.keys(frame[i])]) {
                if (initial[i][key] !== frame[i][key] && !channels.has(key)) {
                    throw Error('Unsupported animated attribute: ' + names[i] + '.' + key);
                }
            }
        }
    }

    function field(n, k) {
        if (links.has(n) && (k === 'd' || (k === 'transform' && n.getAttribute('data-under') !== null))) {
            n = links.get(n);
        }
        const i = numberByNode.get(n);
        if (i === undefined) {
            const s = n.getAttribute(k);
            return { s };
        }
        const s = initial[i][k] ?? null;
        const get = f => f[i][k] ?? null;
        return probes.every(f => get(f) === s) ? { s } : { s, get };
    }

    function vector(n, k, parseValue, eps) {
        const { s, get } = field(n, k);
        const a = parseValue(s);
        if (!get) {
            return a.map(C);
        }
        const m = new Map();

        function read(f) {
            const s = get(f);
            if (!m.has(s)) {
                const v = parseValue(s);
                if (v.length !== a.length) {
                    throw Error('Changing arity for ' + k);
                }
                m.set(s, v);
            }
            return m.get(s);
        }

        for (const f of probes) {
            read(f);
        }
        return a.map((_, i) => track(f => read(f)[i], eps));
    }

    const scalar = (n, k, d = 0) => vector(n, k, s => [s === null ? d : +s], 0.002)[0];
    const mats = n => {
        const { s, get } = field(n, 'transform');
        const identity = [1, 0, 0, 1, 0, 0];
        if (!get) {
            return !s ? [] : matrix(s).map(C);
        }
        const list = s => [...(s ?? '').matchAll(/([a-zA-Z]+)\(([^)]*)\)/g)].map(m => [m[1], numbers(m[2])]);
        const longest = probes
            .map(f => list(get(f)))
            .sort(
                (a, b) =>
                    b.length - a.length ||
                    b.reduce((n, term) => n + term[1].length, 0) -
                        a.reduce((n, term) => n + term[1].length, 0),
            )[0];
        const cache = new Map();
        const aligned = str => {
            if (cache.has(str)) {
                return cache.get(str);
            }
            const terms = list(str);
            let best = null;
            let score = Infinity;

            function match(i, j, slots, cost) {
                if (cost >= score) {
                    return;
                }
                if (i === longest.length) {
                    if (j === terms.length) {
                        best = slots;
                        score = cost;
                    }
                    return;
                }
                if (longest.length - i > terms.length - j) {
                    match(i + 1, j, [...slots, -1], cost);
                }
                if (terms[j]?.[0] === longest[i][0] && terms[j][1].length <= longest[i][1].length) {
                    const p = longest[i][1];
                    const q = terms[j][1];
                    match(
                        i + 1,
                        j + 1,
                        [...slots, j],
                        cost + p.reduce((s, x, k) => s + Math.abs(x - (q[k] ?? 0)) / (1 + Math.abs(x)), 0),
                    );
                }
            }

            match(0, 0, [], 0);
            if (!best) {
                throw Error('Incompatible transform sequence ' + str);
            }
            const args = longest.flatMap(([op, params], i) => {
                const q = terms[best[i]]?.[1];
                return params.map((v, j) =>
                    q
                        ? (q[j] ?? (op === 'scale' ? q[0] : 0))
                        : op === 'scale'
                          ? 1
                          : op === 'rotate' && j > 0
                            ? v
                            : op === 'matrix'
                              ? identity[j]
                              : 0,
                );
            });
            cache.set(str, args);
            return args;
        };
        try {
            for (const f of probes) {
                aligned(get(f));
            }
        } catch {
            return vector(n, 'transform', s => matrix(s ?? ''), 0.006);
        }
        const add = (a, b) => sc.op(6, a, b);
        const mul = (a, b) => sc.op(8, a, b);
        const sub = (a, b) => sc.op(7, a, b);
        const times = ([a, b, c, d, e, f], [g, h, i, j, k, l]) => [
            add(mul(a, g), mul(c, h)),
            add(mul(b, g), mul(d, h)),
            add(mul(a, i), mul(c, j)),
            add(mul(b, i), mul(d, j)),
            add(add(mul(a, k), mul(c, l)), e),
            add(add(mul(b, k), mul(d, l)), f),
        ];
        const zero = C(0);
        const one = C(1);
        let m = identity.map(C);
        let index = 0;
        for (const [op, params] of longest) {
            const a = params.map(() => {
                const j = index++;
                return track(f => aligned(get(f))[j], op === 'rotate' ? 0.035 : 0.002);
            });
            let t;
            if (op === 'translate') {
                t = [one, zero, zero, one, a[0], a[1] ?? zero];
            } else if (op === 'scale') {
                t = [a[0], zero, zero, a[1] ?? a[0], zero, zero];
            } else if (op === 'rotate') {
                const z = sc.scalars.add([18, a[0]]);
                const c = sc.scalars.add([19, a[0]]);
                const x = a[1] ?? zero;
                const y = a[2] ?? zero;
                t = [
                    c,
                    z,
                    sub(zero, z),
                    c,
                    add(sub(x, mul(c, x)), mul(z, y)),
                    sub(sub(y, mul(z, x)), mul(c, y)),
                ];
            } else if (op === 'matrix') {
                t = a;
            } else {
                return vector(n, 'transform', s => matrix(s ?? ''), 0.003);
            }
            m = times(m, t);
        }
        return m;
    };

    function color(s, mask) {
        if (mask && (s === '#fff' || s === '#000')) {
            return [4, s === '#fff' ? 255 : 0];
        }
        const pure = /^\{p\{t#([\da-f]{6})([ec])\}\}$/i.exec(s);
        if (pure) {
            return [pure[2] === 'e' ? 5 : 6, parseInt(pure[1], 16)];
        }
        if (s === '{w}') {
            return [3, 0];
        }
        const m = /^\{(t|p)#([0-9a-f]{6})([ce]?)\}$/i.exec(s);
        if (m) {
            return [m[1] === 'p' ? 5 : m[3] === 'e' ? 2 : 1, parseInt(m[2], 16)];
        }
        if (/^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i.test(s)) {
            let hex = s.slice(1);
            if (hex.length === 3) {
                hex = hex
                    .split('')
                    .map(x => x + x)
                    .join('');
            }
            return [0, parseInt(hex, 16)];
        }
        throw Error('Unknown scene paint ' + s);
    }

    function paint(s, mask = false) {
        if (!s || s === 'none') {
            return -1;
        }
        const g = ids.get(url(s));
        return sc.paints.add(
            g
                ? {
                      absolute: g.getAttribute('gradientUnits') === 'userSpaceOnUse',
                      coords: ['x1', 'y1', 'x2', 'y2'].map(k => +g.getAttribute(k)),
                      stops: g.children.map(t => [
                          +t.getAttribute('offset'),
                          ...color(t.getAttribute('stop-color'), mask),
                      ]),
                  }
                : { coords: [0, 0, 0, 1], stops: [[0, ...color(s, mask)]] },
        );
    }

    // Bookmark's saved pose and its wave are independent inputs. Lower the author's
    // rounded ribbon into ordinary scene expressions; the player has no icon ID.
    const sub = (a, b) => sc.op(7, a, b);
    const add = (a, b) => sc.op(6, a, b);
    const mul = (a, b) => sc.op(8, a, b);
    const div = (a, b) => sc.op(9, a, b);
    let active;
    if (def.tap) {
        if (def.name !== 'bookmark') {
            throw Error('Missing active-state adapter');
        }
        const h = sc.input(1);
        const saved = sc.input(2);
        const rest = sc.input(3);
        const y = add(
            C(states[0].yb),
            add(
                mul(h, C(states[1].yb - states[0].yb)),
                mul(rest, C(def.target(false, { active: true }).yb - states[0].yb)),
            ),
        );
        const shapeWave = Array.from({ length: 513 }, (_, i) => {
            const t = EASE(i / 512);
            return [t, bump(t)];
        });
        const sway = mul(C(3), mul(mul(h, h), mul(sc.input(5), sc.curve(sc.input(4), shapeWave))));
        const x = (v, at) => add(C(v), div(mul(sway, sub(at, C(1.5))), sub(y, C(1.5))));
        const tip = sub(y, C(2.8));
        const cy = add(C(1.5), mul(C(0.5), sub(y, C(4.3))));
        const ribbon = sc.shapes.add({
            kind: 2,
            ops: [
                [0, C(10.2), C(1.5), C(0)],
                [0, C(17.6), C(1.5), C(0)],
                [0, x(17.6, y), y, C(0.6)],
                [0, x(13.9, tip), tip, C(0.7)],
                [0, x(10.2, y), y, C(0.6)],
            ],
        });
        const scale = sc.op(10, C(0.001), saved);
        active = { ribbon, saved, matrix: [scale, C(0), C(0), scale, x(13.9, cy), cy] };
    }

    function geometry(n) {
        const name = n.getAttribute('data-n');
        const even = (n.getAttribute('fill-rule') ?? n.getAttribute('clip-rule')) === 'evenodd';
        if (active && ['rib', 'ribF'].includes(name)) {
            return active.ribbon;
        }
        if (n.tag === 'circle' || n.tag === 'ellipse') {
            const r = scalar(n, 'r');
            return sc.shapes.add({
                kind: 1,
                ops: [
                    [
                        0,
                        scalar(n, 'cx'),
                        scalar(n, 'cy'),
                        n.tag === 'circle' ? r : scalar(n, 'rx'),
                        n.tag === 'circle' ? r : scalar(n, 'ry'),
                    ],
                ],
            });
        }
        if (n.tag === 'rect') {
            return sc.shapes.add({
                kind: 3,
                ops: [[0, ...['x', 'y', 'width', 'height'].map(k => scalar(n, k))]],
            });
        }
        if (n.tag !== 'path') {
            throw Error('Unsupported clip or drawing: ' + n.tag);
        }
        const { s, get } = field(n, 'd');
        const decode = new Map();
        const parsed = s => {
            if (!decode.has(s)) {
                decode.set(s, pathData(s ?? ''));
            }
            return decode.get(s);
        };
        const ops = parsed(s);
        const topology = a => a.map(o => o[0]).join('');
        if (!get) {
            return sc.shapes.add({ kind: 0, even, ops: ops.map(([op, ...args]) => [op, ...args.map(C)]) });
        }
        const layouts = new Map();
        for (const f of probes) {
            const v = parsed(get(f));
            layouts.set(topology(v), v);
        }
        // A growing polyline has one topology: pad its shorter poses with zero-length
        // segments. Otherwise a new endpoint count could make the entire stroke disappear.
        if (layouts.size > 1 && [...layouts.keys()].every(k => /^01+$/.test(k))) {
            const count = Math.max(...[...layouts.values()].map(v => v.length));
            return sc.shapes.add({
                kind: 0,
                even,
                ops: Array.from({ length: count }, (_, j) => [
                    j === 0 ? 0 : 1,
                    ...[1, 2].map(i =>
                        track(f => {
                            const points = parsed(get(f));
                            if (points.length > count || !/^01+$/.test(topology(points))) {
                                throw Error('Unseen polyline topology');
                            }
                            return points[Math.min(j, points.length - 1)][i];
                        }, 0.012),
                    ),
                ]),
            });
        }
        const variants = [];
        for (const [key, layout] of layouts) {
            const coords = f => {
                const a = parsed(get(f));
                const type = topology(a);
                if (!layouts.has(type)) {
                    throw Error('Unseen path topology: ' + type);
                }
                if (type === key) {
                    return a;
                }
                // The unused branch is not drawn. Choose its nearest sampled shape.
                return layout;
            };
            const g = sc.shapes.add({
                kind: 0,
                even,
                ops: layout.map(([op, ...args], j) => [
                    op,
                    ...args.map((_, i) => track(f => coords(f)[j][i + 1], 0.012)),
                ]),
            });
            if (layouts.size === 1) {
                return g;
            }
            variants.push([0, g, track(f => (topology(parsed(get(f))) === key ? 1 : 0), 0.01)]);
        }
        return sc.shapes.add({ kind: 4, ops: variants });
    }

    function clipShape(n) {
        if (n.tag !== 'clipPath' && n.tag !== 'g') {
            return geometry(n);
        }
        if (n.children.length === 1 && !n.children[0].getAttribute('transform')) {
            return clipShape(n.children[0]);
        }
        return sc.shapes.add({ kind: 5, ops: n.children.map(c => [0, clipShape(c), ...mats(c)]) });
    }

    function node(n, style = {}) {
        if (['defs', 'linearGradient', 'mask', 'clipPath'].includes(n.tag)) {
            return null;
        }
        const name = n.getAttribute('data-n');
        const st = { ...style };
        for (const k of [
            'fill',
            'stroke',
            'stroke-width',
            'stroke-linecap',
            'stroke-linejoin',
            'fill-opacity',
            'stroke-opacity',
        ]) {
            if (n.getAttribute(k) !== null) {
                st[k] = n.getAttribute(k);
            }
        }
        const inherit = (k, d) => (n.getAttribute(k) === null ? C(+(st[k] ?? d)) : scalar(n, k, d));
        const group = n.tag === '#root' || n.tag === 'g';
        const text = n.tag === 'text';
        const result = {
            kind: group ? 0 : text ? 2 : 1,
            geo: group || text ? -1 : geometry(n),
            fill: paint(st.fill),
            stroke: paint(st.stroke),
            width: inherit('stroke-width', 1),
            cap: { round: 1, square: 2 }[st['stroke-linecap']] ?? 0,
            join: { round: 1, bevel: 2 }[st['stroke-linejoin']] ?? 0,
            matrix: active && name === 'star' ? active.matrix : mats(n),
            opacity: active && name === 'ribF' ? active.saved : scalar(n, 'opacity', 1),
            fillOpacity: inherit('fill-opacity', 1),
            strokeOpacity: inherit('stroke-opacity', 1),
            dash: vector(n, 'stroke-dasharray', s => (s && s !== 'none' ? numbers(s) : []), 0.005),
            dashOffset: scalar(n, 'stroke-dashoffset'),
            pathLength: scalar(n, 'pathLength'),
            fx: { 'sh-K': 1, 'hi-K': 2 }[url(n.getAttribute('filter'))] ?? 0,
            clip: -1,
            clipMode: 0,
            clipWidth: C(0),
            maskPaint: -1,
            children: [],
        };
        if (text) {
            result.text = n.children.join('');
            result.metrics = ['x', 'y', 'font-size'].map(k => scalar(n, k));
            result.weight = +(n.getAttribute('font-weight') ?? 400);
            result.anchor = { middle: 1, end: 2 }[n.getAttribute('text-anchor')] ?? 0;
        }
        const cp = ids.get(url(n.getAttribute('clip-path')));
        const m = ids.get(url(n.getAttribute('mask')));
        if (cp) {
            result.clip = clipShape(cp);
        }
        if (m) {
            if (cp) {
                throw Error('Concurrent mask and clip');
            }
            const [bg, hole] = m.children;
            if (hole) {
                result.clip = clipShape(hole);
                result.clipMode = 1;
                result.clipWidth = scalar(hole, 'stroke-width');
            } else {
                result.clip = clipShape(bg);
                result.maskPaint = paint(bg.getAttribute('fill'), true);
            }
        }
        if (group) {
            result.children = n.children.map(c => node(c, st)).filter(Boolean);
        }
        return result;
    }

    return encode(sc, node(tree));
}
