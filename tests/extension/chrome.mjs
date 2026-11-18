// Loads an unpacked build of the extension into a throwaway Chrome profile over the DevTools pipe (no port is opened),
// opens the synthetic pages of pages.mjs from a local server (a QA build matches it) and, with --live, the public
// sign-in page of univer.kaznu.kz (no credentials are typed). Prints what the extension did and saves screenshots.
//
//   node tests/extension/chrome.mjs --ext <build>\chromium --out <folder> [--live] [--dark] [--width 1280] [--only /path/]
//       [--click <selector>]   a real mouse click on the first match on the first page only; files it downloads land in
//                              <folder>\downloads (open the same page twice to see what the click stored)
//       [--together]           open all pages at once, each in its own window (two tabs racing for one form)
//       [--settle 1800]        milliseconds to wait after the load before reading the page
//       [--front 5000]         with --together: then bring each tab to the front in turn for this long (a hidden tab
//                              confirms nothing, so this is how a stale copy behind the sending tab gets its turn)
//       [--settings <json>]    settings as the popup stores them, e.g. {"confirm":false}
//       [--popup]              the settings popup instead of pages: each language in light and dark, then a click on
//                              «Оригинальный вид» and what it stored
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pages } from './pages.mjs';
import { start } from './server.mjs';

const options = { ext: '', out: '', chrome: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', port: 8765, live: false, dark: false, width: 1280, height: 900, only: '', settings: '', click: '', together: false, settle: 1800, front: 0, popup: false };
for (let i = 2; i < process.argv.length; i++) {
    const key = process.argv[i].replace(/^--/, '');
    if (typeof options[key] === 'boolean') options[key] = true;
    else options[key] = typeof options[key] === 'number' ? Number(process.argv[++i]) : process.argv[++i];
}
if (!options.ext || !options.out) { console.error('--ext and --out are required'); process.exit(2); }
fs.mkdirSync(options.out, { recursive: true });

const server = await start(options.port);

const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'univer-ext-qa-'));
const chrome = spawn(options.chrome, ['--headless=new', '--remote-debugging-pipe', '--enable-unsafe-extension-debugging', '--user-data-dir=' + profile,
    '--no-first-run', '--no-default-browser-check', '--disable-sync', '--disable-component-update', '--lang=ru', 'about:blank'], { stdio: ['ignore', 'ignore', 'pipe', 'pipe', 'pipe'] });
let stderr = '';
chrome.stdio[2].on('data', chunk => { stderr += chunk; });

// The DevTools protocol over the pipe: JSON messages separated by a NUL byte.
let next = 0, buffer = Buffer.alloc(0);
const pending = new Map(), listeners = [];
chrome.stdio[4].on('data', chunk => {
    buffer = Buffer.concat([buffer, chunk]);
    let end;
    while ((end = buffer.indexOf(0)) >= 0) {
        const message = JSON.parse(buffer.subarray(0, end).toString('utf8'));
        buffer = buffer.subarray(end + 1);
        if (message.id && pending.has(message.id)) {
            const waiting = pending.get(message.id); pending.delete(message.id);
            if (message.error) waiting.reject(new Error(waiting.method + ': ' + message.error.message)); else waiting.resolve(message.result);
        } else listeners.forEach(listener => listener(message));
    }
});
function send(method, params = {}, sessionId) {
    const id = ++next;
    chrome.stdio[3].write(JSON.stringify(sessionId ? { id, method, params, sessionId } : { id, method, params }) + '\0');
    return new Promise((resolve, reject) => pending.set(id, { resolve, reject, method }));
}
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

const results = [];
try {
    const { id: extensionId } = await send('Extensions.loadUnpacked', { path: path.resolve(options.ext) });
    if (options.settings) {
        // Settings as the popup would store them, written from the extension's own page.
        const { targetId } = await send('Target.createTarget', { url: 'chrome-extension://' + extensionId + '/manifest.json' });
        const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });
        await wait(500);
        const stored = await send('Runtime.evaluate', { expression: 'chrome.storage.local.set({settings:' + JSON.stringify(JSON.parse(options.settings)) + '}).then(() => chrome.storage.local.get("settings")).then(v => JSON.stringify(v.settings))', awaitPromise: true, returnByValue: true }, sessionId);
        if (stored.exceptionDetails || !stored.result || typeof stored.result.value !== 'string') throw new Error('settings were not stored: ' + JSON.stringify(stored.exceptionDetails || stored.result));
        await send('Target.closeTarget', { targetId });
    }
    const list = options.only ? options.only.split(',') : Object.keys(pages);
    const urls = list.map(route => 'http://127.0.0.1:' + options.port + route);
    if (options.live) urls.push('https://univer.kaznu.kz/user/login');
    if (options.popup) results.push(...await popup(extensionId));
    else if (options.together) results.push(...await Promise.all(urls.map((url, index) => check(url, index))));
    else for (const [index, url] of urls.entries()) results.push(await check(url, index));
} catch (error) {
    results.push({ error: String(error && error.stack || error), stderr: stderr.slice(-2000) });
} finally {
    chrome.kill();
    await server.close();
    try { fs.rmSync(profile, { recursive: true, force: true }); } catch (_) { }
}
console.log(JSON.stringify({ results, requests: server.log }, null, 2));

