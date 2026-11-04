const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const automation = read('univer-automation.js');
const common = read('page-common.js');
const layout = common + '\n' + read('univer-layout.js');
const theme = read('theme.js');
const desktop = read('desktop.js');

function page(html, dark = true) {
  const dom = new JSDOM(html, { url: 'https://univer.kaznu.kz/student/myschedule/', runScripts: 'outside-only' });
  const w = dom.window;
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.eval(automation); w.eval(layout); w.eval(theme); w.eval(desktop);
  w.__univerTheme = dark ? 'dark' : 'light';
  return { w, d: w.document, close: () => w.close() };
}
const apply = (p, enabled = true) => p.w.__univerDesktop.apply(enabled, false, 'ru');
const site = '<html><head><style id="site">.xp td{color:#000000;background-color:#ffffff;border-top-color:#c0c0c0}.note{color:#800000}</style></head>' +
  '<body><table class="xp"><tr><td id="cell" style="color:#1f3864">Тема</td><td id="fill" bgcolor="#FFFF99">Заливка</td></tr></table><font id="font" color="red">Шрифт</font></body></html>';
function luminance(r, g, b) { const l = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }; return 0.2126 * l(r) + 0.7152 * l(g) + 0.0722 * l(b); }
const contrast = (a, b) => (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);

