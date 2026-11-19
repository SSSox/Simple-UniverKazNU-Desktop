const nodeTest = require('node:test');
const opened = [];
const test = (name, body) => nodeTest.test(name, async () => { try { await body(); } finally { opened.splice(0).forEach(w => { try { w.close(); } catch (_) {} }); } });
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM, VirtualConsole } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const scripts = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'study-pages.js', 'course-tasks.js', 'syllabus-tasks.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'univer-layout.js', 'desktop.js'].map(read);

// Pages rebuilt from the structure observed on 11 November 2026: the home page's "Специальность" and "Отделение" rows,
// the UMKD course list and a course page with several printed syllabi, the syllabus itself (table.students rows, a week
// plan whose rows hold td.it and a nested table.full-width) and the schedule. Ids, names and topics are synthetic.
const MINE = '6B01234', OTHER = '6B09999';
const site = html => '<table class="wrappertable"><tr id="l4"><td class="center">' + html + '</td></tr></table>';
const home = (code = MINE, division = 'русское') => site('<table class="inner"><tr><td><li>Форма обучения: </li></td><td>очная</td><td><li>Отделение: </li></td><td>' + division + '</td></tr>' +
  '<tr><td><li>Факультет: </li></td><td>Синтетический факультет</td><td><li>Специальность: </li></td><td>' + code + ' Синтетическая безопасность</td></tr></table>');
const courseList = courses => site('<table class="mt"><tr><td class="tt"><div class="inner"><table class="inner"><tr><th></th><th>Дисциплина</th><th>Тип</th></tr>' +
  courses.map(c => `<tr class="link" id="${c[0]}"><td><img src="/img/folder.png"></td><td>${c[1]}</td><td>Обязательная</td></tr>`).join('') + '</table></div></td></tr></table>') +
  '<script>$("tr.link").dblclick(function () { window.location = "/student/umkd/" + this.id + "/2026/1"; });</script>';
const coursePage = links => site('<table class="mt"><tr><td class="tt"><div class="inner"><div><h4>Силлабус</h4>' +
  links.map(l => `<a href="/student/interactive/syllabus/print/${l[0]}/2">${l[1]}</a>`).join('') + '</div><table class="inner"><tr><th></th><th>Название файла</th></tr>' +
  '<tr class="file" id="1"><td></td><td><a class="downLoad" href="/student/umkd/get/1/2/2026/1">Силлабус.docx</a></td></tr></table></div></td></tr></table>');
const entry = (type, name, topic, score, rubric) => `<tr><td><div><div><div>${type}:</div><div><div>${name}</div></div></div><div><div><div><div>${topic}</div></div></div></div><div><div>${score}</div></div>` +
  (rubric ? '<div><b>Рубрикатор критериального оценивания</b>[<a href="" class="toggle-link">Показать</a>]<div class="rubricator-content"><table><tbody><tr><td colspan="5">КРИТЕРИИ</td></tr><tr><td class="it">9</td><td>Критерий</td></tr></tbody></table></div></div>' : '') + '</div></td></tr>';
const week = (n, rows) => `<tr><td class="it">${n}</td><td colspan="3"><table class="full-width"><tbody>${rows.join('')}</tbody></table></td></tr>`;
const group = (code, division = 'русское') => `Группа №: 1000001 (Образовательная программа: ${code} Синтетическая программа, Уровень обучения: Первое высшее образование, Семестр: 1, Курс обучения: 1, Языковое отделение: ${division})`;
const syllabus = ({ year = 2026, groups, people = 'Образцов Образец Образцович', weeks }) => '<div class="noprint"><a href="/student/interactive/syllabus/list/">[Назад]</a></div><div id="content">' +
  '<table class="students"><tbody><tr><td class="label">Автор:</td><td class="text">Образцов О. О.</td></tr><tr><td class="label">Общее количество Кредитов:</td><td class="text">6,00</td></tr>' +
  `<tr><td class="label">Академический год</td><td class="text">${year}</td></tr><tr><td class="label">Образовательная программа</td><td class="text">${groups.join(' ')};</td></tr>` +
  `<tr><td class="label">Лектор-(ы):</td><td class="text"><div class="input-row"><div class="input-field"><span>${people}</span></div></div></td></tr></tbody></table>` +
  '<div class="table-scroll"><table class="students"><thead><tr><td class="centered"><label>Неделя</label></td><td class="centered"><label>Тип занятия</label></td><td class="centered"><label>Тема</label></td><td class="centered"><label>Максимальный балл</label></td></tr></thead>' +
  `<tbody><tr><td colspan="4"><h1>МОДУЛЬ 1 Синтетический модуль</h1></td></tr>${weeks.join('')}</tbody></table></div></div>`;
const lessonCell = l => l ? `<td class="field"><div class="groups"><div><p class="teacher">${l[0]}(${l[1]})</p><p class="teacher">${l[2]}</p><p class="params"><span class="aud_faculty">ФИТ</span><span>Ауд.: 309</span></p></div></div></td>` : '<td class="field"></td>';
// The site's week list: week 1 starts on Monday 31.08.2026, and every option's value is that week's own page.
const date = (n, offset) => { const d = new Date(Date.UTC(2026, 7, 31 + 7 * (n - 1) + offset)); return String(d.getUTCDate()).padStart(2, '0') + '.' + String(d.getUTCMonth() + 1).padStart(2, '0') + '.' + d.getUTCFullYear(); };
const weekHref = (n, term = '2026/1') => `/student/myschedule/${term}/${date(n, 0)}/${date(n, 6)}/`;
// rows: one array per time row, each with up to six classes (Monday first).
const schedule = (rows, selected = 4, term = '2026/1', weeks = [3, 4, 5]) => '<select id="weekSelect">' + weeks.map(n => `<option value="${weekHref(n, term)}"${n === selected ? ' selected' : ''}>Неделя ${String(n).padStart(2, '0')}. ${date(n, 0)} - ${date(n, 6)}</option>`).join('') + '</select>' +
  '<div class="inner"><table class="schedule"><tr><th>&nbsp;</th>' + ['ПН', 'ВТ', 'СР', 'ЧТ', 'ПТ', 'СБ'].map(d => '<th>' + d + '</th>').join('') + '</tr>' +
  rows.map((row, i) => `<tr><td class="time"><div class="divIE" style="display:none">${9 + i}:00-${9 + i}:50</div></td>` + [0, 1, 2, 3, 4, 5].map(d => lessonCell(row[d])).join('') + '</tr>').join('') + '</table></div>';

