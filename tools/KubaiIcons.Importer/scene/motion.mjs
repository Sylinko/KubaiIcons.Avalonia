// A normal track follows pose, not wall time. Entry-only travel is sampled
// separately and its remaining amplitude fades when a transition is interrupted.
export function motion(scene, sample, ease) {
    const h = scene.input(1);
    const wave = scene.input(4);
    const weight = scene.input(5);
    const seed = [0, 0.03, 0.11, 0.21, 0.37, 0.5, 0.68, 0.83, 0.95, 0.985, 0.999, 1];

    function fit(get, tol) {
        const keys = [[0, get(0)]];

        function split(a, b, depth) {
            const va = keys.at(-1)[1];
            const vb = get(b);
            const d = vb - va;
            const ok = [0.25, 0.5, 0.75].every(q => Math.abs(get(a + (b - a) * q) - va - d * q) <= tol);
            if (ok || depth === 22) {
                keys.push([b, vb]);
                return;
            }
            const mid = (a + b) / 2;
            split(a, mid, depth + 1);
            split(mid, b, depth + 1);
        }

        for (let i = 1; i < seed.length; i++) {
            split(seed[i - 1], seed[i], 0);
        }
        // Drop redundant collinear keys, without accumulating approximation error.
        for (let i = keys.length - 2; i > 0; i--) {
            const [a, v] = keys[i - 1];
            const [b, w] = keys[i + 1];
            const [t, z] = keys[i];
            if (Math.abs(z - v - ((t - a) / (b - a)) * (w - v)) < 1e-8) {
                keys.splice(i, 1);
            }
        }
        return keys;
    }

    function value(f, tol = 0.005) {
        const normal = t => f(sample(t, true, 1 - t));
        const delta = t => f(sample(t, false, t)) - normal(t);
        const n = fit(normal, tol);
        const d = fit(delta, tol);
        let ok = true;
        for (const start of [0.19, 0.55, 0.91]) {
            for (const out of [false, true]) {
                for (const time of [0.041, 0.15, 0.33, 0.73]) {
                    const e = ease(time);
                    const v = start + ((out ? 0 : 1) - start) * e;
                    const wv = out ? start : v > e + 0.001 ? e : v;
                    const pred = normal(v) + (out ? 1 - e : 1) * delta(wv);
                    if (Math.abs(pred - f(sample(v, out, e))) > tol * 3) {
                        ok = false;
                        break;
                    }
                }
            }
        }
        if (ok) {
            const a = scene.curve(h, n);
            if (d.every(x => x[1] === 0)) {
                return a;
            }
            return scene.op(6, a, scene.op(8, weight, scene.curve(wave, d)));
        }
        // Nonseparable travel (e.g. a point rotated by a swing while it translates).
        // Interpolate between start poses; each column is a real, direction-specific
        // upstream transition, adaptively sampled in linear elapsed time.
        const from = scene.input(13);
        const time = scene.input(14);
        const branch = out => {
            const columns = new Map();
            const actual = (u, t) => {
                const s = ease(u);
                const e = ease(t);
                return f(sample(s + ((out ? 0 : 1) - s) * e, out, e));
            };
            const at = s => {
                if (!columns.has(s)) {
                    columns.set(
                        s,
                        scene.curve(
                            time,
                            fit(t => actual(s, t), tol),
                        ),
                    );
                }
                return columns.get(s);
            };
            const rows = [];

            function split(a, b, depth) {
                const m = (a + b) / 2;
                const ts = [0.025, 0.09, 0.18, 0.32, 0.47, 0.63, 0.8, 0.94];
                if (
                    depth < 1 ||
                    (depth < 10 &&
                        ts.some(t => Math.abs(actual(m, t) - (actual(a, t) + actual(b, t)) / 2) > tol * 2))
                ) {
                    split(a, m, depth + 1);
                    split(m, b, depth + 1);
                    return;
                }
                rows.push([ease(a), at(a)]);
            }

            split(0, 1, 0);
            rows.push([1, at(1)]);
            return scene.scalars.add([17, from, rows]);
        };
        return scene.scalars.add([16, scene.input(15), [branch(true), branch(false)]]);
    }

    return value;
}
