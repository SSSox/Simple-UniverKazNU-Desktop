const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM, VirtualConsole } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const bundle = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'study-pages.js', 'course-tasks.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'notices.js', 'univer-layout.js', 'desktop.js'].map(read);

// The menu count stays red. Answer marks are on detail pages, with one row per teacher or topic.
const menu = (count = '<b style="color:Red; font-size:10px;">&nbsp;(2)</b>') => '<table id="tools"><tr><td class="center"><table class="tool"><tr><td><ul>' +
  `<li><a href="/student/rating/">Анкетирование студентов</a>${count}</li>` +
  '<li><a href="/student/contract/" style="color:red">Договоры</a></li><li><a href="/student/group/">Группа (2)</a></li>' +
  '<li><a href="/student/umkd/">УМКД</a></li><li><a href="/user/logout">Выход</a></li></ul></td></tr></table></td></tr></table>';
const tabs = '<tr id="l3"><td class="center"><a href="/student/bachelor/"><span class="center">Бакалавр</span></a><a href="/ssc/"><span class="center">Е-услуги</span></a><a href="/">Главная</a></td></tr>';
const shell = (content, count) => '<table class="wrappertable"><tr id="l1"><td class="header">КазНУ</td></tr><tr id="l2"><td></td></tr>' + tabs +
  `<tr id="l4"><td class="center">${menu(count)}${content}</td></tr></table>`;
const rows = '<table class="inner"><tr><th>Название</th><th>Тип</th><th>Начало</th><th>Окончание</th></tr>' +
  '<tr class="link" id="208"><td>Удовлетворенность обучающихся качеством преподавания</td><td>Анкетирование преподавателя</td><td>30.10.2026</td><td>30.11.2026</td></tr>' +
  '<tr class="link" id="207"><td>Восприятие коррупции - 2026, осенний сем.</td><td>Анкетирование</td><td>29.10.2026</td><td>11.11.2026</td></tr></table>';
const answers = (...marks) => shell('<table class="inner">' + marks.map(mark =>
  `<tr class="link${mark === 'done' ? ' done' : ''}"><td>Дисциплина</td><td>Преподаватель</td><td>${mark === 'done' ? '' : mark}</td><td></td></tr>`).join('') + '</table>');
const routesFor = (...marks) => ({ '/student/rating/': shell(rows), '/student/rating/208': answers('done', '<img src="/content/icons/add.png" alt=" [+] ">'),
  '/student/rating/207': answers(...marks) });
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
  w.__univerNative = value => sent.push(value);
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
const scan = (p, fetch = false, refresh = 0) => JSON.parse(p.w.__univerNoticesV1.scan(fetch, 'n1', refresh));
const rating = result => result.links.find(link => link.u === '/student/rating/');
const messages = p => p.sent.map(value => { assert.ok(value.startsWith('univer-notices:')); return JSON.parse(value.slice('univer-notices:'.length)); });
const checkedRating = p => messages(p).flatMap(message => message.links || []).find(link => link.u === '/student/rating/' && link.n > 0 && !link.c);
const badge = p => p.d.querySelector('#tools li > b');

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

test('a questionnaire stays open if any of its detail rows is unanswered', async () => {
  const p = page(shell(rows), 'https://univer.kaznu.kz/student/rating/', { routes: routesFor('done', '') });
  const fetch = p.w.fetch;
  let release;
  const firstDetail = new Promise(resolve => { release = resolve; });
  p.w.fetch = async (address, options) => {
    assert.equal(options.method || 'GET', 'GET');
    assert.equal(options.mode, 'same-origin');
    const response = await fetch(address);
    if (address.endsWith('/208')) await firstDetail;
    return response;
  };
  p.w.__univerNoticesV1.present(true);
  const initial = rating(scan(p));
  assert.equal(initial.n, 2, 'the site keeps its count after the answers');
  assert.equal(initial.c, 1);
  assert.equal(initial.o, undefined);
  await settle();
  assert.deepEqual(p.requests, ['/student/rating/208']);
  assert.deepEqual(p.sent, [], 'no count is sent while a detail page is still pending');
  release();
  await settle();
  assert.deepEqual(p.requests, ['/student/rating/208', '/student/rating/207'], 'the open list is reused');
  const checked = checkedRating(p);
  assert.equal(checked.o, 1);
  assert.deepEqual(checked.d, [{ t: 'Восприятие коррупции - 2026, осенний сем.', e: '11.11.2026' }]);
  assert.equal(badge(p).textContent.trim(), '(1)');
  p.w.close();
});

