const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const read = name => fs.readFileSync('src/Univer.Desktop/Assets/' + name, 'utf8');
const scripts = ['page-common.js', 'univer-automation.js', 'news.js', 'study-links.js', 'attendance.js', 'overview.js', 'shortcuts.js', 'univer-layout.js', 'desktop.js'].map(read);

function page(content, url) {
  const html = '<table class="wrappertable">' +
    '<tr id="l1"><td class="header">КазНУ<a href="/lang/change/kz/"><img src="/img/kz.png"></a><img src="/img/ru.png"><a href="/lang/change/en/"><img src="/img/en.png"></a></td></tr>' +
    '<tr id="l2"><td><div id="logo"></div><div class="links"><a href="/user/pass/">Профиль</a><a href="/user/logout">Выход</a></div></td></tr>' +
    '<tr id="l3"><td><table class="inner"><tr><td class="left"></td><td class="center"><a id="tab-home" href="/news/student"><div class="center">Главная</div></a><a id="tab-b" class="active" href="/student/bachelor/"><div class="center">Бакалавр</div></a><a id="tab-e" href="/ssc/"><div class="center">Е-услуги</div></a></td><td class="right"></td></tr></table></td></tr>' +
    '<tr id="l4"><td><div id="menu-home"><table id="tools"><tr><td><table class="tool"><tr><td><ul id="links"><li><a href="/student/myschedule/">Расписание</a></li></ul></td></tr><tr><td class="bottom-center">Учёба</td></tr></table></td></tr></table></div>' +
    content + '</td></tr></table>';
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const w = dom.window;
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.__univerNativeNotify = () => {};
  scripts.forEach(script => w.eval(script));
  return { w, d: w.document, close: () => w.close() };
}
const apply = (p, on = true) => p.w.__univerDesktop.apply(on, false, 'ru');

test('live Bachelor tt cell replaces the original pairs, hides actual trailing rows, and restores every source node', async () => {
  const p=page('<table class="mt"><tr class="top"><td class="ct"><h3>Личные данные</h3></td></tr><tr class="brk"><td class="ct">Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ.</td></tr>'+
    '<tr class="mid" id="source-row"><td class="lt"></td><td class="tt" id="source"><div class="inner"><table class="inner"><tr><td><li>Логин:</li></td><td>fixture_login</td><td><li>Пароль:</li></td><td>fixture_secret</td></tr><tr><td><li>Другой логин:</li></td><td>fixture_other_login</td><td><li>Пароль:</li></td><td>fixture_other_secret</td></tr></table></div></td><td class="rt"></td></tr>'+
    '<tr class="mid" id="padding"><td class="pd"></td></tr><tr class="bot" id="trailing" style="display:block!important"><td class="ct">Нижний блок</td></tr></table><div id="after">Дополнительный блок</div>', 'https://univer.kaznu.kz/student/bachelor/');
  const source=p.d.getElementById('source'), original=Array.from(source.childNodes), table=source.querySelector('table'); let copied, sourceClicks=0;
  table.addEventListener('click',()=>sourceClicks++);
  Object.defineProperty(p.w.navigator,'clipboard',{value:{writeText:async text=>{copied=text;}}});
  for(let i=0;i<3;i++)apply(p);
  const panel=source.querySelector(':scope>.univer-app-credentials'); assert.ok(panel); assert.equal(p.d.querySelectorAll('.univer-app-credentials').length,1);
  assert.equal(p.w.getComputedStyle(source.querySelector('.univer-app-credential-original')).display,'none');
  assert.ok(!p.d.getElementById('source-row').classList.contains('univer-app-after-credentials'));
  for(const id of ['padding','trailing','after'])assert.equal(p.w.getComputedStyle(p.d.getElementById(id)).display,'none');
  assert.equal(panel.querySelector('.univer-app-credential-value').textContent,'fixture_login'); assert.doesNotMatch(panel.textContent,/fixture_secret|fixture_other/);
  panel.querySelectorAll('.univer-app-credential-copy')[1].click(); await Promise.resolve(); assert.equal(copied,'fixture_secret');
  const diagnostic=JSON.parse(p.w.eval(read('review-credentials.js'))); assert.equal(diagnostic.panels[0].passwordMasked,true);
  assert.doesNotMatch(JSON.stringify(diagnostic),/fixture_login|fixture_secret/);
  apply(p,false); assert.deepEqual(Array.from(source.childNodes),original); assert.equal(p.d.getElementById('trailing').style.getPropertyValue('display'),'block');
  assert.equal(p.d.getElementById('trailing').style.getPropertyPriority('display'),'important'); table.click(); assert.equal(sourceClicks,1);
  apply(p); assert.equal(p.d.querySelectorAll('.univer-app-credentials').length,1); await new Promise(r=>setTimeout(r,25)); apply(p,false); p.close();
});

