const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const jsdomUtils = require('jsdom/lib/jsdom/living/generated/utils');
// The student's own click: jsdom marks every scripted event untrusted, so the test dispatches one marked trusted.
function trustedClick(w, node) {
  const event = new w.MouseEvent('click', { bubbles: true, cancelable: true });
  const impl = jsdomUtils.implForWrapper(event); impl.isTrusted = true;
  jsdomUtils.implForWrapper(node)._dispatch(impl);
}
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const scripts = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'study-pages.js', 'course-tasks.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'univer-layout.js', 'desktop.js'].map(read);

// Study pages rebuilt from the observed structure (review captures of 8-9 and 29 October 2026) with synthetic values.
const row = (cell, kind = 'mid') => `<tr class="${kind}"><td class="lt"></td>${cell}<td class="rt"></td></tr>`;
const panel = rows => `<table class="mt">${rows}<tr class="bot"><td class="lt"></td><td class="ct"></td><td class="rt"></td></tr></table>`;
const shell = panels => '<table class="inner"><tr><td class="left"></td><td class="center">' +
  panel(row('<td class="ct message"><li>Общий GPA = 3.12</li></td>')) + panels + '</td><td class="right"></td></tr></table>';
const note = '<td class="ct" id="note"><b><font color="red">Примечание:</font></b><br><br><b><font color="red">* - оценка итогового контроля (экзамена) может быть аннулирована</font></b></td>';
const title = row('<td class="ct">Аттестация студента. Осенний Семестр, 2026-2027 уч. год (2 Курс, 3 Семестр)</td>', 'top') +
  row('<td class="ts"><a class="lt" href="/student/attestation/2025/2/"><img src="/img/tb_left.png">Перейти к 2025-2026 году Весенний семестр</a></td>');
// The October capture: six columns, no total row (cards).
const attestation = shell(panel(title +
  row('<td class="tt"><table class="inner" id="grades"><tr><th>Дисциплина</th><th>Кред.</th><th>Сумма</th><th>GPA</th><th>Букв.</th><th>Значение</th></tr>' +
    '<tr><td>Алгоритмы</td><td>5</td><td>94</td><td>4</td><td id="letter-a">A</td><td>Отлично</td></tr>' +
    '<tr><td>Дискретная математика</td><td>5</td><td>86</td><td>3.33</td><td>B+</td><td>Хорошо</td></tr>' +
    '<tr id="failed"><td>Психология</td><td>3</td><td>41</td><td>0</td><td>FX</td><td>Неудовлетворительно</td></tr></table></td>') +
  row(note)));
// The live page: nine columns and a GPA/credits total row across several columns (a matrix).
const liveAttestation = shell(panel(title +
  row('<td class="tt"><table class="inner" id="grades"><tr><th width="30%">Дисциплина</th><th>Кред.</th><th>РК1</th><th>РК2</th><th>ПА*</th><th>Сумма</th><th>GPA</th><th>Букв.</th><th>Значение</th></tr>' +
    '<tr><td>Иностранный язык</td><td>5</td><td>99</td><td>0</td><td>0</td><td>30</td><td>0</td><td>F</td><td>неуд.</td></tr>' +
    '<tr><td>Алгоритмы</td><td>5</td><td>98</td><td>96</td><td>92</td><td>94</td><td>4</td><td>A</td><td>отл.</td></tr>' +
    '<tr id="total"><td>GPA(2026-2027 уч. год)- 0</td><td colspan="8">Освоено кредитов (2026-2027 уч. год) - 0</td></tr></table></td>') +
  row(note)));
const names = ['Без привязки к дисциплине (0)', 'Введение в информационную безопасность (2)', 'Иностранный язык (0)', 'История Казахстана (1)',
  'Казахский (русский) язык (0)', 'Культурология (0)', 'Политология (0)', 'Обсуждения других пользователей'];
// As on the live page: two blocks of files, "Назад" without an address in one span with the heading link.
const discussion = (back = '<a>Назад</a>') => shell(
  panel(row('<td class="ct">Инструкция</td>', 'top') + row('<td class="ct" id="docs"><div><div><a href="/Content/instructions/1.pdf"><img src="/img/pdf.png"> Студенттерге арналған нұсқаулық</a><br>' +
    '<a href="/Content/instructions/2.pdf"><img src="/img/pdf.png"> Жазбаша емтихан нұсқаулығы.pdf</a></div><div><a href="/Content/instructions/3.pdf"><img src="/img/pdf.png"> Дистанционные курсы Инструкция для студентов</a></div></div></td>')) +
  panel(row(`<td class="ct" id="crumb"><span id="crumb-row">${back} / <a href="/student/discussion/index/">Список дисциплин 2026-2027 год Осенний Семестр</a></span><span><b></b></span></td>`, 'top') +
    row('<td class="ts"><a class="lt" id="subjectShow_btn" href="#"><img src="/img/door-in-icon.png">Открыть дисциплину</a></td>') +
    row('<td class="ct" id="period"><b>2026-2027 год Осенний Семестр</b></td>') +
    row('<td class="tt"><table class="inner" id="courses"><tr><th width="10%"></th><th>Дисциплина</th></tr>' +
      names.map((name, i) => `<tr class="link" id="${i === names.length - 1 ? 'other' : 7100 + i}"><td><img src="/img/folder.png"></td><td>${name}</td></tr>`).join('') + '</table></td>')));
