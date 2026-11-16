/* Finish for the site's plain pages: patterns keyed on the site's own markup (info notes, coloured warnings, period
   arrows, pagers, label and value tables, empty tables, secondary buttons) plus short recipes for named routes. The same
   file runs on Windows and Android. The site's nodes, values and handlers stay; release() puts every change back. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerPolishV1) return;
    var phone = /Android/.test(navigator.userAgent);
    var language = 'ru', enabled = false, style = null, entered = false, calc = null;
    var classes = new Map(), attributes = new Map(), texts = new Map(), mirrors = new Map(), empties = new Map(), clamped = new Map();
    var added = [], listeners = [], relabels = [], wraps = [];

    function words() {
        return language === 'en' ? {more:'Show in full', less:'Collapse', prev:'Previous page', next:'Next page', none:'Not provided', empty:'No records yet',
                open:'Open', answer:'Answer', done:'Answered', waiting:'Not answered', now:'Now', add:'Add a course'} :
            language === 'kz' ? {more:'Толығымен көрсету', less:'Жию', prev:'Алдыңғы бет', next:'Келесі бет', none:'Көрсетілмеген', empty:'Әзірге жазба жоқ',
                open:'Ашу', answer:'Жауап беру', done:'Жауап берілді', waiting:'Жауап берілмеген', now:'Қазір', add:'Пән қосу'} :
            {more:'Показать полностью', less:'Свернуть', prev:'Предыдущая страница', next:'Следующая страница', none:'Не указано', empty:'Записей пока нет',
                open:'Открыть', answer:'Ответить', done:'Ответ дан', waiting:'Нет ответа', now:'Сейчас', add:'Добавить дисциплину'};
    }
    // Old Android WebViews throw on :is() inside a selector string, so script selectors spell their lists out:
    // sel('td:is(.ct,.tt)>p') is 'td.ct>p,td.tt>p'. The style sheet keeps :is(), where an unknown rule is only dropped.
    function sel(pattern) {
        var m = /:is\(([^()]*)\)/.exec(pattern);
        if (!m) return pattern;
        return m[1].split(',').map(function (part) { return sel(pattern.slice(0, m.index) + part + pattern.slice(m.index + m[0].length)); }).join(',');
    }
    var NOTE_ICONS = sel('#l4 td:is(.ct,.tt)>img:first-child'), ALERT_NODES = sel('#l4 td:is(.ct,.tt) :is(center,p,div,font,span,b,strong)'),
        ALERT_PARENT = sel('td :is(center,p,div,font,span,b,strong)'), ARROWS = sel('#l4 a>img:is([src$="/left.png" i],[src$="/right.png" i])'),
        BUTTONS = sel('#l4 :is(input[type=button],input[type=reset],button)'), EMPTY_LINES = sel('#l4 td:is(.ct,.tt)>:is(p,div,span)'),
        FACT_TABLES = sel('#l4 table:is(.univer-app-pairs,.univer-app-nav-table,.univer-app-form-table,.univer-app-calendar-meta)'),
        MOMENTS = sel('#l4 :is(.up-facts,.up-note,.up-gpa-result,table.up-mon tr.news,.up-alert,.up-tip)');
    // The style sheet needs :has() and cascade layers; without them the mirrors would stand beside the tables they replace.
    var capable = !!(window.CSS && CSS.supports && CSS.supports('selector(:has(a))'));
    function clean(node) { return (node && node.textContent || '').replace(/\s+/g, ' ').trim(); }
    function ui() { return window.__univerMotionUI; }
    // Only a real pointer click animates; scripted and keyboard clicks change state at once.
    function moving(event) { return !!event && event.detail > 0; }

    // Every change goes through these helpers, so release() can undo exactly what was done.
    function mark(node, name) {
        if (node.classList.contains(name)) return;
        node.classList.add(name);
        var set = classes.get(node); if (!set) classes.set(node, set = new Set()); set.add(name);
    }
    function unmark(node, name) { var set = classes.get(node); if (set && set.delete(name)) node.classList.remove(name); }
    function attr(node, name, value) {
        var saved = attributes.get(node); if (!saved) attributes.set(node, saved = {});
        if (!(name in saved)) saved[name] = node.getAttribute(name);
        if (value === null) { if (node.hasAttribute(name)) node.removeAttribute(name); }
        else if (node.getAttribute(name) !== value) node.setAttribute(name, value);
    }
    function hide(node, on) { if (on === false && !attributes.has(node)) return; attr(node, 'data-up-hidden', on === false ? null : ''); }
    function hidden(node) { return node.hasAttribute('data-up-hidden'); }
    // A node someone else rewrote since (the layout blanks a crumb slash) starts a new record from its new text.
    function retext(node, value) {
        var saved = texts.get(node); if (!saved || node.nodeValue !== saved.wrote) texts.set(node, saved = {original:node.nodeValue});
        saved.wrote = value; if (node.nodeValue !== value) node.nodeValue = value;
    }
    function listen(node, type, handler) { node.addEventListener(type, handler); listeners.push([node, type, handler]); }
    function create(tag, name) { var node = document.createElement(tag); node.className = name; added.push(node); return node; }
    function label(node, write) {
        var i = relabels.findIndex(function (r) { return r[0] === node; });
        if (i < 0) relabels.push([node, write]); else relabels[i] = [node, write];
        write();
    }
    function drop(node) { node.remove(); var i = added.indexOf(node); if (i >= 0) added.splice(i, 1); }
    // Wraps a cell's only text node in a span; release() puts the text node back.
    function wrapText(cell, name) {
        var existing = cell.querySelector(':scope>.' + name); if (existing) return existing;
        var nodes = Array.from(cell.childNodes).filter(function (n) { return n.nodeType !== 3 || n.nodeValue.trim(); });
        if (nodes.length !== 1 || nodes[0].nodeType !== 3) return null;
        var span = document.createElement('span'); span.className = name;
        cell.insertBefore(span, nodes[0]); span.appendChild(nodes[0]); wraps.push(span);
        return span;
    }
    // A table alone in the layout's scroll box is hidden together with the box.
    function holderOf(table) { var box = table.parentElement; return box && box.matches('.univer-app-table-scroll') && box.children.length === 1 ? box : table; }
    // The layout may put a table into that box after an earlier pass hid the table, so the mark moves up to the box, and
    // showing the table again clears both, also once the box has taken in the sort button.
    function conceal(table, on) {
        var target = holderOf(table), box = table.parentElement;
        if (target !== table) hide(table, false);
        if (on !== false) { hide(target); return target; }
        hide(target, false);
        if (box && box.matches('.univer-app-table-scroll')) hide(box, false);
        return target;
    }
    // Red and green text on the site means "warning" and "all set". The hue survives the dark theme's recolouring.
    function tone(node) {
        var m = /rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/.exec(getComputedStyle(node).color);
        if (!m || m[4] === '0') return '';
        var r = m[1] / 255, g = m[2] / 255, b = m[3] / 255, max = Math.max(r, g, b), d = max - Math.min(r, g, b);
        if (d < 0.25) return '';
        var h = (max === r ? ((g - b) / d + 6) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4) * 60;
        return h < 16 || h > 340 ? 'warn' : h > 85 && h < 165 ? 'tip' : '';
    }

    // "(i)" notes: the site's info icon at the start of a text cell.
    function notes() {
        document.querySelectorAll(NOTE_ICONS).forEach(function (icon) {
            var cell = icon.parentElement;
            if (!/\/icons\/info\.png$/i.test(icon.getAttribute('src') || '') || cell.querySelector('input:not([type=hidden]),select,textarea') || !clean(cell)) return;
            mark(cell, cell.querySelector('table') ? 'up-lead' : 'up-note');
        });
    }
    var ALERT_SKIP = '#tools,details,a,button,label,select,option,.univer-app-info,.univer-app-callout,.univer-app-cards,.univer-app-grade,.igpa-ui,.up-note,.up-alert,.up-tip,.up-facts';
    // Whole red or green sentences become a warning or a tip box; a coloured word inside a sentence stays as it is.
    function alerts() {
        document.querySelectorAll(ALERT_NODES).forEach(function (node) {
            if (node.closest(ALERT_SKIP) || /(^|\s)univer-app/.test(node.className) || node.querySelector('table,input,select,textarea,img,video,iframe')) return;
            var value = clean(node); if (value.length < 24 || value.length > 700) return;
            var kind = tone(node), parent = node.parentElement; if (!kind) return;
            if (parent.matches(ALERT_PARENT) && !/(^|\s)univer-app/.test(parent.className) && tone(parent) === kind && clean(parent).length <= 700) return;
            if (/^(FONT|SPAN|B|STRONG)$/.test(node.tagName) && clean(parent).length > value.length * 1.25) return;
            mark(node, kind === 'warn' ? 'up-alert' : 'up-tip');
        });
        document.querySelectorAll('#l4 .univer-app-form-table td>label:not([for])').forEach(function (node) {
            if (node.classList.contains('up-alert') || node.querySelector('input,select,textarea,img') || clean(node).length < 24 || tone(node) !== 'warn') return;
            mark(node, 'up-alert');
        });
    }
    // Period and page arrows: the site's left.png/right.png images become arrows drawn in text colour.
    function arrows() {
        document.querySelectorAll(ARROWS).forEach(function (icon) {
            var link = icon.parentElement, left = /left\.png$/i.test(icon.getAttribute('src'));
            mark(link, left ? 'up-prev' : 'up-next');
            if (!clean(link)) label(link, function () { attr(link, 'aria-label', words()[left ? 'prev' : 'next']); });
            var pager = link.parentElement; if (pager && pager.matches('td.tb')) mark(pager, 'up-pager');
        });
    }
    function textNodes(root) {
        var list = [], walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), node;
        while ((node = walker.nextNode())) if (node.nodeValue.trim()) list.push(node);
        return list;
    }
    // Small text fixes, each worked out from the site's original text so repeated passes always give the same result:
    // no stray final period in headings ("Договоры ."; initials such as "А. М." keep theirs), no space before a label's
    // colon, no "/" after the Back crumb, no "^" and "#" in front of link text, and one footer line.
    var ABBR = /^(РК|ҚР|МОН|СДО|ДОТ|ОӘК|ЖОО|ВУЗ|КАЗНУ|ҚАЗҰУ|ФИО|ИИН|ЖСН|БИН|ЭЦП|ЭЦҚ|ИУП|ЖОЖ|УМКД|СРО|СРОП|СРСП|СРС|СӨЖ|ОЖСӨЖ|ЕНТ|ҰБТ|ЦОН|ХҚКО|ВКР|ГЭК|ОП|БББ|ОР)$/;
    // "ВНИМАНИЕ! ЭТО ФОТО" reads as "Внимание! Это фото"; Latin abbreviations (GPA, FX) and known Cyrillic ones stay.
    // The text nodes of one element share a state ({start, loud}), so "СДАЙТЕ ДО <font>20 ДЕКАБРЯ</font> ВКЛЮЧИТЕЛЬНО!"
    // stays one sentence; "loud" says the whole element shouts, so a short node in it ("НЕ") follows along.
    var LETTER = /^[A-ZА-ЯЁӘІҢҒҮҰҚӨҺ]$/, PAIR = /^[A-ZА-ЯЁӘІҢҒҮҰҚӨҺ]\.\s?[A-ZА-ЯЁӘІҢҒҮҰҚӨҺ]\./, MARKS = /[,;:()«»"]/g;
    function shouts(text) { return !/[a-zа-яёәіңғүұқөһ]/.test(text) && /[A-ZА-ЯЁӘІҢҒҮҰҚӨҺ]{3}/.test(text); }
    function sentence(text, state) {
        state = state || {start:true};
        if (/[a-zа-яёәіңғүұқөһ]/.test(text) || !(shouts(text) || state.loud)) {
            if (/[!?.]\s*$/.test(text)) state.start = true; else if (/[^\s!?.]/.test(text)) state.start = false;
            state.chain = false; state.afterDot = /\.$/.test(text);
            return text;
        }
        var result = text.replace(/[^\s!?.]+|[!?.]+/g, function (word, offset) {
            if (/^[!?.]+$/.test(word)) {
                state.chain = !!state.initial && word === '.';
                if (!state.chain) state.start = true;
                state.initial = false; return word;
            }
            // Initials ("ИВАНОВ И.И.", "(Ф.И.О)") keep their capitals and their periods do not start a sentence; a lone
            // letter with a period ("2026 Г.", "Г. АЛМАТЫ") is an ordinary word, and so is one after a space ("А.Б. В …").
            var lead = word.length - word.replace(/^[(«"]+/, '').length, single = LETTER.test(word.replace(MARKS, ''));
            var dot = text.charAt(offset + word.length) === '.';
            state.initial = single && dot && (state.chain || PAIR.test(text.slice(offset + lead)));
            // The period may close the previous node ("Ф.И.<font>О</font>").
            var tail = single && !dot && state.chain && (offset > 0 ? text.charAt(offset - 1) === '.' : !!state.afterDot);
            state.chain = false;
            if (state.initial || tail) { state.start = false; return word; }
            // "ИИН/БИН" is two words.
            var out = word.split('/').map(function (part, i) {
                var core = part.replace(MARKS, '');
                if (/^[A-Z0-9-]+$/.test(core) || ABBR.test(core)) return core === 'КАЗНУ' ? part.replace(core, 'КазНУ') : core === 'ҚАЗҰУ' ? part.replace(core, 'ҚазҰУ') : part;
                var low = part.toLocaleLowerCase();
                return state.start && i === 0 ? low.charAt(0).toLocaleUpperCase() + low.slice(1) : low;
            }).join('/');
            state.start = false; return out;
        });
        state.afterDot = /\.$/.test(text);
        return result;
    }
    // "Образцова Б. Н.": the initials stay on one line with the surname.
    function initials(raw) {
        return raw.length > 60 || !/\s[A-ZА-ЯЁӘІҢҒҮҰҚӨҺ]\./.test(raw) ? raw : raw.replace(/(\S)\s+(?=[A-ZА-ЯЁӘІҢҒҮҰҚӨҺ]\.)/g, '$1\u00a0');
    }
    // The site's own text of a node, and the nodes the site wrote text into: a node this module emptied ("Договоры .")
    // still counts, so the next pass picks the same nodes and changes nothing.
    function original(node) { var saved = texts.get(node); return saved && node.nodeValue === saved.wrote ? saved.original : node.nodeValue; }
    function sourced(root) {
        var list = [], walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), node;
        while ((node = walker.nextNode())) if (original(node).trim()) list.push(node);
        return list;
    }
    function sourceLength(root) {
        var text = '', walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT), node;
        while ((node = walker.nextNode())) text += original(node);
        return text.replace(/\s+/g, ' ').trim().length;
    }
    function titles() {
        var want = new Map();
        var base = function (node) { return want.has(node) ? want.get(node) : original(node); };
        document.querySelectorAll('#l4 tr.top>td.ct').forEach(function (cell) {
            var nodes = sourced(cell); if (!nodes.length) return;
            // Only the slash right after the Back link goes; a later one separates the crumb's own parts.
            var back = cell.querySelector('a.univer-app-back-link'), first = back && back.nextSibling;
            if (first && first.nodeType === 3 && original(first).trim()) want.set(first, base(first).replace(/^\s*\/\s*/, ''));
            cell.querySelectorAll(':scope>a').forEach(function (a) {
                var node = sourced(a)[0]; if (!node || !/^\s*\^/.test(base(node))) return;
                mark(a, 'up-uplink');
                var raw = base(node).replace(/^\s*\^\s*/, ''); want.set(node, raw.charAt(0).toLocaleUpperCase() + raw.slice(1));
            });
            if (cell.querySelector(':scope>a.up-uplink')) cell.querySelectorAll(':scope>a:not(.up-uplink)').forEach(function (a) { mark(a, 'up-title-link'); });
            var last = nodes[nodes.length - 1];
            if (sourceLength(cell) <= 90) want.set(last, base(last).replace(/(\s\.|([^\s.]{3,})\.)\s*$/, '$2'));
        });
        document.querySelectorAll('#l4 label').forEach(function (cell) {
            var nodes = sourced(cell), last = nodes[nodes.length - 1];
            if (last && sourceLength(cell) <= 90) want.set(last, base(last).replace(/\s+:(\s*)$/, ':$1'));
        });
        document.querySelectorAll('#l4 .univer-app-semester-nav a').forEach(function (a) {
            var node = sourced(a)[0]; if (node) want.set(node, base(node).replace(/^\s*#\s*/, ''));
        });
        // Bold capitals in a notice panel ("ВНИМАНИЕ! ПАМЯТКА ПО …") also read as a sentence. Names and course codes in
        // capitals stay: a notice line has a "!" or five words or more, and belongs to the panel's own cell, not
        // to a table nested in it.
        document.querySelectorAll('#l4 label.up-alert, #l4 td>b:first-child, #l4 table.mt tr.mid>td.ct b').forEach(function (node) {
            var cell = node.closest('td,th'), parts = lines(node, cell);
            var line = parts.map(function (p) { return (p.fresh || p.pad ? ' ' : '') + base(p.node); }).join('').replace(/\s+/g, ' ').trim();
            if (node.tagName === 'B') {
                var outer = node.parentElement.closest('b');
                if (outer && cell.contains(outer)) return;
                var notice = cell.matches('table.mt tr.mid>td.ct') && cell.closest('table').classList.contains('mt') && (/!/.test(line) || line.split(' ').length >= 5);
                if (!notice && !(node.matches('td>b:first-child') && cell.querySelector(':scope>ol,:scope>ul'))) return;
            }
            var state = {start:true, loud:shouts(line)};
            parts.forEach(function (p) {
                if (p.fresh) { state.start = true; state.chain = false; state.afterDot = false; }
                else if (p.pad) state.afterDot = false;
                want.set(p.node, sentence(base(p.node), state));
            });
        });
        document.querySelectorAll('#l4 td').forEach(function (cell) {
            var node = cell.firstChild;
            if (cell.childElementCount || !node || node !== cell.lastChild || node.nodeType !== 3) return;
            var raw = base(node), value = initials(raw); if (value !== raw) want.set(node, value);
        });
        document.querySelectorAll('#l4 table.univer-app-gpa-layout table:not(#gpa-table) td').forEach(function (cell) {
            textNodes(cell).forEach(function (n) { if (/""/.test(base(n))) want.set(n, base(n).replace(/""/g, '"')); });
        });
        var copyright = document.querySelector('#l5 #copyright td.center');
        if (copyright) {
            mark(copyright, 'up-copyright');
            Array.from(copyright.childNodes).forEach(function (n) { if (n.nodeType === 3 && /^\s*\|\s*$/.test(base(n))) want.set(n, ' · '); });
        }
        want.forEach(function (value, node) { if (value !== node.nodeValue) retext(node, value); });
    }
    // The text nodes of an element that belong to its own cell (not to a table nested in it). A node is "fresh" when a
    // line break or a block edge (an empty block or a nested table too) stands before it, so the line starts with a
    // capital, and "pad" when only spaces stand before it, so the words on both sides stay apart.
    var BLOCK = /^(P|DIV|LI|OL|UL|TABLE|TBODY|TR|TD|TH|CENTER|H[1-6])$/;
    function lines(root, cell) {
        var out = [], walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT), n, gap = false, pad = false, last = null;
        while ((n = walker.nextNode())) {
            if (n.nodeType === 1) { if (n.tagName === 'BR' || BLOCK.test(n.tagName)) gap = true; continue; }
            if (n.parentElement.closest('td,th') !== cell) continue;
            if (!n.nodeValue.trim()) { pad = true; continue; }
            var block = root;
            for (var e = n.parentElement; e && e !== root; e = e.parentElement) if (BLOCK.test(e.tagName)) { block = e; break; }
            out.push({node:n, fresh:gap || (last !== null && block !== last), pad:pad}); gap = false; pad = false; last = block;
        }
        return out;
    }
    var SECONDARY = /^(назад|отмена|отменить|очистить|сбросить|закрыть|артқа|болдырмау|тазалау|жабу|back|cancel|clear|reset|close)$/i;
    // Back, Cancel and Clear stop looking like the main action next to them.
    function buttons() {
        document.querySelectorAll(BUTTONS).forEach(function (button) {
            if (/(^|\s)(univer-app|up-)/.test(button.className)) return;
            var value = (button.tagName === 'INPUT' ? button.value : clean(button)).trim();
            if (button.type === 'reset' || SECONDARY.test(value)) mark(button, 'up-secondary');
        });
    }
    // Instruction files listed as a pdf icon and a link become file cards.
    function files() {
        document.querySelectorAll('#l4 img[src$="/pdf.png" i]').forEach(function (img) {
            var link = img.nextElementSibling;
            if (!link || link.tagName !== 'A' || !clean(link) || /(^|\s)univer-app/.test(link.className)) return;
            hide(img); mark(link, 'up-file'); mark(link.parentElement, 'up-files');
        });
    }
    // "Инструкция для студентов Ссылка": a named document behind a generic link reads as a file card.
    var DOC = /\.(docx?|pdf|xlsx?|pptx?|rtf|odt)(\?|#|$)/i;
    function docLinks() {
        var keep = new Set();
        document.querySelectorAll('#l4 td.ct>div').forEach(function (div) {
            var kids = Array.from(div.children), link = kids.find(function (k) { return k.tagName === 'A'; }), name = kids.find(function (k) { return k.tagName === 'B' || k.tagName === 'STRONG'; });
            // Study pages lay out their own document links, and a link needs words to stand as a button.
            var ok = kids.length === 2 && !!link && !!name && DOC.test(link.getAttribute('href') || '') && !!clean(link) && clean(link).length <= 24 && !!clean(name) &&
                !/(^|\s)univer-app/.test(link.className + ' ' + div.className) && !div.closest('.univer-app-doc-links') &&
                !clean(div).replace(clean(name), '').replace(clean(link), '').trim();
            if (ok) { mark(div, 'up-doc'); mark(link, 'up-doc-link'); keep.add(div); keep.add(link); }
        });
        // A card that no longer qualifies, or a link moved out of its card, loses the look.
        document.querySelectorAll('.up-doc,.up-doc-link').forEach(function (node) { if (!keep.has(node)) { unmark(node, 'up-doc'); unmark(node, 'up-doc-link'); } });
    }
    // A panel of nothing but its heading ("Информация" with nothing under it) hides, and shows again once content arrives.
    // Inner panels go first, so an outer one holding only panels hidden here hides too; a heading with its own links or
    // controls (the site's "Назад" is a link without href) stays.
    var CONTENT = 'input,select,textarea,a[href],img,button,iframe,video,object,embed,canvas,svg,[id]';
    function shownIn(node, row) {
        var veil = (node.nodeType === 1 ? node : node.parentElement).closest('table.mt[data-up-hidden]');
        return !veil || veil === row || !row.contains(veil);
    }
    function blankRow(row) {
        return !textNodes(row).some(function (n) { return shownIn(n, row); }) && !Array.from(row.querySelectorAll(CONTENT)).some(function (n) { return shownIn(n, row); });
    }
    function emptyPanels() {
        Array.from(document.querySelectorAll('#l4 table.mt')).reverse().forEach(function (table) {
            if (table.closest('#tools,details')) return;
            var rows = Array.from(table.rows), head = rows.filter(function (r) { return r.classList.contains('top'); }), body = rows.filter(function (r) { return head.indexOf(r) < 0; });
            var blank = head.length === 1 && !!clean(head[0]) && !head[0].querySelector('a,select,button,input,textarea,[onclick]') && body.length > 0 && body.every(blankRow);
            if (blank) hide(table); else hide(table, false);
        });
    }
    // Profile card: an empty value says "Не указано" instead of leaving a blank row.
    function person() {
        document.querySelectorAll('#l4 table.univer-app-matrix td[rowspan]>img').forEach(function (img) {
            img.closest('table').querySelectorAll('td:not([rowspan])').forEach(function (cell) {
                if (cell.querySelector('li,img,.up-none') || clean(cell)) return;
                var none = create('span', 'up-none'); cell.appendChild(none);
                label(none, function () { none.textContent = words().none; });
            });
        });
    }
    // Short "nothing found" lines ("Договор не найден") read as an empty state.
    function emptyTexts() {
        document.querySelectorAll(EMPTY_LINES).forEach(function (node) {
            var value = clean(node);
            if (node.hidden || /(^|\s)univer-app/.test(node.className)) return;
            if (value && value.length < 70 && !node.children.length && /(не найден|нет данных|не заполнен|табылмады|деректер жоқ|not found|no data)/i.test(value)) mark(node, 'up-empty');
        });
    }

    // Label and value tables (advisor, profile, study plan details) are shown as a list of facts. The source table stays,
    // hidden, so the site's own scripts still find it; counts-only tables become stat tiles.
    function isLabel(cell) {
        if (cell.tagName === 'TH') return true;
        var items = cell.querySelectorAll(':scope>li'), value = clean(cell);
        if (items.length === 1 && value && clean(items[0]) === value) return true;
        return value.length > 0 && value.length <= 60 && /:$/.test(value);
    }
    function pairsOf(table) {
        if (table.querySelector('input:not([type=hidden]),select,textarea,button,form,iframe,video,table,img')) return null;
        var pairs = [];
        for (var row of Array.from(table.rows)) {
            var cells = Array.from(row.cells);
            for (var i = 0; i < cells.length; i++) {
                if (!isLabel(cells[i])) { if (clean(cells[i])) return null; continue; }
                var next = cells[i + 1], value = next && !isLabel(next) ? next : null;
                pairs.push({label:clean(cells[i]).replace(/\s*:\s*$/, ''), value:value});
                if (value) i++;
            }
        }
        return pairs.length >= 2 && pairs.length <= 60 ? pairs : null;
    }
    function facts() {
        // The student home already lays out its records, and course pages belong to the course tasks module, which places
        // its cards against the details table it finds there.
        if (/^\/student\/(bachelor|discussion)(\/|$)/i.test(location.pathname)) return;
        document.querySelectorAll(FACT_TABLES).forEach(function (table) {
            if (table.closest('#tools,details,.univer-app-info,.up-facts')) return;
            var record = mirrors.get(table), signature = table.textContent.replace(/\u00a0/g, ' ');
            if (record && record.box.isConnected && record.signature === signature) { conceal(table); return; }
            if (record) { drop(record.box); conceal(table, false); mirrors.delete(table); }
            var pairs = pairsOf(table); if (!pairs) return;
            var target = holderOf(table), numeric = pairs.length >= 3 && pairs.every(function (p) { return p.value && /^\d{1,4}$/.test(clean(p.value)); });
            var box = create('div', numeric ? 'up-facts up-stats' : 'up-facts');
            pairs.forEach(function (pair) {
                var item = document.createElement('div'), name = document.createElement('span'), value = document.createElement('span');
                item.className = numeric ? 'up-fact up-stat' : 'up-fact'; name.className = 'up-fact-label'; value.className = 'up-fact-value';
                name.textContent = sentence(pair.label);
                if (pair.value && clean(pair.value)) {
                    Array.from(pair.value.childNodes).forEach(function (n) {
                        if (n.nodeName === 'SCRIPT') return;
                        var copy = n.cloneNode(true); if (copy.nodeType === 3) copy.nodeValue = initials(copy.nodeValue);
                        value.appendChild(copy);
                    });
                    value.querySelectorAll('[id]').forEach(function (n) { n.removeAttribute('id'); });
                } else { value.classList.add('up-none'); label(value, function () { value.textContent = words().none; }); }
                if (numeric && /^0+$/.test(clean(pair.value))) item.classList.add('up-zero');
                item.append(name, value); box.appendChild(item);
            });
            target.before(box); conceal(table);
            mirrors.set(table, {box:box, signature:signature});
        });
    }
    // A table of nothing but column headings: hidden, and an empty state says so unless the page already does.
    function hasRows(table) {
        return Array.from(table.rows).some(function (r) { return !r.classList.contains('univer-app-card-header') && !r.querySelector(':scope>th') && (clean(r) || r.querySelector('input,select,textarea,a[href],img,button')); });
    }
    function emptyTables() {
        // Exam, Midterm and message pages keep the column headings and the AJAX result rows in separate tables, and the
        // layout says when the result is empty: there the headings only hide until rows arrive, with no note of our own.
        var split = /^\/student\/my(exam|midterm)\/schedule|^\/my\/messages\//i.test(location.pathname);
        // A result with merged rows becomes a matrix instead of cards, and its rows count too.
        var all = Array.from(document.querySelectorAll(split ? '#l4 table.univer-app-cards,#l4 table.univer-app-matrix' : '#l4 table.univer-app-cards')).filter(function (t) { return !t.closest('#tools,details,.univer-app-info') && t.id !== 'gpa-table'; });
        document.querySelectorAll('#l4 table.univer-app-cards').forEach(function (table) {
            if (table.closest('#tools,details,.univer-app-info') || table.id === 'gpa-table') return;
            table.querySelectorAll('tr.univer-app-card-section').forEach(function (r) { if (!clean(r) && !r.querySelector('input,select,a[href],img,button')) hide(r); });
            var target = holderOf(table), box = empties.get(table), cell = target.closest('td') || target.parentElement;
            var others = Array.from(cell.querySelectorAll('table.univer-app-cards')).filter(function (t) { return t !== table; });
            if (hasRows(table) || all.some(function (t) { return t !== table && hasRows(t); })) { conceal(table, false); if (box) { drop(box); empties.delete(table); } return; }
            conceal(table);
            if (split || box || cell.querySelector('.univer-app-empty-note,.univer-app-list-empty,.up-empty') || others.some(function (t) { return empties.has(t); })) return;
            box = create('div', 'up-empty'); label(box, function () { box.textContent = words().empty; });
            target.after(box); empties.set(table, box);
        });
    }
    // Long text folds to a few lines behind a "Show in full" button; the height change is animated.
    function clamp(box) {
        if (clamped.has(box)) return;
        mark(box, 'up-clamp');
        var shown = box.clientHeight; if (!shown) { unmark(box, 'up-clamp'); return; }
        if (box.scrollHeight <= shown + 12) { unmark(box, 'up-clamp'); clamped.set(box, null); return; }
        var button = create('button', 'up-more'); button.type = 'button'; button.setAttribute('aria-expanded', 'false');
        label(button, function () { button.textContent = words()[box.classList.contains('up-open') ? 'less' : 'more']; });
        listen(button, 'click', function (event) {
            var open = !box.classList.contains('up-open'), before = box.getBoundingClientRect().height;
            ui()?.stop(box);
            if (open) mark(box, 'up-open'); else unmark(box, 'up-open');
            button.setAttribute('aria-expanded', String(open)); button.textContent = words()[open ? 'less' : 'more'];
            if (moving(event)) ui()?.resize(box, before, open ? 240 : 200);
        });
        box.after(button); clamped.set(box, button);
    }

    // Route recipes.
    function mon() {
        document.querySelectorAll('#l4 tr.news').forEach(function (row) {
            var table = row.closest('table'); if (table) mark(table, 'up-mon');
            var body = row.querySelector('span.header+div'); if (body) clamp(body);
        });
    }
    function counts() {
        document.querySelectorAll('#l4 .univer-app-catalog>summary').forEach(function (summary) {
            if (summary.querySelector('.up-count')) return;
            var node = summary.firstChild; if (!node || node.nodeType !== 3) return;
            var match = /^(.*\S)\s*\((\d+)\s+\D.*\)\s*$/.exec(node.nodeValue); if (!match) return;
            retext(node, match[1]);
            var count = create('span', 'up-count'); count.textContent = match[2]; node.after(count);
        });
    }
    function filled(row) {
        return Array.from(row.querySelectorAll('input:not([type=hidden]),select')).some(function (c) { return c.tagName === 'SELECT' ? c.selectedIndex > 0 : !/^0?$/.test(c.value.trim()); });
    }
    // GPA calculator: the result stays in view, and spare empty rows wait behind "Add a course". The site may rebuild its
    // rows after load, so the rows are looked at again on every pass; a row the student asked for stays shown.
    var opened = new WeakSet();
    function calculator() {
        var table = document.getElementById('gpa-table'), result = document.getElementById('resultCell');
        if (!table || !result) return;
        var bar = result.parentElement; if (bar && !bar.contains(table)) mark(bar, 'up-gpa-result');
        var rows = Array.from(table.rows).filter(function (r) { return r.classList.contains('univer-app-card-row'); });
        if (!rows.length) return;
        var last = rows.reduce(function (n, r, i) { return filled(r) ? i : n; }, -1), keep = Math.max(5, last + 2);
        rows.forEach(function (r, i) { if (i >= keep && !filled(r) && !opened.has(r)) hide(r); else hide(r, false); });
        if (!calc || calc.table !== table || !calc.button.isConnected) {
            var button = create('button', 'up-add'); button.type = 'button';
            label(button, function () { button.textContent = words().add; });
            listen(button, 'click', function (event) {
                var next = calc.rows.find(hidden); if (!next) return;
                opened.add(next); hide(next, false); button.hidden = !calc.rows.some(hidden);
                if (moving(event)) ui()?.reveal(next, 'content');
                next.querySelector('input,select')?.focus({preventScroll:!moving(event)});
            });
            var shown = result.textContent;
            var check = function () { setTimeout(function () { if (result.textContent !== shown) { shown = result.textContent; ui()?.reveal(result, 'content'); } }, 0); };
            listen(table, 'change', check); listen(table, 'input', check);
            table.after(button);
            calc = {table:table, button:button};
        }
        calc.rows = rows; calc.button.hidden = !rows.some(hidden);
    }
    function openButton(row, key) {
        if (row.querySelector('.up-open-row')) return;
        var button = create('button', 'up-open-row'); button.type = 'button';
        label(button, function () { button.textContent = words()[key]; });
        listen(button, 'click', function (event) {
            event.preventDefault(); event.stopPropagation();
            row.click();
            // The site opens a row from its jQuery dblclick handler.
            row.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true, view:window}));
        });
        row.cells[row.cells.length - 1].appendChild(button);
    }
    // Questionnaires: an Open button on each survey, an answered/not answered chip and an Answer button on each subject.
    function rating() {
        document.querySelectorAll('#l4 table.univer-app-cards').forEach(function (table) {
            var head = table.querySelector('tr.univer-app-card-header'), rows = Array.from(table.querySelectorAll('tr.link[id]'));
            if (!head || !rows.length) return;
            var column = Array.from(head.cells).findIndex(function (c) { return /ответил|жауап|answered/i.test(clean(c)); });
            rows.forEach(function (row) {
                row.querySelectorAll('font,span:not([class]),b,p').forEach(function (n) { var v = clean(n); if (v && v.length <= 24 && tone(n) === 'warn') mark(n, 'up-badge'); });
                if (column < 0) { openButton(row, 'open'); return; }
                var done = row.classList.contains('done'), cell = row.cells[column];
                if (cell && !cell.querySelector('.up-chip')) {
                    cell.querySelectorAll(':scope>img').forEach(function (img) { hide(img); });
                    var chip = create('span', done ? 'up-chip up-chip-done' : 'up-chip up-chip-wait');
                    label(chip, function () { chip.textContent = words()[done ? 'done' : 'waiting']; });
                    cell.appendChild(chip);
                }
                if (!done) openButton(row, 'answer');
            });
        });
    }
    function exams() {
        document.querySelectorAll('#l4 tr.univer-app-card-row>td').forEach(function (cell) {
            var value = clean(cell), kind = /^(экзамен|емтихан|exam)/i.test(value) ? 'up-kind-exam' : /^(консультация|кеңес|consultation)/i.test(value) ? 'up-kind-consult' : '';
            var span = kind && wrapText(cell, 'up-kind'); if (span) mark(span, kind);
        });
    }
    // Academic calendar: the current period is marked, finished ones step back.
    function calendar() {
        var today = new Date(); today.setHours(0, 0, 0, 0);
        document.querySelectorAll('#l4 table.univer-app-cards tr.univer-app-card-row').forEach(function (row) {
            var dates = Array.from(row.cells).map(function (c) { return /^(\d{2})\.(\d{2})\.(\d{4})$/.exec(clean(c)); }).filter(Boolean)
                .map(function (m) { return new Date(+m[3], m[2] - 1, +m[1]); });
            if (dates.length < 2) return;
            if (dates[dates.length - 1] < today) { mark(row, 'up-past'); return; }
            if (dates[0] > today) return;
            mark(row, 'up-now');
            if (!row.querySelector('.up-chip-now')) { var chip = create('span', 'up-chip up-chip-now'); label(chip, function () { chip.textContent = words().now; }); row.cells[0].appendChild(chip); }
            var panel = row.closest('table.mt'), anchor = panel && panel.previousElementSibling;
            var name = anchor && anchor.matches('a[name^="sem_"]') ? anchor.getAttribute('name') : '';
            var link = name && document.querySelector('.univer-app-semester-nav a[href="#' + name + '"]');
            if (link) mark(link, 'up-current');
        });
    }

    // The first adapted view gets a short staggered entrance, played while the page is revealed rather than after it.
    function enter() {
        if (entered) return; entered = true;
        var root = document.documentElement;
        if (!window.__univerPaintV1?.pending && !root.hasAttribute('data-univer-app-enter')) return;
        var list = Array.from(document.querySelectorAll(MOMENTS)).slice(0, 8);
        if (!list.length) return;
        var start = Date.now();
        (function wait() {
            if (window.__univerPaintV1?.pending && Date.now() - start < 4000) { setTimeout(wait, 40); return; }
            list.forEach(function (node, i) { ui()?.reveal(node, 'tile', i * 32); });
        })();
    }

    function icon(path) {
        return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='black' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>" + path + '</svg>') + '")';
    }
    function css() {
        var P = ':root.univer-mobile', control = (phone ? 48 : 40) + 'px';
        var info = icon("<circle cx='12' cy='12' r='9'/><path d='M12 11v5'/><path d='M12 7.5h.01'/>"), alert = icon("<path d='M10.3 4.2 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0z'/><path d='M12 9.5v4'/><path d='M12 17h.01'/>");
        var check = icon("<circle cx='12' cy='12' r='9'/><path d='m8.5 12.5 2.5 2.5 4.5-5'/>"), chevron = icon("<path d='m6 9 6 6 6-6'/>"), plus = icon("<path d='M12 5v14'/><path d='M5 12h14'/>");
        var inbox = icon("<path d='M22 12h-6l-2 3h-4l-2-3H2'/><path d='M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1z'/>");
        var doc = icon("<path d='M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z'/><path d='M14 3v5h5'/><path d='M9 13h6'/><path d='M9 17h6'/>");
        return `
@layer univer-polish {
${P} [data-up-hidden] { display:none!important; }
/* Layered !important outranks the browser's own [hidden] rule, so everything styled here keeps hidden working. */
${P} :is(#l4,#l5) :is(.up-empty,.up-alert,.up-tip,.up-note,.up-prev,.up-next,.up-file,.up-pager,.up-gpa-result,.up-kind,.up-chip,.up-badge,.up-more,.up-add,.up-open-row,.up-facts)[hidden] { display:none!important; }
/* Boxes the site forces to show scroll bars scroll only when they need to. */
/* Only the message boxes: the layout already shows the schedule, course weeks and UMKD catalog boxes in full, and a rule in
   this layer would beat its !important. */
${P} #l4 div.scrool:is(.inbox,.mes) { height:auto!important; max-height:min(64vh,560px)!important; overflow:auto!important; }
${P} #l4 td.ts>a.min>img[src$="/cal.png" i] { display:none!important; }
${P} #l4 td.ts>a.min { border-radius:999px!important; padding-left:16px!important; padding-right:16px!important; }
${P} #l4 td.ts>a.min[style*="red" i] { border-color:var(--u-brand)!important; background:var(--u-brand)!important; color:#fff!important; }

/* Notes, warnings and tips. */
${P} #l4 td.up-note { display:block!important; position:relative!important; box-sizing:border-box!important; margin:0 0 16px!important; padding:14px 16px 14px 46px!important; border:1px solid var(--u-brand-line)!important; border-radius:12px!important; background:var(--u-brand-soft)!important; color:var(--u-text)!important; font-size:var(--u-fs-body)!important; line-height:1.6!important; text-align:left!important; }
${P} #l4 td.up-note :is(font,span,p,div,li) { font-size:inherit!important; line-height:inherit!important; color:inherit!important; }
${P} #l4 td.up-note :is(b,strong) { color:var(--u-ink)!important; font-weight:600!important; }
${P} #l4 td.up-note>img:first-child { display:none!important; }
${P} #l4 td.up-note::before { content:''!important; position:absolute!important; left:16px!important; top:16px!important; width:20px!important; height:20px!important; color:var(--u-brand)!important; background:currentColor!important; -webkit-mask:${info} center/contain no-repeat!important; }
${P} #l4 td.up-note>a#help_btn { display:inline-flex!important; align-items:center!important; gap:6px!important; color:var(--u-brand)!important; font-weight:500!important; text-decoration:none!important; }
${P} #l4 td.up-note>a#help_btn::after { content:''!important; width:16px!important; height:16px!important; background:currentColor!important; -webkit-mask:${chevron} center/contain no-repeat!important; transition:transform .2s var(--u-ease-out)!important; }
${P} #l4 td.up-note:has(>#help_div[style*="block"])>a#help_btn::after { transform:rotate(180deg)!important; }
@media (hover:hover) { ${P} #l4 td.up-note>a#help_btn:hover { text-decoration:underline!important; text-underline-offset:3px!important; } }
${P} #l4 label.up-alert { width:100%!important; }
${P} #l4 td.up-lead { color:var(--u-muted)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; line-height:1.6!important; text-align:left!important; }
${P} #l4 td.up-lead>img:first-child, ${P} #l4 td.up-lead>br { display:none!important; }
${P} #l4 td.up-lead::before { content:''!important; display:inline-block!important; width:20px!important; height:20px!important; margin-right:8px!important; vertical-align:-5px!important; background:var(--u-brand)!important; -webkit-mask:${info} center/contain no-repeat!important; }
${P} #l4 td.up-lead>b { display:block!important; margin-top:4px!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; line-height:1.6!important; }
${P} #l4 td.up-lead>:is(div,table) { margin-top:16px!important; }
${P} #l4 td.up-lead ol { margin:12px 0!important; padding-left:24px!important; }
${P} #l4 td.up-lead ol>li { margin:0 0 6px!important; padding:2px 0 2px 4px!important; border:0!important; }
${P} #l4 :is(.up-alert,.up-tip) { display:block!important; position:relative!important; box-sizing:border-box!important; margin:12px 0!important; padding:12px 16px 12px 44px!important; border-radius:12px!important; font-size:var(--u-fs-body)!important; font-weight:400!important; line-height:1.55!important; text-align:left!important; }
${P} #l4 .up-alert { background:#fdf0dc!important; color:#7a4300!important; }
${P} #l4 .up-tip { background:#e3f3ea!important; color:#166534!important; }
${P} #l4 :is(.up-alert,.up-tip) :is(font,span,b,strong,p) { color:inherit!important; font-size:inherit!important; }
${P} #l4 :is(.up-alert,.up-tip) a { color:inherit!important; font-weight:600!important; text-decoration:underline!important; text-underline-offset:3px!important; }
${P} #l4 :is(.up-alert,.up-tip)::before { content:''!important; position:absolute!important; left:14px!important; top:13px!important; width:20px!important; height:20px!important; background:currentColor!important; -webkit-mask:${alert} center/contain no-repeat!important; }
${P} #l4 .up-tip::before { -webkit-mask:${check} center/contain no-repeat!important; }
${P} #l4 .up-empty { display:flex!important; flex-direction:row!important; align-items:center!important; gap:10px!important; box-sizing:border-box!important; min-height:${phone ? '72px' : '96px'}!important; margin:8px 0 16px!important; padding:16px!important; border:1px dashed var(--u-line-strong)!important; border-radius:12px!important; background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; line-height:1.45!important; text-align:start!important; }
${P} #l4 tr.univer-app-empty-row>td>div:only-child { display:inline!important; float:none!important; }
${P} #l4 table.univer-app-form-table tr.mes { border-bottom:0!important; }
${P} #l4 .up-empty::before { content:''!important; flex:0 0 18px!important; width:18px!important; height:18px!important; opacity:.7!important; background:currentColor!important; -webkit-mask:${inbox} center/contain no-repeat!important; }

/* Period arrows and pagers. */
${P} #l4 a:is(.up-prev,.up-next) { display:inline-flex!important; align-items:center!important; justify-content:center!important; gap:8px!important; box-sizing:border-box!important; min-width:${control}!important; min-height:${control}!important; margin:0!important; padding:8px 14px!important; border:1px solid var(--u-line)!important; border-radius:10px!important; background:var(--u-surface)!important; color:var(--u-muted)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; line-height:1.3!important; text-decoration:none!important; white-space:normal!important; transition:background-color .15s var(--u-ease-out),border-color .15s var(--u-ease-out),color .15s var(--u-ease-out)!important; }
${P} #l4 a:is(.up-prev,.up-next)>img { display:none!important; }
${P} #l4 a.up-prev::before { content:'←'!important; font-weight:600!important; }
${P} #l4 a.up-next::after { content:'→'!important; font-weight:600!important; }
${P} #l4 .ts>a.up-next { margin-left:auto!important; }
${P} #l4 td.up-pager { border:0!important; background:none!important; background-image:none!important; box-shadow:none!important; display:flex!important; align-items:center!important; justify-content:center!important; gap:12px!important; padding:16px 0 4px!important; color:var(--u-muted)!important; font-size:var(--u-fs-body)!important; font-variant-numeric:tabular-nums!important; }
${P} #l4 td.up-pager>a:is(.up-prev,.up-next) { width:${control}!important; padding:0!important; }
@media (hover:hover) { ${P} #l4 a:is(.up-prev,.up-next):hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; } }

/* Facts and stat tiles. */
${P} #l4 .up-facts { display:grid!important; grid-template-columns:repeat(auto-fit,minmax(min(100%,420px),1fr))!important; box-sizing:border-box!important; width:100%!important; margin:0 0 16px!important; overflow:hidden!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:var(--u-surface)!important; }
${P} #l4 .up-fact { display:grid!important; grid-template-columns:minmax(112px,38%) minmax(0,1fr)!important; gap:16px!important; align-items:baseline!important; min-width:0!important; padding:12px 16px!important; box-shadow:0 1px 0 var(--u-line),1px 0 0 var(--u-line)!important; }
${P} #l4 .up-fact-label { color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; line-height:1.45!important; }
${P} #l4 .up-fact-value { min-width:0!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; line-height:1.45!important; overflow-wrap:anywhere!important; }
${P} #l4 .up-fact-value :is(b,strong,font,span,li) { color:inherit!important; font-size:inherit!important; font-weight:inherit!important; list-style:none!important; }
${P} #l4 .up-fact-value.up-none { color:var(--u-muted)!important; font-weight:400!important; }
${P} #l4 .up-stats { grid-template-columns:repeat(auto-fill,minmax(min(100%,280px),1fr))!important; }
${P} #l4 .up-stat { display:flex!important; align-items:baseline!important; justify-content:space-between!important; gap:12px!important; padding:10px 16px!important; }
${P} #l4 .up-stat .up-fact-label { color:var(--u-text)!important; font-size:var(--u-fs-body)!important; overflow-wrap:normal!important; word-break:normal!important; hyphens:manual!important; }
${P} #l4 .up-stat .up-fact-value { flex:0 0 auto!important; font-size:var(--u-fs-strong)!important; font-variant-numeric:tabular-nums!important; }
${P} #l4 .up-stat.up-zero .up-fact-value { color:var(--u-muted)!important; font-weight:400!important; }
@media (max-width:560px) { ${P} #l4 .up-fact:not(.up-stat) { grid-template-columns:minmax(0,1fr)!important; gap:2px!important; } }

/* Secondary buttons, folding text, counts, chips and row buttons. */
${P} #l4 .up-secondary { border:1px solid var(--u-brand-line)!important; background:var(--u-surface)!important; color:var(--u-brand)!important; box-shadow:none!important; }
@media (hover:hover) { ${P} #l4 .up-secondary:hover { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; color:var(--u-brand-strong)!important; } }
${P} #l4 .up-clamp:not(.up-open) { max-height:calc(1.6em * 4)!important; overflow:hidden!important; -webkit-mask-image:linear-gradient(#000 50%,transparent)!important; mask-image:linear-gradient(#000 50%,transparent)!important; }
${P} #l4 :is(.up-more,.up-add,.up-open-row,.up-doc-link,.univer-app-memo-toggle) { display:inline-flex!important; align-items:center!important; justify-content:center!important; gap:6px!important; box-sizing:border-box!important; width:auto!important; min-height:${phone ? '44px' : '40px'}!important; margin:8px 0 0!important; padding:6px 14px!important; border:1px solid var(--u-brand-line)!important; border-radius:10px!important; background:var(--u-surface)!important; color:var(--u-brand)!important; font:500 ${phone ? 'var(--u-fs-meta)' : 'var(--u-fs-body)'}/1.3 var(--u-font)!important; white-space:nowrap!important; cursor:pointer!important; transition:background-color .15s var(--u-ease-out),border-color .15s var(--u-ease-out)!important; }
${P} #l4 .up-doc-link { flex:0 0 auto!important; margin:0!important; text-decoration:none!important; }
${P} #l4 .univer-app-memo-toggle { margin:0!important; }
${P} #l4 td.univer-app-memo:not([data-univer-folded=true]) .univer-app-memo-toggle { display:flex!important; width:max-content!important; margin:12px 0 0!important; }
${P} #l4 td.univer-app-memo:not([data-univer-folded=true]) .univer-app-memo-toggle::after { transform:rotate(180deg)!important; }
${P} #l4 :is(.up-more,.univer-app-memo-toggle)::after { content:''!important; width:14px!important; height:14px!important; background:currentColor!important; -webkit-mask:${chevron} center/contain no-repeat!important; transition:transform .2s var(--u-ease-out)!important; }
${P} #l4 .up-more[aria-expanded=true]::after { transform:rotate(180deg)!important; }
${P} #l4 .up-add::before { content:''!important; width:16px!important; height:16px!important; background:currentColor!important; -webkit-mask:${plus} center/contain no-repeat!important; }
${P} #l4 .up-open-row { margin:0 0 0 12px!important; border-color:transparent!important; background:var(--u-brand)!important; color:#fff!important; }
@media (hover:hover) { ${P} #l4 :is(.up-more,.up-add,.up-doc-link,.univer-app-memo-toggle):hover { color:var(--u-brand)!important; border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; } ${P} #l4 .up-open-row:hover { background:var(--u-brand-strong)!important; } }
${P} :is(.up-count) { display:inline-flex!important; align-items:center!important; justify-content:center!important; box-sizing:border-box!important; min-width:24px!important; height:22px!important; margin-left:8px!important; padding:0 8px!important; border-radius:999px!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-size:var(--u-fs-caption)!important; font-weight:500!important; font-variant-numeric:tabular-nums!important; vertical-align:middle!important; }
${P} #l4 .up-chip { display:inline-flex!important; align-items:center!important; gap:6px!important; box-sizing:border-box!important; min-height:24px!important; padding:2px 10px!important; border-radius:999px!important; font-size:var(--u-fs-caption)!important; font-weight:500!important; line-height:1.3!important; white-space:nowrap!important; vertical-align:middle!important; }
${P} #l4 .up-chip-done { background:#e3f3ea!important; color:#166534!important; }
${P} #l4 .up-chip-wait { background:#fdf0dc!important; color:#7a4300!important; }
${P} #l4 .up-chip-now { margin-left:8px!important; background:var(--u-brand)!important; color:#fff!important; }
${P} #l4 .up-badge { display:inline-flex!important; align-items:center!important; margin:2px 0 0 8px!important; padding:2px 8px!important; border-radius:999px!important; background:#fdeceb!important; color:#b42318!important; font-size:var(--u-fs-caption)!important; font-weight:500!important; }

/* Ministry news: a date tile beside each item, the text folded to four lines. */
${P} #l4 table.up-mon, ${P} #l4 table.up-mon>tbody { display:block!important; width:100%!important; border:0!important; background:none!important; box-shadow:none!important; }
${P} #l4 table.up-mon tr.univer-app-card-header { display:none!important; }
${P} #l4 table.up-mon tr.news { display:grid!important; grid-template-columns:72px minmax(0,1fr)!important; gap:16px!important; box-sizing:border-box!important; margin:0 0 12px!important; padding:16px!important; border:1px solid var(--u-line)!important; border-radius:14px!important; background:var(--u-surface)!important; box-shadow:none!important; }
${P} #l4 table.up-mon tr.news>td { display:block!important; grid-column:auto!important; grid-row:auto!important; width:auto!important; min-width:0!important; padding:0!important; border:0!important; background:none!important; text-align:left!important; }
${P} #l4 table.up-mon tr.news>td::before { display:none!important; }
${P} #l4 table.up-mon .date { display:flex!important; flex-direction:column!important; align-items:center!important; justify-content:center!important; box-sizing:border-box!important; width:72px!important; height:72px!important; border-radius:12px!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-size:var(--u-fs-caption)!important; font-variant-numeric:tabular-nums!important; line-height:1.2!important; }
${P} #l4 table.up-mon .date .day { color:var(--u-brand)!important; font-size:24px!important; font-weight:500!important; line-height:1.1!important; }
${P} #l4 table.up-mon .date+div { box-sizing:border-box!important; width:72px!important; margin:8px 0 0!important; padding:0!important; background:none!important; font-size:var(--u-fs-caption)!important; text-align:center!important; }
${P} #l4 table.up-mon .date+div :is(span,font) { display:inline!important; padding:0!important; background:none!important; color:var(--u-muted)!important; font-size:var(--u-fs-caption)!important; overflow-wrap:anywhere!important; }
${P} #l4 table.up-mon span.header { display:block!important; }
${P} #l4 table.up-mon span.header a { color:var(--u-ink)!important; font-size:var(--u-fs-section)!important; font-weight:500!important; line-height:1.4!important; text-decoration:none!important; }
${P} #l4 table.up-mon span.header+div { margin-top:8px!important; color:var(--u-text)!important; font-size:var(--u-fs-body)!important; line-height:1.6!important; }
${P} #l4 table.up-mon span.header+div :is(p,span,font) { margin:0!important; color:inherit!important; font-size:inherit!important; line-height:inherit!important; }
@media (hover:hover) { ${P} #l4 table.up-mon span.header a:hover { color:var(--u-brand)!important; text-decoration:underline!important; } }

/* GPA calculator. */
${P} #l4 .univer-app-table-scroll:has(#gpa-table) { overflow:visible!important; }
${P} #l4 .up-gpa-result { position:sticky!important; bottom:12px!important; z-index:3!important; display:flex!important; align-items:baseline!important; gap:12px!important; box-sizing:border-box!important; width:fit-content!important; max-width:100%!important; margin:16px 0 0!important; padding:12px 20px!important; border:1px solid var(--u-brand-line)!important; border-radius:14px!important; background:var(--u-surface)!important; box-shadow:var(--u-shadow-pop)!important; }
${P} #l4 .up-gpa-result>b { color:var(--u-muted)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; }
${P} #l4 .up-gpa-result>#resultCell { display:inline-block!important; color:var(--u-brand)!important; font-size:28px!important; font-weight:600!important; line-height:1.1!important; font-variant-numeric:tabular-nums!important; }
${P} #l4 table.univer-app-gpa-layout table:not(#gpa-table) { width:100%!important; border-collapse:separate!important; border-spacing:0!important; overflow:hidden!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:var(--u-surface)!important; font-variant-numeric:tabular-nums!important; }
${P} #l4 table.univer-app-gpa-layout table:not(#gpa-table) :is(th,td) { padding:10px 12px!important; border:0!important; border-bottom:1px solid var(--u-line)!important; background:none!important; color:var(--u-text)!important; font-size:var(--u-fs-body)!important; line-height:1.45!important; text-align:start!important; vertical-align:middle!important; }
${P} #l4 table.univer-app-gpa-layout table:not(#gpa-table) th { background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; }
${P} #l4 table.univer-app-gpa-layout table:not(#gpa-table) td:is([rowspan],:nth-child(4)) { border-left:1px solid var(--u-line)!important; }
${P} #l4 table.univer-app-gpa-layout table:not(#gpa-table) td:first-child { color:var(--u-ink)!important; font-weight:600!important; }
${P} #l4 table.univer-app-gpa-layout table:not(#gpa-table) tr:last-child>:is(th,td) { border-bottom:0!important; }

/* Personal data: the two photos side by side, labels above them, the upload field under them. */
${P} #l4 .univer-app-table-scroll:has(img.photo) { height:auto!important; min-height:0!important; padding:16px!important; }
${P} #l4 .univer-app-table-scroll:has(img.photo)>table { margin:0!important; }
${P} #l4 .univer-app-table-scroll:has(img.photo) :is(table,tbody) { display:block!important; width:auto!important; }
${P} #l4 .univer-app-table-scroll:has(img.photo) tr { display:flex!important; flex-wrap:wrap!important; gap:24px!important; }
${P} #l4 .univer-app-table-scroll:has(img.photo) td { display:flex!important; flex-direction:column!important; align-items:flex-start!important; gap:8px!important; padding:0!important; border:0!important; background:none!important; }
${P} #l4 .univer-app-table-scroll:has(img.photo) label { margin:0!important; }
${P} #l4 img.photo { display:block!important; box-sizing:border-box!important; width:120px!important; height:160px!important; object-fit:cover!important; border:1px solid var(--u-line)!important; border-radius:10px!important; background:var(--u-surface-2)!important; }
${P} #l4 .univer-app-form-table td:has(img.photo)>label { display:block!important; text-align:start!important; }

/* Profile: the student's card, label and value rows with the photo beside them. */
${P} #l4 .univer-app-table-scroll:has(>table.univer-app-matrix td[rowspan]>img) { display:block!important; width:100%!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) { width:100%!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td { padding:10px 16px!important; border:0!important; border-bottom:1px solid var(--u-line)!important; background:var(--u-surface)!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; text-align:start!important; vertical-align:middle!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td:has(>li) { width:1%!important; white-space:nowrap!important; color:var(--u-muted)!important; font-weight:400!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td>li { display:block!important; margin:0!important; padding:0!important; list-style:none!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td>li :is(b,strong) { color:inherit!important; font-weight:inherit!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) tr:last-child>td, ${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td[rowspan] { border-bottom:0!important; }
${P} #l4 table.univer-app-matrix .up-none { color:var(--u-muted)!important; font-weight:400!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td[rowspan] { width:1%!important; min-width:152px!important; padding:16px!important; vertical-align:top!important; border-left:1px solid var(--u-line)!important; }
${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td[rowspan]>img { display:block!important; width:120px!important; max-width:none!important; height:160px!important; object-fit:cover!important; border-radius:10px!important; }
@media (max-width:560px) { ${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td[rowspan]>img { width:72px!important; height:96px!important; } ${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td[rowspan] { min-width:96px!important; padding:12px!important; } ${P} #l4 table.univer-app-matrix:has(td[rowspan]>img):has(td>li) td { padding:10px 12px!important; } }

/* Schedules and the academic calendar. */
${P} #l4 .up-kind { display:inline-flex!important; padding:2px 10px!important; border-radius:999px!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; }
${P} #l4 .up-kind-exam { background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
${P} #l4 .up-kind-consult { background:var(--u-teal-soft)!important; color:var(--u-teal)!important; }
${P} #l4 th.timerTicked [data-timer] { display:inline-flex!important; margin-left:8px!important; padding:2px 10px!important; border-radius:999px!important; background:#fdf0dc!important; color:#7a4300!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; font-variant-numeric:tabular-nums!important; }
${P} #l4 tr.up-past>td { color:var(--u-muted)!important; }
${P} #l4 tr.up-now>td { background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }
${P} #l4 .univer-app-semester-nav a.up-current { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }

/* IGPA: labelled fields without grey bands, tabs as pills. */
${P} #l4 .igpa-ui table.igpa-fields, ${P} #l4 .igpa-ui table.igpa-fields>tbody { display:block!important; width:100%!important; border:0!important; background:none!important; box-shadow:none!important; }
${P} #l4 .igpa-ui table.igpa-fields>tbody>tr { display:flex!important; flex-direction:column!important; gap:6px!important; margin:0 0 16px!important; padding:0!important; border:0!important; background:none!important; }
${P} #l4 .igpa-ui table.igpa-fields>tbody>tr>:is(th,td) { display:block!important; width:auto!important; padding:0!important; border:0!important; background:none!important; text-align:left!important; }
${P} #l4 .igpa-ui table.igpa-fields>tbody>tr>th :is(label,span) { color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; }
${P} #l4 .igpa-ui table.igpa-fields>tbody>tr:not(:has(input,select,textarea,label,p,a,button,img)) { display:none!important; }
${P} #l4 .igpa-ui nav.igpa-nav { display:flex!important; flex-wrap:wrap!important; gap:8px!important; border:0!important; }
${P} #l4 .igpa-ui nav.igpa-nav a[role=tab] { display:inline-flex!important; align-items:center!important; gap:8px!important; box-sizing:border-box!important; min-height:${control}!important; margin:0!important; padding:8px 16px!important; border:1px solid var(--u-line)!important; border-radius:999px!important; background:var(--u-surface)!important; color:var(--u-ink)!important; font-weight:500!important; text-decoration:none!important; transition:background-color .15s var(--u-ease-out),border-color .15s var(--u-ease-out)!important; }
${P} #l4 .igpa-ui nav.igpa-nav a[role=tab]::after { display:none!important; }
${P} #l4 .igpa-ui nav.igpa-nav a.igpa-tab-active { border-color:var(--u-brand)!important; background:var(--u-brand)!important; color:#fff!important; }
${P} #l4 .igpa-ui nav.igpa-nav .igpa-count { display:inline-flex!important; justify-content:center!important; min-width:20px!important; padding:0 6px!important; border-radius:999px!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-size:var(--u-fs-caption)!important; }
${P} #l4 .igpa-ui nav.igpa-nav a.igpa-tab-active .igpa-count { background:#fff!important; }
@media (hover:hover) { ${P} #l4 .igpa-ui nav.igpa-nav a[role=tab]:not(.igpa-tab-active):hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; } }

/* New appeal: one centred column, the counter under its field, Back beside Send. */
${P} #l4 form#appealForm { box-sizing:border-box!important; width:100%!important; max-width:760px!important; margin:0 auto!important; }
${P} #l4 #appealForm .appeal-form-row { display:flex!important; flex-direction:column!important; gap:6px!important; margin:0 0 16px!important; }
${P} #l4 #appealForm .appeal-form-row>label { color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; }
${P} #l4 #appealForm .appeal-form-row :is(select,textarea) { box-sizing:border-box!important; width:100%!important; max-width:none!important; }
${P} #l4 #appealForm textarea { min-height:168px!important; resize:vertical!important; }
${P} #l4 #appealForm .appeal-buttons { display:flex!important; flex-wrap:wrap!important; align-items:center!important; justify-content:flex-end!important; gap:8px!important; margin-top:8px!important; }
${P} #l4 #appealForm .appeal-form-row>:is(small,span,div,p):not(:has(*)) { color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; }
${P} #l4 table.mt:has(#appealForm) { max-width:760px!important; margin-left:auto!important; margin-right:auto!important; }

/* Instruction files: one card per file, in a grid when the files stand together. */
${P} #l4 a.up-file { position:relative!important; display:flex!important; align-items:center!important; gap:12px!important; box-sizing:border-box!important; min-height:56px!important; margin:0 0 8px!important; padding:10px 16px 10px 10px!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:var(--u-surface)!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; line-height:1.35!important; text-decoration:none!important; transition:border-color .15s var(--u-ease-out),background-color .15s var(--u-ease-out)!important; }
${P} #l4 a.up-file::before { content:''!important; flex:0 0 36px!important; width:36px!important; height:36px!important; border-radius:10px!important; background:var(--u-brand-soft)!important; }
${P} #l4 a.up-file::after { content:''!important; position:absolute!important; left:19px!important; top:calc(50% - 9px)!important; width:18px!important; height:18px!important; background:var(--u-brand)!important; -webkit-mask:${doc} center/contain no-repeat!important; }
${P} #l4 .up-files>br { display:none!important; }
${P} #l4 div.up-doc { position:relative!important; display:flex!important; align-items:center!important; gap:12px!important; box-sizing:border-box!important; min-height:56px!important; margin:0 0 8px!important; padding:10px 12px 10px 10px!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:var(--u-surface)!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; line-height:1.35!important; }
${P} #l4 div.up-doc::before { content:''!important; flex:0 0 36px!important; width:36px!important; height:36px!important; border-radius:10px!important; background:var(--u-brand-soft)!important; }
${P} #l4 div.up-doc::after { content:''!important; position:absolute!important; left:19px!important; top:calc(50% - 9px)!important; width:18px!important; height:18px!important; background:var(--u-brand)!important; -webkit-mask:${doc} center/contain no-repeat!important; }
${P} #l4 div.up-doc>:is(b,strong) { flex:1 1 auto!important; min-width:0!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; }
@media (hover:hover) { ${P} #l4 a.up-file:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; } }
${P} #l4 :is(td.univer-app-doc-cell,.univer-app-media-column):has(>a.univer-app-doc-link) { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(min(100%,300px),1fr))!important; gap:8px!important; align-items:stretch!important; align-content:start!important; }
${P} #l4 :is(td.univer-app-doc-cell,.univer-app-media-column):has(>a.univer-app-doc-link)>:not(a.univer-app-doc-link) { grid-column:1/-1!important; }
${P} #l4 :is(td.univer-app-doc-cell,.univer-app-media-column):has(>a.univer-app-doc-link)>br { display:none!important; }
${P} #l4 :is(td.univer-app-doc-cell,.univer-app-media-column)>a.univer-app-doc-link { box-sizing:border-box!important; width:auto!important; max-width:none!important; margin:0!important; }

/* Plain site tables: quiet headings; a table that only says "nothing found" drops its headings. */
${P} #l4 table.inner>thead>tr>th { padding:10px 12px!important; border:0!important; border-bottom:1px solid var(--u-line)!important; background:var(--u-surface-2)!important; background-image:none!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; text-align:start!important; }
${P} #l4 table.inner>thead>tr>th * { color:inherit!important; font-size:inherit!important; font-weight:inherit!important; background:none!important; }
${P} #l4 table:has(tr.univer-app-empty-row):not(:has(tr:not(.univer-app-empty-row)>td))>thead { display:none!important; }

/* Headings: a semester title is a heading, "To the plan description" a small link beside it. */
${P} #l4 a.up-title-link { color:var(--u-ink)!important; font-size:var(--u-fs-section)!important; font-weight:500!important; text-decoration:none!important; }
${P} #l4 a.up-uplink { display:inline-flex!important; align-items:center!important; gap:6px!important; margin-left:12px!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; text-decoration:none!important; }
${P} #l4 a.up-uplink::before { content:'↑'!important; }
@media (hover:hover) { ${P} #l4 a.up-uplink:hover { color:var(--u-brand)!important; } }

/* IGPA: the form's fields sit straight in the card. */
${P} #l4 .univer-app-table-scroll:has(>table.igpa-fields) { overflow:visible!important; padding:0!important; border:0!important; border-radius:0!important; background:none!important; box-shadow:none!important; }

/* Footer: one quiet centred line under the footer sections. */
${P} #l5 td.center:has(>#copyright) { display:flex!important; flex-direction:column!important; align-items:stretch!important; }
${P} #l5 td.center:has(>#copyright)>* { box-sizing:border-box!important; width:100%!important; }
${P} #l5 #copyright { order:9!important; width:100%!important; margin:16px 0 0!important; }
${P} #l5 td.up-copyright { padding:0!important; color:var(--u-muted)!important; font-size:var(--u-fs-caption)!important; line-height:1.5!important; text-align:center!important; }
${P} #l5 td.up-copyright>br { display:none!important; }
${P} #l5 td.up-copyright a { color:var(--u-muted)!important; }
@media (hover:hover) { ${P} #l5 td.up-copyright a:hover { color:var(--u-brand)!important; } }
}
`;
    }

    function apply(lang) {
        var changed = (lang || 'ru') !== language;
        language = lang || 'ru';
        if (!capable || document.readyState === 'loading' || !/^\/(student|ssc|my)(\/|$)/i.test(location.pathname)) return;
        enabled = true;
        if (!style || !style.isConnected) {
            style = document.createElement('style'); style.id = 'univer-polish-style';
            if (window.__univerPaletteV1) window.__univerPaletteV1.fill(style, css()); else style.textContent = css();
            (document.head || document.documentElement).appendChild(style);
        }
        relabels = relabels.filter(function (r) { return r[0].isConnected; });
        if (changed) relabels.forEach(function (r) { r[1](); });
        var path = location.pathname.toLowerCase();
        notes(); facts(); alerts(); arrows(); titles(); buttons(); files(); docLinks(); person(); emptyTexts(); emptyTables(); emptyPanels();
        if (/^\/student\/monrknews/.test(path)) mon();
        if (/^\/my\/docs/.test(path)) counts();
        if (/^\/my\/calculatorgpa/.test(path)) calculator();
        if (/^\/student\/rating(\/\d+)?\/?$/.test(path)) rating();
        if (/^\/student\/my(exam|midterm)\/schedule/.test(path)) exams();
        if (/^\/student\/academcalendar/.test(path)) calendar();
        enter();
    }
    function release() {
        if (!enabled && !style) return;
        enabled = false;
        style?.remove(); style = null;
        listeners.forEach(function (l) { l[0].removeEventListener(l[1], l[2]); }); listeners = [];
        added.forEach(function (node) { node.remove(); }); added = [];
        mirrors.clear(); empties.clear(); clamped.clear(); relabels = []; calc = null;
        wraps.forEach(function (span) { if (span.parentNode) span.replaceWith(...span.childNodes); }); wraps = [];
        // The layout may have put its own text back first; a node that no longer holds what was written here is left alone.
        texts.forEach(function (saved, node) { if (node.nodeValue === saved.wrote) node.nodeValue = saved.original; }); texts.clear();
        attributes.forEach(function (saved, node) { Object.keys(saved).forEach(function (name) { if (saved[name] === null) node.removeAttribute(name); else node.setAttribute(name, saved[name]); }); }); attributes.clear();
        classes.forEach(function (set, node) { set.forEach(function (name) { node.classList.remove(name); }); if (!node.getAttribute('class')) node.removeAttribute('class'); }); classes.clear();
    }
    window.__univerPolishV1 = {apply:apply, release:release};
})();