test('grade summary opens the matching course; completed lessons switch to the next day without repeated GETs', async () => {
  const p = page('<table class="mt"><tr><td class="ct">Личные данные</td></tr><tr><td class="ct"><b>Студент</b></td></tr></table>', 'https://univer.kaznu.kz/student/bachelor/');
  let instant = new Date(2026,9,12,9,30).getTime(), tick, reads=0;
  const RealDate=p.w.Date;
  p.w.Date=class extends RealDate { constructor(...args){ super(...(args.length?args:[instant])); } static now(){return instant;} };
  p.w.setInterval=callback=>{tick=callback;return 123;}; p.w.clearInterval=()=>{tick=null;};
  Object.defineProperty(p.d,'hidden',{value:false,configurable:true});
  const schedule='<table class="schedule"><tr><th></th><th>Пн</th><th>Вт</th><th>Ср</th><th>Чт</th><th>Пт</th><th>Сб</th></tr><tr><td>09:00 09:50</td><td><p>Алгебра</p></td><td><p>Физика</p></td><td></td><td></td><td></td><td></td></tr></table>';
  const heading='Алгебра ([1]) 01.09.2026 - 20.12.2026';
  const grades='<table id="l4"><tr><td class="ct">'+heading+'</td></tr><tr><td class="ct"><b>РК1 (100): 85</b></td></tr></table>';
  p.w.fetch=url=>{reads++;return Promise.resolve({status:200,url:'https://univer.kaznu.kz'+url,text:async()=>url.includes('attendance')?grades:schedule});};
  apply(p); await new Promise(r=>setTimeout(r,25));
  const subject=p.d.querySelector('.univer-app-overview-course-link'); subject.addEventListener('click',e=>e.preventDefault()); subject.click();
  assert.equal(subject.getAttribute('href'),'/student/attendance/full/');
  assert.equal(p.w.sessionStorage.getItem('univer.desktop.attendance-course.v1:/student/attendance/full/'),heading.toLocaleLowerCase());
  const body=p.d.querySelector('.univer-app-overview-today .univer-app-overview-body');
  assert.match(body.textContent,/Алгебра/); assert.doesNotMatch(body.textContent,/Физика/);
  instant=new Date(2026,9,12,9,50).getTime(); await tick();
  assert.match(body.textContent,/Сегодняшние пары закончились/); assert.match(body.textContent,/Физика/); assert.doesNotMatch(body.textContent,/Алгебра/);
  const list=body.querySelector('ol'); await tick(); assert.equal(body.querySelector('ol'),list); assert.equal(reads,2);
  apply(p,false); assert.equal(tick,null); p.close();
});

test('Wi-Fi section hides and copies nested source credentials, removes the following content, and Original restores it', async () => {
  const p=page('<table class="mt"><tr><td class="ct">Личные данные</td></tr><tr><td class="ct"><b>Студент</b></td></tr><tr><td class="ct">Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ.</td></tr><tr><td class="ct" id="wifi"><table><tr><td>Логин: fixture_user<br>Пароль: fixture_secret</td></tr></table></td></tr><tr id="below"><td>Лишний блок</td></tr></table><p id="footer">Ещё ниже</p>', 'https://univer.kaznu.kz/student/bachelor/');
  const source=p.d.querySelector('#wifi table'), below=p.d.getElementById('below'), footer=p.d.getElementById('footer'); let copied;
  Object.defineProperty(p.w.navigator,'clipboard',{value:{writeText:async text=>{copied=text;}}});
  apply(p); const panel=p.d.querySelector('#wifi>.univer-app-credentials'); assert.ok(panel);
  assert.doesNotMatch(panel.textContent,/fixture_secret/); assert.ok(below.classList.contains('univer-app-after-credentials')); assert.ok(footer.classList.contains('univer-app-after-credentials'));
  panel.querySelector('.univer-app-credential-show').click(); assert.match(panel.textContent,/fixture_secret/);
  panel.querySelector('.univer-app-credential-show').click(); assert.doesNotMatch(panel.textContent,/fixture_secret/);
  panel.querySelectorAll('.univer-app-credential-copy')[1].click(); await Promise.resolve(); assert.equal(copied,'fixture_secret');
  apply(p,false); assert.equal(p.d.querySelector('#wifi table'),source); assert.equal(p.d.getElementById('below'),below); assert.ok(!below.classList.contains('univer-app-after-credentials')); assert.ok(!footer.classList.contains('univer-app-after-credentials'));
  p.close();
});

