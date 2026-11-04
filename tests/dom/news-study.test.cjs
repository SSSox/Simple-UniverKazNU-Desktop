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

test('pointer news expansion fades only after revealing all content; collapse, focus and release cancel it',()=>{
  const p=context(newsPage([newsRow('09','Длинная новость','<a href="#tail" id="tail">Конец</a>')]),'/news/student',news);
  newsGeometry(p,()=>600); const calls=[];
  p.w.__univerMotionUI={reveal(node,kind){calls.push(['reveal',node,kind]);},stop(node){calls.push(['stop',node]);},resize(node,before,duration){calls.push(['resize',node,before,duration]);}};
  p.w.__univerNewsV1.apply('ru'); const body=p.d.querySelector('.univer-app-news-body'),button=p.d.querySelector('.univer-app-news-toggle');
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.equal(body.getAttribute('data-collapsed'),'false'); assert.equal(calls.at(-1)[0],'reveal'); assert.equal(calls.at(-1)[2],'content');
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
  assert.equal(body.getAttribute('data-collapsed'),'true'); assert.equal(calls.at(-1)[0],'resize'); assert.equal(calls.at(-1)[3],180);
  const before=calls.filter(c=>c[0]==='reveal').length; button.click();
  assert.equal(body.getAttribute('data-collapsed'),'false'); assert.equal(calls.filter(c=>c[0]==='reveal').length,before);
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
  const links=Array.from(p.d.querySelector('.univer-app-news-pager').querySelectorAll('a'));
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
  assert.deepEqual(Array.from(pager.querySelectorAll('a')).map(a=>a.textContent),['1','2','3','4','5','20']);
  assert.equal(pager.querySelector('a[href="/news/student/3"]').textContent,'3');assert.equal(pager.querySelector('.univer-app-news-gap').textContent,'…');
  assert.equal(pager.querySelector('input').max,'20');assert.ok(calls.length<=10);
  const cache=JSON.parse(p.w.sessionStorage.getItem('univer.desktop.news-page-count.v1'));assert.equal(cache.count,20);assert.deepEqual(Object.keys(cache).sort(),['count','time']);
  p.w.__univerNewsV1.release();assert.equal(p.d.querySelector('.univer-app-news-pager'),null);p.close();
});
test('unfiltered discovery handles a two-page feed and never overwrites a date search after cancellation',async()=>{
  const p=context(newsPage([newsRow('09','One')],1,true),'/news/student',news);
  p.w.fetch=async path=>{const n=+path.split('/').pop();return response(path,newsPage(n===2?[newsRow('08','Two')]:[],n,false));};
  p.w.__univerNewsV1.apply('ru');await wait(470);
  assert.deepEqual(Array.from(p.d.querySelector('.univer-app-news-pager-top').querySelectorAll('a')).map(a=>a.textContent),['1','2']);p.close();
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
  p.w.__univerMotionUI={reveal(n){motions.push(['reveal',n]);},exit(n){motions.push(['exit',n]);},stop(n){motions.push(['stop',n]);},resize(n){motions.push(['resize',n]);}};
  p.w.fetch=async path=>response(path,path.includes('/0/')?courseList:'<div id="l4"><a href="/student/interactive/syllabus/print/22/2">История</a></div>');
  p.w.__univerStudyV1.apply('ru');await wait(20);const button=p.d.querySelector('.univer-app-course-syllabus');
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));await wait(20);
  const panel=p.d.querySelector('.univer-app-syllabi');assert.ok(motions.some(m=>m[0]==='reveal'&&m[1]===panel));
  button.dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));assert.equal(panel.hidden,true);assert.equal(motions.at(-1)[0],'exit');
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