const LONG = 'Подготовить письменный разбор источников по теме синтетического курса. ' + 'Сравнить подходы разных авторов и записать выводы в таблицу. '.repeat(8);
const politics = syllabus({ groups: [group(OTHER), group(MINE)], people: 'Учителева Анна Петровна', weeks: [
  week(3, [entry('лекция', 'лекция 3', 'Тема лекции третьей недели', 1), entry('семинар', 'семинар 3', 'Тема семинара третьей недели', 5)]),
  week(4, [entry('лекция', 'лекция 4', 'Политическая власть и её источники', 1), entry('семинар', 'семинар 4', 'Разбор кейса о разделении властей', 7),
    entry('СРО/СРОП', 'СРОП 2', 'СРОП 2. Консультация по выполнению СРО 1', 0, true)]),
  week(5, [entry('лекция', 'лекция 5', 'Политические режимы', 1), entry('семинар', 'семинар 5', LONG, 7)])] });
const culture = syllabus({ groups: [group(MINE)], weeks: [
  week(4, [entry('лекция', 'лекция 4', 'Культура как система знаков', 0), entry('СРО/СРОП', 'СРО/СРОП 4', 'Консультация по выполнению СРС 1 «Символы культуры»', 0, true),
    entry('СРО/СРОП', 'СРО/СРОП 4', 'Прием СРС 1 «Символы культуры»', 22, true)])] });
const history = syllabus({ groups: [group(MINE)], weeks: [
  week(4, [entry('лекция', 'лекция 4', 'Тюркская цивилизация', 0), entry('СРО/СРОП', 'СРО 1', 'Контрольная работа. Письменно.', 15, true), entry('СРО/СРОП', 'СРОП 3', 'Разъяснение замечаний по СРО', 0)])] });
const routes = () => ({
  '/student/bachelor/': home(),
  '/student/umkd/': courseList([['7001', 'Политология'], ['7002', 'Культурология'], ['7003', 'Иностранный язык'], ['7004', 'Физическая культура'], ['7005', 'История Казахстана']]),
  '/student/umkd/7001/2026/1': coursePage([['9001', 'Политология (другой факультет)'], ['9002', 'Политология 25-26'], ['9004', 'Политология (Осень 2026)'], ['9003', 'Политология каз. отделение']]),
  '/student/umkd/7002/2026/1': coursePage([['9010', 'Культурология']]),
  '/student/umkd/7003/2026/1': coursePage([['9020', 'ИС_Ин.яз_Юриспруденция']]),
  '/student/umkd/7004/2026/1': coursePage([]),
  '/student/umkd/7005/2026/1': coursePage([['9030', 'История Казахстана']]),
  '/student/interactive/syllabus/print/9001/2': syllabus({ groups: [group(OTHER)], weeks: [week(4, [entry('лекция', 'лекция 4', 'Чужая тема', 1)])] }),
  '/student/interactive/syllabus/print/9002/2': syllabus({ year: 2025, groups: [group(MINE)], people: 'Учителева Анна Петровна', weeks: [week(4, [entry('лекция', 'лекция 4', 'Прошлогодняя тема', 1)])] }),
  '/student/interactive/syllabus/print/9003/2': syllabus({ groups: [group(MINE, 'казахское')], weeks: [week(4, [entry('лекция', 'лекция 4', 'Тема казахского отделения', 1)])] }),
  '/student/interactive/syllabus/print/9004/2': politics,
  '/student/interactive/syllabus/print/9010/2': culture,
  '/student/interactive/syllabus/print/9020/2': syllabus({ groups: [group(OTHER)], weeks: [week(4, [entry('семинар', 'семинар 4', 'Чужой семинар', 5)])] }),
  '/student/interactive/syllabus/print/9030/2': history
});
const lessons = [
  [['Политология', 'лекция', 'Учителева А. П.'], ['Культурология', 'лекция', 'Незнакомов Н. Н.']],
  [['Политология', 'семинар', 'Помощников П. П.'], ['Иностранный язык', 'семинар', 'Языкова Я. Я.']],
  [['Физическая культура', 'практика', 'Спортов С. С.']]];

function page(content, { routes: map = {}, local, dark, now, extension } = {}) {
  const html = '<table class="wrappertable"><tr id="l1"><td class="header">КазНУ</td></tr><tr id="l4"><td>' + content + '</td></tr></table>';
  const errors = [], virtualConsole = new VirtualConsole();
  virtualConsole.on('jsdomError', error => errors.push(String(error.message)));
  const dom = new JSDOM(html, { url: 'https://univer.kaznu.kz/student/myschedule/', runScripts: 'outside-only', virtualConsole });
  const w = dom.window, requests = []; opened.push(w);
  // now: the moment the page is open at, as Date arguments in local time.
  if (now) { const Real = w.Date, at = new Real(...now).getTime(); w.Date = class extends Real { constructor(...args) { super(...(args.length ? args : [at])); } static now() { return at; } }; }
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.__univerNative = () => {};
  if (local) Object.entries(local).forEach(([key, value]) => w.localStorage.setItem(key, value));
  // What the extension's prelude.js sets before the page scripts.
  if (extension) w.__univerOverviewKeep = false;
  w.fetch = address => {
    requests.push(address);
    // Every other week of the schedule is the shown page again unless a test gives that week its own classes; null is a failed read.
    const body = map[address] === undefined && /^\/student\/myschedule\/\d{4}\/[12]\//.test(address) ? site(content) : map[address];
    if (body === undefined || body === null) return Promise.resolve({ ok: false, status: 404, url: 'https://univer.kaznu.kz' + address, text: async () => '' });
    return Promise.resolve({ ok: true, status: 200, url: 'https://univer.kaznu.kz' + address, text: async () => body });
  };
  scripts.forEach(script => w.eval(script));
  if (dark) { w.eval(read('theme.js')); w.__univerTheme = 'dark'; }
  return { w, d: w.document, requests, errors, close: () => w.close() };
}
const apply = (p, on = true, language = 'ru') => p.w.__univerDesktop.apply(on, false, language);
const until = async check => { for (let i = 0; i < 400 && !check(); i++) await new Promise(resolve => setTimeout(resolve, 5)); assert.ok(check(), 'the page did not reach the expected state'); };
const done = p => () => !!p.d.querySelector('.univer-app-syl-panel') && p.d.querySelector('.univer-app-syl-progress').hidden && !p.d.querySelector('.univer-app-syl-task[data-state=loading]');
const lessonOf = (p, subject, type) => Array.from(p.d.querySelectorAll('.univer-app-lesson')).find(card => card.querySelector('.univer-app-lesson-name').textContent === subject && card.querySelector('.univer-app-lesson-type')?.textContent.toLowerCase() === type);
const topics = card => Array.from(card.querySelectorAll('.univer-app-syl-topic')).map(node => node.textContent);
const click = (p, node) => node.dispatchEvent(new p.w.MouseEvent('click', { bubbles: true, cancelable: true }));
const STORE = 'univer.desktop.syllabus.v1', PREFS = 'univer.desktop.syllabus-ui.v1';

