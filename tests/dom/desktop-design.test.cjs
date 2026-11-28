const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');
const automation = fs.readFileSync('src/Univer.Desktop/Assets/univer-automation.js', 'utf8');
const desktop = fs.readFileSync('src/Univer.Desktop/Assets/desktop.js', 'utf8');
const common = fs.readFileSync('src/Univer.Desktop/Assets/page-common.js', 'utf8');
const layout = common + '\n' + fs.readFileSync('src/Univer.Desktop/Assets/univer-layout.js', 'utf8');
const sections = ['page-common.js','news.js','study-links.js','attendance.js','overview.js','shortcuts.js'].map(name=>fs.readFileSync('src/Univer.Desktop/Assets/'+name,'utf8')).join('\n');

function page(html, url) {
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const w = dom.window;
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () { return this.closest('[hidden]') ? [] : [{ width: 100, height: 30 }]; };
  w.jQuery = () => ({ bind() {} });
  w.eval(automation); w.eval(sections); w.eval(layout); w.eval(desktop);
  return { w, d: w.document, close: () => w.close() };
}
function apply(p, enabled = true) { p.w.__univerDesktop.apply(enabled, false, 'ru'); }

test('keyboard and pointer modality switches without losing native focus and Original clears presentation state', () => {
  const p = page('<input id="value" value="kept">', 'https://univer.kaznu.kz/news/student');
  apply(p); const input = p.d.getElementById('value'); input.focus();
  input.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
  assert.ok(p.d.documentElement.classList.contains('univer-app-keyboard'));
  input.dispatchEvent(new p.w.Event('pointerdown', { bubbles: true }));
  assert.equal(p.d.documentElement.classList.contains('univer-app-keyboard'), false);
  assert.equal(p.d.activeElement, input);
  p.w.__univerMotion = false; apply(p); assert.ok(p.d.documentElement.classList.contains('univer-app-still'));
  apply(p, false); assert.equal(p.d.documentElement.classList.contains('univer-app-still'), false);
  assert.equal(p.d.getElementById('value'), input); assert.equal(input.value, 'kept');
  p.close();
});
const shell = '<table class="wrappertable">' +
  '<tr id="l1"><td class="header">Казахский университет <a class="bvi-open" href="#">BVI</a><a href="/lang/change/kz/"><img src="/img/kz.png"></a><img src="/img/ru.png"><a href="/lang/change/en/"><img src="/img/en.png"></a></td></tr>' +
  '<tr id="l2"><td><table class="inner"><tr><td class="left"></td><td class="center"><div id="logo"></div><div class="links"><a href="/user/pass/">Профиль</a><a href="/user/logout">Выход</a></div></td><td class="right"></td></tr></table></td></tr>' +
  '<tr id="l3"><td><table class="inner"><tr><td class="left"></td><td class="center"><a id="tab-home" href="/"><div class="center">Главная</div></a><a id="tab-b" class="active" href="/student/bachelor/"><div class="center">Бакалавр</div></a></td><td class="right"></td></tr></table></td></tr>' +
  '<tr id="l4"><td><div id="menu-home"><table id="tools"><tr><td><table class="tool"><tr><td><ul id="links"><li><a id="schedule" href="/student/myschedule/">Расписание</a></li></ul></td></tr><tr><td class="bottom-center">Учёба</td></tr></table></td></tr></table></div>CONTENT</td></tr>' +
  '<tr id="l5"><td><table class="inner"><tr><td class="center"><table id="copyright"><tr><td>Copyright</td></tr></table></td></tr></table></td></tr></table>';

test('desktop header merges logo, tabs and account into one bar and Original restores every node', () => {
  const p = page(shell.replace('CONTENT', ''), 'https://univer.kaznu.kz/student/bachelor/');
  const logo = p.d.getElementById('logo'), links = p.d.querySelector('.links'), tab = p.d.getElementById('tab-b');
  const homes = [logo.parentElement, links.parentElement, tab.parentElement], bvi = p.d.querySelector('.bvi-open'), header = p.d.querySelector('#l1 .header');
  apply(p);
  const bar = header.querySelector('.univer-app-header');
  assert.equal(logo.closest('.univer-app-brand').parentElement, bar);
  assert.equal(tab.parentElement, bar.querySelector('.univer-app-tabs'));
  assert.equal(links.parentElement, bar.querySelector('.univer-app-actions'));
  // Flags remain inside .header: the trusted flag listener and language probe depend on it.
  assert.ok(p.d.querySelector('.univer-app-flags').closest('.header'));
  assert.equal(p.w.__univerAutomationV1.probe().language, 'ru');
  assert.ok(p.d.getElementById('l2').classList.contains('univer-app-header-row'));
  assert.ok(p.d.getElementById('l3').classList.contains('univer-app-header-row'));
  assert.ok(p.d.querySelector('.univer-app-university').closest('#l5'));
  assert.ok(p.d.documentElement.classList.contains('univer-app-unified'));
  apply(p, false);
  assert.deepEqual([logo.parentElement, links.parentElement, tab.parentElement], homes);
  assert.equal(bvi.parentElement, header);
  assert.equal(p.d.querySelector('.univer-app-header,.univer-app-university,.univer-app-sections,.univer-app-header-row'), null);
  assert.equal(p.d.documentElement.classList.contains('univer-app-unified'), false);
  p.close();
});

test('Ctrl+K opens all sections with the cursor in the search, in any keyboard layout, and only in the reading view', () => {
  const p = page(shell.replace('CONTENT', '<p id="outside">Новости</p>'), 'https://univer.kaznu.kz/news/student');
  apply(p);
  const menu = p.d.querySelector('.univer-app-menu');
  const press = init => { const e = new p.w.KeyboardEvent('keydown', Object.assign({ code: 'KeyK', key: 'л', ctrlKey: true, bubbles: true, cancelable: true }, init)); p.d.body.dispatchEvent(e); return e.defaultPrevented; };
  assert.equal(press({ ctrlKey: false }), false); assert.equal(menu.open, false, 'a plain letter types as usual');
  assert.equal(press({ shiftKey: true }), false); assert.equal(press({ code: 'KeyJ' }), false); assert.equal(menu.open, false);
  assert.equal(press(), true); assert.equal(menu.open, true);
  assert.equal(p.d.activeElement, menu.querySelector('input[type=search]'));
  p.d.getElementById('outside').tabIndex = 0; p.d.getElementById('outside').focus();
  assert.equal(press(), true); assert.equal(menu.open, true, 'a second press keeps it open');
  assert.equal(p.d.activeElement, menu.querySelector('input[type=search]'), 'and returns to the search');
  apply(p, false);
  assert.equal(press(), false, 'Original keeps the site\'s own keys');
  p.close();
});

test('all-sections button opens the real site menu and Escape or an outside click closes it', async () => {
  const p = page(shell.replace('CONTENT', '<p id="outside">Новости</p>'), 'https://univer.kaznu.kz/news/student');
  let opened = 0; p.d.getElementById('schedule').addEventListener('click', e => { e.preventDefault(); opened++; });
  apply(p);
  const button = p.d.querySelector('.univer-app-sections'), menu = p.d.querySelector('.univer-app-menu');
  assert.equal(button.textContent, 'Все разделы');
  button.click(); assert.equal(menu.open, true);
  p.d.getElementById('schedule').click(); assert.equal(opened, 1);
  menu.querySelector('input').focus();
  p.d.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.equal(menu.open, false); assert.equal(p.d.activeElement, button);
  button.click(); assert.equal(menu.open, true);
  p.d.getElementById('outside').click(); assert.equal(menu.open, false);
  button.click(); assert.equal(menu.open, true);
  await new Promise(r => setTimeout(r, 200)); assert.equal(menu.open, true);
  menu.querySelector('.univer-app-menu-close').click(); assert.equal(menu.open, false);
  // With the app's Animations setting off, popups close at once.
  p.w.__univerMotion = false;
  button.click(); assert.equal(menu.open, true);
  p.d.getElementById('outside').click(); assert.equal(menu.open, false);
  button.click(); menu.querySelector('.univer-app-menu-close').click(); assert.equal(menu.open, false);
  apply(p, false);
  assert.equal(p.d.getElementById('tools').parentElement, p.d.getElementById('menu-home'));
  assert.equal(p.d.querySelector('.univer-app-menu,.univer-app-menu-close'), null);
  p.d.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  p.close();
});

