const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const {JSDOM} = require('jsdom');
const common = fs.readFileSync('src/Univer.Desktop/Assets/page-common.js', 'utf8');
const news = common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/news.js','utf8');
const study = common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/study-links.js','utf8');
const origin = 'https://univer.kaznu.kz';
const wait = ms => new Promise(resolve=>setTimeout(resolve,ms));
function newsRow(day,name,extra='') { return `<tr class="news"><td><div class="date"><span class="day">${day}</span> 10.2026</div></td><td><p>${name}</p>${extra}</td></tr>`; }
function newsPage(rows,page=1,next=false) { return `<table><tr id="l4"><td><table class="univer-app-news"><tbody>${rows.join('')}</tbody></table><div><table><tr><td class="tb">Страница ${page}${next?`<a href="/my/news/${page+1}"><img src="/right.png"></a>`:''}</td></tr></table></div></td></tr></table>`; }
function response(path,html,status=200) { return {status,url:origin+path,headers:{get:()=>null},text:async()=>html}; }
function context(html,path,script) {
  const dom = new JSDOM(html,{url:origin+path,runScripts:'outside-only'});
  Object.defineProperty(dom.window.document,'readyState',{value:'complete',configurable:true});
  dom.window.eval(script);
  return {w:dom.window,d:dom.window.document,close:()=>dom.window.close()};
}
function input(p,cls,value) { const n=p.d.querySelector('.univer-app-news-'+cls); n.value=value; n.dispatchEvent(new p.w.Event('input')); }
function results(p) { return Array.from(p.d.querySelectorAll('[data-univer-results] tr.news')).map(n=>n.cells[1].textContent); }
function newsGeometry(p, height) {
  const proto=p.w.HTMLElement.prototype, rect=proto.getBoundingClientRect;
  Object.defineProperty(proto,'scrollHeight',{configurable:true,get(){return this.classList.contains('univer-app-news-body')?height(this):0;}});
  proto.getBoundingClientRect=function(){return this.classList.contains('univer-app-news-body')?{width:this.closest('[hidden],[data-univer-news-source]')?0:720,top:0,height:height(this)}:rect.call(this);};
}
test('long news expand independently, preserve original links, localize controls and restore the exact source',async()=>{
  const p=context(newsPage([newsRow('09','Длинная новость','<p>Полный текст</p><a href="#tail" id="tail">Ссылка в конце</a>'),newsRow('08','Короткая новость')]),'/news/student',news);
  newsGeometry(p,body=>body.textContent.includes('Длинная')?600:80);
  const source=p.d.querySelector('table.univer-app-news'), original=source.innerHTML, link=p.d.getElementById('tail');
  let clicks=0;link.addEventListener('click',e=>{e.preventDefault();clicks++;});
  p.w.__univerNewsV1.apply('ru');
  const buttons=source.querySelectorAll('.univer-app-news-toggle'), body=source.querySelector('.univer-app-news-body');
  assert.equal(buttons[0].textContent,'Показать полностью');assert.equal(buttons[0].getAttribute('aria-expanded'),'false');assert.equal(buttons[1].hidden,true);
  assert.equal(body.getAttribute('data-collapsed'),'true');assert.equal(p.d.getElementById(buttons[0].getAttribute('aria-controls')),body);
  buttons[0].click();assert.equal(buttons[0].textContent,'Свернуть');assert.equal(body.getAttribute('data-collapsed'),'false');
  p.w.__univerNewsV1.apply('en');assert.equal(buttons[0].textContent,'Collapse');
  p.w.__univerNewsV1.apply('kz');assert.equal(buttons[0].textContent,'Жасыру');buttons[0].click();assert.equal(buttons[0].textContent,'Толық көрсету');
  p.w.__univerNewsV1.apply('ru');p.w.__univerNewsV1.apply('ru');assert.equal(source.querySelectorAll('.univer-app-news-body').length,2);
  assert.equal(p.d.getElementById('tail'),link);link.click();assert.equal(clicks,1);
  p.w.__univerNewsV1.release();await wait(10);assert.equal(source.innerHTML,original);assert.equal(p.d.getElementById('tail'),link);link.click();assert.equal(clicks,2);p.close();
});
test('filtered news search hidden article text, retain expansion across result pages and exclude disclosure labels',async()=>{
  const p=context(newsPage([newsRow('09','Длинная новость A','<p>Скрытая концовка</p>'),newsRow('08','Длинная новость B')],1,true),'/news/student',news);
  newsGeometry(p,()=>600);
  p.w.fetch=async path=>response(path,newsPage([newsRow('07','Длинная новость C'),newsRow('06','Длинная новость D')],2,false));
  p.w.__univerNewsV1.apply('ru');input(p,'query','новость');await wait(260);
  let first=p.d.querySelector('[data-univer-results] .univer-app-news-toggle');assert.equal(first.getAttribute('aria-expanded'),'false');first.click();
  p.d.querySelector('.univer-app-news-pager-top a:last-of-type').click();
  assert.equal(p.d.querySelector('[data-univer-results] .univer-app-news-toggle').getAttribute('aria-expanded'),'false');
  p.d.querySelector('.univer-app-news-pager-top a:first-of-type').click();
  assert.equal(p.d.querySelector('[data-univer-results] .univer-app-news-toggle').getAttribute('aria-expanded'),'true');
  assert.equal(p.d.querySelector('[data-univer-results] .univer-app-news-body .univer-app-news-body'),null);
  input(p,'query','Скрытая концовка');await wait(260);assert.equal(p.d.querySelectorAll('[data-univer-results] tr.news').length,1);
  input(p,'query','Показать полностью');await wait(260);assert.equal(p.d.querySelectorAll('[data-univer-results] tr.news').length,0);
  p.d.querySelector('.univer-app-news-reset').click();assert.equal(p.d.querySelector('table:not([data-univer-results]) .univer-app-news-toggle').getAttribute('aria-expanded'),'true');
  p.w.__univerNewsV1.release();assert.equal(p.d.querySelector('.univer-app-news-body,.univer-app-news-toggle'),null);p.close();
});

