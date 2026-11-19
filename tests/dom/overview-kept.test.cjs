const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {JSDOM} = require('jsdom');

const read = name => fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'Univer.Desktop', 'Assets', name), 'utf8');
const KEY = 'univer.desktop.overview.v1', MARK = 'univer.app.account.v1';

// The site's pages the panels read, all synthetic. The week of Monday, 9 November 2026.
const cell = (name, room) => `<td><p>${name} (лекция)</p><p>Синтетический П. П.</p><p>Ауд.: ${room}</p></td>`;
const schedule = monday => `<html><body><select id="weekSelect"><option selected value="/student/myschedule/2026/1/09.11.2026/15.11.2026/">09.11.2026 - 15.11.2026</option></select>
<table class="schedule"><tr><th>Время</th><th>Пн</th><th>Вт</th><th>Ср</th><th>Чт</th><th>Пт</th><th>Сб</th></tr>
<tr><td>09:00-10:00</td>${monday}<td></td><td></td><td></td><td></td><td></td></tr>
<tr><td>12:00-13:00</td><td></td>${cell('Культурология', '101')}<td></td><td></td><td></td><td></td></tr></table></body></html>`;
const grades = score => `<html><body><div id="l4"><table><tr><td class="ct">Политология 01.09.2026 - 15.12.2026</td></tr>
<tr><td class="ct"><b>РК1 (100): ${score} РК2 (100): 60</b></td></tr></table></div></body></html>`;
const statements = `<html><body><div id="l4"><table><tr><th>Услуга</th><th>Дата</th><th>Статус</th></tr>
<tr id="123"><td>Синтетическая справка</td><td>05.11.2026 10:00:00</td><td>Выполнено</td></tr></table></div></body></html>`;
const SITE = {'/student/myschedule/': schedule(cell('Политология', '305')), '/student/attendance/full/': grades(51), '/ssc/StatementList': statements};

// One visit of Бакалавр (or the Е-услуги desk) at a chosen moment, with what the profile's storage held before it.
// hold: the site's pages do not answer until open() is called. down: they fail.
function visit(options) {
  const o = Object.assign({path: '/student/bachelor/', at: [2026, 10, 9, 8, 30], mark: 'a1', storage: null, language: 'ru', pages: SITE}, options);
  const body = o.path === '/ssc/' ? '<div id="l4"><div class="univer-app-ssc-nav"></div><a id="out" href="/user/logout">Выход</a></div>' : '<div id="l4"><table class="mt"><tr><td>x</td></tr></table><a id="out" href="/user/logout">Выход</a><a id="in" href="/student/umkd/">УМКД</a></div>';
  const dom = new JSDOM('<body>' + body + '</body>', {url: 'https://univer.kaznu.kz' + o.path, runScripts: 'outside-only', pretendToBeVisual: true});
  const w = dom.window, RealDate = w.Date, calls = [];
  w.Date = class extends RealDate { constructor(...args) { super(...(args.length ? args : o.at)); } static now() { return new RealDate(...o.at).getTime(); } };
  if (o.mark) w.localStorage.setItem(MARK, o.mark);
  if (o.storage != null) w.localStorage.setItem(KEY, o.storage);
  if (o.keep === false) w.__univerOverviewKeep = false;
  let open; const gate = new Promise(resolve => { open = resolve; });
  w.fetch = async url => {
    calls.push(String(url));
    if (o.hold) await gate;
    if (o.down || !(url in o.pages)) { if (url in SITE || o.down) throw new Error('offline'); return {status: 200, url: 'https://univer.kaznu.kz' + url, text: async () => '<html><body></body></html>'}; }
    return {status: 200, url: 'https://univer.kaznu.kz' + url, text: async () => o.pages[url]};
  };
  w.eval(read('page-common.js')); w.eval(read('overview.js'));
  w.__univerOverviewV1.apply(o.language);
  const settle = async () => { for (let i = 0; i < 12; i++) await new Promise(resolve => setImmediate(resolve)); };
  const $ = selector => w.document.querySelector(selector);
  return {
    w, calls, settle, open: async () => { open(); await settle(); }, close: () => w.close(),
    stored: () => w.localStorage.getItem(KEY),
    lessons: () => Array.from(w.document.querySelectorAll('.univer-app-overview-today .univer-app-lesson-name')).map(n => n.textContent),
    scores: () => Array.from(w.document.querySelectorAll('.univer-app-overview-grades .univer-app-overview-courses li')).map(n => n.textContent.replace(/\s+/g, ' ').trim()),
    requests: () => Array.from(w.document.querySelectorAll('.univer-app-overview-requests-panel a.univer-app-overview-request')).map(n => n.getAttribute('href')),
    pending: cls => !!$('.' + cls + ' .univer-app-pending'), text: cls => $('.' + cls + ' .univer-app-overview-body').textContent, node: selector => $(selector)
  };
}
// A first visit that read the site and left its record.
async function record(options) {
  const p = visit(options); await p.settle();
  const stored = p.stored(); p.close();
  return stored;
}

