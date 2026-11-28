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
 // «нб» is never an unexcused «н»: it is counted apart, as an absence with a valid reason.
 const pairs=id=>Array.from(line(id).querySelectorAll('.univer-app-absence'),n=>[n.dataset.kind,n.firstChild.textContent,n.querySelector('b').textContent]);
 assert.deepEqual(pairs('course-a'),[['unexcused','Без уважительной причины','3'],['excused','По уважительной причине','0']]);
 assert.deepEqual(pairs('course-b'),[['unexcused','Без уважительной причины','0'],['excused','По уважительной причине','1']]);
 p.d.querySelector('.univer-app-attendance-report').click();
 const root=p.d.getElementById('univer-app-report').shadowRoot;
 const courses=Array.from(root.querySelectorAll('.course'));
 assert.equal(courses[0].querySelector('.missed').textContent,'Пропускибез уважительной причины3по уважительной причине0');
 assert.equal(courses[1].querySelector('.missed').textContent,'Пропускибез уважительной причины0по уважительной причине1');
 assert.equal(root.querySelector('.missed.term').textContent,'Пропуски за семестрбез уважительной причины3по уважительной причине1');
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

test('absences with a valid reason («нб», «н/б», «ну», any case) are counted apart: per course, for the term, never in the 25-hour warning',()=>{
 const marks=['нб','Н/Б','ну','НУ','н','нн','н б'];
 const many=Array.from({length:26},()=>'нб');
 const p=page(fixture().replace(`<tr><td class="ct" id="total-a">`,lesson('a3','семинар',marks,'5')+lesson('a4','семинар',many,'1')+`<tr><td class="ct" id="total-a">`));
 const pairs=root=>Array.from(root.querySelectorAll('.univer-app-absence'),n=>n.dataset.kind+' '+n.querySelector('b').textContent);
 assert.deepEqual(pairs(card(p,'course-a').querySelector('.univer-app-attendance-absences')),['unexcused 4','excused 30'],'only a lone «н» is unexcused; «нн» and «н б» are neither');
 assert.equal(card(p,'course-a').querySelector('.univer-app-attendance-absences b').textContent,'4','«Пропуски» still counts «н» only');
 const term=p.d.querySelector('.univer-app-attendance-term');
 assert.equal(term.hidden,false);assert.equal(term.closest('.univer-app-attendance-tools')!==null,true,'the term total stands above the courses');
 assert.equal(term.querySelector('.univer-app-attendance-term-title').textContent,'Пропуски за семестр');
 assert.deepEqual(pairs(term),['unexcused 4','excused 31'],'over every course of the term');
 assert.equal(p.d.querySelector('.univer-app-attendance-limit').hidden,true,'31 excused hours do not raise the 25-hour warning');
 p.w.__univerAttendanceV1.apply('en');
 assert.deepEqual(Array.from(term.querySelectorAll('.univer-app-absence>span:first-child'),n=>n.textContent),['Without a valid reason','With a valid reason']);
 p.w.__univerAttendanceV1.apply('kz');
 assert.deepEqual(Array.from(term.querySelectorAll('.univer-app-absence>span:first-child'),n=>n.textContent),['Дәлелсіз себеппен','Дәлелді себеппен']);
 p.w.__univerAttendanceV1.release();assert.equal(p.d.querySelector('.univer-app-attendance-term'),null);
 p.close();
});

test('a pointer opens a class log by growing its block rows (not the table) and closes it by folding them first; a key is immediate',async()=>{
 const p=page(),calls=[];let finish=[];
 p.w.__univerMotionUI={reveal(n){calls.push(['reveal',n]);},stop(n){calls.push(['stop',n]);},resize(n){calls.push(['resize',n]);},collapse(n,done){calls.push(['collapse',n]);finish.push(done);}};
 const toggle=()=>card(p,'course-a').querySelector('.univer-app-attendance-details-toggle');
 const log=()=>Array.from(p.d.querySelectorAll('[data-univer-journal]'),r=>r.getAttribute('data-univer-journal'));
 toggle().dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
 assert.deepEqual(log(),['top','head','body','head','body'],'the course row and its class rows are one card');
 assert.ok(p.d.querySelector('[data-univer-journal-end]').contains(p.d.getElementById('a2')),'the last class closes the card');
 await new Promise(r=>setTimeout(r,20));
 assert.ok(calls.filter(c=>c[0]==='resize').length>0,'the rows grow from the next frame');
 assert.ok(calls.filter(c=>c[0]==='resize').every(c=>c[1].tagName==='TR'&&c[1].hasAttribute('data-univer-journal')),'never a table');
 assert.equal(p.d.getElementById('a1').closest('tr').hidden,false);
 toggle().dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
 assert.equal(toggle().getAttribute('aria-expanded'),'false');assert.equal(finish.length,4,'every shown row folds');
 assert.equal(p.d.getElementById('a1').closest('tr').hidden,false,'still on screen while it folds');
 finish.forEach(done=>done());
 assert.equal(p.d.getElementById('a1').closest('tr').hidden,true);assert.deepEqual(log(),[]);
 calls.length=0;toggle().click();assert.deepEqual(log(),['top','head','body','head','body']);assert.equal(calls.filter(c=>c[0]==='resize').length,0,'a key opens at once');
 toggle().click();assert.equal(calls.filter(c=>c[0]==='collapse').length,0);assert.equal(p.d.getElementById('a1').closest('tr').hidden,true,'and closes at once');
 p.w.__univerAttendanceV1.release();assert.equal(p.d.querySelector('[data-univer-journal]'),null);p.close();
});