test('menus animate on pointer opening, keep a fade in reduced motion and cancel immediately for keyboard or Original', async () => {
  const p = page(shell.replace('CONTENT', '<p id="outside">Новости</p>'), 'https://univer.kaznu.kz/news/student');
  apply(p); const records = []; let reduced = false;
  p.w.matchMedia = () => ({ matches: reduced });
  p.w.HTMLElement.prototype.animate = function (frames, options) {
    const record = { node: this, frames, options, cancelled: false, cancel() { this.cancelled = true; this.oncancel?.(); } };
    records.push(record); return record;
  };
  const button = p.d.querySelector('.univer-app-sections'), menu = p.d.querySelector('.univer-app-menu');
  button.click(); await new Promise(r => setTimeout(r, 20));
  const normal = records.find(r => r.node === menu);
  // A popup opens at the shared fold pace: 220 ms for a menu that has no height yet (jsdom), eased in and out.
  assert.ok(normal); assert.equal(normal.options.duration, 220); assert.equal(normal.options.easing, 'cubic-bezier(.45,0,.25,1)'); assert.match(normal.frames[0].transform, /translateY/);
  button.click(); assert.equal(menu.open, false); assert.equal(normal.cancelled, true);
  button.click(); await new Promise(r => setTimeout(r, 20));
  await new Promise(r => setTimeout(r, 260)); assert.equal(menu.open, true);
  p.d.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); assert.equal(menu.open, false);
  const before = records.length; button.click(); await new Promise(r => setTimeout(r, 20));
  assert.equal(records.length, before); assert.equal(menu.open, true);
  button.click(); p.d.documentElement.classList.remove('univer-app-keyboard'); reduced = true;
  button.click(); await new Promise(r => setTimeout(r, 20));
  const gentle = records.filter(r => r.node === menu).at(-1);
  assert.equal(gentle.options.duration, 180); assert.equal(gentle.frames[0].transform, 'none'); assert.equal(gentle.frames[0].opacity, .4);
  apply(p, false); assert.equal(gentle.cancelled, true); assert.equal(p.d.querySelector('.univer-app-menu'), null);
  p.close();
});

test('empty result rows and short row actions get quiet presentation and are restored', () => {
  const p = page('<table><tr id="l4"><td><table class="inner" id="services"><tr><th>#</th><th>Услуга</th><th></th></tr><tr><td>1</td><td>Справка</td><td><a id="apply" href="/ssc/apply/1">Подать заявку</a></td></tr></table>' +
    '<table class="inner" id="grades"><tr><th>Дисциплина</th><th>Балл</th></tr><tr id="empty"><td colspan="2"><b>Нет данных</b></td></tr></table></td></tr></table>', 'https://univer.kaznu.kz/ssc/UserServiceList');
  let clicks = 0; p.d.getElementById('apply').addEventListener('click', e => { e.preventDefault(); clicks++; });
  apply(p);
  assert.ok(p.d.getElementById('apply').classList.contains('univer-app-action-link'));
  assert.ok(p.d.getElementById('empty').classList.contains('univer-app-empty-row'));
  p.d.getElementById('apply').click(); assert.equal(clicks, 1);
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-action-link,.univer-app-empty-row'), null);
  p.close();
});

test('a new pin animates alone, preserves existing focus and handlers, and repeated updates do not replay it', () => {
  const p = page(shell.replace('CONTENT', ''), 'https://univer.kaznu.kz/news/student');
  apply(p); const records = [];
  p.w.HTMLElement.prototype.animate = function (frames, options) {
    const record = { node:this, frames, options, cancel() { this.cancelled = true; this.oncancel?.(); } };
    records.push(record); return record;
  };
  const original = p.d.querySelector('.univer-app-quick a'); original.focus();
  let clicks = 0; original.addEventListener('click', e => { e.preventDefault(); clicks++; });
  const pins = [{title:'Сообщения',url:'https://univer.kaznu.kz/my/messages/index/'}];
  p.w.__univerShortcutsV1.configure(pins);
  assert.equal(p.d.querySelector('.univer-app-quick a'), original); assert.equal(p.d.activeElement, original);
  assert.equal(records.length, 1); assert.equal(records[0].node.classList.contains('univer-app-pinned'), true);
  assert.equal(records[0].options.duration, 200);
  p.w.__univerShortcutsV1.configure(pins); apply(p); assert.equal(records.length, 1);
  original.click(); assert.equal(clicks, 1);
  p.w.__univerShortcutsV1.configure([]); assert.equal(records[0].cancelled, true);
  assert.equal(p.d.activeElement, original); apply(p, false); p.close();
});

test('content entrances retarget current opacity and keyboard interruption cancels every pending frame', () => {
  const p = page(shell.replace('CONTENT', '<div id="content">Сводка</div>'), 'https://univer.kaznu.kz/news/student');
  apply(p); const node = p.d.getElementById('content'), records = [];
  p.w.HTMLElement.prototype.animate = function (frames, options) {
    const record = { frames, options, cancel() { this.cancelled = true; this.oncancel?.(); } };
    records.push(record); return record;
  };
  p.w.__univerMotionUI.reveal(node, 'content');
  assert.match(records[0].frames[0].transform, /translateY/); assert.equal(records[0].options.duration, 200);
  node.style.opacity = '.65'; p.w.__univerMotionUI.reveal(node, 'content');
  assert.equal(records[0].cancelled, true); assert.equal(records[1].frames[0].opacity, '0.65');
  p.d.dispatchEvent(new p.w.KeyboardEvent('keydown', {key:'Tab',bubbles:true}));
  assert.equal(records[1].cancelled, true); p.w.__univerMotionUI.reveal(node, 'content'); assert.equal(records.length, 2);
  apply(p, false); p.close();
});

test('home shortcuts follow site menu links that appear after the first adaptation', async () => {
  const p = page(shell.replace('CONTENT', ''), 'https://univer.kaznu.kz/news/student');
  apply(p);
  const tiles = () => Array.from(p.d.querySelectorAll('.univer-app-quick a'));
  assert.deepEqual(tiles().map(a => a.textContent), ['Расписание', 'Успеваемость', 'Транскрипт', 'УМКД', 'СДО Moodle', 'Е-услуги']);
  p.d.getElementById('links').insertAdjacentHTML('beforeend', '<li><a id="umkd-site" href="/student/umkd/">УМКД</a></li>');
  await new Promise(r => setTimeout(r, 250));
  let clicks = 0; p.d.getElementById('umkd-site').addEventListener('click', e => { e.preventDefault(); clicks++; });
  tiles().find(a => a.textContent === 'УМКД').click(); assert.equal(clicks, 1);
  apply(p, false); assert.equal(p.d.querySelector('.univer-app-quick'), null);
  p.close();
});

test('owned disclosures grow and reverse from current geometry; keyboard cancels layout without changing nodes', async () => {
  const p = page(shell.replace('CONTENT',''), 'https://univer.kaznu.kz/news/student'); apply(p);
  const details=p.d.createElement('details'); details.className='univer-app-faq'; details.innerHTML='<summary>Вопрос</summary><p><a href="#answer">Ответ</a></p>';p.d.body.append(details);
  const link=details.querySelector('a'),records=[];
  details.getBoundingClientRect=()=>({width:300,height:details.open?180:40,left:0,top:0});
  p.w.HTMLElement.prototype.animate=function(frames,options){const r={node:this,frames,options,cancel(){this.cancelled=true;this.oncancel?.();}};records.push(r);return r;};
  details.querySelector('summary').dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,cancelable:true,detail:1}));
  // The box is content-box with a 1 px border above and below: its border-box heights run less those 2 px.
  const enter=records.find(r=>r.node===details);assert.equal(details.open,true);assert.equal(enter.frames[0].height,'38px');assert.equal(enter.frames[1].height,'178px');
  // Closing keeps the answer in place while the box closes up to its question; the element closes when the edge arrives.
  details.querySelector('summary').getBoundingClientRect=()=>({width:300,height:40,left:0,top:0,bottom:40});
  details.querySelector('summary').dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,cancelable:true,detail:1}));
  const exit=records.filter(r=>r.node===details).at(-1);assert.equal(details.open,true,'still open while it closes');assert.equal(enter.cancelled,true);
  assert.equal(details.hasAttribute('data-univer-shutting'),true,'the chevron turns back at once');
  assert.equal(exit.frames[0].height,'178px');assert.equal(exit.frames[1].height,'39px','the question and the own 1 px bottom border of the box (41 px border box)');assert.equal(exit.options.duration,248,'140 px: the shared pace');
  await new Promise(r=>setTimeout(r,10));assert.notEqual(exit.cancelled,true);assert.equal(details.querySelector('a'),link);
  p.d.dispatchEvent(new p.w.KeyboardEvent('keydown',{key:'Tab',bubbles:true}));assert.equal(exit.cancelled,true);assert.equal(details.style.height,'');
  assert.equal(details.open,false,'a key ends the close at once');assert.equal(details.hasAttribute('data-univer-shutting'),false);assert.equal(details.hasAttribute('data-univer-resizing'),false);
  apply(p,false);p.close();
});

