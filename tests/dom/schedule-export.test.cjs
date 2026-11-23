const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const {JSDOM} = require('jsdom');
const jsdomUtils = require('jsdom/lib/jsdom/living/generated/utils');

const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const scripts = ['univer-automation.js', 'page-common.js', 'news.js', 'study-links.js', 'attendance.js', 'overview.js', 'shortcuts.js'].map(read).join('\n');
const layout = read('page-common.js') + '\n' + read('univer-layout.js');
const desktop = read('desktop.js');

const BASE = 'https://univer.kaznu.kz';
const weeks = [
  {n: 3, a: '02.11.2026', b: '08.11.2026', from: new Date(2026, 10, 2)},
  {n: 4, a: '09.11.2026', b: '15.11.2026', from: new Date(2026, 10, 9)},
  {n: 5, a: '16.11.2026', b: '22.11.2026', from: new Date(2026, 10, 16)}];
const subjects = ['История', 'Политология', 'Культурология'];
const value = w => '/student/myschedule/2026/1/' + w.a + '/' + w.b + '/';
const options = (selected, texts) => weeks.map((w, i) => '<option value="' + value(w) + '"' + (i === selected ? ' selected' : '') + '>' + (texts ? texts[i] : w.n + ' неделя ' + w.a + ' - ' + w.b) + '</option>').join('');
const table = subject => '<table class="schedule"><tr><th>&nbsp;</th><th>Пн</th><th>Вт</th></tr>' +
  '<tr><td>09:00 - 09:50</td><td><p>' + subject + '(Лекция)</p><p>Синтетический П. П.</p><p>ФФ / Ауд.: 305</p></td><td></td></tr>' +
  '<tr><td>10:00 - 10:50</td><td></td><td><p>' + subject + ' два(Семинар)</p></td></tr></table>';
const html = (selected, subject, texts) => '<table><tr id="l4"><td><select id="weekSelect">' + options(selected, texts) + '</select><div class="inner">' + table(subject) + '</div></td></tr></table>';
const defaultFetch = texts => async url => {
  const asked = weeks.findIndex(x => String(url).includes(x.a));
  return {status: 200, url: String(url), text: async () => html(asked, subjects[asked] || 'Культурология', texts)};
};

function page(opts = {}) {
  const selected = opts.selected === undefined ? 1 : opts.selected;
  const dom = new JSDOM(html(selected, opts.subject || subjects[selected] || 'Политология', opts.texts), {url: BASE + (opts.path || '/student/myschedule/'), runScripts: 'outside-only'});
  const w = dom.window, sent = [], requests = [];
  Object.defineProperty(w.document, 'readyState', {value: 'complete', configurable: true});
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{width: 100, height: 30}]; };
  w.jQuery = () => ({bind() {}});
  if (opts.flag !== undefined) w.__univerScheduleExport = opts.flag;
  w.__univerNative = m => sent.push(m);
  const fallback = defaultFetch(opts.texts);
  w.fetch = async (url, init) => {
    if (/myschedule/.test(String(url))) requests.push({url, init});
    if (opts.fetch) return opts.fetch(url, init, fallback);
    return fallback(url, init);
  };
  w.eval(scripts); w.eval(layout); w.eval(desktop);
  w.__univerDesktop.apply(true, false, opts.lang || 'ru');
  return {w, d: w.document, sent, requests, close: () => w.close()};
}
function trusted(w, node) { const e = new w.MouseEvent('click', {bubbles: true, cancelable: true}); const impl = jsdomUtils.implForWrapper(e); impl.isTrusted = true; jsdomUtils.implForWrapper(node)._dispatch(impl); }
const button = (p, kind) => p.d.querySelector('.univer-app-schedule-export button[data-univer-export=' + kind + ']');
const settle = async () => { for (let i = 0; i < 20; i++) await new Promise(r => setImmediate(r)); };
const ms = d => d.getTime();

