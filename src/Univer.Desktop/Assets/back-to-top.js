/* «Наверх»: one floating button once a page has been scrolled well down, in the reading layout only (Original has none).
   It lives in its own shadow root, so the site's styles stay out of it and showing it never wakes the layout adapter's DOM
   observer. It always stands in the same place, a little above the bottom right corner, and never moves: a button that
   dodged controls jumped about under the reader (the owner's request). Instead the page makes room: when a small control
   would pass under the button (a service's «Подать заявку»), the page column narrows once, as soon as such a control is
   there (as a rule when the page is set up), so the button stands beside the page. A window too short for the button or
   too narrow to spare the room keeps its full column, and there the button stays off a page where it would cover a
   control. It hides while a dialog, menu or drag is under way. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerTopV1 || window !== window.top) return;
    var WORDS = { ru: 'Наверх', kz: 'Жоғарыға', en: 'Back to top' };
    // What a click would reach. A wide one (a row that is one big link) stays clickable beside the button and makes no room.
    var CONTROLS = 'a[href],button,input,select,textarea,summary,label,[role=button],[role=link],[role=tab],[role=menuitem],[role=checkbox],[role=radio],[role=switch],[onclick],[contenteditable=""],[contenteditable=true]';
    var FOCUSABLE = 'a[href],button,input:not([type=hidden]),select,textarea,summary,[tabindex]:not([tabindex="-1"])';
    // Open dialogs, menus and lists, and the tile bin while a tile is dragged: the button waits until they close.
    var OPEN = '#univer-app-report,#simplemodal-container,.univer-app-add-layer,.univer-app-select,.univer-app-menu[open],.univer-app-trash-on,.modal.in,.modal.show,.ui-dialog';
    // Right gap, gap below, height; shown past SHOW screens, hidden again above HIDE.
    var EDGE = 24, LIFT = 56, HEIGHT = 40, PAD = 6, SHOW = 1.2, HIDE = 0.9;
    // The room: html[data-univer-top-room] in steps of 16 px, matched by desktop.js. Controls wider than NARROW and those
    // reaching past the window (inside a box that scrolls sideways) make none.
    var ROOM = 'data-univer-top-room', NARROW = 480, OVERLAP = 24, MIN_ROOM = 144, MAX_ROOM = 240;
    // The button needs a window at least MIN_VIEW tall; the room is given only while MIN_COLUMN px of page (a phone's width)
    // remain beside it. The app's sidebar grows with the zoom: the QA window's page is 656 px at 150% (room made) and
    // 480 px at 200% (no room).
    var MIN_VIEW = 300, MIN_COLUMN = 360;
    // Built node by node: the stores' checks refuse HTML text written into the page.
    function icon() {
        var ns = 'http://www.w3.org/2000/svg', svg = document.createElementNS(ns, 'svg');
        [['viewBox', '0 0 24 24'], ['fill', 'none'], ['stroke', 'currentColor'], ['stroke-width', '2.2'], ['stroke-linecap', 'round'],
            ['stroke-linejoin', 'round'], ['aria-hidden', 'true']].forEach(function (pair) { svg.setAttribute(pair[0], pair[1]); });
        ['m5 12 7-7 7 7', 'M12 19V5'].forEach(function (d) { var path = document.createElementNS(ns, 'path'); path.setAttribute('d', d); svg.appendChild(path); });
        return svg;
    }
    var CSS = ':host{all:initial}' +
        '.top{position:fixed;z-index:800;right:24px;bottom:56px;display:inline-flex;align-items:center;gap:8px;box-sizing:border-box;height:40px;margin:0;padding:0 18px 0 14px;' +
        'border:0;border-radius:20px;background:var(--u-brand,#1b5fa6);color:#fff;font:600 14px/1 var(--u-font,"Segoe UI",Tahoma,sans-serif);white-space:nowrap;cursor:pointer;' +
        'box-shadow:0 6px 18px rgba(16,40,70,.22),0 1px 3px rgba(16,40,70,.12);opacity:0;visibility:hidden;transform:translateY(8px);' +
        'transition:opacity .16s ease,transform .2s cubic-bezier(.22,1,.36,1),background-color .14s ease,visibility 0s linear .2s}' +
        '.top.on{opacity:1;visibility:visible;transform:none;transition:opacity .16s ease,transform .2s cubic-bezier(.22,1,.36,1),background-color .14s ease,visibility 0s}' +
        '.top:hover{background:var(--u-brand-strong,#154d88)}' +
        '.top.on:active{transform:scale(.97)}' +
        '.top:focus-visible{outline:2px solid var(--u-brand,#1b5fa6);outline-offset:3px}' +
        '.top svg{flex:none;width:18px;height:18px}' +
        ':host-context(html.univer-app-dark) .top{background:#2563a8;box-shadow:0 6px 18px rgba(0,0,0,.45),0 1px 3px rgba(0,0,0,.3)}' +
        ':host-context(html.univer-app-dark) .top:hover{background:#2d70bb}' +
        ':host-context(html.univer-app-dark) .top:focus-visible{outline-color:#8ab8ec}' +
        '.top.still,.top.still.on{transition:none;transform:none}' +
        '@media (prefers-reduced-motion:reduce){.top,.top.on{transform:none;transition:opacity .16s ease,visibility 0s linear .16s}.top.on{transition:opacity .16s ease}}' +
        '@media print{.top{display:none!important}}';
    var enabled = false, language = 'ru', host = null, button = null, label = null, visible = false, idle = 0, frame = 0, checked = 0, cramped = false, lastMark = '', lastFound = false;
    var still = function () { return window.__univerMotion === false; };
    var reduced = function () { return still() || !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches; };
    // The visible page area without scrollbars; the fixed button is placed against the same box.
    var view = function () {
        var visual = window.visualViewport;
        return { width: visual ? visual.width : innerWidth, height: visual ? visual.height : innerHeight };
    };
    function build() {
        host = document.createElement('div'); host.id = 'univer-app-top';
        var root = host.attachShadow({ mode: 'open' }), style = document.createElement('style');
        style.textContent = CSS;
        button = document.createElement('button'); button.type = 'button'; button.className = 'top'; button.append(icon(), document.createElement('span'));
        label = button.lastChild;
        button.addEventListener('click', toTop);
        root.append(style, button);
    }
    function ensure() {
        if (!host) build();
        if (!host.isConnected) (document.body || document.documentElement).appendChild(host);
        if (label.textContent !== WORDS[language]) label.textContent = WORDS[language];
        button.classList.toggle('still', still());
    }
    function show() { if (!visible) { visible = true; button.classList.add('on'); } }
    function hide() { if (visible) { visible = false; button.classList.remove('on'); } }
    function dialogOpen() {
        return Array.prototype.some.call(document.querySelectorAll(OPEN), function (node) {
            if (!node.getClientRects().length) return false;
            var style = getComputedStyle(node);
            return style.display !== 'none' && style.visibility !== 'hidden' && parseFloat(style.opacity) !== 0;
        });
    }
    // Whether a small control lies under the button's band anywhere the button can show: it appears past SHOW screens and
    // stays on the way back up until HIDE, so it covers just what is below HIDE screens of page. Read from the layout,
    // never written while reading.
    function crowded(size, width) {
        var main = document.getElementById('l4'), page = document.documentElement.scrollHeight;
        if (!main || page <= size.height * (SHOW + 1)) return false;
        var y = window.scrollY || window.pageYOffset || 0;
        var from = size.height * (HIDE + 1) - LIFT - HEIGHT - PAD, to = page - LIFT + PAD;
        var left = size.width - EDGE - width - PAD, right = size.width - EDGE + PAD;
        return Array.prototype.some.call(main.querySelectorAll(CONTROLS), function (node) {
            var box = node.getBoundingClientRect();
            if (!box.width || !box.height || box.width > NARROW || box.right > size.width + 1) return false;
            return box.bottom + y > from && box.top + y < to && Math.min(box.right, right) - Math.max(box.left, left) >= OVERLAP;
        });
    }
    // Room for the button once a page needs it. Kept for the rest of the document: the narrower column no longer meets
    // the button, and widening it again would bring the control back under it.
    // A window too narrow to spare the room sets `cramped` instead when a control lies under the button's place: the button
    // then stays hidden on that page rather than cover it.
    // The width is the layout one (offsetWidth), so a press, which scales the button, does not change the room.
    function room(size, force) {
        var root = document.documentElement, width = button.offsetWidth || 120;
        var need = Math.min(MAX_ROOM, Math.max(MIN_ROOM, Math.ceil((EDGE + width + 16) / 16) * 16)) + '';
        var held = root.getAttribute(ROOM), fits = size.width - need >= MIN_COLUMN;
        // A room given up leaves the control under the button again: that is checked at once.
        if (size.height < MIN_VIEW || !fits) { if (held) { root.removeAttribute(ROOM); force = true; lastMark = ''; } }
        else { cramped = false; if (held) { if (held !== need) root.setAttribute(ROOM, need); return; } }
        if (size.height < MIN_VIEW) return;
        var now = Date.now();
        // A clock set back does not stop the checks.
        if (!force && now >= checked && now - checked < 1000) return;
        checked = now;
        // The app asks again with every poll. A page that looks as it did at the last check (the same window, height and
        // number of elements) has its controls where they were: they are not measured again, whether a control was found or not.
        var main = document.getElementById('l4');
        var mark = [size.width, size.height, width, root.scrollHeight, main ? main.getElementsByTagName('*').length : 0].join('|');
        var found;
        if (!force && mark === lastMark) found = lastFound;
        else { found = crowded(size, width); lastMark = mark; lastFound = found; }
        if (!fits) cramped = found;
        else if (found) root.setAttribute(ROOM, need);
    }
    function refresh(force) {
        clearTimeout(idle); idle = 0;
        if (!enabled) return;
        ensure();
        var size = view(), y = window.scrollY || window.pageYOffset || 0;
        room(size, force === true);
        var wanted = visible ? y >= size.height * HIDE : y > size.height * SHOW;
        if (!wanted || size.height < MIN_VIEW || cramped || dialogOpen()) hide(); else show();
    }
    // While the page moves only the distance matters; the rest is checked once it is still.
    function scrolled() {
        if (!enabled) return;
        if (!frame) frame = requestAnimationFrame(function () {
            frame = 0;
            var y = window.scrollY || window.pageYOffset || 0, height = view().height;
            if (visible && y < height * HIDE) hide();
            else if (!visible && y > height * SHOW) refresh();
        });
        clearTimeout(idle); idle = setTimeout(refresh, 160);
    }
    function later() { if (enabled) { clearTimeout(idle); idle = setTimeout(refresh, 300); } }
    function toTop() {
        hide();
        window.scrollTo({ top: 0, left: window.scrollX || 0, behavior: reduced() ? 'auto' : 'smooth' });
        // Tab continues from the top of the page, not from the end where the button sits.
        var first = Array.prototype.find.call(document.querySelectorAll(FOCUSABLE), function (node) { return !node.disabled && node.getClientRects().length > 0; });
        if (first) first.focus({ preventScroll: true }); else button.blur();
    }
    window.addEventListener('scroll', scrolled, { passive: true });
    window.addEventListener('resize', function () { checked = 0; lastMark = ''; later(); });
    document.addEventListener('click', later, true);
    document.addEventListener('keyup', later, true);
    window.__univerTopV1 = {
        configure: function (on, lang) {
            enabled = !!on; language = WORDS[lang] ? lang : 'ru';
            if (enabled) { refresh(); return; }
            clearTimeout(idle); idle = 0; visible = false; checked = 0; cramped = false; lastMark = ''; lastFound = false;
            document.documentElement.removeAttribute(ROOM);
            if (host) { host.remove(); host = null; button = label = null; }
        },
        refresh: function () { refresh(true); }
    };
})();
