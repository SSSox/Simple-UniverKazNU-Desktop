const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {JSDOM} = require('jsdom');

const read = name => fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'Univer.Desktop', 'Assets', name), 'utf8');

// Бакалавр's "today" panel at a chosen minute of Monday, 9 November 2026, with two classes: 9:00–10:00 and 12:00–13:00.
// With a language the page is the site root, so apply() only switches the language and builds no panels of its own.
function today(hour, minute, language) {
  const dom = new JSDOM('<body><div id="today"></div></body>', {url: language ? 'https://univer.kaznu.kz/' : 'https://univer.kaznu.kz/student/bachelor/', runScripts: 'outside-only', pretendToBeVisual: true});
  const w = dom.window, RealDate = w.Date;
  w.Date = class extends RealDate { constructor(...args) { super(...(args.length ? args : [2026, 10, 9, hour, minute])); } static now() { return new RealDate(2026, 10, 9, hour, minute).getTime(); } };
  w.fetch = async () => ({ok: true, status: 200, url: 'https://univer.kaznu.kz/student/umkd/', text: async () => '<html><body></body></html>'});
  w.eval(read('page-common.js')); w.eval(read('overview.js'));
  const lesson = (start, end, name) => ({start: start * 60, end: end * 60, parts: {name, type: 'Лекция', kind: 'lecture', room: '305', faculty: '', teacher: 'Синтетический П. П.', rest: [], period: ''}});
  const body = w.document.getElementById('today');
  if (language) w.__univerOverviewV1.apply(language);
  w.__univerOverviewV1.renderToday(body, [lesson(9, 10, 'Политология'), lesson(12, 13, 'Культурология')], null);
  return {w, body, close: () => w.close()};
}
const lead = b => { const n = b.querySelector('.univer-app-overview-upnext'); return n && [n.dataset.state, n.querySelector('b').textContent]; };
const states = b => Array.from(b.querySelectorAll('.univer-app-overview-state')).map(n => n.textContent);
const counted = b => Array.from(b.querySelectorAll('.univer-app-overview-lessons *')).filter(n => !n.children.length && /через|ещё/.test(n.textContent)).map(n => n.textContent);

test('before the next class one line counts down to it and its row does not say it again', () => {
  const p = today(10, 30);
  try {
    assert.deepEqual(lead(p.body), ['next', 'Следующая пара через 1 ч 30 мин']);
    assert.deepEqual(counted(p.body), []);
    assert.deepEqual(states(p.body), ['Прошла']);
    assert.equal(p.body.querySelectorAll('.univer-app-overview-lesson.univer-app-overview-next').length, 1);
  } finally { p.close(); }
});

test('during a class one line says how long is left and the row is only highlighted', () => {
  const p = today(9, 25);
  try {
    assert.deepEqual(lead(p.body), ['now', 'Идёт пара · ещё 35 мин']);
    assert.deepEqual(counted(p.body), []);
    assert.deepEqual(states(p.body), []);
    assert.equal(p.body.querySelectorAll('.univer-app-overview-lesson.univer-app-overview-now').length, 1);
  } finally { p.close(); }
});

test('the date over the classes is written in the chosen language', () => {
  for (const [language, caption] of [['kz', 'Дүйсенбі, 9 қараша'], ['en', 'Monday, 9 November'], ['ru', 'Понедельник, 9 ноября']]) {
    const p = today(10, 30, language);
    try { assert.equal(p.body.querySelector('.univer-app-overview-caption').textContent, caption); } finally { p.close(); }
  }
});

// The Windows app's own copy of the week (MainForm.Schedule.cs) is read from the schedule page while it shows this week.
function schedulePage(selected) {
  const html = '<body><select id="weekSelect">' +
    '<option value="/student/myschedule/2026/1/10/"' + (selected === 10 ? ' selected' : '') + '>09.11.2026 - 15.11.2026</option>' +
    '<option value="/student/myschedule/2026/1/11/"' + (selected === 11 ? ' selected' : '') + '>16.11.2026 - 22.11.2026</option></select>' +
    '<table class="schedule"><tr><th>Время</th><th>Пн</th><th>Вт</th></tr>' +
    '<tr><td>09:00 - 09:50</td><td><p>Политология(Лекция)</p><p>Синтетический П. П.</p><p>ФФ / Ауд.: 305</p></td><td></td></tr>' +
    '<tr><td>10:00 - 10:50</td><td></td><td><p>Культурология(Семинар)</p></td></tr></table></body>';
  const dom = new JSDOM(html, {url: 'https://univer.kaznu.kz/student/myschedule/', runScripts: 'outside-only'});
  const w = dom.window, RealDate = w.Date;
  w.Date = class extends RealDate { constructor(...args) { super(...(args.length ? args : [2026, 10, 10, 8, 0])); } static now() { return new RealDate(2026, 10, 10, 8, 0).getTime(); } };
  w.eval(read('page-common.js')); w.eval(read('overview.js'));
  return w;
}

test('the app reads the current week from the schedule page, and nothing for another week', () => {
  const w = schedulePage(10);
  try {
    const week = JSON.parse(w.__univerOverviewV1.week());
    assert.equal(week.start, new Date(2026, 10, 9).getTime());
    assert.equal(week.end, new Date(2026, 10, 16).getTime());
    assert.deepEqual(week.days, [
      [{start: 540, end: 590, name: 'Политология', type: 'Лекция', room: '305', teacher: 'Синтетический П. П.'}],
      [{start: 600, end: 650, name: 'Культурология', type: 'Семинар', room: '', teacher: ''}]]);
  } finally { w.close(); }
  const other = schedulePage(11);
  try { assert.equal(other.__univerOverviewV1.week(), ''); } finally { other.close(); }
});