test('reflow moves surviving tiles to new positions and cancellation clears every pending transform',()=>{
  const p=page(shell.replace('CONTENT',''),'https://univer.kaznu.kz/news/student');apply(p);
  const parent=p.d.createElement('div'),a=p.d.createElement('button'),b=p.d.createElement('button');parent.append(a,b);p.d.body.append(parent);
  [a,b].forEach(node=>node.getBoundingClientRect=()=>({width:120,height:40,left:0,top:Array.from(parent.children).indexOf(node)*70}));
  const records=[];p.w.HTMLElement.prototype.animate=function(frames,options){const r={node:this,frames,options,cancel(){this.cancelled=true;this.oncancel?.();}};records.push(r);return r;};
  p.w.__univerMotionUI.reflow(parent,()=>parent.insertBefore(b,a));assert.equal(records.length,2);
  assert.equal(records.find(r=>r.node===a).frames[0].transform,'translate(0px,-70px)');assert.equal(records.find(r=>r.node===b).frames[0].transform,'translate(0px,70px)');
  p.w.__univerMotionUI.stop();assert.ok(records.every(r=>r.cancelled));assert.equal(parent.firstChild,b);assert.equal(a.style.transform,'');apply(p,false);p.close();
});
test('reflow lets a card that lost its stretched height shrink instead of snapping',()=>{
  const p=page(shell.replace('CONTENT',''),'https://univer.kaznu.kz/news/student');apply(p);
  let opened=false;const records=[];
  p.w.HTMLElement.prototype.animate=function(frames,options){const r={node:this,frames,options,cancel(){}};records.push(r);return r;};
  const parent=p.d.createElement('div'),card=p.d.createElement('div');parent.append(card);p.d.body.append(parent);
  card.getBoundingClientRect=()=>({left:0,top:0,width:500,height:opened?311:340});
  p.w.__univerMotionUI.reflow(parent,()=>{opened=true;});
  assert.deepEqual(JSON.parse(JSON.stringify(records[0].frames)),[{transform:'none',minHeight:'340px'},{transform:'none',minHeight:'311px'}]);
  apply(p,false);p.close();
});
test('reflow animates card width changes, compensates scroll for an anchor button, and honours reduced motion and keyboard',()=>{
  const p=page(shell.replace('CONTENT',''),'https://univer.kaznu.kz/news/student');apply(p);
  let reduced=false, wide=false, y=200;
  p.w.matchMedia=()=>({matches:reduced,addEventListener(){},removeEventListener(){}});
  Object.defineProperty(p.w,'scrollY',{configurable:true,get:()=>y});
  const scrolls=[], records=[];
  p.w.scrollTo=o=>{scrolls.push(o.top);y=o.top;};
  p.w.HTMLElement.prototype.animate=function(frames,options){const r={node:this,frames,options,cancel(){this.cancelled=true;this.oncancel?.();}};records.push(r);return r;};
  const parent=p.d.createElement('div'), card=p.d.createElement('div'), anchor=p.d.createElement('button');
  card.append(anchor); parent.append(card); p.d.body.append(parent);
  card.getBoundingClientRect=()=>wide?{left:0,top:200,width:1200,height:240}:{left:620,top:200,width:580,height:240};
  anchor.getBoundingClientRect=()=>({left:0,top:(wide?600:450)-y,width:120,height:38});
  p.w.__univerMotionUI.reflow(parent,()=>{wide=true;},{anchor});
  assert.deepEqual(scrolls,[350],'scroll shifts by the anchor delta so the clicked button stays put');
  assert.equal(records.length,1);
  assert.deepEqual(JSON.parse(JSON.stringify(records[0].frames)),[{transform:'translate(620px,0px)',maxWidth:'580px'},{transform:'none',maxWidth:'1200px'}],'widening animates maxWidth and translate');
  // Narrowing back into its column animates minWidth.
  records.length=0; scrolls.length=0;
  p.w.__univerMotionUI.reflow(parent,()=>{wide=false;},{anchor});
  assert.deepEqual(scrolls,[200]);
  assert.equal(records[0].frames[0].minWidth,'1200px'); assert.equal(records[0].frames[1].minWidth,'580px');
  // Reduced motion: still compensates scroll, skips animations.
  reduced=true; records.length=0; scrolls.length=0;
  p.w.__univerMotionUI.reflow(parent,()=>{wide=true;},{anchor});
  assert.deepEqual(scrolls,[350],'reduced motion still compensates scroll');
  assert.equal(records.length,0,'reduced motion skips animation');
  apply(p,false);p.close();
});

test('popup exits are passive snapshots; real controls close now and Original or reopen removes the old visual',async()=>{
  const p=page(shell.replace('CONTENT',''),'https://univer.kaznu.kz/news/student');apply(p);const menu=p.d.querySelector('.univer-app-menu');
  menu.getBoundingClientRect=()=>({width:360,height:400,left:24,top:64});const records=[];
  p.w.HTMLElement.prototype.animate=function(frames,options){const r={node:this,frames,options,cancel(){this.cancelled=true;this.oncancel?.();}};records.push(r);return r;};
  menu.open=true;await new Promise(r=>setTimeout(r,10));p.w.__univerMotionUI.exit(menu);menu.open=false;
  await new Promise(r=>setTimeout(r,10));const ghost=p.d.querySelector('[data-univer-motion-ghost]');assert.ok(ghost);assert.equal(menu.open,false);
  assert.equal(ghost.getAttribute('aria-hidden'),'true');assert.equal(ghost.querySelector('[id],[name],[onclick]'),null);assert.equal(ghost.style.pointerEvents,'none');
  // The 400 px menu fades out at the shared fold pace.
  assert.equal(records.find(r=>r.node===ghost).options.duration,300);assert.equal(records.find(r=>r.node===ghost).options.easing,'cubic-bezier(.45,0,.25,1)');
  menu.open=true;await new Promise(r=>setTimeout(r,10));assert.equal(ghost.isConnected,false);assert.equal(menu.open,true);
  p.w.__univerMotionUI.exit(menu);menu.open=false;apply(p,false);assert.equal(p.d.querySelector('[data-univer-motion-ghost]'),null);p.close();
});

test('login page hides only an empty decorative tab strip', () => {
  const p = page('<table class="wrappertable"><tr id="l1"><td class="header"><img src="/img/ru.png"><a href="/lang/change/en/"><img src="/img/en.png"></a></td></tr>' +
    '<tr id="l2"><td><table class="inner"><tr><td class="center"><div id="logo"></div></td></tr></table></td></tr><tr id="l3"><td><table class="inner"><tr><td class="center"> </td></tr></table></td></tr>' +
    '<tr id="l4"><td><form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form></td></tr></table>', 'https://univer.kaznu.kz/user/login');
  apply(p);
  assert.ok(p.d.getElementById('l3').classList.contains('univer-app-header-row'));
  assert.equal(p.d.querySelector('.univer-app-sections').hidden, true);
  assert.equal(p.w.__univerAutomationV1.probe().loginReady, true);
  apply(p, false); assert.equal(p.d.getElementById('l3').className, ''); p.close();
});

