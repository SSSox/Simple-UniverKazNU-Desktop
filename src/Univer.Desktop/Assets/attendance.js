/* Attendance filters hide complete lesson sections; the site's matrices and overall grades stay intact. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerAttendanceV1) return;
    var common = window.__univerCommonV1, clean = common.clean, ownRows = common.ownRows;
    var key='univer.desktop.attendance-kind.v1', selected='all', language='ru', state=null;
    var courseKey='univer.desktop.attendance-course.v1:'+location.pathname, selectedCourse='all';
    var hidden=new Map(), labels=new Map(), owned=[], identities=new WeakMap(), sequence=0;
    // The user's own choice per course; without one, details open only when a single course is shown.
    var expanded=new Map(), focusToggle=null;
    try { var saved=sessionStorage.getItem(key); if (/^(all|lecture|seminar|sro)$/.test(saved)) selected=saved; } catch (_) {}
    try { var course=sessionStorage.getItem(courseKey); if (course && course.length<=1000) selectedCourse=course; } catch (_) {}
    function courseText() {
        return language==='en'?{label:'Courses',all:'All',empty:'No classes yet'}:
            language==='kz'?{label:'Пәндер',all:'Барлығы',empty:'Сабақтар әлі жоқ'}:{label:'Предметы',all:'Все',empty:'Занятия пока не добавлены'};
    }
    function text() {
        return language==='en' ? {all:'All classes', lecture:'Lectures', seminar:'Seminars', sro:'Independent work', filter:'Show classes', totals:'Totals by class type', overall:'Overall course total', empty:'No classes of this type', ungraded:'No grades yet', note:'The overall course total includes every class type.', lectureLabel:'Lecture', seminarLabel:'Seminar', sroLabel:'Independent work', overview:'Course summary', showDetails:'Show class log', hideDetails:'Hide class log'} :
            language==='kz' ? {all:'Барлық сабақтар', lecture:'Дәрістер', seminar:'Семинарлар', sro:'СӨЖ', filter:'Сабақтарды көрсету', totals:'Сабақ түрлері бойынша қорытынды', overall:'Пән бойынша жалпы қорытынды', empty:'Бұл түрдегі сабақтар жоқ', ungraded:'Бағалар әлі қойылмаған', note:'Пән бойынша жалпы қорытынды сабақтардың барлық түрін қамтиды.', lectureLabel:'Дәріс', seminarLabel:'Семинар', sroLabel:'СӨЖ', overview:'Пәндер бойынша қорытынды', showDetails:'Сабақ журналын көрсету', hideDetails:'Сабақ журналын жасыру'} :
            {all:'Все занятия', lecture:'Лекции', seminar:'Семинары', sro:'СРО', filter:'Показывать занятия', totals:'Итоги по видам занятий', overall:'Общий итог предмета', empty:'Нет занятий этого вида', ungraded:'Оценки пока не выставлены', note:'Общий итог предмета учитывает все виды занятий.', lectureLabel:'Лекция', seminarLabel:'Семинар', sroLabel:'СРО / СРОП', overview:'Сводка по предметам', showDetails:'Показать журнал занятий', hideDetails:'Скрыть журнал занятий'};
    }
    // Server totals read as "РК1 (100): 51 РК2 (100): 0"; each control period becomes a score tile.
    function scores(value) {
        var found=[], pattern=/((?:РК|RK|МБ|АБ)\s*[12]|(?:Экз|Exam|Емтихан|Итог|Total)\p{L}*)\s*(?:\(\s*(\d+(?:[.,]\d+)?)\s*\))?\s*:\s*(-?\d+(?:[.,]\d+)?)?/giu, match;
        while ((match=pattern.exec(value))) found.push({label:match[1].replace(/\s+/g,''),max:match[2]||'',value:match[3]||''});
        return found;
    }
    function scoreTiles(list,compact) {
        var box=document.createElement('div'); box.className='univer-app-attendance-scores'+(compact?' univer-app-attendance-scores-compact':'');
        list.forEach(item=>{
            var tile=document.createElement('div'); tile.className='univer-app-score'; tile.setAttribute('role','group');
            tile.setAttribute('aria-label',item.label+(item.max?' ('+item.max+')':'')+': '+(item.value||'–'));
            var label=document.createElement('span'); label.className='univer-app-score-label'; label.textContent=item.label;
            var value=document.createElement('span'); value.className='univer-app-score-value'; value.textContent=item.value||'–';
            tile.append(label,value);
            if (item.max) {
                var max=document.createElement('span'); max.className='univer-app-score-max'; max.textContent='/ '+item.max; tile.appendChild(max);
                var ratio=Number(item.value.replace(',','.'))/Number(item.max.replace(',','.'));
                if (!compact && item.value && isFinite(ratio)) {
                    var bar=document.createElement('span'); bar.className='univer-app-score-bar';
                    bar.style.setProperty('--univer-score',(Math.round(Math.max(0,Math.min(1,ratio))*1000)/10)+'%'); tile.appendChild(bar);
                }
            }
            Array.from(tile.children).forEach(child=>child.setAttribute('aria-hidden','true'));
            box.appendChild(tile);
        });
        return box;
    }
    function id(node) { if (!identities.has(node)) identities.set(node,++sequence); return identities.get(node); }
    function kind(value) {
        if (/^(лекц|дәріс|lecture)/i.test(value)) return 'lecture';
        if (/^(семинар|seminar)/i.test(value)) return 'seminar';
        if (/^(ср[ос]|сро[п]?|с[өо]ж|б[өо]ж|independent|self[ -]?study)/i.test(value)) return 'sro';
        return '';
    }
    function before(a,b) { return !!(a.compareDocumentPosition(b)&Node.DOCUMENT_POSITION_FOLLOWING); }
    function boundary(cell) {
        var row=cell.closest('tr');
        return row && Array.from(row.cells).every(c=>c===cell || !clean(c)) ? row : cell;
    }
    function model() {
        var root=document.querySelector('#l4'); if (!root) return [];
        var events=[];
        root.querySelectorAll('.ct').forEach(cell=>{
            if (cell.closest('#tools,.univer-app-attendance-summary')) return;
            // Observed course headings contain the teaching date range; summary cells contain bold RK totals.
            if (/\d{2}\.\d{2}\.\d{4}\s*[-–]\s*\d{2}\.\d{2}\.\d{4}/.test(clean(cell))) events.push({tag:'course',cell,start:boundary(cell)});
            else if (cell.querySelector('b,strong') && /(?:РК|RK|МБ|АБ)\s*[12]/i.test(clean(cell))) events.push({tag:'overall',cell,start:boundary(cell)});
        });
        root.querySelectorAll('a.lt[href*="/student/attendance/show/"],a[data-univer-lesson-label]').forEach(link=>{
            var type=kind(clean(link));
            var cell=link.closest('.ts') || link.parentElement;
            events.push({tag:'lesson',cell,link,type,start:boundary(cell)});
        });
        events.sort((a,b)=>a.start===b.start?0:before(a.start,b.start)?-1:1);
        var groups=[], group=null;
        events.forEach((event,index)=>{
            if (event.tag==='course') {
                if (group) group.end=event.start;
                var source=clean(event.cell.querySelector('.univer-app-course-source')||event.cell);
                var name=source.match(/^(.*?)\s*(?:\(\[|\d{2}\.\d{2}\.\d{4})/)?.[1]?.trim()||source;
                group={heading:event.cell,start:event.start,end:null,token:source.normalize('NFC').toLocaleLowerCase(),name,lessons:[],overall:null}; groups.push(group);
            }
            else if (event.tag==='overall') { if (group) group.overall=event.cell; }
            else {
                // A single-group attendance/show page may omit the full-course heading.
                if (!group) { group={heading:null,lessons:[],overall:null}; groups.push(group); }
                event.end=events[index+1]?.start || null; group.lessons.push(event);
                event.tables=Array.from(root.querySelectorAll('table.inner')).filter(table=>before(event.start,table) && (!event.end || before(table,event.end)) && !table.contains(event.start));
            }
        });
        return groups.filter(group=>group.heading||group.lessons.some(lesson=>lesson.type));
    }
    function sums(table) {
        var rows=ownRows(table), entries=[], grid=[];
        // Logical columns account for the observed two-row RK label and any merged date headings.
        rows.forEach((row,r)=>{
            if (!grid[r]) grid[r]=[];
            var column=0;
            Array.from(row.cells).forEach(cell=>{
                while (grid[r][column]) column++;
                for (var y=r;y<r+cell.rowSpan;y++) {
                    if (!grid[y]) grid[y]=[];
                    for (var x=column;x<column+cell.colSpan;x++) grid[y][x]=cell;
                }
                column+=cell.colSpan;
            });
        });
        rows.forEach((row,r)=>{
            var last=grid[r]?.length-1, header=grid[r]?.[last];
            if (!header || header.tagName!=='TH' || !/^(сумма|sum|total|жиыны|барлығы)$/i.test(clean(header))) return;
            var period=clean(grid[r][0]);
            for (var n=r+1;n<rows.length;n++) {
                var value=grid[n]?.[last];
                if (!value || value.tagName!=='TD') break;
                // A spanned value is a single server total, never count it twice.
                if (!entries.some(e=>e.cell===value)) entries.push({period,value:clean(value),cell:value});
            }
        });
        return entries;
    }
    function setHidden(node,value) {
        if (!hidden.has(node)) hidden.set(node,{value:node.hidden,attribute:node.getAttribute('hidden'),classAbsent:!node.hasAttribute('class')});
        var next=hidden.get(node).value||value;
        if (node.hidden!==next) node.hidden=next;
        node.classList.add('univer-app-attendance-part');
    }
    function resetVisibility() {
        hidden.forEach((record,node)=>{
            if (record.attribute===null) node.removeAttribute('hidden'); else node.setAttribute('hidden',record.attribute);
            node.classList.remove('univer-app-attendance-part');
            if (record.classAbsent&&!node.className) node.removeAttribute('class');
        }); hidden.clear();
    }
    function makeLabel(link) {
        if (!labels.has(link)) {
            var caption=document.createElement('span'); caption.className='univer-app-lesson-label';
            var attrs=['hidden','data-univer-lesson-label','aria-hidden','tabindex'].map(name=>[name,link.getAttribute(name)]);
            link.after(caption); link.hidden=true; link.setAttribute('data-univer-lesson-label','true'); link.setAttribute('aria-hidden','true'); link.tabIndex=-1;
            labels.set(link,{caption,attrs,signature:null});
        }
        var record=labels.get(link), signature=language+'|'+clean(link); if (record.signature===signature) return;
        record.signature=signature;record.caption.replaceChildren();
        var parts=clean(link).match(/^(.+?)\s+[-–—]\s+(.+)$/), title=document.createElement('strong');
        if (!parts) {record.caption.textContent=clean(link);return;}
        title.textContent=text()[kind(clean(link))+'Label'];record.caption.appendChild(title);
        if (parts) {var teacher=document.createElement('span');teacher.textContent=parts[2];record.caption.appendChild(teacher);}
    }
    // Keep source links and their listeners for Original mode, but prevent programmatic activation in Reading mode too.
    document.addEventListener('click',event=>{
        if (state && event.target.closest?.('a[data-univer-lesson-label]')) { event.preventDefault(); event.stopImmediatePropagation(); }
    },true);
    function insertAfterCell(cell,node,above) {
        if (cell.matches('td,th') && cell.parentElement?.tagName==='TR') {
            var row=document.createElement('tr'), holder=document.createElement('td'); row.className='univer-app-attendance-owned';
            holder.colSpan=Array.from(cell.parentElement.cells).reduce((sum,c)=>sum+c.colSpan,0); holder.appendChild(node); row.appendChild(holder); if(above)cell.parentElement.before(row);else cell.parentElement.after(row); owned.push(row); return row;
        } else { if(above)cell.before(node);else cell.after(node); owned.push(node); return node; }
    }
    function summary(group,open) {
        var box=document.createElement('section'); box.className='univer-app-attendance-summary';
        // A class-type filter is about the class log; the course total would only repeat what the filter hides.
        if (group.overall && selected!=='all') setHidden(boundary(group.overall),true);
        if (group.overall && selected==='all') {
            var overall=document.createElement('div');overall.className='univer-app-attendance-overall-block';
            var caption=document.createElement('p');caption.className='univer-app-attendance-overall';caption.textContent=text().overall;
            var value=document.createElement('p');value.className='univer-app-attendance-overall-value';value.textContent=clean(group.overall);
            overall.append(caption,value);box.appendChild(overall);
            // Recognized periods are shown as tiles; the server wording stays the fallback for anything else.
            var periods=scores(clean(group.overall));
            if (periods.length) { overall.insertBefore(scoreTiles(periods,false),value); value.hidden=true; }
            // The visible copy is passive text. Original restores the unchanged server nodes in their original place.
            setHidden(boundary(group.overall),true);
        }
        var typesBox=document.createElement('div');typesBox.className='univer-app-attendance-type-totals';box.appendChild(typesBox);
        var title=document.createElement('p'); title.className='univer-app-attendance-summary-title'; title.textContent=text().totals; typesBox.appendChild(title);
        var types=selected==='all'?['lecture','seminar','sro']:[selected], any=false;
        types.forEach(type=>{
            var lessons=group.lessons.filter(l=>l.type===type); if (!lessons.length) return;
            any=true;
            var values=new Map();
            lessons.forEach(lesson=>lesson.tables.forEach(table=>sums(table).forEach(entry=>{
                if (!values.has(entry.period)) values.set(entry.period,[]);
                values.get(entry.period).push(entry.value);
            })));
            // Each class type is one row: a coloured type label, then a compact score chip per control period.
            var row=document.createElement('div'); row.className='univer-app-type-row'; row.dataset.kind=type;
            var label=document.createElement('span'); label.className='univer-app-type-label'; label.textContent=text()[type]; row.appendChild(label);
            var content=[];
            values.forEach((items,period)=>{
                var numeric=items.every(value=>/^-?\d+(?:[.,]\d+)?$/.test(value));
                var precision=Math.max(...items.map(value=>(value.match(/[.,](\d+)$/)?.[1]||'').length)), scale=Math.pow(10,precision);
                numeric=numeric&&precision<=8&&items.every(value=>Math.abs(Number(value.replace(',','.')))*scale<Number.MAX_SAFE_INTEGER/items.length);
                var total=numeric?String(items.reduce((sum,v)=>sum+Math.round(Number(v.replace(',','.'))*scale),0)/scale):items.filter(Boolean).join(' + ');
                content.push({label:period,max:'',value:total||''});
            });
            if (content.length) row.appendChild(scoreTiles(content,true));
            else { var value=document.createElement('span'); value.className='univer-app-type-empty'; value.textContent=text().ungraded; row.appendChild(value); }
            typesBox.appendChild(row);
        });
        if (!any) { var empty=document.createElement('p'); empty.textContent=selected==='all'?courseText().empty:text().empty; typesBox.appendChild(empty); }
        // Nothing to total: one quiet line instead of an empty two-column card.
        if (!any && !group.overall) { box.classList.add('univer-app-attendance-summary-empty'); title.remove(); }
        if (group.heading && group.lessons.length && selected==='all') {
            var toggle=document.createElement('button'); toggle.type='button'; toggle.className='univer-app-attendance-details-toggle';
            toggle.setAttribute('aria-expanded',String(open)); toggle.textContent=open?text().hideDetails:text().showDetails;
            toggle.addEventListener('click',event=>{
                var parent=group.heading.closest('table'), before=parent?.getBoundingClientRect().height || 0;
                window.__univerMotionUI?.stop(parent);
                expanded.set(group.token,!open);focusToggle=group.token;render(model());
                if(event.detail>0) {
                    window.__univerMotionUI?.resize(parent,before,open?180:220);
                    owned.filter(node=>!node.closest('[hidden]')).forEach(node=>window.__univerMotionUI?.reveal(node,'content'));
                }
            });
            box.appendChild(toggle);
            if (focusToggle===group.token) { focusToggle=null; box.dataset.univerFocus='true'; }
        }
        return insertAfterCell(group.heading||group.lessons[0].cell,box);
    }
    function overview(groups) {
        var list=state.overview, courses=groups.filter(group=>group.heading);
        list.replaceChildren(); list.hidden=selectedCourse!=='all'||courses.length<2||selected!=='all';
        if (list.hidden) return;
        var title=document.createElement('p'); title.className='univer-app-attendance-overview-title'; title.textContent=text().overview;
        var rows=document.createElement('ul'); rows.className='univer-app-attendance-overview-list';
        courses.forEach(group=>{
            var row=document.createElement('li');
            var name=document.createElement('button'); name.type='button'; name.className='univer-app-attendance-overview-name'; name.textContent=group.name;
            name.addEventListener('click',event=>chooseCourse(group.token,event)); row.appendChild(name);
            var periods=group.overall?scores(clean(group.overall)):[];
            if (periods.length) row.appendChild(scoreTiles(periods,true));
            else { var none=document.createElement('span'); none.className='univer-app-attendance-overview-empty'; none.textContent=group.overall?clean(group.overall):text().ungraded; row.appendChild(none); }
            rows.appendChild(row);
        });
        list.append(title,rows);
    }
    function saveCourse() { try { if(selectedCourse==='all')sessionStorage.removeItem(courseKey);else sessionStorage.setItem(courseKey,selectedCourse); } catch (_) {} }
    function chooseCourse(token,event) {
        var from=document.activeElement;
        selectedCourse=token;saveCourse();render(model());
        // The pressed overview row is rebuilt; keyboard focus moves to the same course in the course list.
        if(from&&from!==document.body&&!from.isConnected){var target=token==='all'?state.allCourse:state.courseButtons.get(token);target?.focus?.({preventScroll:true});}
        if(event?.detail>0) owned.filter(node=>!node.closest('[hidden]')).forEach(node=>window.__univerMotionUI?.reveal(node,'forward'));
        if (state.host.getBoundingClientRect().top<0) state.host.scrollIntoView?.({block:'start'});
    }
    function courseControls(groups) {
        var courses=groups.filter(group=>group.heading);
        if(selectedCourse!=='all'&&!courses.some(group=>group.token===selectedCourse)){selectedCourse='all';saveCourse();}
        state.courses.hidden=!courses.length;state.courses.setAttribute('aria-label',courseText().label);state.courseCaption.textContent=courseText().label;
        state.allCourse.textContent=courseText().all;state.allCourse.setAttribute('aria-pressed',String(selectedCourse==='all'));
        var keep=new Set();
        courses.forEach((group,index)=>{
            keep.add(group.token);var button=state.courseButtons.get(group.token);
            if(!button){button=document.createElement('button');button.type='button';button.dataset.course=group.token;button.addEventListener('click',event=>chooseCourse(group.token,event));state.courseButtons.set(group.token,button);}
            if(button.textContent!==group.name)button.textContent=group.name;
            button.setAttribute('aria-pressed',String(selectedCourse===group.token));
            var position=state.courseList.children[index+1];
            if(position!==button)state.courseList.insertBefore(button,position||null);
        });
        state.courseButtons.forEach((button,token)=>{if(!keep.has(token)){button.remove();state.courseButtons.delete(token);}});
    }
    function hideRange(elements,start,end,exclude,last) {
        var contained=node=>(node===start||before(start,node))&&(!end||(before(node,end)&&!node.contains(end)))&&
            (!last||node===last||last.contains(node)||(before(node,last)&&!node.contains(last)));
        elements.filter(node=>contained(node)&&!contained(node.parentElement)).forEach(node=>setHidden(node,exclude));
    }
    function render(groups) {
        resetVisibility(); owned.forEach(node=>{window.__univerMotionUI?.stop(node);node.remove();}); owned=[];
        courseControls(groups);
        groups.forEach(group=>group.lessons.forEach(lesson=>{if(lesson.type)makeLabel(lesson.link);}));
        var root=document.querySelector('#l4'), elements=Array.from(root.querySelectorAll('*'));
        // With all courses listed, each course starts as its summary; one chosen course shows its class log.
        var single=selectedCourse!=='all'||groups.filter(group=>group.heading).length<2;
        groups.forEach(group=>{
            var excludeCourse=selectedCourse!=='all'&&selectedCourse!==group.token;
            // Choosing lectures, seminars or independent work opens every course's log of that type.
            var open=!group.heading||selected!=='all'||(expanded.has(group.token)?expanded.get(group.token):single);
            group.lessons.forEach(lesson=>{
                var exclude=excludeCourse||!open||(selected!=='all'&&selected!==lesson.type);
                // Hide only maximal elements wholly inside this lesson's document-order range.
                // This works both with sibling blocks and with legacy tables nesting multiple subjects.
                hideRange(elements,lesson.start,lesson.end,exclude);
            });
            if(excludeCourse&&group.heading)hideRange(elements,group.start,group.end,true,!group.end&&group.overall?boundary(group.overall):null);
            var host=summary(group,open);if(excludeCourse)setHidden(host,true);
        });
        overview(groups);
        state.bar.querySelectorAll('button').forEach(button=>{button.textContent=text()[button.dataset.kind];button.setAttribute('aria-pressed',String(selected===button.dataset.kind));});
        state.bar.setAttribute('aria-label',text().filter);
        // The note explains a type filter, so it appears only while one is active.
        state.caption.textContent=text().filter;state.note.textContent=text().note;state.note.hidden=selected==='all';
        var focused=root.querySelector('.univer-app-attendance-summary[data-univer-focus] .univer-app-attendance-details-toggle');
        if (focused) { focused.closest('.univer-app-attendance-summary').removeAttribute('data-univer-focus'); focused.focus?.({preventScroll:true}); }
    }
    function apply(lang) {
        language=lang;
        if (document.readyState==='loading') return;
        if (!/^\/student\/attendance\/(full|show)(\/|$)/.test(location.pathname)) return;
        var groups=model(); if (!groups.length) { if(state)release(); return; }
        if(state&&!state.host.isConnected){release();groups=model();}
        var signature=language+'|'+groups.map(g=>id(g.heading||g.lessons[0].cell)+':'+(g.token||'')+g.lessons.map(l=>id(l.link)+clean(l.link)+l.tables.map(t=>id(t)+clean(t)).join('|')).join('/')+(g.overall?clean(g.overall): '')).join(';');
        if (!state) {
            var bar=document.createElement('div'); bar.className='univer-app-attendance-filters'; bar.setAttribute('role','group');
            var caption=document.createElement('span');caption.className='univer-app-attendance-filter-caption';bar.appendChild(caption);
            ['all','lecture','seminar','sro'].forEach(type=>{
                var button=document.createElement('button'); button.type='button'; button.dataset.kind=type;
                button.addEventListener('click',event=>{selected=type;try{sessionStorage.setItem(key,type);}catch(_){}render(model());if(event.detail>0)owned.filter(node=>!node.closest('[hidden]')).forEach(node=>window.__univerMotionUI?.reveal(node,'content'));}); bar.appendChild(button);
            });
            var tools=document.createElement('div');tools.className='univer-app-attendance-tools';
            var courses=document.createElement('nav');courses.className='univer-app-attendance-courses';
            var courseCaption=document.createElement('p');courseCaption.className='univer-app-attendance-course-caption';
            var courseList=document.createElement('div');courseList.className='univer-app-attendance-course-list';
            var allCourse=document.createElement('button');allCourse.type='button';allCourse.dataset.course='all';allCourse.addEventListener('click',event=>chooseCourse('all',event));courseList.appendChild(allCourse);
            courses.append(courseCaption,courseList);
            courses.addEventListener('keydown',event=>{
                if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key)||!event.target.matches('button[data-course]'))return;
                event.preventDefault();var buttons=Array.from(courseList.children),index=buttons.indexOf(event.target);
                index=event.key==='Home'?0:event.key==='End'?buttons.length-1:Math.max(0,Math.min(buttons.length-1,index+(event.key==='ArrowRight'?1:-1)));
                buttons[index].focus();buttons[index].click();
            });
            var note=document.createElement('p');note.className='univer-app-attendance-filter-note';
            var summaryList=document.createElement('section');summaryList.className='univer-app-attendance-overview';tools.append(courses,bar,note,summaryList);
            insertAfterCell(groups[0].heading||groups[0].lessons[0].cell,tools,true);
            // The filter bar has its own lifetime; summary rebuilds must not remove it.
            var host=owned.pop(); state={bar,host,caption,note,courses,courseCaption,courseList,allCourse,courseButtons:new Map(),overview:summaryList,signature:null};
        }
        if (state.signature!==signature) { state.signature=signature; render(groups); }
    }
    function release() {
        resetVisibility(); owned.forEach(node=>{window.__univerMotionUI?.stop(node);node.remove();}); owned=[];
        labels.forEach((record,link)=>{record.caption.remove();record.attrs.forEach(a=>{if(a[1]===null)link.removeAttribute(a[0]);else link.setAttribute(a[0],a[1]);});}); labels.clear();
        state?.host.remove(); state=null;
    }
    window.__univerAttendanceV1={apply,release};
})();