test('overview completion fades once; Show all reveals immediately and cancels on rapid collapse or Original', async () => {
  const p = page('<table class="mt"><tr><td class="ct message">Общий GPA = 3.12</td></tr></table>', 'https://univer.kaznu.kz/student/bachelor/');
  const grades = '<table id="l4">' + Array.from({length:7}, (_,i) => '<tr><td class="ct">Предмет ' + i + ' 01.09.2026 - 20.12.2026</td></tr><tr><td class="ct"><b>РК1 (100): 85</b></td></tr>').join('') + '</table>';
  p.w.fetch = url => Promise.resolve({status:200,url:'https://univer.kaznu.kz'+url,text:async()=>url.includes('attendance')?grades:''});
  const records=[];
  p.w.HTMLElement.prototype.animate=function(frames,options){const r={node:this,frames,options,cancel(){this.cancelled=true;this.oncancel?.();}};records.push(r);return r;};
  apply(p); await new Promise(r=>setTimeout(r,30));
  const body=p.d.querySelector('.univer-app-overview-grades .univer-app-overview-body');
  const ready=records.filter(r=>r.node===body); assert.equal(ready.length,1); assert.match(ready[0].frames[0].transform,/translateY.*scale/);
  apply(p); await new Promise(r=>setTimeout(r,20)); assert.equal(records.filter(r=>r.node===body).length,1);
  const button=body.querySelector('button'),items=Array.from(body.querySelectorAll('li')),extra=items.filter(li=>li.hidden);
  button.focus(); button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.ok(extra.length); assert.ok(items.every(li=>!li.hidden)); assert.equal(p.d.activeElement,button);
  const entries=records.filter(r=>extra.includes(r.node)); assert.equal(entries.length,extra.length);
  assert.ok(entries.every(r=>/translateY.*scale/.test(r.frames[0].transform)&&r.options.duration+r.options.delay<300));
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1})); assert.ok(extra.every(li=>li.hidden)); assert.ok(entries.every(r=>r.cancelled));
  const before=records.length; button.click(); assert.ok(items.every(li=>!li.hidden)); assert.equal(records.length,before);
  apply(p,false); assert.equal(ready[0].cancelled,true); assert.equal(p.d.querySelector('.univer-app-overview-grid'),null); p.close();
});

test('Бакалавр has the study tiles and leads with the GPA figures, and Original restores the source', () => {
  const p = page('<table class="mt"><tr><td class="ct message"><ul><li>Переводной GPA = 2.85</li><li>Общий GPA = 3.12</li></ul></td></tr></table><p>Информационный бюллетень</p>',
    'https://univer.kaznu.kz/student/bachelor/');
  apply(p);
  assert.ok(p.d.querySelectorAll('.univer-app-quick .univer-app-tile').length >= 6);
  const cards = Array.from(p.d.querySelectorAll('.univer-app-gpa>div')).map(c => [c.querySelector('b').textContent, c.querySelector('span').textContent]);
  assert.deepEqual(cards, [['2.85', 'Переводной GPA'], ['3.12', 'Общий GPA']]);
  assert.ok(p.d.querySelector('.ct.message').classList.contains('univer-app-gpa-source'));
  assert.ok(p.d.getElementById('tab-b').classList.contains('univer-app-tab-icon'));
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-gpa,.univer-app-gpa-source,.univer-app-tab-icon,.univer-app-quick'), null);
  p.close();
});

