const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {JSDOM}=require('jsdom');
const common=fs.readFileSync('src/Univer.Desktop/Assets/page-common.js','utf8');
const script=common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/attendance.js','utf8');
// A lesson table with `absent` cells reading «н» among its dates.
function matrix(id,absent) {
 const dates=Array.from({length:absent+1},(_,i)=>`<th>${String(i+1).padStart(2,'0')}.10</th>`).join('');
 const cells=Array.from({length:absent},()=>'<td>н</td>').join('')+'<td>5</td>';
 return `<div class="inner"><table class="inner" id="${id}"><tr><th rowspan="2">РК1 (100)</th>${dates}<th>Сумма</th></tr><tr>${cells}<td>5</td></tr></table></div>`;
}
function lesson(id,name,absent) {return `<tr><td class="lt"></td><td class="ts"><a class="lt" href="/student/attendance/show/${id.charCodeAt(0)}/2026/1">${name} - Преподаватель (Русский)</a></td><td class="rt"></td></tr><tr><td colspan="3">${matrix(id,absent)}</td></tr>`;}
function course(title,lessons) {return `<tr><td class="ct">${title} ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>${lessons}<tr><td class="ct"><b>РК1 (100): </b>51</td></tr>`;}
function page(first,second) {
 return `<table><tr id="l4"><td><table class="mt">${course('История',lesson('a','лекция',first))}${course('Физика',lesson('b','семинар',second))}</table></td></tr></table>`;
}
function open(html) {
 const dom=new JSDOM(html,{url:'https://univer.kaznu.kz/student/attendance/full/2026/1',runScripts:'outside-only'}),w=dom.window;
 Object.defineProperty(w.document,'readyState',{value:'complete',configurable:true});
 w.eval(script);return w;
}

test('more than 25 missed hours over all courses of the term puts a warning over the courses',()=>{
 const w=open(page(13,13)),d=w.document;
 w.__univerAttendanceV1.apply('ru');
 const limit=d.querySelector('.univer-app-attendance-limit');
 assert.ok(limit&&!limit.hidden,'26 hours over two courses warn');
 assert.equal(limit.textContent,'Пропущено 26 ч за семестр: это больше 25 ч');
 assert.equal(limit.getAttribute('data-univer-band'),'low');
 assert.equal(limit.nextElementSibling.className,'univer-app-attendance-courses','the warning stands over the courses');
 // Another class type shown: the term still counts every course and type.
 d.querySelector('button[data-kind="lecture"]').click();
 assert.ok(!limit.hidden&&limit.textContent.startsWith('Пропущено 26'),'a filter does not change the count');
 w.__univerAttendanceV1.apply('kz');
 assert.equal(limit.textContent,'Семестрде 26 сағат қатыспадыңыз: бұл 25 сағаттан көп');
 w.__univerAttendanceV1.apply('en');
 assert.equal(limit.textContent,'26 hours missed this term: more than 25');
 w.__univerAttendanceV1.release();
 assert.equal(d.querySelector('.univer-app-attendance-limit'),null,'Original removes the warning');
 w.close();
});

test('25 missed hours or fewer say nothing',()=>{
 const w=open(page(12,13)),d=w.document;
 w.__univerAttendanceV1.apply('ru');
 const limit=d.querySelector('.univer-app-attendance-limit');
 assert.ok(limit.hidden&&limit.textContent==='','exactly 25 is not more than the limit');
 w.__univerAttendanceV1.release();w.close();
});
