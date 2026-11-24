const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const automation = read('univer-automation.js');
const layout = read('page-common.js') + '\n' + read('univer-layout.js');
const theme = read('theme.js');
const desktop = read('desktop.js');

function page(html, url = 'https://univer.kaznu.kz/student/myschedule/') {
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const w = dom.window;
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.eval(automation); w.eval(layout); w.eval(theme); w.eval(desktop);
  w.__univerTheme = 'dark';
  return { w, d: w.document, close: () => w.close() };
}
const apply = p => p.w.__univerDesktop.apply(true, false, 'ru');
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const site = '<html><head><style id="site">.xp td{color:#000000;background-color:#ffffff}</style></head>' +
  '<body><table class="xp"><tr><td id="cell" bgcolor="#FFFF99">Заливка</td></tr></table></body></html>';
// Every write to the two dark layers, as the page's observers see them.
function layerWrites(p) {
  const writes = [];
  const observer = new p.w.MutationObserver(records => records.forEach(record => {
    const node = record.type === 'characterData' ? record.target.parentNode : record.target;
    if (node && /^univer-app-dark-(site|app)$/.test(node.id || '')) writes.push(node.id);
  }));
  observer.observe(p.d.documentElement, { childList: true, subtree: true, characterData: true });
  return writes;
}

test('stylesheets added together are mapped in one pass, and an unchanged layer is not written again', async t => {
  const p = page(site);
  t.after(() => p.close());
  apply(p);
  await pause(0);
  const writes = layerWrites(p);
  const one = p.d.createElement('style'); one.textContent = '.one{color:#0000ff}';
  const two = p.d.createElement('style'); two.textContent = '.two{color:#800000}';
  p.d.head.insertBefore(one, p.d.getElementById('univer-app-dark-app'));
  p.d.head.insertBefore(two, p.d.getElementById('univer-app-dark-app'));
  await pause(0);
  const mapped = p.d.getElementById('univer-app-dark-site').textContent;
  assert.match(mapped, /\.one\{color:#[0-9a-f]{6}\}/);
  assert.match(mapped, /\.two\{color:#[0-9a-f]{6}\}/);
  assert.deepEqual(writes.filter(id => id === 'univer-app-dark-site'), ['univer-app-dark-site'], 'both sheets in one write');
  assert.equal(writes.filter(id => id === 'univer-app-dark-app').length, 0, 'the app layer did not change');
  // A sheet without colours maps to nothing new: neither layer is rewritten, and the known sheets are not read again.
  const rule = p.d.getElementById('site').sheet.cssRules[0], style = rule.style;
  let reads = 0;
  Object.defineProperty(rule, 'style', { configurable: true, get() { reads++; return style; } });
  writes.length = 0;
  const plain = p.d.createElement('style'); plain.textContent = '.plain{margin:0}';
  p.d.head.insertBefore(plain, p.d.getElementById('univer-app-dark-app'));
  await pause(0);
  assert.deepEqual(writes, []);
  assert.equal(reads, 0, 'a mapped sheet is taken from its cache');
  // A changed sheet is mapped anew.
  p.d.getElementById('site').textContent = '.xp td{color:#0000ff}';
  await pause(0);
  assert.doesNotMatch(p.d.getElementById('univer-app-dark-site').textContent, /background-color/);
});

test('a rewritten dark layer does not make the reading layout adapt the page again', async t => {
  const p = page(site);
  t.after(() => p.close());
  let passes = 0;
  p.w.__univerTasksV1 = { apply() { passes++; } };
  apply(p);
  await pause(300);
  const before = passes, app = p.d.getElementById('univer-app-dark-app').textContent;
  assert.ok(before > 0);
  // bgcolor is the theme's, not the layout's: only the dark layer changes for it.
  p.d.getElementById('cell').setAttribute('bgcolor', '#99CCFF');
  await pause(400);
  assert.notEqual(p.d.getElementById('univer-app-dark-app').textContent, app);
  assert.match(p.d.getElementById('univer-app-dark-app').textContent, /bgcolor="#99CCFF"/);
  assert.equal(passes, before);
});

const schedule = subject => '<table><tr id="l4"><td><div class="inner"><table class="schedule"><tr><th>&nbsp;</th><th>Пн</th><th>Вт</th></tr>' +
  '<tr><td>09:00 - 09:50</td><td id="slot"><p>' + subject + '(Лекция)</p><p>Синтетический П. П.</p></td><td></td></tr></table></div>' +
  '<table class="inner" id="records"><tr><th>Дисциплина</th><th>Оценка</th></tr><tr><td>История</td><td>90</td></tr></table></td></tr></table>';

test('an unchanged schedule is not serialised again, and an unchanged card table is not read again', async t => {
  const p = page('<html><head></head><body>' + schedule('История') + '</body></html>');
  t.after(() => p.close());
  const table = p.d.querySelector('table.schedule'), records = p.d.getElementById('records');
  const inner = Object.getOwnPropertyDescriptor(p.w.Element.prototype, 'innerHTML');
  let serialised = 0, read = 0;
  Object.defineProperty(table, 'innerHTML', { configurable: true, get() { serialised++; return inner.get.call(this); }, set(v) { inner.set.call(this, v); } });
  const matches = records.matches;
  records.matches = function (selector) { if (/univer-app-materials,\.schedule/.test(selector)) read++; return matches.call(this, selector); };
  apply(p);
  await pause(300);
  const view = p.d.querySelector('.univer-app-schedule');
  assert.ok(view);
  assert.ok(records.classList.contains('univer-app-cards'));
  const first = { serialised, read };
  p.w.dispatchEvent(new p.w.Event('resize'));
  await pause(300);
  assert.equal(serialised, first.serialised, 'the same week is not serialised again');
  assert.equal(read, first.read, 'the card table is not read again');
  assert.equal(p.d.querySelector('.univer-app-schedule'), view, 'the agenda is kept');
  // A change inside either table is read on the next pass.
  p.d.getElementById('slot').firstChild.textContent = 'Политология(Лекция)';
  const row = records.insertRow(); row.insertCell().textContent = 'Философия'; row.insertCell().textContent = '75';
  await pause(300);
  assert.ok(serialised > first.serialised);
  assert.notEqual(p.d.querySelector('.univer-app-schedule'), view);
  assert.match(p.d.querySelector('.univer-app-schedule').textContent, /Политология/);
  assert.ok(row.classList.contains('univer-app-card-row'));
  assert.equal(row.cells[1].dataset.univerField, 'Оценка');
});