test('Е-услуги desk describes its two real links; print and Wi-Fi sign-in becomes a panel with a hidden, copyable password', async () => {
  const p = page('<div class="ts"><a id="svc" href="/ssc/UserServiceList">Услуги</a><a id="req" href="/ssc/StatementList">Заявки е-услуг</a></div>' +
    '<ul><li id="secret">Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ.:<br>Логин: fixture_user<br>Пароль: fixture-pass1</li></ul>', 'https://univer.kaznu.kz/ssc/');
  let copied = null; Object.defineProperty(p.w.navigator, 'clipboard', { value: { writeText: v => { copied = v; return Promise.resolve(); } }, configurable: true });
  let clicks = 0; p.d.getElementById('svc').addEventListener('click', e => { e.preventDefault(); clicks++; });
  apply(p);
  const card = p.d.getElementById('svc');
  assert.ok(card.classList.contains('univer-app-service-card'));
  assert.match(card.getAttribute('data-univer-hint'), /Справки/);
  assert.ok(p.d.querySelector('.univer-app-ssc-nav').hidden);
  card.click(); assert.equal(clicks, 1);
  assert.equal(p.d.querySelector('.univer-app-tab-icon,.univer-app-tabs a[href="/ssc/"]:not(.univer-app-tab-hidden)'), p.d.querySelector('.univer-app-tab-icon'));
  const panel = p.d.querySelector('.univer-app-credentials'), values = Array.from(panel.querySelectorAll('.univer-app-credential-value'));
  assert.equal(panel.querySelector('h3').textContent, 'Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ');
  assert.ok(p.d.getElementById('secret').classList.contains('univer-app-credentials-source'));
  assert.equal(values[0].textContent, 'fixture_user');
  // The password is not in the page text until it is shown.
  assert.ok(!panel.textContent.includes('fixture-pass1'));
  const show = panel.querySelector('.univer-app-credential-show');
  assert.equal(show.getAttribute('aria-pressed'), 'false');
  show.click(); assert.equal(values[1].textContent, 'fixture-pass1'); assert.equal(show.textContent, 'Скрыть');
  show.click(); assert.ok(!panel.textContent.includes('fixture-pass1'));
  const copy = panel.querySelectorAll('.univer-app-credential-copy')[1];
  copy.click(); await new Promise(r => setTimeout(r, 0));
  assert.equal(copied, 'fixture-pass1'); assert.equal(copy.textContent, 'Скопировано');
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-service-card,.univer-app-credentials,.univer-app-credentials-source,.univer-app-ssc-nav,.univer-app-link-icon,[data-univer-hint],.univer-app-tab-hidden'), null);
  assert.equal(card.textContent, 'Услуги');
  p.close();
});

test('service list gets a search and the request list readable statuses, with a switch between them', () => {
  const rows = ['Справка об обучении', 'Академический отпуск', 'Воинский учет'].map((n, i) => `<tr><td>${i + 1}</td><td>${n}</td><td><a href="/ssc/apply/${i}">Подать заявку</a></td></tr>`).join('');
  const p = page(`<table class="inner" id="list"><tr><th>#</th><th>Услуга</th><th></th></tr>${rows}</table>`, 'https://univer.kaznu.kz/ssc/UserServiceList');
  apply(p);
  const nav = Array.from(p.d.querySelectorAll('.univer-app-ssc-nav a'));
  assert.deepEqual(nav.map(a => [a.textContent, a.getAttribute('aria-current')]), [['Услуги', 'page'], ['Мои заявки', null]]);
  const search = p.d.querySelector('.univer-app-service-search');
  search.value = 'отпуск'; search.dispatchEvent(new p.w.Event('input'));
  assert.deepEqual(Array.from(p.d.querySelectorAll('#list tr:not(.univer-app-service-hidden) td:nth-child(2)')).map(c => c.textContent), ['Академический отпуск']);
  search.value = 'нет такого'; search.dispatchEvent(new p.w.Event('input'));
  assert.equal(p.d.querySelector('.univer-app-service-empty').hidden, false);
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-service-search,.univer-app-service-hidden,.univer-app-ssc-nav'), null);
  p.close();
  const q = page('<table class="inner" id="mine"><tr><td>1</td><td>Справка</td><td>Завершена</td></tr><tr><td>2</td><td>Справка</td><td>В обработке</td></tr><tr><td>3</td><td>Справка</td><td>Отклонена</td></tr></table>',
    'https://univer.kaznu.kz/ssc/StatementList');
  apply(q);
  assert.deepEqual(Array.from(q.d.querySelectorAll('#mine td:last-child')).map(c => c.getAttribute('data-univer-tone')), ['done', 'wait', 'fail']);
  apply(q, false);
  assert.equal(q.d.querySelector('[data-univer-tone],.univer-app-status'), null);
  q.close();
});

