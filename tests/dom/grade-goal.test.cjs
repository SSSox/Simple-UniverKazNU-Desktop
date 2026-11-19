const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {JSDOM}=require('jsdom');
const read=name=>fs.readFileSync('src/Univer.Desktop/Assets/'+name,'utf8');
const script=['page-common.js','attendance.js','grade-report.js'].map(read).join('\n');
// A class's journal: dates with marks, «н» where the student was absent, and the server's sum.
const matrix=(id,marks,total)=>`<div class="inner"><table class="inner" id="${id}"><tr><th rowspan="2">РК1 (100)</th>${marks.map((_,i)=>`<th>0${i+1}.10</th>`).join('')}<th>Сумма</th></tr><tr>${marks.map(m=>`<td>${m}</td>`).join('')}<td>${total}</td></tr></table></div>`;
const lesson=(id,name,marks,total)=>`<tr><td class="ts"><a class="lt" href="/student/attendance/show/1/2026/1">${name} - Преподаватель</a></td></tr><tr><td>${matrix(id,marks,total)}</td></tr>`;
const fixture=(a='<b>РК1 (100): </b>63 <b>РК2 (100): </b>',b='<b>РК1 (100): </b>91 <b>РК2 (100): </b>')=>`<table><tr id="l4"><td><table class="mt">`+
 `<tr><td class="ct" id="course-a">История Казахстана ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>`+
 lesson('a1','лекция',['н','2','Н'],'10')+lesson('a2','семинар',['н','3'],'20')+`<tr><td class="ct" id="total-a">${a}</td></tr>`+
 `<tr><td class="ct" id="course-b">Философия ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>`+
 lesson('b1','лекция',['2','нб','5'],'30')+`<tr><td class="ct" id="total-b">${b}</td></tr></table></td></tr></table>`;