test('the course grid slides with a class log on both opening and closing, and the card\'s room before the next course grows with it',async()=>{
 const p=page(),calls=[],rooms=[];let finish=[];
 const end=()=>p.d.querySelector('[data-univer-journal-end]');
 p.w.HTMLElement.prototype.animate=function(frames){if(frames.some(f=>'marginBottom' in f))rooms.push([this,[...frames].map(f=>f.marginBottom)]);return {cancel(){}};};
 const real=p.w.getComputedStyle;p.w.getComputedStyle=n=>n.hasAttribute?.('data-univer-journal-end')?{marginBottom:'24px'}:real(n);
 p.w.__univerMotionUI={reduced:()=>false,reveal(){},stop(){},resize(){},collapse(n,done){finish.push(done);},
  reflow(parent,mutate){calls.push(['reflow',parent,!!p.d.querySelector('[data-univer-journal]')]);mutate();calls.push(['after',!!p.d.querySelector('[data-univer-journal]')]);
   shutWhileMeasured.push(...Array.from(p.d.querySelectorAll('[data-univer-journal=head],[data-univer-journal=body]'),r=>r.style.maxHeight));}};
 const shutWhileMeasured=[];
 const toggle=()=>card(p,'course-a').querySelector('.univer-app-attendance-details-toggle'),grid=toggle().closest('tbody');
 toggle().dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
 assert.deepEqual(calls,[['reflow',grid,false],['after',true]],'the grid slides as the course widens');
 assert.deepEqual(shutWhileMeasured,['1px','1px','1px','1px'],'the class rows are shut while the grid measures, so the courses below move once');
 await new Promise(r=>setTimeout(r,20));
 assert.ok(Array.from(p.d.querySelectorAll('[data-univer-journal]')).every(r=>!r.hasAttribute('style')),'then they grow, their own style as it was');
 assert.deepEqual(rooms,[[end(),['0px','24px']]],'the room before the next course grows from nothing');
 calls.length=0;toggle().dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
 assert.deepEqual(calls,[],'the grid waits for the fold');
 finish.forEach(done=>done());
 assert.deepEqual(calls,[['reflow',grid,true],['after',false]],'then slides the course back into its column');
 // Reduced motion and a key: no slide and no growing room.
 p.w.__univerMotionUI.reduced=()=>true;calls.length=0;rooms.length=0;finish=[];
 toggle().dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
 assert.deepEqual(calls,[]);assert.deepEqual(rooms,[]);
 toggle().click();toggle().click();assert.deepEqual(calls,[]);assert.deepEqual(rooms,[]);
 p.w.__univerAttendanceV1.release();p.close();
});

test('a class log grows and folds as one block: every row runs at the pace of the whole log, its room too',async()=>{
 const p=page(),grows=[],folds=[],rooms=[];let finish=[];
 p.w.HTMLElement.prototype.animate=function(frames,options){if(frames.some(f=>'marginBottom' in f))rooms.push(options);return {cancel(){}};};
 p.w.HTMLElement.prototype.getBoundingClientRect=function(){return {top:0,left:0,width:300,height:this.hasAttribute('data-univer-journal')?40:100};};
 const real=p.w.getComputedStyle;p.w.getComputedStyle=n=>n.hasAttribute?.('data-univer-journal-end')?{marginBottom:'24px'}:real(n);
 p.w.__univerMotionUI={reduced:()=>false,reveal(){},stop(){},span:d=>1000+d,easing:'shared',reflow(parent,mutate){mutate();},
  resize(n,before,options){grows.push(options);},collapse(n,done,options){folds.push(options);finish.push(done);}};
 const toggle=()=>card(p,'course-a').querySelector('.univer-app-attendance-details-toggle');
 toggle().dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
 await new Promise(r=>setTimeout(r,20));
 // The rows that grow are measured once, together: 40 px each in this stand-in layout.
 const rows=grows.length,distance=rows*40;
 assert.ok(rows>1);
 assert.equal(JSON.stringify(grows),JSON.stringify(Array(rows).fill({distance})),'each row grows at the pace of the whole log');
 assert.deepEqual(rooms.map(o=>[o.duration,o.easing]),[[1000+distance,'shared']],'the room before the next course keeps the same pace');
 toggle().dispatchEvent(new p.w.MouseEvent('click',{bubbles:true,detail:1}));
 assert.equal(JSON.stringify(folds),JSON.stringify(Array(rows).fill({distance})),'and folds at it');
 finish.forEach(done=>done());
 p.w.__univerAttendanceV1.release();p.close();
});
