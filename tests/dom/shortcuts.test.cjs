const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const scripts = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'univer-layout.js', 'desktop.js'].map(read);

function page(url) {
  const useful = ['https://lib.kaznu.kz/|Библиотека', '/user/loginMoodle|СДО Moodle', 'https://app.oqylyq.kz/|Oqylyq', 'https://open.kaznu.kz/|Open KazNU',
    'https://apps.apple.com/us/app/mobile-univer/id1|Univer (iOS)', 'https://play.google.com/store/apps/details?id=x|Univer (Android)',
    'https://omc.kaznu.kz/|Open Master Class', 'https://read.kz/|Читай'].map(v => v.split('|')).map(v => `<a href="${v[0]}" target="_blank">${v[1]}</a><br>`).join('');
  const html = '<table class="wrappertable">' +
    '<tr id="l1"><td class="header">КазНУ<a href="/lang/change/kz/"><img src="/img/kz.png"></a><img src="/img/ru.png"><a href="/lang/change/en/"><img src="/img/en.png"></a></td></tr>' +
    '<tr id="l2"><td><div id="logo"></div><div class="links"><a href="/user/pass/">Профиль</a><a href="/user/logout">Выход</a></div></td></tr>' +
    '<tr id="l4"><td><div id="menu-home"><table id="tools"><tr><td class="center"><table class="tool"><tr><td><ul id="study">' +
    '<li><a id="umkd" href="/student/umkd/">УМКД</a></li><li><a id="schedule" href="/student/myschedule/">Расписание</a></li>' +
    '<li><a href="/student/transcript/">Транскрипт</a></li><li><a href="/student/attendance/full/">Журнал посещаемости</a></li>' +
    '<li><a id="messages" href="/my/messages/index/"> Сообщения </a></li><li><a href="/user/logout">Выход</a></li></ul></td></tr>' +
    '<tr><td class="bottom-center">Учёба</td></tr></table></td></tr></table></div>' +
    '<h1>Новости</h1><div class="links" id="useful">' + useful + '</div></td></tr></table>';
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const w = dom.window, sent = [];
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.__univerNative = value => sent.push(value);
  scripts.forEach(script => w.eval(script));
  return { w, d: w.document, sent, close: () => w.close() };
}
const titles = p => Array.from(p.d.querySelectorAll('.univer-app-quick .univer-app-tile>a')).map(a => a.textContent);

test('stored tile order combines fixed and pinned links, retains nodes and focus, and survives language changes', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  const pins = [{title:'Библиотека',url:'https://lib.kaznu.kz/'}];
  p.w.__univerShortcutsV1.configure(pins);
  p.w.__univerDesktop.apply(true,false,'ru');
  const umkd = Array.from(p.d.querySelectorAll('.univer-app-tile')).find(t=>t.querySelector('a').textContent==='УМКД');
  const handle = umkd.querySelector('a'); handle.focus();
  const order = ['https://lib.kaznu.kz/','https://univer.kaznu.kz/student/umkd/'];
  p.w.__univerShortcutsV1.configure(pins, order);
  assert.deepEqual(titles(p).slice(0,2), ['Библиотека','УМКД']);
  assert.equal(p.d.querySelectorAll('.univer-app-tile')[1],umkd); assert.equal(p.d.activeElement,handle);
  handle.dispatchEvent(new p.w.KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true}));
  assert.deepEqual(p.sent,[]); assert.equal(titles(p)[1],'УМКД');
  p.w.__univerDesktop.apply(true,false,'en');
  assert.equal(p.d.querySelector('.univer-app-tile>a').href,order[0]);
  p.w.__univerDesktop.apply(false,false,'en'); assert.equal(p.d.querySelector('.univer-app-add-tile'),null);
  p.close();
});

