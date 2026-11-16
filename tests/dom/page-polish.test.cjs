const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM, VirtualConsole } = require('jsdom');

// The page polish module is shared word for word with the Android app (assets/univer-polish.js).
const source = fs.readFileSync('src/Univer.Desktop/Assets/page-polish.js', 'utf8');

const fixture = '<table class="wrappertable"><tr id="l4"><td>' +
  '<table><tr class="top"><td class="ct"><a class="univer-app-back-link" href="/student/">Назад</a> / Договоры .</td></tr>' +
  '<tr><td class="ct" id="note"><img src="/img/icons/info.png"> Договор можно скачать после подписания.</td></tr>' +
  '<tr><td class="ct"><p id="warn" style="color:red">Срок оплаты истекает в конце текущего месяца.</p></td></tr>' +
  '<tr><td class="ct"><p id="tip" style="color:green">Все документы приняты и проверены деканатом.</p></td></tr>' +
  '<tr><td class="ct" id="inline">Внимание: <font id="word" style="color:red">оплата должна поступить вовремя</font>, иначе договор будет приостановлен деканатом.</td></tr>' +
  '<tr><td class="ct"><b id="head">ВНИМАНИЕ! СПИСОК ДОКУМЕНТОВ ДЛЯ КАЗНУ</b><ol><li>Паспорт</li></ol></td></tr>' +
  '<tr><td id="teacher">Образцова Б. Н.</td></tr>' +
  '<tr><td class="ct"><p id="none">Договор не найден</p></td></tr>' +
  '<tr><td class="tb" id="pager"><a id="prev" href="/student/contract/1"><img src="/img/left.png"></a><a id="next" href="/student/contract/3"><img src="/img/right.png"></a></td></tr>' +
  '<tr><td class="ct"><img id="pdf" src="/img/pdf.png"><a id="file" href="/files/rules.pdf">Правила оплаты</a></td></tr>' +
  '<tr><td class="ct"><input id="back" type="button" value="Назад"><input id="save" type="button" value="Сохранить"></td></tr></table>' +
  '<table class="univer-app-form-table"><tr><td><label id="rule" style="color:red">ФОТО ДОЛЖНО БЫТЬ В ФОРМАТЕ JPG, НЕ БОЛЕЕ 2 MB</label></td></tr></table>' +
  '<div class="univer-app-table-scroll" id="holder"><table class="univer-app-pairs"><tr><td>Эдвайзер:</td><td>Иванова А. К.</td></tr><tr><td>Телефон:</td><td></td></tr></table></div>' +
  '<table class="univer-app-matrix" id="card"><tr><td rowspan="3"><img src="/photo.jpg"></td><td>ФИО</td></tr><tr><td id="blank"></td></tr><tr><td><li>Студент</li></td></tr></table>' +
  '</td></tr></table>';