test('pointer news expansion unrolls from the preview without a fade; collapse follows its row; focus and release cancel it',()=>{
  const p=context(newsPage([newsRow('09','Длинная новость','<a href="#tail" id="tail">Конец</a>')]),'/news/student',news);
  newsGeometry(p,()=>600); const calls=[];
  p.w.__univerMotionUI={reveal(node,kind){calls.push(['reveal',node,kind]);},stop(node){calls.push(['stop',node]);},resize(node,before){calls.push(['resize',node,before]);},shut(node,target,done,options){calls.push(['shut',node,target,options]);done();}};
  p.w.__univerNewsV1.apply('ru'); const body=p.d.querySelector('.univer-app-news-body'),button=p.d.querySelector('.univer-app-news-toggle');
  body.style.setProperty('--u-news-preview', '248px');
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.equal(body.getAttribute('data-collapsed'),'false'); assert.equal(calls.at(-1)[0],'resize'); assert.equal(calls.filter(c=>c[0]==='reveal').length,0,'the preview is already on screen: no fade');
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.equal(body.getAttribute('data-collapsed'),'true'); assert.equal(calls.at(-1)[0],'shut'); assert.equal(calls.at(-1)[3].follow,body.closest('tr'),'the page follows the article back to its start');
  const resized=calls.filter(c=>c[0]==='resize').length;
  const before=calls.filter(c=>c[0]==='reveal').length; button.click();
  assert.equal(body.getAttribute('data-collapsed'),'false'); assert.equal(calls.filter(c=>c[0]==='reveal').length,before); assert.equal(calls.filter(c=>c[0]==='resize').length,resized,'a key opens at once');
  p.d.getElementById('tail').focus(); assert.equal(calls.at(-1)[0],'stop');
  p.w.__univerNewsV1.release(); assert.equal(calls.at(-1)[0],'stop'); assert.equal(p.d.querySelector('.univer-app-news-body'),null); p.close();
});
test('news react to resize, late media and new rows; keyboard focus reveals a clipped link',async()=>{
  const p=context(newsPage([newsRow('09','Новость','<img src="/image.png"><a href="#more" id="more">Подробнее</a>')]),'/news/student',news);
  let height=100;newsGeometry(p,()=>height);p.w.__univerNewsV1.apply('ru');
  const button=p.d.querySelector('.univer-app-news-toggle'),body=p.d.querySelector('.univer-app-news-body');assert.equal(button.hidden,true);
  height=500;p.w.dispatchEvent(new p.w.Event('resize'));await wait(10);assert.equal(button.hidden,false);assert.equal(body.getAttribute('data-collapsed'),'true');
  p.d.getElementById('more').focus();assert.equal(button.getAttribute('aria-expanded'),'true');button.click();assert.equal(body.getAttribute('data-collapsed'),'true');
  height=80;p.d.querySelector('img').dispatchEvent(new p.w.Event('load'));await wait(10);assert.equal(button.hidden,true);assert.equal(body.getAttribute('data-collapsed'),'false');
  p.d.querySelector('table.univer-app-news tbody').insertAdjacentHTML('beforeend',newsRow('08','Поздняя новость'));height=400;
  p.w.__univerNewsV1.apply('ru');assert.equal(p.d.querySelectorAll('.univer-app-news-toggle:not([hidden])').length,2);
  p.w.__univerNewsV1.release();p.w.dispatchEvent(new p.w.Event('resize'));await wait(10);assert.equal(p.d.querySelector('.univer-app-news-toggle'),null);p.close();
});

