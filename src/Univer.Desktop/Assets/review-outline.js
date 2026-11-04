/* Read-only markup outline of an observed page: tags, classes, masked ids and paths, handler names, short labels.
   Data cells keep only length and numeric look; field values, hidden inputs, cookies and scripts are never read. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz') return '';
    var root = document.querySelector('#l4 td.center') || document.querySelector('#l4');
    if (!root) return JSON.stringify({path:location.pathname.replace(/\d+/g, '{id}'), missing:true});
    var count = 0, jq = window.jQuery;
    function mask(value) { return String(value || '').replace(/\d+/g, '{id}'); }
    function label(e) { return (e.textContent || '').replace(/\s+/g, ' ').trim(); }
    function events(e) {
        try { var data = jq && (jq._data ? jq._data(e, 'events') : jq.data(e, 'events')); return data ? Object.keys(data) : []; } catch (_) { return []; }
    }
    function node(e, depth) {
        if (++count > 900) return null;
        var item = {t:e.tagName.toLowerCase()};
        var own = String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : e.className || '').split(/\s+/).filter(function (c) { return c && !/^univer-app/.test(c); });
        if (own.length) item.c = own.join(' ');
        if (e.id) item.id = mask(e.id);
        var attrs = Array.from(e.attributes).map(function (a) { return a.name; }).filter(function (n) { return !/^(class|id|style|data-univer.*)$/.test(n); });
        if (attrs.length) item.a = attrs;
        var handlers = attrs.filter(function (n) { return /^on/.test(n); }).map(function (n) { return n + ':' + mask(e.getAttribute(n)).slice(0, 60); });
        if (handlers.length) item.on = handlers;
        var bound = events(e); if (bound.length) item.jq = bound;
        if (e.tagName === 'A' && e.getAttribute('href')) item.h = mask(e.getAttribute('href')).slice(0, 80);
        if (e.tagName === 'IMG') item.src = mask(e.getAttribute('src')).replace(/^.*\//, '');
        if (e.tagName === 'INPUT') { item.type = e.type; if (/^(button|submit|reset)$/.test(e.type)) item.x = e.value.slice(0, 60); return item; }
        if (/^(SELECT|TEXTAREA|SCRIPT|STYLE)$/.test(e.tagName)) return item;
        var text = label(e);
        if (/^(TH|A|BUTTON|B|STRONG|H1|H2|H3|H4|LABEL|CAPTION|LEGEND)$/.test(e.tagName) || (e.matches('.ct,.ts') && !e.matches('.message') && text.length <= 120)) item.x = text.slice(0, 80);
        else if (/^(TD|SPAN|DIV|P|LI)$/.test(e.tagName) && !e.children.length && text) { item.n = text.length; if (/^[\d\s.,*()%+-]+$/.test(text)) item.num = true; }
        var children = Array.from(e.children).filter(function (c) { return !/^(SCRIPT|STYLE|NOSCRIPT)$/.test(c.tagName) && !(c.tagName === 'INPUT' && c.type === 'hidden'); });
        if (children.length && depth) {
            // Long lists keep their first rows and the last one: enough to see the repeating pattern.
            var picked = children.length > 8 ? children.slice(0, 6).concat(children.slice(-1)) : children;
            item.k = picked.map(function (c) { return node(c, depth - 1); }).filter(Boolean);
            if (children.length > picked.length) item.more = children.length - picked.length;
        }
        return item;
    }
    return JSON.stringify({path:location.pathname.replace(/\d+/g, '{id}'), title:mask(document.title).slice(0, 80), outline:node(root, 16)});
})();