test('the extension removes old saved questionnaire counts, checks once on the page, and restores the original badge', async () => {
  const routes = routesFor('&nbsp;+&nbsp;');
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { routes, scripts: bundle });
  delete p.w.__univerNative;
  const original = badge(p), originalText = original.firstChild, originalMarkup = original.outerHTML;
  const key = 'univer.desktop.survey-count.v1';
  p.w.localStorage.setItem(key, JSON.stringify({ owner: 'a1', count: 2, open: 1 }));
  p.w.localStorage.setItem('univer.app.account.v1', 'a1');
  let change;
  p.w.chrome = { storage: { local: { get: async () => ({}) }, onChanged: { addListener: listener => { change = listener; } } } };
  p.w.matchMedia = () => ({ matches: false, addEventListener() {} });
  for (const script of ['prelude.js', 'host.js']) p.w.eval(fs.readFileSync('src/Univer.Extension/scripts/' + script, 'utf8'));
  await settle();
  assert.equal(original.hidden, true);
  assert.deepEqual(p.sent, []);
  assert.equal(p.w.localStorage.getItem(key), null);
  p.w.__univerNoticesV1.check();
  assert.deepEqual(p.requests, ['/student/rating/', '/student/rating/208', '/student/rating/207']);
  change({ settings: { newValue: { readable: false } } }, 'local');
  assert.equal(badge(p), original);
  assert.equal(original.firstChild, originalText);
  assert.equal(original.outerHTML, originalMarkup);
  p.w.close();
});

test('a verified desktop result is reused across pages until the menu count changes, it expires, or refresh is requested', async () => {
    const routes = routesFor(''), url = 'https://univer.kaznu.kz/student/rating/';
    const p = page(shell(rows), url, { routes }), warm = page(shell(rows), url, { routes });
    const key = 'univer.desktop.survey-count.v1', account = 'univer.app.account.v1';
    const details = ['/student/rating/208', '/student/rating/207'];
    try {
        let now = 1000000;
        p.w.Date.now = warm.w.Date.now = () => now;
        p.w.localStorage.setItem(account, 'a1');
        scan(p);
        await settle();
        assert.deepEqual(p.requests, details);
        warm.w.localStorage.setItem(key, p.w.localStorage.getItem(key));
        warm.w.localStorage.setItem(account, 'b2');
        warm.w.__univerNoticesV1.present(true);
        assert.equal(badge(warm).textContent.trim(), '(2)', 'the saved check belongs to its account');
        warm.w.localStorage.setItem(account, 'a1');
        const reused = rating(scan(warm));
        assert.equal(reused.o, 1);
        assert.deepEqual(reused.d, checkedRating(p).d);
        assert.deepEqual(warm.requests, []);

        badge(warm).firstChild.data = '(3)';
        assert.equal(rating(scan(warm)).c, 1);
        await settle();
        assert.deepEqual(warm.requests.splice(0), details);
        assert.equal(rating(scan(warm)).o, 1);

        badge(warm).firstChild.data = '(0)';
        assert.equal(rating(scan(warm)).n, 0);
        assert.equal(warm.w.localStorage.getItem(key), null);
        badge(warm).firstChild.data = '(3)';
        assert.equal(rating(scan(warm)).c, 1, 'a count returning after zero needs another read');
        await settle();
        assert.deepEqual(warm.requests.splice(0), details);

        now += 3 * 60 * 1000;
        assert.equal(rating(scan(warm)).c, 1);
        await settle();
        assert.deepEqual(warm.requests.splice(0), details);
        assert.equal(rating(scan(warm)).o, 1);
        scan(warm, false, 1);
        await settle();
        assert.deepEqual(warm.requests, details);
        assert.equal(messages(warm).at(-1).refresh, 1);
    } finally { p.w.close(); warm.w.close(); }
});

test('a refresh queued during a survey check reads again and has its own completion', async () => {
    const p = page(shell(rows), 'https://univer.kaznu.kz/student/rating/', { routes: routesFor('') });
    try {
        const fetch = p.w.fetch;
        let releaseFirst, releaseRefresh, reads = 0;
        const first = new Promise(resolve => { releaseFirst = resolve; });
        const refresh = new Promise(resolve => { releaseRefresh = resolve; });
        p.w.fetch = async (address, options) => {
            const response = await fetch(address, options);
            if (address.endsWith('/208')) await (++reads === 1 ? first : refresh);
            return response;
        };
        assert.equal(scan(p).fetching, true);
        assert.equal(scan(p, false, 7).fetching, true);
        assert.deepEqual(p.requests, ['/student/rating/208']);
        releaseFirst();
        await settle();
        assert.deepEqual(p.requests, ['/student/rating/208', '/student/rating/207', '/student/rating/208']);
        assert.equal(messages(p).filter(message => message.kind === 'done').length, 1);
        assert.equal(messages(p).some(message => message.kind === 'done' && message.refresh === 7), false);
        releaseRefresh();
        await settle();
        const done = messages(p).at(-1);
        assert.deepEqual([done.kind, done.refresh, done.read, done.failed], ['done', 7, 2, 0]);
    } finally { p.w.close(); }
});