test('disclosures respond immediately to rapid toggles and Original preserves nodes and page handlers', async () => {
  const p = page('<details id="faq" open><summary>Вопрос</summary><p>Ответ</p></details>', 'https://univer.kaznu.kz/faq/');
  apply(p);
  const faq = p.d.getElementById('faq');
  const content = faq.querySelector('p'); let clicks = 0;
  faq.querySelector('summary').addEventListener('click', () => clicks++);
  faq.querySelector('summary').click();
  assert.equal(faq.open, false);
  faq.querySelector('summary').click(); assert.equal(faq.open, true);
  await new Promise(r => setTimeout(r, 200));
  assert.equal(faq.open, true);
  p.w.__univerMotion = false; apply(p);
  faq.querySelector('summary').click();
  assert.equal(faq.open, false);
  apply(p, false); faq.querySelector('summary').click();
  assert.equal(faq.open, true); assert.equal(faq.querySelector('p'), content); assert.equal(clicks, 4);
  p.close();
});

test('Бакалавр reads today\'s classes and grades from the site pages, and the desk the latest requests', async () => {
  const schedule = '<table class="schedule"><tr><th></th><th>Пн</th><th>Вт</th><th>Ср</th><th>Чт</th><th>Пт</th><th>Сб</th></tr>' +
    '<tr><td class="time">09:00 09:50</td>' + '<td rowspan="2"><p>Алгебра</p><p class="params"><span>ауд. 101</span><span>лекция</span></p></td>'.repeat(6) + '</tr>' +
    '<tr><td class="time">10:00 10:50</td></tr><tr><td class="time">11:00 11:50</td>' + '<td><p>История</p></td>'.repeat(6) + '</tr></table>';
  const grades = '<table id="l4"><tr><td class="ct">Алгебра ([1]) 01.09.2026 - 20.12.2026</td></tr><tr><td class="ct"><b>РК1 (100): 85 РК2 (100): 70 Экзамен (100): </b></td></tr>' +
    '<tr><td class="ct">История 01.09.2026 - 20.12.2026</td></tr></table>';
  const mine = '<table id="l4"><tr><td><table><tr><td>1</td><td>ФИТ</td><td>Справка об обучении</td><td>18.09.2026 17:59:10</td><td>Завершена</td></tr>' +
    '<tr><td>2</td><td>ФИТ</td><td>Справка для военкомата</td><td>23.09.2026 17:37:41</td><td>В обработке</td></tr></table></td></tr></table>';
  const pages = { '/student/myschedule/': schedule, '/student/attendance/full/': grades, '/ssc/StatementList': mine };
  const p = page('<table class="mt"><tr><td class="ct message"><ul><li>Переводной GPA = 2.85</li><li>Общий GPA = 3.12</li></ul></td></tr></table>' +
    '<table class="mt"><tr><td class="lt"></td><td class="ct">Личные данные</td><td class="rt"></td></tr><tr><td class="lt"></td><td class="ct"><h3>Здравствуйте</h3></td><td class="rt"></td></tr></table>', 'https://univer.kaznu.kz/student/bachelor/');
  const parse = html => new p.w.DOMParser().parseFromString('<html><body>' + html + '</body></html>', 'text/html');
  const monday = new Date(2026, 9, 12, 9, 30);
  const plain = v => JSON.parse(JSON.stringify(v));
  const today = plain(p.w.__univerOverviewV1.lessonsToday(parse(schedule), monday));
  assert.deepEqual(today.map(l => [l.start, l.end, l.subject, l.details]), [[540, 650, 'Алгебра', 'ауд. 101 / лекция'], [660, 710, 'История', '']]);
  assert.deepEqual(plain(p.w.__univerOverviewV1.lessonsToday(parse(schedule), new Date(2026, 9, 11, 9))), []);
  const courses = plain(p.w.__univerOverviewV1.courseScores(parse(grades)));
  assert.deepEqual(courses.map(c => [c.name, c.scores.map(s => s.label + '=' + s.value)]), [['Алгебра', ['РК1=85', 'РК2=70', 'Экзамен=']], ['История', []]]);
  const requests = plain(p.w.__univerOverviewV1.requests(parse(mine)));
  assert.deepEqual(requests.map(r => [r.service, r.status]), [['Справка для военкомата', 'В обработке'], ['Справка об обучении', 'Завершена']]);
  p.w.fetch = url => Promise.resolve({ status: 200, url: 'https://univer.kaznu.kz' + url, text: () => Promise.resolve(pages[url]) });
  apply(p);
  const panels = Array.from(p.d.querySelectorAll('.univer-app-overview-grid>.univer-app-overview h2')).map(h => h.textContent);
  assert.deepEqual(panels, ['Сегодня', 'Успеваемость']);
  await new Promise(r => setTimeout(r, 20));
  assert.ok(p.d.querySelector('.univer-app-overview-grades .univer-app-overview-score'));
  assert.ok(p.d.querySelector('table.mt.univer-app-panel td.univer-app-panel-head .univer-app-link-icon'));
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-overview,.univer-app-panel,.univer-app-panel-head,.univer-app-link-icon'), null);
  p.close();
});

