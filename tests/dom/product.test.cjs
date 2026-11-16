const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
function page(html, url, routes = {}) {
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' }), w = dom.window;
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.fetch = async path => ({ status: routes[path] ? 200 : 404, url: new URL(path, url).href, headers: { get: () => null }, text: async () => routes[path] || '' });
  w.eval(read('page-common.js'));
  return { w, d: w.document, common: w.__univerCommonV1, close: () => w.close() };
}
const courses = (term = '/2026/1') => '<script>$(this).dblclick(function () { window.location = "/student/umkd/" + this.id + "' + term + '"; });</script><div id="l4"><table class="inner"><tr class="link" id="501"><td></td><td>Культурология</td><td>Обязательная</td></tr><tr class="link" id="502"><td></td><td>Казахский (русский) язык</td><td>Обязательная</td></tr></table></div>';

test('a class splits into what it is, where it is and who teaches it, whatever the site puts in its name', () => {
  const p = page('<p>', 'https://univer.kaznu.kz/student/bachelor/');
  const lesson = p.common.lesson(['Культурология(лекция)', 'Преподаватель QA', 'ФИТ / Ауд.: 309', 'Период с 01.09 по 12.12']);
  assert.deepEqual({ ...lesson, rest: [...lesson.rest] }, { name: 'Культурология', type: 'Лекция', kind: 'lecture', room: '309', faculty: 'ФИТ', teacher: 'Преподаватель QA', period: 'Период с 01.09 по 12.12', rest: [] });
  const language = p.common.lesson(['Казахский (русский) язык(семинар)', 'Преподаватель QA', 'МЕХ / Ауд.: Желты']);
  assert.equal(language.name, 'Казахский (русский) язык'); assert.equal(language.kind, 'seminar'); assert.equal(language.room, 'Желты');
  // A bracket that is part of the name stays there, and an unknown line is kept rather than lost.
  const plain = p.common.lesson(['Язык (русский)', 'Первая строка', 'Вторая строка']);
  assert.equal(plain.name, 'Язык (русский)'); assert.equal(plain.type, ''); assert.equal(plain.teacher, 'Первая строка'); assert.equal(plain.rest[0], 'Вторая строка');
  p.close();
});

test('a class links to its course in UMKD from one read of the list, and to the list when the course is not there', async () => {
  let reads = 0;
  const p = page('<p>', 'https://univer.kaznu.kz/student/myschedule/', { '/student/umkd/': courses() });
  const fetch = p.w.fetch; p.w.fetch = (...args) => { reads++; return fetch(...args); };
  assert.equal(await p.common.umkdHref('Культурология'), '/student/umkd/501/2026/1');
  assert.equal(await p.common.umkdHref('Казахский (русский) язык'), '/student/umkd/502/2026/1');
  assert.equal(await p.common.umkdHref('Физика'), '/student/umkd/');
  assert.equal(reads, 1);
  p.close();
});

test('without the list a class still leads to UMKD, and a failed read is not repeated for every class', async () => {
  let reads = 0;
  const p = page('<p>', 'https://univer.kaznu.kz/student/myschedule/');
  const fetch = p.w.fetch; p.w.fetch = (...args) => { reads++; return fetch(...args); };
  assert.equal(await p.common.umkdHref('Культурология'), '/student/umkd/');
  assert.equal(await p.common.umkdHref('Физика'), '/student/umkd/');
  assert.equal(reads, 1);
  p.close();
});

test('the news filter keeps dates behind one button and only speaks up when something is filtered', () => {
  const news = '<div id="l4"><table class="univer-app-news"><tr class="news"><td><div class="date"><span class="day">06</span>11.2026</div></td><td>Новость QA</td></tr></table></div>';
  const p = page(news, 'https://univer.kaznu.kz/news/student');
  p.w.eval(read('news.js')); p.w.__univerNewsV1.apply('ru');
  const bar = p.d.querySelector('.univer-app-news-tools'), period = bar.querySelector('details.univer-app-news-period');
  assert.deepEqual(Array.from(bar.children).map(n => n.className), ['univer-app-news-query', 'univer-app-news-period', 'univer-app-news-important', 'univer-app-news-state']);
  assert.equal(period.querySelector('summary').textContent, 'Период');
  assert.ok(period.contains(bar.querySelector('.univer-app-news-from')) && period.contains(bar.querySelector('.univer-app-news-to')));
  assert.deepEqual(Array.from(period.querySelectorAll('.univer-app-news-preset')).map(b => b.textContent), ['Любая дата', 'За неделю', 'За месяц', 'За полгода']);
  assert.equal(bar.querySelector('.univer-app-news-state').hidden, true);
  period.querySelectorAll('.univer-app-news-preset')[1].click();
  assert.notEqual(bar.querySelector('.univer-app-news-from').value, '');
  assert.equal(bar.querySelector('.univer-app-news-state').hidden, false); assert.equal(period.open, false);
  assert.match(period.querySelector('summary').textContent, /^\d\d\.\d\d – …$/);
  period.querySelectorAll('.univer-app-news-preset')[0].click();
  assert.equal(bar.querySelector('.univer-app-news-from').value, ''); assert.equal(bar.querySelector('.univer-app-news-state').hidden, true);
  p.w.__univerNewsV1.release(); assert.equal(p.d.querySelector('.univer-app-news-tools'), null);
  p.close();
});