test('each class shows the topic of its week and kind from the syllabus of the student\'s own programme', async () => {
  const p = page(schedule(lessons), { routes: routes() });
  apply(p); await until(done(p));
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'лекция')), ['Политическая власть и её источники']);
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'семинар')), ['Разбор кейса о разделении властей']);
  assert.equal(lessonOf(p, 'Политология', 'семинар').querySelector('.univer-app-syl-meta').textContent, 'семинар 4 · макс. балл: 7');
  assert.deepEqual(topics(lessonOf(p, 'Культурология', 'лекция')), ['Культура как система знаков']);
  // No syllabus names the student's programme, and a course without syllabi: nothing is guessed from another programme.
  assert.equal(lessonOf(p, 'Иностранный язык', 'семинар').querySelector('.univer-app-syl-task'), null);
  assert.equal(lessonOf(p, 'Физическая культура', 'практика').querySelector('.univer-app-syl-task'), null);
  const panel = p.d.querySelector('.univer-app-syl-panel');
  // Nothing is said about what is missing: no week chip, no programme, no notes for courses without a syllabus.
  assert.equal(panel.querySelector('.univer-app-syl-week'), null); assert.equal(panel.querySelector('.univer-app-syl-program'), null);
  assert.equal(p.d.querySelector('.univer-app-syl-label'), null);
  assert.deepEqual(Array.from(panel.querySelectorAll('.univer-app-syl-note')), []);
  // Among the syllabi of the programme the one of this year, the student's division and the schedule's teacher wins;
  // the older and foreign ones behind it are never read.
  assert.ok(p.requests.includes('/student/interactive/syllabus/print/9003/2') && p.requests.includes('/student/interactive/syllabus/print/9004/2'));
  assert.ok(!p.requests.includes('/student/interactive/syllabus/print/9002/2') && !p.requests.includes('/student/interactive/syllabus/print/9001/2'));
  assert.equal(lessonOf(p, 'Политология', 'лекция').querySelector('.univer-app-syl-source').getAttribute('href'), '/student/interactive/syllabus/print/9004/2');
  assert.deepEqual(p.errors, []);
  apply(p, false); p.close();
});

test('the week\'s independent work is listed by course without consultations', async () => {
  const p = page(schedule(lessons), { routes: routes() });
  apply(p); await until(done(p));
  const rows = Array.from(p.d.querySelectorAll('.univer-app-syl-item')).map(row => [row.querySelector('.univer-app-syl-course').textContent, row.querySelector('.univer-app-syl-meta').textContent, row.querySelector('.univer-app-syl-topic').textContent]);
  // Culture is on the schedule and comes first; History has no class this week and still owes its work.
  assert.deepEqual(rows, [
    ['Культурология', 'макс. балл: 22', 'Прием СРС 1 «Символы культуры»'],
    ['История Казахстана', 'СРО 1 · макс. балл: 15', 'Контрольная работа. Письменно.']]);
  assert.equal(p.d.querySelector('.univer-app-syl-count').textContent, '2');
  assert.equal(p.d.querySelector('.univer-app-syl-course').getAttribute('href'), '/student/umkd/7002/2026/1');
  assert.deepEqual(Array.from(p.d.querySelectorAll('.univer-app-syl-note')), []);
  assert.ok(!/Консультация|Разъяснение|СРОП/.test(p.d.querySelector('.univer-app-schedule').textContent));
  // Only the adapter's class name links are restyled; the panel's links stay plain.
  assert.equal(p.d.querySelector('.univer-app-syl-panel a.univer-app-page-link'), null);
  apply(p, false); p.close();
});

test('the current week\'s independent work is kept for the home page only from a finished reading of that week', async () => {
  const OVERVIEW = 'univer.desktop.overview.v1', mark = { 'univer.app.account.v1': 'a1' }, WEDNESDAY = [2026, 8, 23, 10, 0];
  const work = p => (JSON.parse(p.w.localStorage.getItem(OVERVIEW) || '{}').work || null);
  // Week 4 (21–27 September) on screen on its Wednesday: the list the page shows, without consultations.
  const p = page(schedule(lessons), { routes: routes(), local: mark, now: WEDNESDAY });
  apply(p); await until(done(p));
  const kept = JSON.parse(p.w.localStorage.getItem(OVERVIEW));
  assert.equal(kept.owner, 'a1'); assert.equal(kept.language, 'ru');
  assert.equal(kept.work.start, new Date(2026, 8, 21).getTime()); assert.equal(kept.work.end, new Date(2026, 8, 28).getTime());
  assert.deepEqual(kept.work.list.map(w => [w.course, w.href, w.name, w.topic, w.score]), [
    ['Культурология', '/student/umkd/7002/2026/1', '', 'Прием СРС 1 «Символы культуры»', 22],
    ['История Казахстана', '/student/umkd/7005/2026/1', 'СРО 1', 'Контрольная работа. Письменно.', 15]]);
  assert.ok(!/СРОП|Консультация|Разъяснение/.test(JSON.stringify(kept.work)));
  const at = kept.work.at; apply(p, false); apply(p); await until(done(p));
  assert.equal(work(p).at, at, 'the same list is not written again');
  p.close();
  // Another week on screen, a week read on another day, or no account mark: nothing is kept.
  for (const [content, now, local] of [[schedule(lessons, 5), WEDNESDAY, mark], [schedule(lessons), [2026, 8, 28, 9, 0], mark], [schedule(lessons), WEDNESDAY, {}]]) {
    const other = page(content, { routes: routes(), local, now });
    apply(other); await until(done(other));
    assert.equal(work(other), null); other.close();
  }
  // A course whose syllabus could not be read, or the group's other weeks that could not be read: the list carries a
  // note on this page and is not kept for the home page.
  const broken = routes(); broken['/student/interactive/syllabus/print/9030/2'] = null;
  const failedWeeks = routes(); failedWeeks[weekHref(5)] = null;
  for (const map of [broken, failedWeeks]) {
    const other = page(schedule(lessons), { routes: map, local: mark, now: WEDNESDAY });
    apply(other); await until(() => done(other)() && !!other.d.querySelector('.univer-app-syl-note'));
    assert.equal(work(other), null); other.close();
  }
  // The browser extension keeps nothing.
  const extension = page(schedule(lessons), { routes: routes(), local: mark, now: WEDNESDAY, extension: true });
  apply(extension); await until(done(extension));
  assert.equal(work(extension), null); extension.close();
});