test('closing a long article holds the bottom scroll position until the height starts moving', () => {
  const p = context(newsPage([newsRow('09', 'Длинная новость')]), '/news/student', news);
  try {
    p.d.documentElement.classList.add('univer-mobile');
    p.w.eval(fs.readFileSync('src/Univer.Desktop/Assets/desktop.js', 'utf8'));
    newsGeometry(p, () => 2000); p.w.__univerNewsV1.apply('ru');
    const body = p.d.querySelector('.univer-app-news-body'), button = p.d.querySelector('.univer-app-news-toggle');
    body.style.setProperty('--u-news-preview', '248px');
    button.click();
    let y = 1900, height = 2000, run;
    const frames = [], scrolls = [];
    Object.defineProperty(p.w, 'scrollY', { get: () => y });
    body.closest('tr').getBoundingClientRect = () => ({ top: 500 - y });
    body.getBoundingClientRect = () => {
      if (body.dataset.collapsed === 'true' && !body.hasAttribute('data-univer-resizing')) {
        y = Math.min(y, 148); return { width: 720, height: 248 };
      }
      return { width: 720, height };
    };
    body.animate = keyframes => { run = { keyframes, cancel() {} }; return run; };
    p.w.requestAnimationFrame = callback => frames.push(callback);
    p.w.cancelAnimationFrame = () => {};
    p.w.scrollTo = ({ top }) => { y = top; scrolls.push(top); };
    button.dispatchEvent(new p.w.MouseEvent('click', { detail: 1 }));
    assert.equal(y, 1900, 'the page is still full height on click');
    assert.deepEqual(Array.from(run.keyframes, frame => frame.height), ['2000px', '248px']);
    height = 1124; frames.shift()();
    assert.deepEqual(scrolls, [1194], 'the glide starts from the scroll position before any shrink');
    run.playState = 'finished'; run.onfinish();
    assert.equal(body.dataset.collapsed, 'true');
  } finally { p.close(); }
});
test('date search aggregates pages, hides unnecessary pagination and search, and clears to the intact source',async()=>{
  const p=context(newsPage([newsRow('09','One'),newsRow('08','Two')],1,true),'/news/student',news);
  const calls=[]; p.w.fetch=async path=>{calls.push(path); return response(path,newsPage([newsRow('07','Three'),newsRow('06','Four')],2,false));};
  const source=p.d.querySelector('table.univer-app-news'), original=source.innerHTML;
  p.w.__univerNewsV1.apply('ru'); input(p,'from','2026-10-08'); await wait(270);
  assert.deepEqual(results(p),['One','Two']);
  assert.deepEqual(calls,['/news/student/2']);
  assert.ok(Array.from(p.d.querySelectorAll('.univer-app-news-pager')).every(n=>n.hidden));
  assert.equal(p.d.querySelector('.univer-app-news-all').hidden,true);
  assert.equal(p.d.querySelector('.univer-app-news-count').textContent,'Показано 2 из 2');
  p.d.querySelector('.univer-app-news-reset').click(); assert.equal(source.hasAttribute('data-univer-news-source'),false);
  assert.equal(p.w.sessionStorage.getItem('univer.desktop.news-filter.v1'),null);
  p.w.__univerNewsV1.release(); assert.equal(source.innerHTML,original); p.close();
});
test('filtered pages are packed to the source limit and retain filters and selected page after reopening',async()=>{
  const html=newsPage([newsRow('09','One'),newsRow('08','Two')],1,true);
  const second=newsPage([newsRow('07','Three'),newsRow('06','Four')],2,false);
  const p=context(html,'/news/student',news); p.w.fetch=async path=>response(path,second);
  p.w.__univerNewsV1.apply('ru'); input(p,'from','2026-10-06'); await wait(260);
  assert.deepEqual(results(p),['One','Two']);
  const links=Array.from(p.d.querySelector('.univer-app-news-pager').querySelectorAll('a:not(.univer-app-news-step)'));
  assert.deepEqual(links.map(a=>a.textContent),['1','2']); links[1].click();
  assert.deepEqual(results(p),['Three','Four']);
  assert.equal(p.d.querySelector('.univer-app-news-from').value,'2026-10-06');
  const saved=p.w.sessionStorage.getItem('univer.desktop.news-filter.v1');
  assert.equal(JSON.parse(saved).page,2); p.close();
  const q=context(html,'/news/student',news); q.w.sessionStorage.setItem('univer.desktop.news-filter.v1',saved);
  // Reinitialize exactly as a new document would, with storage available before the script runs.
  q.w.__univerNewsV1=null; q.w.eval(news); q.w.fetch=async path=>response(path,second); q.w.__univerNewsV1.apply('ru'); await wait(260);
  assert.equal(q.d.querySelector('.univer-app-news-from').value,'2026-10-06');
  assert.deepEqual(results(q),['Three','Four']); assert.equal(q.d.querySelector('a[aria-current=page]').textContent,'2'); q.close();
});
test('broadening a date range resumes beyond the formerly sufficient earlier page without refetching it',async()=>{
  const p=context(newsPage([newsRow('09','One'),newsRow('08','Two')],1,true),'/news/student',news), calls=[];
  p.w.fetch=async path=>{calls.push(path); return response(path,path.endsWith('/2')?newsPage([newsRow('07','Three'),newsRow('06','Four')],2,true):newsPage([newsRow('05','Five')],3,false));};
  p.w.__univerNewsV1.apply('ru'); input(p,'from','2026-10-08'); await wait(260); assert.deepEqual(calls,['/news/student/2']);
  input(p,'from','2026-10-05'); await wait(260);
  assert.deepEqual(calls,['/news/student/2','/news/student/3']); assert.match(p.d.querySelector('.univer-app-news-count').textContent,/из 5$/); p.close();
});
test('late cancelled search never replaces a reset or a newer filter, and failed searches remain visibly incomplete',async()=>{
  const p=context(newsPage([newsRow('09','One'),newsRow('08','Two')],1,true),'/news/student',news);
  let finish; p.w.fetch=()=>new Promise(resolve=>finish=resolve);
  p.w.__univerNewsV1.apply('ru'); input(p,'query','One'); await wait(230);
  p.d.querySelector('.univer-app-news-reset').click();
  finish(response('/news/student/2',newsPage([newsRow('07','Old result')],2,false))); await wait(20);
  assert.equal(p.d.querySelector('.univer-app-news-results').hidden,true);
  p.w.fetch=async()=>response('/user/login','<form id="login_frm"></form>',213);
  input(p,'query','Two'); await wait(260);
  assert.match(p.d.querySelector('.univer-app-news-status').textContent,/не завершён/);
  assert.equal(p.d.querySelector('.univer-app-news-all').hidden,false); p.close();
});
test('single-page feeds need no pager or all-pages search; fetched news cannot inject active markup',async()=>{
  const p=context(newsPage([newsRow('09','One')]),'/news/student',news);
  p.w.fetch=async()=>{throw Error('No request expected');}; p.w.__univerNewsV1.apply('ru');
  input(p,'from','2026-10-01'); await wait(250);
  assert.ok(Array.from(p.d.querySelectorAll('.univer-app-news-pager')).every(n=>n.hidden));
  assert.equal(p.d.querySelector('.univer-app-news-all').hidden,true); p.close();
  const q=context(newsPage([newsRow('09','One')],1,true),'/news/student',news);
  q.w.fetch=async path=>response(path,newsPage([newsRow('08','Two','<a href="javascript:alert(1)" onclick="alert(2)">bad</a><img src="https://evil.test/pixel"><base href="https://evil.test/"><script>alert(3)</script><video src="https://evil.test/clip" autoplay></video>')],2,false));
  q.w.__univerNewsV1.apply('ru'); input(q,'from','2026-10-01'); await wait(260);
  q.d.querySelector('.univer-app-news-pager a:last-of-type').click();
  const output=q.d.querySelector('[data-univer-results]');
  assert.equal(output.querySelector('script,base,video,[onclick],[href^="javascript:"],[src^="https://evil"]'),null); q.close();
});
test('unfiltered news expose direct first five and last pages with gaps, and cache only the discovered count',async()=>{
  const p=context(newsPage([newsRow('09','One')],1,true),'/news/student',news),calls=[];
  p.w.fetch=async path=>{const n=+path.split('/').pop();calls.push(n);return response(path,newsPage(n<=20?[newsRow('08','Page '+n)]:[],n,n<20));};
  p.w.__univerNewsV1.apply('ru');await wait(480);
  const pager=p.d.querySelector('.univer-app-news-pager-top');
  assert.deepEqual(Array.from(pager.querySelectorAll('a:not(.univer-app-news-step)')).map(a=>a.textContent),['1','2','3','4','5','20']);
  // Page 1: the step back is shown but disabled, the step forward is a real link with a spoken name.
  const back=pager.querySelector('.univer-app-news-previous'),next=pager.querySelector('.univer-app-news-next');
  assert.equal(back.tagName,'SPAN');assert.equal(back.getAttribute('aria-disabled'),'true');
  assert.equal(next.getAttribute('href'),'/news/student/2');assert.ok(next.getAttribute('aria-label'));
  assert.equal(pager.querySelector('a[href="/news/student/3"]').textContent,'3');assert.equal(pager.querySelector('.univer-app-news-gap').textContent,'…');
  assert.equal(pager.querySelector('input').max,'20');assert.ok(calls.length<=10);
  const cache=JSON.parse(p.w.sessionStorage.getItem('univer.desktop.news-page-count.v1'));assert.equal(cache.count,20);assert.deepEqual(Object.keys(cache).sort(),['count','time']);
  p.w.__univerNewsV1.release();assert.equal(p.d.querySelector('.univer-app-news-pager'),null);p.close();
});
test('unfiltered discovery handles a two-page feed and never overwrites a date search after cancellation',async()=>{
  const p=context(newsPage([newsRow('09','One')],1,true),'/news/student',news);
  p.w.fetch=async path=>{const n=+path.split('/').pop();return response(path,newsPage(n===2?[newsRow('08','Two')]:[],n,false));};
  p.w.__univerNewsV1.apply('ru');await wait(470);
  assert.deepEqual(Array.from(p.d.querySelector('.univer-app-news-pager-top').querySelectorAll('a:not(.univer-app-news-step)')).map(a=>a.textContent),['1','2']);p.close();
  const q=context(newsPage([newsRow('09','One')],1,true),'/news/student',news);let finish;
  q.w.fetch=()=>new Promise(resolve=>finish=resolve);q.w.__univerNewsV1.apply('ru');await wait(420);
  input(q,'from','2026-10-09');q.w.fetch=async path=>response(path,newsPage([newsRow('08','Two')],2,false));
  finish(response('/news/student/5',newsPage([newsRow('01','Old')],5,false)));await wait(270);
  assert.deepEqual(results(q),['One']);assert.equal(q.d.querySelector('.univer-app-news-pager-top').hidden,true);assert.equal(q.w.sessionStorage.getItem('univer.desktop.news-page-count.v1'),null);q.close();
});
test('unavailable or clamped counts leave direct page entry free instead of limiting it to the next arrow',async()=>{
  const p=context(newsPage([newsRow('09','One')],1,true),'/news/student',news);
  p.w.fetch=async path=>response(path,newsPage([newsRow('09','One')],1,true));p.w.__univerNewsV1.apply('ru');await wait(450);
  assert.equal(p.d.querySelector('.univer-app-news-jump input').hasAttribute('max'),false);
  assert.equal(p.w.sessionStorage.getItem('univer.desktop.news-page-count.v1'),null);p.close();
});
test('streaming rows before the bottom pager never freeze the news as a single-page feed',async()=>{
  const p=context(newsPage([newsRow('09','One')]),'/news/student',news);
  Object.defineProperty(p.d,'readyState',{value:'loading',configurable:true});
  p.w.__univerNewsV1.apply('ru');assert.equal(p.d.querySelector('.univer-app-news-tools'),null);
  p.d.querySelector('td.tb').insertAdjacentHTML('beforeend','<a href="/my/news/2">2</a>');
  Object.defineProperty(p.d,'readyState',{value:'complete',configurable:true});
  p.w.fetch=async path=>{const n=+path.split('/').pop();return response(path,newsPage(n<=20?[newsRow('08','Page '+n)]:[],n,n<20));};
  p.w.__univerNewsV1.apply('ru');await wait(470);
  assert.equal(p.d.querySelector('.univer-app-news-jump input').max,'20');
  assert.ok(p.d.querySelector('.univer-app-news-pager-top a[href="/news/student/3"]'));p.close();
});
const attendance='<table><tr id="l4"><td><table class="mt"><tr><td class="ts"><a class="rt" href="/student/attendance/full/2026/2">Следующий семестр</a></td></tr></table><table class="mt"><tr><td class="ct" id="course">История Казахстана ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr><tr><td class="ts"><a id="syllabus" class="rt syllabus-info" href="#">Интерактивный силлабус</a></td></tr><tr><td><table id="grades"><tr><th rowspan="2">РК</th><td>50</td></tr><tr><td>60</td></tr></table></td></tr></table></td></tr></table>';
const courseList='<table><tr id="l4"><td><table class="inner"><tr class="link" id="321"><td>1</td><td>История Казахстана</td><td>Обязательная</td></tr></table></td></tr></table>';

