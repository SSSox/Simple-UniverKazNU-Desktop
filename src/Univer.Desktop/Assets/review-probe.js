/* Read-only interface diagnostics. Never collect account fields, hidden values,
   cookies, document contents, script bodies or network credentials. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz') return '';
    function rect(e) { var r=e.getBoundingClientRect();return {x:Math.round(r.x),y:Math.round(r.y),w:Math.round(r.width),h:Math.round(r.height)}; }
    function node(e, depth) {
        var s=getComputedStyle(e);
        return {tag:e.tagName,cls:e.className||'',r:rect(e),display:s.display,float:s.cssFloat,position:s.position,overflow:s.overflow,height:s.height,
            children:depth?Array.from(e.children).filter(function(c){return !/SCRIPT|STYLE|INPUT|TEXTAREA/.test(c.tagName);}).slice(0,15).map(function(c){return node(c,depth-1);}):[]};
    }
    function visible(e) { return !!e.getClientRects().length && !e.closest('details:not([open]) :not(summary)') && getComputedStyle(e).visibility!=='hidden'; }
    var clipped=[];
    document.querySelectorAll('#l4 a,#l4 button,#l4 input:not([type=hidden]),#l4 select,#l4 textarea').forEach(function(e){
        if(!visible(e)||e.closest('#login_frm,#modal_auth_frm,#modal_agreement'))return;
        var r=e.getBoundingClientRect(), box=e.parentElement, scrollable=false;
        while(box&&box!==document.body){var s=getComputedStyle(box);if(/auto|scroll/.test(s.overflowX)){scrollable=true;break;}box=box.parentElement;}
        if(!scrollable&&(r.left< -2||r.right>innerWidth+2))clipped.push({tag:e.tagName,cls:e.className||'',r:rect(e)});
    });
    var week=document.getElementById('weekSelect'), grid=document.querySelector('table.schedule'), view=document.querySelector('.univer-app-schedule');
    return JSON.stringify({path:location.pathname.replace(/\d+/g,'{id}'),width:innerWidth,scroll:document.documentElement.scrollWidth,clipped:clipped,
        chrome:Array.from(document.querySelectorAll('#l1,.univer-app-account')).map(function(e){return node(e,4);}),
        metadata:Array.from(document.querySelectorAll('#l4 .ct.message')).map(function(e){return {tags:Array.from(e.children).map(function(c){return c.tagName;}),gpa:Array.from(e.children).filter(function(c){return c.tagName==='LI';}).map(function(c){return /\bGPA\b/i.test(c.textContent);})};}),
        sort:Array.from(document.querySelectorAll('table.table_sort')).map(function(t){return {parent:node(t.parentElement,0),groups:Array.from(t.children).map(function(g){return node(g,0);}),rows:Array.from(t.rows).slice(0,2).map(function(r){return node(r,1);})};}),
        schedule:grid?{source:node(grid,0),agenda:view?node(view,0):null,lessons:view?view.querySelectorAll('.univer-app-lesson').length:0,
            week:week?node(week.closest('.ts')||week.parentElement,3):null,parents:view?[node(view.parentElement,1),node(view.parentElement.parentElement,0)]:[]}:null,
        headings:Array.from(document.querySelectorAll('#l4 .ct')).filter(visible).slice(0,15).map(function(e){return node(e,1);}),
        scrollRegions:Array.from(document.querySelectorAll('#l4 .scrool,#l4 .scrool200,#l4 .univer-app-table-scroll')).filter(visible).slice(0,30).map(function(e){return {r:rect(e),cls:e.className,client:e.clientWidth,scroll:e.scrollWidth,height:e.clientHeight,contentHeight:e.scrollHeight};})});
})();