test('a scripted select press keeps the native control and cannot create the app list', () => {
  const p = page('<label for="week">Неделя</label><select id="week" onchange="window.qaChanged=(window.qaChanged||0)+1"><option>1 неделя</option><option selected>2 неделя</option><option>3 неделя</option></select>',
    'https://univer.kaznu.kz/student/myschedule/');
  apply(p);
  const select = p.d.getElementById('week');
  select.dispatchEvent(new p.w.MouseEvent('mousedown', { bubbles: true, button: 0 }));
  // Untrusted (scripted) presses keep the native behaviour.
  assert.equal(p.d.querySelector('.univer-app-select'), null);
  p.close();
});

test('a desk without its own buttons gets two described cards, and one site table with two titles becomes two sections', () => {
  const p = page('<table class="mt"><tr><td class="lt"></td><td class="ct">Услуги</td><td class="rt"></td></tr><tr><td class="lt"></td><td class="ct"><p>Текст</p></td><td class="rt"></td></tr></table>', 'https://univer.kaznu.kz/ssc/');
  apply(p);
  const cards = Array.from(p.d.querySelectorAll('.univer-app-desk-cards>a'));
  assert.deepEqual(cards.map(a => [new URL(a.href).pathname, a.textContent]), [['/ssc/UserServiceList', 'Услуги'], ['/ssc/StatementList', 'Мои заявки']]);
  apply(p, false); assert.equal(p.d.querySelector('.univer-app-desk-cards'), null);
  p.close();
  const q = page('<table class="mt"><tr><td class="ct">Личные данные</td></tr><tr><td class="ct"><h3>Здравствуйте</h3></td></tr><tr><td class="ct">Обучение</td></tr><tr><td class="ct"><ul><li><a href="/guide.docx">Руководство</a></li></ul></td></tr></table>',
    'https://univer.kaznu.kz/student/bachelor/');
  apply(q);
  assert.deepEqual(Array.from(q.d.querySelectorAll('td.univer-app-panel-head')).map(c => c.textContent), ['Личные данные', 'Обучение']);
  apply(q, false); q.close();
});

test('a day off shows the next classes from the following week, and long lists open on request', async () => {
  const week = (from, to, subject) => '<select id="weekSelect" onchange="location = this.value;">' +
    '<option value="/student/myschedule/1/2/12.10.2026/18.10.2026/" ' + (from === 12 ? 'selected' : '') + '>Неделя 1</option>' +
    '<option value="/student/myschedule/1/3/19.10.2026/25.10.2026/" ' + (from === 19 ? 'selected' : '') + '>Неделя 2</option></select>' +
    '<table class="schedule"><tr><th></th><th>Пн</th><th>Вт</th><th>Ср</th><th>Чт</th><th>Пт</th><th>Сб</th></tr><tr><td class="time">09:00 09:50</td><td>' + (subject ? '<p>' + subject + '</p>' : '') + '</td><td></td><td></td><td></td><td></td><td></td></tr></table>';
  const pages = { '/student/myschedule/1/3/19.10.2026/25.10.2026/': week(19, 25, 'Физика') };
  const p = page('', 'https://univer.kaznu.kz/student/bachelor/');
  p.w.fetch = url => Promise.resolve({ status: 200, url: 'https://univer.kaznu.kz' + url, text: () => Promise.resolve(pages[url]) });
  const parse = html => new p.w.DOMParser().parseFromString('<html><body>' + html + '</body></html>', 'text/html');
  const sunday = new Date(2026, 9, 18, 12, 0);
  const next = await p.w.__univerOverviewV1.nextClasses(parse(week(12, 18, 'Алгебра')), sunday);
  assert.equal(next.lessons[0].subject, 'Физика'); assert.equal(next.date.getDate(), 19);
  p.w.__univerDesktop.apply(false, false, 'ru'); p.close();
});