test('pointer syllabus panels move in and out while keyboard toggles and Original stay immediate',async()=>{
  const p=context(attendance,'/student/attendance/full/',study),motions=[];
  // A pointer close folds the list shut and hides it when the fold ends (collapse calls done), instead of a fading copy.
  let finish=null;
  p.w.__univerMotionUI={reveal(n){motions.push(['reveal',n]);},exit(n){motions.push(['exit',n]);},stop(n){motions.push(['stop',n]);},resize(n){motions.push(['resize',n]);},collapse(n,done){motions.push(['collapse',n]);finish=done;}};
  p.w.fetch=async path=>response(path,path.includes('/0/')?courseList:'<div id="l4"><a href="/student/interactive/syllabus/print/22/2">История</a></div>');
  p.w.__univerStudyV1.apply('ru');await wait(20);const button=p.d.querySelector('.univer-app-course-syllabus');
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));await wait(20);
  const panel=p.d.querySelector('.univer-app-syllabi');assert.ok(motions.some(m=>m[0]==='reveal'&&m[1]===panel));
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));assert.deepEqual(motions.at(-1),['collapse',panel]);
  assert.equal(panel.hidden,false,'on screen while it folds');assert.equal(button.getAttribute('aria-expanded'),'false','but already said to be closed');
  finish();assert.equal(panel.hidden,true);assert.ok(!motions.some(m=>m[0]==='exit'));
  const before=motions.filter(m=>m[0]==='reveal').length;button.click();assert.equal(panel.hidden,false);assert.equal(motions.filter(m=>m[0]==='reveal').length,before);
  p.w.__univerStudyV1.release();assert.equal(motions.at(-1)[0],'stop');assert.equal(panel.isConnected,false);p.close();
});
test('attendance course and syllabus use the real UMKD semester, with passive documents and intact grade nodes',async()=>{
  const p=context(attendance,'/student/attendance/full/',study), calls=[];
  p.w.fetch=async path=>{calls.push(path); return response(path,path.includes('/0/')?courseList:'<div id="l4"><h4>Силлабусы</h4><a href="/student/interactive/syllabus/print/22/2">История</a><a href="/student/umkd/get/6/321/2026/1">Силлабус.docx</a><a href="/student/umkd/get/7/321/2026/1">Лекция.pdf</a><a href="https://evil.test/student/interactive/syllabus/print/1/2">External</a></div>');};
  const matrix=p.d.getElementById('grades'), source=matrix.outerHTML, title=p.d.getElementById('course').firstChild;
  let legacyClicks=0; p.d.getElementById('syllabus').addEventListener('click',()=>legacyClicks++);
  p.w.__univerStudyV1.apply('ru'); await wait(20);
  assert.equal(p.d.querySelector('.univer-app-attendance-course').getAttribute('href'),'/student/umkd/321/2026/1');
  assert.equal(p.d.getElementById('syllabus').hidden,true);
  p.d.getElementById('syllabus').click();assert.equal(legacyClicks,0);assert.equal(p.d.querySelector('.univer-app-syllabi'),null);
  p.d.querySelector('.univer-app-course-syllabus').click(); await wait(20);
  assert.equal(legacyClicks,0);
  assert.deepEqual(calls,['/student/umkd/0/2026/1','/student/umkd/321/2026/1']);
  assert.equal(p.d.querySelectorAll('.univer-app-syllabi li').length,2);
  assert.equal(p.d.querySelector('.univer-app-syllabi li a').getAttribute('href'),origin+'/student/interactive/syllabus/print/22/2');
  p.d.querySelector('.univer-app-course-syllabus').click(); assert.equal(p.d.querySelector('.univer-app-syllabi').hidden,true);
  assert.equal(matrix.outerHTML,source);
  p.w.__univerStudyV1.release(); assert.equal(p.d.getElementById('course').firstChild,title);
  assert.equal(p.d.getElementById('syllabus').getAttribute('href'),'#'); assert.equal(p.d.querySelector('.univer-app-syllabi'),null);
  p.d.getElementById('syllabus').addEventListener('click',e=>e.preventDefault()); p.d.getElementById('syllabus').click(); assert.equal(legacyClicks,1); p.close();
});
test('unavailable syllabus has a retry and UMKD link, repeated polling does not retry automatically',async()=>{
  const p=context(attendance,'/student/attendance/full/2024/2',study); let calls=0;
  p.w.fetch=async()=>{calls++;throw Error('offline');}; p.w.__univerStudyV1.apply('ru'); await wait(15);
  p.w.__univerStudyV1.apply('ru'); p.w.__univerStudyV1.apply('ru'); await wait(15); assert.equal(calls,1);
  p.d.querySelector('.univer-app-course-syllabus').click(); await wait(15);
  assert.match(p.d.querySelector('.univer-app-syllabi').textContent,/Не удалось/);
  assert.equal(p.d.querySelector('.univer-app-syllabi a').getAttribute('href'),'/student/umkd/0/2024/2');
  const before=calls; p.d.querySelector('.univer-app-syllabi button').click(); await wait(15); assert.equal(calls,before+1); p.close();
});

