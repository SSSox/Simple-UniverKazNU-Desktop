const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {JSDOM} = require('jsdom');

const read = name => fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'Univer.Desktop', 'Assets', name), 'utf8');

// The exam or midterm schedule page at 12:00 on Monday, 23 November 2026, with a heading table above the list.
function page(pathname, list) {
  const html = '<body><table><tr><td><table id="head"><tr><th>Дисциплина</th><th>Преподаватель</th><th>Тип</th><th>Аудитория</th></tr></table>' +
    '<table id="scheduleList">' + list + '</table></td></tr></table></body>';
  const dom = new JSDOM(html, {url: 'https://univer.kaznu.kz' + pathname, runScripts: 'outside-only'});
  const w = dom.window, RealDate = w.Date;
  w.Date = class extends RealDate { constructor(...args) { super(...(args.length ? args : [2026, 10, 23, 12, 0])); } static now() { return new RealDate(2026, 10, 23, 12, 0).getTime(); } };
  w.eval(read('page-common.js')); w.eval(read('overview.js'));
  return w;
}
const rows = '<tr><th colspan="4">15.12.2026 13:00</th></tr>' +
  '<tr><td>Алгебра</td><td>Синтетический П. П.</td><td>Экзамен <i>(письменно)</i></td><td>Корпус: ФИТ | Ауд.: 309</td></tr>' +
  '<tr><td>Алгебра</td><td>Синтетический П. П.</td><td>Консультация</td><td>Ауд.: 100</td></tr>' +
  '<tr id="16.12.2026 09:30"><td>Физика</td><td></td><td>Экзамен</td><td></td></tr>' +
  '<tr><th colspan="4">20.11.2026 10:00</th></tr>' +
  '<tr><td>Прошедший</td><td></td><td>Экзамен</td><td></td></tr>';

test('an exam page gives its coming exams as local times, without consultations and past ones', () => {
  const w = page('/student/myexam/schedule/', rows);
  try {
    assert.deepEqual(JSON.parse(w.__univerOverviewV1.exams()), {v: 1, kind: 'exam', items: [
      {kind: 'exam', subject: 'Алгебра', start: '2026-12-15T13:00', place: 'ФИТ · 309'},
      {kind: 'exam', subject: 'Физика', start: '2026-12-16T09:30', place: ''}]});
  } finally { w.close(); }
});

test('a midterm page names its kind', () => {
  const w = page('/student/mymidterm/schedule/', rows);
  try { assert.equal(JSON.parse(w.__univerOverviewV1.exams()).kind, 'midterm'); } finally { w.close(); }
});

test('only the two pages with a list answer, and an empty-note page gives an empty list', () => {
  const other = page('/student/myschedule/', rows);
  try { assert.equal(other.__univerOverviewV1.exams(), ''); } finally { other.close(); }
  const empty = page('/student/myexam/schedule/', '<tr><td colspan="4">Расписание отсутствует</td></tr>');
  try { assert.deepEqual(JSON.parse(empty.__univerOverviewV1.exams()), {v: 1, kind: 'exam', items: []}); } finally { empty.close(); }
  const unread = page('/student/myexam/schedule/', '');
  try { assert.equal(unread.__univerOverviewV1.exams(), ''); } finally { unread.close(); }
});