test('dark reading layout maps the app, site and inline colours and keeps every node', () => {
  const p = page(site);
  const cell = p.d.getElementById('cell'), fill = p.d.getElementById('fill'), siteStyle = p.d.getElementById('site');
  apply(p);
  const root = p.d.documentElement;
  assert.ok(root.classList.contains('univer-app-dark'));
  const app = p.d.getElementById('univer-app-dark-app'), mapped = p.d.getElementById('univer-app-dark-site');
  assert.ok(app && mapped);
  // The app's own tokens are remapped and the page colour is the shell's dark background.
  assert.match(app.textContent, /--u-brand:#[0-9a-f]{6}/);
  assert.doesNotMatch(app.textContent, /--u-brand:#1b5fa6/);
  assert.match(app.textContent, /:root\.univer-mobile\.univer-app-dark\{--u-bg:#15191e/);
  assert.match(app.textContent, /color-scheme:dark/);
  // The site's rules follow its own stylesheet, so the reading layout still overrides them.
  assert.equal(siteStyle.nextElementSibling, mapped);
  assert.ok(Array.from(p.d.head.children).indexOf(mapped) < Array.from(p.d.head.children).indexOf(p.d.getElementById('univer-desktop-style')));
  assert.match(mapped.textContent, /\.xp td\{color:#[0-9a-f]{6};background-color:#1d22(29|2a);border-top-color:#[0-9a-f]{6}\}/);
  // Inline colours: exact attribute matches without specificity, the element itself untouched.
  assert.match(app.textContent, /:where\(\[style="color:#1f3864"\]\)\{color:(#[0-9a-f]{6}|rgb\(\d+, \d+, \d+\))!important\}/);
  assert.match(app.textContent, /:where\(\[bgcolor="#FFFF99" i\]\)\{background-color:(#[0-9a-f]{6}|rgb\(\d+, \d+, \d+\))\}/);
  assert.match(app.textContent, /:where\(font\[color="red" i\]\)\{color:(#[0-9a-f]{6}|rgb\(\d+, \d+, \d+\))\}/);
  assert.equal(cell.getAttribute('style'), 'color:#1f3864');
  assert.equal(fill.getAttribute('bgcolor'), '#FFFF99');
  // Repeated polling ticks do not rebuild or duplicate the layers.
  apply(p); apply(p);
  assert.equal(p.d.querySelectorAll('#univer-app-dark-app,#univer-app-dark-site').length, 2);
  // Original removes the theme with the rest of the presentation.
  apply(p, false);
  assert.equal(root.classList.contains('univer-app-dark'), false);
  assert.equal(p.d.querySelector('[id^="univer-app-dark-"]'), null);
  assert.equal(cell.getAttribute('style'), 'color:#1f3864');
  assert.equal(p.d.getElementById('cell'), cell);
  p.close();
});

test('light theme and Original leave the page alone; refresh switches a shown page', () => {
  const p = page(site, false);
  apply(p);
  assert.equal(p.d.documentElement.classList.contains('univer-app-dark'), false);
  assert.equal(p.d.querySelector('[id^="univer-app-dark-"]'), null);
  p.w.__univerTheme = 'dark'; p.w.__univerThemeV1.refresh();
  assert.ok(p.d.documentElement.classList.contains('univer-app-dark'));
  assert.ok(p.d.getElementById('univer-app-dark-app'));
  p.w.__univerTheme = 'light'; p.w.__univerThemeV1.refresh();
  assert.equal(p.d.querySelector('[id^="univer-app-dark-"]'), null);
  apply(p, false); p.w.__univerTheme = 'dark'; p.w.__univerThemeV1.refresh();
  assert.equal(p.d.documentElement.classList.contains('univer-app-dark'), false);
  p.close();
});

test('the early page colour applies only to a dark reading layout and gives way to the full theme', () => {
  const p = page(site);
  p.w.__univerThemeV1.early(false);
  assert.equal(p.d.getElementById('univer-app-dark-early'), null);
  p.w.__univerThemeV1.early(true);
  assert.match(p.d.getElementById('univer-app-dark-early').textContent, /background:#15191e!important/);
  apply(p);
  assert.equal(p.d.getElementById('univer-app-dark-early'), null);
  p.close();
});

test('stylesheets added later are mapped before they paint', async () => {
  const p = page(site);
  apply(p);
  const late = p.d.createElement('style'); late.id = 'univer-pages-style'; late.textContent = '.late{color:#0000ff}';
  p.d.head.insertBefore(late, p.d.getElementById('univer-app-dark-app'));
  await new Promise(resolve => setTimeout(resolve, 0));
  const app = p.d.getElementById('univer-app-dark-app');
  assert.match(app.textContent, /\.late\{color:#[0-9a-f]{6}\}/);
  assert.equal(p.d.head.lastElementChild, app);
  p.close();
});

test('the colour mapping keeps contrast against the page', () => {
  const p = page(site);
  const card = luminance(29, 34, 41);
  for (const [r, g, b] of [[22, 40, 59], [27, 95, 166], [180, 35, 24], [0, 0, 255], [255, 255, 153], [241, 244, 247], [128, 128, 128]]) {
    const [dr, dg, db] = p.w.__univerThemeV1.dark(r, g, b);
    const before = contrast(luminance(r, g, b), 1), after = contrast(luminance(dr, dg, db), card);
    assert.ok(Math.abs(after - before) / before < 0.08, `${r},${g},${b}: ${before.toFixed(2)} → ${after.toFixed(2)}`);
  }
  const [wr, wg, wb] = p.w.__univerThemeV1.dark(255, 255, 255);
  assert.ok(Math.abs(wr - 29) <= 1 && Math.abs(wg - 34) <= 1 && Math.abs(wb - 41) <= 1);
  // Shadows are not lit; background pictures other than gradients and inline SVG keep their address.
  assert.equal(p.w.__univerThemeV1.map('0 1px 2px rgba(0,0,0,.2)', 'box-shadow'), '0 1px 2px rgba(0,0,0,.2)');
  assert.equal(p.w.__univerThemeV1.map('url(/img/a.png#add)', 'background-image'), 'url(/img/a.png#add)');
  assert.doesNotMatch(p.w.__univerThemeV1.map("url(\"data:image/svg+xml,%3Csvg stroke='%23ffffff'%3E\")", 'background-image'), /%23ffffff/);
  p.close();
});

test('SVG icons in custom properties and backgrounds stay valid addresses and turn light', () => {
  const p = page(site);
  const map = p.w.__univerThemeV1.map;
  // The app's pin, close and search icons: encodeURIComponent keeps the colour names, and a bare # would cut the address.
  const icon = 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' fill='none' stroke='black'><path d='M6 6l12 12'/></svg>") + '")';
  for (const name of ['--univer-pin-icon', 'background-image', '-webkit-mask-image']) {
    const mapped = map(icon, name), address = mapped.slice(5, -2);
    assert.doesNotMatch(address, /#/, name);
    const [, digits] = address.match(/stroke%3D'%23([0-9a-f]{6})'/) || [];
    assert.ok(digits, name + ': ' + mapped);
    assert.ok(luminance(...digits.match(/../g).map(h => parseInt(h, 16))) > 0.6, name + ' stroke is light');
  }
  // Other addresses are kept even where their path looks like a colour; colours around them still map.
  assert.equal(map('url(/img/black-arrow.png)', '--univer-art'), 'url(/img/black-arrow.png)');
  assert.match(map('white url(/img/white.png)', '--univer-art'), /^#1d22(29|2a) url\(\/img\/white\.png\)$/);
  p.close();
});

test('the dark reading layout turns the site logo light without touching its picture', () => {
  const p = page('<html><head><style id="site">#logo{width:200px;height:36px;background-image:url(/content/images/line-second-logo.jpg)}</style></head><body><div id="logo"></div></body></html>');
  apply(p);
  const app = p.d.getElementById('univer-app-dark-app').textContent, mapped = p.d.getElementById('univer-app-dark-site').textContent;
  assert.match(app, /:root\.univer-app-dark #logo\{filter:invert\(1\) brightness\(\.9\);mix-blend-mode:screen\}/);
  assert.match(mapped, /#logo\{background-image:url\("?\/content\/images\/line-second-logo\.jpg"?\)\}/);
  apply(p, false);
  assert.equal(p.d.querySelector('[id^="univer-app-dark-"]'), null);
  p.close();
});

test('colour names inside custom property names are not colours', () => {
  const p = page(site);
  const map = p.w.__univerThemeV1.map;
  assert.equal(map('var(--white)', 'color'), 'var(--white)');
  assert.equal(map('var(--black-text, #000000)', 'color').startsWith('var(--black-text, #'), true);
  assert.doesNotMatch(map('var(--black-text, #000000)', 'color'), /#000000/);
  assert.match(map('white', 'background-color'), /^#1d22(29|2a)$/);
  p.close();
});

test('a stylesheet link that loads after the page is mapped once it has loaded', async () => {
  const p = page(site);
  apply(p);
  const link = p.d.createElement('link');
  link.rel = 'stylesheet'; link.href = '/late.css';
  p.d.head.insertBefore(link, p.d.getElementById('univer-app-dark-app'));
  await new Promise(r => setTimeout(r, 0));
  assert.doesNotMatch(p.d.getElementById('univer-app-dark-site').textContent, /\.linked/);
  // jsdom fetches nothing here, so the loaded sheet is handed over as the browser would do it.
  const sheet = new JSDOM('<style>.linked{color:#0000ff}</style>').window.document.querySelector('style').sheet;
  Object.defineProperty(link, 'sheet', { value: sheet, configurable: true });
  link.dispatchEvent(new p.w.Event('load'));
  await new Promise(r => setTimeout(r, 0));
  const all = p.d.getElementById('univer-app-dark-site').textContent + p.d.getElementById('univer-app-dark-app').textContent;
  assert.match(all, /\.linked\{color:#[0-9a-f]{6}\}/);
  assert.doesNotMatch(all, /\.linked\{color:#0000ff\}/);
  p.close();
});

test('grouping rules keep their own kind', () => {
  const p = page('<html><head><style id="site">@media screen{.m{color:#000000}}@supports (display:grid){.s{color:#000000}}</style><style id="groups">.c{color:#000000}</style></head><body></body></html>');
  // jsdom cannot parse @layer and @container, so those rules are handed over in the shape Chromium gives them.
  const inner = p.d.getElementById('groups').sheet.cssRules[0];
  class CSSLayerBlockRule { constructor() { this.name = 'base'; this.cssRules = [inner]; } }
  class CSSContainerRule { constructor() { this.conditionText = '(min-width: 300px)'; this.cssText = '@container card (min-width: 300px) { .c { color: black; } }'; this.cssRules = [inner]; } }
  Object.defineProperty(p.d.getElementById('groups'), 'sheet', { value: { cssRules: [new CSSLayerBlockRule(), new CSSContainerRule()] } });
  apply(p);
  const mapped = p.d.getElementById('univer-app-dark-site').textContent;
  assert.match(mapped, /@media screen\{\.m\{color:#[0-9a-f]{6}\}\}/);
  assert.match(mapped, /@supports \(display: ?grid\)\{\.s\{color:#[0-9a-f]{6}\}\}/);
  assert.match(mapped, /@layer base\{\.c\{color:#[0-9a-f]{6}\}\}/);
  assert.match(mapped, /@container card \(min-width: 300px\)\{\.c\{color:#[0-9a-f]{6}\}\}/);
  assert.doesNotMatch(mapped, /@supports \(min-width/);
  p.close();
});