test('a keyboard user keeps focus on the same news while the search fetches further pages',async()=>{
  const p=context(newsPage([newsRow('09','One','<a href="/news/one">Open</a>'),newsRow('08','Two')],1,true),'/news/student',news);
  let release=null; p.w.fetch=path=>new Promise(resolve=>{release=()=>resolve(response(path,newsPage([newsRow('07','Three'),newsRow('06','Four')],2,false)));});
  p.w.__univerNewsV1.apply('ru'); input(p,'from','2026-10-08'); await wait(270);
  const link=p.d.querySelector('[data-univer-results] tr.news a'); assert.ok(link && release); link.focus();
  release(); await wait(80);
  assert.equal(link.isConnected,false,'the results were rebuilt with the fetched page');
  assert.equal(p.d.activeElement.href,origin+'/news/one');
  assert.ok(p.d.querySelector('[data-univer-results]').contains(p.d.activeElement));
  p.w.__univerNewsV1.release(); p.close();
});

test('news and study-links enforce the 4 MB limit while streaming and cancel oversized responses', async () => {
  const chunk = new Uint8Array(1024 * 1024);
  const makeHuge = path => {
    let reads = 0, cancels = 0;
    return {
      stats: () => ({ reads, cancels }),
      response: {
        status: 200,
        url: origin + path,
        headers: { get: () => null },
        body: { getReader: () => ({ read: async () => { reads++; return { value: chunk, done: false }; }, cancel: async () => { cancels++; }, releaseLock() {} }) }
      }
    };
  };
  const p = context(newsPage([newsRow('09', 'One')], 1, true), '/news/student', news);
  p.w.TextDecoder = TextDecoder;
  const newsHuge = makeHuge('/news/student/2');
  p.w.fetch = async () => newsHuge.response;
  p.w.__univerNewsV1.apply('ru');
  input(p, 'query', 'One');
  await wait(270);
  assert.equal(newsHuge.stats().reads, 5, 'stops on the 5th 1 MB chunk (> 4 MB)');
  assert.equal(newsHuge.stats().cancels, 1, 'cancels the oversized stream');
  assert.match(p.d.querySelector('.univer-app-news-status').textContent, /не завершён/);
  p.w.__univerNewsV1.release(); p.close();

  const q = context(attendance, '/student/attendance/full/2026/1', study);
  q.w.TextDecoder = TextDecoder;
  const studyHuge = makeHuge('/student/umkd/0/2026/1');
  q.w.fetch = async () => studyHuge.response;
  q.w.__univerStudyV1.apply('ru');
  await wait(30);
  assert.equal(studyHuge.stats().reads, 5, 'study-links stops on the 5th 1 MB chunk (> 4 MB)');
  assert.equal(studyHuge.stats().cancels, 1, 'study-links cancels the oversized stream');
  q.w.__univerStudyV1.release(); q.close();
});

test('news page cache stays bounded across multi-batch searches while preserving results and sanitized links', async () => {
  const p = context(newsPage([newsRow('09', 'Первая запись')], 1, true), '/news/student', news);
  const fetched = [];
  p.w.fetch = async path => {
    const n = +path.split('/').pop();
    fetched.push(n);
    const extra = n === 65 ? '<a href="https://univer.kaznu.kz/news/doc">Ссылка</a><script>evil()</script>' : '';
    const title = n === 65 ? 'Особая новость' : 'Новость ' + n;
    return response(path, newsPage([newsRow('08', title, extra)], n, n < 70));
  };
  p.w.__univerNewsV1.apply('ru');
  input(p, 'query', 'Особая');
  await wait(270);
  // Batch 1 searched pages 2..31; click Continue search twice to cover pages 32..70 (> 60 cached pages).
  p.d.querySelector('.univer-app-news-all').click();
  await wait(60);
  p.d.querySelector('.univer-app-news-all').click();
  await wait(60);
  assert.deepEqual(results(p), ['Особая новостьСсылка']);
  assert.equal(p.d.querySelector('[data-univer-results] a').getAttribute('href'), 'https://univer.kaznu.kz/news/doc');
  assert.equal(p.d.querySelector('[data-univer-results] script'), null);
  // Now start a new search: page 2 was evicted by the cache bound so it must be re-fetched,
  // while recent page 65 stayed in the cache and is NOT re-fetched.
  fetched.length = 0;
  input(p, 'query', 'Новость 2');
  await wait(270);
  assert.ok(fetched.includes(2), 'evicted early page 2 is fetched again on a new search');
  assert.ok(!fetched.includes(65), 'recent cached page 65 is not refetched');
  p.w.__univerNewsV1.release(); p.close();
});