test('long topics are cut and expand on demand, and extra entries of one class wait behind a button', async () => {
  const map = routes();
  map['/student/interactive/syllabus/print/9004/2'] = syllabus({ groups: [group(MINE)], people: 'Учителева Анна Петровна', weeks: [week(4, [
    entry('лекция', 'лекция 4', 'Слово '.repeat(600), 1),
    entry('семинар', 'семинар 4', LONG, 7), entry('семинар', 'семинар 4', 'Вторая тема семинара', 3), entry('семинар', 'семинар 4', 'Третья тема семинара', 2)])] });
  const p = page(schedule(lessons), { routes: map });
  apply(p); await until(done(p));
  const limits = p.w.__univerSyllabusV1.limits, seminar = lessonOf(p, 'Политология', 'семинар');
  const topic = seminar.querySelector('.univer-app-syl-topic'), more = seminar.querySelector('.univer-app-syl-more');
  assert.ok(LONG.length > limits.preview && topic.textContent.length <= limits.preview + 1 && topic.textContent.endsWith('…'));
  assert.ok(topic.classList.contains('univer-app-syl-clamped') && !more.hidden);
  assert.equal(more.textContent, 'Показать полностью');
  click(p, more);
  assert.equal(topic.textContent, LONG.trim()); assert.equal(more.textContent, 'Свернуть'); assert.equal(more.getAttribute('aria-expanded'), 'true');
  assert.ok(!topic.classList.contains('univer-app-syl-clamped'));
  click(p, more); assert.ok(topic.textContent.endsWith('…'));
  // Three seminar rows for one seminar: two are shown, the third waits.
  assert.equal(topics(seminar).length, limits.perLesson);
  const rest = seminar.querySelector('.univer-app-syl-rest'); assert.equal(rest.textContent, 'Показать ещё 1');
  click(p, rest);
  assert.equal(topics(lessonOf(p, 'Политология', 'семинар')).length, 3);
  assert.equal(lessonOf(p, 'Политология', 'семинар').querySelector('.univer-app-syl-rest').textContent, 'Показать меньше');
  // A topic longer than the kept length says where the rest is.
  const lecture = lessonOf(p, 'Политология', 'лекция'); click(p, lecture.querySelector('.univer-app-syl-more'));
  assert.ok(lecture.querySelector('.univer-app-syl-topic').textContent.length <= limits.topic + 1);
  assert.equal(lecture.querySelector('.univer-app-syl-cut').hidden, false);
  // A short topic needs no button.
  assert.equal(lessonOf(p, 'Культурология', 'лекция').querySelector('.univer-app-syl-more').hidden, true);
  apply(p, false); p.close();
});

test('a task folds on its own button, the switch hides all of them, and both choices survive a reload', async () => {
  const p = page(schedule(lessons), { routes: routes() });
  apply(p); await until(done(p));
  let lecture = lessonOf(p, 'Политология', 'лекция'), opened = 0;
  p.d.querySelector('.univer-app-lesson-name').addEventListener('click', event => { opened++; event.preventDefault(); });
  let fold = lecture.querySelector('.univer-app-syl-fold');
  assert.equal(fold.getAttribute('aria-expanded'), 'true');
  click(p, fold);
  lecture = lessonOf(p, 'Политология', 'лекция'); fold = lecture.querySelector('.univer-app-syl-fold');
  assert.equal(fold.getAttribute('aria-expanded'), 'false'); assert.equal(lecture.querySelector('.univer-app-syl-body').hidden, true);
  assert.equal(p.d.getElementById(fold.getAttribute('aria-controls')), lecture.querySelector('.univer-app-syl-body'));
  // The seminar of the same course keeps its own state, and a click inside a task never opens the course materials.
  assert.equal(lessonOf(p, 'Политология', 'семинар').querySelector('.univer-app-syl-body').hidden, false);
  click(p, lessonOf(p, 'Политология', 'семинар').querySelector('.univer-app-syl-topic')); assert.equal(opened, 0);
  const toggle = p.d.querySelector('.univer-app-syl-switch');
  assert.equal(toggle.getAttribute('role'), 'switch'); assert.equal(toggle.getAttribute('aria-checked'), 'true');
  click(p, toggle);
  assert.equal(toggle.getAttribute('aria-checked'), 'false'); assert.equal(p.d.querySelector('.univer-app-schedule').dataset.syllabus, 'off');
  assert.ok(Array.from(p.d.querySelectorAll('.univer-app-syl-task')).every(block => block.hidden));
  // The week's list has its own button and stays when the tasks under classes are hidden.
  assert.equal(p.d.querySelector('.univer-app-syl-sro-body').hidden, false);
  click(p, p.d.querySelector('.univer-app-syl-panel .univer-app-syl-fold'));
  assert.equal(p.d.querySelector('.univer-app-syl-sro-body').hidden, true);
  const local = { [STORE]: p.w.localStorage.getItem(STORE), [PREFS]: p.w.localStorage.getItem(PREFS) };
  apply(p, false); p.close();

  const again = page(schedule(lessons, 5), { routes: routes(), local });
  apply(again); await until(done(again));
  assert.equal(again.d.querySelector('.univer-app-syl-switch').getAttribute('aria-checked'), 'false');
  assert.equal(again.d.querySelector('.univer-app-syl-sro-body').hidden, true);
  assert.equal(again.d.querySelector('.univer-app-syl-panel .univer-app-syl-fold').getAttribute('aria-expanded'), 'false');
  click(again, again.d.querySelector('.univer-app-syl-switch'));
  assert.equal(lessonOf(again, 'Политология', 'лекция').querySelector('.univer-app-syl-body').hidden, true);
  // The other week comes from the remembered plans: no course page or syllabus is read again.
  assert.deepEqual(topics(lessonOf(again, 'Политология', 'семинар')).map(t => t.slice(0, 22)), ['Подготовить письменный']);
  assert.deepEqual(again.requests.filter(address => /syllabus|\/umkd\/\d/.test(address)), []);
  apply(again, false); again.close();
});