const memo = '<td class="ct" id="memo"><b><font color="red">ВНИМАНИЕ!</font><br>ПАМЯТКА ПО ПОВТОРНОМУ ИЗУЧЕНИЮ ДИСЦИПЛИН</b><br>' +
  Array.from({ length: 8 }, (_, i) => `${i + 1}. Синтетический шаг памятки: заявка, рассмотрение в деканате, договор, оплата и проверка статуса.`).join('<br>') + '</td>';
const fail = shell(panel(row('<td class="ct">Информация</td>', 'top') + row(memo)) + panel(row('<td class="ct">Список задолженностей студента.</td>', 'top') +
  row('<td class="tt"><table class="inner"><tr><th colspan="3" id="caption">Мои заявки на повторное изучение</th></tr><tr><th>Дисциплина</th><th>Кредиты</th><th>Статус</th></tr><tr><td colspan="3">Нет данных</td></tr></table></td>')));
// The live exam schedule: instruction files next to red proctoring lines in one block.
const exam = shell(panel(row('<td class="ct">Инструкция</td>', 'top') + row('<td class="ct" id="exam-docs"><div><a href="/Content/instructions/DistanceLearnExam/1.pdf"><img src="/img/pdf.png"> Жазбаша емтихан.pdf</a><br><br>' +
  '<a href="/Content/instructions/DistanceLearnExam/2.pdf"><img src="/img/pdf.png">Инструкция для студентов (онлайн письменный экзамен)</a><br><br>' +
  '<center><font color="red">Начните экзамен через кнопку экстеншна Farabi Proctoring, если вы еще не установили нажмите на </font><a id="install" href="https://example.org/extension">ссылка для установки</a></center></div><div></div></td>')));

function page(content, url, before) {
  const html = '<table class="wrappertable"><tr id="l1"><td class="header">КазНУ</td></tr><tr id="l4"><td>' + content + '</td></tr></table>';
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const w = dom.window;
  if (before) before(w);
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.__univerNative = () => {};
  scripts.forEach(script => w.eval(script));
  return { w, d: w.document, close: () => w.close() };
}
const apply = (p, on = true, language = 'ru') => p.w.__univerDesktop.apply(on, false, language);
const type = (p, input, value) => { input.value = value; input.dispatchEvent(new p.w.Event('input')); };

test('attestation grades get badges and aligned roles without tinting rows, a calm note, and Original restores the source nodes', () => {
  const p = page(attestation, 'https://univer.kaznu.kz/student/attestation/');
  const letter = p.d.getElementById('letter-a'), text = letter.firstChild, noteCell = p.d.getElementById('note'), noteNodes = Array.from(noteCell.childNodes);
  apply(p); apply(p);
  const table = p.d.getElementById('grades');
  assert.ok(table.classList.contains('univer-app-grades'));
  assert.equal(p.d.querySelectorAll('.univer-app-grade').length, 3, 'one badge per course, also after a second pass');
  assert.equal(letter.querySelector('.univer-app-grade').dataset.band, 'a');
  assert.equal(letter.querySelector('.univer-app-grade').firstChild, text, 'the badge holds the original text node');
  assert.equal(p.d.querySelector('#failed .univer-app-grade').dataset.band, 'f');
  assert.equal(p.d.getElementById('failed').className.includes('risk'), false, 'an F only colours its badge; a mid-semester F is interim');
  assert.deepEqual(Array.from(table.rows[1].cells).map(c => c.dataset.univerRole), ['name', 'credit', 'score', 'gpa', 'letter', 'word']);
  assert.ok(noteCell.classList.contains('univer-app-callout'));
  assert.ok(p.d.getElementById('univer-pages-style'));
  apply(p, false);
  assert.equal(letter.firstChild, text); assert.equal(letter.childNodes.length, 1);
  assert.equal(p.d.querySelectorAll('[data-univer-role],.univer-app-grades,.univer-app-callout').length, 0);
  assert.deepEqual(Array.from(noteCell.childNodes), noteNodes);
  assert.equal(p.d.getElementById('univer-pages-style'), null);
  p.close();
});