test('an unreadable questionnaire restores the site count and leaves the details unknown', async () => {
  const routes = routesFor('done');
  delete routes['/student/rating/207'];
  const p = page(shell(rows), 'https://univer.kaznu.kz/student/rating/', { routes });
  p.w.localStorage.setItem('univer.app.account.v1', 'a1');
  p.w.localStorage.setItem('univer.desktop.survey-count.v1', JSON.stringify({ owner: 'a1', count: 2, open: 0, details: [], at: p.w.Date.now() }));
  p.w.__univerNoticesV1.present(true);
  assert.equal(badge(p).hidden, true);
  scan(p, false, 1);
  await settle();
  const checked = checkedRating(p);
  assert.equal(checked.n, 2);
  assert.equal(checked.o, undefined);
  assert.deepEqual(checked.d, []);
  assert.equal(badge(p).hidden, false);
  assert.equal(badge(p).textContent.trim(), '(2)');
  routes['/student/rating/207'] = answers('');
  scan(p);
  await settle();
  assert.equal(messages(p).filter(message => message.kind === 'section').at(-1).links[0].o, 1, 'an inconclusive check is retried');
  p.w.close();
});

test('a slow survey check uses one read deadline for the whole list', async () => {
  const p = page(shell(rows), 'https://univer.kaznu.kz/student/rating/', { routes: routesFor('') });
  const fetch = p.w.fetch;
  let time = 0;
  p.w.performance.now = () => time;
  p.w.fetch = address => { time = 15001; return fetch(address); };
  scan(p);
  await settle();
  assert.deepEqual(p.requests, ['/student/rating/208']);
  assert.equal(checkedRating(p).o, undefined);
  assert.equal(checkedRating(p).n, 2);
  p.w.close();
});

test('a questionnaire row without an id makes the check inconclusive', async () => {
  const p = page(shell(rows.replace(' id="207"', '')), 'https://univer.kaznu.kz/student/rating/', { routes: routesFor('done') });
  scan(p);
  await settle();
  assert.deepEqual(p.requests, []);
  assert.equal(checkedRating(p).n, 2);
  assert.equal(checkedRating(p).o, undefined);
  p.w.close();
});

test('ten questionnaires are checked in full, and a longer list falls back without a partial count', async () => {
  const list = count => '<table>' + Array.from({ length: count }, (_, i) => `<tr class="link" id="${i + 1}"><td>Анкета ${i + 1}</td><td>Тип</td></tr>`).join('') + '</table>';
  const routes = Object.fromEntries(Array.from({ length: 10 }, (_, i) => [`/student/rating/${i + 1}`, answers('')]));
  const exact = page(shell(list(10)), 'https://univer.kaznu.kz/student/rating/', { routes });
  exact.w.__univerNoticesV1.present(true);
  scan(exact);
  await settle();
  assert.equal(exact.requests.length, 10);
  assert.equal(checkedRating(exact).o, 10);
  assert.equal(checkedRating(exact).d.length, 10);
  assert.equal(badge(exact).textContent.trim(), '(10)');
  exact.w.close();
  const p = page(shell(list(11)), 'https://univer.kaznu.kz/student/rating/');
  scan(p);
  await settle();
  assert.deepEqual(p.requests, []);
  assert.equal(checkedRating(p).o, undefined);
  assert.equal(checkedRating(p).n, 2);
  p.w.close();
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
    ...routesFor('done')
  };
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { routes });
  const first = scan(p, true);
  assert.equal(first.fetching, true);
  assert.equal(scan(p, true).fetching, false, 'a second request waits for the running one');
  await settle();
  assert.deepEqual(p.requests, ['/student/rating/', '/student/rating/208', '/student/rating/207', '/ssc/']);
  const results = messages(p);
  assert.deepEqual(results.map(m => [m.kind, m.page, m.g]), [['section', '/student/bachelor/', 'n1'], ['section', '/ssc/', 'n1'], ['done', '/student/bachelor/', 'n1']]);
  assert.equal(rating(results[1]).n, 0, 'the other section lists the link without a count');
  assert.equal(checkedRating(p).o, 0, 'the detail reads find both questionnaires answered');
  assert.deepEqual([results[2].read, results[2].failed], [4, 0]);
});