test('the schedule view has the two export buttons in the page language, and none without the flag', () => {
  const p = page();
  try {
    const group = p.d.querySelector('.univer-app-schedule-export');
    assert.equal(group.getAttribute('role'), 'group');
    assert.equal(group.getAttribute('aria-label'), 'Экспорт расписания');
    assert.deepEqual(Array.from(group.querySelectorAll('button')).map(b => [b.type, b.dataset.univerExport, b.textContent]), [['button', 'ics', 'В календарь'], ['button', 'png', 'Картинкой']]);
    // The accessible name is the visible text; the longer note is the tooltip.
    assert.equal(button(p, 'ics').getAttribute('aria-label'), null);
    assert.match(button(p, 'ics').title, /\(\.ics\)$/);
    assert.match(button(p, 'png').title, /\(PNG\)$/);
  } finally { p.close(); }
  const en = page({lang: 'en'});
  try { assert.deepEqual(Array.from(en.d.querySelectorAll('.univer-app-schedule-export button')).map(b => b.textContent), ['To calendar', 'As picture']); } finally { en.close(); }
  const kz = page({lang: 'kz'});
  try { assert.deepEqual(Array.from(kz.d.querySelectorAll('.univer-app-schedule-export button')).map(b => b.textContent), ['Күнтізбеге', 'Сурет ретінде']); } finally { kz.close(); }
  const off = page({flag: false});
  try { assert.equal(off.d.querySelector('.univer-app-schedule-export'), null); } finally { off.close(); }
});

test('an untrusted click does nothing', async () => {
  const p = page();
  try {
    button(p, 'ics').click(); await settle();
    assert.deepEqual(p.sent, []); assert.equal(p.requests.length, 0);
    assert.equal(button(p, 'ics').hasAttribute('aria-busy'), false);
    assert.equal(p.w.__univerOverviewV1.exportData(), '');
  } finally { p.close(); }
});

test('a trusted press reads the shown week and the next one and tells the app', async () => {
  const p = page();
  try {
    trusted(p.w, button(p, 'ics'));
    assert.equal(button(p, 'ics').getAttribute('aria-busy'), 'true');
    assert.equal(button(p, 'png').getAttribute('aria-disabled'), 'true');
    assert.equal(button(p, 'png').disabled, false, 'never disabled: the pressed button keeps the keyboard focus');
    await settle();
    assert.deepEqual(p.sent, ['univer-schedule:ics']);
    assert.deepEqual(p.requests.map(r => r.url), [BASE + value(weeks[1]), BASE + value(weeks[2])]);
    for (const r of p.requests) {
      assert.equal(r.init.redirect, 'error');
      assert.equal(r.init.credentials, 'same-origin');
    }
    const data = JSON.parse(p.w.__univerOverviewV1.exportData());
    const lessons = name => [[{start: 540, end: 590, name, type: 'Лекция', room: '305', teacher: 'Синтетический П. П.'}], [{start: 600, end: 650, name: name + ' два', type: 'Семинар', room: '', teacher: ''}]];
    assert.deepEqual(data, {
      term: '2026/1', until: ms(new Date(2026, 10, 23)), missing: [],
      weeks: [
        {number: 4, labelled: true, start: ms(new Date(2026, 10, 9)), end: ms(new Date(2026, 10, 16)), days: lessons('Политология')},
        {number: 5, labelled: true, start: ms(new Date(2026, 10, 16)), end: ms(new Date(2026, 10, 23)), days: lessons('Культурология')}]});
    assert.equal(p.w.__univerOverviewV1.exportData(), '', 'given once');
    assert.equal(button(p, 'ics').getAttribute('aria-busy'), 'true', 'busy until the app answers');
    p.w.__univerOverviewV1.exported(true);
    assert.equal(button(p, 'ics').hasAttribute('aria-busy'), false);
    assert.equal(button(p, 'ics').hasAttribute('aria-disabled'), false);
    trusted(p.w, button(p, 'png')); await settle();
    assert.deepEqual(p.sent, ['univer-schedule:ics', 'univer-schedule:png']);
  } finally { p.close(); }
});

test('a second press while busy is ignored, and nothing is sent without the app channel', async () => {
  const p = page();
  try {
    trusted(p.w, button(p, 'ics')); trusted(p.w, button(p, 'png')); await settle();
    assert.deepEqual(p.sent, ['univer-schedule:ics']);
  } finally { p.close(); }
  const lone = page();
  try { lone.w.__univerNative = undefined; trusted(lone.w, button(lone, 'ics')); await settle(); assert.equal(lone.requests.length, 0); assert.equal(button(lone, 'ics').hasAttribute('aria-busy'), false); } finally { lone.close(); }
});