function weekFixture(offsetWeeks) {
  const now = new Date(), monday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - ((now.getDay() + 6) % 7) + 7 * offsetWeeks);
  const sunday = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 6);
  const f = d => String(d.getDate()).padStart(2, '0') + '.' + String(d.getMonth() + 1).padStart(2, '0') + '.' + d.getFullYear();
  const days = ['ПН', 'ВТ', 'СР', 'ЧТ', 'ПТ', 'СБ'];
  const lesson = '<td class="field"><div class="groups"><div><p class="teacher">Предмет</p><p>Преподаватель</p></div></div></td>';
  return '<table><tr id="l4"><td><select id="weekSelect"><option selected>Неделя 06. ' + f(monday) + ' - ' + f(sunday) + '</option></select>' +
    '<div class="inner"><table class="schedule"><tr><th>&nbsp;</th>' + days.map(d => '<th>' + d + '</th>').join('') + '</tr>' +
    '<tr><td class="time"><div class="divIE" style="display:none">09:00-09:50</div></td>' + days.map(() => lesson).join('') + '</tr></table></div></td></tr></table>';
}

test('pointer day and grid changes move in their direction; programmatic selection cancels the old day',()=>{
  const p=page(weekFixture(0),'https://univer.kaznu.kz/student/myschedule/');apply(p);const records=[];
  p.w.HTMLElement.prototype.animate=function(frames,options){const r={node:this,frames,options,cancel(){this.cancelled=true;this.oncancel?.();}};records.push(r);return r;};
  const buttons=Array.from(p.d.querySelectorAll('.univer-app-days button')),other=buttons.find(b=>b.getAttribute('aria-pressed')!=='true');
  other.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));assert.ok(records.some(r=>/translateX/.test(r.frames[0].transform)));
  const previous=records.slice();buttons.find(b=>b!==other).click();assert.ok(previous.every(r=>r.cancelled));
  p.d.querySelector('.univer-app-schedule-modes button:nth-child(2)').dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.ok(records.some(r=>r.node.classList.contains('univer-app-schedule-scroll')&&/translateX/.test(r.frames[0].transform)));
  apply(p,false);p.close();
});

test('pointer zoom interpolates scale only and is immediate when reduced motion or keyboard is requested',()=>{
  const p=page(shell.replace('CONTENT',''),'https://univer.kaznu.kz/news/student');apply(p);const node=p.d.getElementById('l4'),records=[];let reduced=false;
  p.w.matchMedia=()=>({matches:reduced});p.w.HTMLElement.prototype.animate=function(frames,options){const r={frames,options,cancel(){this.cancelled=true;this.oncancel?.();}};records.push(r);return r;};
  p.w.__univerMotionUI.zoom(node,.9);assert.equal(records[0].frames[0].transform,'scale(0.9)');assert.equal(records[0].frames[0].transformOrigin,'top left');
  reduced=true;p.w.__univerMotionUI.zoom(node,1.1);assert.equal(records[0].cancelled,true);assert.equal(records.length,1);
  reduced=false;p.d.dispatchEvent(new p.w.KeyboardEvent('keydown',{key:'+',bubbles:true}));p.w.__univerMotionUI.zoom(node,1.1);assert.equal(records.length,1);apply(p,false);p.close();
});

test('schedule opens today and keeps today marked in all days', () => {
  const p = page(weekFixture(0), 'https://univer.kaznu.kz/student/myschedule/');
  apply(p);
  const today = (new Date().getDay() + 6) % 7, buttons = Array.from(p.d.querySelectorAll('.univer-app-days button'));
  if (today < 6) {
    assert.equal(buttons[today].getAttribute('aria-pressed'), 'true');
    assert.ok(buttons[today].classList.contains('univer-app-today'));
    assert.equal(p.d.querySelectorAll('.univer-app-day:not([hidden])').length, 1);
    assert.ok(p.d.querySelector('.univer-app-day.univer-app-today .univer-app-today-badge'));
    assert.ok(p.d.querySelectorAll('table.schedule th')[today + 1].classList.contains('univer-app-today-col'));
    buttons[buttons.length - 1].click();
    assert.equal(p.d.querySelectorAll('.univer-app-day:not([hidden])').length, 6);
    assert.ok(p.d.querySelector('.univer-app-day.univer-app-today'));
  } else assert.equal(buttons[buttons.length - 1].getAttribute('aria-pressed'), 'true');
  assert.equal(p.d.querySelector('td.time').dataset.univerTime, '09:00–09:50');
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-today-col,[data-univer-time],.univer-app-schedule'), null);
  p.close();
});

test('another week starts with all days and no today mark', () => {
  const p = page(weekFixture(1), 'https://univer.kaznu.kz/student/myschedule/');
  apply(p);
  const buttons = Array.from(p.d.querySelectorAll('.univer-app-days button'));
  assert.equal(buttons[buttons.length - 1].getAttribute('aria-pressed'), 'true');
  assert.equal(p.d.querySelector('.univer-app-today,.univer-app-today-col'), null);
  p.close();
});

function newsFixture() {
  const row = (day, month, important, text) => '<tr class="news"><td><div class="date"><span class="day">' + day + '</span> ' + month + '</div>' +
    (important ? '<div style="background-color: red"><span><font color="white"> Важное</font></span></div>' : '') + '</td><td><p><b>' + text + '</b></p></td></tr>';
  return '<table><tr id="l4"><td><table class="mt"><tr class="mid"><td class="tt"><div class="inner"><table class="inner"><tr><th>Дата</th><th>Новость</th></tr>' +
    row('07', '10.2026', true, 'Гранты для студентов') + row('28', '09.2026', false, 'Расписание экзаменов') + row('03', '09.2026', false, 'Ёлка и праздник') +
    '</table></div></td></tr><tr class="mid"><td class="tb">Страница 2 <a href="/my/news/1"><img src="/content/icons/left.png"></a> <a href="/my/news/3"><img src="/content/icons/right.png"></a></td></tr></table></td></tr></table>';
}

test('news can be searched, filtered by date and importance, and paged without the notice detour', () => {
  const p = page(newsFixture(), 'https://univer.kaznu.kz/news/student/2');
  apply(p);
  const rows = Array.from(p.d.querySelectorAll('tr.news')), query = p.d.querySelector('.univer-app-news-query');
  assert.ok(p.d.querySelector('td.tb').classList.contains('univer-app-news-pager-source'));
  const links = Array.from(p.d.querySelector('.univer-app-news-pager').querySelectorAll('a')).map(a => new URL(a.href).pathname);
  assert.ok(links.includes('/news/student')); assert.ok(links.includes('/news/student/3')); assert.ok(!links.some(l => l.startsWith('/my/news')));
  assert.equal(p.d.querySelector('.univer-app-news-pager a[aria-current=page]').textContent, '2');
  const jump = p.d.querySelector('.univer-app-news-jump');
  assert.equal(jump.querySelector('.univer-app-news-jump-label').textContent, 'Страница');
  assert.equal(jump.querySelector('input').getAttribute('aria-label'), 'Страница');
  query.value = 'елка'; query.dispatchEvent(new p.w.Event('input'));
  assert.deepEqual(rows.map(r => r.hidden), [true, true, false]);
  query.value = ''; query.dispatchEvent(new p.w.Event('input'));
  p.d.querySelector('.univer-app-news-important').click();
  assert.deepEqual(rows.map(r => r.hidden), [false, true, true]);
  p.d.querySelector('.univer-app-news-important').click();
  const from = p.d.querySelector('.univer-app-news-from'); from.value = '2026-09-20'; from.dispatchEvent(new p.w.Event('input'));
  assert.deepEqual(rows.map(r => r.hidden), [false, false, true]);
  assert.match(p.d.querySelector('.univer-app-news-count').textContent, /2 из 2\+/);
  p.d.querySelector('.univer-app-news-reset').click();
  assert.deepEqual(rows.map(r => r.hidden), [false, false, false]);
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-news-tools,.univer-app-news-pager,.univer-app-news-pager-source'), null);
  assert.ok(rows.every(r => !r.hidden));
  p.close();
});