const colors = { red: 'rgb(255, 0, 0)', green: 'rgb(0, 128, 0)' };
function page(path, capable = true, html = fixture) {
  const dom = new JSDOM(html, { url: 'https://univer.kaznu.kz' + path, runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
  const w = dom.window, d = w.document;
  Object.defineProperty(d, 'readyState', { value: 'complete', configurable: true });
  w.CSS = { supports: () => capable };
  const computed = w.getComputedStyle.bind(w);
  w.getComputedStyle = (node, pseudo) => {
    let color = 'rgb(0, 0, 0)';
    for (let n = node; n && n.style; n = n.parentElement) if (n.style.color) { color = colors[n.style.color] || n.style.color; break; }
    const style = computed(node, pseudo);
    return new Proxy(style, { get: (t, k) => k === 'color' ? color : typeof t[k] === 'function' ? t[k].bind(t) : t[k] });
  };
  w.eval(source);
  return { w, d, $: id => d.getElementById(id), body: () => d.getElementById('l4').innerHTML, close: () => w.close() };
}

test('notes, alerts, arrows, files and buttons are marked, and a coloured word inside a sentence is left alone', () => {
  const p = page('/student/contract/');
  p.w.__univerPolishV1.apply('ru');
  assert.ok(p.$('note').classList.contains('up-note'));
  assert.ok(p.$('warn').classList.contains('up-alert'));
  assert.ok(p.$('tip').classList.contains('up-tip'));
  assert.equal(p.$('word').className, '');
  assert.ok(p.$('rule').classList.contains('up-alert'));
  assert.ok(p.$('prev').classList.contains('up-prev') && p.$('next').classList.contains('up-next'));
  assert.equal(p.$('prev').getAttribute('aria-label'), 'Предыдущая страница');
  assert.ok(p.$('pager').classList.contains('up-pager'));
  assert.ok(p.$('pdf').hasAttribute('data-up-hidden') && p.$('file').classList.contains('up-file'));
  assert.ok(p.$('back').classList.contains('up-secondary'));
  assert.ok(!p.$('save').classList.contains('up-secondary'));
  assert.ok(p.$('none').classList.contains('up-empty'));
  assert.ok(p.d.getElementById('univer-polish-style'));
  p.close();
});

test('text fixes: heading period and crumb slash, sentence case that keeps abbreviations, initials kept with the surname', () => {
  const p = page('/student/contract/');
  p.w.__univerPolishV1.apply('ru');
  const crumb = p.d.querySelector('tr.top>td.ct').lastChild.nodeValue;
  assert.equal(crumb, 'Договоры');
  assert.equal(p.$('head').textContent, 'Внимание! Список документов для КазНУ');
  assert.equal(p.$('rule').textContent, 'Фото должно быть в формате JPG, не более 2 MB');
  assert.equal(p.$('teacher').textContent, 'Образцова Б. Н.');
  p.close();
});

test('a label and value table becomes a list of facts, an empty value and a blank profile cell say so, and language changes relabel them', () => {
  const p = page('/student/advisor/');
  p.w.__univerPolishV1.apply('ru');
  const facts = p.d.querySelector('.up-facts');
  assert.ok(facts && facts.nextElementSibling === p.$('holder'));
  assert.ok(p.$('holder').hasAttribute('data-up-hidden'));
  const items = Array.from(facts.querySelectorAll('.up-fact')).map(f => [f.firstChild.textContent, f.lastChild.textContent]);
  assert.deepEqual(items, [['Эдвайзер', 'Иванова А. К.'], ['Телефон', 'Не указано']]);
  assert.equal(p.$('blank').textContent, 'Не указано');
  const before = p.body();
  p.w.__univerPolishV1.apply('ru');
  assert.equal(p.body(), before, 'a second pass changes nothing');
  assert.equal(p.d.querySelector('.up-facts'), facts, 'the facts box is kept, not rebuilt');
  p.w.__univerPolishV1.apply('kz');
  assert.equal(facts.lastChild.lastChild.textContent, 'Көрсетілмеген');
  assert.equal(p.$('blank').textContent, 'Көрсетілмеген');
  assert.equal(p.$('prev').getAttribute('aria-label'), 'Алдыңғы бет');
  p.close();
  for (const path of ['/student/discussion/acpos/week/list/1/2/2026/1/', '/student/bachelor/']) {
    const owned = page(path);
    owned.w.__univerPolishV1.apply('ru');
    assert.equal(owned.d.querySelector('.up-facts'), null, path + ' keeps its own record layout');
    assert.ok(!owned.$('holder').hasAttribute('data-up-hidden'));
    owned.close();
  }
});

test('release() puts every node, text, class and attribute back as the site sent it', () => {
  const p = page('/student/contract/');
  const original = p.body(), nodes = Array.from(p.d.querySelectorAll('#l4 *'));
  p.w.__univerPolishV1.apply('ru');
  p.w.__univerPolishV1.apply('en');
  assert.notEqual(p.body(), original);
  p.w.__univerPolishV1.release();
  assert.equal(p.body(), original);
  assert.deepEqual(Array.from(p.d.querySelectorAll('#l4 *')), nodes);
  assert.equal(p.d.getElementById('univer-polish-style'), null);
  p.w.__univerPolishV1.apply('ru');
  assert.ok(p.$('warn').classList.contains('up-alert'), 'applies again after release');
  p.close();
});

test('nothing changes without :has() support or outside the student pages', () => {
  for (const p of [page('/student/contract/', false), page('/news/student')]) {
    const original = p.body();
    p.w.__univerPolishV1.apply('ru');
    assert.equal(p.body(), original);
    assert.equal(p.d.getElementById('univer-polish-style'), null);
    p.w.__univerPolishV1.release();
    p.close();
  }
});

test('release() leaves a text the layout already put back, and restores the one it changed itself', () => {
  const p = page('/student/contract/');
  const crumb = p.d.querySelector('tr.top>td.ct').lastChild;
  crumb.nodeValue = '/Договоры .';      // as the site sends it
  crumb.nodeValue = ' / Договоры .';    // the layout's spacing, applied before polish
  p.w.__univerPolishV1.apply('ru');
  assert.equal(crumb.nodeValue, 'Договоры');
  crumb.nodeValue = '/Договоры .';      // the layout restores first in Original
  p.w.__univerPolishV1.release();
  assert.equal(crumb.nodeValue, '/Договоры .');
  const again = page('/student/contract/');
  const node = again.$('teacher').firstChild;
  again.w.__univerPolishV1.apply('ru');
  again.w.__univerPolishV1.release();
  assert.equal(node.nodeValue, 'Образцова Б. Н.');
  p.close(); again.close();
});

test('repeated passes keep one relabel per arrow', () => {
  const p = page('/student/contract/');
  for (let i = 0; i < 50; i++) p.w.__univerPolishV1.apply('ru');
  const link = p.$('prev'), read = link.getAttribute.bind(link);
  let reads = 0;
  link.getAttribute = name => { if (name === 'aria-label') reads++; return read(name); };
  p.w.__univerPolishV1.apply('kz');
  assert.equal(link.getAttribute('aria-label'), 'Алдыңғы бет');
  assert.ok(reads <= 3, 'relabels run ' + reads + ' times');
  p.close();
});

const heads = '<table class="univer-app-cards"><tr class="univer-app-card-header"><th>Дисциплина</th><th>Дата</th></tr></table>';
const rows = '<table class="univer-app-cards"><tr><td>История Казахстана</td><td>12.12.2026</td></tr></table>';
const split = (first, second) => '<table class="wrappertable"><tr id="l4"><td><table><tr><td id="a">' + first + '</td></tr><tr><td id="b">' + second + '</td></tr></table></td></tr></table>';
test('"no records yet" only when the whole page has no rows, and never on pages whose rows arrive in a separate table', () => {
  const lonely = page('/student/contract/', true, split(heads, ''));
  lonely.w.__univerPolishV1.apply('ru');
  assert.equal(lonely.d.querySelector('.up-empty').textContent, 'Записей пока нет');
  lonely.close();
  const elsewhere = page('/student/contract/', true, split(heads, rows));
  elsewhere.w.__univerPolishV1.apply('ru');
  assert.equal(elsewhere.d.querySelector('.up-empty'), null);
  assert.equal(elsewhere.d.querySelector('[data-up-hidden]'), null);
  elsewhere.close();
  // Exam schedule before its AJAX rows arrive: the bare headings hide, no note of our own, and they come back with the rows.
  const exams = page('/student/myexam/schedule/', true, split(heads, ''));
  exams.w.__univerPolishV1.apply('ru');
  assert.equal(exams.d.querySelector('.up-empty'), null);
  assert.ok(exams.d.querySelector('table.univer-app-cards').hasAttribute('data-up-hidden'));
  exams.$('b').innerHTML = rows;
  exams.w.__univerPolishV1.apply('ru');
  assert.equal(exams.d.querySelector('[data-up-hidden]'), null);
  exams.close();
  // Rows with merged cells make the result a matrix, not cards; they still count as rows.
  const merged = page('/student/myexam/schedule/', true, split(heads, '<table class="univer-app-matrix"><tr><td rowspan="2">10:00</td><td>История</td></tr><tr><td>Право</td></tr></table>'));
  merged.w.__univerPolishV1.apply('ru');
  assert.equal(merged.d.querySelector('[data-up-hidden]'), null);
  merged.close();
});

// On Windows the layout puts a table into its scroll box after polish has run once; the hidden mark must follow it.
function wrapInBox(table) { const box = table.ownerDocument.createElement('div'); box.className = 'univer-app-table-scroll'; table.before(box); box.appendChild(table); return box; }
test('a table the layout wraps between passes shows again with its rows, and a facts mirror hides the whole box', () => {
  const exams = page('/student/myexam/schedule/', true, split(heads, ''));
  exams.w.__univerPolishV1.apply('ru');
  const box = wrapInBox(exams.$('a').querySelector('table'));
  exams.w.__univerPolishV1.apply('ru');
  assert.ok(box.hasAttribute('data-up-hidden'));
  exams.$('b').innerHTML = rows;
  exams.w.__univerPolishV1.apply('ru');
  assert.equal(exams.d.querySelector('[data-up-hidden]'), null);
  exams.close();
  // The same table gets its rows later.
  const later = page('/student/contract/', true, split(heads, ''));
  later.w.__univerPolishV1.apply('ru');
  const table = later.$('a').querySelector('table');
  wrapInBox(table);
  later.w.__univerPolishV1.apply('ru');
  table.insertAdjacentHTML('beforeend', '<tr><td>Договор 12</td><td>01.09.2026</td></tr>');
  later.w.__univerPolishV1.apply('ru');
  assert.equal(later.d.querySelector('[data-up-hidden]'), null);
  assert.equal(later.d.querySelector('.up-empty'), null);
  later.close();
  // The box takes in the layout's sort button while hidden, then the rows come back.
  const sorted = page('/student/contract/', true, split(heads, ''));
  const list = sorted.$('a').querySelector('table');
  const frame0 = wrapInBox(list);
  sorted.w.__univerPolishV1.apply('ru');
  assert.ok(frame0.hasAttribute('data-up-hidden'));
  list.before(sorted.d.createElement('button'));
  list.insertAdjacentHTML('beforeend', '<tr><td>Заявка 3</td><td>02.09.2026</td></tr>');
  sorted.w.__univerPolishV1.apply('ru');
  assert.equal(sorted.d.querySelector('[data-up-hidden]'), null);
  sorted.close();
  const advisor = page('/student/advicer/', true, '<table class="wrappertable"><tr id="l4"><td><table class="univer-app-pairs" id="pairs"><tr><td>Эдвайзер:</td><td>Иванова А. К.</td></tr><tr><td>Телефон:</td><td></td></tr></table></td></tr></table>');
  advisor.w.__univerPolishV1.apply('ru');
  const frame = wrapInBox(advisor.$('pairs'));
  advisor.w.__univerPolishV1.apply('ru');
  assert.ok(frame.hasAttribute('data-up-hidden'));
  assert.ok(!advisor.$('pairs').hasAttribute('data-up-hidden'));
  assert.equal(advisor.d.querySelectorAll('.up-facts').length, 1);
  advisor.w.__univerPolishV1.release();
  assert.equal(advisor.d.querySelector('[data-up-hidden],.up-facts'), null);
  advisor.close();
});

test('abbreviations keep their capitals in sentence case and in fact labels', () => {
  const html = '<table class="wrappertable"><tr id="l4"><td><table><tr><td class="ct"><b id="h">ВНИМАНИЕ! ЗАГРУЗИТЕ ЭЦП, ИИН И ИУП В СДО MOODLE</b><ol><li>x</li></ol></td></tr></table>' +
    '<table class="univer-app-pairs"><tr><td>ФИО СТУДЕНТА:</td><td>Иванов</td></tr><tr><td>GPA (ОБЩИЙ):</td><td>3,1</td></tr><tr><td>ТЕЛЕФОН:</td><td></td></tr>' +
    '<tr><td>ИИН/БИН:</td><td>—</td></tr><tr><td>ОП (ШИФР):</td><td>6B01</td></tr></table></td></tr></table>';
  const p = page('/student/advicer/', true, html);
  p.w.__univerPolishV1.apply('ru');
  assert.equal(p.$('h').textContent, 'Внимание! Загрузите ЭЦП, ИИН и ИУП в СДО MOODLE');
  assert.deepEqual(Array.from(p.d.querySelectorAll('.up-fact-label')).map(n => n.textContent), ['ФИО студента', 'GPA (общий)', 'Телефон', 'ИИН/БИН', 'ОП (шифр)']);
  p.close();
});

// Пересдача FX and Повышение GPA: an "Информация" panel with nothing under it; Задолженности: a shouting memo; Практика:
// a named document behind a generic "Ссылка".
test('an empty heading panel hides until content arrives, a memo stops shouting, a named document link becomes a card', () => {
  const panel = (rows) => '<table class="mt" id="info"><tr class="top"><td class="lt"></td><td class="ct"> Информация </td><td class="rt"></td></tr>' + rows + '</table>';
  const blank = '<tr class="mid"><td class="lt"></td><td class="pd"></td><td class="rt"></td></tr><tr class="mid"><td class="lt"></td><td class="ct"> </td><td class="rt"></td></tr>';
  const html = '<table class="wrappertable"><tr id="l4"><td>' + panel(blank) +
    '<table class="mt"><tr class="top"><td class="ct">Информация</td></tr><tr class="mid"><td class="ct" id="memo"><b><font style="color:red">ВНИМАНИЕ!</font></b><br><b id="memo-head">ПАМЯТКА ПО ПОВТОРНОМУ ИЗУЧЕНИЮ ДИСЦИПЛИН В СИСТЕМЕ UNIVER</b><br>1. Заявку подаёт студент.</td></tr></table>' +
    '<table class="mt"><tr class="top"><td class="ct">Ссылки</td></tr><tr class="mid"><td class="ct"><div id="doc"><b>Инструкция для студентов</b> <a href="/Content/helper/practik/Практика.docx">Ссылка</a></div>' +
    '<div id="plain"><b>Сайт</b> <a href="https://www.kaznu.kz/">Ссылка</a></div></td></tr></table></td></tr></table>';
  const p = page('/student/failfx/', true, html);
  p.w.__univerPolishV1.apply('ru');
  assert.ok(p.$('info').hasAttribute('data-up-hidden'));
  assert.equal(p.$('memo').querySelector('font').textContent, 'Внимание!');
  assert.equal(p.$('memo-head').textContent, 'Памятка по повторному изучению дисциплин в системе UNIVER');
  assert.ok(p.$('doc').classList.contains('up-doc') && p.$('doc').querySelector('a').classList.contains('up-doc-link'));
  assert.ok(!p.$('plain').classList.contains('up-doc'));
  p.$('info').rows[1].cells[1].textContent = 'Приём заявок до 20 декабря';
  p.w.__univerPolishV1.apply('ru');
  assert.ok(!p.$('info').hasAttribute('data-up-hidden'));
  p.w.__univerPolishV1.release();
  assert.equal(p.d.querySelector('[data-up-hidden],.up-doc,.up-doc-link'), null);
  assert.equal(p.$('memo').querySelector('font').textContent, 'ВНИМАНИЕ!');
  p.close();
});

test('names stay in capitals, a notice split by colour reads as one sentence, and panels hide only over nothing', () => {
  const html = '<table class="wrappertable"><tr id="l4"><td>' +
    '<table class="mt"><tr class="top"><td class="ct">Информация</td></tr><tr class="mid"><td class="ct" id="memo">' +
    '<b id="name">ИВАНОВА АЙГЕРІМ СЕРИКОВНА</b><br><b id="due">СДАЙТЕ ДО <font style="color:red">20 ДЕКАБРЯ</font> ВКЛЮЧИТЕЛЬНО!</b><br>' +
    '<b id="sign">ПОДПИСЬ СТУДЕНТА И ДЕКАНА А.Б. ОБЯЗАТЕЛЬНА!</b><br><b id="name4">ИВАНОВА АЙГЕРІМ СЕРІК ҚЫЗЫ</b><br>' +
    '<b id="lines">ВНИМАНИЕ<br>СДАЙТЕ ДОКУМЕНТЫ ДО ПЯТНИЦЫ</b><br><font><b id="wrapped">ВНИМАНИЕ! ПАМЯТКА ПО ПОВТОРНОМУ ИЗУЧЕНИЮ</b></font><br><b id="late">СДАЙТЕ ДО 20 ДЕКАБРЯ 2026 Г. ОПОЗДАВШИЕ НЕ ДОПУСКАЮТСЯ!</b><br>' +
    '<b id="not">ЗАЯВКИ <font style="color:red">НЕ</font> ПРИНИМАЮТСЯ!</b><br><b id="dean">ПОДПИСЬ ДЕКАНА <font style="color:red">А.Б.</font> ОБЯЗАТЕЛЬНА!</b><br>' +
    '<b id="room">ПОДПИСЬ ДЕКАНА А.Б. В ДЕКАНАТЕ ОБЯЗАТЕЛЬНА!</b><br><b id="fio">УКАЖИТЕ Ф.И.О, ГРУППУ И КУРС!</b><br>' +
    '<b id="block">ПАМЯТКА<div>ПО ПОВТОРНОМУ ИЗУЧЕНИЮ</div>СДАЙТЕ ДОКУМЕНТЫ ДО ПЯТНИЦЫ</b><br><b id="split">ИВАНОВА А<font>ЙГЕРІМ</font> СЕРІК ҚЫЗЫ</b><br>' +
    '<b id="office">ЗАЯВЛЕНИЯ ПРИНИМАЮТСЯ В <font>ОР</font> ДО ПЯТНИЦЫ!</b><br><b id="pad"><font>ВНИМАНИЕ</font> <font>СДАЙТЕ ДОКУМЕНТЫ ДО ПЯТНИЦЫ</font></b><br>' +
    '<b id="around">ПАМЯТКА<table><tr><td>ИВАНОВ ИВАН</td></tr></table>СДАЙТЕ ДОКУМЕНТЫ ДО ПЯТНИЦЫ</b><br><b id="fio2">УКАЖИТЕ Ф.И.<font>О</font>, ГРУППУ И КУРС!</b><br>' +
    '<b id="head">ВНИМАНИЕ! СПИСОК<table><tr><th id="headed">ИВАНОВ ИВАН ИВАНОВИЧ</th></tr></table></b><font><b id="list">ВНИМАНИЕ! СПИСОК ДОПУЩЕННЫХ:<table><tr><td id="listed">ИВАНОВ ИВАН ИВАНОВИЧ</td></tr></table></b></font>' +
    '<table><tr><td><b id="nested">ИВАНОВ ИВАН ИВАНОВИЧ МАГИСТРАНТ</b></td></tr></table></td></tr></table>' +
    '<table class="mt"><tr class="top"><td class="ct" id="contracts"><b>Договоры</b> .</td></tr><tr class="mid"><td class="ct">Договор подписан</td></tr></table>' +
    '<table class="mt"><tr class="top"><td class="ct" id="crumbs"><a class="univer-app-back-link" href="/student/">Назад</a> / <a href="/student/x">Раздел</a> / Подраздел .</td></tr>' +
    '<tr class="mid"><td class="ct"><b id="gap">ПОДПИСЬ ДЕКАНА <font>А.Б.</font> <font>В ДЕКАНАТЕ ОБЯЗАТЕЛЬНА!</font></b></td></tr></table>' +
    '<table class="mt"><tr class="top"><td class="ct" id="blanked"><a class="univer-app-back-link" href="/student/">Назад</a> / <a href="/student/x">Раздел</a> / Подраздел .</td></tr><tr class="mid"><td class="ct">Текст</td></tr></table>' +
    '<table class="mt" id="year"><tr class="top"><td class="ct">Учебный год <select><option>2026</option></select></td></tr><tr class="mid"><td class="ct"> </td></tr></table>' +
    '<table class="mt" id="back"><tr class="top"><td class="ct"><span><a>Назад</a> / Курс</span></td></tr><tr class="mid"><td class="ct"> </td></tr></table>' +
    '<table class="mt"><tr class="top"><td class="ct">Справки</td></tr><tr class="mid"><td class="ct"><div id="moved"><b>Справка</b> <a id="moved-link" href="/files/d.pdf">Ссылка</a></div></td></tr></table>' +
    '<table class="mt" id="outer"><tr class="top"><td class="ct">Практика</td></tr><tr class="mid"><td class="ct">' +
    '<table class="mt" id="inner"><tr class="top"><td class="ct">Информация</td></tr><tr class="mid"><td class="ct"> </td></tr></table></td></tr></table>' +
    '<table class="mt"><tr class="top"><td class="ct">Файлы</td></tr><tr class="mid"><td class="ct univer-app-doc-links"><div id="grid"><b>Положение</b> <a href="/files/a.pdf">Ссылка</a></div>' +
    '<div id="icon"><b>Приказ</b> <a href="/files/b.pdf"><img src="/img/pdf.png"></a></div><div id="later"><b>Заявление</b> <a href="/files/c.docx">Скачать</a></div></td></tr></table>' +
    '</td></tr></table>';
  const p = page('/student/practik/', true, html);
  p.$('grid').parentElement.classList.remove('univer-app-doc-links');
  p.$('blanked').childNodes[1].nodeValue = ''; // the layout blanks the slash after Back itself
  p.w.__univerPolishV1.apply('ru');
  assert.equal(p.$('name').textContent, 'ИВАНОВА АЙГЕРІМ СЕРИКОВНА');
  assert.equal(p.$('nested').textContent, 'ИВАНОВ ИВАН ИВАНОВИЧ МАГИСТРАНТ');
  assert.equal(p.$('due').textContent, 'Сдайте до 20 декабря включительно!');
  assert.equal(p.$('sign').textContent, 'Подпись студента и декана А.Б. обязательна!');
  assert.equal(p.$('name4').textContent, 'ИВАНОВА АЙГЕРІМ СЕРІК ҚЫЗЫ');
  assert.equal(p.$('lines').innerHTML, 'Внимание<br>Сдайте документы до пятницы', 'a new line starts with a capital');
  assert.equal(p.$('wrapped').textContent, 'Внимание! Памятка по повторному изучению', 'a notice wrapped in font still counts');
  assert.equal(p.$('late').textContent, 'Сдайте до 20 декабря 2026 г. Опоздавшие не допускаются!', 'a lone letter with a period is not an initial');
  assert.equal(p.$('not').textContent, 'Заявки не принимаются!');
  assert.equal(p.$('dean').textContent, 'Подпись декана А.Б. обязательна!');
  assert.equal(p.$('room').textContent, 'Подпись декана А.Б. в деканате обязательна!', 'a one-letter word after initials');
  assert.equal(p.$('fio').textContent, 'Укажите Ф.И.О, группу и курс!');
  assert.equal(p.$('block').innerHTML, 'Памятка<div>По повторному изучению</div>Сдайте документы до пятницы', 'text after a block starts a line');
  assert.equal(p.$('split').textContent, 'ИВАНОВА АЙГЕРІМ СЕРІК ҚЫЗЫ', 'a word split by colour counts once');
  assert.equal(p.$('list').firstChild.nodeValue, 'Внимание! Список допущенных:');
  assert.equal(p.$('listed').textContent, 'ИВАНОВ ИВАН ИВАНОВИЧ', 'names in a table inside the notice stay');
  assert.equal(p.$('office').textContent, 'Заявления принимаются в ОР до пятницы!');
  assert.equal(p.$('pad').textContent, 'Внимание сдайте документы до пятницы', 'words apart in separate nodes count as words');
  assert.equal(p.$('around').firstChild.nodeValue + '|' + p.$('around').lastChild.nodeValue, 'Памятка|Сдайте документы до пятницы', 'a nested table is a line edge');
  assert.equal(p.$('around').querySelector('td').textContent, 'ИВАНОВ ИВАН');
  assert.equal(p.$('fio2').textContent, 'Укажите Ф.И.О, группу и курс!');
  assert.equal(p.$('headed').textContent, 'ИВАНОВ ИВАН ИВАНОВИЧ', 'a nested heading cell is not the notice');
  assert.equal(p.$('gap').textContent, 'Подпись декана А.Б. в деканате обязательна!', 'a space between nodes after initials');
  // A second pass changes nothing, also where the first one emptied a text (the heading's lone period, the crumb slash).
  const once = p.body();
  for (let i = 0; i < 3; i++) p.w.__univerPolishV1.apply('ru');
  assert.equal(p.body(), once);
  assert.equal(p.$('crumbs').textContent, 'НазадРаздел / Подраздел', 'only the slash after Back and the final period go');
  assert.equal(p.$('blanked').textContent, 'НазадРаздел / Подраздел', 'a slash the layout already blanked leaves the next one alone');
  // The site or the layout rewrites a text the module changed: its new text wins, and Original leaves it.
  const heading = p.$('contracts').firstChild.nextSibling;
  heading.nodeValue = ' — архив';
  p.w.__univerPolishV1.apply('ru');
  assert.equal(heading.nodeValue, ' — архив');
  assert.ok(!p.$('year').hasAttribute('data-up-hidden'), 'a heading with its own select stays');
  assert.ok(!p.$('back').hasAttribute('data-up-hidden'), 'a heading with the site\'s Back link stays');
  assert.ok(p.$('moved-link').classList.contains('up-doc-link'));
  p.$('moved').after(p.$('moved-link'));
  p.w.__univerPolishV1.apply('ru');
  assert.ok(!p.$('moved-link').classList.contains('up-doc-link') && !p.$('moved').classList.contains('up-doc'), 'a link moved out of its card loses the look');
  assert.ok(p.$('inner').hasAttribute('data-up-hidden') && p.$('outer').hasAttribute('data-up-hidden'), 'a panel over a hidden panel hides');
  assert.ok(!p.$('icon').classList.contains('up-doc'), 'a link with no words is not a card');
  assert.ok(p.$('grid').classList.contains('up-doc') && p.$('later').classList.contains('up-doc'));
  // Study pages take the cell over on a later pass, and so does the layout with one of the links.
  p.$('grid').parentElement.classList.add('univer-app-doc-links');
  p.w.__univerPolishV1.apply('ru');
  assert.ok(!p.$('grid').classList.contains('up-doc') && !p.$('grid').querySelector('.up-doc-link'));
  p.$('inner').rows[1].cells[0].textContent = 'Отчёт сдаётся руководителю практики';
  p.w.__univerPolishV1.apply('ru');
  assert.ok(!p.$('inner').hasAttribute('data-up-hidden') && !p.$('outer').hasAttribute('data-up-hidden'), 'both show once content arrives');
  p.w.__univerPolishV1.release();
  assert.equal(p.$('due').textContent, 'СДАЙТЕ ДО 20 ДЕКАБРЯ ВКЛЮЧИТЕЛЬНО!');
  assert.equal(heading.nodeValue, ' — архив', 'Original leaves a text the site rewrote');
  assert.equal(p.$('crumbs').textContent, 'Назад / Раздел / Подраздел .');
  assert.equal(p.d.querySelector('[data-up-hidden],.up-doc,.up-doc-link'), null);
  p.close();
});

// A rule in the module's cascade layer beats the layout's unlayered !important, so it must not reach the boxes the layout
// already shows in full: the schedule's and course weeks' div.inner.scrool and the UMKD catalog's .scrool200.
test('scroll box rules reach only the message boxes', () => {
  const p = page('/student/contract/');
  p.w.__univerPolishV1.apply('ru');
  const rules = p.d.getElementById('univer-polish-style').textContent.split('}').map(r => r.split('{')[0]).filter(r => /scrool/.test(r));
  assert.ok(rules.length > 0);
  for (const rule of rules) assert.match(rule, /div\.scrool:is\(\.inbox,\.mes\)/, rule.trim());
  p.close();
});

test('script selectors spell out :is() lists, which old Android WebViews cannot parse', () => {
  const call = /\.(?:querySelector(?:All)?|matches|closest)\(\s*(['"`])((?:(?!\1).)*)\1/g;
  const found = Array.from(source.matchAll(call)).map(m => m[2]).filter(s => /:is\(|:has\(/.test(s));
  assert.deepEqual(found, []);
});
