const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {JSDOM} = require('jsdom');

const read = name => fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'Univer.Desktop', 'Assets', name), 'utf8');
const KEY = 'univer.desktop.overview.v1';

// The home page with the kept record of an earlier visit (this account's mark, a language, the grades panel's list).
function page(record, {mark = 'acc01', keep, before} = {}) {
  const dom = new JSDOM('<body></body>', {url: 'https://univer.kaznu.kz/student/bachelor/', runScripts: 'outside-only'});
  const w = dom.window;
  if (keep === false) w.__univerOverviewKeep = false;
  if (mark) w.localStorage.setItem('univer.app.account.v1', mark);
  if (record) w.localStorage.setItem(KEY, JSON.stringify(record));
  if (before) before(w);
  w.eval(read('page-common.js')); w.eval(read('overview.js'));
  return w;
}
const score = (label, max, value) => ({label, max, value});
const course = (name, ...scores) => ({name, token: name.toLowerCase(), scores});
function kept(list, at = Date.now() - 3600000, owner = 'acc01', language = 'ru') { return {owner, language, grades: {at, list}}; }

test('the kept grades panel gives its courses and their scores that have a value, with when it was read', () => {
  const at = Date.now() - 3600000;
  const w = page(kept([course('Алгебра', score('РК1', '100', '71'), score('РК2', '100', ''), score('Экзамен', '100', '')), course('Физика', score('РК1', '100', ''))], at));
  try {
    assert.deepEqual(JSON.parse(w.__univerOverviewV1.grades()), {v: 1, at, courses: [{name: 'Алгебра', scores: [{label: 'РК1', max: '100', value: '71'}]}]});
  } finally { w.close(); }
});

test('another language of the same account still gives the reading', () => {
  const w = page(kept([course('Algebra', score('RK1', '100', '55'))], Date.now() - 1000, 'acc01', 'en'));
  try { assert.equal(JSON.parse(w.__univerOverviewV1.grades()).courses[0].name, 'Algebra'); } finally { w.close(); }
});

test('no mark, another account, a stale or damaged record, or nothing graded gives nothing', () => {
  const list = [course('Алгебра', score('РК1', '100', '71'))];
  const cases = {
    'no mark': page(kept(list), {mark: ''}),
    'another account': page(kept(list, undefined, 'other')),
    'older than two weeks': page(kept(list, Date.now() - 15 * 86400000)),
    'from the future': page(kept(list, Date.now() + 3600000)),
    'more scores than the panel keeps': page(kept([course('Алгебра', ...Array.from({length: 9}, () => score('РК1', '100', '1')))])),
    'a long value': page(kept([course('Алгебра', score('РК1', '100', '7'.repeat(50)))])),
    'no value yet': page(kept([course('Алгебра', score('РК1', '100', ''))])),
    'no record': page(null),
    'damaged': page(null, {before: w => w.localStorage.setItem(KEY, '{not json')}),
  };
  try {
    for (const [name, w] of Object.entries(cases)) {
      assert.equal(w.__univerOverviewV1.grades(), '', name);
    }
  } finally { for (const w of Object.values(cases)) w.close(); }
});

test('what is given is bounded: at most 60 courses and 10 scores each, names cut at 200', () => {
  const list = Array.from({length: 70}, (_, i) => course((i === 0 ? 'Д'.repeat(300) : 'Курс ' + i), ...Array.from({length: 8}, (_, k) => score('РК' + (k % 2 + 1), '100', String(k + 1)))));
  const w = page(kept(list));
  try {
    // isCourses allows 80 courses and 8 scores; the reader trims to its own bounds.
    const got = JSON.parse(w.__univerOverviewV1.grades());
    assert.equal(got.courses.length, 60);
    assert.equal(got.courses[0].name.length, 200);
    assert.ok(got.courses.every(c => c.scores.length <= 10));
  } finally { w.close(); }
});

test('the browser extension keeps nothing, so it gives nothing', () => {
  const w = page(kept([course('Алгебра', score('РК1', '100', '71'))]), {keep: false});
  try { assert.equal(w.__univerOverviewV1.grades(), ''); } finally { w.close(); }
});