async function check(url, index) {
    const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });
    const errors = [], contexts = [];
    const listener = message => {
        if (message.sessionId !== sessionId) return;
        if (message.method === 'Runtime.exceptionThrown') {
            const detail = message.params.exceptionDetails;
            errors.push((detail.exception && detail.exception.description || detail.text).split('\n')[0] + ' @ ' + (detail.url || '') + ':' + detail.lineNumber);
        } else if (message.method === 'Runtime.consoleAPICalled' && message.params.type === 'error') {
            errors.push('console: ' + message.params.args.map(arg => arg.value || arg.description).join(' '));
        } else if (message.method === 'Runtime.executionContextCreated') contexts.push(message.params.context);
        else if (message.method === 'Log.entryAdded' && message.params.entry.level === 'error') errors.push('log: ' + message.params.entry.text + ' ' + (message.params.entry.url || ''));
    };
    listeners.push(listener);
    await send('Page.enable', {}, sessionId);
    await send('Runtime.enable', {}, sessionId);
    await send('Log.enable', {}, sessionId);
    await send('Emulation.setDeviceMetricsOverride', { width: options.width, height: options.height, deviceScaleFactor: 1, mobile: false }, sessionId);
    if (options.dark) await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'dark' }] }, sessionId);
    const loaded = new Promise(resolve => {
        const done = message => { if (message.sessionId === sessionId && message.method === 'Page.loadEventFired') { listeners.splice(listeners.indexOf(done), 1); resolve(); } };
        listeners.push(done);
    });
    const started = Date.now();
    await send('Page.navigate', { url }, sessionId);
    await Promise.race([loaded, wait(20000)]);
    const loadMs = Date.now() - started;
    await wait(options.settle);
    // Only the front tab is visible and the extension acts only in a visible one: each tab in turn is brought forward.
    if (options.front) { await wait(index * options.front); await send('Page.bringToFront', {}, sessionId); await wait(options.front); }
    const evaluate = async (expression, contextId) => {
        const answer = await send('Runtime.evaluate', contextId ? { expression, contextId, returnByValue: true } : { expression, returnByValue: true }, sessionId);
        return answer.exceptionDetails ? { error: answer.exceptionDetails.text } : answer.result.value;
    };
    const page = await evaluate(`(() => { const root = document.documentElement, style = getComputedStyle(root);
        return { url: location.href, visible: document.visibilityState, opacity: style.opacity, pointer: style.pointerEvents, reading: root.classList.contains('univer-mobile'),
            desktopStyle: !!document.getElementById('univer-desktop-style'), dark: !!document.querySelector('#univer-theme-dark,[data-univer-theme=dark]') || root.classList.contains('univer-app-dark'),
            appNodes: document.querySelectorAll('[class*="univer-app"]').length, pinned: document.querySelectorAll('.univer-app-pin[aria-pressed="true"]').length, bodyText: (document.body && document.body.innerText || '').slice(0, 160) }; })()`);
    const isolated = contexts.filter(context => context.auxData && context.auxData.type === 'isolated' && context.auxData.frameId && context.name && /Simple Univer/i.test(context.name));
    let world = null;
    if (isolated.length) world = await evaluate(`JSON.stringify({ ext: !!globalThis.__univerExtV1, host: !!globalThis.__univerHostV1, lang: globalThis.__univerHostV1 && globalThis.__univerHostV1.language(),
        enabled: globalThis.__univerDesktop && globalThis.__univerDesktop.enabled, viewer: globalThis.__univerDesktop && globalThis.__univerDesktop.viewer, gatePending: globalThis.__univerPaintV1 && globalThis.__univerPaintV1.pending,
        modules: ['__univerCommonV1','__univerMobileV1','__univerPolishV1','__univerReportV1','__univerShortcutsV1','__univerNoticesV1','__univerThemeV1','__univerPaintV1'].filter(n => !!globalThis[n]) })`, isolated[isolated.length - 1].id);
    let clicked = null;
    if (options.click && index === 0) clicked = await click(sessionId, evaluate);
    const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false }, sessionId);
    const file = path.join(options.out, new URL(url).pathname.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') + (options.dark ? '-dark' : '') + (options.together ? '-' + index : '') + '.png');
    fs.writeFileSync(file, Buffer.from(shot.data, 'base64'));
    listeners.splice(listeners.indexOf(listener), 1);
    await send('Target.closeTarget', { targetId });
    return { url, loadMs, page, world: world ? JSON.parse(world) : 'no isolated world', clicked, errors, screenshot: file };
}