test('the next visit shows the last one\'s classes and grades before the site answers, and what the site confirms is not drawn again', async () => {
  const first = visit();
  try {
    assert.ok(first.pending('univer-app-overview-today') && first.pending('univer-app-overview-grades'), 'nothing is kept before the first visit');
    await first.settle();
    assert.deepEqual(first.lessons(), ['Политология']);
    const kept = JSON.parse(first.stored());
    assert.equal(kept.owner, 'a1'); assert.equal(kept.language, 'ru');
    assert.ok(!/[<>]/.test(first.stored()), 'only parsed text is kept, never markup');
  } finally { first.close(); }
  const storage = await record();
  const p = visit({storage, hold: true});
  try {
    assert.deepEqual(p.lessons(), ['Политология'], 'classes are shown while the site is still silent');
    assert.match(p.scores()[0], /Политология.*51/);
    assert.ok(!p.pending('univer-app-overview-today') && !p.pending('univer-app-overview-grades'));
    const list = p.node('.univer-app-overview-courses'), lesson = p.node('.univer-app-overview-lesson');
    await p.open();
    assert.equal(p.node('.univer-app-overview-courses'), list, 'the same grades are not drawn again');
    assert.equal(p.node('.univer-app-overview-lesson'), lesson, 'the same classes are not drawn again');
    assert.equal(p.node('.univer-app-overview-stale'), null);
  } finally { p.close(); }
});

test('what the site says replaces what was kept', async () => {
  const storage = await record();
  const p = visit({storage, hold: true, pages: Object.assign({}, SITE, {'/student/attendance/full/': grades(77), '/student/myschedule/': schedule(cell('Психология', '12'))})});
  try {
    assert.match(p.scores()[0], /51/); assert.deepEqual(p.lessons(), ['Политология']);
    await p.open();
    assert.match(p.scores()[0], /77/); assert.doesNotMatch(p.scores()[0], /51/);
    assert.deepEqual(p.lessons(), ['Психология']);
    assert.match(p.stored(), /Психология/); assert.doesNotMatch(p.stored(), /Политология \(лекция\)/);
  } finally { p.close(); }
});

test('when the site cannot be read, kept grades stay and say when they were read; the schedule does not pretend', async () => {
  const storage = await record({at: [2026, 10, 9, 8, 5]});
  for (const [language, note] of [['ru', 'Сохранено 9 ноября, 08:05. Обновить не удалось.']]) {
    const p = visit({storage, down: true, language});
    try {
      await p.settle();
      assert.match(p.scores()[0], /51/);
      assert.equal(p.node('.univer-app-overview-grades .univer-app-overview-stale').textContent, note);
      assert.deepEqual(p.lessons(), []); assert.match(p.text('univer-app-overview-today'), /Не удалось загрузить/);
      assert.equal(JSON.parse(p.stored()).grades.list[0].scores[0].value, '51', 'a failed read does not erase the record');
    } finally { p.close(); }
  }
  // Nothing kept: the old message.
  const bare = visit({down: true});
  try { await bare.settle(); assert.match(bare.text('univer-app-overview-grades'), /Не удалось загрузить/); assert.equal(bare.stored(), null); } finally { bare.close(); }
});

test('the note is written in the chosen language', async () => {
  for (const [language, note] of [['kz', '9 қараша, 08:05 сақталған. Жаңарту мүмкін болмады.'], ['en', 'Saved 9 November, 08:05. Could not refresh.']]) {
    const storage = await record({at: [2026, 10, 9, 8, 5], language});
    const p = visit({storage, down: true, language});
    try { await p.settle(); assert.equal(p.node('.univer-app-overview-grades .univer-app-overview-stale').textContent, note); } finally { p.close(); }
  }
});

