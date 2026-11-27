/* Reads the site's notification marks and corrects the questionnaire badge in the reading layout. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerNoticesV1) return;
    var PREFIX = 'univer-notices:';
    var COUNT = /[(\[]\s*(\d{1,4})\s*[)\]]\s*$/;
    var MARK_CLASS = /(^|[\s_-])(red|danger|attention|warning|alert|badge|counter|count|notify|notification|unread)($|[\s_-])/i;
    var REQUIRED = /(необходимо|нужно|требуется|следует|обязательно|должн|пройдите|заполните|ответьте|оцените|қажет|міндетті|тиіс|өтіңіз|толтырыңыз|бағалаңыз|must|need to|required|please (complete|fill|take|answer))/i;
    var TASK = /(анкет|опрос|тест|оцен|сауалнам|тестіл|бағала|survey|questionnaire|test|evaluat|feedback)/i;
    var DONE = /^[+✓✔☑√]$/;
    var DATE = /\b\d{2}\.\d{2}\.\d{4}\b/g;
    var MESSAGE = '.message,.error,.warning,.alert,.attention,[style*="color"],font[color],p,li,div,span,td,b,strong';
    var SKIP = '#tools,#login_frm,#modal_auth_frm,#modal_agreement,#attentionSt,.news,.univer-app-news,.univer-app-menu,script,style,noscript,select,option,textarea';
    var MAX_LINKS = 120, MAX_SECTIONS = 4, MAX_LISTS = 3;
    var SURVEYS = '/student/rating/', MAX_SURVEYS = 10, READ_TIMEOUT = 15000;
    var ACCOUNT = 'univer.app.account.v1', SURVEY_COUNT = 'univer.desktop.survey-count.v1';
    var badges = new Map(), presenting = false, surveyResult = null, lastCheck = null;
    var fetching = false;

    function clean(value) { return String(value || '').replace(/\s+/g, ' ').trim(); }
    function red(value) {
        var color = String(value || '').trim().toLowerCase(), m;
        if (/^(red|crimson|darkred|firebrick|orangered)$/.test(color)) return true;
        if ((m = color.match(/^#([0-9a-f])([0-9a-f])([0-9a-f])$/))) color = '#' + m[1] + m[1] + m[2] + m[2] + m[3] + m[3];
        if ((m = color.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/))) m = [0, parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)];
        else m = color.match(/^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/);
        return !!m && +m[1] >= 170 && +m[2] <= 100 && +m[3] <= 100;
    }
    // Marked in the site's own markup (computed colours are the reading layout's): a red colour or a badge class.
    function marked(node) {
        return [node].concat(Array.from(node.querySelectorAll('*'))).some(function (element) {
            var style = element.getAttribute('style') || '', color = style.match(/(?:^|;)\s*color\s*:\s*([^;!]+)/i);
            return (color && red(color[1])) || (element.tagName === 'FONT' && red(element.getAttribute('color'))) ||
                (typeof element.className === 'string' && MARK_CLASS.test(element.className) && !/univer-app-/.test(element.className));
        });
    }
    // A same-origin page address (path and query), or null for files, account, language and script links.
    function sitePath(href, base) {
        try {
            var url = new URL(href, base);
            if (url.origin !== location.origin || !/^\/[^\s]*$/.test(url.pathname)) return null;
            if (/^\/(content|lang|user)\//i.test(url.pathname) || /\.(pdf|docx?|xlsx?|pptx?|zip|rar)$/i.test(url.pathname)) return null;
            return url.pathname + url.search;
        } catch (_) { return null; }
    }
    // The count sits in a red <b> after the link: <li><a href="/student/rating/">…</a><b style="color:Red">&nbsp;(2)</b></li>.
    // A red link without a number is a permanent highlight, and an unmarked number is part of the name.
    function item(a) {
        var li = a.closest('li');
        if (li && li.querySelectorAll('a[href]').length === 1) return {text:clean(li.textContent), node:li};
        var text = clean(a.textContent), next = a.nextElementSibling;
        return next && /^(B|SPAN|FONT|SUP|STRONG)$/.test(next.tagName) ? {text:clean(text + ' ' + next.textContent), node:next, link:a} : {text:text, node:a};
    }
    function surveyBadge(a) {
        var node = (a.closest('.univer-app-pin-row') || a).nextElementSibling;
        if (!node || node.tagName !== 'B' || !marked(node)) return null;
        var previous = badges.get(node);
        if (previous) {
            if (node.firstChild === previous.text && previous.text.data === previous.shown) return previous;
            restore(previous);
        }
        var text = node.firstChild;
        if (!text || text.nodeType !== 3 || text !== node.lastChild) return null;
        var count = clean(text.data).match(COUNT);
        return count ? {node:node, text:text, source:text.data, count:+count[1], hidden:node.getAttribute('hidden')} : null;
    }
    function menuBadges() {
        return Array.from(document.querySelectorAll('#tools a[href*="/student/rating"]')).filter(function (a) {
            return sitePath(a.getAttribute('href'), location.href) === SURVEYS;
        }).map(surveyBadge).filter(Boolean);
    }
    function owner() { try { return localStorage.getItem(ACCOUNT) || ''; } catch (_) { return ''; } }
    function cached(mark) {
        if (!mark) return null;
        try {
            var value = JSON.parse(localStorage.getItem(SURVEY_COUNT));
            if (value && value.owner === mark && Number.isInteger(value.count) && value.count > 0 && value.count <= 9999 &&
                Number.isInteger(value.open) && value.open >= 0 && value.open <= MAX_SURVEYS) return value;
        } catch (_) { }
        return null;
    }
    function restore(badge) {
        if (badge.text.data === badge.shown) badge.text.data = badge.source;
        if (badge.hidden === null) badge.node.removeAttribute('hidden');
        else badge.node.setAttribute('hidden', badge.hidden);
        badges.delete(badge.node);
    }
    function present(enabled) {
        presenting = enabled;
        if (!enabled) { badges.forEach(restore); return; }
        var current = menuBadges();
        if (!current.length && !badges.size) return;
        var mark = owner(), saved = cached(mark);
        var live = new Set(current.map(function (badge) { return badge.node; }));
        badges.forEach(function (badge, node) { if (!live.has(node)) restore(badge); });
        current.forEach(function (badge) {
            var result = surveyResult && surveyResult.owner === mark && surveyResult.count === badge.count ? surveyResult : saved;
            var open = result && result.count === badge.count ? result.open : null;
            if (open === null || open === badge.count || badge.count === 0) {
                if (badges.has(badge.node)) restore(badge);
                return;
            }
            badges.set(badge.node, badge);
            badge.shown = open === 0 ? badge.source : badge.source.replace(/\d{1,4}(?=\s*[)\]]\s*$)/, String(open));
            if (badge.text.data !== badge.shown) badge.text.data = badge.shown;
            if (badge.node.hidden !== (open === 0 || badge.hidden !== null)) badge.node.hidden = open === 0 || badge.hidden !== null;
        });
    }
    function remember(mark, count, result) {
        if (owner() !== mark) return;
        surveyResult = {owner:mark, count:count, open:result ? result.o : null};
        try {
            if (mark && result) localStorage.setItem(SURVEY_COUNT, JSON.stringify(surveyResult));
            else if (cached(mark)?.count === count) localStorage.removeItem(SURVEY_COUNT);
        } catch (_) { }
        if (presenting) present(true);
    }
    function links(doc, base) {
        var out = [];
        doc.querySelectorAll('#tools a[href],#l3 a[href],#l2 a[href],.univer-app-tabs a[href]').forEach(function (a) {
            if (out.length >= MAX_LINKS) return;
            var path = sitePath(a.getAttribute('href'), base), badge = path === SURVEYS ? surveyBadge(a) : null;
            var entry = badge ? {text:clean(a.textContent + ' ' + badge.source), node:badge.node} : item(a), text = entry.text;
            if (!path || !text || text.length > 200) return;
            var count = text.match(COUNT), title = clean(text.replace(COUNT, '')).slice(0, 100);
            if (count && !marked(entry.node) && !(entry.link && marked(entry.link))) count = null;
            var link = {u:path, t:count ? title || text.slice(0, 100) : text.slice(0, 100), n:count ? Math.min(+count[1], 9999) : 0};
            if (path === SURVEYS && link.n > 0) link.c = 1;
            if (a.closest('#tools')) link.m = 1;
            out.push(link);
        });
        return out;
    }
    function heading(doc) {
        var top = doc.querySelector('#l4 tr.top>td.ct') || doc.querySelector('h1,h2');
        return clean(top ? top.textContent : doc.title).slice(0, 100);
    }
    // The smallest marked element whose text both asks for something and names a questionnaire or a test.
    function blocks(doc, base, page) {
        var root = doc.getElementById('l4') || doc.body, out = [], texts = new Set();
        if (!root) return out;
        root.querySelectorAll(MESSAGE).forEach(function (node) {
            if (out.length >= 5 || node.closest(SKIP)) return;
            var text = clean(node.textContent);
            if (text.length < 15 || text.length > 400 || !REQUIRED.test(text) || !TASK.test(text)) return;
            if (!marked(node) && !node.matches('.message,.error,.warning,.alert,.attention') && !node.closest('.message,.error,.warning,.alert')) return;
            var inner = Array.from(node.children).some(function (child) {
                var value = clean(child.textContent);
                return value.length >= 15 && REQUIRED.test(value) && TASK.test(value);
            });
            if (inner || texts.has(text)) return;
            texts.add(text);
            var target = Array.from(node.querySelectorAll('a[href]')).map(function (a) { return sitePath(a.getAttribute('href'), base); }).find(Boolean);
            out.push({u:page, t:heading(doc), x:text.slice(0, 280), l:target || ''});
        });
        return out;
    }
    // What a counted page lists: its record rows (tr.link, else rows with an id), header rows left out. o is how many
    // are not answered yet, d their names and last dates.
    function details(doc) {
        var root = doc.getElementById('l4') || doc.body, list = {d:[], o:0, r:0};
        if (!root) return list;
        var rows = root.querySelectorAll('tr.link');
        if (!rows.length) rows = root.querySelectorAll('tr[id]');
        Array.from(rows).forEach(function (row) {
            if (list.r >= 200 || row.closest('#tools') || row.cells.length < 2 || row.querySelector('th')) return;
            var cells = Array.from(row.cells).map(function (cell) { return clean(cell.textContent); });
            var name = cells.find(function (value) { return value.length >= 3 && !/^[\d.:\s/-]+$/.test(value); });
            if (!name) return;
            list.r++;
            if (cells.some(function (value) { return DONE.test(value); })) return;
            list.o++;
            if (list.d.length < 3) {
                var dates = cells.join(' ').match(DATE);
                list.d.push({t:name.slice(0, 100), e:dates ? dates[dates.length - 1] : ''});
            }
        });
        return list;
    }
    function describe(link, doc) {
        var list = details(doc);
        link.d = list.d;
        // A page without rows says nothing about the count.
        if (list.r > 0) link.o = list.o;
    }
    function surveyRows(doc) {
        var root = doc.getElementById('l4') || doc.body;
        return Array.from(root.querySelectorAll('tr.link:not(#tools tr)'));
    }
    async function look(doc, page) {
        var deadline = performance.now() + READ_TIMEOUT;
        function withinDeadline(path) {
            var remaining = deadline - performance.now();
            return remaining > 0 ? page(path, remaining) : null;
        }
        if (!doc) {
            var list = await withinDeadline(SURVEYS);
            if (!list) return null;
            doc = list.doc;
        }
        var rows = surveyRows(doc);
        if (!rows.length || rows.length > MAX_SURVEYS || rows.some(function (row) {
            return !/^\d{1,9}$/.test(row.id) || row.cells.length < 2 || !clean(row.cells[0].textContent);
        })) return null;
        var open = [], ids = new Set();
        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            if (ids.has(row.id)) return null;
            ids.add(row.id);
            var detail = await withinDeadline(SURVEYS + row.id);
            if (!detail) return null;
            var lines = surveyRows(detail.doc);
            if (!lines.length || lines.some(function (line) { return line.cells.length < 3; })) return null;
            if (lines.every(function (line) {
                var cell = line.cells[2];
                return line.classList.contains('done') || !!cell.querySelector('img[alt*="[+]"]') || DONE.test(clean(cell.textContent));
            })) continue;
            var dates = Array.from(row.cells).map(function (cell) { return clean(cell.textContent); }).join(' ').match(DATE);
            open.push({t:clean(row.cells[0].textContent).slice(0, 100), e:dates ? dates[dates.length - 1] : ''});
        }
        return {o:open.length, d:open};
    }
    function read(doc, base, page, kind, generation) {
        var result = {v:1, g:generation, kind:kind, page:page, links:links(doc, base), blocks:blocks(doc, base, page)};
        result.links.forEach(function (link) { if (link.n > 0 && !link.c && link.u === page) describe(link, doc); });
        // The whole main menu was read (not cut by the cap): a menu item it no longer lists is gone.
        result.menu = !!doc.getElementById('tools') && result.links.length < MAX_LINKS && result.links.some(function (link) { return link.m; });
        return result;
    }
    async function readText(response, limit, charset) {
        if (window.__univerCommonV1 && window.__univerCommonV1.boundedText) return window.__univerCommonV1.boundedText(response, limit, charset);
        if (+response.headers?.get('content-length') > limit) throw new Error('Oversized response');
        var decoder; try { decoder = new TextDecoder(charset); } catch (_) { decoder = new TextDecoder('utf-8'); }
        if (response.body && response.body.getReader) {
            var reader = response.body.getReader(), size = 0, parts = [], ended = false;
            try {
                for (;;) {
                    var next = await reader.read();
                    if (next.done) { ended = true; break; }
                    size += next.value.byteLength;
                    if (size > limit) throw new Error('Oversized response');
                    parts.push(decoder.decode(next.value, {stream:true}));
                }
                parts.push(decoder.decode());
                return parts.join('');
            } finally {
                if (!ended) reader.cancel().catch(function () { });
                reader.releaseLock();
            }
        }
        var bytes = await response.arrayBuffer();
        if (bytes.byteLength > limit) throw new Error('Oversized response');
        return decoder.decode(bytes);
    }
    async function load(path, timeout) {
        var abort = new AbortController(), timer = setTimeout(function () { abort.abort(); }, timeout || READ_TIMEOUT);
        try {
            var response = await fetch(path, {mode:'same-origin', credentials:'same-origin', redirect:'follow', cache:'no-store', signal:abort.signal});
            var type = response.headers.get('content-type') || '';
            if (!response.ok || !/text\/html/i.test(type)) return null;
            var final = new URL(response.url);
            if (final.origin !== location.origin || final.pathname !== new URL(path, location.href).pathname) return null;
            var charset = (type.match(/charset=([\w-]+)/i) || [0, 'utf-8'])[1];
            var html = await readText(response, 3000000, charset);
            var doc = new DOMParser().parseFromString(html, 'text/html');
            // A sign-in page or a page without the menu (the read notice) says nothing about the section's marks.
            return doc.getElementById('login_frm') || !doc.getElementById('tools') ? false : {doc:doc, url:response.url};
        } finally { clearTimeout(timer); abort.abort(); }
    }
    function send(result) {
        if (typeof __univerNative === 'function') __univerNative(PREFIX + JSON.stringify(result));
    }
    // The extension host calls this during its layout tick. One check per page and account is enough there.
    function check() {
        if (!presenting || fetching) return;
        var mark = owner(), count = Math.max(0, ...menuBadges().map(function (badge) { return badge.count; }));
        if (!count || (lastCheck && lastCheck.owner === mark && lastCheck.count === count)) return;
        lastCheck = {owner:mark, count:count};
        fetching = true;
        look(location.pathname === SURVEYS ? document : null, load).catch(function () { return null; })
            .then(function (result) { remember(mark, count, result); }).finally(function () { fetching = false; });
    }
    // Each section, then the lists the counts point to that no read has described yet (at most three in all).
    // The last message says how many pages were read and how many failed.
    async function background(current, sections, counted, generation) {
        var lists = 0, seen = new Set(), done = 0, failed = 0, survey, mark = owner();
        async function page(path, timeout) {
            var loaded = await load(path, timeout).catch(function () { return null; });
            if (loaded) done++; else failed++;
            return loaded;
        }
        async function verify(links, doc) {
            if (!links.length) return;
            if (survey === undefined) {
                var failuresBefore = failed;
                survey = await look(doc, page);
                if (!survey && failed === failuresBefore) failed++;
                remember(mark, Math.max(...links.map(function (link) { return link.n; })), survey);
            }
            links.forEach(function (link) {
                delete link.c;
                if (survey) link.o = survey.o;
                link.d = survey ? survey.d : [];
            });
        }
        try {
            var surveys = current.links.filter(function (link) { return link.c; }).map(function (link) { return Object.assign({}, link); });
            if (surveys.length) {
                await verify(surveys, location.pathname === SURVEYS ? document : null);
                if (owner() !== mark) return;
                send({v:1, g:generation, kind:'section', page:current.page, links:surveys});
            }
            for (var i = 0; i < sections.length; i++) {
                // A section that fails to load (a network error, a timeout) does not end the others; a sign-in page does.
                var section = await page(sections[i]);
                if (section === false) return;
                if (!section) continue;
                var result = read(section.doc, section.url, sections[i], 'section', generation);
                await verify(result.links.filter(function (link) { return link.c; }), sections[i] === SURVEYS ? section.doc : null);
                if (owner() !== mark) return;
                for (var j = 0; j < result.links.length; j++) {
                    var link = result.links[j];
                    if (link.n <= 0 || link.d || lists >= MAX_LISTS || seen.has(link.u)) continue;
                    lists++; seen.add(link.u);
                    var target = await page(link.u);
                    if (target === false) return;
                    if (target) describe(link, target.doc);
                }
                send(result);
            }
            // A count on the current page whose list no section showed: its own read, sent as the list page itself.
            for (var k = 0; k < counted.length && lists < MAX_LISTS; k++) {
                if (seen.has(counted[k])) continue;
                lists++; seen.add(counted[k]);
                var list = await page(counted[k]);
                if (list === false) return;
                if (list) {
                    var listing = read(list.doc, list.url, counted[k], 'section', generation);
                    await verify(listing.links.filter(function (link) { return link.c; }), null);
                    if (owner() !== mark) return;
                    send(listing);
                }
            }
        } finally {
            send({v:1, g:generation, kind:'done', page:location.pathname + location.search, read:done, failed:failed});
        }
    }
    function scan(fetchSections, generation) {
        if (document.readyState === 'loading' || !document.body) return null;
        var page = location.pathname + location.search;
        var current = read(document, location.href, page, 'page', String(generation));
        current.fetching = false;
        if (!fetching && typeof __univerNative === 'function') {
            var sections = [];
            if (fetchSections) document.querySelectorAll('#l3 a[href],.univer-app-tabs a[href]').forEach(function (a) {
                var path = sitePath(a.getAttribute('href'), location.href);
                if (path && path !== '/' && path !== page && sections.indexOf(path) < 0 && sections.length < MAX_SECTIONS && /^\/[a-z]+\/[a-z]*\/?$|^\/[a-z]+\/?$/i.test(path)) sections.push(path);
            });
            var counted = fetchSections ? current.links.filter(function (link) { return link.n > 0 && !link.c && link.o === undefined && link.u !== page; })
                .map(function (link) { return link.u; }).filter(function (path, index, all) { return all.indexOf(path) === index; })
                : [];
            current.fetching = sections.length > 0 || counted.length > 0 || current.links.some(function (link) { return link.c; });
            if (current.fetching) {
                fetching = true;
                background(current, sections, counted, String(generation)).catch(function () { }).finally(function () { fetching = false; });
            }
        }
        return JSON.stringify(current);
    }
    window.__univerNoticesV1 = {scan:scan, present:present, check:check};
})();
