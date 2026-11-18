const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {JSDOM}=require('jsdom');
const read=name=>fs.readFileSync('src/Univer.Desktop/Assets/'+name,'utf8');
const script=['page-common.js','attendance.js','grade-report.js'].map(read).join('\n');
const matrix=(id,total)=>`<div class="inner"><table class="inner" id="${id}"><tr><th rowspan="2">РК1 (100)</th><th>06.10</th><th>Сумма</th></tr><tr><td>2</td><td>${total}</td></tr></table></div>`;
const lesson=(id,name,total)=>`<tr><td class="ts"><a class="lt" href="/student/attendance/show/1/2026/1">${name} - Преподаватель</a></td></tr><tr><td>${matrix(id,total)}</td></tr>`;
const fixture=(a='<b>РК1 (100): </b>63 <b>РК2 (100): </b>12',b='<b>РК1 (100): </b>91 <b>РК2 (100): </b>')=>`<table><tr id="l4"><td><table class="mt"><tr><td class="ct" id="course-a">История Казахстана ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>${lesson('a','лекция','10')}<tr><td class="ct" id="total-a">${a}</td></tr><tr><td class="ct" id="course-b">Иностранный язык ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>${lesson('b','семинар','8')}<tr><td class="ct" id="total-b">${b}</td></tr></table></td></tr></table>`;
function page(html=fixture(),url='https://univer.kaznu.kz/student/attendance/full/',before) {
 const dom=new JSDOM(html,{url,runScripts:'outside-only'});
 if(before)before(dom.window);
 Object.defineProperty(dom.window.document,'readyState',{value:'complete',configurable:true});
 const sent=[];dom.window.__univerNative=m=>sent.push(m);
 dom.window.eval(script.replace(/typeof __univerNative === 'function'/g,"typeof window.__univerNative === 'function'").replace(/__univerNative\('/g,"window.__univerNative('"));
 dom.window.__univerAttendanceV1.apply('ru');
 return {w:dom.window,d:dom.window.document,sent,close:()=>dom.window.close()};
}
const sheet=p=>p.d.getElementById('univer-app-report').shadowRoot;
// The student's own click: jsdom marks every scripted event untrusted, so the test dispatches one marked trusted.
const jsdomUtils=require('jsdom/lib/jsdom/living/generated/utils');
function trustedClick(w,node){const e=new w.MouseEvent('click',{bubbles:true,cancelable:true});const impl=jsdomUtils.implForWrapper(e);impl.isTrusted=true;jsdomUtils.implForWrapper(node)._dispatch(impl);}
test('the grades page offers the summary once a course has a grade, and the sheet reads every course with its band and what is left',()=>{
 const empty=page(fixture('<b>РК1</b> ещё не выставлен','Нет оценок'));
 assert.equal(empty.d.querySelector('.univer-app-attendance-actions').hidden,true,'nothing to sum up yet');empty.close();
 // The app takes the summary only on the journal's own addresses (SitePolicy.Grades).
 const odd=page(fixture(),'https://univer.kaznu.kz/student/attendance/full/print');
 assert.equal(odd.d.querySelector('.univer-app-attendance-actions').hidden,true,'no summary where the app would not answer it');odd.close();
 const p=page(),button=p.d.querySelector('.univer-app-attendance-report');
 assert.equal(button.closest('[hidden]'),null);assert.equal(button.textContent,'Печать и PDF');assert.equal(button.getAttribute('aria-haspopup'),'dialog');
 // Icons are real SVG built node by node (the stores refuse HTML text), each before its label.
 const shapes=svg=>[svg.namespaceURI,...Array.from(svg.children,s=>s.namespaceURI==='http://www.w3.org/2000/svg'&&s.tagName)];
 assert.deepEqual(shapes(button.firstChild),['http://www.w3.org/2000/svg','path','rect','path']);
 button.focus();button.click();
 const root=sheet(p);assert.ok(root);
 const svgNs='http://www.w3.org/2000/svg';
 assert.deepEqual(Array.from(root.querySelectorAll('.actions button'),b=>shapes(b.firstChild)),
  [[svgNs,'path','rect','path'],[svgNs,'path','path','path'],[svgNs,'rect','circle','path'],[svgNs,'rect','path']],'print, pdf, png, copy');
 assert.deepEqual(shapes(root.querySelector('button.close').firstChild),['http://www.w3.org/2000/svg','path']);assert.ok(p.d.documentElement.hasAttribute('data-univer-report'));assert.ok(p.d.getElementById('univer-app-report-page'));
 assert.equal(root.querySelector('[role=dialog]').getAttribute('aria-modal'),'true');assert.equal(root.activeElement.textContent,'Печать');
 assert.deepEqual(Array.from(root.querySelectorAll('h3'),h=>h.textContent),['История Казахстана','Иностранный язык']);
 const rows=Array.from(root.querySelectorAll('.row')).map(r=>[r.querySelector('.label').textContent,r.dataset.band||'',r.querySelector('.value').textContent,r.querySelector('.note').textContent].join('|'));
 assert.deepEqual(rows,['РК1|fair|63из 100|Удовлетворительноещё 7 до 70','РК2|low|12из 100|Недоборещё 38 до 50','РК1|top|91из 100|Отлично','РК2||–из 100|']);
 assert.equal(root.querySelector('.row .fill').style.width,'63%');
 assert.deepEqual(Array.from(root.querySelectorAll('.scale span'),s=>s.textContent),['Недобор','Удовлетворительно','Хорошо','Отлично']);
 assert.match(root.querySelector('.meta').textContent,/^Данные на \d+ \S+ 20\d\d/);
 // Untrusted clicks (a page script) never reach the app.
 root.querySelectorAll('.actions button').forEach(b=>b.click());assert.deepEqual(p.sent,[]);
 // Escape closes and gives focus back to the button that opened it.
 root.querySelector('button').dispatchEvent(new p.w.KeyboardEvent('keydown',{key:'Escape',bubbles:true,composed:true}));
 assert.equal(p.d.getElementById('univer-app-report'),null);assert.equal(p.d.documentElement.hasAttribute('data-univer-report'),false);assert.equal(p.d.getElementById('univer-app-report-page'),null);
 assert.equal(p.d.activeElement,button);p.close();
});
test('the sheet follows the language and new numbers, answers the app, lies flat for its picture and goes with Original',()=>{
 const p=page();p.d.querySelector('.univer-app-attendance-report').click();
 const api=p.w.__univerReportV1;assert.equal(api.shown(),true);
 p.d.getElementById('total-a').innerHTML='<b>РК1 (100): </b>70 <b>РК2 (100): </b>12';p.w.__univerAttendanceV1.apply('en');
 let root=sheet(p);assert.equal(root.querySelector('h1').textContent,'Grades');assert.equal(root.querySelector('.actions button').textContent,'Print');
 assert.equal(root.querySelector('.row').dataset.band,'good');assert.equal(root.querySelector('.row .note').textContent,'Good20 points to 90');
 api.done({action:'pdf',ok:true,name:'Grades 2026-11-18.pdf'});
 const status=root.querySelector('.status');assert.equal(status.hidden,false);assert.match(status.textContent,/Saved to Downloads: Grades 2026-11-18\.pdf/);assert.equal(status.querySelector('button').textContent,'Show in folder');
 api.done({action:'png',ok:false,name:''});assert.ok(status.classList.contains('bad'));
 api.done({action:'copy',ok:true,name:''});assert.match(status.textContent,/Picture copied/);
 const fresh=p.d.getElementById('univer-app-report');assert.equal(fresh.classList.contains('still'),false,'the sheet fades in when it opens');
 const box=api.capture(true);assert.equal(fresh.getAttribute('data-mode'),'capture');assert.deepEqual(Object.keys(box),['pages','ratio']);
 assert.equal(box.pages.length,1);assert.deepEqual(Object.keys(box.pages[0]),['x','y','width','height']);
 api.capture(false);assert.equal(fresh.hasAttribute('data-mode'),false);
 assert.equal(fresh.classList.contains('still'),true,'back from the picture the sheet does not fade in again');
 p.w.__univerAttendanceV1.release();assert.equal(api.shown(),false);assert.equal(p.d.getElementById('univer-app-report'),null);assert.equal(p.d.documentElement.hasAttribute('data-univer-report'),false);
 assert.equal(api.capture(true),null);p.close();
});
// Nine courses: jsdom has no layout, so a page is 300 px plus 200 px a course; past 1500 px the next course starts a page.
const many=n=>`<table><tr id="l4"><td><table class="mt">${Array.from({length:n},(_,i)=>`<tr><td class="ct">Предмет ${i+1} ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>${lesson('m'+i,'семинар','8')}<tr><td class="ct"><b>РК1 (100): </b>${50+i} <b>РК2 (100): </b>${40+i}</td></tr>`).join('')}</table></td></tr></table>`;
function measured(p){Object.defineProperty(p.w.HTMLElement.prototype,'offsetHeight',{configurable:true,get(){return this.classList.contains('sheet')?300+200*this.querySelectorAll('.course').length:0;}});}
test('a long summary is cut between courses into pages, each with its number and its own Copy; a short one stays one picture',()=>{
 const p=page(many(9));measured(p);p.d.querySelector('.univer-app-attendance-report').click();
 const root=sheet(p),api=p.w.__univerReportV1,pages=()=>Array.from(root.querySelectorAll('.sheet'));
 assert.deepEqual(pages().map(s=>s.querySelectorAll('.course').length),[6,3],'six courses fill 1500 px, the seventh starts page 2');
 assert.deepEqual(pages().map(s=>s.classList.contains('last')),[false,true]);
 assert.ok(pages()[1].firstElementChild.classList.contains('cont-head'),'page 2 says what it is');assert.equal(pages()[1].querySelector('.cont-title').textContent,'Успеваемость');
 assert.deepEqual(pages().map(s=>s.querySelector('.pageno').textContent),[' · Страница 1 из 2',' · Страница 2 из 2']);
 assert.deepEqual(pages().map(s=>s.lastElementChild.className),['source','source']);
 const copies=Array.from(root.querySelectorAll('.page-bar button'));assert.deepEqual(copies.map(b=>b.getAttribute('aria-label')),['Копировать страницу 1','Копировать страницу 2']);
 assert.equal(Array.from(root.querySelectorAll('.actions button')).find(b=>/картинку/.test(b.textContent)).hidden,true,'one Copy for the whole sheet would be ambiguous');
 copies[1].click();assert.deepEqual(p.sent,[],'a page script cannot copy');
 const later=[],timeout=p.w.setTimeout;p.w.setTimeout=(fn,ms)=>{later.push(ms);return 0;};
 trustedClick(p.w,copies[1]);p.w.setTimeout=timeout;assert.deepEqual(p.sent,['univer-report:copy:2']);assert.deepEqual(later,[55000],'the second picture gets more time');
 assert.equal(copies[0].disabled,true);api.done({action:'copy',ok:true,name:'',page:2});assert.equal(copies[0].disabled,false);
 assert.match(root.querySelector('.status').textContent,/^Страница 2 скопирована/);
 [[2,'2 картинки'],[5,'5 картинок'],[11,'11 картинок'],[21,'21 картинка'],[12,'12 картинок']].forEach(([n,text])=>{api.done({action:'png',ok:true,name:'a.png',count:n});assert.equal(root.querySelector('.status span').textContent,'Сохранено в «Загрузки»: '+text);});
 const box=api.capture(true);assert.equal(box.pages.length,2);api.capture(false);
 // Another language: the pages are made again, with their own buttons only.
 p.w.__univerAttendanceV1.apply('en');
 assert.deepEqual(Array.from(sheet(p).querySelectorAll('.page-bar > span')).map(s=>s.textContent),['Page 1 of 2','Page 2 of 2']);
 assert.equal(sheet(p).querySelectorAll('.page-bar button').length,2);assert.equal(copies[0].isConnected,false);
 api.close();p.close();
 const short=page(many(3));measured(short);short.d.querySelector('.univer-app-attendance-report').click();
 const one=sheet(short);assert.equal(one.querySelectorAll('.sheet').length,1);assert.equal(one.querySelector('.page-bar,.pageno'),null);
 assert.equal(Array.from(one.querySelectorAll('.actions button')).find(b=>/картинку/.test(b.textContent)).hidden,false);short.close();
});
test('a double click on the button keeps the sheet open, Escape works wherever the focus went, and an answer gives focus back',()=>{
 const p=page(fixture('<b>РК1 (100): </b>63 <b>Экз (40): </b>35'));p.d.querySelector('.univer-app-attendance-report').click();
 let root=sheet(p);const scrim=root.querySelector('.scrim'),api=p.w.__univerReportV1;
 // The second press of a double click lands on the backdrop: it is not a request to close.
 scrim.dispatchEvent(new p.w.MouseEvent('mousedown',{bubbles:true,detail:2}));assert.equal(api.shown(),true);
 // A score on another scale shows its share without a band.
 const exam=Array.from(root.querySelectorAll('.row')).find(r=>r.querySelector('.label').textContent==='Экз');
 assert.equal(exam.dataset.band,undefined);assert.equal(exam.querySelector('.fill').style.width,'87.5%');
 // Focus lost (a disabled button, a click on the text): an answer brings it back to the sheet.
 root.activeElement.blur();assert.equal(root.activeElement,null);
 api.done({action:'copy',ok:false,name:''});assert.equal(root.activeElement.textContent,'Печать');assert.match(root.querySelector('.status').textContent,/Не удалось скопировать картинку/);
 root.activeElement.blur();p.d.body.dispatchEvent(new p.w.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));assert.equal(api.shown(),false);
 // The page cannot put another sheet in its place.
 assert.throws(()=>{'use strict';p.w.eval("'use strict';window.__univerReportV1={shown:()=>true}");});assert.equal(p.w.__univerReportV1,api);
 p.d.querySelector('.univer-app-attendance-report').click();assert.equal(api.shown(),true);
 // Each opening has its own number: an answer to an earlier opening is not shown on this one, a plain answer is.
 const now=api.opened();assert.ok(now>1);
 api.done({action:'pdf',ok:true,name:'old.pdf',opening:String(now-1)});assert.equal(sheet(p).querySelector('.status').hidden,true);
 api.done({action:'pdf',ok:true,name:'new.pdf',opening:String(now)});assert.match(sheet(p).querySelector('.status').textContent,/new\.pdf/);
 // A press the app could not take while it made an earlier file only frees the buttons: no failure is shown.
 api.done({action:'png',ok:false,name:'',busy:true});assert.match(sheet(p).querySelector('.status').textContent,/new\.pdf/);assert.equal(sheet(p).querySelector('.status').classList.contains('bad'),false);
 // …and «preparing» does not stay after such an answer, even when the language changed while the file was made.
 const st=sheet(p).querySelector('.status'),pdf=()=>Array.from(sheet(p).querySelectorAll('.actions button')).find(b=>/PDF/.test(b.textContent));
 trustedClick(p.w,pdf());assert.equal(p.sent.pop(),'univer-report:pdf');assert.equal(st.textContent,'Готовим файл…');
 api.done({action:'png',ok:false,name:'',busy:true});assert.equal(st.hidden,true);assert.equal(pdf().disabled,false);
 trustedClick(p.w,pdf());assert.equal(st.textContent,'Готовим файл…');p.w.__univerAttendanceV1.apply('en');
 api.done({action:'png',ok:false,name:'',busy:true});assert.equal(st.hidden,true,'the Russian «preparing» goes after the switch to English');
 // No answer within 40 s frees the sheet the same way, whatever language it speaks now.
 const later=[],timeout=p.w.setTimeout;p.w.setTimeout=(fn,ms)=>{if(ms===40000){later.push(fn);return 0;}return timeout(fn,ms);};
 trustedClick(p.w,pdf());assert.equal(st.textContent,'Preparing the file…');p.w.__univerAttendanceV1.apply('ru');
 assert.equal(later.length,1);later[0]();p.w.setTimeout=timeout;assert.equal(st.hidden,true,'the English «preparing» goes after the switch to Russian');assert.equal(pdf().disabled,false);
 sheet(p).querySelector('.scrim').dispatchEvent(new p.w.MouseEvent('mousedown',{bubbles:true,detail:1}));assert.equal(api.shown(),false);
 p.close();
});
test('in the browser extension the sheet offers print and PDF only: a picture of the tab is not possible there',()=>{
 const p=page(fixture(),undefined,w=>{w.__univerReportPictures=false;});p.d.querySelector('.univer-app-attendance-report').click();
 assert.deepEqual(Array.from(sheet(p).querySelectorAll('.actions button'),b=>b.textContent),['Печать','Сохранить PDF']);p.close();
 const app=page();app.d.querySelector('.univer-app-attendance-report').click();
 assert.deepEqual(Array.from(sheet(app).querySelectorAll('.actions button'),b=>b.textContent),['Печать','Сохранить PDF','Сохранить PNG','Копировать картинку'],'the Windows app keeps all four');app.close();
 const long=page(many(9),undefined,w=>{w.__univerReportPictures=false;});measured(long);long.d.querySelector('.univer-app-attendance-report').click();
 const root=sheet(long);assert.equal(root.querySelectorAll('.sheet').length,2,'a long sheet is still cut into pages for printing');
 assert.deepEqual(Array.from(root.querySelectorAll('.page-bar'),b=>b.textContent),['Страница 1 из 2','Страница 2 из 2']);assert.equal(root.querySelectorAll('.page-bar button').length,0,'no Copy beside a page');long.close();
});

// The marks at 50, 70 and 90 were white stripes in a gradient with clear stops; Chromium's PDF of it showed dark lines in
// MuPDF-based readers. They are plain white strips now, with no gradient on the bar.
test('each bar carries its 50, 70 and 90 marks as plain strips, with no gradient a PDF reader could draw dark',()=>{
 const p=page();p.d.querySelector('.univer-app-attendance-report').click();
 const tracks=Array.from(sheet(p).querySelectorAll('.row .track'));assert.ok(tracks.length>0);
 tracks.forEach(t=>{assert.deepEqual(Array.from(t.querySelectorAll('.mark'),m=>m.style.left),['50%','70%','90%']);
  const fill=t.querySelector('.fill');if(fill)assert.equal(fill.nextElementSibling.className,'mark','the marks come after the fill, so they are drawn over it');});
 assert.doesNotMatch(read('grade-report.js'),/linear-gradient/);p.close();
});