test('home shows study tiles with СДО Moodle beside them, then pinned sections and links with unpin buttons', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  p.w.__univerShortcutsV1.configure([{ title: 'Сообщения', url: 'https://univer.kaznu.kz/my/messages/index/' }, { title: 'Библиотека', url: 'https://lib.kaznu.kz/' },
    { title: 'Расписание', url: 'https://univer.kaznu.kz/student/myschedule/' }]);
  p.w.__univerDesktop.apply(true, false, 'ru');
  // A pin that repeats a study tile is not shown twice.
  assert.deepEqual(titles(p), ['Расписание', 'Успеваемость', 'Транскрипт', 'УМКД', 'СДО Moodle', 'Е-услуги', 'Сообщения', 'Библиотека']);
  const moodle = Array.from(p.d.querySelectorAll('.univer-app-quick a')).find(a => a.textContent === 'СДО Moodle');
  assert.equal(new URL(moodle.href).pathname, '/user/loginMoodle'); assert.equal(moodle.target, '');
  const library = Array.from(p.d.querySelectorAll('.univer-app-quick a')).find(a => a.textContent === 'Библиотека');
  assert.equal(library.target, '_blank'); assert.equal(library.rel, 'noopener');
  assert.equal(p.d.querySelectorAll('.univer-app-tile.univer-app-pinned').length, 2);
  // A tile carries no handle or cross: it says how it is arranged, and the grid ends with New section.
  assert.equal(p.d.querySelector('.univer-app-quick button:not(.univer-app-add-tile)'), null);
  assert.equal(p.d.querySelector('.univer-app-tile>a').title, '');
  assert.equal(p.d.querySelector('.univer-app-tile>a').getAttribute('aria-description'), 'Зажмите плитку, чтобы переставить или удалить её');
  assert.equal(p.d.querySelector('.univer-app-quick').lastElementChild.className, 'univer-app-add-tile');
  assert.equal(p.d.querySelector('.univer-app-add-label').textContent, 'Новый раздел');
  assert.equal(p.d.querySelector('.univer-app-quick-hint'), null);
  // A tile for a site section uses the site's own link and handler.
  let clicks = 0; p.d.getElementById('messages').addEventListener('click', e => { e.preventDefault(); clicks++; });
  Array.from(p.d.querySelectorAll('.univer-app-quick a')).find(a => a.textContent === 'Сообщения').click();
  assert.equal(clicks, 1);
  p.close();
});

test('All sections lists useful links and marks pinned items; scripted clicks never pin', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  p.w.__univerShortcutsV1.configure([{ title: 'Сообщения', url: 'https://univer.kaznu.kz/my/messages/index/' }]);
  p.w.__univerDesktop.apply(true, false, 'ru');
  const box = p.d.querySelector('.univer-app-menu .univer-app-useful');
  assert.ok(box); assert.equal(box.closest('#tools'), null); assert.equal(box.querySelector('summary').textContent, 'Полезные ссылки');
  assert.equal(box.querySelectorAll('li>a').length, 8);
  assert.equal(p.d.getElementById('messages').nextElementSibling.getAttribute('aria-pressed'), 'true');
  // A home section carries one pin, pressed while its tile is on the screen.
  const umkdPin = p.d.getElementById('umkd').nextElementSibling;
  assert.equal(umkdPin.className, 'univer-app-pin'); assert.equal(umkdPin.getAttribute('aria-pressed'), 'true');
  assert.equal(umkdPin.parentElement.querySelectorAll('.univer-app-pin').length, 1);
  // Logout cannot be pinned.
  assert.equal(p.d.querySelector('#study a[href="/user/logout"]').nextElementSibling, null);
  // The original useful link keeps its target and receives the click.
  let opened = 0; p.d.querySelector('#useful a[href="https://lib.kaznu.kz/"]').addEventListener('click', e => { e.preventDefault(); opened++; });
  box.querySelector('a[href="https://lib.kaznu.kz/"]').click(); assert.equal(opened, 1);
  p.d.getElementById('messages').nextElementSibling.click();
  box.querySelector('.univer-app-pin').click();
  umkdPin.click();
  p.d.querySelector('.univer-app-add-tile').click(); assert.equal(p.d.querySelector('.univer-app-add-panel'), null);
  assert.deepEqual(p.sent, []);
  assert.deepEqual(titles(p).slice(-1), ['Сообщения']);
  assert.ok(titles(p).includes('УМКД')); assert.equal(umkdPin.getAttribute('aria-pressed'), 'true');
  // The menu search also filters the useful links.
  const search = p.d.querySelector('.univer-app-menu input[type=search]');
  search.value = 'oqylyq'; search.dispatchEvent(new p.w.Event('input'));
  assert.deepEqual(Array.from(box.querySelectorAll('li')).filter(li => !li.hidden).map(li => li.textContent), ['Oqylyq']);
  search.value = 'нет такого'; search.dispatchEvent(new p.w.Event('input')); assert.equal(box.hidden, true);
  p.close();
});