test('the shown week is the select\'s selectedIndex, not the selected attribute, and an unlabelled week is numbered by place', async () => {
  const p = page({selected: 0, texts: ['02.11.2026 - 08.11.2026', '09.11.2026 - 15.11.2026', '16.11.2026 - 22.11.2026']});
  try {
    p.d.getElementById('weekSelect').selectedIndex = 2;
    trusted(p.w, button(p, 'ics')); await settle();
    const data = JSON.parse(p.w.__univerOverviewV1.exportData());
    assert.deepEqual(data.weeks.map(w => [w.number, w.labelled, w.start]), [[3, false, ms(new Date(2026, 10, 16))], [2, false, ms(new Date(2026, 10, 9))]], 'the last week takes the one before it');
    assert.deepEqual(p.requests.map(r => r.url), [BASE + value(weeks[2]), BASE + value(weeks[1])]);
  } finally { p.close(); }
});

test('switching weeks before AJAX replaces the table exports the selected week from GET, never the stale DOM table', async () => {
  const p = page({selected: 0, subject: 'Устаревшая неделя'});
  try {
    // DOM still shows week 3 ("Устаревшая неделя"), while #weekSelect already moved to week 4.
    p.d.getElementById('weekSelect').selectedIndex = 1;
    trusted(p.w, button(p, 'ics')); await settle();
    assert.deepEqual(p.sent, ['univer-schedule:ics']);
    assert.deepEqual(p.requests.map(r => r.url), [BASE + value(weeks[1]), BASE + value(weeks[2])]);
    const data = JSON.parse(p.w.__univerOverviewV1.exportData());
    assert.equal(data.weeks[0].number, 4);
    assert.equal(data.weeks[0].start, ms(new Date(2026, 10, 9)));
    assert.deepEqual(data.weeks[0].days.map(day => day.map(l => l.name)), [['Политология'], ['Политология два']]);
    assert.equal(data.weeks[1].number, 5);
    assert.deepEqual(data.weeks[1].days.map(day => day.map(l => l.name)), [['Культурология'], ['Культурология два']]);
    assert.deepEqual(data.missing, []);
    assert.ok(p.d.querySelector('table.schedule').textContent.includes('Устаревшая неделя'), 'DOM table was still week A');
  } finally { p.close(); }
});

test('a neighbour that cannot be read is named in missing, not guessed', async () => {
  const answers = {
    redirected: () => Promise.reject(new TypeError('redirect')),
    'other path': url => ({status: 200, url: BASE + '/student/myschedule/', text: async () => html(2, 'X')}),
    'other host': url => ({status: 200, url: 'https://example.org' + value(weeks[2]), text: async () => html(2, 'X')}),
    'other dates': url => ({status: 200, url: String(url), text: async () => html(1, 'X')}),
    login: url => ({status: 200, url: String(url), text: async () => '<form id="login_frm"></form>'}),
    status: url => ({status: 500, url: String(url), text: async () => html(2, 'X')}),
    'no table': url => ({status: 200, url: String(url), text: async () => '<select id="weekSelect">' + options(2) + '</select>'})};
  for (const [name, bad] of Object.entries(answers)) {
    const p = page({fetch: (url, init, fallback) => String(url).includes(weeks[2].a) ? bad(url, init) : fallback(url, init)});
    try {
      trusted(p.w, button(p, 'ics')); await settle();
      assert.deepEqual(p.sent, ['univer-schedule:ics'], name);
      const data = JSON.parse(p.w.__univerOverviewV1.exportData());
      assert.equal(data.weeks.length, 1, name); assert.deepEqual(data.missing, [5], name);
    } finally { p.close(); }
  }
});

test('when the shown week cannot be read, nothing is sent to the app and the buttons are freed', async () => {
  const answers = {
    redirected: () => Promise.reject(new TypeError('redirect')),
    'other path': url => ({status: 200, url: BASE + '/student/myschedule/', text: async () => html(1, 'X')}),
    'other host': url => ({status: 200, url: 'https://example.org' + value(weeks[1]), text: async () => html(1, 'X')}),
    'other dates': url => ({status: 200, url: String(url), text: async () => html(0, 'X')}),
    login: url => ({status: 200, url: String(url), text: async () => '<form id="login_frm"></form>'}),
    status: url => ({status: 500, url: String(url), text: async () => html(1, 'X')}),
    'no table': url => ({status: 200, url: String(url), text: async () => '<select id="weekSelect">' + options(1) + '</select>'})};
  for (const [name, bad] of Object.entries(answers)) {
    let failShown = true;
    const p = page({fetch: (url, init, fallback) => failShown && String(url).includes(weeks[1].a) ? bad(url, init) : fallback(url, init)});
    try {
      trusted(p.w, button(p, 'ics')); await settle();
      assert.deepEqual(p.sent, [], name);
      assert.equal(p.w.__univerOverviewV1.exportData(), '', name);
      assert.deepEqual(p.requests.map(r => r.url), [BASE + value(weeks[1])], name + ': neighbour is not fetched');
      for (const kind of ['ics', 'png']) {
        assert.equal(button(p, kind).hasAttribute('aria-busy'), false, name + ' ' + kind);
        assert.equal(button(p, kind).hasAttribute('aria-disabled'), false, name + ' ' + kind);
      }
      // Buttons work again once the shown week can be read.
      failShown = false;
      trusted(p.w, button(p, 'png')); await settle();
      assert.deepEqual(p.sent, ['univer-schedule:png'], name + ': recovers after failure');
    } finally { p.close(); }
  }
});