function smartFixture(lessonDays, weeks) {
  const days = ['ПН', 'ВТ', 'СР', 'ЧТ', 'ПТ', 'СБ'];
  const cell = (day, text) => lessonDays.includes(day) ? '<td class="field"><div class="groups"><div><p class="teacher">' + text + '</p><p>Аудитория 1</p></div></div></td>' : '<td class="field"></td>';
  const row = (time, text) => '<tr><td class="time"><div class="divIE" style="display:none">' + time + '</div></td>' + days.map((_, i) => cell(i, text)).join('') + '</tr>';
  return '<table><tr id="l4"><td><select id="weekSelect">' + weeks.map((w, i) => '<option' + (i ? '' : ' selected') + '>' + w + '</option>').join('') + '</select>' +
    '<div class="inner"><table class="schedule"><tr><th>&nbsp;</th>' + days.map(d => '<th>' + d + '</th>').join('') + '</tr>' +
    row('09:00-09:50', 'Первая') + row('10:00-10:50', 'Вторая') + row('11:00-11:50', 'Третья') + '</table></div></td></tr></table>';
}
const october = ['Неделя 06. 05.10.2026 - 11.10.2026', 'Неделя 07. 12.10.2026 - 18.10.2026'];
function clocked(html, start) {
  const p = page(html, 'https://univer.kaznu.kz/student/myschedule/');
  p.clock = start; const Real = p.w.Date;
  p.w.Date = class extends Real { constructor(...a) { super(...(a.length ? a : p.clock)); } static now() { return new Real(...p.clock).getTime(); } };
  p.at = async time => { p.clock = time; p.w.dispatchEvent(new p.w.Event('resize')); await new Promise(r => setTimeout(r, 250)); };
  p.card = () => {
    const c = p.d.querySelector('.univer-app-now'), t = s => { const n = c.querySelector(s); return n.hidden ? '' : n.textContent; };
    return { hidden: c.hidden, state: c.dataset.state, title: t('.univer-app-now-title'), count: t('.univer-app-now-count'), subject: t('.univer-app-now-subject'),
      meta: t('.univer-app-now-meta'), note: t('.univer-app-now-note'), action: t('.univer-app-now-action'),
      progress: c.querySelector('.univer-app-now-progress').hidden ? '' : c.querySelector('.univer-app-now-progress>span').style.width };
  };
  apply(p); return p;
}

test('today marks finished, in-progress and next classes with live countdowns', async () => {
  // Wednesday 7 October 2026, 10:15, inside the second class.
  const p = clocked(smartFixture([2], october), [2026, 9, 7, 10, 15]);
  const marks = () => Array.from(p.d.querySelectorAll('.univer-app-day')[2].querySelectorAll('.univer-app-lesson')).map(c => c.querySelector('.univer-app-lesson-badge')?.textContent || '');
  assert.deepEqual(marks(), ['Прошла', 'Идёт · ещё 35 мин', 'Следующая · через 45 мин']);
  const cards = p.d.querySelectorAll('.univer-app-day')[2].querySelectorAll('.univer-app-lesson');
  assert.ok(cards[0].classList.contains('univer-app-lesson-done'));
  assert.ok(cards[1].classList.contains('univer-app-lesson-now'));
  assert.ok(cards[2].classList.contains('univer-app-lesson-next'));
  assert.equal(p.d.querySelector('.univer-app-day.univer-app-today .univer-app-day-range').textContent, '09:00–11:50');
  await p.at([2026, 9, 7, 11, 20]);
  assert.deepEqual(marks(), ['Прошла', 'Прошла', 'Идёт · ещё 30 мин']);
  assert.equal(p.d.querySelector('.univer-app-lesson-next'), null);
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-lesson-badge,.univer-app-schedule,.univer-app-now'), null);
  p.close();
});

test('summary card follows the day: before classes, in class, break, and after the last class', async () => {
  const p = clocked(smartFixture([2], october), [2026, 9, 7, 8, 30]);
  assert.deepEqual(p.card(), { hidden: false, state: 'first', title: 'До первой пары', count: 'через 30 мин', subject: 'Первая', meta: '09:00–09:50 · Ауд. 1', note: 'Сегодня 3 пары, 09:00–11:50', action: '', progress: '' });
  await p.at([2026, 9, 7, 10, 15]);
  assert.deepEqual(p.card(), { hidden: false, state: 'now', title: 'Идёт пара', count: 'ещё 35 мин', subject: 'Вторая', meta: '10:00–10:50 · Ауд. 1', note: 'Затем: Третья, 11:00', action: '', progress: '30%' });
  await p.at([2026, 9, 7, 10, 55]);
  assert.deepEqual(p.card(), { hidden: false, state: 'pause', title: 'Перемена', count: 'через 5 мин', subject: 'Третья', meta: '11:00–11:50 · Ауд. 1', note: 'Осталось 1 пара, до 11:50', action: '', progress: '' });
  await p.at([2026, 9, 7, 11, 30]);
  assert.equal(p.card().note, 'Это последняя пара сегодня');
  let loads = 0; p.d.getElementById('weekSelect').addEventListener('change', () => loads++);
  await p.at([2026, 9, 7, 12, 0]);
  assert.deepEqual(p.card(), { hidden: false, state: 'over', title: 'Пары на сегодня закончились', count: '', subject: 'На этой неделе пар больше нет', meta: '', note: 'Откройте следующую неделю, чтобы увидеть ближайшие пары', action: 'Следующая неделя', progress: '' });
  p.d.querySelector('.univer-app-now-action').click();
  assert.equal(p.d.getElementById('weekSelect').selectedIndex, 1); assert.equal(loads, 1);
  p.close();
});

test('a free weekday counts down to the next class and can open its day', async () => {
  // Tuesday 6 October 2026 with classes only on Thursday.
  const p = clocked(smartFixture([3], october), [2026, 9, 6, 10, 0]);
  assert.deepEqual(p.card(), { hidden: false, state: 'free', title: 'Сегодня пар нет', count: 'через 1 д 23 ч', subject: 'Первая', meta: 'в четверг в 09:00 · Ауд. 1', note: '', action: 'Показать день', progress: '' });
  assert.equal(p.d.querySelectorAll('.univer-app-day')[3].querySelector('.univer-app-lesson-badge').textContent, 'Следующая · через 1 д 23 ч');
  p.d.querySelector('.univer-app-now-action').click();
  await p.at([2026, 9, 6, 10, 0]);
  assert.equal(p.d.querySelectorAll('.univer-app-day')[3].hidden, false);
  assert.equal(p.card().action, '');
  p.close();
});

test('on a day off the next week shows a real countdown after the current week is finished', async () => {
  // Sunday 11 October 2026: the current week is over, the site loads the next one on request.
  const p = clocked(smartFixture([0, 2], october), [2026, 9, 11, 12, 0]);
  assert.equal(p.card().title, 'Сегодня выходной'); assert.equal(p.card().subject, 'На этой неделе пар больше нет');
  const select = p.d.getElementById('weekSelect'), table = p.d.querySelector('table.schedule');
  select.addEventListener('change', () => { table.rows[1].cells[1].querySelector('p').textContent = 'Алгебра'; });
  p.d.querySelector('.univer-app-now-action').click();
  await p.at([2026, 9, 11, 12, 0]);
  assert.deepEqual(p.card(), { hidden: false, state: 'dayOff', title: 'Сегодня выходной', count: 'через 21 ч', subject: 'Алгебра', meta: 'завтра в 09:00 · Ауд. 1', note: '', action: '', progress: '' });
  p.close();
});

test('a later week opened directly names its first class and a past week hides the card', async () => {
  const p = clocked(smartFixture([0, 5], october), [2026, 9, 9, 12, 0]);
  assert.equal(p.card().meta, 'завтра в 09:00 · Ауд. 1');
  p.d.getElementById('weekSelect').selectedIndex = 1;
  await p.at([2026, 9, 9, 12, 0]);
  assert.equal(p.card().title, 'Первая пара этой недели'); assert.equal(p.card().count, 'через 2 д 21 ч');
  await p.at([2026, 9, 25, 12, 0]);
  assert.equal(p.card().hidden, true);
  assert.equal(p.d.querySelector('.univer-app-lesson-badge'), null);
  p.close();
});