// The popup as the toolbar shows it (360 px wide, as tall as its content), in each language and theme.
async function popup(extensionId) {
    const shots = [], page = 'chrome-extension://' + extensionId + '/popup/popup.html';
    const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });
    await send('Page.enable', {}, sessionId); await send('Runtime.enable', {}, sessionId);
    const errors = [];
    listeners.push(message => { if (message.sessionId === sessionId && message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.exception?.description || message.params.exceptionDetails.text); });
    const evaluate = async expression => {
        const answer = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId);
        if (answer.exceptionDetails) throw new Error(expression.slice(0, 80) + ': ' + JSON.stringify(answer.exceptionDetails));
        return answer.result.value;
    };
    await send('Emulation.setDeviceMetricsOverride', { width: 360, height: 600, deviceScaleFactor: 2, mobile: false }, sessionId);
    await send('Page.navigate', { url: page }, sessionId); await wait(600);
    for (const lang of ['ru', 'kz', 'en']) for (const theme of ['light', 'dark']) {
        await evaluate(`chrome.storage.local.set({ language: '${lang}', settings: { readable: true, theme: '${theme}', motion: true, confirm: true } })`);
        await send('Page.reload', {}, sessionId); await wait(500);
        const size = await evaluate(`({ height: Math.ceil(document.documentElement.getBoundingClientRect().height), wide: document.documentElement.scrollWidth > 360,
            broken: Array.from(document.querySelectorAll('.segments span, .layout .name, .open')).filter(n => n.scrollWidth > n.clientWidth + 1).map(n => n.textContent) })`);
        await send('Emulation.setDeviceMetricsOverride', { width: 360, height: size.height, deviceScaleFactor: 2, mobile: false }, sessionId);
        const shot = await send('Page.captureScreenshot', { format: 'png' }, sessionId);
        const file = path.join(options.out, 'popup-' + lang + '-' + theme + '.png');
        fs.writeFileSync(file, Buffer.from(shot.data, 'base64'));
        shots.push({ lang, theme, height: size.height, overflowsWidth: size.wide, clipped: size.broken, screenshot: file });
    }
    // A real click on «Оригинальный вид» stores readable:false.
    const box = await evaluate(`(() => { const r = document.querySelector('input[value=original]').closest('label').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()`);
    for (const type of ['mousePressed', 'mouseReleased'])
        await send('Input.dispatchMouseEvent', { type, x: box.x, y: box.y, button: 'left', clickCount: 1 }, sessionId);
    await wait(300);
    const stored = await evaluate(`chrome.storage.local.get('settings').then(v => v.settings)`);
    await send('Target.closeTarget', { targetId });
    return [{ popup: shots, afterClick: stored, errors }];
}

// A trusted click (Input.dispatchMouseEvent) on options.click, then the labels the element shows while the work runs and
// the files that reach the download folder.
async function click(sessionId, evaluate) {
    const folder = path.join(options.out, 'downloads');
    fs.rmSync(folder, { recursive: true, force: true }); fs.mkdirSync(folder, { recursive: true });
    await send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: folder, eventsEnabled: true });
    // "a>>b" clicks a, then b. Each step takes the first match that is drawn (menus keep hidden copies); the last one is
    // marked so later reads find the same element.
    let box = null;
    for (const step of options.click.split('>>')) {
        box = await evaluate(`(() => { const element = Array.from(document.querySelectorAll(${JSON.stringify(step)})).find(node => node.getClientRects().length && node.getBoundingClientRect().width);
            if (!element) return null; document.querySelectorAll('[data-qa-target]').forEach(node => node.removeAttribute('data-qa-target')); element.setAttribute('data-qa-target', ''); element.scrollIntoView({ block: 'center' });
            const rect = element.getBoundingClientRect(); return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2, label: element.dataset.label || element.getAttribute('aria-label') || element.textContent }; })()`);
        if (!box) return { error: 'nothing visible matches ' + step };
        await wait(200);
        for (const type of ['mouseMoved', 'mousePressed', 'mouseReleased'])
            await send('Input.dispatchMouseEvent', { type, x: box.x, y: box.y, button: type === 'mouseMoved' ? 'none' : 'left', clickCount: type === 'mouseMoved' ? 0 : 1 }, sessionId);
        await wait(400);
    }
    const labels = [box.label];
    for (let i = 0; i < 40; i++) {
        await wait(250);
        const label = await evaluate(`(() => { const element = document.querySelector('[data-qa-target]'); return element ? element.dataset.label || element.getAttribute('aria-label') || element.textContent : null; })()`);
        if (labels[labels.length - 1] !== label) labels.push(label);
    }
    const toast = await evaluate(`(() => { const toast = document.querySelector('.univer-ext-toast'); return toast ? toast.textContent : null; })()`);
    // Chrome's own component updates (CRX, ".crdownload") can land in the same folder; they are not the page's files.
    return { labels, toast, files: fs.readdirSync(folder).filter(name => !name.endsWith('.crdownload')).sort() };
}
