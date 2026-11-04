const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM, VirtualConsole } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const scripts = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'study-pages.js', 'course-tasks.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'univer-layout.js', 'desktop.js'].map(read);

// Distance-course pages rebuilt from the structure observed on 29 October 2026 (course list, curriculum, weeks, a week's
// task list and a task), with synthetic ids, names and text. Routes and script templates follow the live handlers.
const row = (cell, kind = 'mid') => `<tr class="${kind}"><td class="lt"></td>${cell}<td class="rt"></td></tr>`;
const panel = rows => `<table class="mt">${rows}<tr class="bot"><td class="lt"></td><td class="ct"></td><td class="rt"></td></tr></table>`;
const shell = (panels, script = '') => '<table class="inner"><tr><td class="left"></td><td class="center">' +
  panel(row('<td class="ct message"><li>Общий GPA = 3.12</li></td>')) + panels + '</td><td class="right"></td></tr></table>' + (script ? `<script>${script}</script>` : '');
const pad = n => String(n).padStart(2, '0');
const day = offset => { const d = new Date(); d.setDate(d.getDate() + offset); return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`; };

const index = shell(panel(row('<td class="ct">Список дисциплин 2026-2027 год Осенний Семестр</td>', 'top') +
  row('<td class="ts"><a class="lt" id="subjectShow_btn" href="#">Открыть дисциплину</a></td>') +
  row('<td class="tt"><div class="inner"><table class="inner" id="courses"><tr><th width="10%"></th><th>Дисциплина</th></tr>' +
    '<tr class="link" id="7101"><td><img src="/img/folder.png"></td><td>История Казахстана (0)</td></tr>' +
    '<tr class="link" id="7102"><td><img src="/img/folder.png"></td><td>Политология (0)</td></tr>' +
    '<tr class="link" id="other"><td><img src="/img/folder.png"></td><td>Обсуждения других пользователей</td></tr></table></div></td>')),
  '$("table.mt table.inner tr.link").dblclick(function () { if (this.id === "other") { window.location = "/student/discussion/acpos/list/other/2026/1"; } else { window.location = "/student/discussion/acpos/list/" + this.id + "/2026/1/"; } });');
const plans = (rows = ['501']) => shell(panel(row('<td class="ct"><span><a href="/student/discussion/index/2026/1/">Список дисциплин</a></span></td>', 'top') +
  row('<td class="ts"><a class="lt" id="specialityOpen_btn" href="#">Открыть специальность</a></td>') +
  row('<td class="tt"><div class="inner"><table class="inner" id="plans"><tr><th></th><th>Специальность</th><th>Форма обучения</th><th>Курс</th></tr>' +
    rows.map(id => `<tr class="link" id="${id}"><td><img src="/img/folder.png"></td><td>6B0000-Синтетическая специальность (0)</td><td>очная</td><td>1</td></tr>`).join('') + '</table></div></td>')),
  "$('tr.link').dblclick(function () { window.location = '/student/discussion/acpos/week/list/' + this.id + '/7101/2026/1/'; });");
const weekRow = (n, tasks, resources = 0, current = false) => `<tr class="link${current ? ' normal' : ''}" id="${n}"><td><img src="/img/folder.png"></td><td>${n}</td>` +
  `<td>${day((n - 9) * 7 - 2)} 00:00-${day((n - 9) * 7 + 4)} 00:00</td><td>${resources}</td><td>${tasks}</td></tr>`;
const weeks = shell(panel(row('<td class="ct"><span><a href="/student/discussion/index/2026/1/">Список дисциплин</a> / <a href="/student/discussion/acpos/list/7101/2026/1/">Список Уч. планов</a></span></td>', 'top') +
  row('<td class="ts"><a class="lt" id="add_task_btn" href="#">Задания</a><a class="lt" id="lessons_btn" href="#">Проведенные уроки</a></td>') +
  row('<td class="tt"><table class="inner"><tr><th><img src="/img/folder.png"></th><th>Неделя</th><th>Период</th><th>Количество ресурсов</th><th>Количество заданий</th><th></th></tr></table>' +
    '<div class="inner scrool" id="scroll"><table class="inner" id="weeks">' + weekRow(7, 2) + weekRow(8, 0) + weekRow(9, 1, 2, true) + weekRow(10, 0) + '</table></div></td>')),
  '$("tr.link").dblclick(function () { window.location = "/student/discussion/task/list/501/7101/2026/1/" + this.id+"/"; });' +
  '$("#res_btn").click(function () { window.location = "/student/discussion/resource/501/7101/2026/1/" + checked.attr("id") + "/"; });');
const taskList = (rows, week = 9) => shell(panel(row('<td class="ct"><span><a href="/student/discussion/acpos/week/list/501/7101/2026/1/">Недели</a></span></td>', 'top') +
  row('<td class="ts"><a class="lt" id="mark_btn" href="#">Сводный список</a><a class="lt" id="preview_btn" href="#">Просмотр</a></td>') +
  row('<td class="tt"><div class="inner"><table class="inner" id="tasks"><tr><th></th><th>Название</th><th>Дата начала (дд/мм/гггг)</th><th>Дата окончания (дд/мм/гггг)</th><th>Дата созд.</th><th>Участников</th></tr>' +
    rows.map(r => `<tr class="link" id="${r.id}" title="Синтетическое описание"><td><img src="/img/file.png"></td><td>${r.title} (0). Автор: Синтетический Преподаватель</td><td>${day(r.start)}</td><td>${day(r.end)}</td><td>${day(r.start)}</td><td>42</td></tr>`).join('') +
    '<tr class="empty"><td colspan="6"></td></tr></table></div></td>', 'mid" id="files_list')),
  `$("tr.link").dblclick(function () { window.location = "/student/discussion/task/preview/" + checked.attr("id") + "/501/7101/2026/1/${week}/0/0/0/"; });`);
const description = 'План семинарского занятия: 1. Первое синтетическое направление. 2. Второе направление и его этапы. 3. Третье направление в 1858 г. 4.Четвёртое направление без пробела. 5. Пятое направление. Студентам предстоит проанализировать все пять направлений и сравнить подходы.';
const task = (end = 1) => shell(panel(row('<td class="ct"><a href="/student/discussion/acpos/week/list/501/7101/2026/1/">Недели</a><a href="/student/discussion/task/list/501/7101/2026/1/9/">Задания</a></td>', 'top') +
  row('<td class="ct"><table class="xp"><tr><td><li>Предмет:</li></td><td>История Казахстана</td><td><li>Текущая неделя:</li></td><td>9</td></tr></table></td>') +
  row('<td class="tt"><div class="inner" id="source"><table class="inner"><tr id="Tr1"><th>Информация</th><th>Сообщения</th></tr><tr class="message"><td valign="top"><div class="inf">' +
    '<span class="btn"><a class="btn ans" id="reply" href="#mes" title="Отправить сообщение автору" name="user_77"><img src="/img/reply.png"></a></span>' +
    '<span class="title">Название задания:</span>Синтетическое задание по истории<br><span class="capt" title="(teacher)">Автор:</span>Синтетический Преподаватель<br>' +
    `<span class="duration">Продолжительность:</span><br>${day(-3)} 00:00-${day(end)} 23:00<span class="title">Максимальное кол-во ответов:</span><span>1</span><br>` +
    '<span class="title">Максимальный балл:</span>10<br></div></td>' +
    `<td valign="top"><span class="header">Формат ответа:</span>Ответ в виде файла<br><span class="header">Описание:</span>${description}</td></tr></table></div></td>`, 'mid" id="Tr1') +
  row('<td class="tt"><div class="inner"><table class="inner"><tr id="false"><th>Информация</th><th>Сообщения</th></tr></table></div></td>', 'mid" id="group_list') +
  row('<td class="ct">Написать сообщение</td>', 'brk') +
  row('<td class="ct"><a name="mes"></a><form id="send_message" action="/student/discussion/task/send/9001/501/7101/2026/1/9/" method="post"><select id="user_to" name="user_to"><option value="">Преподаватель</option></select>' +
    '<input type="button" id="sb_btn" value="Отправить"></form></td>')));

function page(content, url, { routes = {}, referrer, storage } = {}) {
  const html = '<table class="wrappertable"><tr id="l1"><td class="header">КазНУ</td></tr><tr id="l4"><td>' + content + '</td></tr></table>';
  const errors = [], virtualConsole = new VirtualConsole();
  virtualConsole.on('jsdomError', error => errors.push(String(error.message)));
  const dom = new JSDOM(html, { url, referrer, runScripts: 'outside-only', virtualConsole });
  const w = dom.window, requests = [];
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{ width: 100, height: 30 }]; };
  w.HTMLElement.prototype.scrollIntoView = function () { w.__scrolled = this.id; };
  w.jQuery = () => ({ bind() {} });
  w.__univerNativeNotify = () => {};
  if (storage) Object.entries(storage).forEach(([key, value]) => w.sessionStorage.setItem(key, value));
  w.fetch = (address, options) => {
    requests.push({ address, options });
    const body = routes[address];
    if (body === undefined) return Promise.resolve({ ok: false, status: 404, url: 'https://univer.kaznu.kz' + address, text: async () => '' });
    return Promise.resolve({ ok: true, status: 200, url: 'https://univer.kaznu.kz' + address, text: async () => body });
  };
  scripts.forEach(script => w.eval(script));
  return { w, d: w.document, requests, errors, close: () => w.close() };
}
const apply = (p, on = true, language = 'ru') => p.w.__univerDesktop.apply(on, false, language);
const settle = async () => { for (let i = 0; i < 20; i++) await new Promise(resolve => setTimeout(resolve, 0)); };
const siteRoutes = {
  '/student/discussion/acpos/list/7101/2026/1/': plans(),
  '/student/discussion/acpos/week/list/501/7101/2026/1/': weeks,
  '/student/discussion/task/list/501/7101/2026/1/9/': taskList([{ id: 9001, title: 'Синтетическое задание по истории', start: -3, end: 1 }]),
  '/student/discussion/task/list/501/7101/2026/1/7/': taskList([{ id: 9002, title: 'Прошлое синтетическое задание', start: -17, end: -12 }], 7)
};

test('the course list shows current assignments from the nearby weeks and links each to its task and course weeks', async () => {
  const p = page(index, 'https://univer.kaznu.kz/student/discussion/index/', { routes: siteRoutes });
  apply(p);
  const box = p.d.querySelector('.univer-app-tasks');
  assert.ok(box, 'an assignments panel above the course list');
  assert.equal(box.dataset.busy, 'true');
  assert.match(box.querySelector('.univer-app-tasks-state').textContent, /^Проверяю дисциплины: 0 из 2$/);
  assert.equal(box.nextElementSibling, p.d.getElementById('courses').closest('table.mt'));
  await settle();
  assert.equal(box.dataset.busy, 'false');
  const items = box.querySelectorAll('.univer-app-task-item');
  assert.equal(items.length, 1, 'week 7 is not next to the current week 9, so its task list is not read');
  const link = items[0].querySelector('a.univer-app-task-link');
  assert.equal(link.getAttribute('href'), '/student/discussion/task/preview/9001/501/7101/2026/1/9/0/0/0/');
  assert.equal(link.querySelector('.univer-app-task-title').textContent, 'Синтетическое задание по истории');
  assert.equal(link.querySelector('.univer-app-task-meta').textContent, 'История Казахстана · Неделя 9 · Синтетический Преподаватель');
  const chip = link.querySelector('.univer-app-task-status');
  assert.equal(chip.dataset.state, 'urgent'); assert.match(chip.textContent, /^до \d\d\.\d\d · остался 1 день$/);
  assert.match(link.getAttribute('aria-label'), /^Открыть задание: Синтетическое задание по истории, до /);
  const course = p.d.getElementById('7101').querySelector('.univer-app-course-tasks');
  assert.equal(course.textContent, '1 задание');
  assert.equal(course.getAttribute('href'), '/student/discussion/acpos/week/list/501/7101/2026/1/', 'a single curriculum row links straight to the weeks');
  assert.equal(p.d.getElementById('7102').querySelector('.univer-app-course-tasks'), null);
  assert.equal(box.querySelector('.univer-app-tasks-note').textContent, 'Не удалось проверить: Политология');
  assert.equal(p.d.getElementById('7101').querySelector('.univer-app-course-open').getAttribute('aria-label'), 'Открыть дисциплину: История Казахстана (0)', 'the course badge is not part of the button name');
  assert.deepEqual(p.requests.map(r => r.address).sort(), ['/student/discussion/acpos/list/7101/2026/1/', '/student/discussion/acpos/list/7102/2026/1/',
    '/student/discussion/acpos/week/list/501/7101/2026/1/', '/student/discussion/task/list/501/7101/2026/1/9/']);
  assert.ok(p.requests.every(r => r.options.credentials === 'same-origin' && r.options.redirect === 'error' && !r.options.method && !r.options.body), 'plain same-origin GET, no redirects');
  apply(p, true, 'en');
  assert.equal(box.querySelector('.univer-app-tasks-title').textContent, 'Assignments');
  assert.match(box.querySelector('.univer-app-task-status').textContent, /^due \d\d\.\d\d · 1 day left$/);
  assert.equal(course.isConnected ? course.textContent : p.d.getElementById('7101').querySelector('.univer-app-course-tasks').textContent, '1 assignment');
  const cached = Object.keys(p.w.sessionStorage).find(key => key.startsWith('univer-app-course-tasks:'));
  assert.ok(cached, 'the result is kept for this browser session');
  const stored = p.w.sessionStorage.getItem(cached);
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-tasks,.univer-app-course-tasks,#univer-tasks-style').length, 0);
  assert.equal(p.d.getElementById('7101').cells[1].textContent, 'История Казахстана (0)');
  p.close();

  const again = page(index, 'https://univer.kaznu.kz/student/discussion/index/', { routes: siteRoutes, storage: { [cached]: stored } });
  apply(again);
  assert.equal(again.requests.length, 0, 'a recent result is shown without reading the pages again');
  assert.equal(again.d.querySelectorAll('.univer-app-task-item').length, 1);
  again.d.querySelector('.univer-app-tasks-refresh').click();
  assert.ok(again.requests.length > 0, 'Refresh reads them again');
  await settle(); again.close();
});

test('an expired session or a login page in a background read counts as a failed course, never as no assignments', async () => {
  const login = '<form id="login_frm" action="/user/login"><input type="text"><input type="password"></form>';
  const p = page(index, 'https://univer.kaznu.kz/student/discussion/index/', { routes: { '/student/discussion/acpos/list/7101/2026/1/': login } });
  apply(p); await settle();
  assert.equal(p.d.querySelectorAll('.univer-app-task-item').length, 0);
  assert.equal(p.d.querySelector('.univer-app-tasks-note').textContent, 'Не удалось проверить: История Казахстана, Политология');
  assert.equal(p.d.querySelector('.univer-app-tasks-empty').textContent, 'Текущих заданий нет');
  apply(p, false); p.close();
});

test('a course list without the site handler template reads nothing and adds nothing', async () => {
  const p = page(index.replace(/<script>[\s\S]*<\/script>/, ''), 'https://univer.kaznu.kz/student/discussion/index/', { routes: siteRoutes });
  apply(p); await settle();
  assert.equal(p.requests.length, 0);
  assert.equal(p.d.querySelector('.univer-app-tasks'), null);
  apply(p, false); p.close();
});

test('a one-row curriculum opened from the course list goes straight to its weeks; otherwise it stays with Open buttons', () => {
  const forward = page(plans(), 'https://univer.kaznu.kz/student/discussion/acpos/list/7101/2026/1/', { referrer: 'https://univer.kaznu.kz/student/discussion/index/' });
  apply(forward);
  assert.ok(forward.errors.some(message => /navigation/i.test(message)), 'location.replace to the weeks page was attempted');
  forward.close();
  for (const [content, referrer] of [[plans(), 'https://univer.kaznu.kz/student/discussion/acpos/week/list/501/7101/2026/1/'], [plans(['501', '502']), 'https://univer.kaznu.kz/student/discussion/index/'], [plans(), 'https://example.org/student/discussion/index/']]) {
    const p = page(content, 'https://univer.kaznu.kz/student/discussion/acpos/list/7101/2026/1/', { referrer });
    let opened = null; p.d.getElementById('plans').addEventListener('dblclick', event => { opened = event.target.closest('tr').id; });
    apply(p); apply(p);
    assert.ok(!p.errors.some(message => /navigation/i.test(message)), 'no automatic navigation from ' + referrer);
    const buttons = p.d.querySelectorAll('#plans .univer-app-row-open');
    assert.equal(buttons.length, p.d.querySelectorAll('#plans tr.link').length);
    assert.equal(buttons[0].getAttribute('aria-label'), 'Открыть недели: 6B0000-Синтетическая специальность (0)');
    buttons[0].click();
    assert.equal(opened, '501', 'Open runs the site double-click handler');
    apply(p, false);
    assert.equal(p.d.querySelectorAll('.univer-app-row-open,.univer-app-open-cell').length, 0);
    p.close();
  }
});

test('weeks: the whole list, the current week and counts marked, materials linked, and the course assignments above', async () => {
  const p = page(weeks, 'https://univer.kaznu.kz/student/discussion/acpos/week/list/501/7101/2026/1/', { routes: siteRoutes });
  const tasksCell = p.d.getElementById('9').cells[4], original = tasksCell.firstChild;
  apply(p); apply(p);
  assert.ok(p.d.getElementById('scroll').classList.contains('univer-app-weeks-box'), 'no short inner scroll box');
  assert.equal(p.d.querySelectorAll('.univer-app-week-current').length, 1);
  assert.equal(p.d.getElementById('9').querySelector('.univer-app-week-current').textContent, 'Текущая неделя');
  assert.equal(tasksCell.querySelector('.univer-app-week-count').dataset.state, 'has');
  assert.equal(p.d.getElementById('8').cells[4].querySelector('.univer-app-week-count').dataset.state, 'none');
  const materials = p.d.getElementById('9').querySelector('a.univer-app-week-materials');
  assert.equal(materials.getAttribute('href'), '/student/discussion/resource/501/7101/2026/1/9/'); assert.equal(materials.textContent, 'Материалы: 2');
  assert.equal(p.d.querySelectorAll('.univer-app-week-materials').length, 1, 'only weeks that have materials');
  assert.deepEqual(Array.from(p.d.querySelectorAll('#weeks .univer-app-row-open')).map(b => b.closest('tr').id), ['7', '9'], 'Open only on weeks with assignments, materials or the current one');
  assert.equal(p.d.getElementById('9').querySelector('.univer-app-row-open').getAttribute('aria-label'), 'Открыть неделю 9');
  await settle();
  const box = p.d.querySelector('.univer-app-tasks');
  assert.equal(box.parentElement, p.d.getElementById('scroll').parentElement, 'under the course heading, above the weeks');
  assert.equal(box.querySelector('.univer-app-tasks-state').textContent, '');
  const items = Array.from(box.querySelectorAll('.univer-app-task-item'));
  assert.deepEqual(items.map(i => i.dataset.state), ['urgent', 'past'], 'open assignments first, closed ones last');
  assert.equal(items[1].querySelector('.univer-app-task-meta').textContent, 'Неделя 7 · Синтетический Преподаватель');
  assert.match(items[1].querySelector('.univer-app-task-status').textContent, /срок прошёл$/);
  apply(p, false);
  assert.equal(tasksCell.firstChild, original); assert.equal(tasksCell.childNodes.length, 1);
  assert.equal(p.d.querySelectorAll('.univer-app-tasks,.univer-app-week-current,.univer-app-week-materials,.univer-app-week-count,.univer-app-weeks-box,.univer-app-week,.univer-app-row-open').length, 0);
  p.close();
});

test('a week task list gets deadline badges and Open buttons that use the site handler', () => {
  const p = page(taskList([{ id: 9001, title: 'Открытое задание', start: -3, end: 4 }, { id: 9003, title: 'Будущее задание', start: 3, end: 9 }, { id: 9002, title: 'Закрытое задание', start: -9, end: -2 }]),
    'https://univer.kaznu.kz/student/discussion/task/list/501/7101/2026/1/9/');
  let opened = null; p.d.getElementById('tasks').addEventListener('dblclick', event => { opened = event.target.closest('tr').id; });
  apply(p); apply(p);
  const states = ['9001', '9003', '9002'].map(id => p.d.getElementById(id).querySelector('.univer-app-task-status'));
  assert.deepEqual(states.map(s => s.dataset.state), ['open', 'soon', 'past']);
  assert.equal(states[0].textContent, 'осталось 4 дня');
  assert.match(states[1].textContent, /^откроется \d\d\.\d\d$/);
  assert.equal(states[2].textContent, 'срок прошёл');
  const button = p.d.getElementById('9001').querySelector('.univer-app-row-open');
  assert.equal(p.d.querySelectorAll('#tasks .univer-app-row-open').length, 3);
  assert.equal(button.getAttribute('aria-label'), 'Открыть задание: Открытое задание (0). Автор: Синтетический Преподаватель');
  button.click(); assert.equal(opened, '9001');
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-task-status,.univer-app-row-open').length, 0);
  p.close();
});

test('a task becomes a card: heading, deadline, facts, a numbered plan and a reply button; Original shows the site table', () => {
  const p = page(task(), 'https://univer.kaznu.kz/student/discussion/task/preview/9001/501/7101/2026/1/9/0/0/0/');
  let addressee = null; p.d.getElementById('reply').addEventListener('click', () => { addressee = 'set'; p.d.getElementById('user_to').value = ''; });
  apply(p); apply(p);
  const cards = p.d.querySelectorAll('.univer-app-task-card');
  assert.equal(cards.length, 1, 'one card after repeated passes');
  const card = cards[0];
  const details = p.d.querySelector('table.xp');
  assert.ok(card.parentElement.contains(details) && card.compareDocumentPosition(details) & p.w.Node.DOCUMENT_POSITION_FOLLOWING, 'the task comes before the course details');
  assert.ok(p.d.getElementById('source').classList.contains('univer-app-task-source'), 'the site table is hidden, not removed');
  assert.ok(p.d.getElementById('group_list').classList.contains('univer-app-task-source'), 'an empty message history is hidden');
  assert.equal(card.querySelector('.univer-app-task-kicker').textContent, 'История Казахстана · Неделя 9');
  assert.equal(card.querySelector('h2').textContent, 'Синтетическое задание по истории');
  const due = card.querySelector('.univer-app-task-facts>.univer-app-task-status');
  assert.equal(due.dataset.state, 'urgent'); assert.match(due.textContent, /^до \d\d\.\d\d\.\d{4}, 23:00 · остался 1 день$/);
  assert.deepEqual(Array.from(card.querySelectorAll('.univer-app-task-fact')).map(f => f.textContent), [
    `Продолжительность${day(-3)} 00:00 – ${day(1)} 23:00`, 'Формат ответаОтвет в виде файла', 'АвторСинтетический Преподаватель',
    'Максимальное кол-во ответов1', 'Максимальный балл10']);
  assert.equal(card.querySelector('.univer-app-task-heading').textContent, 'Описание');
  assert.equal(card.querySelector('.univer-app-task-intro').textContent, 'План семинарского занятия:');
  assert.deepEqual(Array.from(card.querySelectorAll('ol li')).map(li => li.textContent), ['Первое синтетическое направление.', 'Второе направление и его этапы.',
    'Третье направление в 1858 г.', 'Четвёртое направление без пробела.', 'Пятое направление.']);
  assert.equal(card.querySelector('ol').nextElementSibling.textContent, 'Студентам предстоит проанализировать все пять направлений и сравнить подходы.');
  const reply = card.querySelector('.univer-app-task-reply');
  assert.equal(reply.textContent, 'Отправить сообщение автору');
  reply.click();
  assert.equal(addressee, 'set', 'the site reply handler ran'); assert.equal(p.w.__scrolled, 'send_message');
  assert.equal(p.w.location.hash, '', 'the hash link itself did not navigate');
  assert.equal(p.d.getElementById('sb_btn').value, 'Отправить', 'the message form is left to the user');
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-task-card,.univer-app-task-source').length, 0);
  assert.equal(p.d.getElementById('source').getAttribute('class'), 'inner');
  p.close();
});

test('a description whose numbers do not run 1, 2, 3 stays as written', () => {
  const plainText = 'Прочитать главы 2. и 5. учебника, затем ответить на вопросы 1. Почему так. Сдать до пятницы.';
  const p = page(task(5).replace(description, plainText), 'https://univer.kaznu.kz/student/discussion/task/preview/9001/501/7101/2026/1/9/0/0/0/');
  apply(p);
  const text = p.d.querySelector('.univer-app-task-text');
  assert.equal(text.querySelector('ol'), null);
  assert.equal(text.textContent, plainText);
  assert.equal(p.d.querySelector('.univer-app-task-facts>.univer-app-task-status').dataset.state, 'open');
  apply(p, false); p.close();
});