test('the live attestation matrix with its total row gets badges and column roles, and the total row stays untouched', () => {
  const p = page(liveAttestation, 'https://univer.kaznu.kz/student/attestation/');
  apply(p);
  const table = p.d.getElementById('grades');
  assert.ok(table.classList.contains('univer-app-matrix'), 'the adapter keeps it a matrix');
  assert.ok(table.classList.contains('univer-app-grades'));
  assert.deepEqual(Array.from(p.d.querySelectorAll('.univer-app-grade')).map(b => b.dataset.band), ['f', 'a']);
  assert.deepEqual(Array.from(table.rows[0].cells).map(c => c.dataset.univerRole || ''), ['name', 'credit', 'mark', 'mark', 'mark', 'score', 'gpa', 'letter', 'word']);
  assert.equal(p.d.getElementById('total').querySelector('[data-univer-role],.univer-app-grade'), null);
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-grade,[data-univer-role]').length, 0);
  p.close();
});

test('distance courses: document cards, course badges, an Open button using the site handler and a list search', () => {
  const p = page(discussion(), 'https://univer.kaznu.kz/student/discussion/index/');
  const courses = p.d.getElementById('courses'), second = courses.rows[2], nameCell = second.cells[1], original = nameCell.firstChild;
  let opened = null; courses.addEventListener('dblclick', event => { opened = event.target.closest('tr').id; });
  apply(p); apply(p);
  assert.ok(p.d.getElementById('docs').classList.contains('univer-app-doc-links'), 'a cell of files only is a grid');
  assert.equal(p.d.querySelectorAll('#docs a.univer-app-doc-link').length, 3);
  assert.equal(p.d.querySelector('.univer-app-back-link'), null, 'a back link without any action is not turned into a dead button');
  assert.ok(p.d.getElementById('period').classList.contains('univer-app-period'));
  assert.ok(courses.classList.contains('univer-app-forum-courses'));
  const buttons = courses.querySelectorAll('.univer-app-course-open');
  assert.equal(buttons.length, names.length, 'one Open button per course row after repeated passes');
  assert.equal(nameCell.querySelector('.univer-app-count').textContent, '2');
  assert.equal(nameCell.querySelector('.univer-app-count').dataset.zero, 'false');
  assert.equal(courses.rows[1].querySelector('.univer-app-count').dataset.zero, 'true');
  assert.equal(p.d.getElementById('other').querySelector('.univer-app-count'), null, 'a name without a count stays as it is');
  assert.equal(nameCell.querySelector('.univer-app-course-open').getAttribute('aria-label'), 'Открыть дисциплину: Введение в информационную безопасность (2)');
  nameCell.querySelector('.univer-app-course-open').click();
  assert.equal(opened, second.id, 'Open runs the double-click handler of that row');
  const filters = p.d.querySelectorAll('.univer-app-list-filter input');
  assert.equal(filters.length, 1);
  const input = filters[0];
  type(p, input, 'истор');
  assert.deepEqual(Array.from(courses.rows).slice(1).filter(r => !r.hidden).map(r => r.id), ['7103']);
  type(p, input, 'открыть');
  assert.ok(!p.d.querySelector('.univer-app-list-empty').hidden, 'the button caption is not searched');
  type(p, input, '');
  assert.equal(Array.from(courses.rows).filter(r => r.hidden).length, 0);
  apply(p, true, 'en');
  assert.equal(nameCell.querySelector('.univer-app-course-open').textContent, 'Open');
  assert.equal(input.placeholder, 'Find a course');
  type(p, input, 'полит');
  apply(p, false);
  assert.equal(nameCell.childNodes.length, 1); assert.equal(nameCell.firstChild, original);
  assert.equal(original.data, 'Введение в информационную безопасность (2)');
  assert.equal(p.d.querySelectorAll('.univer-app-course-open,.univer-app-list-filter,.univer-app-count').length, 0);
  assert.equal(Array.from(courses.rows).filter(r => r.hidden).length, 0, 'search no longer hides rows in Original');
  assert.equal(p.d.querySelectorAll('.univer-app-doc-links,.univer-app-doc-link,.univer-app-period,.univer-app-forum-courses').length, 0);
  p.close();
});

test('a back link that does something becomes a button above the heading in its shared span', () => {
  for (const back of ['<a href="/student/discussion/index/">Назад</a>', '<a onclick="history.back()">Назад</a>']) {
    const p = page(discussion(back), 'https://univer.kaznu.kz/student/discussion/index/');
    apply(p);
    assert.ok(p.d.getElementById('crumb').classList.contains('univer-app-back-crumb'), back);
    assert.ok(p.d.getElementById('crumb-row').classList.contains('univer-app-back-row'));
    assert.ok(p.d.querySelector('#crumb-row>a').classList.contains('univer-app-back-link'));
    apply(p, false);
    assert.equal(p.d.querySelectorAll('.univer-app-back-crumb,.univer-app-back-row,.univer-app-back-link').length, 0);
    p.close();
  }
});