test('a site animation that only rewrites an inline style is adapted a few times a second; structure still adapts at once', async () => {
  const p = page(shell, 'https://univer.kaznu.kz/student/qa');
  let passes = 0;
  const observe = p.w.MutationObserver.prototype.observe;
  p.w.MutationObserver.prototype.observe = function () { passes++; return observe.apply(this, arguments); };
  apply(p);
  const ticker = p.d.createElement('div'); ticker.id = 'qa-ticker'; p.d.body.appendChild(ticker);
  await new Promise(resolve => setTimeout(resolve, 150));
  passes = 0; const began = Date.now();
  for (let i = 0; i < 60; i++) { ticker.style.left = i + 'px'; await new Promise(resolve => setTimeout(resolve, 5)); }
  await new Promise(resolve => setTimeout(resolve, 20));
  // At most one pass per 200 ms however slowly the timers run under a loaded test runner.
  const allowed = Math.floor((Date.now() - began) / 200) + 1;
  assert.ok(passes <= allowed, 'style-only frames adapted ' + passes + ' times, allowed ' + allowed);
  await new Promise(resolve => setTimeout(resolve, 350));
  const settled = passes;
  assert.ok(settled >= 1, 'the last style change is still adapted');
  ticker.className = 'shown';
  await new Promise(resolve => setTimeout(resolve, 150));
  assert.ok(passes > settled, 'a class change adapts without the style delay');
  apply(p, false);
  const after = passes; ticker.style.left = '1px';
  await new Promise(resolve => setTimeout(resolve, 300));
  assert.equal(passes, after, 'Original leaves no pending style pass');
  p.close();
});

test('typing and clicking change the keyboard class only when the modality changes, and never in Original', async () => {
  const p = page(shell, 'https://univer.kaznu.kz/student/qa');
  apply(p);
  await new Promise(resolve => setTimeout(resolve, 150));
  let writes = 0;
  new p.w.MutationObserver(records => { writes += records.filter(r => r.attributeName === 'class').length; })
    .observe(p.d.documentElement, { attributes: true, attributeFilter: ['class'] });
  for (let i = 0; i < 10; i++) p.d.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
  for (let i = 0; i < 5; i++) p.d.dispatchEvent(new p.w.Event('pointerdown', { bubbles: true }));
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(writes, 2, 'one add and one remove for ten keys and five clicks');
  apply(p, false);
  p.d.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
  assert.equal(p.d.documentElement.classList.contains('univer-app-keyboard'), false);
  p.close();
});

test('arrows beside the week list step to the neighbouring week through the site\'s own change handler, and Original puts the list back', () => {
  const weeks = [...october, 'Неделя 08. 19.10.2026 - 25.10.2026'];
  const p = page(smartFixture([2], weeks), 'https://univer.kaznu.kz/student/myschedule/'); apply(p);
  const select = p.d.getElementById('weekSelect'), nav = p.d.querySelector('.univer-app-week-nav');
  assert.equal(select.parentElement, nav);
  const [back, forward] = Array.from(nav.querySelectorAll('button.univer-app-week-step'));
  assert.deepEqual([back.getAttribute('aria-label'), forward.getAttribute('aria-label')], ['Предыдущая неделя', 'Следующая неделя']);
  // The first week has nothing before it.
  assert.equal(back.disabled, true); assert.equal(forward.disabled, false);
  let changes = 0; select.addEventListener('change', () => changes++);
  back.click(); assert.equal(select.selectedIndex, 0); assert.equal(changes, 0);
  forward.click(); assert.equal(select.selectedIndex, 1); assert.equal(changes, 1);
  assert.equal(back.disabled, false); forward.click(); assert.equal(select.selectedIndex, 2); assert.equal(changes, 2);
  assert.equal(forward.disabled, true); forward.click(); assert.equal(select.selectedIndex, 2); assert.equal(changes, 2);
  back.click(); assert.equal(select.selectedIndex, 1);
  apply(p, false);
  assert.equal(p.d.querySelector('.univer-app-week-nav'), null); assert.equal(select.parentElement.tagName, 'TD'); assert.equal(select.selectedIndex, 1);
  p.close();
});
test('the first pass lays the page out once, with every stylesheet in place: all widths are read before any hint is written', () => {
  const table = n => '<table id="t' + n + '"><tr><td>Дисциплина</td><td>Балл</td></tr></table>';
  const p = page(shell.replace('CONTENT', table(1) + table(2)), 'https://univer.kaznu.kz/student/bachelor/');
  const reads = [];
  const read = what => reads.push({ what, styled: !!p.d.getElementById('univer-desktop-style'), hints: p.d.querySelectorAll('.univer-app-scroll-hint').length });
  Object.defineProperty(p.w.HTMLElement.prototype, 'scrollWidth', { configurable: true, get() { if (this.matches('.univer-app-table-scroll')) read(this.firstElementChild.id); return 900; } });
  Object.defineProperty(p.w.HTMLElement.prototype, 'clientWidth', { configurable: true, get() { return 300; } });
  p.w.visualViewport = { offsetTop: 0, get height() { read('viewport'); return 700; }, addEventListener() {}, removeEventListener() {} };
  apply(p);
  assert.deepEqual(reads.map(r => r.what), ['t1', 't2', 'viewport']);
  assert.ok(reads.every(r => r.styled && r.hints === 0), JSON.stringify(reads));
  assert.equal(p.d.querySelectorAll('.univer-app-scroll-hint:not([hidden])').length, 2);
  assert.equal(p.d.documentElement.style.getPropertyValue('--univer-dialog-height'), '676px');
  p.close();
});

test('popup exit hoists CSS.supports, prunes non-visual nodes before copying, and fades a lightweight panel shell above 80 descendants while releasing focus immediately', async () => {
  const p = page(shell.replace('CONTENT', ''), 'https://univer.kaznu.kz/news/student');
  apply(p);
  const records = [];
  p.w.HTMLElement.prototype.animate = function (frames, options) {
    const r = { node: this, frames, options, cancel() { this.cancelled = true; this.oncancel?.(); } };
    records.push(r); return r;
  };
  let supportCalls = 0;
  p.w.CSS = { supports: () => { supportCalls++; return true; } };
  const styledTags = [];
  let styleCalls = 0;
  const origStyle = p.w.getComputedStyle.bind(p.w);
  p.w.getComputedStyle = (node, pseudo) => {
    styleCalls++;
    if (node && node.tagName) styledTags.push(node.tagName);
    return pseudo ? { content: 'none', getPropertyValue: () => '' } : origStyle(node);
  };
  // Small popup (<= 80 descendants) with non-visual nodes: CSS.supports runs once, script/style/iframe are never styled.
  const small = p.d.createElement('div');
  small.innerHTML = '<div id="keep"><span>Item</span><script>window.__x=1;</script><style>.x{}</style><iframe src="about:blank"></iframe></div>';
  p.d.body.appendChild(small);
  small.getBoundingClientRect = () => ({ width: 220, height: 120, left: 10, top: 20 });
  p.w.__univerMotionUI.exit(small);
  assert.equal(supportCalls, 1, 'CSS.supports is hoisted out of the node loop');
  assert.ok(!styledTags.some(t => /^(SCRIPT|STYLE|IFRAME)$/.test(t)), 'non-visual nodes pruned before style copy: ' + styledTags.join(','));
  const smallGhost = p.d.querySelector('[data-univer-motion-ghost]');
  assert.ok(smallGhost && !smallGhost.querySelector('script,style,iframe,[id]'));
  assert.equal(smallGhost.getAttribute('aria-hidden'), 'true');
  assert.equal(records.at(-1).options.duration, 244, 'a 120 px panel at the shared fold pace');
  p.w.__univerMotionUI.stop(small);
  small.remove();

  // Large popup (> 80 descendants): skips deep per-node style copy, fades lightweight wrapper, closes and returns focus immediately.
  const menu = p.d.querySelector('.univer-app-menu'), trigger = p.d.querySelector('.univer-app-sections');
  const list = menu.querySelector('#links');
  for (let i = 0; i < 60; i++) {
    const li = p.d.createElement('li');
    const a = p.d.createElement('a'); a.href = '/item/' + i; a.textContent = 'Раздел ' + i;
    li.appendChild(a); list.appendChild(li);
  }
  assert.ok(menu.getElementsByTagName('*').length > 80);
  menu.getBoundingClientRect = () => ({ width: 640, height: 420, left: 24, top: 64 });
  menu.style.backgroundColor = 'rgb(255, 255, 255)';
  menu.style.borderRadius = '12px';
  menu.open = true;
  menu.querySelector('input[type=search]').focus();
  supportCalls = 0; styleCalls = 0;
  menu.querySelector('.univer-app-menu-close').click();
  assert.equal(menu.open, false, 'large popup closes immediately');
  assert.equal(p.d.activeElement, trigger, 'keyboard focus returns to trigger immediately');
  assert.equal(supportCalls, 0, 'large popup skips pseudo-element CSS.supports checks');
  assert.ok(styleCalls <= 2, 'large popup skips per-descendant getComputedStyle, got ' + styleCalls);
  const largeGhost = p.d.querySelector('[data-univer-motion-ghost]');
  assert.ok(largeGhost, 'lightweight ghost wrapper is rendered');
  assert.equal(largeGhost.childElementCount, 0, 'lightweight ghost does not deep-clone descendants');
  assert.equal(largeGhost.getAttribute('aria-hidden'), 'true');
  assert.equal(largeGhost.style.pointerEvents, 'none');
  assert.equal(largeGhost.style.backgroundColor, 'rgb(255, 255, 255)');
  assert.equal(largeGhost.style.borderRadius, '12px');
  assert.equal(records.at(-1).node, largeGhost);
  assert.equal(records.at(-1).options.duration, 304, 'a 420 px panel at the shared fold pace');
  apply(p, false);
  assert.equal(p.d.querySelector('[data-univer-motion-ghost]'), null, 'Original removes lightweight ghost');
  p.close();
});

