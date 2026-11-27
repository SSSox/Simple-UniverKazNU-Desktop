const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const scripts = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'study-pages.js', 'course-tasks.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'page-polish.js', 'univer-layout.js', 'desktop.js'].map(read);

function page(html, url, before) {
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
// The rules of a media block of a style element, by the block's condition.
const rules = (style, media) => Array.from(style.sheet.cssRules).filter(r => r.media && r.media.mediaText.replace(/\s/g, '') === media).flatMap(r => Array.from(r.cssRules));

// The exam schedule as the site sends it: the headings in one table, the AJAX list with a countdown row per slot in another.
const exam = '<table class="wrappertable"><tr id="l4"><td><table class="mt"><tr class="mid"><td class="tt"><table class="inner" id="head"><tr><th>Название дисциплины</th><th>Преподаватель</th><th>Тип</th>' +
  '<th>Аудитория факультета</th><th>Тип группы</th><th>Примечание</th><th>&nbsp;</th></tr></table><div class="inner"><table class="inner" id="scheduleList">' +
  '<tr><th colspan="7" class="last timerTicked">15.12.2026 13:00 <br><b>До начало экзамена </b> <span data-timer="1">Осталось 3 дня</span></th></tr>' +
  '<tr class="link" id="15.12.2026 13:00:00"><td>Казахский (русский) язык </td><td>Тестов А. А.</td><td>Экзамен</td><td>Корп: ФИТ | Ауд.: Онлайн</td><td>сем</td><td id="note"></td><td id="spacer">&nbsp;</td></tr>' +
  '</table></div></td></tr></table></td></tr></table>';

test('a list whose headings sit in another table stays cards at every width and leaves out its blank fields; Original removes the marks', () => {
  const p = page(exam, 'https://univer.kaznu.kz/student/myexam/schedule/');
  apply(p);
  const list = p.d.getElementById('scheduleList'), head = p.d.getElementById('head'), note = p.d.getElementById('note'), spacer = p.d.getElementById('spacer');
  assert.ok(list.classList.contains('univer-app-cards') && list.classList.contains('univer-app-split-cards'), 'the list is marked as cards with borrowed headings');
  assert.ok(head.classList.contains('univer-app-split-head'), 'the heading table is marked too');
  assert.ok(note.classList.contains('univer-app-blank-field') && spacer.classList.contains('univer-app-blank-field'), 'empty and space-only cells are blank fields');
  assert.equal(list.rows[1].cells[1].classList.contains('univer-app-blank-field'), false, 'a field with a value is kept');
  const style = p.d.getElementById('univer-app-mobile-style'), wide = rules(style, '(min-width:900px)');
  // Above 900 px the table layout leaves out the borrowed-heading cards, and a real table sizes its columns by their content.
  const table = wide.find(r => r.selectorText.includes('univer-app-split-cards') && r.style.getPropertyValue('table-layout'));
  assert.ok(table, 'the table-mode rule excludes the split lists');
  assert.equal(table.style.getPropertyValue('table-layout'), 'auto');
  assert.ok(!wide.some(r => /^html\.univer-mobile \.univer-app-cards (:is\(|\.univer-app-card-row|td|th)/.test(r.selectorText)), 'no table-mode rule still reaches every card table');
  const blank = Array.from(style.sheet.cssRules).find(r => /card-row ?> ?td.univer-app-blank-field$/.test(r.selectorText ?? ''));
  assert.equal(blank?.style.getPropertyValue('display'), 'none');
  const desktop = p.d.getElementById('univer-desktop-style');
  const words = rules(desktop, '(min-width:900px)').find(r => /univer-app-cards:not\(\.univer-app-split-cards\) :is\(td, ?th\)/.test(r.selectorText));
  assert.equal(words?.style.getPropertyValue('overflow-wrap'), 'break-word', 'a word longer than its column breaks instead of running into the next cell');
  apply(p, false);
  assert.equal(p.d.querySelectorAll('.univer-app-split-cards,.univer-app-split-head,.univer-app-blank-field').length, 0);
  p.close();
});

// The printed interactive syllabus: the site floats every block of its sheet; contacts are a heading row and a row per person.
const people = rows => '<div class="input-row"><div class="input-field"><b>Ф.И.О.</b></div><div class="input-field"><b>Номер мобильного телефона</b></div><div class="input-field"><b>Email</b></div></div>' +
  rows.map(r => `<div class="input-row"><div class="input-field"><span>${r[0]}</span></div><div class="input-field"><span>${r[1]}</span></div><div class="input-field"><span>${r[2]}</span></div></div>`).join('');
const syllabus = '<head><style>#content div {position:relative; float:left; width:100%; margin-bottom:3px} .input-row {display:flex; gap:10px}</style></head><body><div id="content"><table class="students">' +
  '<tr><td>Название дисциплины:</td><td>Казахский (русский) язык</td></tr>' +
  `<tr><td>Лектор-(ы):</td><td id="lecturers">${people([['Тестова Айгерим Сериковна', '+7 (701) 234-56-78', 'a.testova@example.kz'], ['Тестов Арман', '8 701 23', 'не указан']])}</td></tr>` +
  `<tr><td>Ассистенты:</td><td id="assistants">${people([['', '', ''], ['Сейітова Дана', '', 'dana@@example']])}</td></tr>` +
  `<tr><td>Ассистенты:</td><td id="nobody">${people([['', '', '']])}</td></tr></table></div></body>`;

test('printed syllabus contacts: phone and mail links only for well-formed values, copy buttons, empty rows left out, Original restores the cells', async () => {
  let copied = null;
  const p = page(syllabus, 'https://univer.kaznu.kz/student/interactive/syllabus/print/12/3', w => {
    Object.defineProperty(w.navigator, 'clipboard', { value: { writeText: value => { copied = value; return Promise.resolve(); } }, configurable: true });
  });
  const cells = ['lecturers', 'assistants', 'nobody'].map(id => p.d.getElementById(id)), before = cells.map(c => c.innerHTML);
  const phone = cells[0].querySelectorAll('.input-row')[1].children[1].querySelector('span').firstChild;
  apply(p); apply(p);
  const links = Array.from(p.d.querySelectorAll('a.univer-app-contact-link')).map(a => a.getAttribute('href'));
  assert.deepEqual(links, ['tel:+77012345678', 'mailto:a.testova@example.kz'], 'a short number, "не указан" and a broken address stay text');
  assert.equal(p.d.querySelector('a[href^="tel:"]').firstChild, phone, 'the link holds the original text node');
  const buttons = Array.from(p.d.querySelectorAll('button.univer-app-copy'));
  assert.deepEqual(buttons.map(b => b.dataset.univerKind), ['name', 'phone', 'email', 'name', 'phone', 'name', 'email'], 'one button per value, none for "не указан", also after a second pass');
  assert.ok(buttons.every(b => b.type === 'button' && b.getAttribute('aria-label').startsWith('Копировать: ')));
  assert.equal(buttons[2].getAttribute('aria-label'), 'Копировать: a.testova@example.kz');
  const rows = cells[1].querySelectorAll('.input-row');
  assert.ok(rows[1].classList.contains('univer-app-contact-empty') && !rows[2].classList.contains('univer-app-contact-empty'), 'only the empty person row is left out');
  assert.ok(cells[2].querySelector('.univer-app-contact-head').classList.contains('univer-app-contact-empty'), 'a cell with nobody hides its headings');
  assert.equal(cells[2].querySelector('.univer-app-contact-none').textContent, 'Не указаны');
  assert.equal(cells[0].querySelectorAll('.input-row')[1].children[2].dataset.univerContactLabel, 'Email', 'each value knows its heading for narrow screens');
  buttons[2].click(); await new Promise(r => setTimeout(r, 0));
  assert.equal(copied, 'a.testova@example.kz', 'the value is copied without the button text');
  assert.ok(buttons[2].classList.contains('univer-app-copied'));
  assert.equal(buttons[2].textContent, 'Скопировано');
  assert.equal(p.d.querySelector('.univer-app-copy-status[role=status]').textContent, 'Скопировано', 'screen readers hear it');
  apply(p, true, 'kz');
  assert.equal(cells[2].querySelector('.univer-app-contact-none').textContent, 'Көрсетілмеген');
  assert.ok(buttons[0].getAttribute('aria-label').startsWith('Көшіру: '));
  apply(p, true, 'en');
  assert.equal(buttons[2].textContent, 'Copied');
  // Print shows the page as the site made it: copy buttons and the note are screen-only.
  const style = p.d.getElementById('univer-pages-style');
  const print = rules(style, 'print').map(r => r.selectorText).join(',');
  assert.match(print, /\.univer-app-copy/); assert.match(print, /\.univer-app-contact-none/);
  assert.ok(Array.from(style.sheet.cssRules).filter(r => /contact/.test(r.cssText) && !r.media).length === 0, 'contact layout rules apply on screen or in print only');
  apply(p, false);
  assert.deepEqual(cells.map(c => c.innerHTML), before, 'Original restores every cell');
  assert.equal(p.d.querySelector('.univer-app-copy-status'), null);
  assert.equal(phone.parentElement.tagName, 'SPAN');
  p.close();
});

test('the copy button falls back to a selected textarea when the clipboard refuses', async () => {
  let selected = null;
  const p = page(syllabus, 'https://univer.kaznu.kz/student/interactive/syllabus/print/12/3', w => {
    Object.defineProperty(w.navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('denied')) }, configurable: true });
    w.document.execCommand = command => { const area = w.document.querySelector('textarea'); selected = command === 'copy' && area ? area.value : null; return true; };
  });
  apply(p);
  const button = p.d.querySelector('button.univer-app-copy[data-univer-kind=phone]');
  button.focus(); button.click(); await new Promise(r => setTimeout(r, 0));
  assert.equal(selected, '+7 (701) 234-56-78');
  assert.equal(button.textContent, 'Скопировано');
  assert.equal(p.d.querySelector('textarea'), null, 'the textarea is removed');
  assert.equal(p.d.activeElement, button, 'focus returns to the button');
  p.close();
});

test('other printed documents and pages get no contact controls', () => {
  const p = page(syllabus, 'https://univer.kaznu.kz/student/transcript/print/1');
  apply(p);
  assert.equal(p.d.querySelectorAll('.univer-app-copy,.univer-app-contact-link').length, 0);
  p.close();
});