test('the long debts memo opens folded with a toggle, and an empty table keeps its own heading', () => {
  const p = page(fail, 'https://univer.kaznu.kz/student/fail/');
  const cell = p.d.getElementById('memo'), nodes = Array.from(cell.childNodes);
  apply(p); apply(p);
  assert.ok(cell.classList.contains('univer-app-memo'));
  assert.equal(cell.dataset.univerFolded, 'true');
  const toggle = cell.querySelectorAll('.univer-app-memo-toggle');
  assert.equal(toggle.length, 1, 'one toggle after repeated passes');
  assert.equal(toggle[0].textContent, 'Показать полностью'); assert.equal(toggle[0].getAttribute('aria-expanded'), 'false');
  toggle[0].click();
  assert.equal(cell.dataset.univerFolded, 'false'); assert.equal(toggle[0].textContent, 'Свернуть'); assert.equal(toggle[0].getAttribute('aria-expanded'), 'true');
  const caption = p.d.getElementById('caption').parentElement;
  assert.ok(caption.classList.contains('univer-app-table-caption'));
  assert.match(p.d.getElementById('univer-pages-style').textContent, /tr\.univer-app-table-caption \{ display:table-row!important; \}/);
  apply(p, false);
  assert.deepEqual(Array.from(cell.childNodes), nodes);
  assert.equal(cell.dataset.univerFolded, undefined);
  assert.ok(!caption.classList.contains('univer-app-table-caption'));
  p.close();
});

test('exam instruction files next to other lines become single cards; the proctoring line stays as it is', () => {
  const p = page(exam, 'https://univer.kaznu.kz/student/myexam/schedule/');
  apply(p);
  const cell = p.d.getElementById('exam-docs');
  assert.ok(cell.classList.contains('univer-app-doc-cell'));
  assert.ok(!cell.classList.contains('univer-app-doc-links'));
  assert.equal(cell.querySelectorAll('a.univer-app-doc-link').length, 2);
  assert.ok(!p.d.getElementById('install').classList.contains('univer-app-doc-link'));
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-doc-cell,.univer-app-doc-link').length, 0);
  p.close();
});

test('UMKD course list of six or more courses gets the same search, which ignores the Open captions', () => {
  const courses = ['Алгоритмы', 'История Казахстана', 'Политология', 'Социология', 'Психология', 'Физическая культура'];
  const umkd = shell(panel(row('<td class="ct">Просмотр УМКД по дисциплине</td>', 'top') + row('<td class="tt"><table class="inner" id="umkd"><tr><th>№</th><th>Дисциплина</th><th>Тип дисциплины</th></tr>' +
    courses.map((name, i) => `<tr class="link" id="${8100 + i}"><td>${i + 1}</td><td>${name}</td><td>Обязательная</td></tr>`).join('') + '</table></td>')));
  const p = page(umkd, 'https://univer.kaznu.kz/student/umkd/');
  apply(p);
  const table = p.d.getElementById('umkd');
  assert.ok(table.classList.contains('univer-app-courses'));
  assert.equal(table.querySelectorAll('.univer-app-course-open').length, courses.length, 'UMKD keeps its own Open buttons');
  const input = p.d.querySelector('.univer-app-list-filter input');
  assert.ok(input, 'a search field over the UMKD list');
  type(p, input, 'истор');
  assert.deepEqual(Array.from(table.rows).slice(1).filter(r => !r.hidden).map(r => r.id), ['8101']);
  type(p, input, 'обязательн');
  assert.equal(Array.from(table.rows).slice(1).filter(r => !r.hidden).length, 0, 'only the course name is searched');
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-list-filter').length, 0);
  assert.equal(Array.from(table.rows).filter(r => r.hidden).length, 0);
  p.close();
});

test('the Open button and course badges are only added where rows match the observed course list', () => {
  const p = page(discussion().replace(/class="link" /g, ''), 'https://univer.kaznu.kz/student/discussion/index/');
  apply(p);
  assert.equal(p.d.querySelectorAll('.univer-app-course-open,.univer-app-count').length, 0);
  assert.ok(p.d.getElementById('docs').classList.contains('univer-app-doc-links'), 'page styling still applies');
  apply(p, false); p.close();
});

