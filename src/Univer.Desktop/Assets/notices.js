/* Read-only reader of the site's notification marks: a red count in brackets next to a menu link, and a page message
   asking for a questionnaire first. The site keeps its count after answering; a list row with "+" counts as answered. */
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
    function links(doc, base) {
        var out = [];
        doc.querySelectorAll('#tools a[href],#l3 a[href],#l2 a[href],.univer-app-tabs a[href]').forEach(function (a) {
            if (out.length >= MAX_LINKS) return;
            var path = sitePath(a.getAttribute('href'), base), entry = item(a), text = entry.text;
            if (!path || !text || text.length > 200) return;
            var count = text.match(COUNT), title = clean(text.replace(COUNT, '')).slice(0, 100);
            if (count && !marked(entry.node) && !(entry.link && marked(entry.link))) count = null;
            var link = {u:path, t:count ? title || text.slice(0, 100) : text.slice(0, 100), n:count ? Math.min(+count[1], 9999) : 0};
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
    function read(doc, base, page, kind, generation) {
        var result = {v:1, g:generation, kind:kind, page:page, links:links(doc, base), blocks:blocks(doc, base, page)};
        result.links.forEach(function (link) { if (link.n > 0 && link.u === page) describe(link, doc); });
        // The whole main menu was read (not cut by the cap): a menu item it no longer lists is gone.
        result.menu = !!doc.getElementById('tools') && result.links.length < MAX_LINKS && result.links.some(function (link) { return link.m; });
        return result;
    }
    async function load(path) {
        var abort = new AbortController(), timer = setTimeout(function () { abort.abort(); }, 15000);
        try {
            var response = await fetch(path, {credentials:'same-origin', redirect:'follow', cache:'no-store', signal:abort.signal});
            var type = response.headers.get('content-type') || '';
            if (!response.ok || new URL(response.url).pathname !== new URL(path, location.href).pathname || !/text\/html/i.test(type)) return null;
            var bytes = await response.arrayBuffer();
            if (bytes.byteLength > 3000000) return null;
            var charset = (type.match(/charset=([\w-]+)/i) || [0, 'utf-8'])[1];
            var html; try { html = new TextDecoder(charset).decode(bytes); } catch (_) { html = new TextDecoder('utf-8').decode(bytes); }
            var doc = new DOMParser().parseFromString(html, 'text/html');
            // A sign-in page or a page without the menu (the read notice) says nothing about the section's marks.
            return doc.getElementById('login_frm') || !doc.getElementById('tools') ? false : {doc:doc, url:response.url};
        } finally { clearTimeout(timer); }
    }
    function send(result) {
        if (typeof window.__univerNativeNotify === 'function') window.__univerNativeNotify(PREFIX + JSON.stringify(result));
    }
    // Each section, then the lists the counts point to that no read has described yet (at most three in all).
    // The last message says how many pages were read and how many failed.
    async function background(sections, counted, generation) {
        var lists = 0, seen = new Set(), done = 0, failed = 0;
        async function page(path) {
            var loaded = await load(path).catch(function () { return null; });
            if (loaded) done++; else failed++;
            return loaded;
        }
        try {
            for (var i = 0; i < sections.length; i++) {
                // A section that fails to load (a network error, a timeout) does not end the others; a sign-in page does.
                var section = await page(sections[i]);
                if (section === false) return;
                if (!section) continue;
                var result = read(section.doc, section.url, sections[i], 'section', generation);
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
                if (list) send(read(list.doc, list.url, counted[k], 'section', generation));
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
        if (fetchSections && !fetching && typeof window.__univerNativeNotify === 'function') {
            var sections = [];
            document.querySelectorAll('#l3 a[href],.univer-app-tabs a[href]').forEach(function (a) {
                var path = sitePath(a.getAttribute('href'), location.href);
                if (path && path !== '/' && path !== page && sections.indexOf(path) < 0 && sections.length < MAX_SECTIONS && /^\/[a-z]+\/[a-z]*\/?$|^\/[a-z]+\/?$/i.test(path)) sections.push(path);
            });
            var counted = current.links.filter(function (link) { return link.n > 0 && link.o === undefined && link.u !== page; })
                .map(function (link) { return link.u; }).filter(function (path, index, all) { return all.indexOf(path) === index; });
            current.fetching = sections.length > 0 || counted.length > 0;
            if (current.fetching) {
                fetching = true;
                background(sections, counted, String(generation)).catch(function () { }).finally(function () { fetching = false; });
            }
        }
        return JSON.stringify(current);
    }
    window.__univerNoticesV1 = {scan:scan};
})();
