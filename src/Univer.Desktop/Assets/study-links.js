/* Read-only links from attendance to the observed UMKD course list and its syllabi. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerStudyV1) return;
    var common = window.__univerCommonV1, clean = common.clean, fold = common.fold;
    var enabled = false, language = 'ru', generation = 0, courses = null, pending = null, attempted = false;
    var changed = new Map(), headings = new Map(), panels = new Map(), controllers = new Set();
    var materials = new Map(), warming = false;
    var COURSE = /^\/student\/umkd\/\d+\/\d{4}\/[12]\/?$/;
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
        var abort = new AbortController(), timeout = setTimeout(() => abort.abort(),10000); controllers.add(abort);
        var cancelled = new Promise((_,reject)=>abort.signal.addEventListener('abort',()=>reject(new Error('UMKD read cancelled')),{once:true}));
        cancelled.catch(()=>{}); // fetch may throw before the race starts
        try {
            var response = await Promise.race([cancelled,fetch(url,{credentials:'same-origin',cache:'no-store',redirect:'error',signal:abort.signal})]);
            var final = new URL(response.url,location.origin), expected = new URL(url,location.origin);
            if (response.status !== 200 || final.origin !== location.origin || final.pathname.replace(/\/$/,'') !== expected.pathname.replace(/\/$/,'') || +response.headers?.get('content-length') > 4*1024*1024) throw new Error('Unexpected UMKD response');
            var html = await Promise.race([cancelled,common.boundedText(response, 4 * 1024 * 1024)]);
            var doc = new DOMParser().parseFromString(html,'text/html');
            if (doc.querySelector('#login_frm')) throw new Error('Session expired');
            return doc;
        } finally { clearTimeout(timeout); abort.abort(); controllers.delete(abort); }
    }
    async function loadCourses() {
        if (courses) return courses;
        if (pending) return pending;
        var epoch = generation; attempted = true;
        var address=listHref(), lang=language, journal=window.__univerSyllabusV1?.journal, saved=journal?.(address,lang);
        if (saved?.length && saved.every(course=>typeof course?.name==='string' && COURSE.test(course.href))) {
            courses=saved; connectCourses(); return courses;
        }
        pending = (async function () {
            var doc = await read(address);
            // The observed dblclick handler is /student/umkd/ + this.id + /year/term.
            // Parse only that route; never execute a fetched site's scripts.
            var term = semester() || Array.from(doc.scripts).map(s=>s.textContent).join('\n').match(/["']\/student\/umkd\/["']\s*\+\s*this\.id\s*\+\s*["'](\/\d{4}\/[12])["']/)?.[1];
            if (!term) throw new Error('Course semester unavailable');
            var found = Array.from(doc.querySelectorAll('#l4 table.inner tr.link[id]')).filter(r=>/^\d+$/.test(r.id) && r.cells.length===3)
                .map(r=>({name:clean(r.cells[1]),href:'/student/umkd/'+r.id+term,id:r.id}));
            if (!found.length) throw new Error('Course list unavailable');
            if (enabled && generation===epoch) { journal?.(address,lang,found); courses=found; connectCourses(); }
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
    function ownText() { return language==='en'?'Your syllabus':language==='kz'?'Сіздің силлабусыңыз':'Ваш силлабус'; }
    function ownPendingText() { return language==='en'?'Looking for your syllabus…':language==='kz'?'Силлабусыңыз ізделуде…':'Ищу ваш силлабус…'; }
    function materialLink(href,name) {
        var url; try { url=new URL(href,location.origin); } catch (_) { return; }
        if (url.origin!==location.origin || url.search || url.hash) return;
        var print=/^\/student\/interactive\/syllabus\/print\/\d+\/\d+\/?$/.test(url.pathname);
        var file=/^\/student\/umkd\/get\/\d+\/\d+\/\d{4}\/[12]\/?$/.test(url.pathname) && /силлаб|syllab/i.test(name);
        if (print || file) return {href:url.href,name:name,print:print};
    }
    function loadItems(course,force) {
        var href=course.href, journal=window.__univerSyllabusV1?.journal, lang=language, epoch=generation;
        if (!COURSE.test(href)) return Promise.reject(new Error('Course materials unavailable'));
        if (!force && materials.has(href)) return materials.get(href);
        var saved=!force && journal?.(href,lang);
        if (saved) return Promise.resolve(saved.map(item=>typeof item?.name==='string' && materialLink(item.href,item.name)).filter(Boolean));
        var request=(async ()=>{
            var doc=await read(href), box=doc.getElementById('l4'), seen=new Set(), items=[];
            if (!box || !enabled || generation!==epoch) throw new Error('Course materials unavailable');
            box.querySelectorAll('a[href]').forEach(a=>{
                var item=materialLink(a.getAttribute('href'),clean(a));
                if (!item || seen.has(item.href)) return;
                seen.add(item.href); item.name=item.name||course.name; items.push(item);
            });
            journal?.(href,lang,items);
            return items;
        })();
        materials.set(href,request);
        request.catch(()=>{ if (materials.get(href)===request) materials.delete(href); });
        return request;
    }
    async function warmOwn(syllabus,epoch) {
        try {
            await loadCourses();
            var queued=Array.from(new Set(Array.from(headings.values(),heading=>heading.course).filter(Boolean))).slice(0,20);
            for (var course of queued) {
                if (!enabled || generation!==epoch) return;
                try {
                    var items=await loadItems(course,false);
                    if (!enabled || generation!==epoch) return;
                    var hrefs=items.filter(item=>item.print).map(item=>item.href);
                    if (hrefs.length && syllabus.peekOwn(hrefs,course.href)===undefined) await syllabus.own(hrefs,course.href);
                } catch (_) { }
            }
        } catch (_) { }
    }
    function markOwn(record,epoch) {
        var syllabus=window.__univerSyllabusV1;
        if (!record) {
            if (!warming && syllabus?.peekOwn) { warming=true; warmOwn(syllabus,epoch); }
            return;
        }
        var hrefs=record.items.filter(item=>item.print).map(item=>item.href);
        if (!hrefs.length || typeof syllabus?.own!=='function') return;
        var finish=href=>{
            record.ownStatus?.remove(); record.ownStatus=null;
            var item=href && record.items?.find(i=>i.href.replace(/\/$/,'')===href.replace(/\/$/,''));
            if (!enabled || generation!==epoch || !item?.li?.isConnected) return;
            // The tag stands beside the link, and the link names it to a screen reader.
            var tag=document.createElement('span'); tag.className='univer-app-own-tag'; tag.textContent=ownText(); tag.id=record.panel.id+'-own';
            item.li.classList.add('univer-app-syllabus-own'); item.link.after(tag); item.link.setAttribute('aria-describedby',tag.id); record.own=item.href; record.tag=tag;
            placeOwn(record);
        };
        var cached=syllabus.peekOwn?.(hrefs,record.course);
        if (cached!==undefined) { finish(cached); return; }
        if (record.ownAttempts>=2) return;
        var status=record.ownStatus=document.createElement('p');
        status.className='univer-app-pending univer-app-own-status'; status.setAttribute('role','status');
        status.textContent=ownPendingText(); record.list.before(status);
        record.ownAttempts=(record.ownAttempts||0)+1;
        syllabus.own(hrefs,record.course).then(finish,()=>{ finish(null); if (generation===epoch) record.ownFailed=true; });
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
    // An open syllabus list spans the whole grid row so it leaves no empty column beside a narrower card; the clicked
    // button stays at the same place in the window, and the grid slides around it when motion is enabled.
    var opened=new Set();
    function cardRow(panel) {
        var tr=panel.closest('tr');
        return tr?.tagName==='TR' && tr.querySelector(':scope>td.ct>.univer-app-attendance-summary') ? tr : null;
    }
    function setOpenCard(tr,on) {
        if (!tr) return;
        if (on) { tr.setAttribute('data-univer-syllabi-open',''); opened.add(tr); }
        else { tr.removeAttribute('data-univer-syllabi-open'); opened.delete(tr); }
    }
    function compensate(anchor,mutate) {
        var top=anchor?.isConnected?anchor.getBoundingClientRect().top:null;
        var out=mutate();
        if (anchor?.isConnected && top!==null) {
            var delta=anchor.getBoundingClientRect().top-top;
            if (Math.abs(delta)>=1) {
                var y=Math.max(0,(window.scrollY||window.pageYOffset||0)+delta);
                try { window.scrollTo?.({top:Math.round(y),left:window.scrollX||0,behavior:'instant'}); } catch (_) {}
            }
        }
        return out;
    }
    function placeCard(panel,anchor,animate,mutate) {
        var tr=cardRow(panel), grid=tr?.parentElement, motion=window.__univerMotionUI;
        var moving=animate && !!motion && window.__univerMotion!==false && !motion.reduced?.();
        if (grid && motion?.reflow) {
            var out;
            if (moving) motion.reflow(grid,()=>{ out=mutate(tr); },{anchor});
            else out=compensate(anchor,()=>mutate(tr));
            return out;
        }
        return compensate(anchor,()=>mutate(tr));
    }
    async function show(link, force, event) {
        var record=panels.get(link);
        var animate=event?.detail>0;
        var motion=window.__univerMotionUI;
        var moving=animate && !!motion && window.__univerMotion!==false && !motion.reduced?.();
        if (record && !force) {
            var opening=record.panel.hidden || record.closing;
            link.setAttribute('aria-expanded',String(opening));
            if (!opening) {
                motion?.stop(record.panel);
                // The list folds shut before it is hidden, then the card slides back into its column.
                var hide=()=>{
                    if (!record.closing) return;
                    record.closing=false;
                    placeCard(record.panel,link,animate,tr=>{
                        record.panel.hidden=true;
                        setOpenCard(tr,false);
                        var draw=record.afterClose; record.afterClose=null; draw?.();
                    });
                };
                record.closing=true;
                if (animate && motion?.collapse) motion.collapse(record.panel,hide); else hide();
                return;
            }
            if (record.closing) {
                var from=moving?record.panel.getBoundingClientRect().height:0;
                record.closing=false;
                motion?.stop(record.panel);
                var pendingDraw=record.afterClose; record.afterClose=null;
                if (pendingDraw) { pendingDraw(); return; }
                if (record.ownFailed) { record.ownFailed=false; markOwn(record,generation); }
                placeOwn(record);
                if (moving) motion.resize(record.panel,from);
                return;
            }
            motion?.stop(record.panel);
            // A lookup that failed is asked again when the list opens again.
            if (record.ownFailed) { record.ownFailed=false; markOwn(record,generation); }
            placeOwn(record);
            var panel=record.panel, prevStyle=panel.getAttribute('style'), target=0;
            placeCard(panel,link,animate,tr=>{
                setOpenCard(tr,true); panel.hidden=false;
                if (moving) { target=panel.getBoundingClientRect().height; panel.style.maxHeight='1px'; panel.style.overflow='hidden'; }
            });
            if (moving) {
                if (prevStyle===null) panel.removeAttribute('style'); else panel.setAttribute('style',prevStyle);
            }
            if (animate) { motion?.resize(panel,1,target>0?{target}:undefined); motion?.reveal(panel,'content'); }
            return;
        }
        if (!record) {
            var panel=document.createElement('section'); panel.className='univer-app-syllabi'; panel.id='univer-syllabi-'+panels.size; panel.setAttribute('aria-label',text().print); panel.hidden=true;
            (headingFor(link)?.header || link.closest('.ts') || link).after(panel); record={panel,items:null}; panels.set(link,record);
            remember(link); link.setAttribute('aria-controls',panel.id);
        }
        var epoch=generation, panel=record.panel; record.closing=false; motion?.stop(panel); link.setAttribute('aria-expanded','true');
        var status=document.createElement('p'); status.setAttribute('role','status'); status.className='univer-app-pending'; status.textContent=text().loading;
        var prevStyle=panel.getAttribute('style'), initTarget=0;
        placeCard(panel,link,animate,tr=>{
            setOpenCard(tr,true); panel.hidden=false; panel.textContent=''; panel.appendChild(status);
            if (moving) { initTarget=panel.getBoundingClientRect().height; panel.style.maxHeight='1px'; panel.style.overflow='hidden'; }
        });
        if (moving) {
            if (prevStyle===null) panel.removeAttribute('style'); else panel.setAttribute('style',prevStyle);
        }
        if (animate) { motion?.resize(panel,1,initTarget>0?{target:initTarget}:undefined); motion?.reveal(panel,'content'); }
        var course, items;
        try {
            await loadCourses(); if (!enabled || generation!==epoch) return;
            course=courseFor(link); if (!course) throw new Error('Course not found');
            items=(await loadItems(course,force)).map(item=>({...item}));
        } catch (_) { items=null; }
        if (!enabled || generation!==epoch || !panel.isConnected) return;
        var draw=()=>{
            var before=0;
            if (!panel.hidden) {
                if (items) before=panel.getBoundingClientRect().height;
                window.__univerMotionUI?.stop(panel);
            }
            record.items=items; record.list=null; record.own=null; record.course=course?.href; panel.replaceChildren();
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
        markOwn(null,generation);
        panels.forEach(record=>{
            if (record.tag && record.tag.textContent!==ownText()) record.tag.textContent=ownText();
            if (record.ownStatus && record.ownStatus.textContent!==ownPendingText()) record.ownStatus.textContent=ownPendingText();
        });
        panels.forEach(record=>{ var tr=cardRow(record.panel); if (tr && !record.panel.hidden) setOpenCard(tr,true); });
    }
    function release() {
        enabled=false; generation++; controllers.forEach(abort=>abort.abort()); controllers.clear();
        courses=pending=null; attempted=false; warming=false; materials.clear();
        panels.forEach(record=>{record.afterClose=null;window.__univerMotionUI?.stop(record.panel);record.panel.remove();}); panels.clear();
        opened.forEach(tr=>tr.removeAttribute('data-univer-syllabi-open')); opened.clear();
        headings.forEach(record=>{record.header.remove();record.original.replaceWith(...record.original.childNodes);});headings.clear();
        changed.forEach((attrs,link)=>attrs.forEach(a=>{ if (a[1]===null) link.removeAttribute(a[0]); else link.setAttribute(a[0],a[1]); })); changed.clear();
    }
    window.__univerStudyV1={apply,release};
})();