// A course's UMKD page (captured 6 November 2026): headed sections of file rows. The teacher's links are synthetic.
const fileRow = (id, name, description) => `<tr class="file" id="${id}"><td><img src="/img/file.png"></td><td><a class="downLoad" href="/student/umkd/get/${id}/321/2026/1">${name}</a></td><td class="description">${description}</td><td>1</td><td class="description">34,68 Кб</td><td class="description">21.09.2026</td><td class="description"><div class="stars"></div></td></tr>`;
const umkdCourse = shell(panel(row('<td class="ct"><a href="/student/umkd/0/2026/1">УМКД</a> \\ <a href="/student/umkd/321/2026/1">Социология</a></td>', 'top') +
  row('<td class="tt"><div class="inner"><table class="inner"><tr><th></th><th>Название файла</th><th>Описание</th><th>Язык. гр.</th><th>Размер</th><th>Дата загрузки</th><th>Оцените качество материала</th></tr>' +
    '<tr><th colspan="7">Конспекты лекций</th></tr>' + fileRow(9002, 'Лекция 1.pdf', '<a id="outside" href="https://example.org/lecture">Видеозапись</a> и <a id="inside" href="/student/umkd/0/2026/1">список</a>') +
    fileRow(9003, 'Лекция 2.pdf', 'Источники: https://example.org/book, затем http://example.net/Метод_(наука)).') +
    fileRow(9004, 'Лекция 3.pdf', '<a id="mail" href="mailto:teacher@example.org">Почта</a> <a id="user" href="https://user:secret@example.org/">С паролем</a>') + '</table></div></td>')));

test('UMKD marks a teacher\'s outside links, links written addresses, and Original puts the text back', () => {
  const p = page(umkdCourse, 'https://univer.kaznu.kz/student/umkd/321/2026/1');
  const cell = p.d.getElementById('9003').cells[2], source = cell.firstChild;
  apply(p); apply(p);
  const links = Array.from(p.d.querySelectorAll('.univer-app-outside-link'));
  assert.deepEqual(links.map(a => a.getAttribute('href')), ['https://example.org/lecture', 'https://example.org/book', encodeURI('http://example.net/Метод_(наука)')], 'only web addresses of other sites, once each after repeated passes');
  assert.equal(links[0].title, 'Ссылка на другой сайт: example.org');
  assert.equal(cell.textContent, 'Источники: https://example.org/book, затем http://example.net/Метод_(наука)).', 'the written text is unchanged');
  assert.equal(links[1].textContent, 'https://example.org/book', 'trailing punctuation stays outside the link');
  for (const id of ['inside', 'mail', 'user']) assert.ok(!p.d.getElementById(id).classList.contains('univer-app-outside-link'), id + ' is not marked');
  assert.ok(!p.d.querySelector('a.downLoad.univer-app-outside-link'), 'file links stay downloads');
  apply(p, true, 'en');
  assert.equal(links[0].title, 'Link to another site: example.org');
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-outside-link').length, 0);
  assert.equal(cell.childNodes.length, 1); assert.equal(cell.firstChild, source, 'the original text node is whole again');
  assert.equal(cell.textContent, 'Источники: https://example.org/book, затем http://example.net/Метод_(наука)).');
  assert.ok(!p.d.getElementById('outside').hasAttribute('title') && !p.d.getElementById('outside').hasAttribute('class'));
  p.close();
});

const linkRow = (id, name) => fileRow(id, name, '').replace('34,68 Кб', '0 б');
const umkdSections = shell(panel(row('<td class="ct"><a href="/student/umkd/0/2026/1">УМКД</a> \\ <a href="/student/umkd/321/2026/1">Социология</a></td>', 'top') +
  row('<td class="tt"><div class="inner"><div><h4>Силлабус</h4><a href="/student/interactive/syllabus/print/22/2">Социология</a><a href="/student/interactive/syllabus/print/22/3">Әлеуметтану</a></div>' +
    '<table class="inner"><tr><th></th><th>Название файла</th><th>Описание</th><th>Язык. гр.</th><th>Размер</th><th>Дата загрузки</th><th>Оцените качество материала</th></tr>' +
    '<tr><th colspan="7" id="lectures">Конспекты лекций</th></tr>' + fileRow(9102, 'Лекция 1.pdf', '') + fileRow(9103, 'Лекция 2.pdf', '') + linkRow(9104, 'Видеозапись лекции') +
    '<tr><th colspan="7" id="tasks">Задания для СРО</th></tr>' + fileRow(9105, 'СРО 1.docx', '') + '</table></div></td>')));

