const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync('src/Univer.Desktop/Assets/back-to-top.js', 'utf8');
// A 1000×800 window; `controls` are rectangles (viewport coordinates) that elementsFromPoint reports as links.
function page(body, controls = []) {
  const dom = new JSDOM('<!doctype html><html><body>' + body + '</body></html>', { url: 'https://univer.kaznu.kz/news/', runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window, d = w.document;
  Object.defineProperty(w, 'innerWidth', { configurable: true, value: 1000 });
  Object.defineProperty(w, 'innerHeight', { configurable: true, value: 800 });
  let y = 0; Object.defineProperty(w, 'scrollY', { configurable: true, get: () => y });
  const scrolls = []; w.scrollTo = (options) => { scrolls.push(options); y = options.top; };
  const nodes = controls.map((rect, i) => {
    const node = d.createElement('a'); node.href = '/news/' + i; node.textContent = 'link ' + i; d.body.appendChild(node);
    node.getBoundingClientRect = () => ({ ...rect, right: rect.left + rect.width, bottom: rect.top + rect.height });
    return { node, rect };
  });
  d.elementsFromPoint = (x, py) => {
    const hit = nodes.filter(({ rect }) => x >= rect.left && x <= rect.left + rect.width && py >= rect.top && py <= rect.top + rect.height).map(({ node }) => node);
    return hit.concat([d.body, d.documentElement]);
  };
  w.eval(script);
  const top = w.__univerTopV1;
  const host = () => d.getElementById('univer-app-top');
  const button = () => host()?.shadowRoot.querySelector('button');
  return { w, d, top, host, button, scrolls, scroll(to) { y = to; }, nodes };
}
const site = '<div id="site"><p>Новости</p></div>';

test('the button waits until the page is well down, then shows at the bottom right with its label', () => {
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
  assert.equal(b.style.bottom, '24px'); assert.equal(b.style.right, '24px');
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

test('a link under the bottom right corner moves the button above it, not over it', () => {
  // A link row across the lower right: y 700…760.
  const p = page(site, [{ left: 600, top: 700, width: 380, height: 60 }]);
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  const b = p.button(), lift = parseInt(b.style.bottom, 10);
  assert.equal(b.classList.contains('on'), true);
  assert.equal(b.style.right, '24px');
  // The button's bottom edge (800 - lift) stays above the link's top edge with room to spare.
  assert.ok(800 - lift <= 700 - 6, 'button bottom ' + (800 - lift) + ' must clear the link top 700');
  p.w.close();
});

test('with the whole right side taken it goes to the left; with both sides taken it hides', () => {
  const right = { left: 700, top: 380, width: 300, height: 420 };
  const p = page(site, [right]);
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  assert.equal(p.button().classList.contains('on'), true);
  assert.equal(p.button().style.left, '24px'); assert.equal(p.button().style.bottom, '24px');
  p.w.close();
  const q = page(site, [right, { left: 0, top: 380, width: 300, height: 420 }]);
  q.top.configure(true, 'ru'); q.scroll(2000); q.top.refresh();
  assert.equal(q.button().classList.contains('on'), false, 'no free spot, no button');
  q.w.close();
});

test('a clickable panel bigger than a quarter of the window does not push the button away', () => {
  const p = page(site, [{ left: 0, top: 100, width: 1000, height: 700 }]);
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  assert.equal(p.button().classList.contains('on'), true);
  assert.equal(p.button().style.bottom, '24px');
  p.w.close();
});

test('the scroll bar of a box that scrolls sideways (an attendance matrix) moves the button above it', () => {
  const run = (overflows) => {
    const p = page(site), box = p.d.createElement('div'), rect = { left: 0, top: 560, width: 1000, height: 200 };
    box.style.overflowX = 'auto'; p.d.body.appendChild(box);
    box.getBoundingClientRect = () => ({ ...rect, right: rect.left + rect.width, bottom: rect.top + rect.height });
    // A 17 px bar along the bottom: the content box ends at 560 + 183 = 743.
    const size = { clientTop: 0, clientLeft: 0, clientHeight: 183, clientWidth: 1000, scrollHeight: 183, scrollWidth: overflows ? 2400 : 1000 };
    for (const key in size) Object.defineProperty(box, key, { configurable: true, value: size[key] });
    const inside = (x, y) => x >= rect.left && x < rect.left + rect.width && y >= rect.top && y < rect.top + rect.height;
    p.d.elementsFromPoint = (x, y) => (inside(x, y) ? [box] : []).concat([p.d.body, p.d.documentElement]);
    p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
    const b = p.button(), state = { on: b.classList.contains('on'), right: b.style.right, lift: parseInt(b.style.bottom, 10) };
    p.w.close();
    return state;
  };
  const bar = run(true);
  assert.equal(bar.on, true); assert.equal(bar.right, '24px');
  assert.ok(800 - bar.lift <= 743 - 6, 'button bottom ' + (800 - bar.lift) + ' must clear the scroll bar top 743');
  // The same box with nothing to scroll has no bar: the button keeps its corner.
  const plain = run(false);
  assert.equal(plain.on, true); assert.equal(plain.lift, 24);
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

test('Original removes the button and leaves the site nodes as they were', () => {
  const p = page(site);
  const before = Array.from(p.d.body.childNodes);
  p.top.configure(true, 'ru'); p.scroll(2000); p.top.refresh();
  p.top.configure(false, 'ru');
  assert.equal(p.host(), null);
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

test('the phone passes a larger size and edge gap for the thumb', () => {
  const p = page(site);
  p.top.configure(true, 'ru', { edge: 16, height: 48 }); p.scroll(2000); p.top.refresh();
  const b = p.button();
  assert.equal(b.style.height, '48px'); assert.equal(b.style.borderRadius, '24px');
  assert.equal(b.style.bottom, '16px'); assert.equal(b.style.right, '16px');
  p.w.close();
});