test('another student signing in on the same profile does not get the previous student\'s programme, course list or weeks', async () => {
  const first = page(schedule(lessons, 4), { routes: routes(), local: { 'univer.app.account.v1': 'a1' } });
  apply(first); await until(done(first));
  const saved = first.w.localStorage.getItem(STORE); apply(first, false); first.close();
  assert.equal(JSON.parse(saved).owner, 'a1');
  const reads = p => p.requests.filter(address => /\/umkd\/\d|\/student\/bachelor\/|\/student\/umkd\/$/.test(address));
  // The same account: everything comes from the store.
  const same = page(schedule(lessons, 4), { routes: routes(), local: { [STORE]: saved, 'univer.app.account.v1': 'a1' } });
  apply(same); await until(done(same));
  assert.deepEqual(same.requests.filter(address => /\/student\/bachelor\//.test(address)), []);
  apply(same, false); same.close();
  // Another account: the programme is read again, the plans of courses (the same for everyone) are reused.
  const other = page(schedule(lessons, 4), { routes: routes(), local: { [STORE]: saved, 'univer.app.account.v1': 'b2' } });
  apply(other); await until(done(other));
  assert.ok(other.requests.some(address => /\/student\/bachelor\//.test(address)), 'the programme is read for the new account');
  assert.deepEqual(other.requests.filter(address => /syllabus\/print/.test(address)), [], 'course plans are reused');
  assert.equal(JSON.parse(other.w.localStorage.getItem(STORE)).owner, 'b2');
  apply(other, false); other.close();
});

test('a plan found once is kept for the semester: a day-old programme and a month-old plan cause no request, a changed programme reads them again', async () => {
  const first = page(schedule(lessons, 4), { routes: routes() });
  apply(first); await until(done(first));
  const saved = JSON.parse(first.w.localStorage.getItem(STORE)); apply(first, false); first.close();
  const day = 86400000, aged = (change) => { const copy = JSON.parse(JSON.stringify(saved)); change(copy); return { [STORE]: JSON.stringify(copy) }; };
  const reads = p => p.requests.filter(address => /syllabus|\/umkd\/\d|\/student\/bachelor\//.test(address));
  // A month on: the plans, the course list and the programme (a few hours old) are all taken from the store.
  const month = page(schedule(lessons, 4), { routes: routes(), local: aged(copy => {
    Object.values(copy.courses).forEach(course => { course.at -= course.status === 'ready' ? 30 * day : 3600000; }); copy.list.at -= 2 * day; copy.program.at -= 3600000; copy.pattern.at -= 3 * day;
  }) });
  apply(month); await until(done(month));
  assert.deepEqual(reads(month), []);
  assert.ok(topics(lessonOf(month, 'Политология', 'лекция')).length === 1);
  apply(month, false); month.close();
  // A remembered course list of another semester is not used for the semester on screen.
  const stale = page(schedule(lessons, 4), { routes: routes(), local: aged(copy => { copy.list.items.forEach(item => { item.href = item.href.replace('/2026/1', '/2025/2'); }); }) });
  apply(stale); await until(done(stale));
  assert.ok(JSON.parse(stale.w.localStorage.getItem(STORE)).list.items.every(item => /\/2026\/1$/.test(item.href)));
  apply(stale, false); stale.close();
  // The programme is reread once a day, and a plan written for another programme is not shown for this one.
  const other = page(schedule(lessons, 4), { routes: routes(), local: aged(copy => {
    copy.program.at -= 2 * day; Object.values(copy.courses).forEach(course => { course.code = '6B09999'; });
  }) });
  apply(other); await until(done(other));
  assert.ok(other.requests.includes('/student/bachelor/'));
  assert.ok(other.requests.some(address => /syllabus\/print/.test(address)));
  assert.equal(topics(lessonOf(other, 'Политология', 'лекция')).length, 1);
  apply(other, false); other.close();
});

test('Original removes the tasks, the panel and the style, and a schedule of another semester shows nothing', async () => {
  const p = page(schedule(lessons), { routes: routes() });
  apply(p); await until(done(p));
  assert.ok(p.d.querySelector('.univer-app-syl-task') && p.d.getElementById('univer-syllabus-style'));
  apply(p, false);
  assert.equal(p.d.querySelector('[class*=univer-app-syl]'), null); assert.equal(p.d.getElementById('univer-syllabus-style'), null);
  assert.equal(p.d.querySelector('[data-syllabus]'), null);
  p.close();
  const other = page(schedule(lessons, 4, '2025/2'), { routes: routes() });
  apply(other); await until(() => other.requests.includes('/student/umkd/') && other.d.querySelector('.univer-app-syl-panel')?.hidden === true);
  assert.equal(other.d.querySelector('.univer-app-syl-task'), null);
  assert.deepEqual(other.requests.filter(address => /syllabus\/print/.test(address)), []);
  apply(other, false); other.close();
});

test('a failed read says what is wrong and retries only on the user\'s button', async () => {
  const map = routes(); delete map['/student/bachelor/'];
  const p = page(schedule(lessons), { routes: map });
  apply(p); await until(() => p.d.querySelector('.univer-app-syl-alert')?.hidden === false);
  assert.match(p.d.querySelector('.univer-app-syl-alert').textContent, /Не удалось определить вашу образовательную программу/);
  assert.equal(p.d.querySelector('.univer-app-syl-task'), null);
  const before = p.requests.length; await new Promise(resolve => setTimeout(resolve, 30)); assert.equal(p.requests.length, before);
  map['/student/bachelor/'] = home(); delete map['/student/interactive/syllabus/print/9010/2'];
  click(p, p.d.querySelector('.univer-app-syl-retry')); await until(done(p));
  assert.equal(p.d.querySelector('.univer-app-syl-alert').hidden, true);
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'лекция')), ['Политическая власть и её источники']);
  // One syllabus could not be read: its course is named with a retry, the others are shown.
  const failed = Array.from(p.d.querySelectorAll('.univer-app-syl-note')).find(n => /Не удалось прочитать силлабус: Культурология/.test(n.textContent));
  assert.ok(failed); map['/student/interactive/syllabus/print/9010/2'] = culture;
  click(p, failed.querySelector('.univer-app-syl-note-retry')); await until(() => topics(lessonOf(p, 'Культурология', 'лекция')).length === 1);
  apply(p, false); p.close();
});

test('a double period names its first class instead of repeating the topic, and a dropped read is tried once more by itself', async () => {
  const map = routes(); delete map['/student/interactive/syllabus/print/9010/2'];
  const seminar = ['Политология', 'семинар', 'Учителева А. П.'];
  const p = page(schedule([[seminar, ['Культурология', 'лекция', 'Незнакомов Н. Н.']], [seminar], [null, seminar]]), { routes: map });
  p.w.__univerSyllabusV1.limits.again = 40;
  apply(p); await until(() => Array.from(p.d.querySelectorAll('.univer-app-syl-note')).some(n => /Не удалось прочитать силлабус: Культурология/.test(n.textContent)));
  const seminars = Array.from(p.d.querySelectorAll('.univer-app-lesson')).filter(card => card.querySelector('.univer-app-lesson-name').textContent === 'Политология');
  assert.equal(seminars.length, 3);
  // Monday's two seminars share one syllabus row; Tuesday's is its own class and shows the topic again.
  assert.deepEqual(topics(seminars[0]), ['Разбор кейса о разделении властей']);
  assert.equal(seminars[1].querySelector('.univer-app-syl-task').dataset.state, 'same');
  assert.equal(seminars[1].querySelector('.univer-app-syl-same').textContent, 'та же тема, что в 9:00'); assert.deepEqual(topics(seminars[1]), []);
  assert.deepEqual(topics(seminars[2]), ['Разбор кейса о разделении властей']);
  map['/student/interactive/syllabus/print/9010/2'] = culture;
  await until(() => topics(lessonOf(p, 'Культурология', 'лекция')).length === 1);
  assert.equal(p.requests.filter(address => address === '/student/interactive/syllabus/print/9010/2').length, 2);
  assert.ok(!Array.from(p.d.querySelectorAll('.univer-app-syl-note')).some(n => /Не удалось/.test(n.textContent)));
  apply(p, false); p.close();
});

test('the dark theme recolours the panel and the tasks with the rest of the page', async () => {
  const p = page(schedule(lessons), { routes: routes(), dark: true });
  apply(p); await until(done(p));
  assert.ok(p.d.documentElement.classList.contains('univer-app-dark'));
  const mapped = p.d.getElementById('univer-app-dark-app').textContent;
  const panel = mapped.match(/[^{}]*\.univer-app-syl-panel\s*\{[^}]*background[^}]*\}/), count = mapped.match(/[^{}]*\.univer-app-syl-count\s*\{[^}]*\}/);
  assert.ok(panel && count, 'the theme mapped the adapter stylesheet');
  // The light card colour is gone from the mapped rule.
  assert.doesNotMatch(panel[0], /#fff\b|#ffffff|rgb\(255, 255, 255\)|white/i);
  apply(p, false); p.close();
});