test('a page that fails to load is counted in the last message, and the other pages are still read', async () => {
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { routes: routesFor('') });
  scan(p, true);
  await settle();
  const messages = p.sent.map(value => JSON.parse(value.slice('univer-notices:'.length)));
  assert.equal(checkedRating(p).o, 1);
  const done = messages[messages.length - 1];
  assert.deepEqual([done.kind, done.read, done.failed], ['done', 3, 1], '/ssc/ answered 404');
});

test('the reading layout keeps the menu count and the section tabs readable', () => {
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { scripts: bundle });
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.ok(p.d.querySelector('.univer-app-tabs a[href="/ssc/"]'), 'the tabs moved into the app header');
  const result = scan(p);
  assert.equal(rating(result).n, 2);
  assert.equal(result.menu, true);
});

test('background reads route through boundedText with response charset and cancel oversized streams at 3 MB', async () => {
  // Windows-1251 encoded shell for /ssc/ containing "Анкетирование" with no count.
  const winBytes = new Uint8Array([0x3c,0x74,0x61,0x62,0x6c,0x65,0x20,0x69,0x64,0x3d,0x22,0x74,0x6f,0x6f,0x6c,0x73,0x22,0x3e,0x3c,0x74,0x72,0x3e,0x3c,0x74,0x64,0x3e,0x3c,0x75,0x6c,0x3e,0x3c,0x6c,0x69,0x3e,0x3c,0x61,0x20,0x68,0x72,0x65,0x66,0x3d,0x22,0x2f,0x73,0x74,0x75,0x64,0x65,0x6e,0x74,0x2f,0x72,0x61,0x74,0x69,0x6e,0x67,0x2f,0x22,0x3e,0xc0,0xed,0xea,0xe5,0xf2,0xe8,0xf0,0xee,0xe2,0xe0,0xed,0xe8,0xe5,0x3c,0x2f,0x61,0x3e,0x3c,0x2f,0x6c,0x69,0x3e,0x3c,0x2f,0x75,0x6c,0x3e,0x3c,0x2f,0x74,0x64,0x3e,0x3c,0x2f,0x74,0x72,0x3e,0x3c,0x2f,0x74,0x61,0x62,0x6c,0x65,0x3e]);
  const p = page(shell('<p>Бакалавр</p>'), 'https://univer.kaznu.kz/student/bachelor/', { scripts: [read('page-common.js'), read('notices.js')] });
  let reads = 0, cancels = 0;
  const bigChunk = new Uint8Array(1024 * 1024);
  p.w.fetch = address => {
    p.requests.push(address);
    if (address === '/ssc/') {
      const pieces = [winBytes.slice(0, 40), winBytes.slice(40)];
      return Promise.resolve({
        ok: true, status: 200, url: 'https://univer.kaznu.kz' + address,
        headers: { get: name => /content-type/i.test(name) ? 'text/html; charset=windows-1251' : null },
        body: { getReader: () => ({ read: async () => pieces.length ? { value: pieces.shift(), done: false } : { done: true }, cancel: async () => {}, releaseLock() {} }) }
      });
    }
    return Promise.resolve({
      ok: true, status: 200, url: 'https://univer.kaznu.kz' + address,
      headers: { get: name => /content-type/i.test(name) ? 'text/html; charset=utf-8' : null },
      body: { getReader: () => ({ read: async () => { reads++; return { value: bigChunk, done: false }; }, cancel: async () => { cancels++; }, releaseLock() {} }) }
    });
  };
  scan(p, true);
  await settle();
  const messages = p.sent.map(value => JSON.parse(value.slice('univer-notices:'.length)));
  const ssc = messages.find(m => m.page === '/ssc/');
  assert.ok(ssc, 'windows-1251 streamed page parsed');
  assert.equal(rating(ssc).t, 'Анкетирование');
  assert.equal(reads, 3, 'oversized /student/rating/ stream stops at the 3rd 1 MB chunk (> 3000000 bytes)');
  assert.equal(cancels, 1, 'oversized stream is cancelled');
  const done = messages[messages.length - 1];
  assert.deepEqual([done.kind, done.read, done.failed], ['done', 1, 1]);
});
