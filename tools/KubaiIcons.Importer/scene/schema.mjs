// Supported drawing semantics. A new SVG feature needs a compiler adapter, not
// an ignored attribute or a decoder branch keyed by the icon's name.
const slots = {
    '#root': '',
    defs: '',
    linearGradient: 'id x1 y1 x2 y2 gradientUnits',
    stop: 'offset stop-color',
    mask: 'id maskUnits x y width height',
    clipPath: 'id',
    filter: 'id x y width height color-interpolation-filters',
    g: 'd',
    path: 'd pathLength',
    circle: 'cx cy r',
    ellipse: 'cx cy rx ry',
    rect: 'x y width height',
    text: 'x y text-anchor font-size font-weight font-family',
};
const drawing = new Set(['g', 'path', 'circle', 'ellipse', 'rect', 'text']);
const common =
    'fill stroke fill-rule clip-rule fill-opacity stroke-opacity ' +
    'stroke-width stroke-linecap stroke-linejoin stroke-dasharray stroke-dashoffset ' +
    'opacity transform clip-path mask filter data-n data-under data-over';
const attributes = new Map(
    Object.entries(slots).map(([tag, list]) => [
        tag,
        new Set((list + ' ' + (drawing.has(tag) ? common : '')).trim().split(/\s+/)),
    ]),
);

export function validateTree(node) {
    if (typeof node === 'string') {
        return;
    }
    const allowed = attributes.get(node.tag);
    if (!allowed) {
        throw Error('Unsupported SVG element: ' + node.tag);
    }
    for (const [key] of node.attrs) {
        if (!allowed.has(key)) {
            throw Error('Unsupported ' + node.tag + ' attribute: ' + key);
        }
    }
    if (node.tag === 'filter') {
        if (!['sh-K', 'hi-K'].includes(node.getAttribute('id'))) {
            throw Error('Unsupported filter: ' + node.getAttribute('id'));
        }
        return; // The two standard kit filters have native vector equivalents.
    }
    if (node.tag === 'mask') {
        const [base, hole] = node.children;
        const knockout =
            node.children.length === 2 &&
            base.tag === 'rect' &&
            base.getAttribute('fill') === '#fff' &&
            hole.tag === 'path' &&
            (hole.getAttribute('fill') === '#000' ||
                (hole.getAttribute('fill') === 'none' && hole.getAttribute('stroke') === '#000'));
        const gradient =
            node.children.length === 1 && base.tag === 'rect' && /^url\(#/.test(base.getAttribute('fill'));
        if (!knockout && !gradient) {
            throw Error('Unsupported mask: ' + node.getAttribute('id'));
        }
    }
    node.children.forEach(validateTree);
}
