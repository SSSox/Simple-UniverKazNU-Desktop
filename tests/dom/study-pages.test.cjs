const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
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

function page(content, url) {
  const html = '<table class="wrappertable"><tr id="l1"><td class="header">КазНУ</td></tr><tr id="l4"><td>' + content + '</td></tr></table>';
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const w = dom.window;
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.__univerNativeNotify = () => {};
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
