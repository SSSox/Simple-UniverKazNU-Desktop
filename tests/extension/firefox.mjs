// The Firefox counterpart of chrome.mjs: a throwaway profile, the build installed as a temporary add-on through
// WebDriver BiDi on a local port that lives only as long as this run, the same synthetic pages and, with --live, the
// public sign-in page. Firefox gives no access to the extension's own world here, so the checks read the page's DOM.
//
//   node tests/extension/firefox.mjs --ext <build>\firefox --out <folder> [--live] [--dark] [--only /path/]
//       [--click <selector>]   a real mouse click on the first visible match on the first page only; files it downloads
//                              land in <folder>\ff-downloads (open the same page twice to see what the click stored)
//       [--together]           open all pages at once, each in its own window (two tabs racing for one form)
//       [--settle 1800]        milliseconds to wait after the load before reading the page
//       [--front 5000]         with --together: then bring each window to the front in turn for this long
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pages } from './pages.mjs';
import { start } from './server.mjs';

const options = { ext: '', out: '', firefox: 'C:\\Program Files\\Mozilla Firefox\\firefox.exe', port: 8765, bidi: 9444, live: false, dark: false, width: 1280, height: 900, only: '', click: '', together: false, settle: 1800, front: 0 };
for (let i = 2; i < process.argv.length; i++) {
    const key = process.argv[i].replace(/^--/, '');
    if (typeof options[key] === 'boolean') options[key] = true;
    else options[key] = typeof options[key] === 'number' ? Number(process.argv[++i]) : process.argv[++i];
}
if (!options.ext || !options.out) { console.error('--ext and --out are required'); process.exit(2); }
fs.mkdirSync(options.out, { recursive: true });
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

const server = await start(options.port);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'univer-ext-ff-'));
fs.writeFileSync(path.join(profile, 'user.js'), [
    'user_pref("browser.shell.checkDefaultBrowser", false);',
    'user_pref("datareporting.policy.dataSubmissionEnabled", false);',
    'user_pref("toolkit.telemetry.reportingpolicy.firstRun", false);',
    'user_pref("browser.startup.homepage_override.mstone", "ignore");',
    'user_pref("app.update.disabledForTesting", true);',
    'user_pref("extensions.autoDisableScopes", 0);',
    'user_pref("intl.accept_languages", "ru");',
    options.dark ? 'user_pref("layout.css.prefers-color-scheme.content-override", 0);' : 'user_pref("layout.css.prefers-color-scheme.content-override", 1);',
    'user_pref("browser.download.folderList", 2);',
    'user_pref("browser.download.useDownloadDir", true);',
    'user_pref("browser.download.always_ask_before_handling_new_types", false);',
    'user_pref("browser.download.dir", ' + JSON.stringify(path.resolve(options.out, 'ff-downloads')) + ');',
].join('\n'));
fs.rmSync(path.join(options.out, 'ff-downloads'), { recursive: true, force: true });
fs.mkdirSync(path.join(options.out, 'ff-downloads'), { recursive: true });
const firefox = spawn(options.firefox, ['--headless', '--no-remote', '--profile', profile, '--remote-debugging-port=' + options.bidi, 'about:blank'], { stdio: ['ignore', 'pipe', 'pipe'] });
let output = '';
const ready = new Promise((resolve, reject) => {
    const seen = chunk => { output += chunk; if (/WebDriver BiDi listening on (ws:\/\/\S+)/.test(output)) resolve(RegExp.$1); };
    firefox.stdout.on('data', seen); firefox.stderr.on('data', seen);
    setTimeout(() => reject(new Error('Firefox did not start BiDi: ' + output.slice(-800))), 30000);
});

let socket, next = 0;
const pending = new Map(), events = [];
function send(method, params = {}) {
    const id = ++next;
    socket.send(JSON.stringify({ id, method, params }));
    return new Promise((resolve, reject) => pending.set(id, { resolve, reject, method }));
}
const results = [];
try {
    const address = await ready;
    socket = new WebSocket(address.replace(/\/?$/, '') + '/session');
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = () => reject(new Error('BiDi socket failed')); });
    socket.onmessage = event => {
        const message = JSON.parse(event.data);
        if (message.id && pending.has(message.id)) {
            const waiting = pending.get(message.id); pending.delete(message.id);
            if (message.type === 'error') waiting.reject(new Error(waiting.method + ': ' + message.error + ' ' + message.message)); else waiting.resolve(message.result);
        } else if (message.method) events.push(message);
    };
    await send('session.new', { capabilities: {} });
    const installed = await send('webExtension.install', { extensionData: { type: 'path', path: path.resolve(options.ext) } });
    await send('session.subscribe', { events: ['log.entryAdded'] });
    const tree = await send('browsingContext.getTree', {});
    const context = tree.contexts[0].context;
    await send('browsingContext.setViewport', { context, viewport: { width: options.width, height: options.height } });
    const list = options.only ? options.only.split(',') : Object.keys(pages);
    const urls = list.map(route => 'http://127.0.0.1:' + options.port + route);
    if (options.live) urls.push('https://univer.kaznu.kz/user/login');
    if (options.together) {
        // Each page in its own window: a second tab would be hidden, and hidden pages are left alone by the extension.
        const contexts = [context];
        for (let i = 1; i < urls.length; i++) {
            const created = await send('browsingContext.create', { type: 'window' });
            await send('browsingContext.setViewport', { context: created.context, viewport: { width: options.width, height: options.height } });
            contexts.push(created.context);
        }
        results.push(...await Promise.all(urls.map((url, index) => visit(contexts[index], url, index))));
    } else for (const [index, url] of urls.entries()) results.push(await visit(context, url, index));
    results.unshift({ extension: installed.extension });
} catch (error) {
    results.push({ error: String(error && error.stack || error), output: output.slice(-1500) });
} finally {
    try { socket && socket.close(); } catch (_) { }
    firefox.kill();
    await server.close();
    await wait(800);
    try { fs.rmSync(profile, { recursive: true, force: true }); } catch (_) { }
}
console.log(JSON.stringify({ results, requests: server.log }, null, 2));