test('consultations are told from independent work as the observed syllabi write them, in three languages', () => {
  const p = page('', {}), api = p.w.__univerSyllabusV1;
  const cases = [
    ['СРО/СРОП 4', 'Консультация по выполнению СРС 1 «Комплексная защита информации»', 0, 'srop'],
    ['СРО/СРОП 5', 'Прием СРС 1 «Комплексная защита информации»', 22, 'sro'],
    ['СРО/СРОП 8', 'Контрольная работа', 20, 'sro'],
    ['лекция 15', 'Прием СРС 3 Средства обеспечения безопасности', 24, 'sro'],
    ['СРОП 1', 'СРОП 1. Консультация по выполнению СРО 1. Тема: эссе', 0, 'srop'],
    ['СРО 1', 'СРО 1. Тема: эссе', 20, 'sro'],
    ['СРОП 4', 'Консультация по выполнению СРО 3. Роль деятеля', 0, 'srop'],
    ['СРО 2', 'Коллоквиум. Устно.', 20, 'sro'],
    ['СРО/СРОП 2', 'БОӨЖ 1. БӨЖ 1 тапсыру бойынша кеңес беру', 0, 'srop'],
    ['СРО/СРОП 4', 'БOӨЖ 2: Grammar and vocabulary revision.', 0, 'srop'],
    ['СРО/СРОП 3', 'БӨЖ 1. Тақырыбы: эссе', 20, 'sro'],
    ['СРО/СРОП 6', 'ОБӨЖ 3. БӨЖ 2 тақырыбын орындау бойынша кеңес беру', 0, 'srop'],
    ['СРО/СРОП 3', 'Теориялық-танымдық тапсырмалар бойынша кеңес беру', 0, 'srop'],
    ['СРО/СРОП 4', '1. «Менің сүйікті жазушым» пікір алмасу', 15, 'sro'],
    ['IWS/IWST 2', 'IWST 1. Consultation on IWS 1', 0, 'srop'],
    ['IWS/IWST 3', 'IWS 1. Essay', 20, 'sro']];
  cases.forEach(c => assert.equal(api.sroKind(c[0], c[1], c[2]), c[3], c[0] + ' / ' + c[1]));
  assert.deepEqual(['лекция', 'дәріс', 'lecture', 'семинар', 'seminar', 'СРО/СРОП', 'БӨЖ/БМӨЖ', 'IWS/IWST', 'лабораторная', 'практика', 'экзамен'].map(api.kindOf),
    ['lecture', 'lecture', 'lecture', 'seminar', 'seminar', 'sro', 'sro', 'sro', 'lab', 'practice', 'other']);
  // The Kazakh and English printed forms keep the structure and change the labels.
  const doc = new p.w.DOMParser().parseFromString(syllabus({ groups: ['x'], weeks: [week(2, [entry('дәріс', 'дәріс 2', 'Тақырып', 1), entry('БӨЖ/БМӨЖ', 'БӨЖ/БМӨЖ 2', 'БӨЖ 1. Эссе', 10)])] })
    .replace('Академический год', 'Академиялық жыл').replace('<td class="label">Образовательная программа</td><td class="text">x;', '<td class="label">Білім беру бағдарламасы</td><td class="text">Топ № 1000001 (Білім беру бағдарламасы: ' + MINE + ' Синтетикалық, Семестр: 1, Тілдік бөлімі: орыс);'), 'text/html');
  const parsed = api.parseSyllabus(doc);
  assert.equal(parsed.year, '2026'); assert.equal(parsed.programs[0].code, MINE); assert.equal(parsed.programs[0].division, 'ru');
  assert.deepEqual(JSON.parse(JSON.stringify(parsed.entries.map(e => [e.week, e.kind, e.name, e.score]))), [[2, 'lecture', 'дәріс 2', 1], [2, 'sro', '', 10]]);
  assert.equal(api.rate(parsed, { code: MINE, name: '', division: 'ru' }, '2026', []), 13);
  assert.equal(api.rate(parsed, { code: OTHER, name: '', division: 'ru' }, '2026', []), -1);
  assert.equal(api.rate(parsed, { code: MINE, name: '', division: 'ru' }, '2025', []), -1);
  // A practical class reads the seminar rows only when the syllabus has no rows of its own kind.
  const plan = { weeks: { 2: [{ kind: 'seminar', topic: 'S' }, { kind: 'lecture', topic: 'L' }] }, kinds: { seminar: true, lecture: true } };
  assert.deepEqual(Array.from(api.tasksFor(plan, 2, 'practice'), e => e.topic), ['S']);
  assert.deepEqual(Array.from(api.tasksFor(plan, 2, 'lecture'), e => e.topic), ['L']);
  // The schedule leads: a class in a week the syllabus gives no row takes the row laid over it, and a week unknown to the timetable keeps its own.
  const placed = { classes: [3], rows: { 3: [2] } };
  assert.deepEqual(Array.from(api.tasksFor(plan, 3, 'seminar', placed), e => e.topic), ['S']);
  assert.deepEqual(Array.from(api.tasksFor(plan, 2, 'seminar', placed), e => e.topic), ['S']);
  assert.equal(api.tasksFor(plan, 3, 'seminar').length, 0); assert.equal(api.tasksFor(plan, 2, 'other').length, 0);
  const programDoc = new p.w.DOMParser().parseFromString(home('6B06301', 'казахское'), 'text/html');
  assert.deepEqual(JSON.parse(JSON.stringify(api.parseProgram(programDoc))), { code: '6B06301', name: 'синтетическая безопасность', division: 'kz' });
  p.close();
});

// The group's own timetable against a syllabus written for other weeks, as observed on 11 November 2026: the syllabus
// puts a course's seminars on even weeks and its work between them, the group has the seminars on odd weeks.
const shiftedSyllabus = syllabus({ groups: [group(MINE)], people: 'Учителева Анна Петровна', weeks: [
  week(2, [entry('лекция', 'лекция 2', 'Лекция второй недели', 1), entry('семинар', 'семинар 2', 'Первый семинар курса', 5)]),
  week(3, [entry('лекция', 'лекция 3', 'Лекция третьей недели', 1), entry('СРО/СРОП', 'СРОП 1', 'Консультация по выполнению СРО 1', 0)]),
  week(4, [entry('лекция', 'лекция 4', 'Лекция четвёртой недели', 1), entry('семинар', 'семинар 4', 'Второй семинар курса', 7), entry('СРО/СРОП', 'СРО 1', 'Глоссарий из двадцати терминов', 15, true)]),
  week(5, [entry('лекция', 'лекция 5', 'Лекция пятой недели', 1)])] });