test('UMKD: «Скачать все» stands beside a files section\'s heading, sends its files and leaves the heading and fold alone', () => {
  const p = page(umkdSections, 'https://univer.kaznu.kz/student/umkd/321/2026/1');
  const sent = []; p.w.__univerNative = message => sent.push(message);
  apply(p); apply(p);
  const buttons = p.d.querySelectorAll('button.univer-app-fold-all'), head = p.d.getElementById('lectures');
  assert.equal(buttons.length, 1, 'only the section with two or more files has the button, once after repeated passes; the syllabus block has none');
  const button = buttons[0];
  assert.equal(button.parentNode, head.parentNode, 'it stands in the heading row'); assert.ok(!head.contains(button), 'not inside the heading button');
  assert.equal(head.textContent, 'Конспекты лекций', 'the heading text (fold name and folder) is unchanged');
  assert.equal(button.textContent, ''); assert.equal(button.dataset.label, 'Скачать все'); assert.equal(button.getAttribute('aria-label'), 'Скачать все файлы раздела'); assert.equal(button.title, 'Скачать все файлы раздела');
  button.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
  button.click();
  assert.equal(sent.length, 0, 'a page script clicking the button starts no downloads');
  trustedClick(p.w, button);
  assert.equal(head.getAttribute('aria-expanded'), 'true', 'a click or Enter on the button does not fold the section');
  assert.equal(sent.length, 1); assert.ok(sent[0].startsWith('univer-download-all:'));
  assert.deepEqual(JSON.parse(sent[0].slice('univer-download-all:'.length)), { section: 'Конспекты лекций', urls: ['https://univer.kaznu.kz/student/umkd/get/9102/321/2026/1', 'https://univer.kaznu.kz/student/umkd/get/9103/321/2026/1'], stop: false }, 'the teacher\'s link row is left out');
  p.w.__univerPagesV1.downloading('Конспекты лекций', 1, 2);
  assert.equal(button.dataset.label, 'Остановить · 1/2'); assert.ok(button.hasAttribute('data-univer-busy')); assert.equal(button.getAttribute('aria-label'), 'Остановить');
  trustedClick(p.w, button);
  assert.equal(sent.length, 1, 'a second click within 1.5 s (a double click) sends nothing');
  const now = p.w.performance.now.bind(p.w.performance); p.w.performance.now = () => now() + 2000;
  trustedClick(p.w, button);
  assert.equal(JSON.parse(sent[1].slice('univer-download-all:'.length)).stop, true, 'only a button that shows a run asks to stop it');
  p.w.__univerPagesV1.downloading('Конспекты лекций', 0, 0);
  assert.equal(button.dataset.label, 'Скачать все'); assert.ok(!button.hasAttribute('data-univer-busy'));
  head.click();
  assert.equal(head.getAttribute('aria-expanded'), 'false', 'the heading folds its section');
  assert.equal(p.w.localStorage.length, 0, 'the fold is not stored anywhere');
  apply(p, true, 'en');
  assert.equal(button.dataset.label, 'Download all');
  apply(p, false);
  assert.equal(p.d.querySelectorAll('button.univer-app-fold-all').length, 0, 'Original removes the button');
  assert.equal(head.parentNode.children.length, 1);
  p.close();
});

test('UMKD: every visit opens with all sections unfolded, and folds stored by earlier versions are dropped', () => {
  const stored = JSON.stringify({ '/student/umkd/321/2026/1': ['Конспекты лекций', 'Задания для СРО'] });
  const p = page(umkdSections, 'https://univer.kaznu.kz/student/umkd/321/2026/1', w => w.localStorage.setItem('univer.desktop.umkd-folds.v1', stored));
  apply(p);
  for (const id of ['lectures', 'tasks']) assert.equal(p.d.getElementById(id).getAttribute('aria-expanded'), 'true', id + ' starts open');
  assert.equal(p.w.localStorage.getItem('univer.desktop.umkd-folds.v1'), null, 'the old record is removed');
  p.close();
});

