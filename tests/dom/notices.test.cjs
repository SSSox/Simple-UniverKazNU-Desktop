const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM, VirtualConsole } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const bundle = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'study-pages.js', 'course-tasks.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'notices.js', 'univer-layout.js', 'desktop.js'].map(read);

// Markup as observed live on 2 November 2026 (Android QA): the count in a red <b> beside the link, the list as tr.link
// rows, an answered row with a "+" cell. Names, ids and dates are synthetic.
const menu = (count = '<b style="color:Red; font-size:10px;">&nbsp;(2)</b>') => '<table id="tools"><tr><td class="center"><table class="tool"><tr><td><ul>' +
  `<li><a href="/student/rating/">Анкетирование студентов</a>${count}</li>` +
  '<li><a href="/student/contract/" style="color:red">Договоры</a></li><li><a href="/student/group/">Группа (2)</a></li>' +
  '<li><a href="/student/umkd/">УМКД</a></li><li><a href="/user/logout">Выход</a></li></ul></td></tr></table></td></tr></table>';
const tabs = '<tr id="l3"><td class="center"><a href="/student/bachelor/"><span class="center">Бакалавр</span></a><a href="/ssc/"><span class="center">Е-услуги</span></a><a href="/">Главная</a></td></tr>';
const shell = (content, count) => '<table class="wrappertable"><tr id="l1"><td class="header">КазНУ</td></tr><tr id="l2"><td></td></tr>' + tabs +
  `<tr id="l4"><td class="center">${menu(count)}${content}</td></tr></table>`;
const rows = marks => '<table class="inner"><tr><th>Название</th><th>Тип</th><th>Начало</th><th>Окончание</th><th>Статус</th></tr>' +
  '<tr class="link" id="208"><td>Удовлетворенность обучающихся качеством преподавания</td><td>Анкетирование преподавателя <p style="color:red;">Обязательная</p></td><td>30.10.2026</td><td>30.11.2026</td>' + `<td>${marks[0]}</td></tr>` +
  '<tr class="link" id="207"><td>Восприятие коррупции - 2026, осенний сем.</td><td>Анкетирование <p style="color:red;">Обязательная</p></td><td>29.10.2026</td><td>11.11.2026</td>' + `<td>${marks[1]}</td></tr></table>`;
const blocked = '<table class="mt"><tr class="mid"><td class="ct"><span style="color:red">Для просмотра успеваемости необходимо пройти <a href="/student/rating/">анкетирование</a>.</span></td></tr></table>';

function page(html, url, { routes = {}, scripts = [read('notices.js')] } = {}) {
  const errors = [], virtualConsole = new VirtualConsole();
  virtualConsole.on('jsdomError', error => errors.push(String(error.message)));
  const dom = new JSDOM(html, { url, runScripts: 'outside-only', virtualConsole });
  const w = dom.window, requests = [], sent = [];
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.TextDecoder = TextDecoder;
  w.__univerNativeNotify = value => sent.push(value);
  w.fetch = address => {
    requests.push(address);
    const body = routes[address];
    const response = { ok: body !== undefined, status: body === undefined ? 404 : 200, url: 'https://univer.kaznu.kz' + address,
      headers: { get: () => 'text/html; charset=utf-8' }, arrayBuffer: async () => new TextEncoder().encode(body || '').buffer };
    return Promise.resolve(response);
  };
  scripts.forEach(script => w.eval(script));
  return { w, d: w.document, requests, sent, errors };
}
const settle = async () => { for (let i = 0; i < 30; i++) await new Promise(resolve => setTimeout(resolve, 0)); };
const scan = (p, fetch = false) => JSON.parse(p.w.__univerNoticesV1.scan(fetch, 'n1'));
const rating = result => result.links.find(link => link.u === '/student/rating/');

