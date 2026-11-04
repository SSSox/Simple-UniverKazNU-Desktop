const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {JSDOM}=require('jsdom');
const common = fs.readFileSync('src/Univer.Desktop/Assets/page-common.js', 'utf8');
const script=common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/attendance.js','utf8');
function matrix(id,total,period='РК1 (100)') {return `<div class="inner"><table class="inner" id="${id}"><tr><th rowspan="2">${period}</th><th colspan="2">06.10</th><th></th><th>Сумма</th></tr><tr><td>2</td><td>3</td><td></td><td>${total}</td></tr></table></div>`;}
function lesson(id,name,total,period) {return `<tr id="section-${id}"><td class="lt"></td><td class="ts"><a id="label-${id}" class="lt" href="/student/attendance/show/123/2026/1">${name} - Преподаватель (Русский)</a><a class="rt syllabus-info" href="#">Интерактивный силлабус</a></td><td class="rt"></td></tr><tr><td colspan="3">${matrix(id,total,period)}</td></tr>`;}
const fixture=`<table><tr id="l4"><td><table class="mt"><tr><td class="ct">Журнал посещений и успеваемости</td></tr><tr><td class="ct" id="course-a">История ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>${lesson('lecture','лекция','10')}${lesson('seminar','семинар','26')}${lesson('sro','СРО/СРОП','15')}${lesson('sro2','СРС','5','РК2 (100)')}<tr><td class="ct" id="total-a"><b>РК1 (100): </b>51 <span></span><b>РК2 (100): </b>5</td></tr><tr><td class="ct" id="course-b">Язык ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>${lesson('seminar-b','семинар','8')}<tr><td class="ct" id="total-b"><b>РК1 (100): </b>8 <b>РК2 (100): </b>0</td></tr></table></td></tr></table>`;
function page(html=fixture,path='/student/attendance/full/2026/1') {
 const dom=new JSDOM(html,{url:'https://univer.kaznu.kz'+path,runScripts:'outside-only'});
 Object.defineProperty(dom.window.document,'readyState',{value:'complete',configurable:true});
 dom.window.eval(script);return {w:dom.window,d:dom.window.document,close:()=>dom.window.close()};
}
function visible(p,id) {return !p.d.getElementById(id).closest('[hidden]');}
function select(p,type) {p.d.querySelector(`[data-kind="${type}"]`).click();}
function subject(p,index) {p.d.querySelectorAll('.univer-app-attendance-courses button')[index].click();}
// With every course listed, class logs start collapsed; tests of the matrices open each log explicitly.
// Totals by class type as read aloud: type, then each period chip's label.
function types(node) {return Array.from(node.querySelectorAll('.univer-app-type-row')).map(r=>r.querySelector('.univer-app-type-label').textContent+' '+Array.from(r.querySelectorAll('.univer-app-score')).map(t=>t.getAttribute('aria-label')).join(' · ')).join('\n');}
function expandAll(p) {let toggle;while((toggle=Array.from(p.d.querySelectorAll('.univer-app-attendance-details-toggle[aria-expanded=false]')).find(t=>!t.closest('[hidden]'))))toggle.click();}
test('subject pages select a complete course and combine with the lesson filter without losing matrices or Original',()=>{
 const p=page(fixture.replace('</table></td></tr></table>','</table><p id="outside">Общая информация</p></td></tr></table>')),original=p.d.querySelector('#l4').innerHTML,matrix=p.d.getElementById('seminar-b');
 p.w.__univerAttendanceV1.apply('ru');
 const buttons=p.d.querySelectorAll('.univer-app-attendance-courses button');assert.deepEqual(Array.from(buttons).map(b=>b.textContent),['Все','История','Язык']);
 subject(p,2);assert.equal(visible(p,'course-a'),false);assert.equal(visible(p,'lecture'),false);assert.equal(visible(p,'course-b'),true);assert.equal(visible(p,'seminar-b'),true);
 assert.equal(p.d.querySelector('.univer-app-attendance-tools').closest('[hidden]'),null);
 const summaries=Array.from(p.d.querySelectorAll('.univer-app-attendance-summary')).filter(n=>!n.closest('[hidden]'));assert.equal(summaries.length,1);assert.match(summaries[0].textContent,/РК1 \(100\): 8/);
 select(p,'lecture');assert.equal(visible(p,'seminar-b'),false);assert.equal(visible(p,'course-b'),true);assert.match(p.d.querySelectorAll('.univer-app-attendance-summary')[1].textContent,/Нет занятий этого вида/);
 subject(p,0);assert.equal(visible(p,'course-a'),true);assert.equal(visible(p,'course-b'),true);assert.equal(visible(p,'lecture'),true);
 expandAll(p);assert.equal(visible(p,'lecture'),true);assert.equal(visible(p,'seminar-b'),false);
 assert.equal(matrix,p.d.getElementById('seminar-b'));assert.equal(matrix.rows[0].cells[0].rowSpan,2);assert.equal(visible(p,'outside'),true);
 p.w.__univerAttendanceV1.release();assert.equal(p.d.querySelector('#l4').innerHTML,original);p.close();
});
test('course selection survives reopening and keyboard navigation, stays within its semester, and resets if the course is removed',()=>{
 const p=page();p.w.__univerAttendanceV1.apply('ru');subject(p,2);
 const storageKey='univer.desktop.attendance-course.v1:/student/attendance/full/2026/1',saved=p.w.sessionStorage.getItem(storageKey);assert.ok(saved);
 p.w.__univerAttendanceV1.release();p.w.__univerAttendanceV1=null;p.w.eval(script);p.w.__univerAttendanceV1.apply('ru');assert.equal(visible(p,'course-a'),false);assert.equal(visible(p,'course-b'),true);
 const selected=p.d.querySelector('.univer-app-attendance-courses button[aria-pressed=true]');selected.focus();selected.dispatchEvent(new p.w.KeyboardEvent('keydown',{key:'ArrowLeft',bubbles:true}));
 assert.equal(visible(p,'course-a'),true);assert.equal(visible(p,'course-b'),false);assert.equal(p.d.activeElement.textContent,'История');
 p.w.__univerAttendanceV1.apply('en');assert.equal(p.d.activeElement.textContent,'История');assert.equal(p.d.querySelector('.univer-app-attendance-courses button').textContent,'All');
 subject(p,2);p.d.getElementById('seminar-b').rows[1].lastElementChild.textContent='9';p.w.__univerAttendanceV1.apply('ru');assert.equal(visible(p,'course-a'),false);assert.match(types(p.d.querySelectorAll('.univer-app-attendance-summary')[1]),/Семинары РК1 \(100\): 9/);
 let row=p.d.getElementById('course-b').parentElement;while(row){const next=row.nextElementSibling;row.remove();row=next;}
 p.w.__univerAttendanceV1.apply('ru');assert.equal(visible(p,'course-a'),true);assert.equal(p.d.querySelectorAll('.univer-app-attendance-courses button').length,2);assert.equal(p.w.sessionStorage.getItem(storageKey),null);p.close();
 const q=page(fixture,'/student/attendance/full/2025/2');q.w.sessionStorage.setItem(storageKey,saved);q.w.__univerAttendanceV1.apply('ru');assert.equal(visible(q,'course-a'),true);assert.equal(visible(q,'course-b'),true);q.close();
});
test('late and empty subjects become selectable, and a replaced attendance root rebuilds the selector',()=>{
 const p=page();p.w.__univerAttendanceV1.apply('ru');subject(p,1);
 const table=p.d.getElementById('course-b').closest('table');table.insertAdjacentHTML('beforeend','<tr><td class="ct" id="course-c">Новый предмет 01.09.2026 - 12.12.2026</td></tr>');
 p.w.__univerAttendanceV1.apply('ru');assert.equal(p.d.querySelectorAll('.univer-app-attendance-courses button').length,4);assert.equal(visible(p,'course-c'),false);
 subject(p,3);assert.equal(visible(p,'course-a'),false);assert.equal(visible(p,'course-c'),true);assert.match(p.d.querySelectorAll('.univer-app-attendance-summary')[2].textContent,/Занятия пока не добавлены/);
 const fresh=new JSDOM(fixture);p.d.querySelector('#l4').innerHTML=fresh.window.document.querySelector('#l4').innerHTML;fresh.window.close();
 p.w.__univerAttendanceV1.apply('ru');assert.equal(p.d.querySelectorAll('.univer-app-attendance-courses').length,1);assert.equal(p.d.querySelectorAll('.univer-app-attendance-courses button').length,3);assert.equal(visible(p,'course-a'),true);
 p.w.__univerAttendanceV1.release();assert.equal(p.d.querySelector('.univer-app-attendance-courses'),null);p.close();
});
test('lesson labels cannot navigate; filter keeps the selected matrices, original subject totals and server sums by type',()=>{
 const p=page(),original=p.d.querySelector('#l4').innerHTML, matrixNode=p.d.getElementById('seminar'),sumNode=p.d.querySelector('#total-a b');let clicks=0;
 p.d.getElementById('label-seminar').addEventListener('click',e=>{e.preventDefault();clicks++;});
 p.w.__univerAttendanceV1.apply('ru');expandAll(p);
 assert.equal(p.d.querySelectorAll('.univer-app-lesson-label').length,5);
 p.d.getElementById('label-seminar').click();assert.equal(clicks,0);
 assert.equal(p.d.getElementById('label-seminar').tabIndex,-1);
 const summaries=p.d.querySelectorAll('.univer-app-attendance-summary');assert.equal(summaries.length,2);
 assert.match(types(summaries[0]),/Лекции РК1 \(100\): 10/);
 assert.match(types(summaries[0]),/Семинары РК1 \(100\): 26/);
 assert.match(types(summaries[0]),/СРО РК1 \(100\): 15 · РК2 \(100\): 5/);
 select(p,'seminar');
 assert.equal(visible(p,'lecture'),false);assert.equal(visible(p,'sro'),false);assert.equal(visible(p,'seminar'),true);assert.equal(visible(p,'seminar-b'),true);
 assert.equal(visible(p,'total-a'),false);assert.equal(visible(p,'course-b'),true);
 // With a class type chosen, the course total steps aside and each course shows only that type's row.
 assert.equal(p.d.querySelector('.univer-app-attendance-overall-value'),null);
 assert.equal(p.d.querySelectorAll('.univer-app-attendance-summary .univer-app-type-row').length,2);
 select(p,'lecture');assert.equal(visible(p,'lecture'),true);assert.equal(visible(p,'seminar-b'),false);
 assert.match(p.d.querySelectorAll('.univer-app-attendance-summary')[1].textContent,/Нет занятий этого вида/);
 assert.equal(p.d.getElementById('seminar'),matrixNode);assert.equal(matrixNode.rows[0].cells[0].rowSpan,2);assert.equal(p.d.querySelector('#total-a b'),sumNode);
 p.w.__univerAttendanceV1.release();assert.equal(p.d.querySelector('#l4').innerHTML,original);
 p.d.getElementById('label-seminar').click();assert.equal(clicks,1);p.close();
});
test('saved filter and late grade updates survive repeated polling without duplicate controls or totals',()=>{
 const p=page();p.w.sessionStorage.setItem('univer.desktop.attendance-kind.v1','sro');p.w.__univerAttendanceV1=null;p.w.eval(script);
 p.w.__univerAttendanceV1.apply('ru');expandAll(p);assert.equal(visible(p,'sro'),true);assert.equal(visible(p,'lecture'),false);
 const total=p.d.getElementById('sro').rows[1].lastElementChild;total.textContent='17,5';
 p.d.getElementById('total-a').childNodes[1].textContent='53,5 ';
 p.w.__univerAttendanceV1.apply('ru');assert.match(p.d.querySelector('.univer-app-attendance-summary').textContent,/17.5/);
 assert.equal(p.d.querySelector('.univer-app-attendance-overall-value'),null);
 for(let i=0;i<5;i++)p.w.__univerAttendanceV1.apply('ru');
 assert.equal(p.d.querySelectorAll('.univer-app-attendance-filters').length,1);assert.equal(p.d.querySelectorAll('.univer-app-attendance-type-totals').length,2);
 p.w.__univerAttendanceV1.apply('en');assert.match(p.d.querySelector('.univer-app-attendance-summary').textContent,/Independent work/);
 assert.equal(visible(p,'lecture'),false);select(p,'all');assert.equal(p.w.sessionStorage.getItem('univer.desktop.attendance-kind.v1'),'all');expandAll(p);assert.equal(visible(p,'lecture'),true);
 assert.match(p.d.querySelector('.univer-app-attendance-overall-value').textContent,/53,5/);p.close();
});
test('ungraded lessons and pre-hidden content are preserved, and types do not borrow another subject totals',()=>{
 const html=fixture.replace(matrix('sro','15'),'<div class="inner" id="sro">Оценки не выставлены</div>').replace('<div class="inner"><table class="inner" id="seminar-b"','<div class="inner" hidden><table class="inner" id="seminar-b"');
 const p=page(html);p.w.__univerAttendanceV1.apply('ru');select(p,'sro');
 assert.match(types(p.d.querySelector('.univer-app-attendance-summary')),/РК2 \(100\): 5/);
 assert.doesNotMatch(types(p.d.querySelector('.univer-app-attendance-summary')),/РК1 \(100\): (15|26|8)/);
 select(p,'all');assert.equal(visible(p,'seminar-b'),false);p.w.__univerAttendanceV1.release();assert.equal(visible(p,'seminar-b'),false);p.close();
});
test('a single-group page can filter its lessons without adding source navigation',()=>{
 const p=page(`<div id="l4"><div class="ts"><a class="lt" href="/student/attendance/show/123/2026/1">лекция - Преподаватель</a></div>${matrix('one','0')}</div>`,'/student/attendance/show/123/2026/1');
 p.w.__univerAttendanceV1.apply('kz');assert.match(types(p.d.querySelector('.univer-app-attendance-summary')),/Дәрістер РК1 \(100\): 0/);
 select(p,'sro');assert.equal(visible(p,'one'),false);select(p,'all');assert.equal(visible(p,'one'),true);p.close();
});
test('decimal server totals retain their precision and unrecognized lesson sections have their own boundary',()=>{
 const p=page(fixture.replace('>15</td>','>15,125</td>').replace(lesson('sro2','СРС','5','РК2 (100)'),lesson('sro2','СРС','5,001')).replace(lesson('seminar','семинар','26'),lesson('seminar','семинар','26')+lesson('other','Лабораторная','7')));
 p.w.__univerAttendanceV1.apply('ru');expandAll(p);assert.match(types(p.d.querySelector('.univer-app-attendance-summary')),/СРО РК1 \(100\): 20.126/);
 select(p,'seminar');assert.equal(visible(p,'seminar'),true);assert.equal(visible(p,'other'),false);select(p,'all');assert.equal(visible(p,'other'),true);p.close();
});
test('one syllabus action per subject stays visible with every lesson filter and opens the right course list',async()=>{
 const p=page(), original=p.d.querySelector('#l4').innerHTML;
 p.w.eval(common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/study-links.js','utf8'));
 const calls=[];p.w.fetch=async path=>{calls.push(path);return {status:200,url:'https://univer.kaznu.kz'+path,headers:{get:()=>null},text:async()=>path.includes('/0/')?'<div id="l4"><table class="inner"><tr class="link" id="321"><td>1</td><td>История</td><td>Обязательная</td></tr><tr class="link" id="322"><td>2</td><td>Язык</td><td>Обязательная</td></tr></table></div>':`<div id="l4"><a href="/student/interactive/syllabus/print/${path.includes('/322/')?'32':'31'}/2">Силлабус</a></div>`};};
 p.w.__univerStudyV1.apply('ru');p.w.__univerAttendanceV1.apply('ru');await new Promise(resolve=>setTimeout(resolve,20));
 assert.equal(p.d.querySelectorAll('.univer-app-course-syllabus').length,2);
 assert.ok(Array.from(p.d.querySelectorAll('a.syllabus-info')).every(a=>a.hidden&&a.tabIndex===-1));
 assert.equal(p.d.querySelector('.univer-app-attendance-course').textContent,'История');
 const buttons=Array.from(p.d.querySelectorAll('.univer-app-course-syllabus'));buttons[1].click();await new Promise(resolve=>setTimeout(resolve,20));
 assert.equal(p.d.querySelector('.univer-app-syllabi a').getAttribute('href'),'https://univer.kaznu.kz/student/interactive/syllabus/print/32/2');
 select(p,'lecture');assert.equal(buttons[1].closest('[hidden]'),null);assert.equal(p.d.querySelector('.univer-app-syllabi').closest('[hidden]'),null);
 subject(p,2);assert.equal(buttons[0].closest('[hidden]')!==null,true);assert.equal(buttons[1].closest('[hidden]'),null);assert.equal(p.d.querySelector('.univer-app-syllabi').closest('[hidden]'),null);
 subject(p,1);assert.equal(buttons[1].closest('[hidden]')!==null,true);assert.equal(p.d.querySelector('.univer-app-syllabi').closest('[hidden]')!==null,true);
 subject(p,0);assert.equal(buttons[1].closest('[hidden]'),null);assert.equal(p.d.querySelector('.univer-app-syllabi').closest('[hidden]'),null);
 for(let i=0;i<4;i++){p.w.__univerStudyV1.apply('ru');p.w.__univerAttendanceV1.apply('ru');}
 assert.equal(p.d.querySelectorAll('.univer-app-course-syllabus').length,2);assert.deepEqual(calls,['/student/umkd/0/2026/1','/student/umkd/322/2026/1']);
 p.w.__univerStudyV1.release();p.w.__univerAttendanceV1.release();assert.equal(p.d.querySelector('#l4').innerHTML,original);p.close();
});
test('all courses open as a summary with score tiles, the class log opens on request and Original restores the page',()=>{
 const p=page(),original=p.d.querySelector('#l4').innerHTML;p.w.__univerAttendanceV1.apply('ru');
 // Headings and totals stay visible; the per-date matrices wait for a request.
 assert.equal(visible(p,'course-a'),true);assert.equal(visible(p,'course-b'),true);assert.equal(visible(p,'lecture'),false);assert.equal(visible(p,'seminar-b'),false);
 const overview=p.d.querySelector('.univer-app-attendance-overview');assert.equal(overview.hidden,false);
 const rows=Array.from(overview.querySelectorAll('li'));assert.deepEqual(rows.map(r=>r.querySelector('button').textContent),['История','Язык']);
 assert.deepEqual(Array.from(rows[0].querySelectorAll('.univer-app-score')).map(t=>t.getAttribute('aria-label')),['РК1 (100): 51','РК2 (100): 5']);
 const tiles=p.d.querySelectorAll('.univer-app-attendance-summary')[0].querySelectorAll('.univer-app-attendance-overall-block .univer-app-score');
 assert.deepEqual(Array.from(tiles).map(t=>t.querySelector('.univer-app-score-value').textContent+' '+t.querySelector('.univer-app-score-max').textContent),['51 / 100','5 / 100']);
 assert.equal(tiles[0].querySelector('.univer-app-score-bar').style.getPropertyValue('--univer-score'),'51%');
 assert.equal(p.d.querySelector('.univer-app-attendance-overall-value').hidden,true);assert.equal(p.d.querySelector('.univer-app-attendance-filter-note').hidden,true);
 const toggle=p.d.querySelectorAll('.univer-app-attendance-details-toggle')[1];assert.equal(toggle.textContent,'Показать журнал занятий');assert.equal(toggle.getAttribute('aria-expanded'),'false');
 toggle.click();assert.equal(visible(p,'seminar-b'),true);assert.equal(visible(p,'lecture'),false);
 const reopened=p.d.querySelectorAll('.univer-app-attendance-details-toggle')[1];
 assert.equal(reopened.getAttribute('aria-expanded'),'true');assert.equal(reopened.textContent,'Скрыть журнал занятий');assert.equal(p.d.activeElement,reopened);
 // Repeated polling keeps the choice; a type filter explains itself with the note.
 for(let i=0;i<3;i++)p.w.__univerAttendanceV1.apply('ru');assert.equal(visible(p,'seminar-b'),true);assert.equal(visible(p,'lecture'),false);
 select(p,'seminar');assert.equal(p.d.querySelector('.univer-app-attendance-filter-note').hidden,false);
 // A course chosen in the summary opens with its class log; the summary itself steps aside.
 rows[0].querySelector('button').click();assert.equal(overview.hidden,true);assert.equal(visible(p,'seminar'),true);assert.equal(visible(p,'course-b'),false);
 select(p,'all');p.w.__univerAttendanceV1.apply('kz');assert.equal(p.d.querySelectorAll('.univer-app-attendance-details-toggle')[0].textContent,'Сабақ журналын жасыру');
 p.w.__univerAttendanceV1.release();assert.equal(p.d.querySelector('#l4').innerHTML,original);p.close();
});
test('a course without classes or totals is one quiet line, and unrecognized totals keep the server wording',()=>{
 const html=fixture.replace('<b>РК1 (100): </b>8 <b>РК2 (100): </b>0','<b>РК1</b> ещё не выставлен').replace('</table></td></tr></table>','<tr><td class="ct" id="course-c">Новый предмет 01.09.2026 - 12.12.2026</td></tr></table></td></tr></table>');
 const p=page(html);p.w.__univerAttendanceV1.apply('ru');
 const summaries=p.d.querySelectorAll('.univer-app-attendance-summary');assert.equal(summaries.length,3);
 assert.ok(summaries[2].classList.contains('univer-app-attendance-summary-empty'));assert.equal(summaries[2].textContent,'Занятия пока не добавлены');
 assert.equal(summaries[2].querySelector('.univer-app-attendance-details-toggle'),null);
 assert.equal(summaries[1].querySelector('.univer-app-attendance-overall-block .univer-app-score'),null);assert.equal(summaries[1].querySelector('.univer-app-attendance-overall-value').hidden,false);
 const rows=p.d.querySelectorAll('.univer-app-attendance-overview li');
 assert.equal(rows[1].querySelector('.univer-app-attendance-overview-empty').textContent,'РК1 ещё не выставлен');
 assert.equal(rows[2].querySelector('.univer-app-attendance-overview-empty').textContent,'Оценки пока не выставлены');p.close();
});
