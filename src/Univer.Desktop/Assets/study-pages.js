/* Layouts for observed study pages (attestation, distance courses, debts, FX, GPA increase, exam schedules). Only
   classes, data attributes and small added controls; the site's nodes and handlers stay, and release() restores them. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerPagesV1) return;
    var common = window.__univerCommonV1, clean = common.clean, fold = common.fold, ownRows = common.ownRows;
    var enabled = false, language = 'ru', style = null;
    var marked = new Set(), roles = new Set(), wrapped = [], splits = [], buttons = [], filters = new Map(), hiddenRows = new Map(), memoButtons = new Map(), links = [], hinted = new Set(), sections = new Map();
    // A course cell's own text, without the Open button the adapters add to it.
    function label(cell) { return Array.from(cell.childNodes).filter(function (n) { return !n.classList?.contains('univer-app-course-open') && !n.classList?.contains('univer-app-course-tasks'); }).map(function (n) { return n.textContent; }).join('').replace(/\s+/g, ' ').trim(); }
    function text() {
        return language === 'en' ? {copy:'Copy', copied:'Copied', noContacts:'Not specified', open:'Open', filter:'Find a course', none:'No courses match the search', openCourse:'Open course: ', more:'Show in full', less:'Collapse', outside:'Link to another site: ', save:'Download file', openLink:'Open link', all:'Download all', allLabel:'Download every file in this section', stop:'Stop', fresh:'New', freshHint:'Added since your last visit'} :
            language === 'kz' ? {copy:'Көшіру', copied:'Көшірілді', noContacts:'Көрсетілмеген', open:'Ашу', filter:'Пәнді іздеу', none:'Іздеуге сәйкес пән жоқ', openCourse:'Пәнді ашу: ', more:'Толығымен көрсету', less:'Жию', save:'Файлды жүктеп алу', openLink:'Сілтемені ашу', outside:'Басқа сайтқа сілтеме: ', all:'Барлығын жүктеу', allLabel:'Бөлімнің барлық файлын жүктеу', stop:'Тоқтату', fresh:'Жаңа', freshHint:'Соңғы кіруіңізден кейін қосылды'} :
            {copy:'Копировать', copied:'Скопировано', noContacts:'Не указаны', open:'Открыть', save:'Скачать файл', openLink:'Открыть ссылку', filter:'Найти дисциплину', none:'Дисциплины по запросу не найдены', openCourse:'Открыть дисциплину: ', more:'Показать полностью', less:'Свернуть', outside:'Ссылка на другой сайт: ', all:'Скачать все', allLabel:'Скачать все файлы раздела', stop:'Остановить', fresh:'Новый', freshHint:'Появился после вашего прошлого захода'};
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
    // УМКД: every material section folds under its heading, whether it is a heading row of the files table or a heading
    // over a block of links (Силлабус). The heading itself is the button. Every visit opens with all sections unfolded
    // (the owner's choice): folds are never stored, and what earlier versions stored is removed.
    try { localStorage.removeItem('univer.desktop.umkd-folds.v1'); } catch (_) { }
    function coursePage() { return /^\/student\/umkd\/\d+\/\d+\/\d+\/?$/.test(location.pathname); }
    function foldName(head) { return clean(head).slice(0, 200); }
    // A fold slides its rows shut and open rather than cutting them. One registered number, --univer-slide, runs between
    // 1 and 0, and a rule scales each row's height, vertical padding, margins and borders by it: the card rules set those
    // with !important, which keyframes cannot move. The folded attribute is the state from the first frame; a row sliding
    // shut stays on screen until its run ends. The run's own keyframes carry the measures, so no inline style is written.
    var slides = new Map(), slideRegistered = null;
    function slideReady() {
        var root = document.documentElement;
        if (window.__univerMotion === false || !root.classList.contains('univer-mobile') || root.classList.contains('univer-app-keyboard') || window.__univerMotionUI?.reduced?.() || !root.animate) return false;
        if (slideRegistered === null) {
            try { CSS.registerProperty({name:'--univer-slide', syntax:'<number>', inherits:false, initialValue:'1'}); slideRegistered = true; }
            catch (error) { slideRegistered = !!error && error.name === 'InvalidModificationError'; }
        }
        return slideRegistered;
    }
    function slideMeasures(item) {
        var style = getComputedStyle(item);
        // An inline item, inline blocks too, sits in a line that keeps its height until the item is hidden: it is cut at once.
        // (A child of a flex or grid row is blockified, so it never reports inline-*.)
        if (/^(none|contents|inline|table|ruby)/.test(style.display)) return null;
        var height = item.getBoundingClientRect().height;
        if (height < 1) return null;
        return {'--univer-slide-h':height + 'px', '--univer-slide-pt':style.paddingTop, '--univer-slide-pb':style.paddingBottom, '--univer-slide-mt':style.marginTop,
            '--univer-slide-mb':style.marginBottom, '--univer-slide-bt':style.borderTopWidth, '--univer-slide-bb':style.borderBottomWidth};
    }
    function stopSlide(item) {
        var run = slides.get(item);
        if (!run) return;
        slides.delete(item); item.removeAttribute('data-univer-sliding'); run.animation.cancel();
    }
    // Starts the slide from measures already read. A reversal uses the row's current --univer-slide as `from`.
    function slideRow(item, closing, prepared) {
        if (prepared.run) stopSlide(item);
        if (!closing) item.removeAttribute('data-univer-folded-row');
        if (closing) item.setAttribute('data-univer-folded-row', '');
        var measures = prepared.measures;
        if (!measures || !item.animate) return;
        var from = prepared.from, to = closing ? 0 : 1, frame = function (value) { var result = {'--univer-slide':String(value)}; Object.keys(measures).forEach(function (k) { result[k] = measures[k]; }); return result; };
        item.setAttribute('data-univer-sliding', '');
        var animation = item.animate([frame(from), frame(to)], {duration:Math.max(90, Math.round((closing ? 260 : 320) * Math.abs(to - from))),
            easing:closing ? 'cubic-bezier(.4,0,.2,1)' : 'cubic-bezier(.22,1,.36,1)', fill:'forwards'});
        var mine = {animation:animation, measures:measures};
        slides.set(item, mine);
        animation.onfinish = function () { if (slides.get(item) === mine) stopSlide(item); };
    }
    function paintFold(record, animate) {
        var closed = record.closed, state = closed ? 'closed' : 'open', items = record.items;
        // Past 20 rows the slide would measure and animate every row. The fold still happens, at once.
        var slide = animate && items.length <= 20 && slideReady(), plan = [];
        if (slide) {
            items.forEach(function (item) {
                if (item.hasAttribute('data-univer-folded-row') === closed) return;
                var run = slides.get(item), from = closed ? 1 : 0, measures = run ? run.measures : null;
                if (run) { var now = parseFloat(getComputedStyle(item).getPropertyValue('--univer-slide')); if (now >= 0 && now <= 1) from = now; }
                plan.push({item:item, run:run, from:from, measures:measures, measure:!measures});
            });
            // A folded row has no box until it is shown. Open every one of those, then read them together.
            if (!closed) plan.forEach(function (step) { if (step.measure) step.item.removeAttribute('data-univer-folded-row'); });
            plan.forEach(function (step) { if (step.measure) step.measures = slideMeasures(step.item); });
        }
        if (record.head.getAttribute('aria-expanded') !== String(!closed)) record.head.setAttribute('aria-expanded', String(!closed));
        if (record.head.dataset.univerFold !== state) record.head.dataset.univerFold = state;
        if (!slide) {
            items.forEach(function (item) {
                if (item.hasAttribute('data-univer-folded-row') === closed) return;
                stopSlide(item);
                if (closed) item.setAttribute('data-univer-folded-row', ''); else item.removeAttribute('data-univer-folded-row');
            });
            return;
        }
        plan.forEach(function (step) { slideRow(step.item, closed, step); });
    }
    // «Скачать все»: a button in a files section's heading row, beside the heading button rather than inside it, with its
    // label drawn by CSS, so the heading's text (which names the fold and the folder) stays the same. Teachers' links are
    // not files and are left out; the app checks every address again before it asks for one.
    function sectionFiles(items) {
        var found = [];
        items.forEach(function (row) {
            if (row.querySelector(':scope>td[data-univer-kind=link]')) return;
            var link = row.querySelector('a.downLoad[href]'), url = null;
            try { url = link && new URL(link.href); } catch (_) { url = null; }
            if (url && url.origin === location.origin && /^\/student\/umkd\/get\/\d+\/\d+\/\d+\/\d+\/?$/.test(url.pathname) && found.indexOf(url.href) < 0) found.push(url.href);
        });
        return found;
    }
    function notify(message) {
        if (typeof __univerNative === 'function') __univerNative(message);
    }
    // The app takes at most 200 files in one message, so a larger section has no button rather than one that does nothing.
    function allButton(record, files) {
        var button = record.all, count = files ? sectionFiles(record.items).length : 0;
        if (count < 2 || count > 200) {
            if (button) { button.remove(); buttons = buttons.filter(function (b) { return b !== button; }); record.all = null; }
            return;
        }
        if (!button) {
            button = record.all = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-fold-all';
            button.addEventListener('click', function (event) {
                event.preventDefault(); event.stopPropagation();
                // A page script clicking the button starts nothing: only the student's own click or key does.
                if (!event.isTrusted) return;
                // Stop only from a button that shows a run, and a second click within 1.5 s (a double click, whose second
                // half may already see the run's label) is ignored, so a double click never stops the run it started.
                var urls = sectionFiles(record.items), now = performance.now();
                if (record.sent != null && now - record.sent < 1500) return;
                record.sent = now;
                if (urls.length) notify('univer-download-all:' + JSON.stringify({section:foldName(record.head), urls:urls, stop:record.progress != null}));
            });
            buttons.push(button);
        }
        var row = record.head.parentNode;
        if (row && button.parentNode !== row) row.appendChild(button);
        labelAll(record);
    }
    function labelAll(record) {
        var button = record.all; if (!button) return;
        var t = text(), busy = record.progress != null;
        var label = busy ? t.stop + ' · ' + record.progress : t.all, name = busy ? t.stop : t.allLabel;
        if (button.dataset.label !== label) button.dataset.label = label;
        if (button.getAttribute('aria-label') !== name) button.setAttribute('aria-label', name);
        if (button.title !== name) button.title = name;
        if (button.hasAttribute('data-univer-busy') !== busy) button.toggleAttribute('data-univer-busy', busy);
    }
    // The app reports a running «Скачать все» of a section: files done of the total, or a total of 0 once it has ended.
    function downloading(section, done, total) {
        sections.forEach(function (record) {
            if (foldName(record.head) !== section) return;
            record.progress = total > 0 ? Math.min(done, total) + '/' + total : null;
            labelAll(record);
        });
    }
    function section(head, items, files) {
        var record = sections.get(head);
        if (!record) {
            record = {head:head, items:items, closed:false};
            record.toggle = function (event) {
                if (event.type === 'keydown' && event.key !== 'Enter' && event.key !== ' ') return;
                if (event.target !== head && event.target.closest('a,button,input,select,textarea')) return;
                event.preventDefault();
                record.closed = !record.closed; paintFold(record, event.type === 'keydown' || event.detail > 0);
                if (record.closed && owns.has(head)) promoteOwn(owns.get(head));
            };
            head.addEventListener('click', record.toggle); head.addEventListener('keydown', record.toggle);
            head.setAttribute('role', 'button'); head.tabIndex = 0;
            sections.set(head, record);
        } else record.items = items;
        var count = String(items.length);
        if (head.dataset.univerCount !== count) head.dataset.univerCount = count;
        paintFold(record, false);
        allButton(record, files);
    }
    // An empty syllabus has its own wrapper and the site's explicit placeholder.
    var SYLLABUS = /^(силлабус|syllabus)$/i;
    var NOT_FILLED = /^(не\s+заполнен[оаы]?|толтырылма(ған|ды|й\s+қалған)|not\s+(filled|completed)(\s+in)?)$/i, blanks = new Set();
    function unfilled(head) {
        var box = head.parentElement, words = '', node = head.nextSibling;
        if (!SYLLABUS.test(clean(head)) || box?.tagName !== 'DIV' || box.querySelector('a[href],img,iframe,video') ||
            box.querySelectorAll('h4').length !== 1) return false;
        while (node) {
            words += ' ' + (node.textContent || ''); node = node.nextSibling;
        }
        return NOT_FILLED.test(words.replace(/[\s()[\].,:;!–—-]+/g, ' ').trim());
    }
    // The student's own syllabus in a block of printed syllabi: a tag in its link, and first in the block. It moves only
    // while nobody is using the block (folded, or neither pointed at nor focused); otherwise it is first on the next visit,
    // when syllabus-tasks.js already knows the answer.
    var owns = new Map();
    function ownText() { return language === 'en' ? 'Your programme' : language === 'kz' ? 'Сіздің мамандығыңыз' : 'Ваша специальность'; }
    function ownSection(head, items) {
        var own = window.__univerSyllabusV1?.own, links = items.filter(function (node) {
            if (!node.matches('a[href]')) return false;
            try { var url = new URL(node.getAttribute('href'), location.href); return url.origin === location.origin && /^\/student\/interactive\/syllabus\/print\/\d+\/\d+\/?$/.test(url.pathname); } catch (_) { return false; }
        });
        if (links.length < 1 || typeof own !== 'function') return;
        var key = links.map(function (a) { return a.href; }).join(' '), record = owns.get(head);
        // A lookup that failed (the programme could not be read) is asked again, at most every 20 s.
        if (record && record.key === key && !(record.failed && Date.now() - record.failed > 20000)) { promoteOwn(record); return; }
        if (record) unmarkOwn(record);
        record = {key:key, head:head, link:null, tag:null, home:null, pending:false, box:null, later:null}; owns.set(head, record);
        own(links.map(function (a) { return a.href; })).then(function (href) {
            var link = href && links.find(function (a) { return a.href === href; });
            if (!enabled || owns.get(head) !== record || !link || !link.isConnected) return;
            var tag = document.createElement('span'); tag.className = 'univer-app-own-tag'; tag.textContent = ownText();
            link.appendChild(tag); link.classList.add('univer-app-syllabus-own'); record.link = link; record.tag = tag;
            if (head.nextElementSibling === link) return;
            // In use now: the move waits until the block is folded, or left by both the pointer and the focus.
            record.pending = true; record.box = link.parentNode;
            record.later = function () { setTimeout(function () { if (owns.get(head) === record) promoteOwn(record); }, 0); };
            record.box.addEventListener('mouseleave', record.later); record.box.addEventListener('focusout', record.later);
            promoteOwn(record);
        }, function () { record.failed = Date.now(); });
    }
    function promoteOwn(record) {
        var link = record.link, head = record.head, box = record.box, closed = !!sections.get(head)?.closed;
        if (!enabled || !record.pending || !link || !link.isConnected || link.parentNode !== box) return;
        var inUse = false;
        try { inUse = box.matches(':hover'); } catch (_) { }
        if ((inUse || box.contains(document.activeElement)) && !closed) return;
        record.pending = false; stopWaiting(record);
        if (head.nextElementSibling === link) return;
        record.home = {parent:box, next:link.nextSibling};
        var move = function () { head.after(link); };
        if (window.__univerMotionUI && !closed) window.__univerMotionUI.reflow(box, move); else move();
    }
    function stopWaiting(record) {
        if (!record.box || !record.later) return;
        record.box.removeEventListener('mouseleave', record.later); record.box.removeEventListener('focusout', record.later); record.later = null;
    }
    function unmarkOwn(record) {
        record.pending = false; stopWaiting(record);
        record.tag?.remove();
        if (record.link) { record.link.classList.remove('univer-app-syllabus-own'); if (!record.link.getAttribute('class')) record.link.removeAttribute('class'); }
        var home = record.home;
        if (home && record.link && home.parent.isConnected) home.parent.insertBefore(record.link, home.next && home.next.parentNode === home.parent ? home.next : null);
    }
    function folds() {
        if (!coursePage()) return;
        document.querySelectorAll('#l4 table.univer-app-materials').forEach(function (table) {
            var current = null, groups = [];
            ownRows(table).forEach(function (row) {
                if (row.cells.length === 1 && row.cells[0].tagName === 'TH') { current = {head:row.cells[0], items:[]}; groups.push(current); }
                else if (current && row.matches('tr.file')) current.items.push(row);
            });
            groups.forEach(function (group) { if (group.items.length && clean(group.head)) section(group.head, group.items, true); });
        });
        var empty = new Set();
        document.querySelectorAll('#l4 td.tt h4').forEach(function (h4) {
            if (h4.closest('#tools')) return;
            if (unfilled(h4)) {
                var box = h4.parentElement;
                empty.add(box);
                if (!blanks.has(box)) box.setAttribute('data-univer-empty-section', '');
                return;
            }
            var items = [], node = h4.nextElementSibling;
            while (node && node.tagName !== 'H4') { items.push(node); node = node.nextElementSibling; }
            if (items.length && clean(h4)) { section(h4, items, false); ownSection(h4, items); }
        });
        blanks.forEach(function (box) { if (!empty.has(box)) box.removeAttribute('data-univer-empty-section'); });
        blanks = empty;
        // A file row says what its click does: it saves the file, or opens the teacher's link.
        document.querySelectorAll('#l4 table.univer-app-materials tr.file a.downLoad').forEach(function (a) {
            var hint = a.closest('tr').querySelector(':scope>td[data-univer-kind=link]') ? text().openLink : text().save;
            if (!a.hasAttribute('title')) hinted.add(a);
            if (hinted.has(a) && a.getAttribute('title') !== hint) a.setAttribute('title', hint);
        });
    }
    /* Files new since the student's last visit to this course page. The first visit only notes what is there; later
       visits mark the files that were not, and the marks stay until the page is left (the next visit knows them all).
       Only file ids are kept, per course page, for the account that signed in here (the sign-in form's mark): no mark,
       nothing is read or written. The browser extension keeps none (__univerUmkdSeenKeep === false). */
    var SEEN = 'univer.desktop.umkd-seen.v1', SEEN_PAGES = 60, SEEN_FILES = 500, SEEN_SIZE = 200000;
    var seenKeeps = window.__univerUmkdSeenKeep !== false, baseline, seenWritten = false, seenStored = new Set(), freshMarks = [];
    function fileId(link) {
        try {
            var url = new URL(link.getAttribute('href'), location.href);
            var found = url.origin === location.origin && url.pathname.match(/^\/student\/umkd\/get\/(\d{1,12})\/(\d{1,12})\/\d{1,6}\/\d{1,6}\/?$/);
            return found ? found[1] + '/' + found[2] : '';
        } catch (_) { return ''; }
    }
    function seenRecord(who) {
        try {
            var raw = localStorage.getItem(SEEN), all = raw && raw.length <= SEEN_SIZE ? JSON.parse(raw) : null;
            if (all && typeof all === 'object' && all.owner === who && all.pages && typeof all.pages === 'object' && !Array.isArray(all.pages)) return all;
        } catch (_) { }
        return {owner:who, pages:{}};
    }
    function noteSeen(who, path, ids) {
        try {
            var all = seenRecord(who);
            all.pages[path] = {at:Date.now(), ids:ids.slice(-SEEN_FILES)};
            Object.keys(all.pages).sort(function (a, b) { return (all.pages[b].at || 0) - (all.pages[a].at || 0); }).slice(SEEN_PAGES).forEach(function (old) { delete all.pages[old]; });
            var raw = JSON.stringify(all); if (raw.length <= SEEN_SIZE) localStorage.setItem(SEEN, raw);
        } catch (_) { }
    }
    function freshFiles() {
        if (!coursePage() || !seenKeeps) return;
        var who = ''; try { who = localStorage.getItem('univer.app.account.v1') || ''; } catch (_) { }
        if (!who) return;
        // The course's file table with its headings: an empty one is a fair first look, a page without it (a notice, an error) is not.
        var tables = Array.from(document.querySelectorAll('#l4 td.tt table.inner')).filter(function (table) {
            var head = ownRows(table)[0]; return !table.closest('#tools') && head && head.cells.length >= 3 && head.cells[0].tagName === 'TH';
        });
        if (!tables.length) return;
        var rows = [], ids = [];
        tables.forEach(function (table) { ownRows(table).forEach(function (row) {
            var link = row.matches('tr.file') && row.querySelector('a.downLoad[href]'), id = link ? fileId(link) : '';
            if (id) { rows.push({link:link, id:id}); if (ids.indexOf(id) < 0) ids.push(id); }
        }); });
        var path = location.pathname.replace(/\/$/, '');
        // Read once per page: later passes (and Original → reading mode) mark from memory. Files that appear later in the
        // same visit are new against the same first look and are noted too.
        if (baseline === undefined) {
            var page = seenRecord(who).pages[path];
            baseline = page && Array.isArray(page.ids) ? new Set(page.ids.filter(function (id) { return typeof id === 'string'; })) : null;
        }
        if (!seenWritten || ids.some(function (id) { return !seenStored.has(id); })) {
            var known = baseline ? Array.from(baseline) : []; seenWritten = true;
            ids.forEach(function (id) { seenStored.add(id); });
            noteSeen(who, path, known.filter(function (id) { return !seenStored.has(id); }).concat(Array.from(seenStored)));
        }
        if (!baseline) return;
        var t = text();
        rows.forEach(function (item) {
            if (baseline.has(item.id)) return;
            var mark = item.link.nextElementSibling;
            if (!mark || !mark.classList.contains('univer-app-file-new')) {
                mark = document.createElement('span'); mark.className = 'univer-app-file-new';
                item.link.after(mark); freshMarks.push(mark);
            }
            if (mark.textContent !== t.fresh) mark.textContent = t.fresh;
            if (mark.title !== t.freshHint) mark.title = t.freshHint;
        });
    }
    // The printed syllabus: a lecturer's or assistant's cell holds a row of bold headings (name, mobile phone, e-mail) and
    // a row per person. A well-formed phone or address becomes a tel: or mailto: link, each value gets a copy button, and
    // a person row with nothing in it is left out on screen; print shows the page as the site made it.
    var contactCells = new Map(), contactStatus = null;
    function contactKind(label) {
        return /телефон|phone/i.test(label) ? 'phone' : /e-?mail|почта|пошта/i.test(label) ? 'email' : /^(ф\.?\s*и\.?\s*о\.?|фио|аты.?жөні|full name|name)$/i.test(label) ? 'name' : '';
    }
    function phoneHref(value) {
        var digits = value.replace(/\D/g, '');
        return /^\+?[\d\s()-]+$/.test(value) && digits.length >= 10 && digits.length <= 15 ? 'tel:' + (/^\s*\+/.test(value) ? '+' : '') + digits : '';
    }
    function mailHref(value) {
        return value.length <= 254 && !/\.\./.test(value) && /^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$/.test(value) ? 'mailto:' + value : '';
    }
    // The one text node a field shows its value in, or null when the value is split or already wrapped.
    function valueNode(field) {
        var walker = document.createTreeWalker(field, 4), found = [], node;
        while ((node = walker.nextNode())) if (node.data.trim() && !node.parentElement.closest('button')) found.push(node);
        return found.length === 1 && !found[0].parentElement.closest('a') ? found[0] : null;
    }
    function copyText(value, done) {
        var fallback = function () {
            var area = document.createElement('textarea'), focused = document.activeElement; area.value = value; area.setAttribute('readonly', '');
            area.style.cssText = 'position:fixed;opacity:0;left:-9999px;top:0'; document.body.appendChild(area); area.select();
            try { if (document.execCommand('copy')) done(); } catch (_) { }
            area.remove(); focused?.focus?.();
        };
        if (navigator.clipboard?.writeText) navigator.clipboard.writeText(value).then(done, fallback); else fallback();
    }
    function copyButton(record, field, kind) {
        var button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-copy';
        var label = document.createElement('span'); label.className = 'univer-app-copy-label'; button.appendChild(label);
        var value = function () { return Array.from(field.childNodes).filter(function (n) { return n !== button; }).map(function (n) { return n.textContent; }).join('').replace(/\s+/g, ' ').trim(); };
        button.__univerLabel = function () {
            var name = text().copy + ': ' + value();
            if (button.getAttribute('aria-label') !== name) { button.setAttribute('aria-label', name); button.title = name; }
            var shown = button.classList.contains('univer-app-copied') ? text().copied : '';
            if (label.textContent !== shown) label.textContent = shown;
        };
        button.addEventListener('click', function (event) {
            event.preventDefault(); event.stopPropagation();
            copyText(value(), function () {
                if (!enabled) return;
                button.classList.add('univer-app-copied'); button.__univerLabel();
                if (contactStatus) { contactStatus.textContent = ''; contactStatus.textContent = text().copied; }
                clearTimeout(button.__univerTimer);
                button.__univerTimer = setTimeout(function () { button.classList.remove('univer-app-copied'); button.__univerLabel(); }, 1600);
            });
        });
        button.dataset.univerKind = kind; field.appendChild(button); record.buttons.push(button); button.__univerLabel();
    }
    function contacts() {
        if (!/^\/student\/interactive\/syllabus\/print\//i.test(location.pathname)) return;
        document.querySelectorAll('td,th').forEach(function (cell) {
            var record = contactCells.get(cell);
            if (!record) {
                var rows = Array.from(cell.children).filter(function (n) { return n.matches('div.input-row'); });
                var head = rows[0], heads = head ? Array.from(head.children).filter(function (n) { return n.matches('.input-field'); }) : [];
                if (rows.length < 2 || heads.length < 2 || !heads.every(function (f) { return f.querySelector('b,strong') && clean(f); })) return;
                var kinds = heads.map(function (f) { return contactKind(clean(f)); });
                if (kinds.indexOf('phone') < 0 && kinds.indexOf('email') < 0) return;
                record = {head:head, rows:rows.slice(1), buttons:[], links:[], fields:[], note:null};
                contactCells.set(cell, record); mark(cell, 'univer-app-contacts'); mark(head, 'univer-app-contact-head');
                record.rows.forEach(function (row) {
                    var fields = Array.from(row.children).filter(function (n) { return n.matches('.input-field'); });
                    if (!fields.some(function (f) { return clean(f); })) { mark(row, 'univer-app-contact-empty'); return; }
                    fields.forEach(function (field, i) {
                        var kind = kinds[i] || '', value = clean(field);
                        field.dataset.univerContactLabel = clean(heads[i] || ''); record.fields.push(field);
                        if (!value) return;
                        var node = valueNode(field), href = node && (kind === 'phone' ? phoneHref(value) : kind === 'email' ? mailHref(value) : '');
                        if (href) {
                            var link = document.createElement('a'); link.href = href; link.className = 'univer-app-contact-link';
                            node.parentNode.insertBefore(link, node); link.appendChild(node); record.links.push(link);
                        }
                        // A dash or "не указан" is no number or address to copy.
                        if (kind === 'phone' ? (value.match(/\d/g) || []).length >= 5 : kind === 'email' ? value.indexOf('@') > 0 : /\p{L}/u.test(value)) copyButton(record, field, kind || 'name');
                    });
                });
                if (record.rows.every(function (row) { return row.classList.contains('univer-app-contact-empty'); })) {
                    mark(head, 'univer-app-contact-empty');
                    record.note = document.createElement('span'); record.note.className = 'univer-app-contact-none'; cell.appendChild(record.note);
                }
            }
            record.buttons.forEach(function (button) { button.__univerLabel(); });
            if (record.note && record.note.textContent !== text().noContacts) record.note.textContent = text().noContacts;
        });
        if (contactCells.size && (!contactStatus || !contactStatus.isConnected)) {
            contactStatus = document.createElement('span'); contactStatus.className = 'univer-app-copy-status'; contactStatus.setAttribute('role', 'status');
            document.body.appendChild(contactStatus);
        }
    }
    function releaseContacts() {
        contactCells.forEach(function (record) {
            record.buttons.forEach(function (button) { clearTimeout(button.__univerTimer); button.remove(); });
            record.links.forEach(function (link) { var parent = link.parentNode; if (parent) { link.replaceWith(...link.childNodes); parent.normalize(); } });
            record.fields.forEach(function (field) { delete field.dataset.univerContactLabel; });
            record.note?.remove();
        });
        contactCells.clear(); contactStatus?.remove(); contactStatus = null;
    }
    // A course's UMKD page: a teacher's link to another site is marked as such, and an address
    // written out as plain text in a material's row becomes such a link. The app opens it only after finding the same
    // address in this page's materials.
    function outside() {
        if (!/^\/student\/umkd\/\d+\/\d+\/\d+\/?$/.test(location.pathname)) return;
        document.querySelectorAll('#l4 table.mt').forEach(function (panel) {
            if (panel.closest('#tools')) return;
            panel.querySelectorAll('tr.file>td,tr.link>td').forEach(function (cell) {
                var walker = document.createTreeWalker(cell, 4), found = [], node;
                while ((node = walker.nextNode())) if (/https?:\/\//.test(node.data) && !node.parentElement.closest('a,button,script,style')) found.push(node);
                found.forEach(linkify);
            });
            panel.querySelectorAll('a[href]').forEach(function (a) {
                var url; try { url = new URL(a.getAttribute('href'), location.href); } catch (_) { return; }
                if (!/^https?:$/.test(url.protocol) || url.origin === location.origin || url.username || url.password) return;
                mark(a, 'univer-app-outside-link');
                var hint = text().outside + url.hostname;
                if (!a.hasAttribute('title')) hinted.add(a);
                if (hinted.has(a) && a.getAttribute('title') !== hint) a.setAttribute('title', hint);
            });
        });
    }
    // An address as it was written in a sentence: the sentence's punctuation is not part of it, and a closing bracket
    // is only when its opening one is ("…/Метод_(наука)").
    function written(value) {
        for (;;) {
            var cut = value.replace(/[.,;:!?»"']+$/, '');
            if (/\)$/.test(cut) && cut.split('(').length < cut.split(')').length) cut = cut.slice(0, -1);
            if (cut === value) return value;
            value = cut;
        }
    }
    function linkify(node) {
        var match = /https?:\/\/[^\s<>"']+/.exec(node.data), address = match && written(match[0]), url;
        try { url = new URL(address); } catch (_) { return; }
        var tail = node.splitText(match.index), rest = tail.splitText(address.length);
        var link = document.createElement('a'); link.href = url.href;
        tail.parentNode.insertBefore(link, tail); link.appendChild(tail); links.push(link);
        if (/https?:\/\//.test(rest.data)) linkify(rest);
    }
    function icon(svg, color) { return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='" + (color || 'black') + "' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'>" + svg + '</svg>') + '")'; }
    function css() {
        var doc = icon("<path d='M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z'/><path d='M14 3v5h5M9 13h6M9 17h4'/>", '#1b5fa6');
        var note = icon("<circle cx='12' cy='12' r='9'/><path d='M12 7.5v5.5M12 16.5v.5'/>");
        var inbox = icon("<path d='M4 13h4l2 3h4l2-3h4'/><path d='M6 5h12l2 8v6H4v-6z'/>");
        var search = icon("<circle cx='11' cy='11' r='6.5'/><path d='M16 16l4 4'/>");
        var fold = icon("<path d='M6 9l6 6 6-6'/>");
        var save = icon("<path d='M12 4v11M7 10l5 5 5-5M5 20h14'/>");
        var halt = icon("<rect x='7' y='7' width='10' height='10' rx='1.5'/>");
        var leave = icon("<path d='M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6'/>");
        var copy = icon("<rect x='9' y='9' width='11' height='11' rx='2'/><path d='M5 15V6a2 2 0 0 1 2-2h9'/>"), check = icon("<path d='M5 12.5l4.5 4.5L19 7.5'/>");
        return `
/* Grade tables: numbers line up and the letter grade is a coloured badge. */
:root.univer-mobile #l4 .univer-app-grades :is(td,th):is([data-univer-role=credit],[data-univer-role=score],[data-univer-role=gpa],[data-univer-role=mark],[data-univer-role=letter]) { font-variant-numeric:tabular-nums; }
:root.univer-mobile #l4 .univer-app-grade { display:inline-flex; align-items:center; justify-content:center; box-sizing:border-box; min-width:36px; height:24px; padding:0 8px; border-radius:6px; font-size:13px!important; font-weight:600!important; line-height:1!important;  }
:root.univer-mobile #l4 .univer-app-grade[data-band=a] { background:#e3f3ef; color:#0f6b63!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=b] { background:#e9f1fb; color:#1b5fa6!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=c] { background:#fdf3dd; color:#80540a!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=d] { background:#fdeee3; color:#a3440e!important; }
:root.univer-mobile #l4 .univer-app-grade[data-band=f] { background:#fdeceb; color:#b42318!important; }
@media (min-width:900px) {
 :root.univer-mobile #l4 .univer-app-grades tr>:is(td,th):is([data-univer-role=credit],[data-univer-role=score],[data-univer-role=gpa],[data-univer-role=mark],[data-univer-role=letter]) { width:1%; white-space:nowrap!important; text-align:center!important; padding-left:14px!important; padding-right:14px!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=name] { color:var(--u-ink)!important; font-weight:500!important; text-align:start!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=score] { color:var(--u-ink)!important; font-weight:500!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=mark] { color:var(--u-text)!important; }
 :root.univer-mobile #l4 .univer-app-grades td[data-univer-role=word] { color:var(--u-muted)!important; font-weight:500!important; }
}
/* Narrow windows: one compact card per course, the name first and the figures in one wrapped line. */
@media (max-width:899px) {
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row { display:flex!important; flex-wrap:wrap; align-items:center; gap:8px 18px; padding:14px 16px!important; margin:8px 0!important; }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td { display:inline-flex!important; align-items:center; gap:6px; grid-column:auto!important; font-weight:500; color:var(--u-ink); }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td[data-univer-role=name] { flex:1 0 100%; font-size:15px!important; }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td[data-univer-role=word] { font-weight:400; color:var(--u-muted); }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td[data-univer-field]::before { display:inline!important; margin:0!important; font-size:12px!important; font-weight:400!important; color:var(--u-muted)!important; }
 :root.univer-mobile #l4 .univer-app-grades tr.univer-app-card-row>td:is([data-univer-role=name],[data-univer-role=word],[data-univer-role=letter])::before { display:none!important; }
}
/* Notes: a short box with an icon instead of large red bold text. */
:root.univer-mobile #l4 td.ct.univer-app-callout { display:block!important; position:relative; box-sizing:border-box; margin:8px 0 16px!important; padding:12px 16px 12px 46px!important; border:1px solid #f1d6a6!important; border-radius:12px!important; background:#fff8ea!important; color:#5c3d0a!important; font-size:14px!important; font-weight:400!important; line-height:1.55!important; }
:root.univer-mobile #l4 td.ct.univer-app-callout::before { content:''; position:absolute; left:16px; top:14px; width:18px; height:18px; background:#b26a00; -webkit-mask:${note} center/contain no-repeat; }
:root.univer-mobile #l4 td.ct.univer-app-callout :is(b,strong,font,span,p) { color:inherit!important; font-size:inherit!important; font-weight:400!important; }
:root.univer-mobile #l4 td.ct.univer-app-callout>:is(b,strong):first-child, :root.univer-mobile #l4 td.ct.univer-app-callout>:is(b,strong):first-child * { font-weight:600!important; }
:root.univer-mobile #l4 td.ct.univer-app-callout br+br, :root.univer-mobile #l4 td.ct.univer-app-callout>br:first-child { display:none; }
/* A long memo opens folded to its first lines, in a box, with a button to read it in full. */
:root.univer-mobile #l4 td.ct.univer-app-memo { display:block!important; position:relative; box-sizing:border-box; margin:4px 0 16px!important; padding:14px 18px 56px!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:#fff!important; font-size:14px!important; line-height:1.55!important; overflow:hidden; }
:root.univer-mobile #l4 td.ct.univer-app-memo :is(b,strong,span,p,font,div) { font-size:inherit!important; line-height:inherit!important; }
:root.univer-mobile #l4 td.ct.univer-app-memo[data-univer-folded=true] { max-height:15.5em; }
/* The folded text fades out above a solid band that holds the button, so no line runs under it. */
:root.univer-mobile #l4 td.ct.univer-app-memo[data-univer-folded=true]::after { content:''; position:absolute; z-index:0; left:0; right:0; bottom:0; height:104px; background:linear-gradient(rgba(255,255,255,0),#fff 50%); pointer-events:none; }
:root.univer-mobile #l4 .univer-app-memo-toggle { position:absolute; z-index:1; left:18px; bottom:10px; min-height:32px; padding:4px 12px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; font-size:13px!important; font-weight:500!important; cursor:pointer; }
:root.univer-mobile #l4 .univer-app-memo-toggle:hover { background:var(--u-brand)!important; color:#fff!important; }
/* An open memo is long: Collapse rides the bottom edge of the window and stops at the memo's end. */
:root.univer-mobile #l4 td.ct.univer-app-memo[data-univer-folded=false] { padding-bottom:14px!important; overflow:clip; }
:root.univer-mobile #l4 td.ct.univer-app-memo[data-univer-folded=false] .univer-app-memo-toggle { position:sticky; left:auto; bottom:16px; display:block; margin:12px 0 0!important; box-shadow:0 4px 14px rgba(16,40,70,.12); }
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
:root.univer-mobile #l4 tr.univer-app-table-caption>th { padding:12px 16px!important; background:#fff!important; color:var(--u-ink)!important; font-size:15px!important; font-weight:500!important; text-align:start!important; }
:root.univer-mobile #l4 tr.univer-app-empty-row>td::before { content:''!important; display:inline-block!important; width:18px; height:18px; margin-right:10px; vertical-align:-4px; background:currentColor; opacity:.7; -webkit-mask:${inbox} center/contain no-repeat; }
/* Instruction files as document cards: a grid when the cell holds only files (nested blocks flatten into it),
   one card per file next to other text. */
:root.univer-mobile #l4 td.ct.univer-app-doc-links { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(280px,1fr)); gap:10px; padding:4px 0 12px!important; }
:root.univer-mobile #l4 td.ct.univer-app-doc-links :is(div,p,span,center) { display:contents!important; }
:root.univer-mobile #l4 td.ct.univer-app-doc-links br, :root.univer-mobile #l4 td.ct.univer-app-doc-cell a.univer-app-doc-link~br:not(center br) { display:none; }
:root.univer-mobile #l4 td.ct.univer-app-doc-cell a.univer-app-doc-link { width:min(100%,560px); margin:0 0 10px!important; }
:root.univer-mobile #l4 a.univer-app-doc-link { display:flex!important; align-items:center; gap:12px; min-height:56px; box-sizing:border-box; padding:10px 14px!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:500!important; line-height:1.35!important; text-decoration:none!important; overflow-wrap:anywhere; }
:root.univer-mobile #l4 a.univer-app-doc-link::before { content:''; flex:0 0 34px; height:34px; border-radius:8px; background:var(--u-brand-soft) ${doc} center/18px no-repeat; }
:root.univer-mobile #l4 a.univer-app-doc-link>img { display:none!important; }
:root.univer-mobile #l4 a.univer-app-doc-link:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }
/* Back link above the heading of a nested list; on the live page it shares a span with the heading link. */
:root.univer-mobile #l4 td.ct.univer-app-back-crumb, :root.univer-mobile #l4 .univer-app-back-row { display:flex!important; flex-direction:column; align-items:flex-start; gap:10px; font-size:0!important; }
:root.univer-mobile #l4 td.ct.univer-app-back-crumb :is(a:not(.univer-app-back-link),span:not(.univer-app-back-row),b) { font-size:var(--u-fs-title)!important; font-weight:500!important; color:var(--u-ink)!important; text-decoration:none!important; }
:root.univer-mobile #l4 a.univer-app-back-link { display:inline-flex!important; align-items:center; gap:6px; min-height:32px; box-sizing:border-box; padding:4px 12px 4px 10px!important; border:1px solid var(--u-line)!important; border-radius:6px!important; background:#fff!important; color:var(--u-muted)!important; font-size:13px!important; font-weight:500!important; text-decoration:none!important; cursor:pointer; }
:root.univer-mobile #l4 a.univer-app-back-link::before { content:'←'; font-weight:600; }
:root.univer-mobile #l4 a.univer-app-back-link:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }
:root.univer-mobile #l4 td.ct.univer-app-period, :root.univer-mobile #l4 td.ct.univer-app-period * { padding:8px 0 2px!important; text-align:start!important; color:var(--u-muted)!important; font-size:13px!important; font-weight:500!important;  }
/* Distance-course list: a narrow icon column, the name with its discussion count and an Open button. */
:root.univer-mobile #l4 .univer-app-forum-courses tr>:is(td,th):first-child { width:36px!important; min-width:36px!important; padding-right:0!important; }
:root.univer-mobile #l4 .univer-app-forum-courses tr>:is(td,th):first-child img { width:18px; height:auto; opacity:.85; }
:root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child { position:relative; padding-right:132px!important; color:var(--u-ink)!important; font-weight:500; }
:root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child>.univer-app-course-open { position:absolute; right:14px; top:50%; transform:translateY(-50%); min-width:0; padding:4px 12px!important; min-height:32px!important; }
:root.univer-mobile #l4 .univer-app-count-box { font-size:0!important; margin-left:8px; }
:root.univer-mobile #l4 .univer-app-count { display:inline-flex; align-items:center; justify-content:center; min-width:28px; height:22px; box-sizing:border-box; padding:0 8px; border-radius:6px; background:var(--u-brand-soft); color:var(--u-ink); font-size:12px; font-weight:600; }
:root.univer-mobile #l4 .univer-app-count[data-zero=true] { background:var(--u-surface-2); color:var(--u-muted); font-weight:500; }
@media (max-width:899px) {
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link { display:flex!important; align-items:center; gap:10px; padding:10px 14px!important; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:first-child { display:inline-flex!important; width:auto!important; min-width:0!important; flex:0 0 auto; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child { flex:1 1 auto; grid-column:auto!important; display:flex!important; align-items:center; flex-wrap:wrap; gap:8px; padding-right:0!important; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td:last-child>.univer-app-course-open { position:static; transform:none; margin-left:auto!important; }
 :root.univer-mobile #l4 .univer-app-forum-courses tr.link>td::before { display:none!important; }
}
/* A teacher's link to another site: it reads as a link and its arrow says it leaves the app. */
/* The file's download link covers its whole row; a link in the description stands above that cover. */
:root.univer-mobile #l4 a.univer-app-outside-link { position:relative; z-index:1; color:var(--u-brand)!important; text-decoration:underline!important; text-underline-offset:3px; overflow-wrap:anywhere; cursor:pointer; }
:root.univer-mobile #l4 a.univer-app-outside-link::after { content:''; display:inline-block; width:14px; height:14px; margin-left:6px; vertical-align:-2px; background:currentColor; -webkit-mask:${leave} center/contain no-repeat; }
:root.univer-mobile #l4 a.univer-app-outside-link:focus-visible { outline:2px solid var(--u-brand); outline-offset:2px; border-radius:4px; }
/* Search over a long course list; adapter row layouts would otherwise outrank the hidden attribute. */
:root.univer-mobile #l4 :is(.univer-app-forum-courses,.univer-app-courses) tr[hidden] { display:none!important; }
/* The search stands apart from the list it filters, as every other control row does. */
:root:root.univer-mobile #l4 .univer-app-list-filter { margin:8px 0 16px!important; }
:root.univer-mobile #l4 .univer-app-list-filter input[type=search] { display:block; width:min(100%,420px)!important; height:40px; padding-left:38px!important; background:#fff ${search} 12px center/18px no-repeat!important; }
/* A material section's heading folds the files under it: a chevron, the name and the number of files, one button. */
:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr>th[data-univer-fold], :root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold] { display:flex!important; align-items:center; gap:10px; box-sizing:border-box; min-height:44px; padding:10px 16px!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:600!important; line-height:1.35!important; text-align:start!important; cursor:pointer; user-select:none; transition:background-color .14s ease, border-radius .26s cubic-bezier(.4,0,.2,1); }
:root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold] { margin:12px 0 0!important; border:1px solid var(--u-line)!important; border-radius:10px!important; background:var(--u-surface-2)!important; }
:root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold=open] { border-radius:10px 10px 0 0!important; }
:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr>th[data-univer-fold]::before, :root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold]::before { content:''; flex:0 0 18px; height:18px; background:var(--u-muted); -webkit-mask:${fold} center/contain no-repeat; transition:transform .26s cubic-bezier(.22,1,.36,1); }
:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr>th[data-univer-fold=closed]::before, :root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold=closed]::before { transform:rotate(-90deg); }
:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr>th[data-univer-fold]::after, :root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold]::after { content:attr(data-univer-count); display:inline-flex; align-items:center; justify-content:center; box-sizing:border-box; min-width:26px; height:22px; margin-left:auto; padding:0 8px; border:1px solid var(--u-line); border-radius:999px; background:var(--u-surface); color:var(--u-muted); font-size:12px; font-weight:600; }
:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr>th[data-univer-fold]:hover, :root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold]:hover { background:var(--u-brand-soft)!important; }
:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr>th[data-univer-fold]:focus-visible, :root:root:root.univer-mobile #l4 td.tt h4[data-univer-fold]:focus-visible { outline:2px solid var(--u-brand); outline-offset:-2px; }
/* A folded row outranks the file cards' own display:grid!important (desktop.js), table form included. */
:root:root:root:root:root:root.univer-mobile #l4 [data-univer-folded-row]:not([data-univer-sliding]), :root:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr[data-univer-folded-row]:not([data-univer-sliding]) { display:none!important; }
/* A row sliding shut or open (slideRow): --univer-slide scales its height, vertical padding, margins and borders, and it fades
   in the first part of a fold and the last part of an unfold. Its links take no clicks until it settles. */
:root:root:root:root:root:root:root:root.univer-mobile #l4 [data-univer-sliding] { box-sizing:border-box!important; height:calc(var(--univer-slide-h,0px) * var(--univer-slide,1))!important; min-height:0!important; max-height:none!important; padding-top:calc(var(--univer-slide-pt,0px) * var(--univer-slide,1))!important; padding-bottom:calc(var(--univer-slide-pb,0px) * var(--univer-slide,1))!important; margin-top:calc(var(--univer-slide-mt,0px) * var(--univer-slide,1))!important; margin-bottom:calc(var(--univer-slide-mb,0px) * var(--univer-slide,1))!important; border-top-width:calc(var(--univer-slide-bt,0px) * var(--univer-slide,1))!important; border-bottom-width:calc(var(--univer-slide-bb,0px) * var(--univer-slide,1))!important; overflow:hidden!important; opacity:calc(var(--univer-slide,1) * 1.6 - .6)!important; pointer-events:none!important; }
/* «Скачать все» at the end of a files section's heading row, beside the heading button (the row's own heading cell is
   found by th:only-of-type, so the button does not change it); the heading leaves it room. Centred by margins: a press
   scales buttons with transform. */
:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr:has(>button.univer-app-fold-all) { position:relative!important; }
:root:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr:has(>button.univer-app-fold-all)>th[data-univer-fold] { padding-right:196px!important; }
:root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all { position:absolute!important; top:0!important; bottom:0!important; right:12px!important; z-index:1; display:inline-flex!important; align-items:center; justify-content:center; gap:6px; min-width:0!important; box-sizing:border-box; width:auto!important; height:32px!important; min-height:0!important; margin:auto 0!important; padding:0 12px!important; border:1px solid var(--u-line)!important; border-radius:8px!important; background:var(--u-surface)!important; color:var(--u-brand)!important; font:inherit; font-size:13px!important; font-weight:500!important; line-height:1!important; box-shadow:none!important; cursor:pointer; }
:root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all::before { content:''; flex:0 0 16px; height:16px; background:currentColor; -webkit-mask:${save} center/contain no-repeat; }
:root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all[data-univer-busy]::before { -webkit-mask-image:${halt}; }
:root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all::after { content:attr(data-label); white-space:nowrap; }
:root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; }
:root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all:focus-visible { outline:2px solid var(--u-brand); outline-offset:2px; }
@media (max-width:599px) {
 :root:root:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr:has(>button.univer-app-fold-all)>th[data-univer-fold] { padding-right:60px!important; }
 :root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all { width:36px!important; padding:0!important; justify-content:center; }
 :root:root:root:root:root:root.univer-mobile #l4 button.univer-app-fold-all::after { display:none; }
}
:root.univer-mobile #l4 .univer-app-list-empty { margin:10px 2px 0!important; color:var(--u-muted); font-size:var(--u-fs-meta); }
/* A file added since the last visit: a quiet tag after its name, outside the link the app reads the file type from. */
:root.univer-mobile #l4 .univer-app-file-new { display:inline-flex; align-items:center; height:20px; margin:0 0 0 8px; padding:0 8px; box-sizing:border-box; border-radius:6px; background:var(--u-brand-soft); color:var(--u-brand); font-size:12px; font-weight:600; line-height:1; vertical-align:middle; white-space:nowrap; }
/* Printed syllabus contacts, on screen only: the site floats every block of its sheet (#content div), so a value row could
   spill out of its cell, under the next row's hover colour and border. Here the rows are in flow and laid out as a grid. */
@media screen {
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row { display:grid!important; grid-template-columns:minmax(0,1.2fr) minmax(0,1fr) minmax(0,1.3fr); gap:4px 16px; position:static!important; float:none!important; width:auto!important; min-width:0; box-sizing:border-box; margin:0!important; padding:8px 0!important; border-top:1px solid var(--u-line,#dfe5ec); }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row.univer-app-contact-head { padding-top:0!important; border-top:0; }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row.univer-app-contact-head+.input-row { border-top:0; }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row.univer-app-contact-head :is(b,strong) { color:var(--u-muted,#5b6b7c); font-size:12px!important; font-weight:600; line-height:1.4!important; }
 :root.univer-mobile :is(td,th).univer-app-contacts { max-width:none!important; }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row>.input-field { display:block!important; position:static!important; float:none!important; width:auto!important; min-width:0; margin:0!important; overflow-wrap:anywhere; user-select:text; }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row.univer-app-contact-empty { display:none!important; }
 :root.univer-mobile :is(td,th).univer-app-contacts a.univer-app-contact-link { color:var(--u-brand,#1b5fa6)!important; text-decoration:underline; text-decoration-color:var(--u-brand-line,#bfd3ea); text-underline-offset:3px; }
 :root.univer-mobile :is(td,th).univer-app-contacts a.univer-app-contact-link:hover { text-decoration-color:currentColor; }
 :root.univer-mobile :is(td,th).univer-app-contacts a.univer-app-contact-link[href^="tel:"] { white-space:nowrap; }
 :root.univer-mobile :is(td,th).univer-app-contacts .univer-app-contact-none { color:var(--u-muted,#5b6b7c); font-size:14px!important; }
 :root.univer-mobile button.univer-app-copy { display:inline-flex; flex:none; align-items:center; gap:4px; box-sizing:border-box; min-width:28px; height:28px; min-height:0!important; margin:0 0 0 6px!important; padding:0 6px!important; vertical-align:middle; border:1px solid var(--u-line,#dfe5ec)!important; border-radius:6px!important; background:transparent!important; color:var(--u-muted,#5b6b7c)!important; font-family:inherit; font-size:12px!important; font-weight:500; line-height:1!important; cursor:pointer; user-select:none; transition:color .15s ease, border-color .15s ease, background-color .15s ease; }
 :root.univer-mobile button.univer-app-copy::before { content:''; flex:none; width:15px; height:15px; background:currentColor; -webkit-mask:${copy} center/contain no-repeat; mask:${copy} center/contain no-repeat; }
 :root.univer-mobile button.univer-app-copy:hover { border-color:var(--u-brand-line,#bfd3ea)!important; color:var(--u-brand,#1b5fa6)!important; }
 :root.univer-mobile button.univer-app-copy:focus-visible { outline:2px solid var(--u-brand,#1b5fa6); outline-offset:2px; }
 :root.univer-mobile button.univer-app-copy.univer-app-copied { border-color:var(--univer-band-good-line,#9fd3b4)!important; color:var(--univer-band-good-ink,#1d6b40)!important; }
 :root.univer-mobile button.univer-app-copy.univer-app-copied::before { -webkit-mask-image:${check}; mask-image:${check}; }
 :root.univer-mobile button.univer-app-copy .univer-app-copy-label { font-size:12px!important; line-height:1!important; }
 :root.univer-mobile button.univer-app-copy .univer-app-copy-label:empty { display:none; }
 :root.univer-mobile .univer-app-copy-status { position:fixed; width:1px; height:1px; overflow:hidden; clip-path:inset(50%); white-space:nowrap; }
}
@media screen and (max-width:599px) {
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row { grid-template-columns:minmax(0,1fr); gap:6px; }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row.univer-app-contact-head { display:none!important; }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row>.input-field[data-univer-contact-label]::before { content:attr(data-univer-contact-label); display:block; color:var(--u-muted,#5b6b7c); font-size:12px; font-weight:600; line-height:1.4; }
 :root.univer-mobile :is(td,th).univer-app-contacts>.input-row>.input-field:first-child { font-weight:600; }
}
@media print { .univer-app-copy, .univer-app-copy-status, .univer-app-contact-none { display:none!important; } a.univer-app-contact-link { color:inherit!important; text-decoration:none!important; } }
`;
    }
    function apply(lang) {
        language = lang || 'ru';
        if (document.readyState === 'loading' || !/^\/(student|ssc|my)(\/|$)/i.test(location.pathname)) return;
        enabled = true;
        if (!style || !style.isConnected) { style = document.createElement('style'); style.id = 'univer-pages-style'; style.textContent = css(); (document.head || document.documentElement).appendChild(style); }
        grades(); notes(); captions(); documents(); crumbs(); periods(); memos(); courses(); outside(); folds(); freshFiles(); contacts();
        owns.forEach(function (record) { if (record.tag && record.tag.textContent !== ownText()) record.tag.textContent = ownText(); });
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
        links.forEach(function (link) { var parent = link.parentNode; if (parent) { link.replaceWith(...link.childNodes); parent.normalize(); } }); links = [];
        hinted.forEach(function (link) { link.removeAttribute('title'); }); hinted.clear();
        freshMarks.forEach(function (mark) { mark.remove(); }); freshMarks = [];
        releaseContacts();
        Array.from(slides.keys()).forEach(stopSlide);
        owns.forEach(unmarkOwn); owns.clear();
        blanks.forEach(function (box) { box.removeAttribute('data-univer-empty-section'); }); blanks.clear();
        sections.forEach(function (record, head) {
            head.removeEventListener('click', record.toggle); head.removeEventListener('keydown', record.toggle);
            ['role','tabindex','aria-expanded','data-univer-fold','data-univer-count'].forEach(function (name) { head.removeAttribute(name); });
            record.items.forEach(function (item) { item.removeAttribute('data-univer-folded-row'); });
        }); sections.clear();
        var names = ['univer-app-outside-link','univer-app-grades','univer-app-callout','univer-app-table-caption','univer-app-doc-links','univer-app-doc-cell','univer-app-doc-link','univer-app-back-crumb','univer-app-back-link','univer-app-back-row','univer-app-period','univer-app-memo','univer-app-forum-courses','univer-app-contacts','univer-app-contact-head','univer-app-contact-empty'];
        marked.forEach(function (node) { node.classList.remove(...names); if (!node.getAttribute('class')) node.removeAttribute('class'); }); marked.clear();
    }
    window.__univerPagesV1 = {apply:apply, release:release, downloading:downloading};
})();