test('collapse folds a block before its change and runs the change at once without motion', () => {
  const p = page('<table><tr id="l4"><td><div id="box">Текст</div></td></tr></table>', 'https://univer.kaznu.kz/student/attendance/full/');
  apply(p);
  const box = p.d.getElementById('box'), ui = p.w.__univerMotionUI; let done = 0;
  box.getBoundingClientRect = () => ({ top: 0, height: 120, width: 300 });
  p.w.__univerMotion = false; ui.collapse(box, () => done++); assert.equal(done, 1, 'motion off: the change is made at once');
  p.w.__univerMotion = true; p.w.matchMedia = q => ({ matches: /reduce/.test(q), addEventListener() {}, removeEventListener() {} });
  ui.collapse(box, () => done++); assert.equal(done, 2, 'reduced motion: at once too');
  apply(p, false); p.close();
});

test('resize and collapse move max-height with height, so a row whose height a stylesheet holds at auto!important still grows and folds', () => {
  const p = page('<table><tr id="l4"><td><div id="box">Текст</div></td></tr></table>', 'https://univer.kaznu.kz/student/attendance/full/');
  apply(p);
  const box = p.d.getElementById('box'), ui = p.w.__univerMotionUI, runs = [];
  box.getBoundingClientRect = () => ({ top: 0, height: 120, width: 300 });
  p.w.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  box.animate = frames => { runs.push([...frames].map(f => [f.height, f.maxHeight])); return { cancel() {}, finish() {} }; };
  ui.resize(box, 40); ui.collapse(box, () => {});
  assert.deepEqual(runs, [[['40px', '40px'], ['120px', '120px']], [['120px', '120px'], ['0px', '0px']]]);
  apply(p, false); p.close();
});

function motionPage() {
  const p = page(shell.replace('CONTENT', '<div id="box">Текст</div>'), 'https://univer.kaznu.kz/news/student'); apply(p);
  p.w.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  const records = [];
  p.w.HTMLElement.prototype.animate = function (frames, options) { const r = { node: this, frames, options, playState: 'running', cancel() { this.cancelled = true; this.playState = 'idle'; this.oncancel?.(); }, end() { this.playState = 'finished'; this.onfinish?.(); } }; records.push(r); return r; };
  return { p, records, ui: p.w.__univerMotionUI, box: p.d.getElementById('box') };
}

test('folds take a pace that grows with the distance, within 220 to 420 ms, and ease in and out', () => {
  const { p, records, ui, box } = motionPage(); let height = 120;
  box.getBoundingClientRect = () => ({ top: 0, height, width: 300 });
  for (const [before, after, duration] of [[100, 120, 224], [248, 1248, 420], [248, 748, 320], [3000, 248, 420]]) {
    height = after; ui.resize(box, before);
    const run = records.at(-1);
    assert.equal(run.options.duration, duration, before + ' → ' + after);
    assert.equal(run.options.easing, 'cubic-bezier(.45,0,.25,1)');
    assert.deepEqual([run.frames[0].overflow, run.frames[1].overflow], ['hidden', 'hidden'], 'clipped to the moving edge');
  }
  height = 400; ui.collapse(box, () => {});
  assert.equal(records.at(-1).options.duration, 300); assert.equal(records.at(-1).options.easing, 'cubic-bezier(.45,0,.25,1)');
  // A caller that moves several boxes as one (the journal's rows) gives the whole distance.
  height = 40; ui.resize(box, 1, { distance: 1000 }); assert.equal(records.at(-1).options.duration, 420);
  // A caller inside a widening card can pass its pre-measured full-width target height.
  height = 320; ui.resize(box, 1, { target: 180 });
  assert.deepEqual([records.at(-1).frames[0].height, records.at(-1).frames[1].height], ['1px', '180px']);
  assert.equal(ui.span(0), 220); assert.equal(ui.span(-2000), 420);
  apply(p, false); p.close();
});

