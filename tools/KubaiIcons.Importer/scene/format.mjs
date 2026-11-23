// Build-time native scene IR. References are integers, never SVG markup.
export class Pool {
    items = [];
    ids = new Map();

    add(value) {
        const key = JSON.stringify(value);
        if (!this.ids.has(key)) {
            this.ids.set(key, this.items.length);
            this.items.push(value);
        }
        return this.ids.get(key);
    }
}

export class Scene {
    scalars = new Pool();
    shapes = new Pool();
    paints = new Pool();

    constant(x) {
        if (!Number.isFinite(x)) {
            throw Error('Non-finite scene scalar');
        }
        return this.scalars.add([0, x]);
    }

    input(op) {
        return this.scalars.add([op]);
    }

    op(op, a, b) {
        const v = this.scalars.items[a];
        const w = this.scalars.items[b];
        if (op === 6 && ((v[0] === 0 && v[1] === 0) || (w[0] === 0 && w[1] === 0))) {
            return v[0] === 0 && v[1] === 0 ? b : a;
        }
        if (op === 8) {
            for (const [x, other] of [
                [v, b],
                [w, a],
            ]) {
                if (x[0] === 0 && (x[1] === 0 || x[1] === 1)) {
                    return x[1] === 0 ? this.constant(0) : other;
                }
            }
        }
        if (v[0] === 0 && w[0] === 0) {
            return this.constant(
                [
                    v[1] + w[1],
                    v[1] - w[1],
                    v[1] * w[1],
                    v[1] / w[1],
                    Math.max(v[1], w[1]),
                    Math.min(v[1], w[1]),
                ][op - 6],
            );
        }
        return this.scalars.add([op, a, b]);
    }

    curve(input, keys) {
        if (keys.every(k => k[1] === keys[0][1])) {
            return this.constant(keys[0][1]);
        }
        return this.scalars.add([12, input, keys]);
    }
}

class Bytes {
    data = [];

    byte(n) {
        this.data.push(n);
    }

    uint(n) {
        if (!Number.isSafeInteger(n) || n < 0) {
            throw Error('Invalid index ' + n);
        }
        while (n >= 128) {
            this.byte((n % 128) + 128);
            n = Math.floor(n / 128);
        }
        this.byte(n);
    }

    number(n) {
        const b = Buffer.allocUnsafe(8);
        b.writeDoubleLE(n);
        this.data.push(...b);
    }

    string(s) {
        const b = Buffer.from(s);
        this.uint(b.length);
        this.data.push(...b);
    }

    list(a, fn) {
        this.uint(a.length);
        for (const x of a) {
            fn(x);
        }
    }
}

export function encode(scene, root) {
    const w = new Bytes();
    w.data.push(75, 66, 73, 1, 6);
    const refs = a => w.list(a, n => w.uint(n));
    w.list(scene.scalars.items, ([op, a, b]) => {
        w.byte(op);
        if (op === 0) {
            w.number(a);
        } else if (op === 12) {
            w.uint(a);
            w.list(b, ([t, v]) => {
                w.number(t);
                w.number(v);
            });
        } else if (op >= 6 && op <= 11) {
            w.uint(a);
            w.uint(b);
        } else if (op === 18 || op === 19) {
            w.uint(a);
        } else if (op === 17) {
            w.uint(a);
            w.list(b, ([t, index]) => {
                w.number(t);
                w.uint(index);
            });
        } else if (op === 16) {
            w.uint(a);
            w.uint(b[0]);
            w.uint(b[1]);
        }
    });
    w.list(scene.paints.items, p => {
        w.byte(p.absolute ? 1 : 0);
        p.coords.forEach(x => w.number(x));
        w.list(p.stops, ([at, mode, rgb]) => {
            w.number(at);
            w.byte(mode);
            w.uint(rgb);
        });
    });
    w.list(scene.shapes.items, s => {
        w.byte(s.kind);
        w.byte(s.even ? 1 : 0);
        w.list(s.ops, ([op, ...args]) => {
            w.byte(op);
            refs(args);
        });
    });
    const node = n => {
        w.byte(n.kind);
        w.uint(n.geo + 1);
        w.uint(n.fill + 1);
        w.uint(n.stroke + 1);
        refs(n.matrix);
        [n.opacity, n.width, n.fillOpacity, n.strokeOpacity, n.dashOffset, n.pathLength].forEach(x =>
            w.uint(x),
        );
        w.byte(n.cap);
        w.byte(n.join);
        w.byte(n.fx);
        refs(n.dash);
        w.uint(n.clip + 1);
        w.byte(n.clipMode);
        w.uint(n.clipWidth);
        w.uint(n.maskPaint + 1);
        if (n.kind === 2) {
            w.string(n.text);
            refs(n.metrics);
            w.uint(n.weight);
            w.byte(n.anchor);
        }
        w.list(n.children, node);
    };
    node(root);
    return Buffer.from(w.data);
}
