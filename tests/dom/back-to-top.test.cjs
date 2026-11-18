const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync('src/Univer.Desktop/Assets/back-to-top.js', 'utf8');
const desktop = fs.readFileSync('src/Univer.Desktop/Assets/desktop.js', 'utf8');
// A 1000×800 window (or `view`) over a page `height` px tall; `controls` are links in #l4 whose rectangles are given in
// page coordinates and reported against the current scroll position.
function page(body, controls = [], height = 6000, view = { width: 1000, height: 800 }) {
  const dom = new JSDOM('<!doctype html><html><body>' + body + '<div id="l4"></div></body></html>', { url: 'https://univer.kaznu.kz/news/', runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window, d = w.document;
  Object.defineProperty(w, 'innerWidth', { configurable: true, value: view.width });
  Object.defineProperty(w, 'innerHeight', { configurable: true, value: view.height });
  Object.defineProperty(d.documentElement, 'scrollHeight', { configurable: true, get: () => height });
  let y = 0; Object.defineProperty(w, 'scrollY', { configurable: true, get: () => y });
  const scrolls = []; w.scrollTo = (options) => { scrolls.push(options); y = options.top; };
  const nodes = controls.map((rect, i) => {
    const node = d.createElement('a'); node.href = '/ssc/apply/' + i; node.textContent = 'Подать заявку'; d.getElementById('l4').appendChild(node);
    node.getBoundingClientRect = () => ({ left: rect.left, width: rect.width, height: rect.height, right: rect.left + rect.width, top: rect.top - y, bottom: rect.top + rect.height - y });
    return node;
  });
  w.eval(script);
  const top = w.__univerTopV1;
  const host = () => d.getElementById('univer-app-top');
  const button = () => host()?.shadowRoot.querySelector('button');
  const room = () => d.documentElement.getAttribute('data-univer-top-room');
  return { w, d, top, host, button, room, scrolls, scroll(to) { y = to; }, nodes };
}
const site = '<div id="site"><p>Новости</p></div>';

test('the button waits until the page is well down, then shows a little above the bottom right corner with its label', () => {
  const p = page(site);
  p.top.configure(true, 'ru');
  assert.ok(p.host(), 'the host is added once reading mode is on');
  assert.equal(p.button().classList.contains('on'), false, 'not at the top of the page');
  p.scroll(900); p.top.refresh();
  assert.equal(p.button().classList.contains('on'), false, 'one screen down is not yet far');
  p.scroll(2000); p.top.refresh();
  const b = p.button();
  assert.equal(b.classList.contains('on'), true);
  assert.equal(b.textContent, 'Наверх');
  assert.equal(b.type, 'button');
  const css = b.parentNode.querySelector('style').textContent;
  assert.match(css, /\.top\{position:fixed;[^}]*right:24px;bottom:56px;/);
  assert.doesNotMatch(css, /bottom \.2s/, 'nothing animates a move: the button has one place');
  p.scroll(600); p.top.refresh();
  assert.equal(b.classList.contains('on'), false, 'back near the top it hides');
  p.w.close();
});

test('labels follow the app language', () => {
  for (const [lang, words] of [['kz', 'Жоғарыға'], ['en', 'Back to top'], ['xx', 'Наверх']]) {
    const p = page(site); p.top.configure(true, lang); p.scroll(2000); p.top.refresh();
    assert.equal(p.button().textContent, words, lang); p.w.close();
  }
});

test('the button never moves: a control under its place gets room beside the page instead', () => {
  // «Подать заявку» at the right edge, far down the page: it would pass under the button.
  const p = page(site, [{ left: 860, top: 3000, width: 110, height: 32 }]);
  p.top.configure(true, 'ru');
  assert.equal(p.room(), '160', 'the page is told to make room as soon as it is set up');
  for (const depth of [1000, 2000, 2600, 5200]) {
    p.scroll(depth); p.top.refresh();
    const b = p.button();
    assert.equal(b.style.left + b.style.right + b.style.bottom, '', 'no inline position: the button stays where its style puts it');
  }
  assert.equal(p.button().classList.contains('on'), true);
  // The narrower column moves the control away; the room stays for the rest of the page instead of flipping back.
  p.nodes[0].remove(); p.top.refresh();
  assert.equal(p.room(), '160');
  p.w.close();
});

test('room is made only for a small control the button could actually cover', () => {
  const cases = [
    ['a row that is one wide link', [{ left: 0, top: 3000, width: 1000, height: 48 }], 6000],
    ['a control near the top, above where the button ever shows', [{ left: 860, top: 600, width: 110, height: 32 }], 6000],
    ['a control left of the button', [{ left: 600, top: 3000, width: 200, height: 32 }], 6000],
    ['a page too short for the button', [{ left: 860, top: 1000, width: 110, height: 32 }], 1700],
    ['a cell of a box that scrolls sideways, past the window edge', [{ left: 900, top: 3000, width: 300, height: 32 }], 6000]
  ];
  for (const [name, controls, height] of cases) {
    const p = page(site, controls, height);
    p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
    assert.equal(p.room(), null, name);
    p.w.close();
  }
  // The same check after the page was scrolled: rectangles are read against the scroll position.
  const q = page(site, [{ left: 860, top: 3000, width: 110, height: 32 }]);
  q.scroll(2500); q.top.configure(true, 'ru');
  assert.equal(q.room(), '160');
  q.w.close();
});

test('a control the button passes on the way back up gets room too: the button stays shown until 0.9 screens', () => {
  // Below 2.2 screens the button never appears on the way down, but scrolling back from far down it stays until 0.9.
  const p = page(site, [{ left: 860, top: 1470, width: 110, height: 32 }]);
  p.top.configure(true, 'ru');
  assert.equal(p.room(), '160');
  p.w.close();
});

test('a zoomed window still makes room; one too narrow to spare it keeps its column and shows no button over a control', () => {
  const short = page(site, [{ left: 860, top: 3000, width: 110, height: 32 }], 6000, { width: 1000, height: 250 });
  short.top.configure(true, 'ru'); short.scroll(3000); short.top.refresh();
  assert.equal(short.room(), null, 'a 250 px tall window never shows the button');
  assert.equal(short.button().classList.contains('on'), false);
  short.w.close();
  // A 552 px page: 160 px of room leaves 392 px, so the page makes room.
  const zoomed = page(site, [{ left: 420, top: 3000, width: 100, height: 32 }], 6000, { width: 552, height: 400 });
  zoomed.top.configure(true, 'ru');
  assert.equal(zoomed.room(), '160');
  zoomed.w.close();
  // 480 px wide: the room would leave 320 px of page. The column stays, and the button stays away instead of covering.
  const narrow = page(site, [{ left: 360, top: 3000, width: 100, height: 32 }], 6000, { width: 480, height: 800 });
  narrow.top.configure(true, 'ru'); narrow.scroll(3000); narrow.top.refresh();
  assert.equal(narrow.room(), null);
  assert.equal(narrow.button().classList.contains('on'), false, 'no room and a control under its place: no button');
  narrow.w.close();
  // The same window over a page whose controls stay clear of the button still shows it.
  const clear = page(site, [{ left: 16, top: 3000, width: 100, height: 32 }], 6000, { width: 480, height: 800 });
  clear.top.configure(true, 'ru'); clear.scroll(3000); clear.top.refresh();
  assert.equal(clear.button().classList.contains('on'), true);
  clear.w.close();
});

test('a resized window gives the room once it can spare it, and drops it again when it cannot', async () => {
  const wait = (ms) => new Promise((done) => setTimeout(done, ms));
  // One control under the button's place in a 480 px window, another under it in a 1000 px one.
  const p = page(site, [{ left: 360, top: 3000, width: 100, height: 32 }, { left: 860, top: 3200, width: 110, height: 32 }], 6000, { width: 480, height: 800 });
  const resize = (width) => { Object.defineProperty(p.w, 'innerWidth', { configurable: true, value: width }); p.w.dispatchEvent(new p.w.Event('resize')); };
  p.top.configure(true, 'ru'); p.scroll(3000); p.top.refresh();
  assert.equal(p.room(), null);
  assert.equal(p.button().classList.contains('on'), false, 'too narrow for the room: no button over the control');
  resize(1000); await wait(400);
  assert.equal(p.room(), '160', 'the wider window makes room at once');
  assert.equal(p.button().classList.contains('on'), true, 'and the button is back');
  resize(480); await wait(400);
  assert.equal(p.room(), null);
  assert.equal(p.button().classList.contains('on'), false);
  p.w.close();
});

test('a room given up is checked again at once, and a clock set back does not stop the checks', () => {
  const add = (p, rect) => {
    const node = p.d.createElement('a'); node.href = '/ssc/apply/late'; node.textContent = 'Подать заявку'; p.d.getElementById('l4').appendChild(node);
    node.getBoundingClientRect = () => ({ left: rect.left, width: rect.width, height: rect.height, right: rect.left + rect.width, top: rect.top - p.w.scrollY, bottom: rect.top + rect.height - p.w.scrollY });
  };
  // The window narrows with no resize event yet, within a second of the last check: the control under the button is seen now.
  const p = page(site, [{ left: 360, top: 3000, width: 100, height: 32 }, { left: 860, top: 3200, width: 110, height: 32 }]);
  p.top.configure(true, 'ru'); p.scroll(3000); p.top.configure(true, 'ru');
  assert.equal(p.room(), '160');
  Object.defineProperty(p.w, 'innerWidth', { configurable: true, value: 480 });
  p.top.configure(true, 'ru');
  assert.equal(p.room(), null);
  assert.equal(p.button().classList.contains('on'), false, 'the button does not stand over the control meanwhile');
  p.w.close();
  // A control that appears after the clock was set back a minute is still checked.
  const q = page(site);
  q.top.configure(true, 'ru'); q.scroll(3000);
  const now = q.w.Date.now; q.w.Date.now = () => now() - 60000;
  add(q, { left: 860, top: 3000, width: 110, height: 32 });
  q.top.configure(true, 'ru');
  assert.equal(q.room(), '160');
  q.w.close();
});

test('desktop.js narrows the page column by the room, and the schedule only on the right', () => {
  for (let n = 144; n <= 240; n += 16) assert.ok(desktop.includes('[data-univer-top-room="' + n + '"] { --u-top-room:' + n + 'px; }'), String(n));
  assert.match(desktop, /\[data-univer-top-room\]:not\(\.univer-app-schedule-page\) #l4 td\.center \{ padding-left:max\(32px, calc\(\(100% - var\(--u-page\)\) \/ 2\), var\(--u-top-room\)\)!important; padding-right:max\(/);
  assert.match(desktop, /\.univer-app-schedule-page\[data-univer-top-room\] #l4 td\.center \{ padding-right:max\(32px, var\(--u-top-room\)\)!important; \}/);
  // Right only at every width (a fractional 899.5 px viewport too), on screen only: the rules above outweigh it from 900 px.
  assert.match(desktop, /@media screen \{\s*:root:root\.univer-mobile\[data-univer-top-room\] #l4 td\.center \{ padding-right:max\(16px, var\(--u-top-room\)\)!important; \}\s*\}/);
  // Every rule that pads the page for the room sits in screen media (the button is not printed): the style sheet is read
  // rule by rule with the @media blocks around each.
  const open = desktop.indexOf('textContent = `') + 'textContent = `'.length;
  const css = desktop.slice(open, desktop.indexOf('`', open)).replace(/\/\*[\s\S]*?\*\//g, '').replace(/\$\{[^}]*\}/g, '');
  const rules = [], stack = []; let from = 0;
  for (let i = 0; i < css.length; i++) {
    if (css[i] === '{') { stack.push(css.slice(from, i).trim()); from = i + 1; }
    else if (css[i] === '}') { const prelude = stack.pop(); if (prelude && !prelude.startsWith('@')) rules.push({ prelude, body: css.slice(from, i), media: stack.filter((p) => p.startsWith('@media')) }); from = i + 1; }
  }
  const padding = rules.filter((rule) => rule.prelude.includes('[data-univer-top-room]') && /padding/.test(rule.body));
  assert.equal(padding.length, 4, 'ordinary pages, attestation, the schedule and the right-only rule');
  for (const rule of padding) assert.ok(rule.media.length > 0 && rule.media.every((media) => /^@media screen\b/.test(media)), rule.media.join(' / ') + ' ' + rule.prelude);
});

test('an open dialog hides the button until it closes', () => {
  const p = page(site + '<div id="simplemodal-container">Окно</div>');
  const dialog = p.d.getElementById('simplemodal-container');
  dialog.getClientRects = () => [{ width: 300, height: 200 }];
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  assert.equal(p.button().classList.contains('on'), false);
  dialog.remove(); p.top.refresh();
  assert.equal(p.button().classList.contains('on'), true);
  p.w.close();
});

test('a click scrolls to the top smoothly, moves focus to the first control and hides the button', () => {
  const p = page('<header><a id="brand" href="/">Univer</a></header>' + site);
  const brand = p.d.getElementById('brand'); brand.getClientRects = () => [{ width: 80, height: 20 }];
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  p.button().click();
  assert.equal(JSON.stringify(p.scrolls.at(-1)), JSON.stringify({ top: 0, left: 0, behavior: 'smooth' }));
  assert.equal(p.d.activeElement, brand);
  assert.equal(p.button().classList.contains('on'), false);
  p.w.close();
});

test('with motion turned off in the app the jump is instant', () => {
  const p = page(site);
  p.w.__univerMotion = false;
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  p.button().click();
  assert.equal(p.scrolls.at(-1).behavior, 'auto');
  assert.equal(p.button().classList.contains('still'), true);
  p.w.close();
});

test('Original removes the button and the room and leaves the site nodes as they were', () => {
  const p = page(site, [{ left: 860, top: 3000, width: 110, height: 32 }]);
  const before = Array.from(p.d.body.childNodes);
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  assert.equal(p.room(), '160');
  p.top.configure(false, 'ru');
  assert.equal(p.host(), null);
  assert.equal(p.room(), null);
  assert.deepEqual(Array.from(p.d.body.childNodes), before);
  p.top.refresh(); assert.equal(p.host(), null, 'a late refresh adds nothing back');
  p.w.close();
});

test('a page that replaced its body gets the button back on the next check', () => {
  const p = page(site);
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  p.d.body.innerHTML = '<p>new content</p>';
  assert.equal(p.host(), null);
  p.top.refresh();
  assert.ok(p.host()); assert.equal(p.button().classList.contains('on'), true);
  p.w.close();
});