test('on Бакалавр the sign-in section under its own title gets the same hidden password and copy buttons', () => {
  const p = page('<table class="mt"><tr><td class="ct">Личные данные</td></tr><tr><td class="ct"><b>Здравствуйте</b></td></tr>' +
    '<tr><td class="ct">Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ.</td></tr><tr><td class="ct" id="cred">Логин: fixture_user<br>Пароль: fixture-pass1</td></tr></table>',
    'https://univer.kaznu.kz/student/bachelor/');
  apply(p);
  const cell = p.d.getElementById('cred'), panel = cell.querySelector('.univer-app-credentials');
  assert.ok(cell.classList.contains('univer-app-credentials-host'));
  assert.equal(panel.querySelector('h3'), null);
  assert.equal(panel.querySelectorAll('.univer-app-credential-value')[0].textContent, 'fixture_user');
  assert.ok(!panel.textContent.includes('fixture-pass1'));
  assert.equal(panel.querySelectorAll('.univer-app-credential-copy').length, 2);
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-credentials,.univer-app-credentials-host'), null);
  assert.equal(cell.textContent, 'Логин: fixture_userПароль: fixture-pass1');
  p.close();
});

test('when Бакалавр shows only the sign-in title, the values are read from the Е-услуги page into the same panel', async () => {
  const p = page('<table class="mt"><tr><td class="ct">Личные данные</td></tr><tr><td class="ct"><b>Здравствуйте</b></td></tr>' +
    '<tr><td class="ct">Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ.</td></tr><tr><td class="ct" id="empty"></td></tr></table>', 'https://univer.kaznu.kz/student/bachelor/');
  const desk = '<table id="l4"><tr><td><ul><li>Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ.:<br>Логин: fixture_user<br>Пароль: fixture-pass1</li></ul></td></tr></table>';
  p.w.fetch = url => Promise.resolve({ status: 200, url: 'https://univer.kaznu.kz' + url, text: () => Promise.resolve(url === '/ssc/' ? desk : '') });
  apply(p); await new Promise(r => setTimeout(r, 30));
  const panel = p.d.getElementById('empty').querySelector('.univer-app-credentials:not(.univer-app-pending)');
  assert.equal(panel.querySelectorAll('.univer-app-credential-value')[0].textContent, 'fixture_user');
  assert.ok(!panel.textContent.includes('fixture-pass1'));
  apply(p, false); assert.equal(p.d.querySelector('.univer-app-credentials,.univer-app-credentials-host'), null);
  p.close();
});


test('observed separate menu row and nested home content keep tiles with the page and restore the original records', () => {
  const p = page('<table class="inner"><tr><td class="left"></td><td class="center" id="fixture-body"><table class="mt" id="fixture-gpa"><tr class="top"><td class="ct message"><ul><li>Переводной GPA = 2.85</li><li>Общий GPA = 3.12</li></ul></td></tr></table><table class="mt" id="fixture-panel"><tr class="top"><td class="ct">Личные данные</td></tr><tr><td class="ct">Синтетический студент</td></tr><tr class="brk"><td class="ct">Обучение</td></tr><tr><td class="tt" id="fixture-tt"><table class="inner" id="fixture-records"><tr><td><li>Форма обучения:</li></td><td>Очная</td><td><li>Уровень обучения:</li></td><td>Первое высшее образование</td></tr></table></td></tr></table></td><td class="right"></td></tr></table>', 'https://univer.kaznu.kz/student/bachelor/');
  const menu = p.d.getElementById('menu-home'), carrier = p.d.createElement('tr'), cell = p.d.createElement('td');
  carrier.appendChild(cell); cell.appendChild(menu); p.d.getElementById('l4').before(carrier);
  const body = p.d.getElementById('fixture-body'), source = p.d.getElementById('fixture-gpa'), records = p.d.getElementById('fixture-records'), values = Array.from(records.querySelectorAll('td'));
  let click = 0; values[1].addEventListener('click', () => click++);
  const sourceParent = source.parentElement, recordsParent = records.parentElement;
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.equal(body.firstElementChild.className, 'univer-app-quick');
  assert.equal(carrier.querySelector('.univer-app-quick'), null);
  assert.ok(source.classList.contains('univer-app-gpa-source-table'));
  assert.ok(recordsParent.classList.contains('univer-app-panel-body'));
  assert.equal(values[1].textContent, 'Очная');
  p.w.__univerDesktop.apply(true, false, 'en');
  assert.equal(body.querySelectorAll('.univer-app-quick').length, 1);
  p.w.__univerDesktop.apply(false, false, 'ru');
  assert.equal(source.parentElement, sourceParent); assert.equal(records.parentElement, recordsParent);
  assert.equal(p.d.querySelector('.univer-app-home-body,.univer-app-gpa-source-table,.univer-app-panel-body'), null);
  assert.equal(p.d.getElementById('menu-home').parentElement, cell);
  assert.deepEqual(Array.from(records.querySelectorAll('td')), values); values[1].click(); assert.equal(click, 1);
  p.close();
});