test('pins update in place, are localized, and Original removes every added node', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  p.w.__univerDesktop.apply(true, false, 'en');
  assert.equal(p.d.querySelector('.univer-app-quick-hint'), null);
  assert.equal(p.d.querySelector('.univer-app-useful>summary').textContent, 'Useful links');
  assert.equal(p.d.getElementById('messages').nextElementSibling.getAttribute('aria-label'), 'Pin to home screen: Сообщения');
  p.w.__univerShortcutsV1.configure([{ title: 'Сообщения', url: 'https://univer.kaznu.kz/my/messages/index/' }]);
  assert.deepEqual(titles(p).slice(-1), ['Сообщения']);
  assert.equal(p.d.getElementById('messages').nextElementSibling.getAttribute('aria-pressed'), 'true');
  p.w.__univerShortcutsV1.configure([]);
  assert.ok(!titles(p).includes('Сообщения'));
  p.w.__univerDesktop.apply(false, false, 'en');
  assert.equal(p.d.querySelector('.univer-app-quick,.univer-app-useful,.univer-app-pin,.univer-app-unpin,#univer-shortcuts-style'), null);
  assert.equal(p.d.getElementById('useful').parentElement.tagName, 'TD');
  p.close();
});

test('a pin saved in Russian is named in the current language: the app\'s word, the site\'s menu link, else the saved name', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  // The site's menu speaks the chosen language; the pins keep the names they were saved with.
  p.d.getElementById('messages').textContent = ' Messages ';
  p.w.__univerShortcutsV1.configure([{ title: 'Текущая аттестация', url: 'https://univer.kaznu.kz/student/attestation/' },
    { title: 'Дистанционные курсы', url: 'https://univer.kaznu.kz/student/discussion/index/' },
    { title: 'Сообщения', url: 'https://univer.kaznu.kz/my/messages/index/' }, { title: 'Справка', url: 'https://univer.kaznu.kz/student/unknown/' }]);
  p.w.__univerDesktop.apply(true, false, 'en');
  assert.deepEqual(titles(p).slice(-4), ['Current attestation', 'Distance courses', 'Messages', 'Справка']);
  p.w.__univerDesktop.apply(true, false, 'kz');
  assert.deepEqual(titles(p).slice(-4, -2), ['Ағымдағы аттестация', 'Қашықтан оқыту курстары']);
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.deepEqual(titles(p).slice(-4, -2), ['Текущая аттестация', 'Дистанционные курсы']);
  p.w.__univerDesktop.apply(false, false, 'ru'); p.close();
});

// After sign-in the site lands on /news/student/1/ (seen on the live cabinet, 15 Nov 2026): that is still the home page.
test('the news page the site opens after sign-in has the home tiles, a later news page does not', () => {
  for (const [url, tiles] of [['https://univer.kaznu.kz/news/student/1/', true], ['https://univer.kaznu.kz/news/student/1', true], ['https://univer.kaznu.kz/news/student/2/', false]]) {
    const p = page(url);
    p.w.__univerDesktop.apply(true, false, 'ru');
    assert.equal(titles(p).length > 0, tiles, url);
    assert.equal(p.d.documentElement.classList.contains('univer-app-home'), tiles, url);
    p.close();
  }
});

test('other pages get the pin buttons and useful links but no home tiles', () => {
  const p = page('https://univer.kaznu.kz/student/umkd/');
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.equal(p.d.querySelector('.univer-app-quick'), null);
  assert.ok(p.d.querySelector('.univer-app-useful'));
  assert.ok(p.d.querySelector('#study .univer-app-pin'));
  p.close();
});

test('tiles and useful links get decorative icons, and the Animations setting reaches the page', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  p.w.__univerShortcutsV1.configure([{ title: 'Сообщения', url: 'https://univer.kaznu.kz/my/messages/index/' }]);
  p.w.__univerDesktop.apply(true, false, 'ru');
  const icons = Array.from(p.d.querySelectorAll('.univer-app-quick .univer-app-link-icon'));
  assert.equal(icons.length, p.d.querySelectorAll('.univer-app-quick .univer-app-tile').length);
  assert.ok(icons.every(i => i.getAttribute('aria-hidden') === 'true' && i.style.getPropertyValue('--glyph').startsWith('url(')));
  const useful = Array.from(p.d.querySelectorAll('.univer-app-useful li>a'));
  assert.ok(useful.length >= 8 && useful.every(a => a.querySelector('.univer-app-link-icon')));
  // Names stay plain text for search, pinning and screen readers.
  assert.equal(useful.find(a => a.href === 'https://lib.kaznu.kz/').textContent, 'Библиотека');
  assert.equal(p.d.documentElement.classList.contains('univer-app-still'), false);
  p.w.__univerMotion = false; p.w.__univerDesktop.apply(true, false, 'ru');
  assert.equal(p.d.documentElement.classList.contains('univer-app-still'), true);
  p.close();
});