test('an option that is not a dated week address is never fetched', async () => {
  const p = page();
  try {
    p.d.getElementById('weekSelect').options[2].value = '/student/myschedule/2026/1/12/';
    trusted(p.w, button(p, 'ics')); await settle();
    assert.deepEqual(p.requests.map(r => r.url), [BASE + value(weeks[1])]);
    assert.deepEqual(JSON.parse(p.w.__univerOverviewV1.exportData()).missing, [5]);
  } finally { p.close(); }
  const badShown = page();
  try {
    badShown.d.getElementById('weekSelect').options[1].value = '/student/myschedule/2026/1/12/';
    trusted(badShown.w, button(badShown, 'ics')); await settle();
    assert.equal(badShown.requests.length, 0);
    assert.deepEqual(badShown.sent, []);
    assert.equal(badShown.w.__univerOverviewV1.exportData(), '');
    assert.equal(button(badShown, 'ics').hasAttribute('aria-busy'), false);
    assert.equal(button(badShown, 'png').hasAttribute('aria-disabled'), false);
  } finally { badShown.close(); }
});

test('the shown last week takes the previous one as its neighbour', async () => {
  const p = page({selected: 2});
  try {
    trusted(p.w, button(p, 'png')); await settle();
    assert.deepEqual(p.requests.map(r => r.url), [BASE + value(weeks[2]), BASE + value(weeks[1])]);
    const data = JSON.parse(p.w.__univerOverviewV1.exportData());
    assert.deepEqual(data.weeks.map(w => w.number), [5, 4]); assert.equal(data.until, ms(new Date(2026, 10, 23)));
    assert.deepEqual(p.sent, ['univer-schedule:png']);
  } finally { p.close(); }
});

test('a week after a gap is not the neighbour: the week right before is, also on a semester tab address', async () => {
  const p = page({path: '/student/myschedule/2026/1/'});
  try {
    const later = p.d.getElementById('weekSelect').options[2];
    later.value = '/student/myschedule/2026/1/30.11.2026/06.12.2026/'; later.textContent = '7 неделя 30.11.2026 - 06.12.2026';
    trusted(p.w, button(p, 'ics')); await settle();
    assert.deepEqual(p.requests.map(r => r.url), [BASE + value(weeks[1]), BASE + value(weeks[0])]);
    const data = JSON.parse(p.w.__univerOverviewV1.exportData());
    assert.deepEqual(data.weeks.map(w => w.number), [4, 3]); assert.deepEqual(data.missing, []);
    assert.equal(data.until, ms(new Date(2026, 11, 7)));
  } finally { p.close(); }
});

test('an answer before the app took the reading drops it, and cancelling mid-fetch stops before the neighbour', async () => {
  const p = page();
  try {
    trusted(p.w, button(p, 'ics')); await settle();
    assert.deepEqual(p.sent, ['univer-schedule:ics']);
    p.w.__univerOverviewV1.exported(false);
    assert.equal(p.w.__univerOverviewV1.exportData(), '');
    assert.equal(button(p, 'ics').hasAttribute('aria-busy'), false);
  } finally { p.close(); }
  let release;
  const held = new Promise(r => { release = r; });
  const c = page({fetch: async (url, init, fallback) => { await held; return fallback(url, init); }});
  try {
    trusted(c.w, button(c, 'ics'));
    c.w.__univerOverviewV1.exported(false);
    release(); await settle();
    assert.deepEqual(c.sent, []);
    assert.deepEqual(c.requests.map(r => r.url), [BASE + value(weeks[1])]);
    assert.equal(c.w.__univerOverviewV1.exportData(), '');
  } finally { c.close(); }
});