// jsdom has no animations or layout: the run, the row's box and its computed values are stand-ins.
test('UMKD: a clicked fold slides its rows through --univer-slide, turns back from where it is, and Original stops a slide', () => {
  const p = page(umkdSections, 'https://univer.kaznu.kz/student/umkd/321/2026/1');
  apply(p);
  const runs = [], real = p.w.getComputedStyle.bind(p.w), box = p.w.Element.prototype.getBoundingClientRect;
  let slide = '';
  p.w.CSS = { registerProperty() {} };
  p.w.Element.prototype.animate = function (frames, options) { const run = { frames, options, cancelled: false, cancel() { this.cancelled = true; } }; runs.push(run); return run; };
  p.w.getComputedStyle = node => node.matches('tr.file') ? { display: 'grid', paddingTop: '8px', paddingBottom: '8px', marginTop: '0px', marginBottom: '0px',
    borderTopWidth: '1px', borderBottomWidth: '0px', getPropertyValue: name => name === '--univer-slide' ? slide : '' } : real(node);
  p.w.Element.prototype.getBoundingClientRect = function () { return { top: 0, left: 0, width: 600, height: this.matches('tr.file') ? 52 : 30 }; };
  const head = p.d.getElementById('lectures'), rows = Array.from(p.d.querySelectorAll('tr.file')).slice(0, 3);
  const click = () => head.dispatchEvent(new p.w.MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
  const ends = run => [run.frames[0]['--univer-slide'], run.frames[1]['--univer-slide']];
  click();
  assert.equal(runs.length, 3, 'each row of the section slides');
  assert.ok(rows.every(r => r.hasAttribute('data-univer-folded-row') && r.hasAttribute('data-univer-sliding')), 'folded from the first frame, on screen while sliding');
  assert.deepEqual(ends(runs[0]), ['1', '0']);
  assert.equal(runs[0].frames[0]['--univer-slide-h'], '52px'); assert.equal(runs[0].frames[1]['--univer-slide-pt'], '8px'); assert.equal(runs[0].frames[1]['--univer-slide-bt'], '1px');
  assert.equal(runs[0].options.fill, 'forwards');
  assert.ok(rows.every(r => !r.hasAttribute('style')), 'no inline style is written');
  slide = '0.4'; click();
  assert.equal(runs.length, 6); assert.ok(runs.slice(0, 3).every(r => r.cancelled), 'the shutting runs stop');
  assert.deepEqual(ends(runs[3]), ['0.4', '1'], 'the unfold starts where the row was');
  assert.equal(runs[3].frames[0]['--univer-slide-h'], '52px', 'and keeps the open row\'s measures');
  assert.ok(rows.every(r => !r.hasAttribute('data-univer-folded-row') && r.hasAttribute('data-univer-sliding')));
  runs[3].onfinish();
  assert.ok(!rows[0].hasAttribute('data-univer-sliding') && runs[3].cancelled, 'a finished run leaves no mark and lets go of its last frame');
  p.w.getComputedStyle = real; p.w.Element.prototype.getBoundingClientRect = box;
  apply(p);
  assert.ok(rows[1].hasAttribute('data-univer-sliding') && !runs[4].cancelled, 'an adapter pass leaves a running slide alone');
  apply(p, false);
  assert.ok(rows.every(r => !r.hasAttribute('data-univer-sliding') && !r.hasAttribute('data-univer-folded-row')) && runs[4].cancelled && runs[5].cancelled, 'Original stops a slide and leaves no marks');
  p.close();
  const still = page(umkdSections, 'https://univer.kaznu.kz/student/umkd/321/2026/1');
  apply(still); still.w.__univerMotion = false; still.w.CSS = { registerProperty() {} };
  let started = 0; still.w.Element.prototype.animate = () => { started++; return { cancel() {} }; };
  still.d.getElementById('lectures').dispatchEvent(new still.w.MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
  assert.equal(started, 0); assert.ok(Array.from(still.d.querySelectorAll('tr.file')).slice(0, 3).every(r => r.hasAttribute('data-univer-folded-row') && !r.hasAttribute('data-univer-sliding')), 'with animations off the fold is at once');
  still.close();
});

test('UMKD: a section with more files than the app takes in one message has no «Скачать все»', () => {
  const many = n => Array.from({ length: n }, (_, i) => fileRow(20000 + i, 'Файл ' + i + '.pdf', '')).join('');
  const html = count => shell(panel(row('<td class="tt"><div class="inner"><table class="inner"><tr><th></th><th>Название файла</th><th>Описание</th><th>Язык. гр.</th><th>Размер</th><th>Дата загрузки</th><th>Оцените качество материала</th></tr>' +
    '<tr><th colspan="7" id="big">Лекции</th></tr>' + many(count) + '</table></div></td>')));
  for (const [count, shown] of [[200, 1], [201, 0]]) {
    const p = page(html(count), 'https://univer.kaznu.kz/student/umkd/321/2026/1');
    apply(p);
    assert.equal(p.d.querySelectorAll('button.univer-app-fold-all').length, shown, count + ' files');
    p.close();
  }
});

test('pages other than a UMKD course get no outside-link marks', () => {
  const p = page(umkdCourse, 'https://univer.kaznu.kz/student/umkd/');
  apply(p);
  assert.equal(p.d.querySelectorAll('.univer-app-outside-link').length, 0);
  assert.equal(p.d.getElementById('9003').cells[2].childNodes.length, 1);
  apply(p, false); p.close();
});
// Files new since the last visit to a course page. Each visit is a new document over the same profile storage.
const SEEN = 'univer.desktop.umkd-seen.v1';
const course = rows => shell(panel(row('<td class="ct"><a href="/student/umkd/0/2026/1">УМКД</a> \ <a href="/student/umkd/321/2026/1">Социология</a></td>', 'top') +
  row('<td class="tt"><div class="inner"><table class="inner"><tr><th></th><th>Название файла</th><th>Описание</th><th>Язык. гр.</th><th>Размер</th><th>Дата загрузки</th><th>Оцените качество материала</th></tr>' +
    '<tr><th colspan="7">Конспекты лекций</th></tr>' + rows.map(id => fileRow(id, 'Лекция ' + id + '.pdf', '')).join('') + '</table></div></td>')));
function courseVisit(rows, storage, { mark = 'a1', extension = false, url = 'https://univer.kaznu.kz/student/umkd/321/2026/1' } = {}) {
  return page(course(rows), url, w => {
    if (mark) w.localStorage.setItem('univer.app.account.v1', mark);
    if (storage != null) w.localStorage.setItem(SEEN, storage);
    if (extension) w.__univerUmkdSeenKeep = false;
  });
}
const fresh = p => Array.from(p.d.querySelectorAll('.univer-app-file-new')).map(n => n.closest('tr').id);

test('UMKD: files added since the last visit are marked until the page is left; the first visit only notes the files', () => {
  const first = courseVisit([9001, 9002]);
  apply(first);
  assert.deepEqual(fresh(first), [], 'the first visit is the baseline');
  const stored = first.w.localStorage.getItem(SEEN); first.close();
  assert.deepEqual(JSON.parse(stored).pages['/student/umkd/321/2026/1'].ids, ['9001/321', '9002/321']);
  assert.ok(!/Лекция|pdf/.test(stored), 'only file ids are kept');
  const second = courseVisit([9001, 9002, 9003], stored);
  apply(second); apply(second);
  assert.deepEqual(fresh(second), ['9003'], 'one mark, also after a second pass');
  const tag = second.d.querySelector('.univer-app-file-new');
  assert.equal(tag.textContent, 'Новый'); assert.equal(tag.title, 'Появился после вашего прошлого захода');
  assert.equal(tag.previousElementSibling.matches('a.downLoad'), true); assert.equal(second.d.getElementById('9003').querySelector('a.downLoad').textContent, 'Лекция 9003.pdf', 'the link text stays the file name');
  apply(second, true, 'kz'); assert.equal(tag.textContent, 'Жаңа');
  // Original takes the marks away; reading mode brings back the same ones within this visit.
  apply(second, false); assert.deepEqual(fresh(second), []);
  apply(second); assert.deepEqual(fresh(second), ['9003']);
  const after = second.w.localStorage.getItem(SEEN); second.close();
  // The next visit knows them all.
  const third = courseVisit([9001, 9002, 9003], after);
  apply(third); assert.deepEqual(fresh(third), []); third.close();
});

test('UMKD: new-file marks belong to the account and course page; without a mark or in the extension nothing is kept', () => {
  const first = courseVisit([9001]); apply(first); const stored = first.w.localStorage.getItem(SEEN); first.close();
  // Another account signed in here: its first visit is a baseline again and the old list goes.
  const other = courseVisit([9001, 9002], stored, { mark: 'b2' }); apply(other);
  assert.deepEqual(fresh(other), []); assert.equal(JSON.parse(other.w.localStorage.getItem(SEEN)).owner, 'b2'); other.close();
  // Another course page has its own list.
  const elsewhere = courseVisit([9001, 9002], stored, { url: 'https://univer.kaznu.kz/student/umkd/322/2026/1' }); apply(elsewhere);
  assert.deepEqual(fresh(elsewhere), []); elsewhere.close();
  for (const options of [{ mark: '' }, { extension: true }]) {
    const p = courseVisit([9001, 9002], options.mark === '' ? null : stored, options); apply(p);
    assert.deepEqual(fresh(p), []); assert.equal(p.w.localStorage.getItem(SEEN), options.mark === '' ? null : stored, JSON.stringify(options)); p.close();
  }
  // A damaged record starts again; a page without the file table (a notice) is no first look.
  const damaged = courseVisit([9001, 9002], '{"owner":"a1","pages":[1]}'); apply(damaged); assert.deepEqual(fresh(damaged), []); damaged.close();
  const notice = page(shell(panel(row('<td class="ct">Ознакомьтесь с уведомлением</td>'))), 'https://univer.kaznu.kz/student/umkd/321/2026/1', w => w.localStorage.setItem('univer.app.account.v1', 'a1'));
  apply(notice); assert.equal(notice.w.localStorage.getItem(SEEN), null); notice.close();
  // An empty file table is a fair first look: the first file added later is new.
  const empty = courseVisit([]); apply(empty); const none = empty.w.localStorage.getItem(SEEN); empty.close();
  const added = courseVisit([9001], none); apply(added); assert.deepEqual(fresh(added), ['9001']); added.close();
});