test('every link in a shared or nested menu item gets an independent pin action and Original restores nodes', () => {
  const p = page('https://univer.kaznu.kz/student/bachelor/');
  const li = p.d.createElement('li');
  li.innerHTML = '<a href="/fixture/exams/">���������� ��������� � ������� ���������</a><span><a href="/fixture/exams/next/">��������� �������</a></span>';
  p.d.getElementById('study').appendChild(li);
  const anchors = Array.from(li.querySelectorAll('a')), children = Array.from(li.childNodes), original = li.innerHTML;
  let clicks = 0; anchors[1].addEventListener('click', e => { e.preventDefault(); clicks++; });
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.equal(li.querySelectorAll('.univer-app-pin-row').length, 2);
  for (const a of anchors) {
    assert.equal(a.nextElementSibling.className, 'univer-app-pin');
    assert.ok(a.nextElementSibling.getAttribute('aria-label').endsWith(a.textContent));
    a.nextElementSibling.click();
  }
  assert.equal(p.d.getElementById('umkd').parentElement.querySelectorAll('.univer-app-pin').length, 1);
  assert.equal(p.d.querySelector('.univer-app-useful a[href="https://univer.kaznu.kz/user/loginMoodle"]').nextElementSibling.getAttribute('aria-pressed'), 'true');
  assert.equal(p.d.querySelector('.univer-app-quick-hint,.univer-app-credential-note'), null);
  assert.deepEqual(p.sent, []);
  p.w.__univerDesktop.apply(false, false, 'ru');
  assert.equal(li.innerHTML, original); assert.deepEqual(Array.from(li.childNodes), children);
  anchors[1].click(); assert.equal(clicks, 1);
  assert.equal(p.d.documentElement.classList.contains('univer-app-home'), false);
  p.close();
});


test('dark theme keeps the pin, plus and bin icons as whole SVG addresses', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  p.w.eval(read('theme.js'));
  p.w.__univerTheme = 'dark';
  p.w.__univerShortcutsV1.configure([{title:'Библиотека',url:'https://lib.kaznu.kz/'}]);
  p.w.__univerDesktop.apply(true,false,'ru');
  p.w.__univerThemeV1.refresh();
  assert.ok(p.d.querySelector('.univer-app-pin') && p.d.querySelector('.univer-app-add-tile'));
  const app = p.d.getElementById('univer-app-dark-app').textContent;
  for (const name of ['--univer-pin-icon', '--univer-pinned-icon', '--univer-close-icon', '--univer-plus-icon', '--univer-trash-icon', '--univer-book-icon']) {
    const value = (app.match(new RegExp(name + ':\\s*url\\("(data:image/svg\\+xml,[^"]*)"\\)')) || [])[1];
    assert.ok(value, name + ' is mapped');
    assert.doesNotMatch(value, /#/, name + ' has no bare #');
    assert.match(decodeURIComponent(value), /<\/svg>$/, name + ' stays a whole SVG');
  }
  p.close();
});

test('a fixed tile can leave the home screen and New section offers it back with everything else that is not pinned', () => {
  const p = page('https://univer.kaznu.kz/news/student');
  const umkd = 'https://univer.kaznu.kz/student/umkd/', messages = 'https://univer.kaznu.kz/my/messages/index/';
  p.w.__univerShortcutsV1.configure([{ title: 'Библиотека', url: 'https://lib.kaznu.kz/' }], [], [umkd]);
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.ok(!titles(p).includes('УМКД') && titles(p).includes('Расписание') && titles(p).includes('Библиотека'));
  const groups = p.w.__univerShortcutsV1.offered(), all = groups.flatMap(g => g.items);
  assert.equal(groups[0].name, 'Убрано с главной'); assert.equal(groups[0].items[0].title, 'УМКД'); assert.equal(groups[0].items[0].gone, true);
  assert.ok(all.some(i => i.href === messages));
  // Pinned and fixed tiles are not offered, and logging out never is.
  assert.ok(!all.some(i => /Библиотека|Расписание|Выход/.test(i.title)));
  assert.equal(new Set(all.map(i => i.href)).size, all.length);
  // Only known tiles can be taken away; a stored address of anything else is ignored.
  p.w.__univerShortcutsV1.configure([], [], ['https://univer.kaznu.kz/student/iup/']);
  assert.ok(titles(p).includes('УМКД'));
  p.close();
});
