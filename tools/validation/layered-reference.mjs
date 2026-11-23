// Render the published inventory with upstream JavaScript and Chromium, independently of the importer.
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';
const [source, manifest, out, chrome] = process.argv.slice(2);
const load = f => import(pathToFileURL(path.resolve(source, f)));
const { renderSVG } = await load('packages/core/src/render.js');
const { EASE, PHASE } = await load('packages/core/src/motion.js');
const { withBadge, withSlash } = await load('packages/core/src/compose.js');
const icons = JSON.parse(await fs.readFile(manifest));
const jobs = [];
for (const d of icons) {
    jobs.push({ name: d.name, from: 0, to: true, t: d.base ? 0.19 : 0, soft: false });
    if (!d.base) {
        jobs.push(
            { name: d.name, from: 0, to: true, t: 0.18, soft: true },
            { name: d.name, from: 0.63, to: false, t: 0.12, soft: false },
        );
    }
}
const definitions = new Map(icons.map(d => [d.name, d]));
await fs.mkdir(out, { recursive: true });
const profile = await fs.mkdtemp(path.join(os.tmpdir(), 'kubai-reference-'));
const browser = spawn(
    chrome,
    [
        '--headless',
        '--disable-gpu',
        '--hide-scrollbars',
        '--no-first-run',
        '--no-default-browser-check',
        '--remote-debugging-port=0',
        '--remote-debugging-address=127.0.0.1',
        '--user-data-dir=' + profile,
        'about:blank',
    ],
    { stdio: 'ignore', windowsHide: true },
);
const pause = ms => new Promise(ok => setTimeout(ok, ms));
try {
    let port;
    for (let i = 0; i < 150 && !port; i++) {
        try {
            port = (await fs.readFile(path.join(profile, 'DevToolsActivePort'), 'utf8'))
                .split('\n')[0]
                .trim();
        } catch {
            await pause(100);
        }
    }
    if (!port) {
        throw Error('Chromium did not start');
    }
    const pages = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
    const ws = new WebSocket(pages.find(p => p.type === 'page').webSocketDebuggerUrl);
    await new Promise((ok, bad) => {
        ws.onopen = ok;
        ws.onerror = bad;
    });
    let id = 0;
    const waiting = new Map();
    ws.onmessage = ({ data }) => {
        const r = JSON.parse(data);
        if (r.id) {
            const [ok, bad] = waiting.get(r.id);
            waiting.delete(r.id);
            r.error ? bad(Error(r.error.message)) : ok(r.result);
        }
    };
    const call = (method, params = {}) =>
        new Promise((ok, bad) => {
            waiting.set(++id, [ok, bad]);
            ws.send(JSON.stringify({ id, method, params }));
        });
    const side = 62;
    const columns = 10;
    const batch = 100;
    await call('Emulation.setDeviceMetricsOverride', {
        width: side * columns,
        height: side * columns,
        deviceScaleFactor: 1,
        mobile: false,
    });
    for (let start = 0; start < jobs.length; start += batch) {
        let svg =
            '<svg xmlns="http://www.w3.org/2000/svg" width="620" height="620"><rect width="100%" height="100%" fill="#192637"/>';
        for (const [i, j] of jobs.slice(start, start + batch).entries()) {
            const d = definitions.get(j.name);
            const base = (await load('icons/' + (d.base ?? d.name) + '.js')).default;
            const def = !d.base
                ? base
                : d.badge === 'off'
                  ? withSlash(base, (await load('badges/_slash.js')).default)
                  : withBadge(base, (await load('badges/' + d.badge + '.js')).default);
            const e = EASE(j.t);
            const h = j.from + ((j.to ? 1 : 0) - j.from) * e;
            globalThis[PHASE] = { out: !j.to, e };
            svg += renderSVG(def, {
                k: 'ref' + i,
                size: side,
                pad: 0,
                progress: h,
                variant: j.soft ? 'soft' : 'color',
                badge: '#F01487',
            }).replace(
                '<svg ',
                '<svg x="' + (i % columns) * side + '" y="' + Math.floor(i / columns) * side + '" ',
            );
            delete globalThis[PHASE];
        }
        const page = await call('Runtime.evaluate', {
            expression:
                'document.body.style.margin="0";document.body.innerHTML=' + JSON.stringify(svg + '</svg>'),
        });
        if (page.exceptionDetails) {
            throw Error('Failed to render upstream sheet');
        }
        const shot = await call('Page.captureScreenshot', { format: 'png', fromSurface: true });
        await fs.writeFile(path.join(out, 'reference-' + start + '.png'), Buffer.from(shot.data, 'base64'));
    }
    await fs.writeFile(path.join(out, 'poses.json'), JSON.stringify(jobs));
    console.log(jobs.length + ' upstream poses across ' + icons.length + ' published Layered identities.');
    ws.close();
} finally {
    browser.kill();
    await fs.rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 }).catch(() => {});
}