function page(html=fixture(),before) {
 const dom=new JSDOM(html,{url:'https://univer.kaznu.kz/student/attendance/full/',runScripts:'outside-only'});
 if(before)before(dom.window);
 Object.defineProperty(dom.window.document,'readyState',{value:'complete',configurable:true});
 dom.window.__univerNative=()=>{};
 dom.window.eval(script.replace(/typeof __univerNative === 'function'/g,"typeof window.__univerNative === 'function'").replace(/__univerNative\('/g,"window.__univerNative('"));
 dom.window.__univerAttendanceV1.apply('ru');
 return {w:dom.window,d:dom.window.document,close:()=>dom.window.close()};
}
// Each course's totals card, in page order (course-a, course-b).
const card=(p,course)=>p.d.querySelectorAll('.univer-app-attendance-summary')[course==='course-a'?0:1];
const total=(p,course)=>Array.from(card(p,course).querySelectorAll('.univer-app-score-total .univer-app-score'));
const goalButton=(root,name)=>root.querySelector(`.univer-app-goal button[data-goal=${name}]`);

test('without a goal a total says what is left to the next mark; a goal counts it to its own mark and says when it is reached',()=>{
 const p=page(),common=p.w.__univerCommonV1;
 assert.equal(common.goal(),null);
 assert.deepEqual(JSON.parse(JSON.stringify(common.band('63','100'))),{band:'fair',score:63,next:70,left:7,goal:null,reached:false});
 const note=()=>total(p,'course-a')[0].querySelector('.univer-app-score-next');
 assert.equal(note().textContent,'ещё 7 до 70');assert.equal(note().dataset.univerLeft,'true','what is left stands out');
 // The switch is on the page beside the summary's button: «Нет» pressed until a level is chosen.
 const picker=p.d.querySelector('.univer-app-attendance-actions .univer-app-goal');
 assert.deepEqual(Array.from(picker.querySelectorAll('button'),b=>[b.textContent,b.getAttribute('aria-pressed')]),
  [['Нет','true'],['Удовлетворительно','false'],['Хорошо','false'],['Отлично','false']]);
 goalButton(picker,'top').click();
 assert.equal(common.goal(),'top');assert.equal(p.w.localStorage.getItem('univer.desktop.grade-goal.v1'),'top');
 assert.equal(note().textContent,'ещё 27 до 90','the band stays, what is left goes to the goal');
 assert.match(total(p,'course-a')[0].getAttribute('aria-label'),/Удовлетворительно, ещё 27 до 90/);
 assert.equal(goalButton(p.d.querySelector('.univer-app-goal'),'top').getAttribute('aria-pressed'),'true');
 // 91 is past «Отлично»: no «ещё», and nothing highlighted.
 const b=total(p,'course-b')[0].querySelector('.univer-app-score-next');
 assert.equal(b.textContent,'Цель достигнута');assert.equal(b.dataset.univerLeft,undefined);
 // An empty РК is not a zero: nothing is counted for it.
 assert.equal(total(p,'course-a')[1].querySelector('.univer-app-score-next'),null);
 assert.equal(common.band('','100'),null);assert.equal(common.band('63','50'),null,'only a score out of 100 has a band');
 assert.equal(common.band('89.95','100').left,0.1,'rounded up to a tenth');
 goalButton(p.d.querySelector('.univer-app-goal'),'none').click();
 assert.equal(common.goal(),null);assert.equal(p.w.localStorage.getItem('univer.desktop.grade-goal.v1'),null);
 assert.equal(note().textContent,'ещё 7 до 70');
 p.close();
});

test('the goal is kept for the next visit, and the extension keeps it only while the page lives',()=>{
 const kept=page(fixture(),w=>w.localStorage.setItem('univer.desktop.grade-goal.v1','good'));
 assert.equal(kept.w.__univerCommonV1.goal(),'good');
 assert.equal(total(kept,'course-a')[0].querySelector('.univer-app-score-next').textContent,'ещё 7 до 70');
 kept.close();
 const junk=page(fixture(),w=>w.localStorage.setItem('univer.desktop.grade-goal.v1','<b>'));
 assert.equal(junk.w.__univerCommonV1.goal(),null,'an unknown value is not a goal');junk.close();
 const ext=page(fixture(),w=>{w.__univerGoalKeep=false;w.localStorage.setItem('univer.desktop.grade-goal.v1','top');});
 assert.equal(ext.w.__univerCommonV1.goal(),null,'nothing is read');
 goalButton(ext.d.querySelector('.univer-app-goal'),'good').click();
 assert.equal(ext.w.__univerCommonV1.goal(),'good');assert.equal(ext.w.localStorage.getItem('univer.desktop.grade-goal.v1'),'top','nothing is written');
 ext.close();
});

test('absences are the journal\'s «н» marks, counted per course and by class type, on the page and on the sheet',()=>{
 const p=page();
 const line=id=>card(p,id).querySelector('.univer-app-attendance-absences');
 assert.equal(line('course-a').querySelector('b').textContent,'3');
 assert.equal(line('course-a').querySelector('small').textContent,'лекции 2, семинары 1');
 // «нб» and grades are not «н»: a course without one shows no line at all.
 assert.equal(line('course-b'),null);
 p.d.querySelector('.univer-app-attendance-report').click();
 const root=p.d.getElementById('univer-app-report').shadowRoot;
 const courses=Array.from(root.querySelectorAll('.course'));
 assert.equal(courses[0].querySelector('.missed').textContent,'Пропуски3');assert.equal(courses[1].querySelector('.missed'),null);
 p.close();
});

test('the sheet says the chosen goal, highlights what is left and follows a new goal; the file waits for nothing to change',()=>{
 const p=page();p.d.querySelector('.univer-app-attendance-report').click();
 const root=()=>p.d.getElementById('univer-app-report').shadowRoot;
 assert.equal(root().querySelector('.aim'),null,'no goal, no line');
 assert.equal(root().querySelector('.note .left').textContent,'ещё 7 до 70');
 goalButton(root(),'top').click();
 assert.equal(p.w.__univerCommonV1.goal(),'top');
 assert.match(root().querySelector('.aim').textContent,/«Отлично» — от 90 баллов/);
 assert.deepEqual(Array.from(root().querySelectorAll('.note span'),n=>[n.className,n.textContent]),[['left','ещё 27 до 90'],['','Цель достигнута']]);
 // The page's own switch follows the sheet's.
 assert.equal(goalButton(p.d.querySelector('.univer-app-attendance-actions'),'top').getAttribute('aria-pressed'),'true');
 // The goal row is off the paper: neither the picture nor the printout carries the switch.
 assert.ok(/:host\(\[data-mode=capture\]\)[^{]*\.goal-row\{display:none\}/.test(root().querySelector('style').textContent));
 assert.ok(/\.bar,\.status,\.goal-row\{display:none!important\}/.test(root().querySelector('style').textContent));
 p.close();
});

test('the goal speaks the page\'s language',()=>{
 const p=page();p.w.__univerAttendanceV1.apply('kz');
 assert.deepEqual(Array.from(p.d.querySelectorAll('.univer-app-attendance-actions .univer-app-goal button'),b=>b.textContent),['Жоқ','Қанағаттанарлық','Жақсы','Өте жақсы']);
 goalButton(p.d,'top').click();
 assert.equal(total(p,'course-a')[0].querySelector('.univer-app-score-next').textContent,'90-ға дейін 27 балл');
 assert.equal(card(p,'course-a').querySelector('.univer-app-attendance-absences span').textContent,'Қатыспаған сабақтар');
 p.close();
});

const jsdomUtils=require('jsdom/lib/jsdom/living/generated/utils');
function trustedClick(w,node){const e=new w.MouseEvent('click',{bubbles:true,cancelable:true});jsdomUtils.implForWrapper(e).isTrusted=true;jsdomUtils.implForWrapper(node)._dispatch(jsdomUtils.implForWrapper(e));}

test('a goal chosen in the sheet keeps the focus on its button, and waits while the app makes a file',()=>{
 const p=page();p.d.querySelector('.univer-app-attendance-report').click();
 const root=p.d.getElementById('univer-app-report').shadowRoot;
 const top=goalButton(root,'top');top.focus();top.click();
 assert.equal(top.isConnected,true,'the pressed button is not made again');assert.equal(root.activeElement,top);
 assert.equal(top.getAttribute('aria-pressed'),'true');
 // A file is being made: the page's own switch changes the goal, the sheet stays as it is until the app answers.
 trustedClick(p.w,root.querySelector('.actions button'));
 const before=root.querySelector('.sheet');
 assert.equal(goalButton(root,'good').disabled,true,'the sheet\'s switch waits');
 goalButton(p.d.querySelector('.univer-app-attendance-actions'),'good').click();
 assert.equal(root.querySelector('.sheet'),before,'nothing is drawn mid-file');
 assert.match(root.querySelector('.aim').textContent,/Отлично/);
 p.w.__univerReportV1.done({action:'print',ok:true});
 assert.match(root.querySelector('.aim').textContent,/«Хорошо» — от 70 баллов/,'drawn once the file is done');
 assert.equal(goalButton(root,'good').getAttribute('aria-pressed'),'true');
 p.close();
});

test('a class of no known type (a lab) still counts its absences in the course total',()=>{
 const p=page(fixture().replace(`<tr><td class="ct" id="total-a">`,lesson('a3','лабораторная',['н','4'],'5')+`<tr><td class="ct" id="total-a">`));
 assert.equal(card(p,'course-a').querySelector('.univer-app-attendance-absences b').textContent,'4');
 assert.equal(card(p,'course-a').querySelector('.univer-app-attendance-absences small').textContent,'лекции 2, семинары 1, другие 1','the parts add up');
 p.close();
});