const lectureClass = ['Политология', 'лекция', 'Учителева А. П.'], seminarClass = ['Политология', 'семинар', 'Учителева А. П.'];
const shiftedWeeks = [1, 2, 3, 4, 5, 6], shiftedRows = n => n === 3 || n === 5 ? [[lectureClass], [seminarClass]] : [[lectureClass]];
const shiftedSchedule = n => schedule(shiftedRows(n), n, '2026/1', shiftedWeeks);
function shiftedRoutes() {
  const map = routes(); map['/student/interactive/syllabus/print/9004/2'] = shiftedSyllabus;
  shiftedWeeks.forEach(n => { map[weekHref(n)] = site(shiftedSchedule(n)); });
  return map;
}
const entryNames = card => Array.from(card.querySelectorAll('.univer-app-syl-entry-name')).map(node => node.textContent);
const listed = p => Array.from(p.d.querySelectorAll('.univer-app-syl-item')).map(row => [row.querySelector('.univer-app-syl-course').textContent, row.querySelector('.univer-app-syl-meta').textContent, row.querySelector('.univer-app-syl-topic').textContent]);
const noteTexts = p => Array.from(p.d.querySelectorAll('.univer-app-syl-note')).map(node => node.textContent);

test('a seminar held on other weeks than the syllabus says takes its topic by the order of the group\'s classes, with the work beside it', async () => {
  const p = page(shiftedSchedule(5), { routes: shiftedRoutes() });
  apply(p); await until(done(p));
  // Week 5: the syllabus has a lecture only and the group has its second seminar, so the second seminar row is shown
  // with the work the syllabus sets beside that seminar.
  const held = lessonOf(p, 'Политология', 'семинар');
  assert.deepEqual(topics(held), ['Второй семинар курса', 'Глоссарий из двадцати терминов']);
  assert.deepEqual(entryNames(held), ['семинар 4 · макс. балл: 7', 'СРО 1 · макс. балл: 15']);
  // The lecture is held every week in both, so it keeps its own week.
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'лекция')), ['Лекция пятой недели']);
  assert.equal(lessonOf(p, 'Политология', 'лекция').querySelector('.univer-app-syl-meta').textContent, 'лекция 5 · макс. балл: 1');
  // The work is listed in the week of that seminar, and the consultation stays out.
  assert.deepEqual(listed(p), [['Политология', 'СРО 1 · макс. балл: 15', 'Глоссарий из двадцати терминов']]);
  assert.deepEqual(noteTexts(p), []);
  assert.ok(!/Консультация|СРОП/.test(p.d.querySelector('.univer-app-schedule').textContent));
  // Every week of the site's list is read once.
  assert.deepEqual(p.requests.filter(address => /\/myschedule\//.test(address)).sort(), shiftedWeeks.map(n => weekHref(n)).sort());
  assert.deepEqual(p.errors, []);
  const local = { [STORE]: p.w.localStorage.getItem(STORE) };
  apply(p, false); p.close();

  // Week 4 from what is remembered: the group has no seminar, so the seminar row and its work are not shown here.
  const fourth = page(shiftedSchedule(4), { routes: shiftedRoutes(), local });
  apply(fourth); await until(done(fourth));
  assert.deepEqual(topics(lessonOf(fourth, 'Политология', 'лекция')), ['Лекция четвёртой недели']);
  assert.deepEqual(listed(fourth), [['История Казахстана', 'СРО 1 · макс. балл: 15', 'Контрольная работа. Письменно.'], ['Культурология', 'макс. балл: 22', 'Прием СРС 1 «Символы культуры»']]);
  assert.deepEqual(fourth.requests.filter(address => /myschedule|syllabus|\/umkd\/\d/.test(address)), []);
  apply(fourth, false); fourth.close();

  // Week 3: the group's first seminar takes the first seminar row, written for week 2.
  const third = page(shiftedSchedule(3), { routes: shiftedRoutes(), local });
  apply(third); await until(done(third));
  assert.deepEqual(topics(lessonOf(third, 'Политология', 'семинар')), ['Первый семинар курса']);
  assert.equal(lessonOf(third, 'Политология', 'семинар').querySelector('.univer-app-syl-meta').textContent, 'семинар 2 · макс. балл: 5');
  assert.deepEqual(listed(third), []);
  apply(third, false); third.close();
});

test('work is not moved into a week that has work of its own: two works of one course never meet beside one seminar', async () => {
  // Observed: work set for weeks 10 and 11 with seminars in weeks 9 and 11 put both under the seminar of week 11.
  const map = shiftedRoutes();
  map['/student/interactive/syllabus/print/9004/2'] = syllabus({ groups: [group(MINE)], people: 'Учителева Анна Петровна', weeks: [
    week(2, [entry('семинар', 'семинар 2', 'Первый семинар курса', 5)]),
    week(4, [entry('семинар', 'семинар 4', 'Второй семинар курса', 7), entry('СРО/СРОП', 'СРО 1', 'Глоссарий из двадцати терминов', 15)]),
    week(5, [entry('лекция', 'лекция 5', 'Лекция пятой недели', 1), entry('СРО/СРОП', 'СРО 2', 'Доклад пятой недели', 10)])] });
  const p = page(shiftedSchedule(5), { routes: map });
  apply(p); await until(done(p));
  assert.deepEqual(listed(p), [['Политология', 'СРО 2 · макс. балл: 10', 'Доклад пятой недели']]);
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'семинар')), ['Второй семинар курса', 'Доклад пятой недели']);
  const local = { [STORE]: p.w.localStorage.getItem(STORE) };
  apply(p, false); p.close();
  // The work of week 4 goes to the free seminar of week 3 instead.
  const third = page(shiftedSchedule(3), { routes: map, local });
  apply(third); await until(done(third));
  assert.deepEqual(listed(third), [['Политология', 'СРО 1 · макс. балл: 15', 'Глоссарий из двадцати терминов']]);
  apply(third, false); third.close();
});

test('when another week of the schedule cannot be read the topics stay on the syllabus weeks until a retry reads it', async () => {
  const map = shiftedRoutes(); map[weekHref(2)] = null;
  const p = page(shiftedSchedule(5), { routes: map });
  apply(p); await until(done(p));
  // Half a timetable would move the topics to wrong classes, so nothing is moved.
  assert.equal(lessonOf(p, 'Политология', 'семинар').querySelector('.univer-app-syl-task'), null);
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'лекция')), ['Лекция пятой недели']);
  assert.deepEqual(listed(p), []);
  const failed = Array.from(p.d.querySelectorAll('.univer-app-syl-note')).find(node => /^Не удалось прочитать расписание других недель, темы показаны по неделям силлабуса/.test(node.textContent));
  assert.ok(failed); assert.equal(JSON.parse(p.w.localStorage.getItem(STORE)).pattern, undefined);
  map[weekHref(2)] = site(shiftedSchedule(2));
  click(p, failed.querySelector('.univer-app-syl-note-retry')); await until(() => topics(lessonOf(p, 'Политология', 'семинар')).length === 2);
  assert.ok(!noteTexts(p).some(note => /Не удалось/.test(note)));
  apply(p, false); p.close();
});

