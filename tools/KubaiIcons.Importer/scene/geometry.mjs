// Normalize equivalent SVG spellings before comparing a path across poses.
const num = /-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?/g;

export function numbers(s) {
    return (s.match(num) ?? []).map(Number);
}

export function pathData(d) {
    const tok = d.match(/[a-df-zA-DF-Z]|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?/g) ?? [];
    const out = [];
    let i = 0;
    let x = 0;
    let y = 0;
    let sx = 0;
    let sy = 0;
    let op = '';
    let last = '';
    let qx = 0;
    let qy = 0;
    while (i < tok.length) {
        if (/^[a-z]$/i.test(tok[i])) {
            op = tok[i++];
        }
        const u = op.toUpperCase();
        const rel = op !== u;
        const count = { M: 2, L: 2, H: 1, V: 1, Q: 4, T: 2, C: 6, S: 4, A: 7, Z: 0 }[u];
        if (count == null) {
            throw Error('Unknown path operator ' + op);
        }
        const n = tok.slice(i, i + count).map(Number);
        i += count;
        if (n.length !== count || n.some(v => !Number.isFinite(v))) {
            throw Error('Invalid path: ' + d);
        }
        const X = j => n[j] + (rel ? x : 0);
        const Y = j => n[j] + (rel ? y : 0);
        let a;
        switch (u) {
            case 'Z':
                out.push([5]);
                x = sx;
                y = sy;
                op = '';
                last = u;
                continue;
            case 'M':
                a = [0, X(0), Y(1)];
                sx = a[1];
                sy = a[2];
                op = rel ? 'l' : 'L';
                break;
            case 'H':
                a = [1, X(0), y];
                break;
            case 'V':
                a = [1, x, Y(0)];
                break;
            case 'L':
                a = [1, X(0), Y(1)];
                break;
            case 'T':
                a = [
                    2,
                    'QT'.includes(last) ? 2 * x - qx : x,
                    'QT'.includes(last) ? 2 * y - qy : y,
                    X(0),
                    Y(1),
                ];
                break;
            case 'Q':
                a = [2, X(0), Y(1), X(2), Y(3)];
                break;
            case 'S':
                a = [
                    3,
                    'CS'.includes(last) ? 2 * x - qx : x,
                    'CS'.includes(last) ? 2 * y - qy : y,
                    X(0),
                    Y(1),
                    X(2),
                    Y(3),
                ];
                break;
            case 'C':
                a = [3, X(0), Y(1), X(2), Y(3), X(4), Y(5)];
                break;
            case 'A':
                a = [4, n[0], n[1], n[2], n[3], n[4], X(5), Y(6)];
                break;
        }
        if (a[0] === 2 || a[0] === 3) {
            qx = a.at(-4);
            qy = a.at(-3);
        }
        x = a.at(-2);
        y = a.at(-1);
        out.push(a);
        last = u;
    }
    return out;
}

export function matrix(s = '') {
    let m = [1, 0, 0, 1, 0, 0];
    const mul = ([a, b, c, d, e, f], [g, h, i, j, k, l]) => [
        a * g + c * h,
        b * g + d * h,
        a * i + c * j,
        b * i + d * j,
        a * k + c * l + e,
        b * k + d * l + f,
    ];
    while (s.trim()) {
        const match = /^\s*(translate|scale|rotate|matrix|skewX|skewY)\(([^)]*)\)\s*,?/.exec(s);
        if (!match) {
            throw Error('Unsupported transform ' + s);
        }
        s = s.slice(match[0].length);
        const [x, y, z, ...tail] = numbers(match[2]);
        const angle = (x * Math.PI) / 180;
        let a;
        switch (match[1]) {
            case 'translate':
                a = [1, 0, 0, 1, x, y ?? 0];
                break;
            case 'scale':
                a = [x, 0, 0, y ?? x, 0, 0];
                break;
            case 'rotate': {
                const c = Math.cos(angle);
                const b = Math.sin(angle);
                const cx = y ?? 0;
                const cy = z ?? 0;
                a = [c, b, -b, c, cx - c * cx + b * cy, cy - b * cx - c * cy];
                break;
            }
            case 'skewX':
                a = [1, 0, Math.tan(angle), 1, 0, 0];
                break;
            case 'skewY':
                a = [1, Math.tan(angle), 0, 1, 0, 0];
                break;
            default:
                a = [x, y, z, ...tail];
                break;
        }
        m = mul(m, a);
    }
    return m.map(x => Math.round(x * 1e8) / 1e8);
}