test('a direct center content cell retains the outer gutter instead of being treated as a nested wrapper', () => {
  const p = page('<h1>Новости</h1><p>Синтетическая новость</p>', 'https://univer.kaznu.kz/news/student');
  const frame = p.d.querySelector('#l4>td'); frame.classList.add('center');
  p.w.__univerDesktop.apply(true, false, 'ru');
  assert.equal(frame.firstElementChild.className, 'univer-app-quick');
  assert.equal(frame.classList.contains('univer-app-home-body'), false);
  p.w.__univerDesktop.apply(false, false, 'ru');
  assert.equal(frame.className, 'center');
  assert.equal(frame.querySelector('h1').textContent, 'Новости');
  p.close();
});

test('overview builds nothing while its anchor is missing and a rebuilt summary keeps a single refresh timer', async () => {
  const bare = page('<div id="no-anchor">Студент</div>', 'https://univer.kaznu.kz/student/bachelor/');
  let sections = 0; const make = bare.d.createElement.bind(bare.d);
  bare.d.createElement = (name, ...rest) => { if (String(name).toLowerCase() === 'section') sections++; return make(name, ...rest); };
  for (let i = 0; i < 5; i++) bare.w.__univerOverviewV1.apply('ru');
  assert.equal(sections, 0); assert.equal(bare.d.querySelector('.univer-app-overview-grid'), null); bare.close();
  const p = page('<table class="mt"><tr><td class="ct">Личные данные</td></tr></table>', 'https://univer.kaznu.kz/student/bachelor/');
  const timers = new Set(); let next = 1;
  p.w.setInterval = () => { const id = next++; timers.add(id); return id; }; p.w.clearInterval = id => { timers.delete(id); };
  Object.defineProperty(p.d, 'hidden', { value: false, configurable: true });
  p.w.fetch = url => Promise.resolve({ status: 200, url: 'https://univer.kaznu.kz' + url, text: async () => '<table class="schedule"><tr><th></th><th>Пн</th></tr></table>' });
  apply(p); await new Promise(r => setTimeout(r, 25)); assert.equal(timers.size, 1);
  p.d.querySelector('.univer-app-overview-grid').remove(); p.w.__univerOverviewV1.apply('ru');
  await new Promise(r => setTimeout(r, 25));
  assert.equal(p.d.querySelectorAll('.univer-app-overview-grid').length, 1); assert.equal(timers.size, 1);
  apply(p, false); assert.equal(timers.size, 0); p.close();
});

test('services navigation builds nothing on a services page without the content cell', () => {
  const dom = new JSDOM('<table class="wrappertable"><tr id="l1"><td class="header">КазНУ</td></tr></table>', { url: 'https://univer.kaznu.kz/ssc/', runScripts: 'outside-only' });
  const w = dom.window; Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} }); w.__univerNativeNotify = () => {};
  scripts.forEach(script => w.eval(script));
  let navs = 0; const make = w.document.createElement.bind(w.document);
  w.document.createElement = (name, ...rest) => { if (String(name).toLowerCase() === 'nav') navs++; return make(name, ...rest); };
  for (let i = 0; i < 5; i++) w.__univerShortcutsV1.apply('ru');
  assert.equal(navs, 0); assert.equal(w.document.querySelector('.univer-app-ssc-nav'), null);
  w.__univerShortcutsV1.release(); w.close();
});
