/* Attendance filters hide complete lesson sections; the site's matrices and overall grades stay intact. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerAttendanceV1) return;
    var common = window.__univerCommonV1, clean = common.clean, ownRows = common.ownRows;
    var selected='all', language='ru', state=null;
    var courseKey='univer.desktop.attendance-course.v1:'+location.pathname, selectedCourse='all';
    var hidden=new Map(), labels=new Map(), marked=new Map(), owned=[], identities=new WeakMap(), sequence=0;
    // The user's own choice per course; without one, details open only when a single course is shown. Nothing of it is
    // stored (the owner's choice): every visit starts with all courses and all classes, each course as its summary. The
    // one course a home-page row asks for (overview.js) is read once and removed; choices kept by earlier versions go too.
    var expanded=new Map(), focusToggle=null;
    try {
        var course=sessionStorage.getItem(courseKey); if (course && course.length<=1000) selectedCourse=course;
        sessionStorage.removeItem(courseKey); sessionStorage.removeItem('univer.desktop.attendance-kind.v1');
    } catch (_) {}
    function courseText() {
        return language==='en'?{label:'Courses',all:'All',empty:'No classes yet'}:
            language==='kz'?{label:'Пәндер',all:'Барлығы',empty:'Сабақтар әлі жоқ'}:{label:'Предметы',all:'Все',empty:'Занятия пока не добавлены'};
    }
    function text() {
        return language==='en' ? {all:'All classes', lecture:'Lectures', seminar:'Seminars', sro:'Independent work', filter:'Class type', totals:'Totals by class type', overall:'Overall course total', empty:'No classes of this type', ungraded:'No grades yet', note:'The overall course total includes every class type.', lectureLabel:'Lecture', seminarLabel:'Seminar', sroLabel:'Independent work', overview:'Course summary', showDetails:'Show class log', hideDetails:'Hide class log', kind:'Class type', total:'Total', outOf:'of'} :
            language==='kz' ? {all:'Барлық сабақтар', lecture:'Дәрістер', seminar:'Семинарлар', sro:'СӨЖ', filter:'Сабақ түрі', totals:'Сабақ түрлері бойынша қорытынды', overall:'Пән бойынша жалпы қорытынды', empty:'Бұл түрдегі сабақтар жоқ', ungraded:'Бағалар әлі қойылмаған', note:'Пән бойынша жалпы қорытынды сабақтардың барлық түрін қамтиды.', lectureLabel:'Дәріс', seminarLabel:'Семинар', sroLabel:'СӨЖ', overview:'Пәндер бойынша қорытынды', showDetails:'Сабақ журналын көрсету', hideDetails:'Сабақ журналын жасыру', kind:'Сабақ түрі', total:'Барлығы', outOf:'/'} :
            {all:'Все занятия', lecture:'Лекции', seminar:'Семинары', sro:'СРО', filter:'Тип занятий', totals:'Итоги по видам занятий', overall:'Общий итог предмета', empty:'Нет занятий этого вида', ungraded:'Оценки пока не выставлены', note:'Общий итог предмета учитывает все виды занятий.', lectureLabel:'Лекция', seminarLabel:'Семинар', sroLabel:'СРО / СРОП', overview:'Сводка по предметам', showDetails:'Показать журнал занятий', hideDetails:'Скрыть журнал занятий', kind:'Вид занятий', total:'Итого', outOf:'из'};
    }
    // Server totals read as "РК1 (100): 51 РК2 (100): 0"; each control period becomes a score tile.
    function scores(value) {
        var found=[], pattern=/((?:РК|RK|МБ|АБ)\s*[12]|(?:Экз|Exam|Емтихан|Итог|Total)\p{L}*)\s*(?:\(\s*(\d+(?:[.,]\d+)?)\s*\))?\s*:\s*(-?\d+(?:[.,]\d+)?)?/giu, match;
        while ((match=pattern.exec(value))) found.push({label:match[1].replace(/\s+/g,''),max:match[2]||'',value:match[3]||''});
        return found;
    }
    // The same chip the home screen shows, so a grade looks alike wherever it is read.
    function gradeChips(list) {
        var box=document.createElement('span'); box.className='univer-app-overview-scores';
        list.forEach(item=>box.appendChild(common.scoreChip(item,language,true)));
        return box;
    }
    function absenceText() { return language==='en'?{label:'Absences',lecture:'lectures',seminar:'seminars',sro:'independent work',other:'other',unexcused:'Without a valid reason',excused:'With a valid reason',term:'Absences this term'}:language==='kz'?{label:'Қатыспаған сабақтар',lecture:'дәріс',seminar:'семинар',sro:'СӨЖ',other:'басқа',unexcused:'Дәлелсіз себеппен',excused:'Дәлелді себеппен',term:'Семестрдегі қатыспаған сабақтар'}:{label:'Пропуски',lecture:'лекции',seminar:'семинары',sro:'СРО',other:'другие',unexcused:'Без уважительной причины',excused:'По уважительной причине',term:'Пропуски за семестр'}; }
    // Absences are the journal's own marks in a class's date cells: «н» without a valid reason, EXCUSED with one (the
    // owner's reading: the journal of 28 November 2026 showed only «н»). Anything else in a cell is not counted.
    var EXCUSED=/^(нб|н\/б|ну)$/i;
    function absences(table) {
        var count={missed:0,excused:0};
        ownRows(table).forEach(row=>Array.from(row.cells).forEach(cell=>{
            if (cell.tagName!=='TD') return;
            var mark=clean(cell);
            // The cell shows which it is in the class log too.
            if (/^н$/i.test(mark)) { count.missed++; markCell(cell,'univer-app-absent'); }
            else if (EXCUSED.test(mark)) { count.excused++; markCell(cell,'univer-app-excused'); }
        }));
        return count;
    }
    // One labelled pill: «Без уважительной причины 3».
    function absencePair(kind,label,value) {
        var pair=document.createElement('span'); pair.className='univer-app-absence'; pair.dataset.kind=kind;
        var name=document.createElement('span'); name.textContent=label; var number=document.createElement('b'); number.textContent=String(value);
        pair.append(name,number); return pair;
    }
    // Every class counts, labs and practicals with no known type too; a type counts only its own.
    // More «н» hours than this in a term (one class = one hour) puts a warning over the courses, as on Android.
    var MISSED_LIMIT=25;
    function limitText(hours) {
        return (language==='en'?'{n} hours missed this term: more than {limit}':language==='kz'?'Семестрде {n} сағат қатыспадыңыз: бұл {limit} сағаттан көп':'Пропущено {n} ч за семестр: это больше {limit} ч')
            .replace('{n}',hours).replace('{limit}',MISSED_LIMIT);
    }
    // Every course of the term counts, whichever course or class type is shown; written only when it changes.
    function showLimit(groups) {
        var hours=groups.reduce((n,group)=>n+missed(group),0), over=hours>MISSED_LIMIT, words=over?limitText(hours):'';
        if (state.limit.hidden===over) state.limit.hidden=!over;
        if (state.limit.textContent!==words) state.limit.textContent=words;
        // The term's absences over every course above the courses: hidden when both are zero, otherwise only non-zero kinds.
        var term=state.term, said=absenceText(), away=groups.reduce((n,group)=>n+excused(group),0), has=hours>0||away>0;
        if (term.hidden===has) term.hidden=!has;
        var signature=language+'|'+hours+'|'+away;
        if (has && term.dataset.univerShown!==signature) {
            term.dataset.univerShown=signature;
            var title=document.createElement('span'); title.className='univer-app-attendance-term-title'; title.textContent=said.term;
            var nodes=[title];
            if (hours>0) nodes.push(absencePair('unexcused',said.unexcused,hours));
            if (away>0) nodes.push(absencePair('excused',said.excused,away));
            term.replaceChildren(...nodes);
        }
    }
    function missed(group,type) { return group.lessons.filter(l=>!type||l.type===type).reduce((sum,lesson)=>sum+lesson.tables.reduce((n,table)=>n+tableAbsences(table).missed,0),0); }
    function excused(group,type) { return group.lessons.filter(l=>!type||l.type===type).reduce((sum,lesson)=>sum+lesson.tables.reduce((n,table)=>n+tableAbsences(table).excused,0),0); }
    function cell(cls,text) { var node=document.createElement('span'); node.className=cls; node.setAttribute('role','cell'); if (text!=null) node.textContent=text; return node; }
    // A period's name without its "(100)".
    function periodKey(value) { return value.replace(/\s*\(.*$/,'').replace(/\s+/g,''); }
    // Rows are class types, columns the control periods, the last row the course total: one number per place.
    function scoreTable(columns,rows,periods) {
        var table=document.createElement('div'); table.className='univer-app-score-table'; table.setAttribute('role','table'); table.setAttribute('aria-label',text().totals);
        table.style.setProperty('--univer-columns',String(columns.length));
        var head=document.createElement('div'); head.className='univer-app-score-head'; head.setAttribute('role','row');
        var corner=document.createElement('span'); corner.setAttribute('role','columnheader'); corner.textContent=text().kind; head.appendChild(corner);
        columns.forEach(column=>{
            var name=document.createElement('span'); name.setAttribute('role','columnheader'); name.textContent=column.label;
            if (column.max) { var max=document.createElement('small'); max.textContent=text().outOf+' '+column.max; name.appendChild(max); }
            head.appendChild(name);
        });
        table.appendChild(head);
        rows.forEach(item=>{
            var row=document.createElement('div'); row.className='univer-app-type-row'; row.dataset.kind=item.type; row.setAttribute('role','row');
            var label=document.createElement('span'); label.className='univer-app-type-label'; label.setAttribute('role','rowheader'); label.textContent=text()[item.type]; row.appendChild(label);
            columns.forEach(column=>{
                var value=item.values.get(column.key)?.value||'';
                var score=cell('univer-app-score',value||'–'); score.setAttribute('aria-label',column.label+(column.max?' ('+column.max+')':'')+': '+(value||'–')); row.appendChild(score);
            });
            table.appendChild(row);
        });
        if (periods.length) {
            var total=document.createElement('div'); total.className='univer-app-score-total'; total.setAttribute('role','row');
            var name=document.createElement('span'); name.setAttribute('role','rowheader'); name.textContent=text().total; total.appendChild(name);
            columns.forEach(column=>{
                var found=periods.find(p=>periodKey(p.label)===column.key), value=found?.value||'';
                var said=text().total+', '+column.label+': '+(value||'–')+(found?.max?' / '+found.max:''), level=common.band(value,found?.max);
                var score=cell('univer-app-score');
                var number=document.createElement('b'); number.className='univer-app-score-value'; number.textContent=value||'–'; score.appendChild(number);
                var ratio=found?.max&&value?Number(value.replace(',','.'))/Number(found.max.replace(',','.')):NaN;
                if (isFinite(ratio)) { var bar=document.createElement('span'); bar.className='univer-app-score-bar'; bar.setAttribute('aria-hidden','true'); bar.style.setProperty('--univer-score',(Math.round(Math.max(0,Math.min(1,ratio))*1000)/10)+'%'); score.appendChild(bar); }
                // The course total out of 100 shows its band and what is left to the next mark.
                if (level) {
                    var note=common.bandNote(level,language), next=document.createElement('span'); next.className='univer-app-score-next'; next.setAttribute('aria-hidden','true'); next.textContent=note; score.appendChild(next);
                    // What is left is the part a parent looks for: it stands out; a reached goal or the top band is plain.
                    if (level.next) next.dataset.univerLeft='true';
                    score.dataset.univerBand=level.band; said+='. '+common.bandWords(language)[level.band]+(level.next||level.reached?', '+note:'');
                }
                score.setAttribute('aria-label',said);
                total.appendChild(score);
            });
            table.appendChild(total);
        }
        return table;
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
        // One walk of the page for every lesson's tables, not one per lesson.
        var inner=Array.from(root.querySelectorAll('table.inner')), groups=[], group=null;
        events.forEach((event,index)=>{
            if (event.tag==='course') {
                if (group) group.end=event.start;
                var source=headingText(event.cell);
                var name=source.match(/^(.*?)\s*(?:\(\[|\d{2}\.\d{2}\.\d{4})/)?.[1]?.trim()||source;
                group={heading:event.cell,start:event.start,end:null,token:source.normalize('NFC').toLocaleLowerCase(),name,lessons:[],overall:null}; groups.push(group);
            }
            else if (event.tag==='overall') { if (group) group.overall=event.cell; }
            else {
                // A single-group attendance/show page may omit the full-course heading.
                if (!group) { group={heading:null,lessons:[],overall:null}; groups.push(group); }
                event.end=events[index+1]?.start || null; group.lessons.push(event);
                event.tables=inner.filter(table=>before(event.start,table) && (!event.end || before(table,event.end)) && !table.contains(event.start));
            }
        });
        return groups.filter(group=>group.heading||group.lessons.some(lesson=>lesson.type));
    }
    function markSum(cell) { markCell(cell,'univer-app-sum'); }
    function markCell(cell,name) {
        if (!marked.has(cell)) marked.set(cell,!cell.hasAttribute('class'));
        cell.classList.add(name);
    }
    function clearTableMarks(table) {
        ownRows(table).forEach(row=>Array.from(row.cells).forEach(cell=>{
            if (!marked.has(cell)) return;
            cell.classList.remove('univer-app-sum','univer-app-absent','univer-app-excused','univer-app-empty-col');
        }));
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
        var sumCells=new Set();
        rows.forEach((row,r)=>{
            var last=grid[r]?.length-1, header=grid[r]?.[last];
            if (!header || header.tagName!=='TH' || !/^(сумма|sum|total|жиыны|барлығы)$/i.test(clean(header))) return;
            var period=clean(grid[r][0]); markSum(header); sumCells.add(header);
            for (var n=r+1;n<rows.length;n++) {
                var value=grid[n]?.[last];
                if (!value || value.tagName!=='TD') break;
                // A spanned value is a single server total, never count it twice.
                markSum(value); sumCells.add(value); if (!entries.some(e=>e.cell===value)) entries.push({period,value:clean(value),cell:value});
            }
        });
        // Columns without a date or a mark are hidden reversibly per RK section (header row(s) + following mark rows),
        // so an empty column in RK2 is hidden even when RK1 has a date at the same column index.
        var sections=[];
        for (var r=0;r<rows.length;r++) {
            var hasTh=Array.from(rows[r].cells).some(cell=>cell.tagName==='TH');
            var prevTd=r>0&&Array.from(rows[r-1].cells).some(cell=>cell.tagName==='TD');
            if (!sections.length || (hasTh && prevTd)) sections.push({start:r,end:r+1});
            else sections[sections.length-1].end=r+1;
        }
        sections.forEach(sec=>{
            var width=0;
            for (var y=sec.start;y<sec.end;y++) width=Math.max(width,grid[y]?.length||0);
            var blank=new Array(width).fill(false);
            for (var c=0;c<width;c++) {
                var seen=false, empty=true;
                for (var y=sec.start;y<sec.end;y++) {
                    var cell=grid[y]?.[c];
                    if (!cell) continue;
                    seen=true;
                    if (sumCells.has(cell) || clean(cell)) { empty=false; break; }
                }
                blank[c]=seen&&empty;
            }
            for (var c=0;c<width;c++) {
                if (!blank[c]) continue;
                var ok=true;
                for (var y=sec.start;y<sec.end;y++) {
                    var cell=grid[y]?.[c];
                    if (!cell) continue;
                    if (cell.colSpan>1) {
                        for (var k=0;k<width;k++) if (grid[y][k]===cell && !blank[k]) { ok=false; break; }
                        if (!ok) break;
                    }
                    if (cell.rowSpan>1) {
                        for (var ry=0;ry<grid.length;ry++) if ((ry<sec.start||ry>=sec.end)&&grid[ry]?.[c]===cell) { ok=false; break; }
                        if (!ok) break;
                    }
                }
                if (!ok) continue;
                for (var y=sec.start;y<sec.end;y++) { var cell=grid[y]?.[c]; if (cell) markCell(cell,'univer-app-empty-col'); }
            }
        });
        return entries;
    }
    // One read of a table's text stands for its totals until that text changes. Lengths plus the text fix each cell's
    // string, so a later pass can reuse the totals instead of walking the grid again.
    var tableFacts=new WeakMap();
    function factsFor(table) {
        var raw=table.textContent||'';
        var facts=tableFacts.get(table);
        if (!facts || facts.raw!==raw) { facts={raw:raw,text:raw.replace(/\s+/g,' ').trim(),snapshot:null,entries:null,absences:null}; tableFacts.set(table,facts); }
        return facts;
    }
    function snapshot(table) {
        var rows=ownRows(table), parts=[String(rows.length)];
        for (var r=0;r<rows.length;r++) {
            var cells=rows[r].cells; parts.push(String(cells.length));
            for (var c=0;c<cells.length;c++) { var cell=cells[c]; parts.push(cell.tagName,String(cell.rowSpan),String(cell.colSpan),String((cell.textContent||'').length)); }
        }
        return parts.join('\u0000');
    }
    function ensureTable(table) {
        var facts=factsFor(table), shot=snapshot(table);
        if (facts.entries && facts.snapshot===shot) return facts;
        clearTableMarks(table);
        facts.snapshot=shot; facts.entries=sums(table); facts.absences=absences(table); return facts;
    }
    function tableText(table) { return factsFor(table).text; }
    function tableSums(table) { return ensureTable(table).entries; }
    function tableAbsences(table) { return ensureTable(table).absences; }
    function setHidden(node,value) {
        if (!hidden.has(node)) hidden.set(node,{value:node.hidden,attribute:node.getAttribute('hidden'),classAbsent:!node.hasAttribute('class')});
        var next=hidden.get(node).value||value;
        if (node.hidden!==next) node.hidden=next;
        node.classList.add('univer-app-attendance-part');
    }
    // An open course's class log reads as one card with its course: the course row is the top, each class type's rows
    // follow (a type's first row carries its name and teacher), and the last row closes the card. Only the site's own
    // rows beside the course row are marked; release removes the marks.
    var journal=new Map(), closing=new Map(), shut=new Map(), pendingOpen=new Map(), rowAnims=new Map(), openSeq=0;
    // The height a closed card had (stretched to its neighbour): an open card keeps it, so its totals and footer stay put.
    var keptHeight=new Map(), heldStyle=new Map();
    function trackRowAnim(row,anim) {
        if (!anim) return;
        var set=rowAnims.get(row); if (!set) rowAnims.set(row,set=new Set());
        set.add(anim);
        var clear=()=>{ var cur=rowAnims.get(row); if (cur) { cur.delete(anim); if (!cur.size) rowAnims.delete(row); } };
        var prevFinish=anim.onfinish, prevCancel=anim.oncancel;
        anim.onfinish=function(){ clear(); prevFinish?.apply(this,arguments); };
        anim.oncancel=function(){ clear(); prevCancel?.apply(this,arguments); };
    }
    function cancelRowAnims(rows) {
        var list=rows||Array.from(rowAnims.keys());
        list.forEach(row=>{ var set=rowAnims.get(row); if (!set) return; rowAnims.delete(row); set.forEach(anim=>{ try { anim.cancel?.(); } catch (_) {} }); });
    }
    // A row about to grow, held shut by its own style (the row's style attribute as it was is kept and put back).
    function shutRow(row) { if (!shut.has(row)) shut.set(row,row.getAttribute('style')); row.style.setProperty('max-height','1px'); row.style.setProperty('margin-bottom','0px'); row.style.setProperty('visibility','hidden'); }
    function unshutRow(row) {
        if (!shut.has(row)) return;
        var was=shut.get(row); shut.delete(row);
        if (was===null) row.removeAttribute('style'); else row.setAttribute('style',was);
    }
    function markJournal(group,rows) {
        var top=group.start;
        if (!group.heading || top?.tagName!=='TR') return;
        var shown=rows.filter(row=>row.tagName==='TR' && row.parentElement===top.parentElement && !row.hidden && !row.querySelector(':scope>td.pd'));
        if (!shown.length) return;
        var starts=new Set(group.lessons.map(lesson=>lesson.start));
        [top].concat(shown).forEach(row=>{ row.setAttribute('data-univer-journal',row===top?'top':starts.has(row)?'head':'body'); journal.set(row,group.token); });
        shown[shown.length-1].setAttribute('data-univer-journal-end','');
        if (keptHeight.has(group.token)) { heldStyle.set(top,top.getAttribute('style')); top.style.setProperty('min-height',keptHeight.get(group.token)+'px'); }
    }
    function freeHeight(row) {
        if (!heldStyle.has(row)) return;
        var was=heldStyle.get(row); heldStyle.delete(row);
        if (was===null) row.removeAttribute('style'); else row.setAttribute('style',was);
    }
    function journalOf(token) { return Array.from(journal.keys()).filter(row=>journal.get(row)===token && row.getAttribute('data-univer-journal')!=='top'); }
    function unmarkJournal(token) {
        journal.forEach((owner,row)=>{ if (token===undefined || owner===token) { cancelRowAnims([row]); unshutRow(row); freeHeight(row); row.removeAttribute('data-univer-journal'); row.removeAttribute('data-univer-journal-end'); journal.delete(row); } });
    }
    // A fold still running is let go: its rows come back as they were, and its change is not made.
    function stopClosing() { pendingOpen.clear(); cancelRowAnims(); var runs=Array.from(closing.values()); closing.clear(); runs.forEach(rows=>rows.forEach(row=>window.__univerMotionUI?.stop(row))); }
    function resetVisibility() {
        unmarkJournal();
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
        var parts=clean(link).match(/^(.+?)\s+[-–—]\s+(.+)$/), title=document.createElement('strong'), type=kind(clean(link));
        if (type) record.caption.dataset.kind=type;
        if (!parts) {record.caption.textContent=clean(link);return;}
        title.textContent=text()[type+'Label'];record.caption.appendChild(title);
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
    function courseHeader(cell) { return Array.from(cell?.children||[]).find(node=>node.classList.contains('univer-app-course-heading'))||null; }
    // The heading text without the totals this module may have put into the same cell.
    function headingText(cell) {
        var source=cell.querySelector('.univer-app-course-source'); if (source) return clean(source);
        if (!cell.querySelector('.univer-app-attendance-summary')) return clean(cell);
        var copy=cell.cloneNode(true); copy.querySelectorAll('.univer-app-attendance-summary').forEach(node=>node.remove()); return clean(copy);
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
    function summary(group,open) {
        var box=document.createElement('section'); box.className='univer-app-attendance-summary';
        var body=document.createElement('div'); body.className='univer-app-summary-body';
        // A class-type filter is about the class log; the course total would only repeat what the filter hides.
        if (group.overall && selected!=='all') setHidden(boundary(group.overall),true);
        var periods=[];
        if (group.overall && selected==='all') {
            periods=scores(clean(group.overall));
            // The visible copy is passive text. Original restores the unchanged server nodes in their original place.
            setHidden(boundary(group.overall),true);
        }
        var types=selected==='all'?['lecture','seminar','sro']:[selected], any=false, rows=[];
        types.forEach(type=>{
            var lessons=group.lessons.filter(l=>l.type===type); if (!lessons.length) return;
            any=true;
            var values=new Map();
            lessons.forEach(lesson=>lesson.tables.forEach(table=>tableSums(table).forEach(entry=>{
                if (!values.has(entry.period)) values.set(entry.period,[]);
                values.get(entry.period).push(entry.value);
            })));
            var totals=new Map();
            values.forEach((items,period)=>{
                var numeric=items.every(value=>/^-?\d+(?:[.,]\d+)?$/.test(value));
                var precision=Math.max(...items.map(value=>(value.match(/[.,](\d+)$/)?.[1]||'').length)), scale=Math.pow(10,precision);
                numeric=numeric&&precision<=8&&items.every(value=>Math.abs(Number(value.replace(',','.')))*scale<Number.MAX_SAFE_INTEGER/items.length);
                var total=numeric?String(items.reduce((sum,v)=>sum+Math.round(Number(v.replace(',','.'))*scale),0)/scale):items.filter(Boolean).join(' + ');
                totals.set(periodKey(period),{period,value:total||''});
            });
            rows.push({type,values:totals});
        });
        var columns=periods.map(p=>({key:periodKey(p.label),label:p.label,max:p.max}));
        rows.forEach(row=>row.values.forEach((entry,key)=>{
            if (!columns.some(c=>c.key===key)) columns.push({key,label:key,max:entry.period.match(/\(\s*(\d+(?:[.,]\d+)?)\s*\)/)?.[1]||''});
        }));
        if (columns.length) body.appendChild(scoreTable(columns,rows,periods));
        else rows.forEach(item=>{
            var row=document.createElement('div'); row.className='univer-app-type-row'; row.dataset.kind=item.type;
            var label=document.createElement('span'); label.className='univer-app-type-label'; label.textContent=text()[item.type]; row.appendChild(label);
            var value=document.createElement('span'); value.className='univer-app-type-empty'; value.textContent=text().ungraded; row.appendChild(value);
            body.appendChild(row);
        });
        // Anything the server wrote that is not a recognised period stays readable as its own line.
        if (group.overall && selected==='all' && !periods.length) { var value=document.createElement('p');value.className='univer-app-attendance-overall-value';value.textContent=clean(group.overall);body.appendChild(value); }
        // Absences of the shown class types: hidden when both are zero; otherwise only non-zero kinds as tinted pills.
        var gone=types.map(type=>({type,count:missed(group,type)})).filter(item=>item.count>0);
        var away=selected==='all'?excused(group):types.reduce((n,type)=>n+excused(group,type),0);
        if (!any) { var empty=document.createElement('p'); empty.className='univer-app-attendance-nothing'; empty.textContent=selected==='all'?courseText().empty:text().empty; body.appendChild(empty); }
        var known=gone.reduce((n,item)=>n+item.count,0), sum=selected==='all'?missed(group):known;
        if (sum>0 || away>0) {
            var lost=document.createElement('p'), words=absenceText(); lost.className='univer-app-attendance-absences';
            var lostLabel=document.createElement('span'); lostLabel.className='univer-app-attendance-absences-label'; lostLabel.textContent=words.label;
            lost.appendChild(lostLabel);
            if (sum>0) {
                var unexcused=absencePair('unexcused',words.unexcused,sum);
                // Classes of no known type (labs) are in the total; the breakdown names them too, so its parts add up.
                if (sum>known&&gone.length) gone.push({type:'other',count:sum-known});
                if (gone.length>1) { var parts=document.createElement('small'); parts.textContent=gone.map(item=>words[item.type]+' '+item.count).join(', '); unexcused.appendChild(parts); }
                lost.appendChild(unexcused);
            }
            if (away>0) lost.appendChild(absencePair('excused',words.excused,away));
            body.appendChild(lost);
        }
        box.appendChild(body);
        if (!any && !group.overall) box.classList.add('univer-app-attendance-summary-empty');
        if (group.heading && group.lessons.length && selected==='all') {
            var toggle=document.createElement('button'); toggle.type='button'; toggle.className='univer-app-attendance-details-toggle';
            toggle.setAttribute('aria-expanded',String(open)); toggle.textContent=open?text().hideDetails:text().showDetails;
            // The card keeps its header, totals and footer in place while opening and closing. Opening widens the card and
            // slides its neighbours in the grid (with scroll compensation so the button stays put) while the journal rows
            // unfold below; closing folds the rows shut first, then narrows the card back into its column.
            toggle.addEventListener('click',event=>{
                var motion=window.__univerMotionUI, animate=event.detail>0, token=group.token, leaving=closing.get(token);
                var card=group.start?.tagName==='TR'?group.start:null, grid=card?.parentElement||null;
                var moving=animate&&!!motion&&window.__univerMotion!==false&&!motion.reduced?.();
                var place=(mutate,settings)=>{
                    if (moving&&grid&&motion.reflow) { var made; motion.reflow(grid,()=>{ made=mutate(); },Object.assign({anchor:toggle},settings)); return made; }
                    return compensate(toggle,mutate);
                };
                if (leaving) {
                    closing.delete(token); expanded.set(token,true); toggle.setAttribute('aria-expanded','true'); toggle.textContent=text().hideDetails;
                    var heights=moving?leaving.map(row=>row.getBoundingClientRect().height):null;
                    cancelRowAnims(leaving);
                    leaving.forEach(row=>motion?.stop(row));
                    if (moving && heights) {
                        var full=leaving.map(row=>row.getBoundingClientRect().height);
                        var leftDist=full.reduce((sum,h,i)=>sum+Math.max(0,h-heights[i]),0);
                        leaving.forEach((row,i)=>motion.resize(row,heights[i],{distance:leftDist}));
                    }
                    return;
                }
                var nextOpen=box.dataset.open!=='true'; expanded.set(token,nextOpen);
                var tall=nextOpen&&card?(card.querySelector(':scope>td.ct')||card).getBoundingClientRect().height:0;
                if (tall>0) keptHeight.set(token,tall); else keptHeight.delete(token);
                var change=()=>{
                    unmarkJournal(token);
                    var excludeCourse=selectedCourse!=='all'&&selectedCourse!==token, shownRows=[];
                    group.lessons.forEach(lesson=>{
                        var exclude=excludeCourse||!nextOpen||(selected!=='all'&&selected!==lesson.type);
                        hideRange(lesson.start,lesson.end,exclude,null,exclude?null:shownRows);
                    });
                    if (nextOpen&&!excludeCourse) markJournal(group,shownRows);
                    box.dataset.open=String(nextOpen);
                    toggle.setAttribute('aria-expanded',String(nextOpen));
                    toggle.textContent=nextOpen?text().hideDetails:text().showDetails;
                    if (toggle.focus) toggle.focus({preventScroll:true});
                    return box;
                };
                if (!nextOpen) {
                    pendingOpen.delete(token);
                    var shown=journalOf(token).filter(row=>!row.hidden), left=shown.length;
                    cancelRowAnims(shown);
                    toggle.setAttribute('aria-expanded','false');
                    toggle.textContent=text().showDetails;
                    if (!animate || !left || !motion?.collapse || shown.every(row=>shut.has(row))) { place(change); return; }
                    closing.set(token,shown);
                    // The rows fold at one pace, set by the whole log's height (the shared fold pace).
                    var distance=shown.reduce((sum,row)=>sum+row.getBoundingClientRect().height,0);
                    shown.forEach(row=>motion.collapse(row,()=>{ if (--left===0 && closing.get(token)===shown) { closing.delete(token); place(change); } },{distance}));
                    return;
                }
                // The rows stay shut while the grid slides (so the courses below are not moved twice) and grow in the next
                // frame after the layout adapter's pass, so the height they grow to is final and matches the card's width.
                // Capture afterCard inside the layout mutation before reflow installs its backwards-filled width animation.
                var beforeCard=moving&&card?card.getBoundingClientRect():null, afterCard=null;
                var scrollBefore=window.scrollY||window.pageYOffset||0;
                var ease=motion?.easing||'cubic-bezier(.45,0,.25,1)', widthDur=300;
                place(()=>{
                    change();
                    if (moving) {
                        journalOf(token).forEach(shutRow);
                        if (card) afterCard=card.getBoundingClientRect();
                    }
                },{duration:widthDur,easing:ease});
                var scrollAfter=window.scrollY||window.pageYOffset||0;
                var cardAnim=moving&&card?.getAnimations?card.getAnimations().slice(-1)[0]:null;
                if (moving) {
                    var nonce=++openSeq; pendingOpen.set(token,nonce);
                    Promise.resolve().then(()=>(window.requestAnimationFrame||setTimeout)(()=>{
                        if (pendingOpen.get(token)!==nonce || !expanded.get(token) || closing.has(token)) return;
                        pendingOpen.delete(token);
                        var grow=journalOf(token).filter(row=>shut.has(row)); grow.forEach(unshutRow);
                        var distance=grow.reduce((sum,row)=>sum+row.getBoundingClientRect().height,0);
                        var dur=motion.span?motion.span(distance):240;
                        var widened=beforeCard&&afterCard&&beforeCard.width>0&&afterCard.width-beforeCard.width>=1;
                        var dx=widened?beforeCard.left-afterCard.left:0;
                        var bottomOf=r=>typeof r.bottom==='number'?r.bottom:r.top+r.height;
                        var dy=widened?bottomOf(beforeCard)-(bottomOf(afterCard)-(scrollAfter-scrollBefore)):0;
                        grow.forEach(row=>{
                            motion.resize(row,1,{distance});
                            if (widened && row.animate) {
                                var shift=Math.abs(dx)>=1||Math.abs(dy)>=1?(Math.abs(dy)>=1?'translate('+dx+'px,'+dy+'px)':'translateX('+dx+'px)'):'none';
                                var from={maxWidth:beforeCard.width+'px',transform:shift,opacity:0.2}, to={maxWidth:afterCard.width+'px',transform:'none',opacity:1};
                                var anim=row.animate([from,to],{duration:widthDur,easing:ease,fill:'backwards'});
                                if (anim && cardAnim && typeof cardAnim.startTime==='number') { try { anim.startTime=cardAnim.startTime; } catch (_) {} }
                                trackRowAnim(row,anim);
                            }
                            else motion.reveal(row,'content');
                            var room=row.hasAttribute('data-univer-journal-end')&&row.animate?getComputedStyle(row).marginBottom:'';
                            if (parseFloat(room)>0) trackRowAnim(row,row.animate([{marginBottom:'0px'},{marginBottom:room}],{duration:dur,easing:ease}));
                        });
                    }));
                }
                else if (animate) {
                    journalOf(token).forEach(row=>{ motion?.resize(row,1); motion?.reveal(row,'content'); });
                }
            });
            box.appendChild(toggle);
            if (focusToggle===group.token) { focusToggle=null; box.dataset.univerFocus='true'; }
        }
        // A closed course is a card: the totals sit inside its heading cell, so the cards can stand side by side.
        // Without the reading heading the totals stay in their own row, as before.
        box.dataset.open=String(open);
        var header=courseHeader(group.heading);
        if (header) { header.after(box); owned.push(box); return box; }
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
            if (periods.length) row.appendChild(gradeChips(periods));
            else { var none=document.createElement('span'); none.className='univer-app-attendance-overview-empty'; none.textContent=group.overall?clean(group.overall):text().ungraded; row.appendChild(none); }
            rows.appendChild(row);
        });
        list.append(title,rows);
    }
    // Every course as the grade summary (grade-report.js) shows it: its name and the server's control periods, or the
    // server's own words when it wrote no period.
    function reportCourses(groups) {
        return groups.filter(group=>group.heading).map(group=>{
            var periods=group.overall?scores(clean(group.overall)):[];
            return {name:group.name,periods:periods.map(p=>({label:p.label,max:p.max,value:p.value})),note:!periods.length&&group.overall?clean(group.overall):'',absences:missed(group),excused:excused(group)};
        });
    }
    function reportText() { return language==='en'?'Print and PDF':language==='kz'?'Басып шығару және PDF':'Печать и PDF'; }
    function chooseCourse(token,event) {
        var from=document.activeElement;
        selectedCourse=token;render(model());
        // The pressed overview row is rebuilt; keyboard focus moves to the same course in the course list.
        if(from&&from!==document.body&&!from.isConnected){var target=token==='all'?state.allCourse:state.courseButtons.get(token);target?.focus?.({preventScroll:true});}
        if(event?.detail>0) owned.filter(node=>!node.closest('[hidden]')).forEach(node=>window.__univerMotionUI?.reveal(node,'forward'));
        if (state.host.getBoundingClientRect().top<0) state.host.scrollIntoView?.({block:'start'});
    }
    function courseControls(groups) {
        var courses=groups.filter(group=>group.heading);
        if(selectedCourse!=='all'&&!courses.some(group=>group.token===selectedCourse)){selectedCourse='all';}
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
    // The next element after `node` in document order, skipping the descendants just hidden with it.
    function nextAfter(node,root) {
        while (node && node!==root) { if (node.nextElementSibling) return node.nextElementSibling; node=node.parentElement; }
        return null;
    }
    function follows(node,bound) { return !!(node.compareDocumentPosition(bound)&Node.DOCUMENT_POSITION_FOLLOWING); }
    function discard(node) {
        if (!node) return;
        window.__univerMotionUI?.stop(node);
        var index=owned.indexOf(node); if (index>=0) owned.splice(index,1);
        node.remove();
    }
    // Hide the blocks that sit wholly between two boundaries: the lesson's own rows, or, on a page with no rows,
    // the element and its following siblings. One position check up front; the walk then stops when it meets the bound.
    function hideRange(start,end,exclude,last,shown) {
        if (!start) return;
        var root=document.querySelector('#l4');
        if (!root || !root.contains(start)) return;
        if (end && end!==start && !start.contains(end) && !follows(start,end)) return;
        if (last && last!==start && !start.contains(last) && !last.contains(start) && !follows(start,last)) return;
        var node=start;
        while (node && node!==root && root.contains(node)) {
            if (node===end) return;
            if (end && node.contains(end)) { node=node.firstElementChild; if (!node) return; continue; }
            if (last && node!==last && node.contains(last)) { node=node.firstElementChild; if (!node) return; continue; }
            setHidden(node,exclude); if (shown) shown.push(node);
            if (node===last) return;
            node=nextAfter(node,root);
        }
    }
    function focusSummary() {
        var root=document.querySelector('#l4'); if (!root) return;
        var focused=root.querySelector('.univer-app-attendance-summary[data-univer-focus] .univer-app-attendance-details-toggle');
        if (!focused) return;
        focused.closest('.univer-app-attendance-summary').removeAttribute('data-univer-focus');
        if (focused.focus) focused.focus({preventScroll:true});
    }
    function render(groups) {
        stopClosing(); resetVisibility(); owned.forEach(node=>{window.__univerMotionUI?.stop(node);node.remove();}); owned=[];
        courseControls(groups);
        groups.forEach(group=>group.lessons.forEach(lesson=>{if(lesson.type)makeLabel(lesson.link);}));
        var root=document.querySelector('#l4');
        // With all courses listed, each course starts as its summary; one chosen course shows its class log.
        var single=selectedCourse!=='all'||groups.filter(group=>group.heading).length<2;
        groups.forEach(group=>{
            var excludeCourse=selectedCourse!=='all'&&selectedCourse!==group.token;
            // Choosing lectures, seminars or independent work opens every course's log of that type.
            var open=!group.heading||selected!=='all'||(expanded.has(group.token)?expanded.get(group.token):single);
            var rows=[];
            group.lessons.forEach(lesson=>{
                var exclude=excludeCourse||!open||(selected!=='all'&&selected!==lesson.type);
                hideRange(lesson.start,lesson.end,exclude,null,exclude?null:rows);
            });
            if(excludeCourse&&group.heading)hideRange(group.start,group.end,true,!group.end&&group.overall?boundary(group.overall):null);
            else if(open)markJournal(group,rows);
            var host=summary(group,open);if(excludeCourse)setHidden(host,true);
        });
        overview(groups);
        // The summary is offered once a course has a grade; an open one follows new numbers and the language.
        var report=reportCourses(groups);
        // Only where the app takes the summary's buttons (SitePolicy.Grades: numeric parts after full/show).
        state.report.hidden=!REPORT_PAGE.test(location.pathname)||!report.some(course=>course.periods.length);
        state.reportLabel.textContent=reportText();
        // The goal is offered with the same numbers the summary needs; it speaks the page's language.
        if (state.goalLanguage!==language) { var picker=common.goalPicker(language); state.goal.replaceWith(picker); state.goal=picker; state.goalLanguage=language; }
        common.goalPressed(state.goal);
        window.__univerReportV1?.update(report,language);
        state.bar.querySelectorAll('button').forEach(button=>{button.textContent=text()[button.dataset.kind];button.setAttribute('aria-pressed',String(selected===button.dataset.kind));});
        state.bar.setAttribute('aria-label',text().filter);
        // The note explains a type filter, so it appears only while one is active.
        state.caption.textContent=text().filter;state.note.textContent=text().note;state.note.hidden=selected==='all';
        focusSummary();
    }
    var REPORT_PAGE=/^\/student\/attendance\/(full|show)(\/\d+)*\/?$/;
    function apply(lang) {
        language=lang;
        if (document.readyState==='loading') return;
        if (!/^\/student\/attendance\/(full|show)(\/|$)/.test(location.pathname)) return;
        var groups=model(); if (!groups.length) { if(state)release(); return; }
        if(state&&!state.host.isConnected){release();groups=model();}
        var signature=language+'|'+groups.map(g=>id(g.heading||g.lessons[0].cell)+':'+(courseHeader(g.heading)?'h':'')+(g.token||'')+g.lessons.map(l=>id(l.link)+clean(l.link)+l.tables.map(t=>id(t)+tableText(t)).join('|')).join('/')+(g.overall?clean(g.overall): '')).join(';');
        if (!state) {
            var bar=document.createElement('div'); bar.className='univer-app-attendance-filters'; bar.setAttribute('role','group');
            var caption=document.createElement('p');caption.className='univer-app-attendance-filter-caption';
            ['all','lecture','seminar','sro'].forEach(type=>{
                var button=document.createElement('button'); button.type='button'; button.dataset.kind=type;
                button.addEventListener('click',event=>{selected=type;render(model());if(event.detail>0)owned.filter(node=>!node.closest('[hidden]')).forEach(node=>window.__univerMotionUI?.reveal(node,'content'));}); bar.appendChild(button);
            });
            var tools=document.createElement('div');tools.className='univer-app-attendance-tools';
            var limit=document.createElement('p');limit.className='univer-app-attendance-limit';limit.dataset.univerBand='low';limit.setAttribute('role','status');limit.hidden=true;
            var term=document.createElement('p');term.className='univer-app-attendance-term';term.hidden=true;
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
            var summaryList=document.createElement('section');summaryList.className='univer-app-attendance-overview';
            var actions=document.createElement('div');actions.className='univer-app-attendance-actions';
            var report=document.createElement('button');report.type='button';report.className='univer-app-attendance-report';report.setAttribute('aria-haspopup','dialog');
            // The printer icon, node by node: the stores' checks refuse HTML text written into the page.
            var svgNs='http://www.w3.org/2000/svg',printIcon=document.createElementNS(svgNs,'svg');printIcon.setAttribute('viewBox','0 0 24 24');printIcon.setAttribute('aria-hidden','true');
            [['path',{d:'M6 9V3h12v6'}],['rect',{x:'3',y:'9',width:'18',height:'8',rx:'2'}],['path',{d:'M7 14h10v7H7z'}]].forEach(part=>{
                var shape=document.createElementNS(svgNs,part[0]);Object.keys(part[1]).forEach(key=>shape.setAttribute(key,part[1][key]));printIcon.appendChild(shape);
            });
            report.append(printIcon,document.createElement('span'));
            var reportLabel=report.lastChild;
            report.addEventListener('click',()=>window.__univerReportV1?.open(reportCourses(model()),language,report));
            var goalBox=common.goalPicker(language);
            actions.append(goalBox,report);
            // The class-type switch and the summary's button share one row: filters on the left, the action on the right.
            var filterRow=document.createElement('div');filterRow.className='univer-app-attendance-filter-row';filterRow.append(bar,actions);
            tools.append(term,limit,courses,caption,filterRow,note,summaryList);
            insertAfterCell(groups[0].heading||groups[0].lessons[0].cell,tools,true);
            // The filter bar has its own lifetime; summary rebuilds must not remove it.
            var host=owned.pop(); state={bar,host,limit,term,caption,note,courses,courseCaption,courseList,allCourse,courseButtons:new Map(),overview:summaryList,report:actions,reportLabel,goal:goalBox,goalLanguage:language,signature:null};
        }
        if (state.signature!==signature) { state.signature=signature; render(groups); showLimit(groups); }
    }
    function release() {
        tableFacts=new WeakMap();
        window.__univerReportV1?.close();
        keptHeight.clear();
        marked.forEach((absent,cell)=>{cell.classList.remove('univer-app-sum','univer-app-absent','univer-app-excused','univer-app-empty-col');if(absent&&!cell.className)cell.removeAttribute('class');}); marked.clear();
        stopClosing(); resetVisibility(); owned.forEach(node=>{window.__univerMotionUI?.stop(node);node.remove();}); owned=[];
        labels.forEach((record,link)=>{record.caption.remove();record.attrs.forEach(a=>{if(a[1]===null)link.removeAttribute(a[0]);else link.setAttribute(a[0],a[1]);});}); labels.clear();
        state?.host.remove(); state=null;
    }
    // Another goal: the totals and an open summary count what is left to it.
    common.onGoal(()=>{ if (state&&state.host.isConnected) render(model()); });
    window.__univerAttendanceV1={apply,release};
})();
