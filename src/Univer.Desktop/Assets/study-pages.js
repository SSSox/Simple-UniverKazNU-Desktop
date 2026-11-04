/* Layouts for observed study pages (attestation, distance courses, debts, FX, GPA increase, exam schedules). Only
   classes, data attributes and small added controls; the site's nodes and handlers stay, and release() restores them. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerPagesV1) return;
    var common = window.__univerCommonV1, clean = common.clean, fold = common.fold, ownRows = common.ownRows;
    var enabled = false, language = 'ru', style = null;
    var marked = new Set(), roles = new Set(), wrapped = [], splits = [], buttons = [], filters = new Map(), hiddenRows = new Map(), memoButtons = new Map();
    // A course cell's own text, without the Open button the adapters add to it.
    function label(cell) { return Array.from(cell.childNodes).filter(function (n) { return !n.classList?.contains('univer-app-course-open') && !n.classList?.contains('univer-app-course-tasks'); }).map(function (n) { return n.textContent; }).join('').replace(/\s+/g, ' ').trim(); }
    function text() {
        return language === 'en' ? {open:'Open', filter:'Find a course', none:'No courses match the search', openCourse:'Open course: ', more:'Show in full', less:'Collapse'} :
            language === 'kz' ? {open:'Ашу', filter:'Пәнді іздеу', none:'Іздеуге сәйкес пән жоқ', openCourse:'Пәнді ашу: ', more:'Толығымен көрсету', less:'Жию'} :
            {open:'Открыть', filter:'Найти дисциплину', none:'Дисциплины по запросу не найдены', openCourse:'Открыть дисциплину: ', more:'Показать полностью', less:'Свернуть'};
    }
    function mark(node, name) { if (!node.classList.contains(name)) node.classList.add(name); marked.add(node); }
    function role(cell, value) { if (cell.dataset.univerRole !== value) cell.dataset.univerRole = value; roles.add(cell); }
    // Grade tables: the column headings name what each cell holds.
    var columns = [['name', /дисципл|пән|discipline|course|предмет/i], ['letter', /^(букв|әріп|letter)/i], ['credit', /^(кред|credit)/i],
        ['score', /^(сумма|балл|итог|жалпы|total|score)/i], ['gpa', /^gpa\b/i], ['mark', /^(рк|ра|па|аб|бк|экз|емт|exam|mid|rc|fc)\s*\d?\s*\*?$/i],
        ['word', /^(значение|мағына|meaning|traditional|оценка|баға)/i]];
    function band(letter) { var value = letter.toUpperCase(); return /^A[+-]?$/.test(value) ? 'a' : /^B[+-]?$/.test(value) ? 'b' : /^C[+-]?$/.test(value) ? 'c' : /^D[+-]?$/.test(value) ? 'd' : /^FX?$/.test(value) ? 'f' : ''; }
    // A badge wraps the cell's single text node; release() puts the node back.
    function badge(cell, value) {
        var existing = cell.querySelector(':scope>.univer-app-grade');
        if (existing) { if (existing.dataset.band !== value) existing.dataset.band = value; return; }
        if (cell.childNodes.length !== 1 || cell.firstChild.nodeType !== 3) return;
        var span = document.createElement('span'); span.className = 'univer-app-grade'; span.dataset.band = value;
        cell.insertBefore(span, cell.firstChild); span.appendChild(span.nextSibling); wrapped.push(span);
    }
    // Card tables, and grade matrices on the attestation page; the total row keeps the adapter's layout. A mid-semester F
    // is only interim, so failed letters get a badge colour but their rows are not tinted.
    function grades() {
        document.querySelectorAll('#l4 table:is(.univer-app-cards,.univer-app-matrix)').forEach(function (table) {
            var rows = ownRows(table), header = rows.find(function (r) { return r.cells.length > 1 && Array.from(r.cells).every(function (c) { return c.tagName === 'TH' && c.colSpan === 1; }); });
            if (!header) return;
            var kinds = Array.from(header.cells).map(function (cell) { var label = clean(cell), found = columns.find(function (c) { return c[1].test(label); }); return found ? found[0] : ''; });
            if (kinds.indexOf('letter') < 0) return;
            mark(table, 'univer-app-grades');
            Array.from(header.cells).forEach(function (cell, i) { if (kinds[i]) role(cell, kinds[i]); });
            rows.forEach(function (row) {
                if (row === header || row.cells.length !== header.cells.length || Array.from(row.cells).some(function (c) { return c.tagName !== 'TD' || c.colSpan !== 1; })) return;
                Array.from(row.cells).forEach(function (cell, i) {
                    if (!kinds[i]) return;
                    role(cell, kinds[i]);
                    if (kinds[i] !== 'letter') return;
                    var value = band(clean(cell)); if (value) badge(cell, value);
                });
            });
        });
    }
    // "Примечание: …" is set in large red bold on the site; it reads as a short note box instead.
    function notes() {
        document.querySelectorAll('#l4 td.ct').forEach(function (cell) {
            var value = clean(cell);
            if (value.length < 900 && /^(примечание|ескерту|ескертпе|note)\s*:/i.test(value) && !cell.querySelector('table,input,select,textarea')) mark(cell, 'univer-app-callout');
        });
    }
    // A heading cell across the whole table (for example "Заявки на повторное изучение дисциплин") stays visible
    // when the table is empty, so each "Нет данных" says what is missing.
    function captions() {
        document.querySelectorAll('#l4 table.univer-app-data').forEach(function (table) {
            var rows = ownRows(table), first = rows[0];
            if (!first || first.cells.length !== 1 || first.cells[0].tagName !== 'TH' || first.cells[0].colSpan < 2 || !clean(first)) return;
            if (rows.slice(1).some(function (r) { return r.cells.length > 1 && Array.from(r.cells).every(function (c) { return c.tagName === 'TH'; }); })) mark(first, 'univer-app-table-caption');
        });
    }
    // Instruction files (links with a file icon) become document cards. A cell of nothing but such links lays them out
    // as a grid; next to other text (the exam page's proctoring lines) each file is a card of its own.
    function documents() {
        document.querySelectorAll('#l4 td.ct').forEach(function (cell) {
            if (cell.matches('.univer-app-doc-links,.univer-app-doc-cell') || cell.querySelector('table,input,select,textarea')) return;
            var all = Array.from(cell.querySelectorAll('a[href]')).filter(function (a) { return clean(a); });
            var files = all.filter(function (a) { return a.querySelector(':scope>img'); });
            if (files.length < 2) return;
            files.forEach(function (a) { mark(a, 'univer-app-doc-link'); });
            var outside = clean(cell).length - files.reduce(function (n, a) { return n + clean(a).length; }, 0);
            mark(cell, files.length === all.length && outside <= 24 ? 'univer-app-doc-links' : 'univer-app-doc-cell');
        });
    }
    function acts(link) {
        if (link.hasAttribute('href') || link.hasAttribute('onclick') || typeof link.onclick === 'function') return true;
        try { var jq = window.jQuery, data = jq && (jq._data ? jq._data(link, 'events') : jq.data(link, 'events')); return !!(data && data.click); } catch (_) { return false; }
    }
    // "Назад / Список дисциплин …": a back link that does something becomes a small button above the heading. On the
    // live page both links share one span; one without any action is left as the site shows it, not a dead button.
    function crumbs() {
        document.querySelectorAll('#l4 tr.top>td.ct').forEach(function (cell) {
            var back = cell.querySelector(':scope>span:first-child>a:first-child,:scope>a:first-child');
            if (!back || !/^(назад|артқа|кері|back)$/i.test(clean(back)) || clean(cell).length <= clean(back).length || !acts(back)) return;
            mark(cell, 'univer-app-back-crumb'); mark(back, 'univer-app-back-link');
            if (back.parentElement !== cell) mark(back.parentElement, 'univer-app-back-row');
        });
    }
    // A long memo ("ВНИМАНИЕ! ПАМЯТКА …" on the debts page) pushes the tables two screens down: it opens folded to
    // its first lines, with a button to read it in full.
    function memos() {
        document.querySelectorAll('#l4 td.ct').forEach(function (cell) {
            var record = memoButtons.get(cell);
            if (!record) {
                if (cell.matches('.univer-app-callout,.univer-app-doc-links,.univer-app-doc-cell,.message,.warning') || cell.querySelector('table,input,select,textarea') ||
                    clean(cell).length < 700 || !/^(внимание|назар|attention|памятка|жадынама)/i.test(clean(cell))) return;
                var button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-memo-toggle';
                record = {button:button, label:function () {
                    var folded = cell.dataset.univerFolded === 'true';
                    if (button.textContent !== (folded ? text().more : text().less)) button.textContent = folded ? text().more : text().less;
                    button.setAttribute('aria-expanded', String(!folded));
                }};
                button.addEventListener('click', function () {
                    var folding = cell.dataset.univerFolded !== 'true'; cell.dataset.univerFolded = String(folding); record.label();
                    if (folding) cell.scrollIntoView({block:'nearest'});
                });
                memoButtons.set(cell, record); mark(cell, 'univer-app-memo'); cell.dataset.univerFolded = 'true'; cell.appendChild(button);
            }
            record.label();
        });
    }
    // A lone "2026-2027 год Осенний Семестр" line above a list reads as the list's caption.
    function periods() {
        document.querySelectorAll('#l4 tr:not(.top)>td.ct').forEach(function (cell) {
            var value = clean(cell);
            if (value.length <= 60 && /^\d{4}\s*-\s*\d{4}\s+(год|жыл|year)/i.test(value) && !cell.querySelector('a,input,img,table')) mark(cell, 'univer-app-period');
        });
    }
    // Distance courses: rows like UMKD's (tr.link[id], opened by the site's double-click handler) get an Open button,
    // and the "(N)" discussion count becomes a badge.
    function count(cell) {
        if (cell.querySelector('.univer-app-count-box')) return;
        var node = Array.from(cell.childNodes).reverse().find(function (n) { return n.nodeType === 3 && n.data.trim(); });
        var match = node && /^([\s\S]*?\S)(\s*)\((\d+)\)\s*$/.exec(node.data);
        if (!match) return;
        // "(N)" goes into a box that hides the brackets; the number itself becomes the badge.
        var tail = node.splitText(match[1].length + match[2].length), digits = tail.splitText(1); digits.splitText(match[3].length);
        var box = document.createElement('span'); box.className = 'univer-app-count-box';
        var span = document.createElement('span'); span.className = 'univer-app-count'; span.dataset.zero = match[3] === '0' ? 'true' : 'false';
        tail.parentNode.insertBefore(box, tail); box.append(tail, digits, digits.nextSibling);
        box.insertBefore(span, digits); span.appendChild(digits); splits.push({box:box, span:span, node:node});
    }
    function courses() {
        // UMKD's course list (its rows and Open buttons come from univer-layout.js) gets the same search.
        document.querySelectorAll('#l4 table.univer-app-courses').forEach(function (table) { filter(table, ownRows(table).filter(function (r) { return r.matches('tr.link[id]'); })); });
        if (!/^\/student\/discussion\/index(\/|$)/i.test(location.pathname)) return;
        document.querySelectorAll('#l4 table.univer-app-cards').forEach(function (table) {
            var rows = ownRows(table), header = rows.find(function (r) { return r.classList.contains('univer-app-card-header'); });
            var links = rows.filter(function (r) { return r.matches('tr.link[id]') && r.cells.length === 2; });
            if (!header || header.cells.length !== 2 || clean(header.cells[0]) || !links.length) return;
            mark(table, 'univer-app-forum-courses');
            links.forEach(function (row) {
                var cell = row.cells[1]; count(cell);
                var button = cell.querySelector(':scope>.univer-app-course-open');
                if (!button) {
                    button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-course-open';
                    button.addEventListener('click', function (event) {
                        event.preventDefault(); event.stopPropagation();
                        row.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true, view:window}));
                    });
                    cell.appendChild(button); buttons.push(button);
                }
                if (button.textContent !== text().open) button.textContent = text().open;
                var name = text().openCourse + label(cell);
                if (button.getAttribute('aria-label') !== name) button.setAttribute('aria-label', name);
            });
            filter(table, links);
        });
    }
    // A search field over a long course list; it hides rows that do not match and says when none do.
    function filter(table, rows) {
        if (rows.length < 6) return;
        var record = filters.get(table);
        if (!record) {
            var anchor = table.closest('.univer-app-table-scroll') || table;
            var box = document.createElement('div'); box.className = 'univer-app-list-filter';
            var input = document.createElement('input'); input.type = 'search'; input.autocomplete = 'off'; input.spellcheck = false;
            var empty = document.createElement('p'); empty.className = 'univer-app-list-empty'; empty.hidden = true; empty.setAttribute('role', 'status');
            box.append(input, empty); anchor.before(box);
            record = {box:box, input:input, empty:empty, rows:rows};
            input.addEventListener('input', function () { run(record); });
            filters.set(table, record);
        }
        record.rows = rows;
        if (record.input.placeholder !== text().filter) { record.input.placeholder = text().filter; record.input.setAttribute('aria-label', text().filter); }
        if (record.empty.textContent !== text().none) record.empty.textContent = text().none;
        if (record.input.value) run(record);
    }
    function run(record) {
        var query = fold(record.input.value.trim()), shown = 0;
        record.rows.forEach(function (row) {
            if (!hiddenRows.has(row)) hiddenRows.set(row, row.hidden);
            var name = fold(label(row.cells[row.cells.length > 2 ? 1 : row.cells.length - 1]));
            var hide = hiddenRows.get(row) || (!!query && !name.includes(query));
            if (row.hidden !== hide) row.hidden = hide;
            if (!hide) shown++;
        });
        var none = !!query && !shown;
        if (record.empty.hidden !== !none) record.empty.hidden = !none;
    }
    function icon(svg, color) { return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='" + (color || 'black') + "' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'>" + svg + '</svg>') + '")'; }
    function css() {
        var doc = icon("<path d='M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z'/><path d='M14 3v5h5M9 13h6M9 17h4'/>", '#1b5fa6');
        var note = icon("<circle cx='12' cy='12' r='9'/><path d='M12 7.5v5.5M12 16.5v.5'/>");
        var inbox = icon("<path d='M4 13h4l2 3h4l2-3h4'/><path d='M6 5h12l2 8v6H4v-6z'/>");
        var search = icon("<circle cx='11' cy='11' r='6.5'/><path d='M16 16l4 4'/>");
        return `
/* Grade tables: numbers line up and the letter grade is a coloured badge. */
:root.univer-mobile #l4 .univer-app-grades :is(td,th):is([data-univer-role=credit],[data-univer-role=score],[data-univer-role=gpa],[data-univer-role=mark],[data-univer-role=letter]) { font-variant-numeric:tabular-nums; }
:root.univer-mobile #l4 .univer-app-grade { display:inline-flex; align-items:center; justify-content:center; box-sizing:border-box; min-width:36px; height:24px; padding:0 8px; border-radius:999px; font-size:13px!important; font-weight:700!important; line-height:1!important; letter-spacing:.02em; }
:root.univer-mobile #l4 .univer-app-grade[data-band=a] { background:#e3f3ef; color:#0f6b63!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=b] { background:#e9f1fb; color:#1b5fa6!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=c] { background:#fdf3dd; color:#80540a!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=d] { background:#fdeee3; color:#a3440e!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=f] { background:#fdeceb; color:#b42318!important; }
@media (min-width:900px) {
 :root.univer-mobile #l4 .univer-app-grades tr>:is(td,th):is([data-univer-role=credit],[data-univer-role=score],[data-univer-role=gpa],[data-univer-role=mark],[data-univer-role=letter]) { width:1%; white-space:nowrap!important; text-align:center!important; padding-left:14px!important; padding-right:14px!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=name] { color:var(--u-ink)!important; font-weight:600!important; text-align:start!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=score] { color:var(--u-ink)!important; font-weight:600!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=mark] { color:var(--u-text)!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=word] { color:var(--u-muted)!important; font-weight:500!important; }
}
/* Narrow windows: one compact card per course, the name first and the figures in one wrapped line. */
@media (max-width:899px) {
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row { display:flex!important; flex-wrap:wrap; align-items:center; gap:8px 18px; padding:14px 16px!important; margin:8px 0!important; }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td { display:inline-flex!important; align-items:center; gap:6px; grid-column:auto!important; font-weight:600; color:var(--u-ink); }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td[data-univer-role=name] { flex:1 0 100%; font-size:15px!important; }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td[data-univer-role=word] { font-weight:400; color:var(--u-muted); }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td[data-univer-field]::before { display:inline!important; margin:0!important; font-size:12px!important; font-weight:400!important; color:var(--u-muted)!important; }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td:is([data-univer-role=name],[data-univer-role=word],[data-univer-role=letter])::before { display:none!important; }
}
/* Notes: a short box with an icon instead of large red bold text. */
:root.univer-mobile #l4 td.ct.univer-app-callout { display:block!important; position:relative; box-sizing:border-box; margin:8px 0 16px!important; padding:12px 16px 12px 46px!important; border:1px solid #f1d6a6!important; border-radius:12px!important; background:#fff8ea!important; color:#5c3d0a!important; font-size:14px!important; font-weight:400!important; line-height:1.55!important; }
:root.univer-mobile #l4 td.ct.univer-app-callout::before { content:''; position:absolute; left:16px; top:14px; width:18px; height:18px; background:#b26a00; -webkit-mask:${note} center/contain no-repeat; }
:root.univer-mobile #l4 td.ct.univer-app-callout :is(b,strong,font,span,p) { color:inherit!important; font-size:inherit!important; font-weight:400!important; }
:root.univer-mobile #l4 td.ct.univer-app-callout>:is(b,strong):first-child, :root.univer-mobile #l4 td.ct.univer-app-callout>:is(b,strong):first-child * { font-weight:700!important; }
:root.univer-mobile #l4 td.ct.univer-app-callout br+br, :root.univer-mobile #l4 td.ct.univer-app-callout>br:first-child { display:none; }
/* A long memo opens folded to its first lines, in a box, with a button to read it in full. */
:root.univer-mobile #l4 td.ct.univer-app-memo { display:block!important; position:relative; box-sizing:border-box; margin:4px 0 16px!important; padding:14px 18px 56px!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:#fff!important; font-size:14px!important; line-height:1.55!important; overflow:hidden; }
:root.univer-mobile #l4 td.ct.univer-app-memo :is(b,strong,span,p,font,div) { font-size:inherit!important; line-height:inherit!important; }
:root.univer-mobile #l4 td.ct.univer-app-memo[data-univer-folded=true] { max-height:15.5em; }
/* The folded text fades out above a solid band that holds the button, so no line runs under it. */
:root.univer-mobile #l4 td.ct.univer-app-memo[data-univer-folded=true]::after { content:''; position:absolute; z-index:0; left:0; right:0; bottom:0; height:104px; background:linear-gradient(rgba(255,255,255,0),#fff 50%); pointer-events:none; }
:root.univer-mobile #l4 .univer-app-memo-toggle { position:absolute; z-index:1; left:18px; bottom:10px; min-height:32px; padding:5px 14px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-size:13px!important; font-weight:600!important; cursor:pointer; }
:root.univer-mobile #l4 .univer-app-memo-toggle:hover { background:var(--u-brand)!important; color:#fff!important; }
/* Empty tables keep their own heading; "no data" gets a quiet icon. */
@media (min-width:900px) {
 :root.univer-mobile #l4 .univer-app-table-scroll>table:has(tr.univer-app-empty-row):not(:has(tr:not(.univer-app-empty-row)>td)) tr.univer-app-table-caption,
 :root.univer-mobile #l4 table tr.univer-app-table-caption { display:table-row!important; }
 :root.univer-mobile #l4 tr.univer-app-table-caption>th { display:table-cell!important; }
}
/* Narrow card tables are blocks: the heading spans the full width instead of shrinking around its text. */
@media (max-width:899px) {
 :root.univer-mobile #l4 .univer-app-table-scroll>table:has(tr.univer-app-empty-row):not(:has(tr:not(.univer-app-empty-row)>td)) tr.univer-app-table-caption,
 :root.univer-mobile #l4 table tr.univer-app-table-caption { display:block!important; }
 :root.univer-mobile #l4 tr.univer-app-table-caption>th { display:block!important; width:auto!important; border-radius:10px 10px 0 0; }
}
:root.univer-mobile #l4 tr.univer-app-table-caption>th { padding:12px 16px!important; background:#fff!important; color:var(--u-ink)!important; font-size:15px!important; font-weight:600!important; text-align:start!important; }
:root.univer-mobile #l4 tr.univer-app-empty-row>td::before { content:''!important; display:inline-block!important; width:18px; height:18px; margin-right:10px; vertical-align:-4px; background:currentColor; opacity:.7; -webkit-mask:${inbox} center/contain no-repeat; }
/* Instruction files as document cards: a grid when the cell holds only files (nested blocks flatten into it),
   one card per file next to other text. */