// Observed on 12 November 2026 (Политология): seminars written for weeks 4, 6, 8 and held in weeks 5, 7, 9, with a lecture every week.
// The group's week 7 has the seminar of week 8, and its lecture stays the lecture of week 7.
const aheadSyllabus = syllabus({ groups: [group(MINE)], people: 'Учителева Анна Петровна', weeks: [4, 5, 6, 7, 8, 9, 10].map(n =>
  week(n, [entry('лекция', 'лекция ' + n, 'Лекция недели ' + n, 1)].concat(n % 2 ? [] : [entry('семинар', 'семинар ' + n, 'Семинар недели ' + n, 20)]))) });
const aheadWeeks = [3, 4, 5, 6, 7, 8, 9, 10], aheadRows = n => n % 2 && n > 4 && n < 10 ? [[lectureClass], [seminarClass]] : [[lectureClass]];
function aheadRoutes() {
  const map = routes(); map['/student/interactive/syllabus/print/9004/2'] = aheadSyllabus;
  aheadWeeks.forEach(n => { map[weekHref(n)] = site(schedule(aheadRows(n), n, '2026/1', aheadWeeks)); });
  return map;
}
test('a syllabus that puts seminars on the other parity than the group holds them is read one week ahead for seminars only', async () => {
  const p = page(schedule(aheadRows(7), 7, '2026/1', aheadWeeks), { routes: aheadRoutes() });
  apply(p); await until(done(p));
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'лекция')), ['Лекция недели 7']);
  assert.deepEqual(topics(lessonOf(p, 'Политология', 'семинар')), ['Семинар недели 8']);
  apply(p, false); p.close();
  // The last class takes the last row, and the lecture still its own week.
  const last = page(schedule(aheadRows(9), 9, '2026/1', aheadWeeks), { routes: aheadRoutes() });
  apply(last); await until(done(last));
  assert.deepEqual(topics(lessonOf(last, 'Политология', 'лекция')), ['Лекция недели 9']);
  assert.deepEqual(topics(lessonOf(last, 'Политология', 'семинар')), ['Семинар недели 10']);
  apply(last, false); last.close();
});

test('the one-week lead is decided for each kind of class and only when rows stand next to the classes', () => {
  const p = page('', {}), api = p.w.__univerSyllabusV1;
  const plan = rows => ({ weeks: rows, kinds: { seminar: true, lecture: true, lab: true } });
  const row = (kind, weeks) => weeks.reduce((all, n) => { (all[n] = all[n] || []).push({ kind, week: n }); return all; }, {});
  const seminars = plan(row('seminar', [4, 6, 8, 10, 12]));
  assert.equal(api.leadOf(seminars, [5, 7, 9, 11, 13], 'seminar'), 1);
  // Lectures, same parity, a different pace, and rows far from the classes keep the order rule.
  assert.equal(api.leadOf(seminars, [5, 7, 9, 11, 13], 'lecture'), 0);
  assert.equal(api.leadOf(seminars, [4, 6, 8, 10, 12], 'seminar'), 0);
  assert.equal(api.leadOf(seminars, [1, 2, 3, 4, 5], 'seminar'), 0);
  assert.equal(api.leadOf(plan(row('seminar', [10, 12, 14])), [3, 5, 7], 'seminar'), 0);
  // A lab with rows of its own is not moved because the seminars are.
  const both = plan([4, 6, 8].reduce((all, n) => { all[n] = [{ kind: 'seminar', week: n }, { kind: 'lab', week: n }]; return all; }, {}));
  assert.equal(api.leadOf(both, [5, 7, 9], 'seminar'), 1); assert.equal(api.leadOf(both, [4, 6, 8], 'lab'), 0);
  p.close();
});

test('syllabus weeks are laid over the group\'s class weeks in order, as near to their own weeks as the order allows', () => {
  const p = page('', {}), api = p.w.__univerSyllabusV1, align = (rows, classes) => JSON.parse(JSON.stringify(api.align(rows, classes)));
  const odd = [5, 7, 9, 11, 13], even = [4, 6, 8, 10, 12], all = Array.from({ length: 15 }, (_, i) => i + 1), everyOther = [1, 3, 5, 7, 9, 11, 13, 15];
  // Observed: seminars written for even weeks and held on odd ones, and the reverse.
  assert.deepEqual(align(even, odd), { 5: [4], 7: [6], 9: [8], 11: [10], 13: [12] });
  assert.deepEqual(align(odd, even), { 4: [5], 6: [7], 8: [9], 10: [11], 12: [13] });
  // Observed: five seminars bunched in the syllabus and five class weeks spread evenly.
  assert.deepEqual(align([7, 8, 13, 14, 15], even), { 4: [7], 6: [8], 8: [13], 10: [14], 12: [15] });
  // Every syllabus week has its class: nothing moves, and class weeks without rows stay empty.
  assert.deepEqual(align(all, all)[9], [9]);
  assert.deepEqual(align(even, all), { 4: [4], 6: [6], 8: [8], 10: [10], 12: [12] });
  // More class weeks than rows: the rows keep their order and take the nearest weeks, the later one on a tie.
  assert.deepEqual(align(even, everyOther), { 5: [4], 7: [6], 9: [8], 11: [10], 13: [12] });
  // More rows than class weeks (observed: six seminars for five classes): neighbours share a class and none is lost.
  assert.deepEqual(align([5, 6, 7, 8, 9, 10], odd), { 5: [5], 7: [6, 7], 9: [8], 11: [9], 13: [10] });
  const halved = align(all, everyOther);
  assert.deepEqual(Object.values(halved).flat(), all); assert.ok(Object.values(halved).every(rows => rows.length >= 1 && rows.length <= 2));
  assert.deepEqual(align(even, []), {}); assert.deepEqual(align([], odd), {});
  // A week's page: classes by course and kind, a hidden subgroup left out; a page without the week list is not a schedule.
  const weekDoc = new p.w.DOMParser().parseFromString(site(schedule([[lectureClass, ['Культурология', 'семинар', 'Незнакомов Н. Н.']], [seminarClass]])).replace('<div class="groups"><div><p class="teacher">Культурология', '<div class="groups"><div style="display:none"><p class="teacher">Культурология'), 'text/html');
  assert.deepEqual(JSON.parse(JSON.stringify(api.parseWeek(weekDoc))), [{ key: 'политология', kind: 'lecture' }, { key: 'политология', kind: 'seminar' }]);
  assert.throws(() => api.parseWeek(new p.w.DOMParser().parseFromString(site('<p>Ознакомьтесь с уведомлением</p>'), 'text/html')));
  p.close();
});
