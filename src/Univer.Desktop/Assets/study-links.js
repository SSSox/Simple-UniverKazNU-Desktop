/* Read-only links from attendance to the observed UMKD course list and its syllabi. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerStudyV1) return;
    var common = window.__univerCommonV1, clean = common.clean, fold = common.fold;
    var enabled = false, language = 'ru', generation = 0, courses = null, pending = null, attempted = false;
    var changed = new Map(), headings = new Map(), panels = new Map(), controller = null;
    function text() {
        return language === 'en' ? {loading:'Loading syllabi…', empty:'No syllabi have been uploaded for this course.', failed:'Could not load the syllabi. Try again or open the course materials.', retry:'Retry', umkd:'Open course materials', print:'Interactive syllabus', printType:'Print version', file:'File'} :
            language === 'kz' ? {loading:'Силлабустар жүктелуде…', empty:'Бұл пәнге силлабустар жүктелмеген.', failed:'Силлабустарды жүктеу мүмкін болмады. Қайталаңыз немесе оқу материалдарын ашыңыз.', retry:'Қайталау', umkd:'Оқу материалдарын ашу', print:'Интерактивті силлабус', printType:'Баспа нұсқасы', file:'Файл'} :
            {loading:'Загрузка силлабусов…', empty:'Для этого предмета силлабусы не загружены.', failed:'Не удалось загрузить силлабусы. Повторите или откройте УМКД.', retry:'Повторить', umkd:'Открыть УМКД предмета', print:'Интерактивный силлабус', printType:'Печатная версия', file:'Файл'};
    }
    function semester() {
        var path = location.pathname.match(/^\/student\/attendance\/full\/(\d{4})\/([12])\/?$/) ||
            location.pathname.match(/^\/student\/attendance\/show\/\d+\/(\d{4})\/([12])\/?$/);
        if (path) return '/' + path[1] + '/' + path[2];
        var next = document.querySelector('#l4 .ts a.rt[href*="/attendance/full/"]');
        var match = next?.getAttribute('href').match(/\/attendance\/full\/(\d{4})\/([12])\/?$/);
        return match ? '/' + (+match[1] - (match[2] === '1' ? 1 : 0)) + '/' + (match[2] === '1' ? '2' : '1') : '';
    }
    function listHref() { var term = semester(); return '/student/umkd/' + (term ? '0' + term : ''); }
    async function read(url) {
        var abort = new AbortController(), timeout = setTimeout(() => abort.abort(),10000); controller = abort;
        try {
            var response = await fetch(url,{credentials:'same-origin',cache:'no-store',redirect:'error',signal:abort.signal});
            var final = new URL(response.url,location.origin), expected = new URL(url,location.origin);
            if (response.status !== 200 || final.origin !== location.origin || final.pathname.replace(/\/$/,'') !== expected.pathname.replace(/\/$/,'') || +response.headers?.get('content-length') > 4*1024*1024) throw new Error('Unexpected UMKD response');
            var html = await common.boundedText(response, 4 * 1024 * 1024);
            var doc = new DOMParser().parseFromString(html,'text/html');
            if (doc.querySelector('#login_frm')) throw new Error('Session expired');
            return doc;
        } finally { clearTimeout(timeout); abort.abort(); }
    }
    async function loadCourses() {
        if (courses) return courses;
        if (pending) return pending;
        var epoch = generation; attempted = true;
        pending = (async function () {
            var doc = await read(listHref());
            // The observed dblclick handler is /student/umkd/ + this.id + /year/term.
            // Parse only that route; never execute a fetched site's scripts.
            var term = semester() || Array.from(doc.scripts).map(s=>s.textContent).join('\n').match(/["']\/student\/umkd\/["']\s*\+\s*this\.id\s*\+\s*["'](\/\d{4}\/[12])["']/)?.[1];
            if (!term) throw new Error('Course semester unavailable');
            var found = Array.from(doc.querySelectorAll('#l4 table.inner tr.link[id]')).filter(r=>/^\d+$/.test(r.id) && r.cells.length===3)
                .map(r=>({name:clean(r.cells[1]),href:'/student/umkd/'+r.id+term,id:r.id}));
            if (!found.length) throw new Error('Course list unavailable');
            if (enabled && generation===epoch) { courses=found; connectCourses(); }
            return found;
        })();
        try { return await pending; } finally { if (generation===epoch) pending=null; }
    }
    function remember(link) {
        if (!changed.has(link)) changed.set(link,['href','role','aria-controls','aria-expanded','hidden','tabindex','aria-hidden','data-univer-syllabus-source'].map(name=>[name,link.getAttribute(name)]));
    }
    function connectCourses() {
        if (!enabled) return;
        document.querySelectorAll('#l4 .ct').forEach(cell=>{
            if (cell.closest('#tools,.univer-app-attendance-summary')) return;
            var record=headings.get(cell);
            if (!record) {
                var source=clean(cell), parts=source.match(/^(.*?)\s*(\(\[.*?)?\s*(\d{2}\.\d{2}\.\d{4}\s*[-–]\s*\d{2}\.\d{2}\.\d{4})$/);
                if (!parts || cell.querySelector('form,input,select,button')) return;
                var original=document.createElement('span'); original.className='univer-app-course-source'; original.hidden=true; original.append(...cell.childNodes);
                var header=document.createElement('div'); header.className='univer-app-course-heading';
                var title=document.createElement('div'); title.className='univer-app-course-title';
                var name=document.createElement('span'); name.className='univer-app-course-name'; name.textContent=parts[1]; title.appendChild(name);
                var meta=document.createElement('div'); meta.className='univer-app-course-meta';
                [parts[3],parts[2]].filter(Boolean).forEach(value=>{var line=document.createElement('span');line.textContent=value;meta.appendChild(line);});title.appendChild(meta);
                var button=document.createElement('button');button.type='button';button.className='univer-app-course-syllabus';button.setAttribute('aria-expanded','false');
                header.append(title,button);cell.append(original,header);
                record={original,header,title,name,button,course:null,link:null};headings.set(cell,record);
                button.addEventListener('click',event=>{event.stopPropagation();show(button,false,event);});
            }
            if (record.button.textContent!==text().print) record.button.textContent=text().print;
            var course=courses?.find(c=>fold(clean(record.original))===fold(c.name) || fold(clean(record.original)).startsWith(fold(c.name)+' ('));
            if (course && !record.link) {
                record.course=course;
                var link=anchor(course.href,course.name);link.className='univer-app-attendance-course';link.title=text().umkd;
                record.name.replaceWith(link);record.link=link;
            }
        });
        document.querySelectorAll('#l4 a.syllabus-info').forEach(link=>{
            if (!headings.size) return;
            remember(link);link.hidden=true;link.tabIndex=-1;link.setAttribute('aria-hidden','true');link.setAttribute('data-univer-syllabus-source','true');
        });
    }
    function courseFor(link) {
        var found=null;
        headings.forEach((record,cell)=>{ if (record.button===link || cell.compareDocumentPosition(link)&Node.DOCUMENT_POSITION_FOLLOWING) found=record.course; });
        return found;
    }
    function headingFor(button) { return Array.from(headings.values()).find(record=>record.button===button); }
    function anchor(href,title) { var a=document.createElement('a'); a.href=href; a.textContent=title; return a; }
    function ownText() { return language==='en'?'Your programme':language==='kz'?'Сіздің мамандығыңыз':'Ваша специальность'; }
    // The syllabus written for the student's programme (syllabus-tasks.js reads the printed ones) is tagged once the list
    // is on screen. It moves to the top only while nobody is using the list: at once when the answer was kept, otherwise
    // on the next opening.
    function markOwn(record,epoch) {
        var prints=record.items.filter(item=>item.print), own=window.__univerSyllabusV1?.own;
        if (!prints.length || typeof own!=='function') return;
        own(prints.map(item=>item.href)).then(href=>{
            var item=href && record.items?.find(i=>i.href===href);
            if (!enabled || generation!==epoch || !item?.li?.isConnected) return;
            // The tag stands beside the link (out of its underline), and the link names it to a screen reader.
            var tag=document.createElement('span'); tag.className='univer-app-own-tag'; tag.textContent=ownText(); tag.id=record.panel.id+'-own';
            item.li.classList.add('univer-app-syllabus-own'); item.link.after(tag); item.link.setAttribute('aria-describedby',tag.id); record.own=href; record.tag=tag;
            placeOwn(record);
        },()=>{ if (generation===epoch) record.ownFailed=true; });
    }
    function busy(node) {
        try { if (node.matches(':hover')) return true; } catch (_) {}
        return node.contains(document.activeElement);
    }
    function placeOwn(record) {
        var item=record.own && record.items?.find(i=>i.href===record.own), list=record.list;
        if (!item || !list || list.firstElementChild===item.li) return;
        var move=()=>list.prepend(item.li);
        if (record.panel.hidden) move();
        else if (!busy(list)) { if (window.__univerMotionUI) window.__univerMotionUI.reflow(list,move); else move(); }
    }
    // An open list makes its card taller; the cards beside it in the same grid row keep their own height instead of
    // stretching into an empty box. Cards of other rows still line up.
    var kept=new Set();
    function ownHeight() {
        var open=new Set(), want=new Set(), card=tr=>tr.tagName==='TR' && !!tr.querySelector(':scope>td.ct>.univer-app-attendance-summary[data-open=false]');
        panels.forEach(record=>{ var tr=record.panel.hidden?null:record.panel.closest('tr'); if (tr && card(tr)) open.add(tr); });
        open.forEach(tr=>{
            var top=tr.getBoundingClientRect().top;
            Array.from(tr.parentElement?.children||[]).forEach(other=>{ if (!open.has(other) && card(other) && Math.abs(other.getBoundingClientRect().top-top)<1) want.add(other); });
        });
        kept.forEach(tr=>{ if (!want.has(tr)) { tr.removeAttribute('data-univer-own-height'); kept.delete(tr); } });
        want.forEach(tr=>{ if (!kept.has(tr)) { tr.setAttribute('data-univer-own-height',''); kept.add(tr); } });
    }
    async function show(link, force, event) {
        var record=panels.get(link);
        var animate=event?.detail>0;
        var motion=window.__univerMotionUI;
        if (record && !force) {
            var opening=record.panel.hidden || record.closing;
            link.setAttribute('aria-expanded',String(opening));
            motion?.stop(record.panel);
            if (!opening) {
                // The list folds shut before it is hidden, so the card closes up instead of jumping.
                var hide=()=>{
                    record.closing=false; record.panel.hidden=true;
                    var draw=record.afterClose; record.afterClose=null; draw?.();
                    ownHeight();
                };
                record.closing=true;
                if (animate && motion?.collapse) motion.collapse(record.panel,hide,180); else hide();
                return;
            }
            // A lookup that failed is asked again when the list opens again.
            if (record.ownFailed) { record.ownFailed=false; markOwn(record,generation); }
            placeOwn(record); record.panel.hidden=false; ownHeight();
            if (animate) { motion?.resize(record.panel,1,220); motion?.reveal(record.panel,'content'); }
            return;
        }
        if (!record) {
            var panel=document.createElement('section'); panel.className='univer-app-syllabi'; panel.id='univer-syllabi-'+panels.size; panel.setAttribute('aria-label',text().print);
            (headingFor(link)?.header || link.closest('.ts') || link).after(panel); record={panel,items:null}; panels.set(link,record);
            remember(link); link.setAttribute('aria-controls',panel.id);
        }
        var epoch=generation, panel=record.panel; motion?.stop(panel); record.closing=false; panel.hidden=false; panel.textContent=''; link.setAttribute('aria-expanded','true');
        var status=document.createElement('p'); status.setAttribute('role','status'); status.className='univer-app-pending'; status.textContent=text().loading; panel.appendChild(status);
        ownHeight();
        if (animate) { motion?.resize(panel,1,220); motion?.reveal(panel,'content'); }
        var course, items;
        try {
            await loadCourses(); if (!enabled || generation!==epoch) return;
            course=courseFor(link); if (!course) throw new Error('Course not found');
            var doc=await read(course.href); if (!enabled || generation!==epoch || !panel.isConnected) return;
            var seen=new Set(); items=[];
            doc.querySelectorAll('#l4 a[href]').forEach(a=>{
                var url=new URL(a.getAttribute('href'),location.origin), name=clean(a);
                if (url.origin!==location.origin || url.search || url.hash || seen.has(url.href)) return;
                var print=/^\/student\/interactive\/syllabus\/print\/\d+\/\d+\/?$/.test(url.pathname);
                var file=/^\/student\/umkd\/get\/\d+\/\d+\/\d{4}\/[12]\/?$/.test(url.pathname) && /силлаб|syllab/i.test(name);
                if (!print && !file) return;
                seen.add(url.href); items.push({href:url.href,name:name||course.name,print});
            });
        } catch (_) { items=null; }
        if (!enabled || generation!==epoch || !panel.isConnected) return;
        var draw=()=>{
            var before=0;
            if (!panel.hidden) {
                if (items) before=panel.getBoundingClientRect().height;
                window.__univerMotionUI?.stop(panel);
            }
            record.items=items; record.list=null; record.own=null; panel.replaceChildren();
            if (!items) {
                status.className=''; status.textContent=text().failed; panel.appendChild(status);
                var retry=document.createElement('button'); retry.type='button'; retry.textContent=text().retry; retry.addEventListener('click',event=>show(link,true,event));
                panel.appendChild(retry);
            }
            else if (!items.length) { status.className=''; status.textContent=text().empty; panel.appendChild(status); }
            else {
                var list=document.createElement('ul');
                items.forEach(item=>{ var li=document.createElement('li'), type=document.createElement('span'); type.className='univer-app-syllabus-type'; type.textContent=item.print?text().printType:text().file; item.link=anchor(item.href,item.name); item.li=li; li.append(item.link,type); list.appendChild(li); });
                panel.appendChild(list); record.list=list;
                markOwn(record,epoch);
            }
            panel.appendChild(anchor(course?.href||listHref(),text().umkd));
            if (animate && !panel.hidden) {
                if (items) window.__univerMotionUI?.resize(panel,before);
                window.__univerMotionUI?.reveal(panel,'content');
            }
        };
        if (record.closing) record.afterClose=draw; else draw();
    }
    document.addEventListener('click',event=>{
        if (!enabled || event.button!==0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        var link=event.target.closest?.('#l4 a.syllabus-info');
        if (link) { event.preventDefault(); event.stopImmediatePropagation(); if(!link.hidden)show(link,false,event); }
        else if (event.target.closest?.('.univer-app-attendance-course')) event.stopPropagation();
    },true);
    document.addEventListener('keydown',event=>{
        if (enabled && event.key===' ' && event.target.matches?.('#l4 a.syllabus-info') && !event.target.hidden) {event.preventDefault();event.target.click();}
    });
    function apply(lang) {
        language=lang;
        if (document.readyState==='loading') return;
        if (!/^\/student\/attendance\/(full|show)(\/|$)/.test(location.pathname)) return;
        enabled=true;
        if (!courses && !pending && !attempted) loadCourses().catch(()=>{});
        connectCourses();
        panels.forEach(record=>{ if (record.tag && record.tag.textContent!==ownText()) record.tag.textContent=ownText(); });
        if (kept.size || Array.from(panels.values()).some(record=>!record.panel.hidden)) ownHeight();
    }
    function release() {
        enabled=false; generation++; controller?.abort(); courses=pending=null; attempted=false;
        panels.forEach(record=>{record.afterClose=null;window.__univerMotionUI?.stop(record.panel);record.panel.remove();}); panels.clear();
        kept.forEach(tr=>tr.removeAttribute('data-univer-own-height')); kept.clear();
        headings.forEach(record=>{record.header.remove();record.original.replaceWith(...record.original.childNodes);});headings.clear();
        changed.forEach((attrs,link)=>attrs.forEach(a=>{ if (a[1]===null) link.removeAttribute(a[0]); else link.setAttribute(a[0],a[1]); })); changed.clear();
    }
    window.__univerStudyV1={apply,release};
})();