test('news page cache never evicts pages with active-filter matches when more than 60 pages all match', async t => {
  const total = 65;
  const p = context(newsPage([newsRow('09', 'Общая новость #1')], 1, true), '/news/student', news);
  t.after(() => { p.w.__univerNewsV1.release(); p.close(); });
  const fetched = [];
  p.w.fetch = async path => {
    const n = +path.split('/').pop();
    fetched.push(n);
    return response(path, newsPage([newsRow('08', 'Общая новость #' + n)], n, n < total));
  };
  p.w.__univerNewsV1.apply('ru');
  input(p, 'query', 'Общая новость');
  await wait(270);
  // Batch 1 covered pages 1..30; press Continue search twice to cover pages 31..65 (> 60 matching pages).
  p.d.querySelector('.univer-app-news-all').click();
  await wait(60);
  p.d.querySelector('.univer-app-news-all').click();
  await wait(60);
  assert.equal(p.d.querySelector('.univer-app-news-count').textContent, 'Показано 1 из ' + total);
  assert.equal(p.d.querySelector('.univer-app-news-status').hidden, true);
  assert.equal(p.d.querySelector('.univer-app-news-all').hidden, true);
  const listed = [];
  for (let page = 1; page <= total; page++) {
    const jump = p.d.querySelector('.univer-app-news-pager-top .univer-app-news-jump');
    jump.querySelector('input').value = String(page);
    jump.dispatchEvent(new p.w.Event('submit', { cancelable: true }));
    listed.push(...results(p));
  }
  assert.deepEqual(listed, Array.from({ length: total }, (_, i) => 'Общая новость #' + (i + 1)));

  // Narrowing the filter reuses the cached pages without refetching and prunes excess non-matching pages once complete.
  fetched.length = 0;
  input(p, 'query', 'Общая новость #65');
  await wait(270);
  p.d.querySelector('.univer-app-news-all').click();
  await wait(40);
  p.d.querySelector('.univer-app-news-all').click();
  await wait(40);
  assert.deepEqual(fetched, [], 'narrowing reuses all cached pages before pruning');
  assert.deepEqual(results(p), ['Общая новость #65']);

  // After pruning back to the 60-page cache cap, the oldest non-matching page (2) is fetched again when queried.
  input(p, 'query', 'Общая новость #2');
  await wait(270);
  assert.ok(fetched.includes(2), 'evicted non-matching page 2 is fetched again after cache shrinks back to 60');
  assert.deepEqual(results(p), ['Общая новость #2']);
});
test('a journal list shows lookup progress, then a localized own syllabus tag spoken with the link',async()=>{
  const p=context(attendance,'/student/attendance/full/',study);let answer;
  p.w.__univerSyllabusV1={own:list=>{p.asked=[...list];return new Promise(r=>answer=r);}};
  p.w.fetch=async path=>response(path,path.includes('/0/')?courseList:'<div id="l4"><a href="/student/interactive/syllabus/print/22/1">История (каз.)</a><a href="/student/interactive/syllabus/print/22/2">История</a></div>');
  p.w.__univerStudyV1.apply('ru');await wait(20);p.d.querySelector('.univer-app-course-syllabus').click();await wait(20);
  const panel=p.d.querySelector('.univer-app-syllabi'),names=()=>Array.from(panel.querySelectorAll('li>a'),a=>a.textContent);
  assert.deepEqual(names(),['История (каз.)','История'],'the list is shown at once, before the answer');
  assert.deepEqual(p.asked,['https://univer.kaznu.kz/student/interactive/syllabus/print/22/1','https://univer.kaznu.kz/student/interactive/syllabus/print/22/2']);
  const pending=panel.querySelector('.univer-app-own-status[role=status]');
  assert.equal(pending.textContent,'Ищу ваш силлабус…');
  p.w.__univerStudyV1.apply('kz');assert.equal(pending.textContent,'Силлабусыңыз ізделуде…');
  p.w.__univerStudyV1.apply('ru');
  answer('https://univer.kaznu.kz/student/interactive/syllabus/print/22/2');await wait(10);
  const li=panel.querySelector('li.univer-app-syllabus-own'),tag=li.querySelector('.univer-app-own-tag');
  assert.equal(tag.textContent,'Ваш силлабус');assert.equal(li.querySelector('a').getAttribute('aria-describedby'),tag.id);
  assert.equal(panel.querySelector('.univer-app-own-status'),null);
  assert.deepEqual(names(),['История','История (каз.)'],'first in the list');
  p.w.__univerStudyV1.apply('en');assert.equal(tag.textContent,'Your syllabus');
  p.w.__univerStudyV1.apply('kz');assert.equal(tag.textContent,'Сіздің силлабусыңыз');
  p.w.__univerStudyV1.release();assert.equal(p.d.querySelector('.univer-app-own-tag'),null);p.close();
});
test('a list in use is not reordered under the pointer; it is first when the list opens again',async()=>{
  const p=context(attendance,'/student/attendance/full/',study);let answer;
  p.w.__univerSyllabusV1={own:()=>new Promise(r=>answer=r)};
  p.w.fetch=async path=>response(path,path.includes('/0/')?courseList:'<div id="l4"><a href="/student/interactive/syllabus/print/22/1">А</a><a href="/student/interactive/syllabus/print/22/2">Б</a></div>');
  p.w.__univerStudyV1.apply('ru');await wait(20);const button=p.d.querySelector('.univer-app-course-syllabus');button.click();await wait(20);
  const panel=p.d.querySelector('.univer-app-syllabi'),names=()=>Array.from(panel.querySelectorAll('li>a'),a=>a.textContent);
  panel.querySelector('li>a').focus();answer('https://univer.kaznu.kz/student/interactive/syllabus/print/22/2');await wait(10);
  assert.ok(panel.querySelector('.univer-app-own-tag'),'tagged at once');assert.deepEqual(names(),['А','Б'],'but not moved while in use');
  button.click();button.click();assert.deepEqual(names(),['Б','А']);p.close();
});

