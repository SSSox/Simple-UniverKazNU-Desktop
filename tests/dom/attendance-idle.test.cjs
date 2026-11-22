const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {JSDOM}=require('jsdom');
const common=fs.readFileSync('src/Univer.Desktop/Assets/page-common.js','utf8');
const script=common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/attendance.js','utf8');
function matrix(id) {return `<div class="inner"><table class="inner" id="${id}"><tr><th rowspan="2">РК1 (100)</th><th colspan="2">06.10</th><th></th><th>Сумма</th></tr><tr><td>2</td><td>3</td><td></td><td>5</td></tr></table></div>`;}
function lesson(id,name) {return `<tr><td class="lt"></td><td class="ts"><a class="lt" href="/student/attendance/show/123/2026/1">${name} - Преподаватель (Русский)</a></td><td class="rt"></td></tr><tr><td colspan="3">${matrix(id)}</td></tr>`;}
const html=`<table><tr id="l4"><td><table class="mt"><tr><td class="ct">История ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>${lesson('a','лекция')}${lesson('b','семинар')}${lesson('c','СРО/СРОП')}<tr><td class="ct"><b>РК1 (100): </b>51 <b>РК2 (100): </b>5</td></tr></table></td></tr></table>`;

test('the page tables are collected once per model pass and an unchanged page is not rewritten',()=>{
 const dom=new JSDOM(html,{url:'https://univer.kaznu.kz/student/attendance/full/2026/1',runScripts:'outside-only'}),w=dom.window,d=w.document;
 Object.defineProperty(d,'readyState',{value:'complete',configurable:true});
 w.eval(script);w.__univerAttendanceV1.apply('ru');
 const root=d.querySelector('#l4'),original=root.querySelectorAll;let walks=0;
 root.querySelectorAll=function(selector){if(selector==='table.inner')walks++;return original.call(this,selector);};
 const seen=[],observer=new w.MutationObserver(list=>seen.push(...list));observer.observe(root,{attributes:true,childList:true,characterData:true,subtree:true});
 w.__univerAttendanceV1.apply('ru');w.__univerAttendanceV1.apply('ru');
 assert.equal(walks,2,'one table walk per apply, whatever the number of lessons');
 seen.push(...observer.takeRecords());assert.equal(seen.length,0,'an unchanged page is not rewritten');
 d.getElementById('a').rows[1].cells[1].textContent='4';w.__univerAttendanceV1.apply('ru');
 assert.ok(d.querySelector('.univer-app-attendance-summary'),'a changed page still renders');
 observer.disconnect();w.__univerAttendanceV1.release();w.close();
});