test('the red count next to a menu link is a notification; a red link or an unmarked number is not', () => {
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/');
  const before = p.d.body.innerHTML;
  const result = scan(p);
  assert.deepEqual(p.errors, []);
  assert.equal(result.g, 'n1'); assert.equal(result.kind, 'page'); assert.equal(result.menu, true);
  assert.deepEqual({ t: rating(result).t, n: rating(result).n, m: rating(result).m }, { t: 'Анкетирование студентов', n: 2, m: 1 });
  assert.equal(result.links.find(link => link.u === '/student/contract/').n, 0);
  assert.equal(result.links.find(link => link.u === '/student/group/').n, 0);
  assert.equal(result.links.some(link => /logout/.test(link.u)), false, 'account links are never reported');
  assert.equal(rating(result).o, undefined, 'a menu read says nothing about the answers');
  assert.equal(p.d.body.innerHTML, before, 'reading never changes the page');
});

test('a questionnaire list counts only the rows without the "+" of an answer', () => {
  const answered = scan(page(shell(rows(['+', '+'])), 'https://univer.kaznu.kz/student/rating/'));
  assert.equal(rating(answered).n, 2, 'the site keeps its count after the answers');
  assert.equal(rating(answered).o, 0);
  assert.deepEqual(rating(answered).d, []);
  const half = scan(page(shell(rows(['&nbsp;+&nbsp;', ''])), 'https://univer.kaznu.kz/student/rating/'));
  assert.equal(rating(half).o, 1);
  assert.deepEqual(rating(half).d, [{ t: 'Восприятие коррупции - 2026, осенний сем.', e: '11.11.2026' }]);
  const open = scan(page(shell(rows(['', ''])), 'https://univer.kaznu.kz/student/rating/'));
  assert.equal(rating(open).o, 2, 'the header row is not a questionnaire');
});

test('a page message asking for a questionnaire first is reported with its link', () => {
  const result = scan(page(shell(blocked), 'https://univer.kaznu.kz/student/attestation/'));
  assert.equal(result.blocks.length, 1);
  assert.match(result.blocks[0].x, /^Для просмотра успеваемости необходимо пройти анкетирование/);
  assert.equal(result.blocks[0].l, '/student/rating/');
});

test('the background read covers the sections and the counted list, never the site root, and runs once at a time', async () => {
  const routes = {
    '/ssc/': shell('<p>Е-услуги</p>', ''),
    '/student/rating/': shell(rows(['+', '+']))
  };
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { routes });
  const first = scan(p, true);
  assert.equal(first.fetching, true);
  assert.equal(scan(p, true).fetching, false, 'a second request waits for the running one');
  await settle();
  assert.deepEqual(p.requests, ['/ssc/', '/student/rating/']);
  const messages = p.sent.map(value => { assert.ok(value.startsWith('univer-notices:')); return JSON.parse(value.slice('univer-notices:'.length)); });
  assert.deepEqual(messages.map(m => [m.kind, m.page, m.g]), [['section', '/ssc/', 'n1'], ['section', '/student/rating/', 'n1'], ['done', '/student/bachelor/', 'n1']]);
  assert.equal(rating(messages[0]).n, 0, 'the other section lists the link without a count');
  assert.equal(rating(messages[1]).o, 0, 'the list read finds both questionnaires answered');
  assert.deepEqual([messages[2].read, messages[2].failed], [2, 0]);
});

test('a page that fails to load is counted in the last message, and the other pages are still read', async () => {
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { routes: { '/student/rating/': shell(rows(['+', ''])) } });
  scan(p, true);
  await settle();
  const messages = p.sent.map(value => JSON.parse(value.slice('univer-notices:'.length)));
  assert.equal(rating(messages.find(m => m.page === '/student/rating/')).o, 1);
  const done = messages[messages.length - 1];
  assert.deepEqual([done.kind, done.read, done.failed], ['done', 1, 1], '/ssc/ answered 404');
});

test('the reading layout keeps the menu count and the section tabs readable', () => {
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { scripts: bundle });
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.ok(p.d.querySelector('.univer-app-tabs a[href="/ssc/"]'), 'the tabs moved into the app header');
  const result = scan(p);
  assert.equal(rating(result).n, 2);
  assert.equal(result.menu, true);
});