test('a failed journal own-syllabus lookup clears its progress and retries on only one reopening',async()=>{
  const p=context(attendance,'/student/attendance/full/',study);let calls=0;
  p.w.__univerSyllabusV1={own:async()=>{calls++;throw new Error('unavailable');}};
  p.w.fetch=async path=>response(path,path.includes('/0/')?courseList:'<div id="l4"><a href="/student/interactive/syllabus/print/22/1">А</a></div>');
  p.w.__univerStudyV1.apply('ru');await wait(20);
  const button=p.d.querySelector('.univer-app-course-syllabus');
  button.click();await wait(20);
  assert.equal(calls,1);
  assert.equal(p.d.querySelector('.univer-app-own-status'),null);
  for(let i=0;i<4;i++){button.click();button.click();await wait(5);}
  assert.equal(calls,2);
  assert.equal(p.d.querySelector('.univer-app-own-status'),null);
  p.close();
});
const twoCards=(()=>{
  const course=(id,name)=>`<tr><td class="ct" id="${id}">${name} ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr><tr><td class="ts"><a class="rt syllabus-info" href="#">Интерактивный силлабус</a></td></tr>`+
    `<tr><td class="ts"><a class="lt" href="/student/attendance/show/1/2026/1">лекция - Преподаватель</a></td></tr><tr><td><div class="inner"><table class="inner"><tr><th rowspan="2">РК1 (100)</th><th>01.10</th><th>Сумма</th></tr><tr><td>5</td><td>5</td></tr></table></div></td></tr>`+
    `<tr><td class="ct"><b>РК1 (100): </b>50 <b>РК2 (100): </b></td></tr>`;
  return `<table><tr id="l4"><td><table class="mt">${course('course-a','История Казахстана')}${course('course-b','Философия')}${course('course-c','Политология')}</table></td></tr></table>`;
})();
test('an open syllabus list spans the full row, animates via reflow, compensates scroll, and restores on close and Original',async()=>{
  const p=context(twoCards,'/student/attendance/full/',common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/attendance.js','utf8')+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/study-links.js','utf8'));
  const reflows=[], scrolls=[]; let finishCollapse=null, y=400;
  Object.defineProperty(p.w,'scrollY',{configurable:true,get:()=>y});
  p.w.scrollTo=o=>{scrolls.push(o.top);y=o.top;};
  p.w.fetch=async path=>response(path,path.includes('/0/')?'<table><tr id="l4"><td><table class="inner"><tr class="link" id="321"><td>1</td><td>История Казахстана</td><td>Обязательная</td></tr></table></td></tr></table>':'<div id="l4"><a href="/student/interactive/syllabus/print/22/2">История</a></div>');
  p.w.__univerMotionUI={
    reduced:()=>false, stop(){}, resize(){}, reveal(){},
    collapse(node,done){finishCollapse=()=>{finishCollapse=null;done();};},
    reflow(parent,mutate,settings){reflows.push([parent,settings?.anchor]);mutate();}
  };
  p.w.__univerStudyV1.apply('ru');p.w.__univerAttendanceV1.apply('ru');await wait(20);
  const row=id=>p.d.getElementById(id).closest('tr'),wide=()=>['course-a','course-b','course-c'].filter(id=>row(id).hasAttribute('data-univer-syllabi-open'));
  const button=row('course-a').querySelector('.univer-app-course-syllabus');assert.ok(button,'the card has its syllabus button');
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));await wait(20);
  assert.deepEqual(wide(),['course-a'],'opening marks the card to span the full row');
  assert.equal(reflows.length,1,'pointer opening animates through reflow');
  assert.equal(reflows[0][1],button,'reflow receives the clicked button as scroll anchor');
  // Closing folds first, then slides the card back into its column.
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.deepEqual(wide(),['course-a'],'stays full width while folding shut');
  assert.equal(typeof finishCollapse,'function');
  finishCollapse();
  assert.deepEqual(wide(),[],'returns to its column once folded');
  assert.equal(reflows.length,2,'closing reflows back into the grid');
  // Keyboard activation skips reflow animation but still compensates scroll if the button moved.
  let btnTop=120; button.getBoundingClientRect=()=>({top:row('course-a').hasAttribute('data-univer-syllabi-open')?btnTop+180:btnTop,left:0,width:140,height:38});
  reflows.length=0; scrolls.length=0;
  button.click();
  assert.deepEqual(wide(),['course-a']);
  assert.equal(reflows.length,0,'keyboard opening does not animate via reflow');
  assert.deepEqual(scrolls,[580],'keyboard opening still keeps the button in place');
  p.w.__univerStudyV1.release();assert.deepEqual(wide(),[],'Original clears the open-row marker');p.close();
});
test('re-opening a closing syllabus list keeps the card full width and grows from its live height, and opening measures target height before reflow widens',async()=>{
  const p=context(twoCards,'/student/attendance/full/',common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/attendance.js','utf8')+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/study-links.js','utf8'));
  const reflows=[], resizes=[]; let finishCollapse=null, panelH=180;
  p.w.fetch=async path=>response(path,path.includes('/0/')?'<table><tr id="l4"><td><table class="inner"><tr class="link" id="321"><td>1</td><td>История Казахстана</td><td>Обязательная</td></tr></table></td></tr></table>':'<div id="l4"><a href="/student/interactive/syllabus/print/22/2">История</a></div>');
  p.w.__univerMotionUI={
    reduced:()=>false,
    stop(){ if(finishCollapse){ const fn=finishCollapse; finishCollapse=null; fn(); } },
    resize(node,before,opts){ resizes.push([before,opts?.target]); },
    reveal(){},
    collapse(node,done){ finishCollapse=()=>{ finishCollapse=null; done(); }; },
    reflow(parent,mutate){ reflows.push(true); mutate(); }
  };
  p.w.__univerStudyV1.apply('ru');p.w.__univerAttendanceV1.apply('ru');await wait(20);
  const row=p.d.getElementById('course-a').closest('tr');
  const button=row.querySelector('.univer-app-course-syllabus');
  const click=()=>button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  click(); await wait(20);
  const panel=row.querySelector('.univer-app-syllabi');
  panel.getBoundingClientRect=()=>({height:panelH,width:1100,top:0,left:0});
  // Close fully, then re-open: full-width target height is passed to resize.
  click(); finishCollapse();
  resizes.length=0; reflows.length=0;
  click();
  assert.deepEqual(resizes,[[1,180]],'re-opening passes measured full-width target height to resize');
  // Start closing, then click mid-close: stop() must NOT narrow the card or hide the panel; grows back from live height (72 px).
  reflows.length=0; resizes.length=0; panelH=72;
  click();
  assert.equal(typeof finishCollapse,'function');
  click();
  assert.equal(reflows.length,0,'mid-close reversal does not narrow and re-widen the card');
  assert.equal(panel.hidden,false,'panel stays visible');
  assert.equal(row.hasAttribute('data-univer-syllabi-open'),true,'card stays full width');
  assert.deepEqual(resizes,[[72,undefined]],'grows smoothly from live mid-close height');
  p.w.__univerStudyV1.release();p.close();
});
test('course cards use a dense grid with full-row open syllabi, no dead own-height rules, and padded own syllabus row',()=>{
  const css=fs.readFileSync('src/Univer.Desktop/Assets/desktop.js','utf8');
  const studySrc=fs.readFileSync('src/Univer.Desktop/Assets/study-links.js','utf8');
  assert.match(css,/grid-auto-flow:dense/);
  assert.match(css,/data-univer-syllabi-open/);
  assert.doesNotMatch(css,/data-univer-own-height/);
  assert.doesNotMatch(studySrc,/ownHeight|data-univer-own-height/);
  assert.match(css,/\.univer-app-syllabi li:is\(\.univer-app-syllabus-own,:has\(\.univer-app-own-tag\)\) \{[^}]*padding:10px 14px!important;[^}]*border:1px solid var\(--u-brand-line\)!important;[^}]*background:var\(--u-brand-soft\)!important;/);
  assert.match(css,/button\.univer-app-attendance-details-toggle:focus-visible \{ outline:none!important;/);
  assert.match(css,/button\.univer-app-course-syllabus:focus-visible \{ outline:none!important;/);
  assert.match(css,/table\.univer-app-matrix \{ border-collapse:separate!important; border-spacing:6px 8px!important;/);
});
test('first syllabus open and reopen animate width, slide neighbours and compensate scroll via real reflow',async()=>{
  const desktop=fs.readFileSync('src/Univer.Desktop/Assets/desktop.js','utf8');
  const p=context(twoCards,'/student/attendance/full/',common+'\n'+desktop+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/attendance.js','utf8')+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/study-links.js','utf8'));
  p.d.documentElement.classList.add('univer-mobile');
  p.w.matchMedia=()=>({matches:false,addEventListener(){},removeEventListener(){}});
  let y=400; const scrolls=[], anims=[];
  Object.defineProperty(p.w,'scrollY',{configurable:true,get:()=>y});
  p.w.scrollTo=o=>{scrolls.push(o.top);y=o.top;};
  p.w.HTMLElement.prototype.animate=function(frames,options){
    const rec={node:this,frames:JSON.parse(JSON.stringify(frames)),options,playState:'running',
      cancel(){this.playState='idle';this.oncancel?.();},
      finish(){this.playState='finished';this.onfinish?.();}};
    anims.push(rec); return rec;
  };
  p.w.fetch=async path=>response(path,path.includes('/0/')?'<table><tr id="l4"><td><table class="inner"><tr class="link" id="321"><td>1</td><td>История Казахстана</td><td>Обязательная</td></tr><tr class="link" id="322"><td>2</td><td>Философия</td><td>Обязательная</td></tr></table></td></tr></table>':'<div id="l4"><a href="/student/interactive/syllabus/print/22/2">Силлабус</a></div>');
  p.w.__univerStudyV1.apply('ru');p.w.__univerAttendanceV1.apply('ru');await wait(20);
  const rowA=p.d.getElementById('course-a').closest('tr'), rowB=p.d.getElementById('course-b').closest('tr'), rowC=p.d.getElementById('course-c').closest('tr');
  const btnA=rowA.querySelector('.univer-app-course-syllabus'), btnB=rowB.querySelector('.univer-app-course-syllabus');
  // Mirror desktop.js CSS: a visible .univer-app-syllabi or [data-univer-syllabi-open] immediately widens the row.
  const isWide=tr=>tr.hasAttribute('data-univer-syllabi-open')||!!tr.querySelector('.univer-app-syllabi:not([hidden])');
  rowA.getBoundingClientRect=()=>({left:0,top:200-(y-400),width:isWide(rowA)?1200:580,height:240});
  rowB.getBoundingClientRect=()=>isWide(rowB)?{left:0,top:460-(y-400),width:1200,height:240}:{left:620,top:200-(y-400),width:580,height:240};
  rowC.getBoundingClientRect=()=>isWide(rowB)?{left:620,top:200-(y-400),width:580,height:240}:{left:0,top:460-(y-400),width:580,height:240};
  btnB.getBoundingClientRect=()=>({left:isWide(rowB)?1020:900,top:(isWide(rowB)?660:400)-(y-400),width:140,height:38});
  btnA.getBoundingClientRect=()=>({left:420,top:(isWide(rowA)?520:400)-(y-400),width:140,height:38});
  // First pointer open: panel must be inserted hidden before reflow measures geometry and anchor position.
  btnB.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.deepEqual(scrolls,[660],'first open compensates scroll for the moved button');
  const firstB=anims.find(a=>a.node===rowB), firstC=anims.find(a=>a.node===rowC);
  assert.ok(firstB,'first open animates the widened card');
  assert.equal(firstB.frames[0].maxWidth,'580px'); assert.equal(firstB.frames[1].maxWidth,'1200px');
  assert.equal(firstB.frames[0].transform,'translate(620px,0px)');
  assert.ok(firstC,'first open slides the backfilling neighbour');
  await wait(20);
  // Close fully, then reopen: reopening also animates width, slides neighbour, and compensates scroll.
  anims.length=0; scrolls.length=0;
  btnB.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  const panelB=rowB.querySelector('.univer-app-syllabi');
  anims.filter(a=>a.node===panelB).forEach(a=>a.finish());
  assert.deepEqual(scrolls,[400],'closing restores scroll position');
  anims.length=0; scrolls.length=0;
  btnB.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.deepEqual(scrolls,[660],'reopening compensates scroll');
  const reopenB=anims.find(a=>a.node===rowB), reopenC=anims.find(a=>a.node===rowC);
  assert.ok(reopenB&&reopenB.frames[0].maxWidth==='580px'&&reopenB.frames[1].maxWidth==='1200px','reopening animates card width');
  assert.ok(reopenC,'reopening slides neighbour');
  // First keyboard open on a fresh card skips animation but still compensates scroll.
  anims.length=0; scrolls.length=0; y=400;
  btnA.click();
  assert.equal(anims.length,0,'first keyboard open skips animation');
  assert.deepEqual(scrolls,[520],'first keyboard open compensates scroll');
  p.w.__univerStudyV1.release();p.w.__univerAttendanceV1.release();p.close();
});