:root.univer-mobile #l4 td.ct.univer-app-doc-links { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(280px,1fr)); gap:10px; padding:4px 0 12px!important; }
:root.univer-mobile #l4 td.ct.univer-app-doc-links :is(div,p,span,center) { display:contents!important; }
:root.univer-mobile #l4 td.ct.univer-app-doc-links br, :root.univer-mobile #l4 td.ct.univer-app-doc-cell a.univer-app-doc-link~br:not(center br) { display:none; }
:root.univer-mobile #l4 td.ct.univer-app-doc-cell a.univer-app-doc-link { width:min(100%,560px); margin:0 0 10px!important; }
:root.univer-mobile #l4 a.univer-app-doc-link { display:flex!important; align-items:center; gap:12px; min-height:56px; box-sizing:border-box; padding:10px 14px!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:600!important; line-height:1.35!important; text-decoration:none!important; overflow-wrap:anywhere; }
:root.univer-mobile #l4 a.univer-app-doc-link::before { content:''; flex:0 0 34px; height:34px; border-radius:9px; background:var(--u-brand-soft) ${doc} center/18px no-repeat; }
:root.univer-mobile #l4 a.univer-app-doc-link>img { display:none!important; }
:root.univer-mobile #l4 a.univer-app-doc-link:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-brand-strong)!important; }
/* Back link above the heading of a nested list; on the live page it shares a span with the heading link. */
:root.univer-mobile #l4 td.ct.univer-app-back-crumb, :root.univer-mobile #l4 .univer-app-back-row { display:flex!important; flex-direction:column; align-items:flex-start; gap:10px; font-size:0!important; }
:root.univer-mobile #l4 td.ct.univer-app-back-crumb :is(a:not(.univer-app-back-link),span:not(.univer-app-back-row),b) { font-size:var(--u-fs-title)!important; font-weight:600!important; color:var(--u-ink)!important; text-decoration:none!important; }
:root.univer-mobile #l4 a.univer-app-back-link { display:inline-flex!important; align-items:center; gap:6px; min-height:32px; box-sizing:border-box; padding:4px 12px 4px 10px!important; border:1px solid var(--u-line)!important; border-radius:999px!important; background:#fff!important; color:var(--u-muted)!important; font-size:13px!important; font-weight:600!important; text-decoration:none!important; cursor:pointer; }
:root.univer-mobile #l4 a.univer-app-back-link::before { content:'←'; font-weight:700; }
:root.univer-mobile #l4 a.univer-app-back-link:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
:root.univer-mobile #l4 td.ct.univer-app-period, :root.univer-mobile #l4 td.ct.univer-app-period * { padding:8px 0 2px!important; text-align:start!important; color:var(--u-muted)!important; font-size:13px!important; font-weight:600!important; letter-spacing:.01em; }
/* Distance-course list: a narrow icon column, the name with its discussion count and an Open button. */
:root.univer-mobile #l4 .univer-app-forum-courses tr>:is(td,th):first-child { width:36px!important; min-width:36px!important; padding-right:0!important; }
:root.univer-mobile #l4 .univer-app-forum-courses tr>:is(td,th):first-child img { width:18px; height:auto; opacity:.85; }
:root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child { position:relative; padding-right:132px!important; color:var(--u-ink)!important; font-weight:500; }
:root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child>.univer-app-course-open { position:absolute; right:14px; top:50%; transform:translateY(-50%); min-width:0; padding:5px 14px!important; min-height:32px!important; }
:root.univer-mobile #l4 .univer-app-count-box { font-size:0!important; margin-left:8px; }
:root.univer-mobile #l4 .univer-app-count { display:inline-flex; align-items:center; justify-content:center; min-width:28px; height:22px; box-sizing:border-box; padding:0 8px; border-radius:999px; background:var(--u-brand-soft); color:var(--u-brand); font-size:12px; font-weight:700; }
:root.univer-mobile #l4 .univer-app-count[data-zero=true] { background:var(--u-surface-2); color:var(--u-muted); font-weight:600; }
@media (max-width:899px) {
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link { display:flex!important; align-items:center; gap:10px; padding:10px 14px!important; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:first-child { display:inline-flex!important; width:auto!important; min-width:0!important; flex:0 0 auto; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child { flex:1 1 auto; grid-column:auto!important; display:flex!important; align-items:center; flex-wrap:wrap; gap:8px; padding-right:0!important; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child>.univer-app-course-open { position:static; transform:none; margin-left:auto!important; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td::before { display:none!important; }
}
/* Search over a long course list; adapter row layouts would otherwise outrank the hidden attribute. */
:root.univer-mobile #l4 :is(.univer-app-forum-courses,.univer-app-courses) tr[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-list-filter { margin:8px 0 4px; }
:root.univer-mobile #l4 .univer-app-list-filter input[type=search] { display:block; width:min(100%,420px)!important; height:40px; padding-left:38px!important; background:#fff ${search} 12px center/18px no-repeat!important; }
:root.univer-mobile #l4 .univer-app-list-empty { margin:10px 2px 0!important; color:var(--u-muted); font-size:var(--u-fs-meta); }
`;
    }
    function apply(lang) {
        language = lang || 'ru';
        if (document.readyState === 'loading' || !/^\/(student|ssc|my)(\/|$)/i.test(location.pathname)) return;
        enabled = true;
        if (!style || !style.isConnected) { style = document.createElement('style'); style.id = 'univer-pages-style'; style.textContent = css(); (document.head || document.documentElement).appendChild(style); }
        grades(); notes(); captions(); documents(); crumbs(); periods(); memos(); courses();
    }
    function release() {
        if (!enabled && !style) return;
        enabled = false;
        style?.remove(); style = null;
        filters.forEach(function (record) { record.box.remove(); }); filters.clear();
        hiddenRows.forEach(function (hidden, row) { if (row.hidden !== hidden) row.hidden = hidden; }); hiddenRows.clear();
        buttons.forEach(function (button) { button.remove(); }); buttons = [];
        splits.forEach(function (record) {
            var parent = record.box.parentNode;
            record.span.replaceWith(...record.span.childNodes); record.box.replaceWith(...record.box.childNodes); parent?.normalize();
        }); splits = [];
        wrapped.forEach(function (span) { if (span.parentNode) span.replaceWith(...span.childNodes); }); wrapped = [];
        roles.forEach(function (cell) { delete cell.dataset.univerRole; }); roles.clear();
        memoButtons.forEach(function (record, cell) { record.button.remove(); delete cell.dataset.univerFolded; }); memoButtons.clear();
        var names = ['univer-app-grades','univer-app-callout','univer-app-table-caption','univer-app-doc-links','univer-app-doc-cell','univer-app-doc-link','univer-app-back-crumb','univer-app-back-link','univer-app-back-row','univer-app-period','univer-app-memo','univer-app-forum-courses'];
        marked.forEach(function (node) { node.classList.remove(...names); if (!node.getAttribute('class')) node.removeAttribute('class'); }); marked.clear();
    }
    window.__univerPagesV1 = {apply:apply, release:release};
})();