async function visit(context, url, index) {
    const before = events.length, started = Date.now();
    await send('browsingContext.navigate', { context, url, wait: 'complete' });
    const loadMs = Date.now() - started;
    await wait(options.settle);
    // As in chrome.mjs: each window in turn comes to the front, since a hidden page confirms nothing.
    if (options.front) { await wait(index * options.front); await send('browsingContext.activate', { context }); await wait(options.front); }
    const answer = await send('script.evaluate', { target: { context }, awaitPromise: false, resultOwnership: 'none', expression: `JSON.stringify((() => { const root = document.documentElement, style = getComputedStyle(root);
        return { url: location.href, visible: document.visibilityState, dark: root.classList.contains('univer-app-dark'), still: root.classList.contains('univer-app-still'), opacity: style.opacity, pointer: style.pointerEvents, reading: root.classList.contains('univer-mobile'), desktopStyle: !!document.getElementById('univer-desktop-style'),
            appNodes: document.querySelectorAll('[class*="univer-app"]').length, pinned: document.querySelectorAll('.univer-app-pin[aria-pressed="true"]').length, lang: (document.querySelector('.univer-app-flags img:not(a img)') || {}).src || '',
            bodyText: (document.body && document.body.innerText || '').slice(0, 120) }; })())` });
    let clicked = null;
    if (options.click && index === 0) clicked = await click(context);
    const shot = await send('browsingContext.captureScreenshot', { context });
    const file = path.join(options.out, 'ff-' + new URL(url).pathname.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') + (options.dark ? '-dark' : '') + (options.together ? '-' + index : '') + '.png');
    fs.writeFileSync(file, Buffer.from(shot.data, 'base64'));
    const errors = events.slice(before).filter(event => event.method === 'log.entryAdded' && /error|warn/.test(event.params.level)).map(event => event.params.level + ': ' + event.params.text);
    return { url, loadMs, page: JSON.parse(answer.result.value), clicked, errors, screenshot: file };
}

// A trusted click (input.performActions) on options.click, then the labels the element shows while the work runs and the
// files that reach the download folder.
async function click(context) {
    const read = async expression => (await send('script.evaluate', { target: { context }, awaitPromise: false, resultOwnership: 'none', expression })).result.value;
    // "a>>b" clicks a, then b. Each step takes the first match that is drawn (menus keep hidden copies); the last one is
    // marked so later reads find the same element.
    let box = null;
    for (const step of options.click.split('>>')) {
        box = JSON.parse(await read(`JSON.stringify((() => { const element = Array.from(document.querySelectorAll(${JSON.stringify(step)})).find(node => node.getClientRects().length && node.getBoundingClientRect().width);
            if (!element) return null; document.querySelectorAll('[data-qa-target]').forEach(node => node.removeAttribute('data-qa-target')); element.setAttribute('data-qa-target', ''); element.scrollIntoView({ block: 'center' });
            const rect = element.getBoundingClientRect(); return { x: Math.round(rect.left + rect.width / 2), y: Math.round(rect.top + rect.height / 2), label: element.dataset.label || element.getAttribute('aria-label') || element.textContent }; })())`));
        if (!box) return { error: 'nothing visible matches ' + step };
        await wait(200);
        await send('input.performActions', { context, actions: [{ type: 'pointer', id: 'mouse', parameters: { pointerType: 'mouse' },
            actions: [{ type: 'pointerMove', x: box.x, y: box.y }, { type: 'pointerDown', button: 0 }, { type: 'pointerUp', button: 0 }] }] });
        await wait(400);
    }
    const labels = [box.label];
    for (let i = 0; i < 40; i++) {
        await wait(250);
        const label = await read(`(() => { const element = document.querySelector('[data-qa-target]'); return element ? element.dataset.label || element.getAttribute('aria-label') || element.textContent : null; })()`);
        if (labels[labels.length - 1] !== label) labels.push(label);
    }
    const toast = await read(`(() => { const toast = document.querySelector('.univer-ext-toast'); return toast ? toast.textContent : null; })()`);
    const folder = path.join(options.out, 'ff-downloads');
    return { labels, toast, files: fs.readdirSync(folder).filter(name => !name.endsWith('.part')).sort() };
}