test('a record is shown only to the account, in the language and for the week it was made for', async () => {
  const storage = await record();
  const shows = (options, what) => { const p = visit(Object.assign({storage, hold: true}, options)); try { return what ? what(p) : [p.lessons().length, p.scores().length]; } finally { p.close(); } };
  assert.deepEqual(shows({}), [1, 1]);
  // Another account signed in here: nothing is shown and the record is gone at once.
  assert.deepEqual(shows({mark: 'b2'}, p => [p.lessons().length, p.scores().length, p.stored()]), [0, 0, null]);
  // No account mark (a profile that never saw the sign-in form): not read, and nothing is written either.
  assert.deepEqual(shows({mark: ''}), [0, 0]);
  assert.equal(await record({mark: ''}), null);
  assert.deepEqual(shows({language: 'en'}), [0, 0]);
  // The following Monday: last week's classes are not today's; grades are still within their two weeks.
  assert.deepEqual(shows({at: [2026, 10, 16, 10, 30]}), [0, 1]);
  assert.deepEqual(shows({at: [2026, 10, 24, 10, 30]}), [0, 0], 'grades older than two weeks are not shown');
  assert.deepEqual(shows({at: [2026, 10, 8, 10, 30]}), [0, 0], 'a record from the future is not trusted');
  // A week the site starts on Wednesday ends with its own Sunday: the next Monday is not within it.
  const midweek = JSON.parse(storage); midweek.schedule.start += 2 * 86400000;
  assert.deepEqual(shows({storage: JSON.stringify(midweek), at: [2026, 10, 16, 8, 30]}), [0, 1]);
  const endless = JSON.parse(storage); delete endless.schedule.end;
  assert.deepEqual(shows({storage: JSON.stringify(endless)}), [0, 1], 'a week without its end is not shown');
  // A day without classes left answers from the same week: Tuesday's class.
  assert.deepEqual(shows({at: [2026, 10, 9, 18, 0]}, p => [p.lessons(), /Ближайшие пары · Вторник, 10 ноября/.test(p.text('univer-app-overview-today'))]), [['Культурология'], true]);
  // Damaged or oversized records are dropped, never drawn.
  for (const bad of ['{', '[]', JSON.stringify({owner: 'a1', language: 'ru', grades: {at: 1, list: 'x'}}), JSON.stringify({owner: 'a1', language: 'ru', pad: 'x'.repeat(200001)})])
    assert.deepEqual(shows({storage: bad}), [0, 0]);
  const wrong = JSON.parse(storage); wrong.schedule.days[0][0].parts.name = {toString: 1};
  assert.deepEqual(shows({storage: JSON.stringify(wrong)}), [0, 1], 'a class that is not text is not drawn');
});

test('the browser extension keeps and reads nothing', async () => {
  assert.equal(await record({keep: false}), null);
  const storage = await record();
  const p = visit({storage, hold: true, keep: false});
  try { assert.deepEqual([p.lessons().length, p.scores().length], [0, 0]); assert.equal(p.stored(), storage, 'the app\'s record is left alone'); } finally { p.close(); }
});

test('requests on the Е-услуги desk are kept the same way, and only the site\'s own request addresses are opened', async () => {
  const storage = await record({path: '/ssc/'});
  assert.deepEqual(JSON.parse(storage).requests.list.map(r => r.href), ['/ssc/StatementPage?statementId=123']);
  const p = visit({path: '/ssc/', storage, hold: true});
  try {
    assert.deepEqual(p.requests(), ['/ssc/StatementPage?statementId=123']);
    const link = p.node('a.univer-app-overview-request');
    await p.open();
    assert.equal(p.node('a.univer-app-overview-request'), link);
  } finally { p.close(); }
  for (const href of ['https://example.com/', 'javascript:alert(1)', '/ssc/StatementPage?statementId=1&x=2', '//example.com/ssc/StatementList']) {
    const bad = JSON.parse(storage); bad.requests.list[0].href = href;
    const q = visit({path: '/ssc/', storage: JSON.stringify(bad), hold: true});
    try { assert.deepEqual(q.requests(), [], href); assert.ok(q.pending('univer-app-overview-requests-panel')); } finally { q.close(); }
  }
});

test('the site\'s sign-out link removes the record; other links leave it', async () => {
  const storage = await record();
  const p = visit({storage, hold: true});
  try {
    p.w.document.addEventListener('click', event => event.preventDefault());
    p.node('#in').click(); assert.equal(p.stored(), storage);
    p.node('#out').click(); assert.equal(p.stored(), null);
  } finally { p.close(); }
});

// What the schedule page keeps of the week's independent work (syllabus-tasks.js) for the week of 9 November.
const WORK = {course: 'Политология', href: '/student/umkd/7001/2026/1', name: 'СРО 1', topic: 'Эссе о разделении властей', score: 15.5};
const withWork = (list, options) => {
  const o = Object.assign({start: new Date(2026, 10, 9).getTime(), end: new Date(2026, 10, 16).getTime(), at: new Date(2026, 10, 8, 20, 0).getTime(), language: 'ru', owner: 'a1'}, options);
  return JSON.stringify({owner: o.owner, language: o.language, work: {at: o.at, start: o.start, end: o.end, list}});
};
const work = p => { const box = p.node('.univer-app-overview-work'); return box && Array.from(box.querySelectorAll('.univer-app-overview-work-list>li')).map(li => li.textContent); };