test('a running fold marks its box so !important caps step aside, and the mark goes when it ends or is stopped', () => {
  const { p, records, ui, box } = motionPage(); let height = 248;
  box.getBoundingClientRect = () => ({ top: 0, height, width: 300 });
  ui.resize(box, 1200);
  assert.equal(box.hasAttribute('data-univer-resizing'), true, 'closing: the cap waits');
  records.at(-1).end(); assert.equal(box.hasAttribute('data-univer-resizing'), false);
  height = 900; ui.resize(box, 248); assert.equal(box.hasAttribute('data-univer-resizing'), true);
  ui.stop(box); assert.equal(records.at(-1).cancelled, true); assert.equal(box.hasAttribute('data-univer-resizing'), false);
  // The folded news preview's cap is the one that held the close still: it now waits for the run.
  assert.match(desktop, /\.univer-app-news-body\[data-collapsed=true\]:not\(\[data-univer-resizing\]\) \{ max-height:var\(--u-news-preview\)!important; overflow:hidden!important; \}/);
  assert.doesNotMatch(desktop, /\.univer-app-news-body\[data-collapsed=true\] \{ max-height/);
  // Without motion nothing is marked and nothing runs.
  const count = records.length; p.w.__univerMotion = false; ui.resize(box, 1200);
  assert.equal(records.length, count); assert.equal(box.hasAttribute('data-univer-resizing'), false);
  apply(p, false); p.close();
});

test('a box closing above the reader scrolls the page back with its edge; the reader scrolling takes over at once', () => {
  const { p, records, ui, box } = motionPage(); let height = 248, top = -1500, y = 3000; const frames = [], scrolls = [];
  const row = p.d.createElement('div'); p.d.body.append(row); row.getBoundingClientRect = () => ({ top, height: 300, width: 300 });
  box.getBoundingClientRect = () => ({ top, height, width: 300 });
  Object.defineProperty(p.w, 'scrollY', { configurable: true, get: () => y });
  p.w.scrollTo = (o) => { scrolls.push(o.top); y = o.top; };
  p.w.requestAnimationFrame = (f) => { frames.push(f); return frames.length; }; p.w.cancelAnimationFrame = () => {};
  ui.resize(box, 2000, { follow: row });
  height = 1124; frames.shift()(); // half way through the fold
  assert.equal(scrolls.at(-1), 3000 + (1488 - 3000) / 2, 'half the fold, half the way back');
  records.at(-1).end(); assert.equal(scrolls.at(-1), 1488, 'the article starts just under the window top');
  // An article whose start is in view does not move the page; a wheel stops the page from following.
  top = 200; scrolls.length = 0; height = 248; ui.resize(box, 2000, { follow: row }); records.at(-1).end(); assert.deepEqual(scrolls, []);
  top = -1500; y = 3000; ui.resize(box, 2000, { follow: row });
  p.w.dispatchEvent(new p.w.Event('wheel')); records.at(-1).end(); assert.deepEqual(scrolls, [], 'the reader has the page');
  apply(p, false); p.close();
});

test('a click on a closing FAQ keeps it open and grows it back from where it is', () => {
  const p = page(shell.replace('CONTENT', ''), 'https://univer.kaznu.kz/news/student'); apply(p);
  p.w.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  const details = p.d.createElement('details'); details.className = 'univer-app-faq'; details.open = true;
  const summary = p.d.createElement('summary'); summary.textContent = 'Вопрос'; const answer = p.d.createElement('p'); answer.textContent = 'Ответ';
  details.append(summary, answer); p.d.body.append(details);
  let height = 300; details.getBoundingClientRect = () => ({ width: 300, height, top: 0 }); summary.getBoundingClientRect = () => ({ width: 300, height: 40, top: 0, bottom: 40 });
  const records = []; p.w.HTMLElement.prototype.animate = function (frames, options) { const r = { node: this, frames, options, cancel() { this.cancelled = true; this.oncancel?.(); } }; records.push(r); return r; };
  const click = () => summary.dispatchEvent(new p.w.MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
  click(); const closing = records.at(-1); assert.equal(details.open, true); assert.equal(closing.frames[1].height, '39px', 'down to the question and the bottom border (content box: less both borders)');
  // Half way shut, the box is 170 px tall; a second click stops the close and the box grows back from there.
  height = 170; const count = records.length; details.getBoundingClientRect = () => ({ width: 300, height: records.length > count || closing.cancelled ? 300 : 170, top: 0 });
  click();
  assert.equal(closing.cancelled, true); assert.equal(details.open, true, 'a second click keeps it open');
  assert.equal(details.hasAttribute('data-univer-shutting'), false);
  const back = records.at(-1); assert.notEqual(back, closing); assert.equal(back.frames[0].height, '168px'); assert.equal(back.frames[1].height, '298px');
  apply(p, false); p.close();
});

// The helper measures border boxes; a content-box box whose padding changes with its state (the open news body gains
// 28 px below) runs those heights less its current padding and borders, so it starts and ends where it stands.
test('a fold runs border-box heights on the box own sizing basis, so a padding change never snaps', () => {
  const { p, records, ui, box } = motionPage(); let height = 900;
  box.getBoundingClientRect = () => ({ top: 0, height, width: 300 });
  box.style.paddingBottom = '28px'; box.style.boxSizing = 'content-box';
  ui.resize(box, 248);
  let run = records.at(-1);
  assert.deepEqual([run.frames[0].height, run.frames[0].maxHeight], ['220px', '220px'], 'starts at the 248 px preview: 220 + 28');
  assert.deepEqual([run.frames[1].height, run.frames[1].maxHeight], ['872px', '872px'], 'ends at the article: 872 + 28 = 900, its height after the run');
  run.end();
  // Closing: the same basis from the article down to the preview.
  height = 248; ui.resize(box, 900); run = records.at(-1);
  assert.deepEqual([run.frames[0].height, run.frames[1].height], ['872px', '220px']);
  run.end();
  // A border-box box runs the measured heights as they are.
  box.style.boxSizing = 'border-box'; height = 900; ui.resize(box, 248); run = records.at(-1);
  assert.deepEqual([run.frames[0].height, run.frames[1].height], ['248px', '900px']);
  run.end(); box.style.boxSizing = 'content-box';
  // collapse and shut use the same basis.
  height = 400; ui.collapse(box, () => {}); assert.equal(records.at(-1).frames[0].height, '372px'); assert.equal(records.at(-1).frames[1].height, '0px');
  ui.stop(box); ui.shut(box, 100, () => {}); assert.deepEqual([records.at(-1).frames[0].height, records.at(-1).frames[1].height], ['372px', '72px']);
  apply(p, false); p.close();
});

test('a close click on an FAQ still opening closes from where its edge is', () => {
  const p = page(shell.replace('CONTENT', ''), 'https://univer.kaznu.kz/news/student'); apply(p);
  p.w.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  const details = p.d.createElement('details'); details.className = 'univer-app-faq';
  const summary = p.d.createElement('summary'); summary.textContent = 'Вопрос'; const answer = p.d.createElement('p'); answer.textContent = 'Ответ';
  details.append(summary, answer); p.d.body.append(details);
  const records = []; p.w.HTMLElement.prototype.animate = function (frames, options) { const r = { node: this, frames, options, cancel() { this.cancelled = true; this.oncancel?.(); } }; records.push(r); return r; };
  // While the opening run is live the box is 110 px on screen, half way to its 180 px.
  const opening = () => records.find(r => r.node === details && r.frames[1].height === '178px' && !r.cancelled);
  details.getBoundingClientRect = () => ({ width: 300, height: opening() ? 110 : details.open ? 180 : 40, top: 0 });
  summary.getBoundingClientRect = () => ({ width: 300, height: 40, top: 0, bottom: 40 });
  const click = () => summary.dispatchEvent(new p.w.MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
  click(); const enter = opening(); assert.ok(enter); assert.equal(details.open, true);
  click();
  const closing = records.at(-1);
  assert.equal(enter.cancelled, true); assert.notEqual(closing, enter);
  assert.equal(closing.frames[0].height, '108px', 'from the 110 px on screen (less both borders), not the full 180 px');
  assert.equal(closing.frames[1].height, '39px');
  assert.equal(closing.options.duration, 234, '69 px left to close: the shared pace');
  apply(p, false); p.close();
});

test('the account panel and the menu open and close on a pointer at the shared fold pace; keys and reduced motion keep theirs', async () => {
  const p = page(shell.replace('CONTENT', ''), 'https://univer.kaznu.kz/news/student'); apply(p);
  let reduced = false; p.w.matchMedia = () => ({ matches: reduced, addEventListener() {}, removeEventListener() {} });
  const records = []; p.w.HTMLElement.prototype.animate = function (frames, options) { const r = { node: this, frames, options, cancel() { this.cancelled = true; this.oncancel?.(); } }; records.push(r); return r; };
  const account = p.d.querySelector('details.univer-app-account'), summary = account.querySelector('summary'), body = account.querySelector('summary+div');
  body.getBoundingClientRect = () => ({ width: 240, height: 200, left: 0, top: 40 });
  account.getBoundingClientRect = () => ({ width: 120, height: 40, left: 120, top: 0 });
  const click = (target, detail = 1) => target.dispatchEvent(new p.w.MouseEvent('click', { bubbles: true, cancelable: true, detail }));
  click(summary); await new Promise(r => setTimeout(r, 20));
  assert.equal(account.open, true);
  const open = records.filter(r => r.node === body).at(-1);
  assert.ok(open, 'a pointer opening moves the panel in'); assert.equal(open.options.duration, 260, '200 px panel'); assert.equal(open.options.easing, 'cubic-bezier(.45,0,.25,1)');
  assert.match(open.frames[0].transform, /translateY/);
  summary.focus(); click(summary);
  assert.equal(account.open, false, 'the real panel closes at once'); assert.equal(p.d.activeElement, summary, 'focus stays on the summary');
  const ghost = p.d.querySelector('[data-univer-motion-ghost]'); assert.ok(ghost);
  const fade = records.find(r => r.node === ghost); assert.equal(fade.options.duration, 260, '200 px panel'); assert.equal(fade.options.easing, 'cubic-bezier(.45,0,.25,1)');
  assert.equal(ghost.style.top, '40px', 'only the panel is copied');
  await new Promise(r => setTimeout(r, 20));
  // The menu's own summary at the same pace.
  const menu = p.d.querySelector('.univer-app-menu'); menu.getBoundingClientRect = () => ({ width: 360, height: 300, left: 24, top: 64 });
  click(menu.querySelector('summary')); await new Promise(r => setTimeout(r, 20));
  assert.equal(menu.open, true); const shown = records.filter(r => r.node === menu).at(-1); assert.equal(shown.options.duration, 280); assert.equal(shown.options.easing, 'cubic-bezier(.45,0,.25,1)');
  click(menu.querySelector('summary')); assert.equal(menu.open, false);
  const menuGhost = Array.from(p.d.querySelectorAll('[data-univer-motion-ghost]')).at(-1); assert.equal(records.find(r => r.node === menuGhost).options.duration, 280);
  await new Promise(r => setTimeout(r, 20));
  // Reduced motion keeps its short fades without movement.
  reduced = true; click(summary); await new Promise(r => setTimeout(r, 20));
  const gentle = records.filter(r => r.node === body).at(-1); assert.equal(gentle.options.duration, 180); assert.equal(gentle.frames[0].transform, 'none');
  click(summary); const still = records.at(-1); assert.equal(still.options.duration, 140); assert.equal(still.frames[1].transform, 'none');
  reduced = false; await new Promise(r => setTimeout(r, 20));
  // A key opens and closes at once.
  p.d.dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
  const count = records.length; account.open = true; await new Promise(r => setTimeout(r, 20));
  assert.equal(records.length, count, 'no motion for a key'); account.open = false;
  apply(p, false); p.close();
});
