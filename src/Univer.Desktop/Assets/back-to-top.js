/* «Наверх»: one floating button once a page has been scrolled well down, in the reading layout only (Original has none).
   It lives in its own shadow root, so the site's styles stay out of it and showing or moving it never wakes the layout
   adapter's DOM observer. By default it never covers a control: from the bottom right it climbs the right edge, then
   tries the left one, and stays hidden while no spot is free. The phone's anchored button keeps its corner instead.
   Either way it hides while a dialog, menu or drag is under way. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerTopV1 || window !== window.top) return;
    var WORDS = { ru: 'Наверх', kz: 'Жоғарыға', en: 'Back to top' };
    // What a click would reach. A covered one moves the button; a clickable panel larger than a quarter of the window
    // does not, or the button could never show over a page that is one big link.
    var CONTROLS = 'a[href],button,input,select,textarea,summary,label,[role=button],[role=link],[role=tab],[role=menuitem],[role=checkbox],[role=radio],[role=switch],[onclick],[contenteditable=""],[contenteditable=true]';
    var FOCUSABLE = 'a[href],button,input:not([type=hidden]),select,textarea,summary,[tabindex]:not([tabindex="-1"])';
    // Open dialogs, menus and lists, and the tile bin while a tile is dragged: the button waits until they close.
    var OPEN = '#simplemodal-container,.univer-app-add-layer,.univer-app-select,.univer-app-menu[open],.univer-app-trash-on,.modal.in,.modal.show,.ui-dialog';
    // Edge gap and height: 24 and 40 px on Windows; the phone passes larger ones for the thumb.
    var EDGE = 24, HEIGHT = 40, STEP = 12, PAD = 6, SHOW = 1.2, HIDE = 0.9;
    var ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m5 12 7-7 7 7"/><path d="M12 19V5"/></svg>';
    var CSS = ':host{all:initial}' +
        '.top{position:fixed;z-index:800;right:24px;bottom:24px;display:inline-flex;align-items:center;gap:8px;box-sizing:border-box;height:40px;margin:0;padding:0 18px 0 14px;' +
        'border:0;border-radius:20px;background:var(--u-brand,#1b5fa6);color:#fff;font:600 14px/1 var(--u-font,"Segoe UI",Tahoma,sans-serif);white-space:nowrap;cursor:pointer;' +
        'box-shadow:0 6px 18px rgba(16,40,70,.22),0 1px 3px rgba(16,40,70,.12);opacity:0;visibility:hidden;transform:translateY(8px);' +
        'transition:opacity .16s ease,transform .2s cubic-bezier(.22,1,.36,1),bottom .2s cubic-bezier(.22,1,.36,1),background-color .14s ease,visibility 0s linear .2s}' +
        '.top.on{opacity:1;visibility:visible;transform:none;transition:opacity .16s ease,transform .2s cubic-bezier(.22,1,.36,1),bottom .2s cubic-bezier(.22,1,.36,1),background-color .14s ease,visibility 0s}' +
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
    var enabled = false, language = 'ru', host = null, button = null, label = null, visible = false, idle = 0, frame = 0, edge = EDGE, tall = HEIGHT, anchored = false;
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
        button = document.createElement('button'); button.type = 'button'; button.className = 'top'; button.innerHTML = ICON + '<span></span>';
        label = button.lastChild;
        button.addEventListener('click', toTop);
        root.append(style, button);
    }
    function ensure() {
        if (!host) build();
        if (!host.isConnected) (document.body || document.documentElement).appendChild(host);
        if (label.textContent !== WORDS[language]) label.textContent = WORDS[language];
        button.classList.toggle('still', still());
        var height = tall + 'px';
        if (button.style.height !== height) { button.style.height = height; button.style.borderRadius = tall / 2 + 'px'; }
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
    // The scroll bar of a box that scrolls on its own (an attendance matrix), when the point is on it: a click there drags
    // the bar. Geometry first, the style only for the rare point inside a scroll bar's band.
    function scrollbar(node, x, y) {
        if (node === document.documentElement || node === document.body || !node.clientHeight) return null;
        var box = node.getBoundingClientRect(), below = box.top + node.clientTop + node.clientHeight, beside = box.left + node.clientLeft + node.clientWidth;
        var across = node.scrollWidth > node.clientWidth && y >= below && y < box.bottom, down = node.scrollHeight > node.clientHeight && x >= beside && x < box.right;
        if (!across && !down) return null;
        var style = getComputedStyle(node);
        if (across && /auto|scroll/.test(style.overflowX)) return { top: below };
        if (down && /auto|scroll/.test(style.overflowY)) return { top: box.top };
        return null;
    }
    // The control a click at this point would reach, if it is small enough to matter.
    function controlAt(x, y, area) {
        var hits = document.elementsFromPoint(x, y);
        for (var i = 0; i < hits.length; i++) {
            if (hits[i] === host) continue;
            var bar = scrollbar(hits[i], x, y);
            if (bar) return bar;
            var control = hits[i].closest(CONTROLS);
            if (!control) return null;
            var box = control.getBoundingClientRect();
            return box.width * box.height < area / 4 ? box : null;
        }
        return null;
    }
    // The top edge of the first control under the button's box with a small margin, or -1 when the box is free.
    function blocker(left, top, width, size) {
        var right = left + width + PAD, bottom = top + tall + PAD, area = size.width * size.height;
        var columns = Math.ceil((width + PAD * 2) / 16), rows = Math.ceil((tall + PAD * 2) / 13);
        for (var r = 0; r <= rows; r++)
            for (var c = 0; c <= columns; c++) {
                var x = Math.min(Math.max(left - PAD + (right - left + PAD) * c / columns, 0), size.width - 1);
                var y = Math.min(Math.max(top - PAD + (bottom - top + PAD) * r / rows, 0), size.height - 1);
                var box = controlAt(x, y, area);
                if (box) return box.top;
            }
        return -1;
    }
    // The lowest free spot on the right edge, then on the left, within the window's lower half. An anchored button (the
    // phone, where cards fill the width) keeps its corner like a floating action button: there every stop moved it or,
    // when it waited for a free spot instead, made it blink in and out. The page end reserves room under it.
    function findSpot() {
        var size = view(), width = Math.ceil(button.getBoundingClientRect().width) || 120;
        if (anchored) return { side: 'right', lift: edge };
        var sides = [{ side: 'right', left: size.width - edge - width }, { side: 'left', left: edge }];
        for (var s = 0; s < sides.length; s++) {
            if (sides[s].left < 0) continue;
            for (var lift = edge; size.height - lift - tall >= size.height / 2;) {
                var top = size.height - lift - tall, block = blocker(sides[s].left, top, width, size);
                if (block < 0) return { side: sides[s].side, lift: lift };
                // Jump past the control that is in the way instead of creeping over it.
                lift = Math.max(lift + STEP, Math.ceil(size.height - block + PAD));
            }
        }
        return null;
    }
    function place(spot) {
        var style = button.style, bottom = spot.lift + 'px';
        if (spot.side === 'left') { if (style.left !== edge + 'px') { style.left = edge + 'px'; style.right = 'auto'; } }
        else if (style.right !== edge + 'px') { style.right = edge + 'px'; style.left = 'auto'; }
        if (style.bottom !== bottom) style.bottom = bottom;
    }
    function refresh() {
        clearTimeout(idle); idle = 0;
        if (!enabled) return;
        ensure();
        var size = view(), y = window.scrollY || window.pageYOffset || 0;
        var wanted = visible ? y >= size.height * HIDE : y > size.height * SHOW;
        if (!wanted || size.height < 300 || dialogOpen()) { hide(); return; }
        var spot = findSpot();
        if (!spot) { hide(); return; }
        place(spot); show();
    }
    // While the page moves only the distance matters; where to stand is settled once it is still.
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
    window.addEventListener('resize', later);
    document.addEventListener('click', later, true);
    document.addEventListener('keyup', later, true);
    window.__univerTopV1 = {
        configure: function (on, lang, options) {
            enabled = !!on; language = WORDS[lang] ? lang : 'ru';
            edge = options && options.edge > 0 ? options.edge : EDGE; tall = options && options.height > 0 ? options.height : HEIGHT;
            anchored = !!(options && options.anchored);
            if (enabled) { refresh(); return; }
            clearTimeout(idle); idle = 0; visible = false;
            if (host) { host.remove(); host = null; button = label = null; }
        },
        refresh: refresh
    };
})();
