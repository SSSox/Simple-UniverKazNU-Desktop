/* Dark theme of the reading layout. Original layout never changes. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerThemeV1) return;
    // Every colour keeps its contrast with white: (L' + .05) = k / (L + .05) turns the white card into the app's dark card
    // (29,34,41) with hue and saturation kept, so text stays as readable as in the light layout.
    var CARD = [29, 34, 41], PAGE = '#15191e';
    var cache = new Map(), states = new WeakMap(), loading = new WeakSet();
    var OWN = /^univer-app-dark-/;
    // Colour names old pages use in bgcolor and font colour attributes.
    var NAMES = { red: '#ff0000', green: '#008000', blue: '#0000ff', yellow: '#ffff00', gray: '#808080', grey: '#808080', silver: '#c0c0c0', maroon: '#800000',
        navy: '#000080', purple: '#800080', teal: '#008080', olive: '#808000', lime: '#00ff00', aqua: '#00ffff', fuchsia: '#ff00ff', orange: '#ffa500',
        brown: '#a52a2a', pink: '#ffc0cb', darkblue: '#00008b', darkred: '#8b0000', darkgreen: '#006400', lightgray: '#d3d3d3', lightgrey: '#d3d3d3',
        lightblue: '#add8e6', lightyellow: '#ffffe0', lightgreen: '#90ee90', whitesmoke: '#f5f5f5', gold: '#ffd700', white: '#ffffff', black: '#000000' };
    // Colour properties as Chromium serialises them in a rule's declarations.
    var PROPS = /^(color|background-color|background-image|border-(top|right|bottom|left)-color|outline-color|text-decoration-color|column-rule-color|caret-color|fill|stroke|accent-color|-webkit-text-fill-color|--.*)$/;
    function linear(c) { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }
    function luminance(r, g, b) { return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b); }
    function toHsl(r, g, b) {
        r /= 255; g /= 255; b /= 255;
        var max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2, h = 0, s = 0, d = max - min;
        if (d) { s = l > 0.5 ? d / (2 - max - min) : d / (max + min); h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4; h /= 6; }
        return [h, s, l];
    }
    function hue(p, q, t) { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3 ? p + (q - p) * (2 / 3 - t) * 6 : p; }
    function fromHsl(h, s, l) {
        if (s <= 0) { var v = Math.round(l * 255); return [v, v, v]; }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        return [hue(p, q, h + 1 / 3), hue(p, q, h), hue(p, q, h - 1 / 3)].map(function (c) { return Math.max(0, Math.min(255, Math.round(c * 255))); });
    }
    var SCALE = (luminance(CARD[0], CARD[1], CARD[2]) + 0.05) * 1.05, CARD_HSL = toHsl(CARD[0], CARD[1], CARD[2]);
    function dark(r, g, b) {
        var key = r + ',' + g + ',' + b, known = cache.get(key);
        if (known) return known;
        var target = Math.max(0, Math.min(1, SCALE / (luminance(r, g, b) + 0.05) - 0.05)), x = toHsl(r, g, b);
        if (x[1] < 0.05) { x[0] = CARD_HSL[0]; x[1] = CARD_HSL[1]; }
        var low = 0, high = 1;
        for (var i = 0; i < 22; i++) { var mid = (low + high) / 2, c = fromHsl(x[0], x[1], mid); if (luminance(c[0], c[1], c[2]) < target) low = mid; else high = mid; }
        var result = fromHsl(x[0], x[1], (low + high) / 2);
        if (cache.size < 4096) cache.set(key, result);
        return result;
    }
    function hex(c) { return c.map(function (v) { return (v < 16 ? '0' : '') + v.toString(16); }).join(''); }
    function expand(value) { return value.length <= 4 ? value.split('').map(function (c) { return c + c; }).join('') : value; }
    function colours(text, mark) {
        var result = text.replace(/rgba?\(\s*(\d{1,3})[\s,]+(\d{1,3})[\s,]+(\d{1,3})\s*(?:[,/]\s*([\d.]+%?)\s*)?\)/gi, function (match, r, g, b, a) {
            var c = dark(+r, +g, +b);
            return a === undefined ? 'rgb(' + c.join(', ') + ')' : 'rgba(' + c.join(', ') + ', ' + a + ')';
        });
        result = result.replace(/(#|%23)([0-9a-f]{8}|[0-9a-f]{6}|[0-9a-f]{3,4})\b/gi, function (match, found, digits) {
            var full = expand(digits), c = dark(parseInt(full.slice(0, 2), 16), parseInt(full.slice(2, 4), 16), parseInt(full.slice(4, 6), 16));
            return found + hex(c) + full.slice(6);
        });
        // Only the keywords themselves: var(--white) and --black-text are names, not colours.
        return result.replace(/(?<![-\w])(white|black)(?![-\w])/gi, function (word) { return mark + hex(word.toLowerCase() === 'white' ? dark(255, 255, 255) : dark(0, 0, 0)); });
    }
    // Rewrites the colours in one declaration value; shadows stay (a lit shadow would glow). In an SVG data address a
    // colour becomes %23rrggbb, since a bare # would end the address; other addresses stay.
    function map(value, name) {
        if (!value || /shadow/.test(name)) return value;
        if (name === 'background-image' && !/gradient|data:image\/svg/.test(value)) return value;
        return value.split(/(url\((?:"[^"]*"|'[^']*'|[^)]*)\))/i).map(function (part, i) {
            if (i % 2 === 0) return colours(part, '#');
            return /^url\(\s*["']?data:image\/svg/i.test(part) ? colours(part, '%23') : part;
        }).join('');
    }
    // A longhand set through a shorthand with var() reads empty; the shorthand still holds the value.
    var SHORTHANDS = { background: /^background-/, border: /^border-/, outline: /^outline-/, 'text-decoration': /^text-decoration-/, 'column-rule': /^column-rule-/ };
    function shorthand(style, name) {
        var candidates = [];
        var side = name.match(/^border-(top|right|bottom|left)-color$/);
        if (side) candidates.push('border-' + side[1], 'border-color');
        Object.keys(SHORTHANDS).forEach(function (key) { if (SHORTHANDS[key].test(name)) candidates.push(key); });
        for (var i = 0; i < candidates.length; i++) if (style.getPropertyValue(candidates[i])) return candidates[i];
        return null;
    }
    // Every colour declaration of a rule, mapped or not: the copies then cascade among themselves exactly as the
    // originals did.
    function declarations(style) {
        var out = [], done = {};
        for (var i = 0; i < style.length; i++) {
            var name = style[i];
            if (!PROPS.test(name)) continue;
            var value = style.getPropertyValue(name);
            if (!value) { name = shorthand(style, name); if (!name || done[name]) continue; value = style.getPropertyValue(name); }
            if (done[name]) continue;
            done[name] = true;
            var mapped = map(value, name);
            out.push(name + ':' + mapped + (style.getPropertyPriority(name) ? '!important' : ''));
        }
        return out.join(';');
    }
    function rules(list) {
        var text = [];
        Array.from(list || []).forEach(function (rule) {
            if (rule.selectorText !== undefined && rule.style) { var body = declarations(rule.style); if (body) text.push(rule.selectorText + '{' + body + '}'); }
            else if (rule.cssRules) { var head = prelude(rule), inner = head && rules(rule.cssRules); if (inner) text.push(head + '{' + inner + '}'); }
        });
        return text.join('\n');
    }
    // The group a copied rule goes back into. Kinds are told by constructor name, which also holds for rules of a frame;
    // a layer keeps its name, so the copies join the original layer and cascade within it.
    function prelude(rule) {
        var kind = rule.constructor && rule.constructor.name;
        if (kind === 'CSSMediaRule') return '@media ' + rule.media.mediaText;
        if (kind === 'CSSSupportsRule') return '@supports ' + rule.conditionText;
        if (kind === 'CSSLayerBlockRule') return '@layer' + (rule.name ? ' ' + rule.name : '');
        if (kind === 'CSSContainerRule') { var css = rule.cssText || '', open = css.indexOf('{'); return open > 0 ? css.slice(0, open).trim() : ''; }
        return '';
    }
    function quote(value) { return '"' + value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\a ').replace(/\r/g, '\\d ').replace(/\f/g, '\\c ') + '"'; }
    // Colours written on the site's own elements: style attributes, bgcolor and font colours. The rules match the exact
    // attribute value with no specificity, so the reading layout's own rules still win wherever they did before.
    function inline(doc) {
        var text = [], seen = new Set(), probe = doc.createElement('div');
        doc.querySelectorAll('[style*="color" i],[style*="background" i],[bgcolor],font[color]').forEach(function (node) {
            var style = node.getAttribute('style');
            if (style && !seen.has('s' + style) && /color|background/i.test(style)) {
                seen.add('s' + style); probe.setAttribute('style', style);
                var body = declarations(probe.style);
                if (body) text.push(':where([style=' + quote(style) + ']){' + body.split(';').map(function (d) { return /!important$/.test(d) ? d : d + '!important'; }).join(';') + '}');
            }
            [['bgcolor', 'background-color', ''], ['color', 'color', 'font']].forEach(function (pair) {
                var value = node.getAttribute(pair[0]);
                if (!value || (pair[2] && node.localName !== pair[2]) || seen.has(pair[0] + value)) return;
                seen.add(pair[0] + value);
                var color = /^[0-9a-f]{6}$/i.test(value.trim()) ? '#' + value.trim() : value.trim();
                probe.removeAttribute('style'); probe.style.color = color;
                var parsed = NAMES[(probe.style.color || '').toLowerCase()] || probe.style.color, mapped = map(parsed, 'color');
                if (parsed && mapped !== parsed) text.push(':where(' + (pair[2] || '') + '[' + pair[0] + '=' + quote(value) + ' i]){' + pair[1] + ':' + mapped + '}');
            });
        });
        return text.join('\n');
    }
    function stateOf(doc) {
        var state = states.get(doc);
        if (!state) { state = { on: false, observer: null, timer: 0 }; states.set(doc, state); }
        return state;
    }
    function own(node) { return node && node.id && OWN.test(node.id); }
    function element(doc, id) {
        var style = doc.getElementById(id);
        if (!style) { style = doc.createElement('style'); style.id = id; }
        return style;
    }
    // Two layers: the site's stylesheets mapped right after them (so the reading layout keeps winning over them), and
    // the app's stylesheets, inline colours and the page colour mapped after everything else.
    function build(doc) {
        var state = stateOf(doc);
        if (!state.on || !doc.documentElement) return;
        var site = [], app = [], lastSite = null;
        // In document order, which is the cascade order; sheets of other sites cannot be read and stay as they are.
        Array.from(doc.querySelectorAll('style,link[rel~="stylesheet" i]')).forEach(function (node) {
            var sheet = node.sheet, list;
            if (own(node)) return;
            // A stylesheet link added after the page is mapped once it has loaded.
            if (!sheet) { if (node.localName === 'link' && !loading.has(node)) { loading.add(node); node.addEventListener('load', function () { loading.delete(node); build(doc); }, { once: true }); } return; }
            try { list = sheet.cssRules; } catch (_) { return; }
            var text = rules(list);
            if (node.id && /^univer-/.test(node.id)) { if (text) app.push(text); }
            else { if (text) site.push(text); lastSite = node; }
        });
        app.push(inline(doc));
        app.push(':root.univer-app-dark{color-scheme:dark;background:' + PAGE + '}:root.univer-mobile.univer-app-dark{--u-bg:' + PAGE + ';--u-shadow-pop:0 12px 36px rgba(0,0,0,.45),0 2px 6px rgba(0,0,0,.3)}' +
            // The site's logo is a JPEG with dark text on white: inverted and screened, the white drops out and the text turns light.
            ':root.univer-app-dark #logo{filter:invert(1) brightness(.9);mix-blend-mode:screen}');
        var head = doc.head || doc.documentElement, observer = state.observer;
        if (observer) observer.disconnect();
        var siteStyle = element(doc, 'univer-app-dark-site'), appStyle = element(doc, 'univer-app-dark-app');
        siteStyle.textContent = site.join('\n');
        if (lastSite && lastSite.parentNode) { if (lastSite.nextSibling !== siteStyle) lastSite.parentNode.insertBefore(siteStyle, lastSite.nextSibling); }
        else if (siteStyle.parentNode !== head) head.insertBefore(siteStyle, head.firstChild);
        appStyle.textContent = app.join('\n');
        if (appStyle.parentNode !== head || head.lastElementChild !== appStyle) head.appendChild(appStyle);
        var early = doc.getElementById('univer-app-dark-early'); if (early) early.remove();
        if (observer) watch(doc, state);
    }
    function relevant(records) {
        return records.some(function (record) {
            if (record.type === 'attributes') { var value = record.target.getAttribute(record.attributeName) || ''; return record.attributeName !== 'style' || /color|background/i.test(value + (record.oldValue || '')); }
            return Array.from(record.addedNodes).concat(Array.from(record.removedNodes)).some(function (node) {
                if (node.nodeType !== 1 || own(node)) return node.nodeType === 3 && record.target.localName === 'style' && !own(record.target);
                return node.localName === 'style' || node.localName === 'link' || !!(node.matches && node.matches('[style],[bgcolor],font[color]')) || !!(node.querySelector && node.querySelector('style,link,[style*="color" i],[style*="background" i],[bgcolor],font[color]'));
            });
        });
    }
    function watch(doc, state) {
        if (!state.observer) state.observer = new MutationObserver(function (records) {
            if (!relevant(records)) return;
            // New stylesheets are mapped before the next paint; attribute changes in batches.
            var sheet = records.some(function (record) { return record.type === 'childList' && Array.from(record.addedNodes).some(function (node) { return node.localName === 'style' || node.localName === 'link'; }) || record.target.localName === 'style'; });
            if (sheet) { build(doc); return; }
            var view = doc.defaultView || window;
            if (!state.timer) state.timer = view.setTimeout(function () { state.timer = 0; build(doc); }, 120);
        });
        state.observer.observe(doc.documentElement, { childList: true, subtree: true, characterData: true, attributes: true, attributeOldValue: true, attributeFilter: ['style', 'bgcolor', 'color'] });
    }
    function apply(doc, on) {
        if (!doc || !doc.documentElement) return;
        var state = stateOf(doc), root = doc.documentElement;
        if (on && state.on) { if (!doc.getElementById('univer-app-dark-app')) build(doc); return; }
        if (!on && !state.on) { var stale = doc.getElementById('univer-app-dark-early'); if (stale) stale.remove(); root.classList.remove('univer-app-dark'); return; }
        state.on = on;
        if (state.observer) { state.observer.disconnect(); state.observer = null; }
        if (state.timer) { (doc.defaultView || window).clearTimeout(state.timer); state.timer = 0; }
        if (on) {
            root.classList.add('univer-app-dark');
            build(doc); watch(doc, state);
        } else {
            ['univer-app-dark-site', 'univer-app-dark-app', 'univer-app-dark-early'].forEach(function (id) { var node = doc.getElementById(id); if (node) node.remove(); });
            root.classList.remove('univer-app-dark');
        }
    }
    function documents() {
        var result = [document];
        Array.from(document.querySelectorAll('iframe,frame')).forEach(function (frame) {
            try { if (frame.contentWindow.location.origin === location.origin && frame.contentDocument) result.push(frame.contentDocument); } catch (_) { }
        });
        return result;
    }
    window.__univerThemeV1 = {
        apply: apply,
        // At document start: a dark reading layout begins on the dark page colour, not on the site's white.
        early: function (readable) {
            if (window.__univerTheme !== 'dark' || !readable || !document.documentElement || document.getElementById('univer-app-dark-early')) return;
            var style = document.createElement('style'); style.id = 'univer-app-dark-early';
            style.textContent = 'html{background:' + PAGE + '!important;color-scheme:dark}';
            document.documentElement.appendChild(style);
        },
        // After the app switched themes.
        refresh: function () {
            var on = !!(window.__univerDesktop && window.__univerDesktop.enabled) && window.__univerTheme === 'dark';
            documents().forEach(function (doc) { apply(doc, on); });
        },
        dark: function (r, g, b) { return dark(r, g, b); },
        map: map
    };
})();
