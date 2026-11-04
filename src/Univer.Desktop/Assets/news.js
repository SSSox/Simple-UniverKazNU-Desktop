/* News filters belong to the whole result list; source rows and site handlers stay intact. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerNewsV1) return;
    var common = window.__univerCommonV1, setText = common.setText;
    var key = 'univer.desktop.news-filter.v1', countKey = 'univer.desktop.news-page-count.v1', state = null, language = 'ru', timer = null;
    var folds = new Map(), foldSources = new WeakMap(), expanded = new WeakMap(), foldObserver = null, foldTimer = null, foldSequence = 0;
    var filter = {query:'', from:'', to:'', important:false, page:1};
    try {
        var saved = JSON.parse(sessionStorage.getItem(key));
        if (saved && typeof saved.query === 'string' && saved.query.length <= 300 &&
            [saved.from, saved.to].every(v => typeof v === 'string' && (!v || /^\d{4}-\d{2}-\d{2}$/.test(v))))
            filter = {query:saved.query, from:saved.from, to:saved.to, important:saved.important === true, page:Math.max(1, Math.min(1000, +saved.page || 1))};
    } catch (_) { /* Storage may be unavailable; filtering still works in this document. */ }
    function text() {
        return language === 'en' ? {search:'Search news…', from:'From', to:'To', important:'Important only', shown:'Shown', of:'of', all:'Search all pages', reset:'Reset', searching:'Searching…', page:'Page', go:'Go', none:'No news match these filters', failed:'Search is incomplete. Retry to check the remaining pages.', retry:'Continue search', invalid:'The start date must not be after the end date.'} :
            language === 'kz' ? {search:'Жаңалықтарды іздеу…', from:'Бастап', to:'Дейін', important:'Тек маңызды', shown:'Көрсетілді', of:'/', all:'Барлық беттерден іздеу', reset:'Тазалау', searching:'Іздеу…', page:'Бет', go:'Өту', none:'Бұл сүзгілерге сәйкес жаңалықтар жоқ', failed:'Іздеу аяқталмады. Қалған беттерді тексеру үшін қайталаңыз.', retry:'Іздеуді жалғастыру', invalid:'Басталу күні аяқталу күнінен кейін болмауы керек.'} :
            {search:'Поиск по новостям…', from:'С', to:'По', important:'Только важные', shown:'Показано', of:'из', all:'Искать на всех страницах', reset:'Сбросить', searching:'Поиск…', page:'Страница', go:'Перейти', none:'По этим условиям новостей нет', failed:'Поиск не завершён. Повторите, чтобы проверить остальные страницы.', retry:'Продолжить поиск', invalid:'Дата «С» должна быть не позже даты «По».'};
    }
    function clean(node) {
        // Search and signatures use the complete article, excluding our disclosure control.
        if (node?.matches?.('tr.news')) return Array.from(node.cells).map(cell => clean(cell.querySelector(':scope > .univer-app-news-body') || cell)).join(' ');
        return (node?.textContent || '').replace(/\s+/g, ' ').trim();
    }
    function foldText() {
        return language === 'en' ? {expand:'Show full news', collapse:'Collapse'} :
            language === 'kz' ? {expand:'Толық көрсету', collapse:'Жасыру'} : {expand:'Показать полностью', collapse:'Свернуть'};
    }
    function updateFold(record) {
        if (!record.body.getBoundingClientRect().width) return; // Hidden search/source pages have no useful geometry.
        var long = record.body.scrollHeight > 280, open = expanded.get(record.source) === true;
        var collapsed = String(long && !open);
        if (record.body.getAttribute('data-collapsed') !== collapsed) record.body.setAttribute('data-collapsed',collapsed);
        if (record.button.hidden === long) record.button.hidden = !long;
        var value = String(!long || open);
        if (record.button.getAttribute('aria-expanded') !== value) record.button.setAttribute('aria-expanded',value);
        setText(record.button,long ? (open ? foldText().collapse : foldText().expand) : '');
    }
    function unfold(record) {
        window.__univerMotionUI?.stop(record.body);
        foldObserver?.unobserve(record.body); record.button.remove();
        if (record.body.parentNode) record.body.replaceWith(...record.body.childNodes);
    }
    function refreshFolds() {
        if (!state) return;
        folds.forEach((record,row) => {
            if (!record.body.isConnected || (!state.table.contains(row) && !state.results.contains(row))) { unfold(record); folds.delete(row); }
        });
        [state.table,state.results].forEach(root => root.querySelectorAll('tr.news').forEach(row => {
            var record = folds.get(row), cell = row.cells[1]; if (!cell) return;
            if (!record) {
                var body = document.createElement('div'); body.className = 'univer-app-news-body'; body.id = 'univer-news-body-' + (++foldSequence);
                body.append(...cell.childNodes);
                var button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-news-toggle'; button.hidden = true;
                button.setAttribute('aria-controls',body.id); cell.append(body,button);
                record = {body,button,row,source:foldSources.get(row) || row}; folds.set(row,record);
                button.addEventListener('click',event => {
                    event.stopPropagation(); var wasOpen = expanded.get(record.source) === true;
                    var before = body.getBoundingClientRect().height;
                    window.__univerMotionUI?.stop(body);
                    expanded.set(record.source,!wasOpen); updateFold(record);
                    if (event.detail > 0) window.__univerMotionUI?.resize(body, before, wasOpen ? 180 : 220);
                    if (!wasOpen && event.detail > 0) window.__univerMotionUI?.reveal(body, 'content');
                    if (wasOpen && row.getBoundingClientRect().top < 80) row.scrollIntoView?.({block:'start', behavior:event.detail > 0 && window.__univerMotion !== false && !window.__univerMotionUI?.reduced() ? 'smooth' : 'auto'});
                });
                // A keyboard user reaching a link below the preview gets the full article before continuing.
                body.addEventListener('focusin',() => { window.__univerMotionUI?.stop(body); expanded.set(record.source,true); updateFold(record); });
                body.addEventListener('load',scheduleFolds,true);
                foldObserver?.observe(body);
            }
            updateFold(record);
        }));
    }
    function scheduleFolds() {
        if (!state || foldTimer !== null) return;
        foldTimer = setTimeout(() => { foldTimer = null; refreshFolds(); },0);
    }
    function fold(value) { return value.toLocaleLowerCase().replace(/ё/g, 'е'); }
    function pageNumber() { return +(location.pathname.match(/^\/news\/student\/(\d+)/)?.[1] || 1); }
    function href(page) { return page > 1 ? '/news/student/' + page : '/news/student'; }
    function active() { return !!(filter.query.trim() || filter.from || filter.to || filter.important); }
    function valid() { return !(filter.from && filter.to && filter.from > filter.to); }
    function date(row) {
        var box = row.cells[0]?.querySelector('.date'), day = +clean(box?.querySelector('.day'));
        var parts = clean(box).match(/(\d{1,2})\.(\d{4})\s*$/);
        return day && parts ? parts[2] + '-' + parts[1].padStart(2, '0') + '-' + String(day).padStart(2, '0') : '';
    }
    function matches(row) {
        var stamp = date(row);
        return (!filter.from || (stamp && stamp >= filter.from)) && (!filter.to || (stamp && stamp <= filter.to)) &&
            (!filter.important || /важное|маңызды|important/i.test(clean(row.cells[0]))) &&
            fold(filter.query.trim()).split(/\s+/).every(word => fold(clean(row)).includes(word));
    }
    function save() { try { if (active()) sessionStorage.setItem(key, JSON.stringify(filter)); else sessionStorage.removeItem(key); } catch (_) {} }
    function hasNext(doc, page) {
        return Array.from(doc.querySelectorAll('td.tb a[href]')).some(a => {
            var match = new URL(a.href, location.href).pathname.match(/^\/(?:my\/news|news\/student)\/(\d+)\/?$/);
            return match && +match[1] > page;
        });
    }
    // Fetched HTML is inert. Only passive, sanitized copies are inserted into the result list.
    function safeRow(row) {
        var clone = row.cloneNode(true);
        clone.querySelectorAll('.univer-app-news-toggle').forEach(n => n.remove());
        clone.querySelectorAll('.univer-app-news-body').forEach(n => n.replaceWith(...n.childNodes));
        clone.querySelectorAll('script,style,iframe,frame,object,embed,form,input,button,svg,math,link,meta').forEach(n => n.remove());
        [clone, ...clone.querySelectorAll('*')].forEach(node => {
            if (!/^(TR|TD|TH|DIV|P|SPAN|A|IMG|BR|B|STRONG|I|EM|U|S|SUB|SUP|UL|OL|LI|TABLE|TBODY|THEAD|TFOOT|H[1-6]|BLOCKQUOTE|HR|PRE|CODE)$/.test(node.tagName)) { node.replaceWith(...node.childNodes); return; }
            Array.from(node.attributes).forEach(attribute => {
                if (!/^(class|colspan|rowspan|alt|title|href|src)$/.test(attribute.name)) node.removeAttribute(attribute.name);
            });
            ['href', 'src'].forEach(name => {
                if (!node.hasAttribute(name)) return;
                try {
                    var url = new URL(node.getAttribute(name), location.origin);
                    if (url.protocol !== 'https:' || url.username || url.password || (name === 'src' && url.origin !== location.origin)) node.removeAttribute(name);
                    else node.setAttribute(name, url.href);
                } catch (_) { node.removeAttribute(name); }
            });
        });
        clone.hidden = false;
        return clone;
    }
    function pager(current, count, filtered, exact) {
        var nav = document.createElement('nav'), t = text(); nav.className = 'univer-app-news-pager'; nav.setAttribute('aria-label', t.page);
        if (count <= 1) nav.hidden = true;
        var add = function (p) {
            var a = document.createElement('a'); a.textContent = String(p); a.href = filtered ? '#news-results-' + p : href(p);
            if (p === current) { a.setAttribute('aria-current', 'page'); a.className = 'univer-app-news-current'; }
            if (filtered) a.addEventListener('click', e => { e.preventDefault(); filter.page = p; save(); render(); state.bar.scrollIntoView?.({block:'start'}); });
            nav.appendChild(a);
        };
        var pages = new Set([1, count]);
        for (var first = 1; first <= Math.min(5, count); first++) pages.add(first);
        for (var p = Math.max(1, current - 2); p <= Math.min(count, current + 2); p++) pages.add(p);
        var previous = 0;
        Array.from(pages).sort((a,b) => a-b).forEach(p => {
            if (previous && p > previous + 1) { var gap = document.createElement('span'); gap.className = 'univer-app-news-gap'; gap.textContent = '…'; nav.appendChild(gap); }
            add(p); previous = p;
        });
        var form = document.createElement('form'); form.className = 'univer-app-news-jump';
        var caption = document.createElement('span'); caption.className = 'univer-app-news-jump-label'; caption.textContent = t.page;
        var number = document.createElement('input'); number.type = 'number'; number.min = '1'; if (exact) number.max = String(count); number.value = String(current); number.setAttribute('aria-label', t.page);
        var go = document.createElement('button'); go.type = 'submit'; go.textContent = t.go;
        form.append(caption, number, go); nav.appendChild(form);
        form.addEventListener('submit', e => {
            e.preventDefault(); var target = Math.min(exact ? count : 4096, Math.max(1, parseInt(number.value, 10) || 1));
            if (filtered) { filter.page = target; save(); render(); } else location.assign(href(target));
        });
        return nav;
    }
    function replacePagers(current, count, filtered, exact = true) {
        var signature = [current, count, filtered, exact, language].join(':');
        if (state.pagerSignature === signature) return;
        // Keyboard focus inside a pager moves to the same place in its rebuilt copy: the current page link or the jump field.
        var focused = document.activeElement, old = focused?.closest?.('.univer-app-news-pager');
        var top = old ? old.classList.contains('univer-app-news-pager-top') : false, part = focused?.closest?.('.univer-app-news-jump') ? (focused.tagName === 'INPUT' ? 'input' : 'button') : 'a[aria-current=page]';
        document.querySelectorAll('.univer-app-news-pager').forEach(n => n.remove());
        state.pagerSignature = signature;
        var upper = pager(current, count, filtered, exact), lower = pager(current, count, filtered, exact);
        upper.classList.add('univer-app-news-pager-top'); state.bar.after(upper); state.anchor.after(lower);
        if (old) (top ? upper : lower).querySelector(part)?.focus?.({preventScroll:true});
    }
    function render() {
        if (!state) return;
        var t = text(), rows = Array.from(state.table.querySelectorAll('tr.news')), shown = 0;
        rows.forEach(row => {
            if (!state.hidden.has(row)) state.hidden.set(row, row.hidden);
            var visible = !state.hidden.get(row) && (!active() || (valid() && matches(row)));
            if (row.hidden === visible) row.hidden = !visible;
            if (visible) shown++;
        });
        state.reset.hidden = !active();
        state.all.hidden = !active() || state.running || !state.searched || state.complete || !valid();
        setText(state.all, state.next > 1 ? t.retry : t.all);
        if (!active()) {
            state.table.removeAttribute('data-univer-news-source'); state.results.hidden = true;
            setText(state.counter, t.shown + ' ' + rows.length);
            replacePagers(pageNumber(), state.total || state.known, false, !!state.total);
            refreshFolds();
            return;
        }
        var found = [];
        state.pages.forEach((items, p) => items.forEach((row, i) => { if (!(state.hidden.has(row) ? state.hidden.get(row) : row.hidden) && valid() && matches(row)) found.push({row:row, page:p, index:i}); }));
        found.sort((a,b) => a.page-b.page || a.index-b.index);
        var count = Math.max(1, Math.ceil(found.length / state.pageSize));
        // A restored page is retained while earlier pages are still being fetched.
        if (!state.running && state.searched) filter.page = Math.min(filter.page, count);
        var current = Math.min(filter.page, count);
        state.table.setAttribute('data-univer-news-source', 'true'); state.results.hidden = false;
        // Progressive search rebuilds the results every fetched page; keyboard focus stays on the same control of the same news.
        var focused = document.activeElement, keep = null, shownRow = focused && state.results.contains(focused) ? focused.closest('tr.news') : null;
        if (shownRow && foldSources.get(shownRow)) keep = {source:foldSources.get(shownRow), index:Array.from(shownRow.querySelectorAll('a,button,summary,[tabindex]')).indexOf(focused)};
        state.results.replaceChildren();
        var status = document.createElement('p'); status.className = 'univer-app-news-status'; status.setAttribute('role', 'status');
        status.textContent = !valid() ? t.invalid : state.running ? t.searching : !state.complete ? t.failed : found.length ? '' : t.none;
        status.hidden = !status.textContent; state.results.appendChild(status);
        var table = document.createElement('table'); table.className = 'univer-app-news'; table.dataset.univerResults = 'true';
        var body = document.createElement('tbody'); table.appendChild(body);
        var slice = found.slice((current-1) * state.pageSize, current * state.pageSize);
        slice.forEach(hit => { var row = safeRow(hit.row); foldSources.set(row,hit.row); body.appendChild(row); if (keep && keep.source === hit.row) keep.row = row; }); state.results.appendChild(table);
        setText(state.counter, t.shown + ' ' + slice.length + ' ' + t.of + ' ' + found.length + (state.running || !state.complete ? '+' : ''));
        replacePagers(current, count, true); save(); refreshFolds();
        if (keep && keep.row && keep.index >= 0) keep.row.querySelectorAll('a,button,summary,[tabindex]')[keep.index]?.focus?.({preventScroll:true});
    }
    function cancel() { clearTimeout(timer); if (state) { state.generation++; state.controller?.abort(); state.running = false; } }
    function changed() {
        cancel(); filter.page = 1;
        filter.query = state.query.value.slice(0,300); filter.from = state.from.value; filter.to = state.to.value; filter.important = state.important.getAttribute('aria-pressed') === 'true';
        state.next = 1; state.complete = pageNumber()===1 && !hasNext(document,1); state.searched=false;
        save(); render();
        if (active() && valid()) timer = setTimeout(() => search(), 200);
        else if (!active() && !state.total) timer = setTimeout(discoverPages, 400);
    }
    async function readPage(p, record, generation) {
        var controller = new AbortController(); record.controller = controller;
        var timeout = setTimeout(() => controller.abort(), 10000);
        try {
            var response = await fetch(href(p), {credentials:'same-origin', cache:'no-store', redirect:'error', signal:controller.signal});
            var url = new URL(response.url, location.origin);
            if (response.status !== 200 || url.origin !== location.origin || url.pathname.replace(/\/$/,'') !== href(p) || +response.headers?.get('content-length') > 4*1024*1024) throw new Error('Unexpected news response');
            var html = await response.text(); if (html.length > 4*1024*1024) throw new Error('Oversized news response');
            if (state !== record || record.generation !== generation) throw new Error('Stale news response');
            var doc = new DOMParser().parseFromString(html, 'text/html'), rows = Array.from(doc.querySelectorAll('tr.news'));
            if (doc.querySelector('#login_frm') || (!rows.length && !doc.querySelector('td.tb'))) throw new Error('News unavailable');
            var displayed=Array.from(doc.querySelectorAll('td.tb')).map(cell=>{var copy=cell.cloneNode(true);copy.querySelectorAll('a').forEach(a=>a.remove());return clean(copy);}).join(' ').match(/(?:Страница|Page|Бет)\s*(\d+)/i);
            if (displayed && +displayed[1]!==p) throw new Error('News page was clamped');
            return {rows, more:hasNext(doc,p)};
        } finally { clearTimeout(timeout); }
    }
    async function discoverPages() {
        if (!state || active() || state.total || typeof fetch !== 'function') return;
        var record=state, generation=record.generation, low=pageNumber(), high=0, probe=Math.max(5,low*2);
        // The pager shows only a next arrow, so bounded exponential/binary GET probes find the last page; only the count is kept.
        try {
            for (var attempts=0; attempts<24; attempts++) {
                var result=await readPage(probe,record,generation);
                if (result.rows.length) {
                    low=probe; record.known=Math.max(record.known,probe);
                    if (!result.more) { high=low; break; }
                } else high=probe-1;
                if (high && high<=low) break;
                if (!high && probe>=4096) return;
                probe=high ? Math.ceil((low+high)/2) : Math.min(4096,probe*2);
                render();
            }
            if (high===low && state===record && record.generation===generation) {
                record.total=low;
                try { sessionStorage.setItem(countKey,JSON.stringify({count:low,time:Date.now()})); } catch (_) {}
                render();
            }
        } catch (_) { /* Keep confirmed links and an unrestricted direct jump when counting is unavailable. */ }
    }
    async function search() {
        cancel(); if (!state || !active() || !valid()) return;
        var record = state, generation = record.generation; record.running = true; render();
        // At most 30 read-only GETs per batch; a failed/limited search is explicitly incomplete.
        for (var fetched = 0; fetched < 30 && !record.complete; fetched++) {
            var p = record.next;
            if (p === pageNumber()) { record.next++; if (!hasNext(document, p)) record.complete = true; continue; }
            if (record.pages.has(p)) {
                var cached = record.pages.get(p); record.next++;
                if (!record.more.get(p) || (filter.from && cached.length && cached.every(row=>date(row) && date(row)<filter.from))) record.complete=true;
                continue;
            }
            try {
                var result = await readPage(p,record,generation), rows=result.rows, more=result.more;
                record.pages.set(p, rows); record.more.set(p,more); record.pageSize = Math.max(record.pageSize, rows.length); record.next++;
                if (!rows.length || !more) record.complete = true;
                // News are newest first. Stop only after an entire dated page precedes the lower bound.
                if (filter.from && rows.length && rows.every(row => date(row) && date(row) < filter.from)) record.complete = true;
                render();
            } catch (_) { break; }
        }
        if (state !== record || record.generation !== generation) return;
        record.running = false; record.searched=true; render();
    }
    function apply(lang) {
        language = lang;
        // The site streams its bottom pager after the news rows. A partial document is not a one-page feed.
        if (document.readyState === 'loading') return;
        if (!/^\/news\/student(?:\/\d+)?\/?$/.test(location.pathname)) return;
        var table = document.querySelector('table.univer-app-news:not([data-univer-results])'); if (!table) return;
        if (state && state.table !== table) release();
        if (!state) {
            var bar = document.createElement('div'); bar.className = 'univer-app-news-tools'; bar.setAttribute('role','search');
            var input = function (type, cls, value) { var n = document.createElement('input'); n.type = type; n.className = 'univer-app-news-' + cls; n.value = value; return n; };
            var query = input('search','query',filter.query), from = input('date','from',filter.from), to = input('date','to',filter.to); query.maxLength = 300;
            var button = function (cls) { var n = document.createElement('button'); n.type = 'button'; n.className = 'univer-app-news-' + cls; return n; };
            var important = button('important'), reset = button('reset'), all = button('all'); important.setAttribute('aria-pressed',String(filter.important));
            var dateLabel = function (control) { var label = document.createElement('label'); label.className = 'univer-app-news-date'; label.append(document.createElement('span'), control); return label; };
            var counter = document.createElement('span'); counter.className = 'univer-app-news-count'; counter.setAttribute('role','status');
            bar.append(query,dateLabel(from),dateLabel(to),important,reset,all,counter);
            var results = document.createElement('section'); results.className = 'univer-app-news-results'; results.hidden = true;
            var anchor = table.closest('.univer-app-table-scroll') || table; anchor.before(bar,results);
            var rows = Array.from(table.querySelectorAll('tr.news'));
            state = {table,anchor,bar,query,from,to,important,reset,all,counter,results,hidden:new Map(),pages:new Map([[pageNumber(),rows]]),more:new Map(),next:1,complete:pageNumber()===1&&!hasNext(document,1),running:false,searched:false,generation:0,pageSize:Math.max(1,rows.length)};
            if (typeof ResizeObserver === 'function') foldObserver = new ResizeObserver(scheduleFolds);
            window.addEventListener('resize',scheduleFolds);
            state.known=pageNumber()+(hasNext(document,pageNumber())?1:0); state.total=hasNext(document,pageNumber())?0:pageNumber();
            try { var cached=JSON.parse(sessionStorage.getItem(countKey)); if (!state.total && cached && Number.isInteger(cached.count) && cached.count>=state.known && cached.count<=4096 && Date.now()-cached.time>=0 && Date.now()-cached.time<600000) state.total=cached.count; } catch (_) {}
            [query,from,to].forEach(n => n.addEventListener('input',changed));
            important.addEventListener('click',() => { important.setAttribute('aria-pressed',String(important.getAttribute('aria-pressed')!=='true')); changed(); });
            reset.addEventListener('click',() => { query.value=from.value=to.value=''; important.setAttribute('aria-pressed','false'); changed(); });
            all.addEventListener('click',search);
            if (active()) timer = setTimeout(search,200);
            else if (!state.total) timer = setTimeout(discoverPages,400);
        }
        var t = text(); state.query.placeholder=t.search; state.query.setAttribute('aria-label',t.search);
        var captions = state.bar.querySelectorAll('.univer-app-news-date>span'); setText(captions[0],t.from); setText(captions[1],t.to);
        setText(state.important,t.important); setText(state.reset,t.reset);
        document.querySelectorAll('#l4 td.tb').forEach(cell => { if (cell.querySelector('a[href*="/my/news/"]') || /^(Страница|Page|Бет)\s*\d+/i.test(clean(cell))) cell.classList.add('univer-app-news-pager-source'); });
        // Native polling calls apply repeatedly; only rerender on actual language/content changes.
        var signature = language + '|' + Array.from(table.querySelectorAll('tr.news')).map(clean).join('|');
        if (state.signature !== signature) {
            state.signature = signature; state.pages.set(pageNumber(),Array.from(table.querySelectorAll('tr.news'))); render();
        }
        scheduleFolds();
    }
    function release() {
        cancel(); if (!state) return;
        clearTimeout(foldTimer); foldTimer = null; window.removeEventListener('resize',scheduleFolds);
        foldObserver?.disconnect(); foldObserver = null;
        folds.forEach(unfold); folds.clear(); foldSources = new WeakMap(); expanded = new WeakMap();
        state.hidden.forEach((hidden,row) => { row.hidden=hidden; }); state.table.removeAttribute('data-univer-news-source');
        state.bar.remove(); state.results.remove(); document.querySelectorAll('.univer-app-news-pager').forEach(n=>n.remove());
        document.querySelectorAll('.univer-app-news-pager-source').forEach(n=>n.classList.remove('univer-app-news-pager-source')); state=null;
    }
    window.__univerNewsV1 = {apply,release};
})();
