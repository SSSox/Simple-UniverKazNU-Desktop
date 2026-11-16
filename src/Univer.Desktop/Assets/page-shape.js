/* The shape of the visible page for the loading placeholder: boxes (cards, controls, tiles, plates) and lines of text, as
   rectangles in CSS pixels from the top of the document. Only geometry leaves the page: no text, values or addresses. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || !document.body) return null;
    // A scrolled page has its pinned header where the reader is; the next visit opens at the top.
    if (scrollY > 4) return null;
    var root = document.documentElement, width = root.clientWidth, outer = innerWidth, height = innerHeight, top = scrollY, limit = 700;
    var items = [], accents = new Set();
    function colour(value) {
        var m = /rgba?\(([^)]+)\)/.exec(value || ''); if (!m) return null;
        var p = m[1].split(/[\s,\/]+/).filter(Boolean).map(parseFloat);
        return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1];
    }
    var page = colour(getComputedStyle(document.body).backgroundColor);
    if (!page || page[3] < .5) page = colour(getComputedStyle(root).backgroundColor) || [255, 255, 255, 1];
    // The colour a box is painted on: its nearest ancestor with a solid background, else the page.
    var grounds = new Map();
    function ground(node) {
        var parent = node.parentElement;
        if (!parent || parent === document.body || parent === root) return page;
        if (grounds.has(parent)) return grounds.get(parent);
        var c = colour(getComputedStyle(parent).backgroundColor), found = c && c[3] >= .5 ? c : ground(parent);
        grounds.set(parent, found); return found;
    }
    // 0 no plate, 1 a card lighter than what it lies on, 2 a track darker than it, 3 a coloured plate (the selected tab, a
    // primary button).
    function plate(c, under) {
        if (!c || c[3] < .5) return 0;
        var max = Math.max(c[0], c[1], c[2]), min = Math.min(c[0], c[1], c[2]);
        if (max - min > 60) return 3;
        var light = (c[0] + c[1] + c[2]) / 3, base = (under[0] + under[1] + under[2]) / 3;
        return Math.abs(light - base) < 4 ? 0 : light > base ? 1 : 2;
    }
    // A rule under or over a row: one visible horizontal border on a wide box.
    function rule(style, side) {
        var c = colour(style['border' + side + 'Color']);
        return parseFloat(style['border' + side + 'Width']) >= 1 && style['border' + side + 'Style'] !== 'none' && c && c[3] > .2;
    }
    function edge(style) {
        var sides = ['Top', 'Right', 'Bottom', 'Left'].filter(function (side) {
            var c = colour(style['border' + side + 'Color']);
            return parseFloat(style['border' + side + 'Width']) > 0 && style['border' + side + 'Style'] !== 'none' && c && c[3] > .2;
        });
        if (sides.length < 4) return 0;
        return style.borderTopStyle === 'dashed' ? 2 : 1;
    }
    function inAccent(node) { for (var i = 0; node && i < 8; i++, node = node.parentElement) if (accents.has(node)) return true; return false; }
    function add(kind, r, radius, fill, line) {
        if (items.length >= limit) return;
        items.push([kind, Math.round(r.left), Math.round(r.top + top), Math.round(r.width), Math.round(r.height), Math.round(radius || 0), fill || 0, line || 0]);
    }
    // Row rules: the cells of one row share one rule, so neighbours on the same line join into one; short ones are dropped.
    var rules = new Map(), ruled = new Set();
    function addRule(left, y, w) {
        var key = Math.round(y + top), prev = rules.get(key);
        if (prev && left >= prev[1] - 2 && left <= prev[1] + prev[3] + 2) { prev[3] = Math.max(prev[3], Math.round(left + w) - prev[1]); return; }
        if (items.length >= limit) return;
        var item = [0, Math.round(left), key, Math.round(w), 1, 0, 2, 0]; items.push(item); rules.set(key, item); ruled.add(item);
    }
    // The first screen of the document, wherever the reader has scrolled since.
    function seen(r) { return r.width >= 2 && r.height >= 2 && r.bottom + top > 0 && r.top + top < height && r.right > 0 && r.left < width; }
    var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
        acceptNode: function (node) {
            if (node.nodeType === 3) return /\S/.test(node.data) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT;
            if (/^(SCRIPT|STYLE|NOSCRIPT|TEMPLATE|OPTION|DATALIST)$/.test(node.tagName)) return NodeFilter.FILTER_REJECT;
            if (node.ownerSVGElement) return NodeFilter.FILTER_REJECT;
            // A closed menu's items (the account menu is a details element) are not on screen.
            if (node.tagName !== 'SUMMARY' && node.parentElement && node.parentElement.closest('details:not([open])') && !node.closest('summary')) return NodeFilter.FILTER_REJECT;
            if (node.checkVisibility && !node.checkVisibility({checkOpacity:true, checkVisibilityCSS:true})) return NodeFilter.FILTER_REJECT;
            var style = getComputedStyle(node);
            if (style.display === 'none' || style.visibility === 'hidden' || +style.opacity < .05) return NodeFilter.FILTER_REJECT;
            var r = node.getBoundingClientRect();
            // Toasts, dialogs and floating menus are not the page; a bar pinned across the top is.
            if (style.position === 'fixed' && !(r.top <= 1 && r.width >= width * .9)) return NodeFilter.FILTER_REJECT;
            if (r.top + top > height && style.position !== 'absolute') return NodeFilter.FILTER_REJECT;
            return NodeFilter.FILTER_ACCEPT;
        }
    });
    var node, line = null;
    while ((node = walker.nextNode()) && items.length < limit) {
        if (node.nodeType === 3) {
            var parent = node.parentElement;
            if (!parent || /^(TEXTAREA|SELECT|OPTION)$/.test(parent.tagName) || inAccent(parent)) continue;
            var range = document.createRange(); range.selectNodeContents(node);
            Array.from(range.getClientRects()).forEach(function (r) {
                if (!seen(r)) return;
                // Neighbouring pieces of one line become one bar.
                if (line && Math.abs(line.top - r.top) < 3 && r.left - line.right < 8 && r.left >= line.left) { line.right = r.right; return; }
                if (line) add(1, {left:line.left, top:line.top + line.height * .22, width:line.right - line.left, height:Math.max(6, line.height * .56)}, 4);
                line = {left:r.left, right:r.right, top:r.top, height:r.height};
            });
            continue;
        }
        var style = getComputedStyle(node), r = node.getBoundingClientRect();
        if (!seen(r)) continue;
        if (/^(IMG|SVG|CANVAS|VIDEO|svg)$/.test(node.tagName)) { if (r.width <= 96 && r.height <= 96 && !inAccent(node)) add(1, r, Math.min(r.width, r.height) / 4); continue; }
        if (r.width >= width - 2 && r.height >= height - 2) continue;
        var fill = plate(colour(style.backgroundColor), ground(node)), border = edge(style);
        if (/^(INPUT)$/.test(node.tagName) && /^(checkbox|radio|hidden)$/i.test(node.type)) continue;
        if (!fill && !border) {
            if (r.width >= 16 && rule(style, 'Bottom')) addRule(r.left, r.bottom - 1, r.width);
            else if (r.width >= 16 && rule(style, 'Top')) addRule(r.left, r.top, r.width);
            continue;
        }
        if (fill === 3) accents.add(node);
        add(0, r, parseFloat(style.borderTopLeftRadius) || 0, fill, border);
    }
    if (line) add(1, {left:line.left, top:line.top + line.height * .22, width:line.right - line.left, height:Math.max(6, line.height * .56)}, 4);
    items = items.filter(function (item) { return !ruled.has(item) || item[3] >= 120; });
    return JSON.stringify({v:1, w:width, o:outer, h:height, items:items});
})();