test('the week\'s independent work kept by the schedule page is listed on Бакалавр while that week lasts', async () => {
  const p = visit({storage: withWork([WORK, Object.assign({}, WORK, {course: 'История', href: '/student/umkd/7005/2026/1', name: '', score: 0, topic: ''})]), hold: true});
  try {
    assert.deepEqual(work(p), ['ПолитологияСРО 1 · макс. балл: 15,5Эссе о разделении властей', 'История']);
    assert.equal(p.node('.univer-app-overview-work a.univer-app-overview-course-link').getAttribute('href'), '/student/umkd/7001/2026/1');
    assert.equal(p.node('.univer-app-overview-work .univer-app-overview-caption').textContent, '9–15 ноября');
    assert.equal(p.node('.univer-app-overview-work .univer-app-overview-stale').textContent, 'Прочитано в расписании 8 ноября, 20:00');
    assert.equal(p.node('.univer-app-overview-grid').nextElementSibling, p.node('.univer-app-overview-work'), 'under the two panels');
    await p.open();
    assert.equal(work(p).length, 2, 'the site\'s pages do not replace it');
    assert.equal(JSON.parse(p.stored()).work.list.length, 2, 'reading the schedule and grades keeps it');
    p.w.__univerOverviewV1.release(); assert.equal(p.node('.univer-app-overview-work'), null, 'Original takes it away');
  } finally { p.close(); }
  const shows = options => { const q = visit(Object.assign({hold: true}, options)); try { return work(q); } finally { q.close(); } };
  const across = visit({hold: true, at: [2026, 11, 1, 9, 0], language: 'en', storage: withWork([WORK], {language: 'en', start: new Date(2026, 10, 30).getTime(), end: new Date(2026, 11, 7).getTime()})});
  try {
    assert.equal(across.node('.univer-app-overview-work .univer-app-overview-caption').textContent, '30 November – 6 December');
    assert.match(work(across)[0], /СРО 1 · max score: 15\.5/);
  } finally { across.close(); }
  assert.equal(shows({storage: withWork([WORK]), at: [2026, 10, 16, 8, 0]}), null, 'the next week does not show it');
  assert.equal(shows({storage: withWork([WORK]), language: 'kz'}), null, 'another language does not');
  assert.equal(shows({storage: withWork([WORK], {owner: 'b2'})}), null, 'another account does not');
  assert.equal(shows({storage: withWork([])}), null, 'a week without work shows no panel');
  assert.equal(shows({storage: withWork([WORK]), keep: false}), null, 'the extension reads nothing');
  for (const bad of [{href: 'https://example.com/'}, {href: '/student/bachelor/'}, {score: -1}, {topic: 'x'.repeat(1201)}, {course: {}}])
    assert.equal(shows({storage: withWork([Object.assign({}, WORK, bad)])}), null, JSON.stringify(bad));
  assert.equal(shows({storage: withWork([WORK], {end: new Date(2026, 10, 30).getTime()})}), null, 'a "week" longer than seven days');
});

test('the schedule page writes the week\'s work beside the panels\' record, for its account and language only', async () => {
  const storage = await record();
  const p = visit({storage, hold: true});
  try {
    const api = p.w.__univerOverviewV1, part = {start: new Date(2026, 10, 9).getTime(), end: new Date(2026, 10, 16).getTime(), list: [WORK]};
    api.keepWork('ru', part);
    const kept = JSON.parse(p.stored());
    assert.equal(kept.work.list[0].topic, WORK.topic); assert.ok(kept.grades && kept.schedule, 'the other panels stay');
    api.keepWork('ru', Object.assign({}, part, {list: [Object.assign({}, WORK, {href: 'javascript:alert(1)'})]}));
    assert.equal(JSON.parse(p.stored()).work.list[0].href, WORK.href, 'an invalid list is not written');
    api.keepWork('de', part); assert.equal(JSON.parse(p.stored()).language, 'ru');
    // Written in another language, the record starts again in that language.
    api.keepWork('en', part); assert.deepEqual(Object.keys(JSON.parse(p.stored())).sort(), ['language', 'owner', 'work']);
  } finally { p.close(); }
  const bare = visit({mark: '', hold: true});
  try { bare.w.__univerOverviewV1.keepWork('ru', {start: 1, end: 2, list: []}); assert.equal(bare.stored(), null, 'no account mark, nothing written'); } finally { bare.close(); }
});
