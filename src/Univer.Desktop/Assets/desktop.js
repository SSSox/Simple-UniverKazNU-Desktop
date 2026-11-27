/* Desktop presentation layered on the reversible Android DOM adapter. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz') return;
    if (!window.__univerMotionUI) {
        var motions = new Map(), layouts = new Map(), ghosts = new Map();
        var ease = 'cubic-bezier(.22,1,.36,1)';
        var reduce = function () { return !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches; };
        var cancel = function (node) { var animation = motions.get(node); if (animation) { motions.delete(node); animation.cancel(); } };
        var allowed = function () { var root = document.documentElement; return window.__univerMotion !== false && root.classList.contains('univer-mobile') && !root.classList.contains('univer-app-keyboard'); };
        var retire = function (map, node) { var record = map.get(node); if (record) { map.delete(node); record.animation.cancel(); record.cleanup?.(); } };
        var stop = function (parent) {
            [motions, layouts, ghosts].forEach(function (map) { Array.from(map.keys()).forEach(function (node) {
                if (!parent || parent === node || parent.contains(node)) { if (map === motions) cancel(node); else retire(map, node); }
            }); });
        };
        var track = function (map, node, animation, cleanup) {
            var record = {animation:animation, cleanup:cleanup}; map.set(node, record);
            animation.onfinish = animation.oncancel = function () { if (map.get(node) === record) { map.delete(node); cleanup?.(); } };
        };
        var reveal = function (node, kind, delay) {
            if (!node || !node.isConnected) return;
            var root = document.documentElement;
            if (window.__univerMotion === false || !root.classList.contains('univer-mobile') || root.classList.contains('univer-app-keyboard') || !node.animate) { cancel(node); return; }
            var current = motions.has(node) ? getComputedStyle(node) : null;
            retire(ghosts, node);
            var gentle = reduce(), content = kind === 'content', horizontal = kind === 'forward' || kind === 'back';
            var from = current ? {opacity:current.opacity, transform:current.transform} :
                {opacity:gentle ? .4 : content ? .2 : .2, transform:gentle ? 'none' : horizontal ? 'translateX(' + (kind === 'back' ? '-16px' : '16px') + ') scale(.985)' : content ? 'translateY(8px)' : kind === 'tile' ? 'translateY(10px) scale(.97)' : 'translateY(-8px) scale(.97)'};
            cancel(node);
            var animation = node.animate([from, {opacity:1, transform:'none'}],
                {duration:gentle ? 180 : content ? 200 : kind === 'tile' ? 200 : 240, delay:gentle ? 0 : (delay || 0), easing:ease, fill:'backwards'});
            motions.set(node, animation);
            var finish = function () { if (motions.get(node) === animation) motions.delete(node); };
            animation.onfinish = animation.oncancel = finish;
        };
        var resize = function (node, before, duration) {
            if (!node?.isConnected || !allowed() || reduce() || !node.animate) return;
            var after = node.getBoundingClientRect().height;
            if (before <= 0 || after <= 0 || Math.abs(before - after) < 1) return;
            retire(layouts, node);
            // max-height moves too: a box whose height a stylesheet holds at auto with !important (which an animation cannot
            // override) still grows within it.
            track(layouts, node, node.animate([{height:before + 'px', maxHeight:before + 'px', overflow:'hidden'}, {height:after + 'px', maxHeight:after + 'px', overflow:'hidden'}],
                {duration:duration || 220, easing:ease, fill:'backwards'}));
        };
        // A block folds shut to nothing and only then is hidden (done), so what follows it closes up instead of jumping.
        // done runs exactly once: at the end, when the run is stopped, or at once without motion.
        var collapse = function (node, done, duration) {
            var height = node?.isConnected ? node.getBoundingClientRect().height : 0;
            stop(node);
            if (!node?.isConnected || !allowed() || reduce() || !node.animate || height < 1) { done(); return; }
            var animation = node.animate([{height:height + 'px', maxHeight:height + 'px', overflow:'hidden', opacity:1},
                {height:'0px', maxHeight:'0px', overflow:'hidden', opacity:0, paddingTop:'0px', paddingBottom:'0px', marginTop:'0px', marginBottom:'0px'}],
                {duration:duration || 180, easing:'cubic-bezier(.4,0,.2,1)', fill:'forwards'});
            track(layouts, node, animation, function () { done(); animation.cancel(); });
        };
        var change = function (node, mutate, kind) {
            var before = node?.getBoundingClientRect().height || 0; stop(node); mutate();
            resize(node, before); reveal(node, kind || 'content');
        };
        var reflow = function (parent, mutate) {
            var focus = parent.contains(document.activeElement) ? document.activeElement : null;
            var before = new Map(); Array.from(parent.children).forEach(function (node) { before.set(node, node.getBoundingClientRect()); });
            stop(parent); mutate();
            if (focus?.isConnected && document.activeElement !== focus) focus.focus({preventScroll:true});
            if (!allowed() || reduce()) return;
            before.forEach(function (rect, node) {
                if (!node.isConnected || !node.animate || !rect.width || !rect.height) return;
                var after = node.getBoundingClientRect(), x = rect.left - after.left, y = rect.top - after.top;
                if (!after.width || !after.height || Math.abs(x) + Math.abs(y) < 1) return;
                var a = node.animate([{transform:'translate(' + x + 'px,' + y + 'px)'}, {transform:'none'}],
                    {duration:300, easing:'cubic-bezier(.45,0,.1,1)', fill:'backwards'});
                motions.set(node, a); a.onfinish = a.oncancel = function () { if (motions.get(node) === a) motions.delete(node); };
            });
        };
        var GHOST_LIMIT = 80;
        var GHOST_SKIP = /^(?:SCRIPT|STYLE|IFRAME|FRAME|OBJECT|EMBED|LINK|META)$/i;
        var GHOST_PROPS = 'display position top right bottom left box-sizing width height min-width min-height max-width max-height margin padding border border-radius background color opacity font font-family font-size font-weight line-height letter-spacing text-align text-decoration white-space overflow gap align-items justify-content flex flex-direction flex-wrap grid-template-columns grid-column transform transform-origin box-shadow list-style vertical-align object-fit -webkit-mask mask'.split(' ');
        var GHOST_BOX_PROPS = ['box-sizing', 'background', 'background-color', 'border', 'border-top', 'border-right', 'border-bottom', 'border-left', 'border-radius', 'box-shadow', 'overflow'];
        var exit = function (node) {
            if (!node?.isConnected || !allowed() || !node.animate) { stop(node); return; }
            var visual = getComputedStyle(node), opacity = visual.opacity, transform = visual.transform;
            cancel(node); var rect = node.getBoundingClientRect();
            if (!rect.width || !rect.height) { stop(node); return; }
            // A passive visual copy lets the real popup close and release focus immediately.
            // Computed styles avoid duplicate adapter classes/IDs and preserve the already painted shape;
            // large subtrees fade a lightweight panel shell instead of copying every descendant's style.
            var clone;
            if (node.getElementsByTagName('*').length > GHOST_LIMIT) {
                clone = document.createElement('div');
                GHOST_BOX_PROPS.forEach(function (property) {
                    var value = visual.getPropertyValue(property);
                    if (value) clone.style.setProperty(property, value);
                });
                clone.style.pointerEvents = 'none'; clone.tabIndex = -1;
            } else {
                clone = node.cloneNode(true);
                var pairs = [];
                (function walk(source, copy) {
                    pairs.push([source, copy]);
                    var sKids = source.children, cKids = copy.children;
                    for (var i = sKids.length - 1; i >= 0; i--) {
                        if (GHOST_SKIP.test(sKids[i].tagName)) cKids[i]?.remove();
                    }
                    var j = 0;
                    for (var k = 0; k < sKids.length; k++) {
                        if (GHOST_SKIP.test(sKids[k].tagName)) continue;
                        if (cKids[j]) walk(sKids[k], cKids[j++]);
                    }
                })(node, clone);
                var pseudos = !!window.CSS?.supports?.('content', '""');
                pairs.forEach(function (pair, index) {
                    var source = pair[0], copy = pair[1];
                    var css = index === 0 ? visual : getComputedStyle(source);
                    Array.from(copy.attributes).forEach(function (attr) { if (/^(?:id|class|name|autofocus|on)/i.test(attr.name)) copy.removeAttribute(attr.name); });
                    GHOST_PROPS.forEach(function (property) { copy.style.setProperty(property, css.getPropertyValue(property)); });
                    copy.style.pointerEvents = 'none'; copy.tabIndex = -1;
                    if (!pseudos) return;
                    ['::before','::after'].forEach(function (which) {
                        var pseudo = getComputedStyle(source, which);
                        if (!pseudo.content || pseudo.content === 'none' || pseudo.content === 'normal') return;
                        var span = document.createElement('span');
                        GHOST_PROPS.forEach(function (property) { span.style.setProperty(property, pseudo.getPropertyValue(property)); });
                        if (/^["'].*["']$/.test(pseudo.content)) span.textContent = pseudo.content.slice(1, -1);
                        if (which === '::before') copy.prepend(span); else copy.append(span);
                    });
                });
            }
            clone.setAttribute('data-univer-motion-ghost', ''); clone.setAttribute('aria-hidden', 'true'); clone.inert = true;
            clone.style.cssText += ';position:fixed!important;left:' + rect.left + 'px!important;top:' + rect.top + 'px!important;width:' + rect.width + 'px!important;height:' + rect.height + 'px!important;margin:0!important;z-index:10000!important;pointer-events:none!important;';
            stop(node); document.body.appendChild(clone);
            track(ghosts, node, clone.animate([{opacity:opacity, transform:transform}, {opacity:0, transform:reduce() ? 'none' : 'translateY(-8px) scale(.97)'}],
                {duration:140, easing:ease, fill:'forwards'}), function () { clone.remove(); });
        };
        var zoom = function (node, ratio) {
            if (!node?.isConnected) return; stop(node);
            if (!allowed() || reduce() || !node.animate || !Number.isFinite(ratio) || ratio <= 0) return;
            var animation = node.animate([{transform:'scale(' + ratio + ')',transformOrigin:'top left'}, {transform:'none',transformOrigin:'top left'}],
                {duration:180, easing:ease, fill:'backwards'});
            motions.set(node, animation); animation.onfinish = animation.oncancel = function () { if (motions.get(node) === animation) motions.delete(node); };
        };
        var detailsSelector = 'details.univer-app-menu,details.univer-app-account,details.univer-app-faq,details.univer-app-catalog,details.univer-app-info,details.univer-app-category,details.univer-app-university,details.univer-app-useful';
        document.addEventListener('click', function (event) {
            var summary = event.target.closest?.('summary'), details = summary?.parentElement;
            if (!details?.matches(detailsSelector) || !allowed() || event.detail === 0) return;
            event.preventDefault(); var open = !details.open;
            if (details.matches('.univer-app-menu,.univer-app-account')) { if (!open) exit(details); else stop(details); details.open = open; }
            else {
                var before = details.getBoundingClientRect().height; stop(details); details.open = open; resize(details, before, open ? 220 : 180);
                if (open) Array.from(details.children).filter(function (n) { return n.tagName !== 'SUMMARY'; }).forEach(function (n) { reveal(n, 'content'); });
            }
            details.__univerMotionToggle = open;
        }, true);
        window.__univerMotionUI = {reveal:reveal, stop:stop, reduced:reduce, change:change, reflow:reflow, exit:exit, resize:resize, zoom:zoom, collapse:collapse};
        document.addEventListener('visibilitychange', function () { if (document.hidden) stop(); });
        document.addEventListener('toggle', function (event) {
            var details = event.target;
            if (!details.matches?.(detailsSelector)) return;
            var handled = details.__univerMotionToggle === details.open; delete details.__univerMotionToggle;
            if (!details.open) { if (!handled && !ghosts.has(details)) stop(details); return; }
            if (details.classList.contains('univer-app-menu')) reveal(details, 'popup');
            else if (!handled) Array.from(details.children).filter(function (node) { return node.tagName !== 'SUMMARY'; }).forEach(function (node) { reveal(node, details.classList.contains('univer-app-account') ? 'popup' : 'content'); });
        }, true);
        var preference = window.matchMedia?.('(prefers-reduced-motion: reduce)');
        if (preference?.addEventListener) preference.addEventListener('change', function () { stop(); });
    }
    if (!window.__univerDesktop) {
        // A flag click changes the app's language natively (a main-frame navigation with the user's gesture); no message.
        window.__univerDesktop = { enabled: true, viewer: false, close: false };
        // Keyboard input cancels decorative movement without delaying focus or the action.
        // Only a real modality change touches <html>: each class write wakes the layout observer, and Original stays untouched.
        document.addEventListener('keydown', function () {
            var root = document.documentElement;
            if (window.__univerDesktop.enabled && !root.classList.contains('univer-app-keyboard')) root.classList.add('univer-app-keyboard');
            window.__univerMotionUI.stop();
        }, true);
        document.addEventListener('pointerdown', function () {
            var root = document.documentElement;
            if (root.classList.contains('univer-app-keyboard')) root.classList.remove('univer-app-keyboard');
        }, true);
        // Single-choice lists open immediately. The real <select> keeps its value,
        // keyboard use and handlers: a choice sets selectedIndex and fires the same input/change events.
        var openList = null;
        var closeList = function (restore) {
            if (!openList) return; var list = openList.list, select = openList.select; openList = null;
            window.__univerMotionUI.exit(list);
            list.remove();
            if (restore && select.isConnected) select.focus({preventScroll:true});
        };
        var choose = function (select, index) {
            if (index >= 0 && select.selectedIndex !== index) {
                select.selectedIndex = index;
                select.dispatchEvent(new Event('input', {bubbles:true})); select.dispatchEvent(new Event('change', {bubbles:true}));
            }
            closeList(true);
        };
        document.addEventListener('mousedown', function (event) {
            if (openList && !openList.list.contains(event.target)) closeList(false);
            var select = event.target.closest && event.target.closest('select');
            var root = document.documentElement;
            if (!select || event.button !== 0 || !event.isTrusted || select.multiple || select.size > 1 || select.disabled || window !== window.top ||
                !root.classList.contains('univer-mobile') || root.classList.contains('univer-app-still') || window.__univerMotion === false || !select.options.length) return;
            event.preventDefault(); select.focus({preventScroll:true});
            var box = select.getBoundingClientRect(), list = document.createElement('div');
            list.className = 'univer-app-select'; list.setAttribute('role', 'listbox'); list.tabIndex = -1;
            if (select.getAttribute('aria-label') || select.labels?.[0]) list.setAttribute('aria-label', select.getAttribute('aria-label') || select.labels[0].textContent.trim());
            var items = [];
            Array.from(select.children).forEach(function (child) {
                var add = function (option, group) {
                    if (option.hidden) return;
                    var item = document.createElement('div'); item.className = 'univer-app-select-option' + (group ? ' univer-app-select-grouped' : '');
                    item.setAttribute('role', 'option'); item.id = 'univer-select-' + option.index; item.textContent = option.textContent.replace(/\s+/g, ' ').trim() || '\u00a0';
                    item.setAttribute('aria-selected', String(option.selected)); if (option.disabled) item.setAttribute('aria-disabled', 'true');
                    item.addEventListener('mousedown', function (e) { e.preventDefault(); });
                    item.addEventListener('click', function () { if (!option.disabled) choose(select, option.index); });
                    item.__univerIndex = option.index; list.appendChild(item); items.push(item);
                };
                if (child.tagName === 'OPTGROUP') { var label = document.createElement('div'); label.className = 'univer-app-select-group'; label.textContent = child.label; list.appendChild(label); Array.from(child.children).forEach(function (o) { add(o, true); }); }
                else if (child.tagName === 'OPTION') add(child, false);
            });
            if (!items.length) return;
            var below = innerHeight - box.bottom - 8, above = box.top - 8, up = below < 220 && above > below;
            list.style.left = Math.max(8, Math.min(box.left, innerWidth - Math.max(box.width, 180) - 8)) + 'px';
            list.style.minWidth = Math.min(box.width, innerWidth - 16) + 'px'; list.style.maxHeight = Math.max(40, Math.min(360, up ? above : below)) + 'px';
            if (up) { list.style.bottom = (innerHeight - box.top + 4) + 'px'; list.classList.add('univer-app-select-up'); } else list.style.top = (box.bottom + 4) + 'px';
            document.body.appendChild(list);
            window.__univerMotionUI.reveal(list, 'popup');
            var active = Math.max(0, items.findIndex(function (i) { return i.__univerIndex === select.selectedIndex; }));
            var mark = function (n) {
                active = Math.max(0, Math.min(items.length - 1, n)); items.forEach(function (i, k) { i.classList.toggle('univer-app-select-active', k === active); });
                list.setAttribute('aria-activedescendant', items[active].id); items[active].scrollIntoView({block:'nearest'});
            };
            mark(active); list.focus({preventScroll:true});
            list.addEventListener('keydown', function (e) {
                if (e.key === 'ArrowDown') mark(active + 1); else if (e.key === 'ArrowUp') mark(active - 1);
                else if (e.key === 'Home') mark(0); else if (e.key === 'End') mark(items.length - 1);
                else if (e.key === 'Enter' || e.key === ' ') { if (items[active].getAttribute('aria-disabled') !== 'true') choose(select, items[active].__univerIndex); }
                else if (e.key === 'Escape') closeList(true);
                else if (e.key === 'Tab') { closeList(true); e.stopPropagation(); return; }
                else return;
                e.preventDefault(); e.stopPropagation();
            });
            list.addEventListener('mousemove', function (e) { var k = items.indexOf(e.target.closest('.univer-app-select-option')); if (k >= 0 && k !== active) mark(k); });
            openList = {list:list, select:select};
        }, true);
        document.addEventListener('visibilitychange', function () { if (document.hidden) closeList(false); });
        window.addEventListener('resize', function () { closeList(false); });
        document.addEventListener('scroll', function (e) { if (openList && !openList.list.contains(e.target)) closeList(false); }, true);
        var back = history.back.bind(history), go = history.go.bind(history);
        history.back = function () { if (window.__univerDesktop.viewer) window.__univerDesktop.close = true; else back(); };
        history.go = function (delta) { if (window.__univerDesktop.viewer && delta < 0) window.__univerDesktop.close = true; else go(delta); };
    }
    // Icons are inline SVG masks: no network requests and they follow the text colour.
    function icon(path, color) { return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='" + (color || 'black') + "' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>" + path + '</svg>') + '")'; }
    var icons = {
        person: icon("<circle cx='12' cy='8' r='4'/><path d='M4 21c1.6-4 4.6-6 8-6s6.4 2 8 6'/>"),
        download: icon("<path d='M12 3v12'/><path d='M7 10l5 5 5-5'/><path d='M4 17v4h16v-4'/>"),
        print: icon("<path d='M7 9V3h10v6'/><rect x='3' y='9' width='18' height='8' rx='2'/><path d='M7 14h10v7H7z'/>"),
        doc: icon("<path d='M14 3H6v18h12V7z'/><path d='M14 3v4h4'/><path d='M9 13h6M9 17h6'/>"),
        search: icon("<circle cx='11' cy='11' r='7'/><path d='M20 20l-3.5-3.5'/>", '#7a8a9a'),
        close: icon("<path d='M6 6l12 12M18 6L6 18'/>"),
        back: icon("<path d='M15 6l-6 6 6 6'/>"),
        forward: icon("<path d='M9 6l6 6-6 6'/>"),
        fold: icon("<path d='M6 9l6 6 6-6'/>"),
        link: icon("<path d='M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1 1'/><path d='M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1-1'/>"),
        saveBrand: icon("<path d='M12 4v11'/><path d='M7 10l5 5 5-5'/><path d='M5 20h14'/>", '#1b5fa6'),
        saveWhite: icon("<path d='M12 4v11'/><path d='M7 10l5 5 5-5'/><path d='M5 20h14'/>", '#ffffff'),
        openBrand: icon("<path d='M14 4h6v6'/><path d='M20 4l-9 9'/><path d='M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5'/>", '#1b5fa6'),
        openWhite: icon("<path d='M14 4h6v6'/><path d='M20 4l-9 9'/><path d='M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5'/>", '#ffffff')
    };
    var lastApply = null;
    window.__univerDesktop.apply = function (enabled, viewer, language, version) {
        window.__univerDesktop.enabled = enabled;
        window.__univerDesktop.viewer = viewer;
        var lang = language || 'ru', dark = !!(enabled && window.__univerTheme === 'dark'), still = window.__univerMotion === false;
        var mutated = !lastApply || lastApply.enabled !== enabled || lastApply.viewer !== viewer || lastApply.lang !== lang || lastApply.dark !== dark || lastApply.still !== still || lastApply.version !== version;
        lastApply = { enabled: enabled, viewer: viewer, lang: lang, dark: dark, still: still, version: version };
        var wasMobile = document.documentElement.classList.contains('univer-mobile');
        // The adapter's layout reads wait for this layer's stylesheets below: the page is laid out once, with all of them.
        var measure = !!window.__univerMobileV1 && window.__univerMobileV1.setEnabled(enabled, lang, true);
        if (measure || document.documentElement.classList.contains('univer-mobile') !== wasMobile) mutated = true;
        // The dark theme belongs to the reading layout only; Original removes it with the rest of the presentation.
        function apply(doc) {
            var changed = present(doc);
            if (window.__univerThemeV1) {
                var hadApp = !!doc.getElementById('univer-app-dark-app'), hadEarly = !!doc.getElementById('univer-app-dark-early'), hadDark = doc.documentElement.classList.contains('univer-app-dark');
                window.__univerThemeV1.apply(doc, dark);
                if (!!doc.getElementById('univer-app-dark-app') !== hadApp || !!doc.getElementById('univer-app-dark-early') !== hadEarly || doc.documentElement.classList.contains('univer-app-dark') !== hadDark) changed = true;
            }
            return changed;
        }
        function present(doc) {
            // The explicit app preference disables all feedback transitions; system reduction is handled by CSS.
            var changed = false;
            if (still || !enabled) window.__univerMotionUI.stop();
            if (doc.documentElement.classList.contains('univer-app-still') !== still) { doc.documentElement.classList.toggle('univer-app-still', still); changed = true; }
            var style = doc.getElementById('univer-desktop-style');
            if (!enabled) {
                if (openList) { closeList(false); changed = true; }
                doc.querySelectorAll('.univer-app-select').forEach(function (list) { list.remove(); changed = true; });
                if (doc.documentElement.classList.contains('univer-app-keyboard') || doc.documentElement.classList.contains('univer-app-still')) {
                    doc.documentElement.classList.remove('univer-app-keyboard', 'univer-app-still');
                    changed = true;
                }
                if (style) { style.remove(); changed = true; }
                return changed;
            }
            if (style) return changed;
            style = doc.createElement('style'); style.id = 'univer-desktop-style';
            // Reading mode's visual tokens. ":root.univer-mobile" outranks the adapter's "html.univer-mobile" rules without
            // rewriting them; the adapter keeps the original nodes and handlers.
            style.textContent = `
:root { color-scheme:light; }
:root.univer-mobile {
  --u-brand:#1b5fa6; --u-brand-strong:#154d88; --u-brand-soft:#e9f1fb; --u-brand-line:#bfd3ea;
  --u-ink:#16283b; --u-text:#2a3d51; --u-muted:#5b6b7c; --u-line:#dfe5ec; --u-line-strong:#c8d3df;
  --u-bg:#f1f4f7; --u-surface:#fff; --u-surface-2:#f7f9fb;
  --u-teal:#0f766e; --u-teal-soft:#e3f3ef; --u-danger:#b42318; --u-danger-soft:#fdeceb;
  --u-shadow:none;
  --u-shadow-pop:0 12px 36px rgba(16,40,70,.12),0 2px 6px rgba(16,40,70,.06);
  --u-font:'Segoe UI Variable Text','Segoe UI',Tahoma,system-ui,sans-serif;
  --u-ease-out:cubic-bezier(.23,1,.32,1); --u-press:140ms; --u-release:100ms;
  /* Type scale shared with the native shell: captions only for short labels, readable secondary text from 13px. */
  --u-fs-caption:12px; --u-fs-meta:13px; --u-fs-body:14px; --u-fs-strong:15px; --u-fs-section:16px; --u-fs-title:20px; --u-fs-display:22px;
  --u-header:52px; --u-page:1400px;
  background:var(--u-bg); scroll-padding-top:calc(var(--u-header) + 16px);
}
:root.univer-mobile body { font:14px/1.55 var(--u-font)!important; background:var(--u-bg)!important; color:var(--u-text)!important; }
:root.univer-mobile :is(td,th,p,li,label,h1,h2,h3,h4,h5,h6,div,span,a,input,select,textarea,button,summary,b,strong,font) { font-family:var(--u-font)!important; }
:root.univer-mobile :is(td,th,p,li,label) { line-height:1.55!important; }
:root.univer-mobile a { color:var(--u-brand); text-underline-offset:3px; text-decoration-thickness:1px; }
:root.univer-mobile a:hover { color:var(--u-brand-strong); }
:root.univer-mobile :is(input,select,textarea,button,a,summary,[tabindex]):focus-visible { outline:2px solid var(--u-brand)!important; outline-offset:2px!important; }
:root.univer-mobile input:is([type=checkbox],[type=radio]) { accent-color:var(--u-brand); }
:root.univer-mobile :is(input[type=text],input[type=password],input[type=number],input[type=search],input[type=date],input[type=email],input[type=tel],input:not([type]),select,textarea) { min-height:40px; padding:8px 12px!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background-color:#fff; color:var(--u-ink); box-sizing:border-box!important; font-size:14px!important; }
:root.univer-mobile :is(input,select,textarea):not([type=checkbox]):not([type=radio]):hover { border-color:#9fb2c6!important; }
:root.univer-mobile :is(input,select,textarea):not([type=checkbox]):not([type=radio]):focus { outline:none!important; border-color:var(--u-brand)!important; box-shadow:0 0 0 3px rgba(27,95,166,.16)!important; }
:root.univer-mobile :is(input,textarea)[readonly] { background-color:var(--u-surface-2); color:var(--u-muted); }
:root.univer-mobile :is(button,input[type=submit],input[type=button],summary) { cursor:pointer; }
:root.univer-mobile :is(button,input[type=submit],input[type=button]):disabled { cursor:default; opacity:.5; }
:root.univer-mobile details>summary { list-style:none; }
:root.univer-mobile details>summary::-webkit-details-marker { display:none; }
:root.univer-mobile .univer-app-shell { max-width:none!important; }
:root.univer-mobile .univer-app-menu-return { display:none!important; }

/* One compact sticky bar: brand, real site tabs, all sections, language flags and account. */
:root.univer-mobile #l1 { height:auto!important; }
:root.univer-mobile #l1 .header { position:sticky; top:0; z-index:800; background:#fff!important; color:var(--u-ink)!important; padding:0 24px!important; border-bottom:1px solid var(--u-line)!important; box-shadow:none; height:auto!important; }
:root.univer-mobile .univer-app-header { display:flex!important; align-items:center; flex-wrap:wrap; gap:8px 16px; min-height:var(--u-header)!important; max-width:var(--u-page); margin:0 auto; }
:root.univer-mobile .univer-app-brand { display:flex; align-items:center; flex:0 0 auto; }
:root.univer-mobile .univer-app-brand:empty { display:none; }
:root.univer-mobile .univer-app-brand #logo { width:112px!important; height:34px!important; margin:0!important; float:none!important; background-size:contain!important; background-repeat:no-repeat!important; background-position:left center!important; }
:root.univer-mobile .univer-app-tabs { display:flex; align-items:center; gap:4px; flex:1 1 auto; min-width:0; }
:root.univer-mobile .univer-app-tabs:empty { display:none; }
:root.univer-mobile .univer-app-tabs>a { display:inline-flex!important; align-items:center; height:36px; min-height:0!important; padding:0 14px!important; margin:0!important; border:0!important; border-radius:8px!important; background:none!important; color:var(--u-muted)!important; font-size:14px!important; font-weight:500!important; text-decoration:none!important; white-space:nowrap; float:none!important; width:auto!important; flex:0 0 auto!important; }
:root.univer-mobile .univer-app-tabs>a:hover { background:var(--u-surface-2)!important; color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-tabs>a.active { background:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile .univer-app-tabs>a.active:hover { background:var(--u-brand-strong)!important; color:#fff!important; }
:root.univer-mobile .univer-app-tabs>a.active * { color:inherit!important; }
:root.univer-mobile .univer-app-tabs>a>:is(.left,.right) { display:none!important; }
:root.univer-mobile .univer-app-tabs>a>.center { display:inline!important; float:none!important; height:auto!important; padding:0!important; background:none!important; color:inherit!important; font:inherit!important; line-height:inherit!important; }
/* The structural adapter's #l1 header rule forces inherited colours with !important. Match its ID scope. */
:root.univer-mobile #l1 .header .univer-app-tabs>a { color:var(--u-muted)!important; }
:root.univer-mobile #l1 .header .univer-app-tabs>a:hover { color:var(--u-ink)!important; }
:root.univer-mobile #l1 .header .univer-app-tabs>a.active { color:#fff!important; }
:root.univer-mobile #l1 .header .univer-app-tabs>a.active * { color:inherit!important; }
:root.univer-mobile .univer-app-actions { display:flex; align-items:center; gap:10px; margin-left:auto; flex:0 0 auto; }
:root.univer-mobile .univer-app-header>.univer-app-university { flex:0 1 auto; order:5; }
:root.univer-mobile #l1 .univer-app-sections { display:inline-flex!important; align-items:center; gap:8px; height:36px; min-height:0!important; box-sizing:border-box; padding:0 14px 0 12px!important; margin:0!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:500!important; white-space:nowrap; }
:root.univer-mobile #l1 .univer-app-sections::before { content:''; width:16px; height:12px; background:linear-gradient(currentColor,currentColor) top/100% 2px no-repeat,linear-gradient(currentColor,currentColor) center/100% 2px no-repeat,linear-gradient(currentColor,currentColor) bottom/100% 2px no-repeat; }
:root.univer-mobile #l1 .univer-app-sections:is(:hover,[aria-expanded=true]) { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-ink)!important; }
:root.univer-mobile #l1 .univer-app-sections[hidden] { display:none!important; }
:root.univer-mobile #l1 .univer-app-flags { display:flex!important; align-items:center; gap:2px; padding:3px; border:1px solid var(--u-line); border-radius:10px; background:var(--u-surface-2); }
:root.univer-mobile #l1 .univer-app-flags>:is(a,img) { box-sizing:border-box; width:34px!important; height:28px!important; min-height:0!important; padding:0!important; margin:0!important; display:inline-flex!important; align-items:center; justify-content:center; border:0!important; border-radius:6px!important; }
:root.univer-mobile #l1 .univer-app-flags>a img { width:20px!important; min-width:0!important; height:auto!important; opacity:.7; border:0!important; }
:root.univer-mobile #l1 .univer-app-flags>a:hover { background:#fff; }
:root.univer-mobile #l1 .univer-app-flags>a:hover img { opacity:1; }
:root.univer-mobile #l1 .univer-app-flags>img { padding:6px 7px!important; object-fit:contain!important; background:#fff; outline:1.5px solid var(--u-brand); outline-offset:-1.5px; }
:root.univer-mobile .univer-app-actions .links { position:relative; display:flex!important; align-items:center; gap:8px; width:auto!important; min-width:0!important; max-width:none!important; padding:0!important; margin:0!important; float:none!important; text-align:start!important; font-size:14px!important; white-space:nowrap!important; }
:root.univer-mobile .univer-app-actions .links>a { display:inline-flex!important; align-items:center; height:36px; padding:0 10px!important; border-radius:8px; color:var(--u-muted)!important; font-size:14px!important; font-weight:500; text-decoration:none!important; }
:root.univer-mobile .univer-app-actions .links>a:hover { background:var(--u-surface-2); color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-actions .univer-app-account { position:relative; }
:root.univer-mobile .univer-app-actions .univer-app-account>summary { display:inline-flex!important; align-items:center; gap:8px; height:36px; min-height:0!important; box-sizing:border-box; padding:0 12px!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:500!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>summary::before { content:''; width:18px; height:18px; background:currentColor; -webkit-mask:${icons.person} center/contain no-repeat; opacity:.75; }
:root.univer-mobile .univer-app-actions .univer-app-account>summary::after { content:''; width:6px; height:6px; margin:-3px 0 0 2px; border-right:2px solid currentColor; border-bottom:2px solid currentColor; transform:rotate(45deg); opacity:.55; }
:root.univer-mobile .univer-app-actions .univer-app-account:is([open],:hover)>summary { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-actions .univer-app-account[open]>div { position:absolute; right:0; top:calc(100% + 8px); z-index:850; box-sizing:border-box; width:max-content; min-width:260px; max-width:min(380px,calc(100vw - 32px)); padding:8px!important; background:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow-pop); white-space:normal; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a { display:block!important; min-height:0!important; line-height:1.45!important; padding:6px 12px!important; margin:0!important; border-radius:8px; color:var(--u-ink)!important; font-size:14px!important; font-weight:500; text-decoration:none!important; float:none!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a:hover { background:var(--u-surface-2); }
:root.univer-mobile .univer-app-actions .univer-app-account>div a :is(span,div,b,strong,font) { display:inline!important; float:none!important; width:auto!important; color:inherit!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a[href*="/user/pass"] { font-weight:500!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a[href*="/user/logout"] * { color:inherit!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a[href*="/user/logout"] { color:var(--u-danger)!important; border-top:1px solid var(--u-line); border-radius:0 0 8px 8px; margin-top:4px!important; }
:root.univer-mobile .univer-app-academic-meta { list-style:none; display:flex; flex-wrap:wrap; gap:6px; padding:8px 12px 4px!important; margin:4px 0 0!important; border-top:1px solid var(--u-line); }
:root.univer-mobile .univer-app-academic-meta li { padding:2px 8px!important; border-radius:6px; background:var(--u-surface-2); color:var(--u-muted); font-size:12px!important; }
:root.univer-mobile.univer-app-unified :is(#l2,#l3).univer-app-header-row { display:none!important; }

/* "All sections" opens as a floating panel instead of pushing the page down. */
:root.univer-mobile .univer-app-menu { scroll-margin-top:calc(var(--u-header) + 12px); }
:root.univer-mobile.univer-app-unified .univer-app-menu:not([open]) { display:none!important; }
:root.univer-mobile.univer-app-unified .univer-app-menu[open] { position:fixed!important; z-index:900; top:calc(var(--u-header) + 8px); left:24px; right:24px; max-width:1200px!important; margin:0 auto!important; box-sizing:border-box; max-height:calc(100vh - var(--u-header) - 24px); overflow:auto; padding:16px 16px 20px!important; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow-pop); }
:root.univer-mobile.univer-app-unified .univer-app-menu>summary { display:none!important; }
:root.univer-mobile .univer-app-menu-close { display:none; }
:root.univer-mobile.univer-app-unified .univer-app-menu-close { display:block; position:absolute; top:16px; right:16px; width:40px!important; height:40px!important; min-height:0!important; padding:0!important; border:1px solid var(--u-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-muted)!important; font-size:0!important; }
:root.univer-mobile.univer-app-unified .univer-app-menu-close { display:grid; place-items:center; }
:root.univer-mobile .univer-app-menu-close::before { content:''; width:16px; height:16px; background:currentColor; -webkit-mask:${icons.close} center/contain no-repeat; }
:root.univer-mobile .univer-app-menu-close:hover { background:var(--u-surface-2)!important; color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-menu input[type=search] { display:block; width:100%!important; height:40px; margin:0 0 14px!important; padding-left:38px!important; background:#fff ${icons.search} 12px center/18px no-repeat!important; }
:root.univer-mobile.univer-app-unified .univer-app-menu input[type=search] { width:calc(100% - 52px)!important; }
:root.univer-mobile .univer-app-menu .univer-app-empty { margin:8px 4px; color:var(--u-muted); }
:root.univer-mobile #tools { width:100%!important; background:none!important; border:0!important; }
:root.univer-mobile #tools>tbody>tr>td.center { display:block!important; columns:250px auto; column-gap:12px; padding:0!important; background:none!important; }
/* Columns rather than a grid: a short category sits right under the one above it instead of below the tallest row. */
:root.univer-mobile #tools>tbody>tr>td.center>* { break-inside:avoid; margin:0 0 12px!important; }
:root.univer-mobile #tools>tbody>tr>td.center:has(>:nth-child(2):last-child) { columns:2 250px; }
:root.univer-mobile #tools>tbody>tr>td.center:has(>:nth-child(3):last-child) { columns:3 220px; }
:root.univer-mobile #tools>tbody>tr>td.center:has(>:nth-child(4):last-child) { columns:4 200px; }
:root.univer-mobile .univer-app-category { margin:0!important; overflow:hidden; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:10px!important; }
:root.univer-mobile .univer-app-category>summary { padding:10px 14px!important; min-height:0!important; background:var(--u-surface-2); border-bottom:1px solid var(--u-line); color:var(--u-ink)!important; font-size:13px!important; font-weight:500!important; }
:root.univer-mobile #tools li { border:0!important; }
:root.univer-mobile #tools li a { display:block; padding:7px 14px!important; color:var(--u-ink)!important; font-size:14px!important; line-height:1.45!important; text-decoration:none; outline:none!important; }
:root.univer-mobile #tools li a:hover { background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }
:root.univer-mobile #tools li a:focus-visible { background:var(--u-brand-soft)!important; box-shadow:inset 0 0 0 2px var(--u-brand); }
/* The current menu page: a tint plus a left bar, so the state does not rest on colour alone. */
:root.univer-mobile #tools a[aria-current=page] { background:var(--u-brand-soft)!important; color:var(--u-ink)!important; font-weight:500!important; box-shadow:inset 3px 0 0 var(--u-brand)!important; border-left:0!important; }
/* All sections: each section is a small outlined row, with its count and pin at the row's end. */
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category :is(table.tool,table.tool>tbody,table.tool>tbody>tr:not(.univer-app-menu-footer),table.tool>tbody>tr>td:not(.bottom-center)) { display:block!important; width:auto!important; margin:0!important; padding:0!important; border:0!important; background:none!important; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category ul { display:flex!important; flex-direction:column; gap:6px; box-sizing:border-box; width:auto!important; margin:0!important; padding:10px!important; list-style:none!important; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li:not([hidden]) { display:grid!important; grid-template-columns:minmax(0,1fr) auto auto; align-items:center; column-gap:2px; width:100%; min-height:40px; box-sizing:border-box; margin:0!important; padding:0 4px 0 0!important; border:1px solid var(--u-line)!important; border-radius:8px; background:var(--u-surface)!important; transition:border-color .12s ease,background-color .12s ease; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li:not([hidden]):hover { border-color:var(--u-brand-line,#bfd3ea)!important; background:var(--u-brand-soft)!important; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li:has(a[aria-current=page]) { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category .univer-app-pin-row { display:contents!important; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li a { grid-column:1; min-width:0; box-sizing:border-box; padding:8px 10px!important; border:0!important; border-radius:7px; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:14px!important; line-height:1.35!important; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li>:not(a,.univer-app-pin-row,.univer-app-pin) { grid-row:1; grid-column:2; }
:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category .univer-app-pin { grid-column:3; margin:0!important; }
/* Links that share a menu item each take a line of the grid with their own pin; a count stays beside the first. */
:root:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li>span:not(.univer-app-pin-row):has(a) { display:contents!important; }
:root:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li>br { display:none!important; }
/* An item that holds two links (Расписание and Расписание экзаменов) is two cards, each with its own outline, hover and pin. */
:root:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li:not([hidden]):has(>.univer-app-pin-row~.univer-app-pin-row) { display:flex!important; flex-direction:column; gap:6px; min-height:0; padding:0!important; border:0!important; background:none!important; }
:root:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li:has(>.univer-app-pin-row~.univer-app-pin-row)>.univer-app-pin-row { display:grid!important; grid-template-columns:minmax(0,1fr) auto auto; align-items:center; column-gap:2px; width:100%; min-height:40px; box-sizing:border-box; padding:0 4px 0 0; border:1px solid var(--u-line); border-radius:8px; background:var(--u-surface); transition:border-color .12s ease,background-color .12s ease; }
:root:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li:has(>.univer-app-pin-row~.univer-app-pin-row)>.univer-app-pin-row:hover { border-color:var(--u-brand-line,#bfd3ea); background:var(--u-brand-soft); }
:root:root:root.univer-mobile :is(#tools,.univer-app-menu) .univer-app-category li:has(>.univer-app-pin-row~.univer-app-pin-row)>.univer-app-pin-row:has(a[aria-current=page]) { border-color:var(--u-brand); background:var(--u-brand-soft); }

/* Page body. */
:root.univer-mobile #l4 td.center { padding:8px 32px 40px!important; background:transparent!important; }
:root.univer-mobile #l4 td.center>*, :root.univer-mobile #l5 td.center>* { max-width:var(--u-page); margin-left:auto!important; margin-right:auto!important; box-sizing:border-box; }
/* One column for the header, the page and the footer. Study panels are blocks as wide as their cell, so the cell itself
   is narrowed: in a wide window nothing stretches past the column, whatever the site put on the page. The schedule is
   the exception, its week needs the room; the attestation sheet is a short table and gets a narrower column that the
   table fills, so its title, semester buttons, table and note share both edges. */
@media (min-width:1464px) {
 :root.univer-mobile:not(.univer-app-schedule-page) #l4 td.center { padding-left:calc((100% - var(--u-page)) / 2)!important; padding-right:calc((100% - var(--u-page)) / 2)!important; }
}
:root.univer-mobile.univer-app-schedule-page #l4 td.center>* { max-width:none; }
@media (min-width:1024px) {
 :root.univer-mobile[data-univer-page=attestation] #l4 td.center { padding-left:calc((100% - 960px) / 2)!important; padding-right:calc((100% - 960px) / 2)!important; }
 :root:root:root:root:root:root.univer-mobile[data-univer-page=attestation] #l4 td.center :is(.univer-app-table-scroll,table.univer-app-grades) { width:100%!important; max-width:100%!important; box-sizing:border-box; }
 :root:root:root:root:root:root.univer-mobile[data-univer-page=attestation] #l4 table.univer-app-grades>tbody>tr>:is(td,th):first-child:not([colspan]) { width:52%!important; }
}
/* Room for «Наверх» (back-to-top.js): a page with a small control under the button's place gets side padding as wide
   as the button and its gaps, so the button stands beside the page. Centred columns narrow on both sides; the schedule's
   full-width week and a narrow window only on the right. On screen only: the button is not printed. */
:root.univer-mobile[data-univer-top-room="144"] { --u-top-room:144px; }
:root.univer-mobile[data-univer-top-room="160"] { --u-top-room:160px; }
:root.univer-mobile[data-univer-top-room="176"] { --u-top-room:176px; }
:root.univer-mobile[data-univer-top-room="192"] { --u-top-room:192px; }
:root.univer-mobile[data-univer-top-room="208"] { --u-top-room:208px; }
:root.univer-mobile[data-univer-top-room="224"] { --u-top-room:224px; }
:root.univer-mobile[data-univer-top-room="240"] { --u-top-room:240px; }
@media screen and (min-width:900px) {
 :root:root.univer-mobile[data-univer-top-room]:not(.univer-app-schedule-page) #l4 td.center { padding-left:max(32px, calc((100% - var(--u-page)) / 2), var(--u-top-room))!important; padding-right:max(32px, calc((100% - var(--u-page)) / 2), var(--u-top-room))!important; }
 :root:root.univer-mobile[data-univer-top-room][data-univer-page=attestation] #l4 td.center { padding-left:max(32px, calc((100% - 960px) / 2), var(--u-top-room))!important; padding-right:max(32px, calc((100% - 960px) / 2), var(--u-top-room))!important; }
}
@media screen { :root:root.univer-mobile.univer-app-schedule-page[data-univer-top-room] #l4 td.center { padding-right:max(32px, var(--u-top-room))!important; } }
/* Right only at any width (a fractional 899–900 px viewport included); from 900 px the rules above outweigh it. */
@media screen {
 :root:root.univer-mobile[data-univer-top-room] #l4 td.center { padding-right:max(16px, var(--u-top-room))!important; }
}
:root.univer-mobile table.mt { background:transparent!important; border:0!important; }
:root.univer-mobile #l4 table.mt>tbody>tr.top>:is(.ct,.tt) { padding:16px 0 8px!important; background:none!important; color:var(--u-ink)!important; font-size:var(--u-fs-title)!important; font-weight:500!important; line-height:1.35!important;  }
:root.univer-mobile #l4 td.center>:is(table.mt:first-of-type,table.univer-app-empty-panel+table.mt)>tbody>tr.top:first-child>:is(.ct,.tt) { padding-top:8px!important; }
:root.univer-mobile #l4 table.mt>tbody>tr>.tt { padding:4px 0 8px!important; }
:root.univer-mobile #l4 .univer-app-study-panel .ct.message { font-size:var(--u-fs-meta)!important; color:var(--u-muted)!important; }
:root.univer-mobile #l4 :is(h1,h2,h3) { color:var(--u-ink); line-height:1.3;  }
:root.univer-mobile #l4 h4 { margin:12px 0 8px!important; padding:0!important; border:0!important; background:none!important; color:var(--u-ink)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; text-align:start!important; }
:root.univer-mobile .univer-app-nav-table>tbody>tr { gap:0!important; padding:0!important; border-bottom:1px solid var(--u-line); }
:root.univer-mobile .univer-app-nav-table>tbody>tr:last-child { border-bottom:0; }
:root.univer-mobile .univer-app-table-scroll .univer-app-nav-table>tbody>tr>td { border:0!important; padding:12px 14px!important; }
:root.univer-mobile table.univer-app-empty-panel, :root.univer-mobile .univer-app-study-panel>tbody>tr>.univer-app-secondary-heading { display:none!important; }

/* Site links that act as navigation are chips, not text boxes. */
:root.univer-mobile #l4 .ts { display:flex!important; flex-wrap:wrap; align-items:center; gap:8px; height:auto!important; min-height:0!important; padding:4px 0 12px!important; overflow:visible!important; }
:root.univer-mobile #l4 :is(.ts>a,a.univer-app-page-link,.univer-app-semester-nav a,.univer-app-nav-table a) { display:inline-flex!important; align-items:center; gap:8px; box-sizing:border-box; width:auto!important; max-width:100%; min-height:38px!important; flex:0 1 auto!important; float:none!important; margin:0!important; padding:7px 14px!important; border:1px solid var(--u-line)!important; border-radius:10px!important; background:#fff!important; box-shadow:var(--u-shadow); color:var(--u-ink)!important; font-size:14px!important; font-weight:500!important; line-height:1.35!important; text-align:start!important; text-decoration:none!important; }
:root.univer-mobile #l4 :is(.ts>a,a.univer-app-page-link,.univer-app-semester-nav a,.univer-app-nav-table a):hover { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-ink)!important; }
:root.univer-mobile #l4 .ts>a>img:is([src*="tb_left"],[src*="tb_right"]) { display:none!important; }
:root.univer-mobile #l4 .ts>a>img { width:16px; height:auto; }
:root.univer-mobile #l4 .ts>a:has(>img:is([src*="tb_left"],[src*="tb_right"])) { color:var(--u-muted)!important; font-weight:500!important; }
:root.univer-mobile #l4 .ts>a:has(>img[src*="tb_right"]) { margin-left:auto!important; }
:root.univer-mobile #l4 .ts>a:has(>img[src*="tb_left"])::before { content:'←'; font-weight:600; }
:root.univer-mobile #l4 .ts>a:has(>img[src*="tb_right"])::after { content:'→'; font-weight:600; }
:root.univer-mobile #l4 .ct>a.univer-app-page-link:only-child { min-height:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:20px!important; font-weight:500!important; }
:root.univer-mobile #l4 .ts>a.syllabus-info::before { content:''; width:16px; height:16px; flex:0 0 auto; background:currentColor; -webkit-mask:${icons.doc} center/contain no-repeat; }
:root.univer-mobile #l4 .univer-app-breadcrumb { display:flex!important; flex-wrap:wrap; align-items:center; gap:4px 8px; padding:4px 0 8px!important; }
:root.univer-mobile #l4 .univer-app-breadcrumb>a.univer-app-page-link { min-height:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-muted)!important; font-size:14px!important; font-weight:500!important; }
:root.univer-mobile #l4 .univer-app-breadcrumb>a.univer-app-page-link:not(:last-of-type)::after { content:'/'; margin-left:8px; color:var(--u-line-strong); }
:root.univer-mobile #l4 .univer-app-breadcrumb>a.univer-app-page-link:last-of-type { flex-basis:100%; color:var(--u-ink)!important; font-size:20px!important; font-weight:500!important; }
:root.univer-mobile .univer-app-quick { max-width:var(--u-page); margin:16px 32px 4px!important; display:grid!important; grid-template-columns:repeat(auto-fill,minmax(200px,1fr)); gap:10px; }
:root.univer-mobile .univer-app-quick a { display:flex!important; align-items:center; min-height:56px; box-sizing:border-box; padding:12px 16px!important; border:0!important; border-radius:8px!important; background:#fff!important; box-shadow:none; color:var(--u-ink)!important; font-size:14px!important; font-weight:500; text-align:start!important; justify-content:flex-start!important; text-decoration:none; }
:root.univer-mobile .univer-app-quick a::after { content:'→'; margin-left:auto; padding-left:12px; color:var(--u-ink); }
:root.univer-mobile .univer-app-quick a:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; }

/* Buttons that the adapter adds, and the site's own content buttons. */
:root.univer-mobile #l4 :is(input[type=button],input[type=submit],button):not(.univer-app-days button):not(.univer-app-schedule-modes button):not(.univer-app-schedule-export button) { border-radius:8px!important; font-weight:500; }
:root.univer-mobile .univer-app-course-open, :root.univer-mobile .univer-app-catalog-download { display:inline-flex!important; align-items:center; justify-content:center; gap:6px; width:auto!important; min-width:96px; min-height:34px!important; margin:0!important; padding:6px 14px!important; border:0!important; border-radius:8px!important; background:var(--u-brand)!important; color:#fff!important; font-size:var(--u-fs-body)!important; font-weight:500!important; cursor:pointer; }
:root.univer-mobile .univer-app-course-open:hover, :root.univer-mobile .univer-app-catalog-download:hover { background:var(--u-brand-strong)!important; }
:root.univer-mobile .univer-app-catalog-download::before { content:''; width:14px; height:14px; background:currentColor; -webkit-mask:${icons.download} center/contain no-repeat; }
:root.univer-mobile a.univer-app-action-link { display:inline-flex!important; align-items:center; min-height:32px!important; padding:5px 12px!important; border:1px solid var(--u-brand-line); border-radius:8px; background:var(--u-brand-soft); color:var(--u-ink)!important; font-size:13px!important; font-weight:500; white-space:nowrap; text-decoration:none; }
:root.univer-mobile a.univer-app-action-link:hover { background:var(--u-brand)!important; color:#fff!important; }

/* Tables. */
:root.univer-mobile .univer-app-table-scroll { margin:6px 0 16px!important; overflow:auto; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow); scrollbar-color:#a9b8c8 #eef2f6; }
:root.univer-mobile .univer-app-table-scroll>table { border:0!important; background:none!important; margin:0!important; }
:root.univer-mobile .univer-app-table-scroll table { border-collapse:collapse!important; border-spacing:0!important; }
:root.univer-mobile .univer-app-table-scroll :is(th,td) { border:0!important; border-bottom:1px solid var(--u-line)!important; padding:10px 14px!important; vertical-align:middle!important; font-size:14px!important; }
:root.univer-mobile .univer-app-table-scroll tr:last-child>td { border-bottom:0!important; }
:root.univer-mobile .univer-app-table-scroll th { background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:13px!important; font-weight:500!important; text-align:start; }
:root.univer-mobile .univer-app-table-scroll tbody>tr:hover>td { background:#fafcfe; }
:root.univer-mobile .univer-app-scroll-hint { margin:8px 0 -2px!important; font-size:var(--u-fs-meta)!important; color:var(--u-muted)!important; }
:root.univer-mobile .univer-app-matrix { table-layout:auto!important; }
:root.univer-mobile .univer-app-table-scroll .univer-app-matrix :is(th,td) { white-space:normal!important; text-align:center!important; border-right:1px solid #edf1f5!important; }
:root.univer-mobile .univer-app-table-scroll .univer-app-matrix tr>:first-child { text-align:start!important; }
:root.univer-mobile .univer-app-table-scroll .univer-app-matrix tr>:last-child { border-right:0!important; font-weight:500; }
/* Empty results are one quiet line: no tall placeholder and no column headings over nothing. */
:root.univer-mobile #l4 tr.univer-app-empty-row>:is(td,th) { padding:12px 16px!important; background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; text-align:start!important; }
:root.univer-mobile #l4 tr.univer-app-empty-row * { color:inherit!important; font-weight:inherit!important; font-size:inherit!important; }
:root.univer-mobile #l4 .univer-app-table-scroll>table:has(tr.univer-app-empty-row):not(:has(tr:not(.univer-app-empty-row)>td)) tr:has(>th) { display:none!important; }
:root.univer-mobile .univer-app-table-scroll:has(>table tr.univer-app-empty-row):not(:has(tr:not(.univer-app-empty-row)>td)) { box-shadow:none; border-style:dashed!important; }
:root.univer-mobile .univer-app-table-scroll>table { height:auto!important; }
:root.univer-mobile table.univer-app-empty-note { width:100%!important; margin:6px 0 12px; border-collapse:separate!important; background:none!important; border:0!important; }
:root.univer-mobile table.univer-app-empty-note td { padding:12px 16px!important; border:1px dashed var(--u-line-strong)!important; border-radius:10px; background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; text-align:start!important; }
:root.univer-mobile table.univer-app-empty-note td :is(b,strong,font,span,p) { color:inherit!important; font-weight:inherit!important; font-size:inherit!important; }
:root.univer-mobile .univer-app-card-row { border-radius:12px!important; border-color:var(--u-line)!important; box-shadow:var(--u-shadow); }
:root.univer-mobile #l4 .univer-app-form-table>tbody>tr.univer-app-form-spacer { display:none!important; }
:root.univer-mobile #l4 .univer-app-form-table:not(.univer-app-form-table .univer-app-form-table) { box-sizing:border-box; padding:4px 16px!important; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile #l4 .univer-app-form-table:not(.univer-app-form-table .univer-app-form-table)>tbody>tr { border-bottom:1px solid var(--u-line)!important; background:none!important; }
:root.univer-mobile #l4 .univer-app-form-table:not(.univer-app-form-table .univer-app-form-table)>tbody>tr:last-child { border-bottom:0!important; }
:root.univer-mobile #l4 .univer-app-form-table>tbody>tr>:is(td,th) { background:none!important; }
:root.univer-mobile #l4 .univer-app-form-table li { list-style:none!important; margin:0!important; padding:0!important; color:var(--u-muted); font-size:13px!important; font-weight:500; }
:root.univer-mobile #l4 .univer-app-sort-head>th { cursor:pointer; }
:root.univer-mobile .univer-app-sort-toggle { border-radius:8px!important; }
:root.univer-mobile .univer-app-pairs>tbody>tr { border-radius:12px!important; border-color:var(--u-line)!important; box-shadow:var(--u-shadow); background:#fff!important; }
:root.univer-mobile .univer-app-pairs>tbody>tr>td:nth-child(odd) { color:var(--u-muted)!important; font-size:13px!important; font-weight:500; }

/* УМКД: course list and material files. */
:root.univer-mobile .univer-app-courses tr.link { cursor:default; }
:root.univer-mobile .univer-app-courses tr.link>td:nth-child(2) { color:var(--u-ink)!important; font-weight:500; }
:root.univer-mobile .univer-app-courses tr.link>td:last-child { color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-courses tr.link>td:last-child .univer-app-course-open { margin-left:12px!important; vertical-align:middle; }
:root.univer-mobile input[type=file] { box-sizing:border-box; max-width:100%; min-height:44px; padding:6px!important; border:1px dashed var(--u-line-strong)!important; border-radius:10px!important; background:var(--u-surface-2)!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile input[type=file]::file-selector-button { margin-right:12px; padding:7px 14px; border:1px solid var(--u-brand-line); border-radius:8px; background:#fff; color:var(--u-ink); font:600 13px var(--u-font); cursor:pointer; }
:root.univer-mobile .univer-app-materials>tbody>tr>th:only-of-type { padding:14px 14px 8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:13px!important; font-weight:600!important;  text-align:start!important; }
:root.univer-mobile .univer-app-materials a.downLoad { display:inline-flex!important; align-items:center; gap:8px; min-height:32px!important; padding:5px 12px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; text-decoration:none!important; overflow-wrap:anywhere; }
:root.univer-mobile .univer-app-materials a.downLoad::before { content:''; width:15px; height:15px; flex:0 0 auto; background:currentColor; -webkit-mask:${icons.download} center/contain no-repeat; }
:root.univer-mobile .univer-app-materials a.downLoad:hover { background:var(--u-brand)!important; border-color:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile :is(.univer-app-courses,.univer-app-materials) td { overflow-wrap:break-word!important; }

/* Schedule: a day agenda at every width, plus the unchanged source grid on request. */
:root.univer-mobile .univer-app-schedule { display:block!important; width:100%; }
:root.univer-mobile .univer-app-schedule-modes { display:flex; gap:2px; margin:4px 0 12px; padding:3px; width:max-content; max-width:100%; border:1px solid var(--u-line); border-radius:10px; background:#e9eef4; }
:root.univer-mobile #l4 .univer-app-schedule-modes button { min-height:34px!important; padding:6px 16px!important; border:0!important; border-radius:8px!important; background:transparent!important; color:var(--u-muted)!important; font-size:14px!important; font-weight:500!important; }
:root.univer-mobile #l4 .univer-app-schedule-modes button[aria-pressed=true] { background:#fff!important; color:var(--u-ink)!important; box-shadow:0 1px 3px rgba(16,40,70,.12); }
:root.univer-mobile .univer-app-schedule-bar { display:flex; flex-wrap:wrap; align-items:center; gap:8px 12px; margin:4px 0 12px; }
:root.univer-mobile .univer-app-schedule-bar>.univer-app-schedule-modes { margin:0; }
:root.univer-mobile .univer-app-schedule-export { display:flex; flex-wrap:wrap; gap:6px; margin-left:auto; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-schedule-export button { width:auto!important; height:var(--u-control)!important; min-height:var(--u-control)!important; margin:0!important; padding:0 14px!important; border:1px solid var(--u-line)!important; border-radius:8px!important; background:var(--u-surface)!important; box-shadow:none!important; color:var(--u-text)!important; font-size:13px!important; font-weight:500!important; cursor:pointer; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-schedule-export button:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-schedule-export button:focus-visible { outline:2px solid var(--u-brand)!important; outline-offset:2px; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-schedule-export button[aria-busy=true] { opacity:.55; cursor:progress; pointer-events:none; }
:root.univer-mobile .univer-app-schedule-bar { grid-column:1/-1; }
:root.univer-mobile .univer-app-days { display:flex!important; flex-wrap:wrap; gap:6px; margin:4px 0 16px!important; }
:root.univer-mobile #l4 .univer-app-days button { display:inline-flex; align-items:center; gap:8px; min-height:36px!important; padding:6px 12px!important; border:1px solid var(--u-line)!important; border-radius:6px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:500!important; }
:root.univer-mobile #l4 .univer-app-days button:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; }
:root.univer-mobile #l4 .univer-app-days button[aria-pressed=true] { background:var(--u-brand)!important; border-color:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile .univer-app-days button:last-child { order:-1; }
:root.univer-mobile .univer-app-day-count { min-width:20px; padding:0 6px; box-sizing:border-box; border-radius:6px; background:var(--u-surface-2); color:var(--u-muted); font-size:var(--u-fs-caption)!important; font-weight:600; line-height:20px; text-align:center; }
:root.univer-mobile .univer-app-days button[aria-pressed=true] .univer-app-day-count { background:rgba(255,255,255,.22); color:#fff; }
/* All days: up to six columns, so a full-width window shows the week in one row; narrower windows reflow. */
:root.univer-mobile .univer-app-schedule[data-all-days=true] { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(max(220px,calc((100% - (var(--univer-days,6) - 1) * 12px) / var(--univer-days,6))),1fr)); gap:12px; align-items:start; }
:root.univer-mobile .univer-app-schedule-modes, :root.univer-mobile .univer-app-days { grid-column:1/-1; }
/* The days share the whole width equally: a track is never narrower than one day's share of the row, so a wide window
   has exactly as many tracks as days and no empty ones beside them. Everything else in the view, the mode switch, the
   next-class and syllabus panels, the day tabs and one day shown alone, keeps to one centred column as wide as the page,
   so switching between all days and one day moves nothing but the days themselves. */
:root.univer-mobile .univer-app-schedule:not([data-all-days=true]) { max-width:var(--u-page); margin-left:auto!important; margin-right:auto!important; }
:root:root.univer-mobile .univer-app-schedule[data-all-days=true]>:not(.univer-app-day) { margin-left:max(0px,calc((100% - var(--u-page)) / 2))!important; margin-right:max(0px,calc((100% - var(--u-page)) / 2))!important; }
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-days { margin:0!important; }
:root.univer-mobile .univer-app-day { min-width:0; overflow:hidden; background:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-day h3 { margin:0!important; padding:10px 14px!important; background:var(--u-surface-2); border-bottom:1px solid var(--u-line); color:var(--u-ink)!important; font-size:var(--u-fs-strong)!important; font-weight:500; }
:root.univer-mobile .univer-app-lesson { margin:0!important; padding:12px 14px!important; background:#fff!important; border:0!important; border-bottom:1px solid #edf1f5!important; border-radius:0!important; }
:root.univer-mobile .univer-app-lesson:last-child { border-bottom:0!important; }
:root.univer-mobile .univer-app-lesson time { display:inline-block; padding:2px 8px!important; border-radius:6px!important; background:var(--u-teal-soft)!important; color:var(--u-teal)!important; font-size:var(--u-fs-meta)!important; font-weight:600!important; font-variant-numeric:tabular-nums; }
:root.univer-mobile .univer-app-lesson p { margin:4px 0 0!important; font-size:var(--u-fs-body)!important; line-height:1.45!important; color:var(--u-muted); }
:root.univer-mobile .univer-app-lesson p.univer-app-subject { margin-top:6px!important; color:var(--u-ink)!important; font-size:var(--u-fs-strong)!important; font-weight:500!important; line-height:1.35!important; }
:root.univer-mobile .univer-app-lesson a.univer-app-page-link { margin-top:8px!important; min-height:32px!important; padding:4px 10px!important; font-size:var(--u-fs-meta)!important; box-shadow:none; }
:root.univer-mobile .univer-app-no-classes { margin:0!important; padding:12px 14px!important; background:transparent!important; border:0!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; }
/* A free day in All days is one compact line instead of a tall empty card. */
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-day:has(>.univer-app-no-classes) { display:flex; align-items:center; flex-wrap:wrap; background:var(--u-surface-2); box-shadow:none; }
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-day:has(>.univer-app-no-classes) h3 { flex:1 1 auto; border-bottom:0; background:none; color:var(--u-muted)!important; }
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-day:has(>.univer-app-no-classes)>.univer-app-no-classes { flex:0 0 auto; padding:10px 14px 10px 0!important; }
/* Current and next classes of today; the inset bar on the left marks that state, not decoration. */
:root.univer-mobile .univer-app-lesson.univer-app-lesson-now { background:var(--u-brand-soft)!important; box-shadow:inset 3px 0 0 var(--u-brand); }
:root.univer-mobile .univer-app-lesson.univer-app-lesson-next { box-shadow:inset 3px 0 0 var(--u-brand-line); }
:root.univer-mobile .univer-app-lesson-badge { display:inline-block; margin-left:8px; padding:1px 8px; border-radius:6px; background:var(--u-brand); color:#fff; font-size:var(--u-fs-caption); font-weight:600; vertical-align:1px; }
:root.univer-mobile .univer-app-lesson-next .univer-app-lesson-badge { background:var(--u-surface-2); color:var(--u-ink); box-shadow:inset 0 0 0 1px var(--u-brand-line); }
:root.univer-mobile .univer-app-lesson.univer-app-lesson-done>:not(.univer-app-lesson-badge) { opacity:.55; }
:root.univer-mobile .univer-app-lesson-done .univer-app-lesson-badge { background:var(--u-surface-2); color:var(--u-muted); }
:root.univer-mobile .univer-app-day h3 .univer-app-day-range { margin-left:8px; color:var(--u-muted); font-size:var(--u-fs-meta); font-weight:500; font-variant-numeric:tabular-nums; }
/* Summary of the day: class in progress, break or countdown to the next class. */
:root.univer-mobile .univer-app-now { grid-column:1/-1; margin:0 0 12px; padding:14px 16px; background:#fff; border:1px solid var(--u-brand-line); border-radius:12px; box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-now[hidden], :root.univer-mobile .univer-app-now [hidden] { display:none!important; }
:root.univer-mobile .univer-app-schedule[data-mode=grid]>.univer-app-now { display:none!important; }
:root.univer-mobile .univer-app-now[data-state=now] { border-left-color:var(--u-brand); background:var(--u-brand-soft); }
:root.univer-mobile .univer-app-now:is([data-state=pause],[data-state=gap],[data-state=first]) { border-left-color:var(--u-teal); }
:root.univer-mobile .univer-app-now-head { display:flex; align-items:baseline; justify-content:space-between; gap:12px; flex-wrap:wrap; }
:root.univer-mobile .univer-app-now-title { color:var(--u-muted); font-size:var(--u-fs-meta); font-weight:500; }
:root.univer-mobile .univer-app-now[data-state=now] .univer-app-now-title { color:var(--u-ink); }
:root.univer-mobile .univer-app-now-count { color:var(--u-ink); font-size:var(--u-fs-title); font-weight:600; font-variant-numeric:tabular-nums; }
:root.univer-mobile .univer-app-now[data-state=now] .univer-app-now-count { color:var(--u-ink); }
:root.univer-mobile .univer-app-now:is([data-state=pause],[data-state=gap],[data-state=first]) .univer-app-now-count { color:var(--u-teal); }
:root.univer-mobile #l4 .univer-app-now p { margin:6px 0 0!important; line-height:1.4!important; }
:root.univer-mobile #l4 .univer-app-now p.univer-app-now-subject { color:var(--u-ink)!important; font-size:var(--u-fs-section)!important; font-weight:500!important; }
:root.univer-mobile #l4 .univer-app-now p.univer-app-now-meta { color:var(--u-muted)!important; font-size:var(--u-fs-body)!important; }
:root.univer-mobile .univer-app-now-progress { height:6px; margin-top:10px; border-radius:6px; background:var(--u-brand-line); overflow:hidden; }
:root.univer-mobile .univer-app-now-progress>span { display:block; height:100%; border-radius:inherit; background:var(--u-brand); }
:root.univer-mobile .univer-app-now-foot { display:flex; align-items:center; justify-content:space-between; gap:8px 12px; flex-wrap:wrap; margin-top:10px; }
:root.univer-mobile .univer-app-now-foot:not(:has(>:not([hidden]))) { display:none; }
:root.univer-mobile .univer-app-now-note { color:var(--u-text); font-size:var(--u-fs-meta); }
:root.univer-mobile #l4 .univer-app-now-action { min-height:32px!important; padding:4px 14px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; cursor:pointer; }
:root.univer-mobile #l4 .univer-app-now-action:hover { background:var(--u-brand-soft)!important; }
:root.univer-mobile #l4 .univer-app-now-action:focus-visible { outline:2px solid var(--u-brand); outline-offset:2px; }
:root.univer-mobile .univer-app-schedule[data-mode=grid] { display:block!important; }
:root.univer-mobile .univer-app-schedule[data-mode=grid]>.univer-app-day, :root.univer-mobile .univer-app-schedule[data-mode=grid]>.univer-app-days { display:none!important; }
:root.univer-mobile table.univer-app-schedule-source { display:none!important; }
:root.univer-mobile table.univer-app-schedule-source.univer-app-schedule-grid-visible { display:table!important; width:100%!important; min-width:1100px!important; table-layout:auto!important; }
:root.univer-mobile .univer-app-schedule-scroll { display:none!important; }
:root.univer-mobile .univer-app-schedule-scroll.univer-app-schedule-grid-visible { display:block!important; }
:root.univer-mobile #l4 .univer-app-schedule-content { height:auto!important; max-height:none!important; overflow:visible!important; position:static!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct.univer-app-schedule-title { display:flex!important; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:8px 16px; padding:4px 0 8px!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct.univer-app-schedule-title>a:first-child { flex:1 1 420px; min-height:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:20px!important; font-weight:500!important; line-height:1.35!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct>a.univer-app-print-action { flex:0 0 auto!important; color:var(--u-ink)!important; font-weight:500!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct>a.univer-app-print-action::before { content:''; width:16px; height:16px; background:currentColor; -webkit-mask:${icons.print} center/contain no-repeat; }
:root.univer-mobile table.schedule :is(th,td) { font-size:var(--u-fs-body)!important; white-space:normal!important; min-width:0!important; vertical-align:top!important; padding:8px!important; border:1px solid var(--u-line)!important; }
:root.univer-mobile table.schedule th { background:var(--u-surface-2)!important; color:var(--u-ink)!important; font-weight:500; }
:root.univer-mobile table.schedule td.time { width:100px!important; color:var(--u-teal); font-variant-numeric:tabular-nums; background:var(--u-surface-2)!important; text-align:center!important; vertical-align:middle!important; }
:root.univer-mobile table.schedule tr:first-child>:first-child, :root.univer-mobile table.schedule col:first-child { width:96px!important; min-width:96px!important; }
:root.univer-mobile table.schedule td.time { min-width:96px!important; }
:root.univer-mobile table.schedule tr:first-child>th:not(:first-child) { width:14%; }
:root.univer-mobile table.schedule td.time[data-univer-time]>* { display:none!important; }
:root.univer-mobile table.schedule td.time[data-univer-time]::before { content:attr(data-univer-time); display:block; color:var(--u-teal); font-size:var(--u-fs-meta); font-weight:600; line-height:1.4; white-space:nowrap; }
:root.univer-mobile table.schedule .groups>div { box-sizing:border-box; padding:8px!important; border-radius:8px; }
:root.univer-mobile table.schedule .groups p { margin:0 0 3px!important; padding:0!important; border:0!important; background:none!important; line-height:1.4!important; }
:root.univer-mobile table.schedule :is(p,.groups) { width:auto!important; font-size:var(--u-fs-meta)!important; overflow-wrap:break-word; }
:root.univer-mobile table.schedule .groups p:first-child { color:var(--u-ink); font-size:var(--u-fs-body)!important; font-weight:500; }
:root.univer-mobile #l4 .univer-app-schedule-controls { display:block!important; height:auto!important; min-height:0!important; position:static!important; padding:4px 0 8px!important; clear:both; }
:root.univer-mobile #l4 .univer-app-schedule-controls>div { float:none!important; position:static!important; width:100%!important; height:auto!important; }
:root.univer-mobile #weekSelect { display:block!important; width:100%!important; max-width:420px!important; min-height:40px!important; margin:0!important; font-size:14px!important; font-weight:500; color:var(--u-ink)!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct { clear:both; height:auto!important; }

/* Documents, FAQ, GPA, news and the website footer. */
:root.univer-mobile #l4 input.univer-app-catalog-search { display:block; clear:both; width:100%!important; max-width:560px!important; height:42px; margin:4px 0 16px!important; padding-left:38px!important; background:#fff ${icons.search} 12px center/18px no-repeat!important; }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university) { margin:0 0 10px!important; overflow:hidden; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)>summary { display:flex!important; align-items:center; gap:12px; min-height:0!important; padding:14px 18px!important; color:var(--u-ink)!important; font-size:15px!important; font-weight:500!important; }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)>summary::after { content:''; width:8px; height:8px; margin:-4px 2px 0 auto; flex:0 0 auto; border-right:2px solid var(--u-muted); border-bottom:2px solid var(--u-muted); transform:rotate(45deg); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)[open]>summary::after { margin-top:4px; transform:rotate(-135deg); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)[open]>summary { border-bottom:1px solid var(--u-line); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university):hover>summary { background:var(--u-surface-2); }
:root.univer-mobile #l5 .univer-app-university>div { padding:12px 18px!important; font-size:13px!important; color:var(--u-muted); }
:root.univer-mobile .univer-app-catalog tr.file { margin:0!important; border:0!important; border-bottom:1px solid var(--u-line)!important; border-radius:0!important; padding:12px 18px!important; }
:root.univer-mobile .univer-app-catalog tr.file:last-child { border-bottom:0!important; }
:root.univer-mobile .univer-app-catalog tr.file>td:nth-child(3) { font-weight:500; font-style:normal; color:var(--u-ink); }
:root.univer-mobile .univer-app-catalog tr.file>td:nth-child(3) :is(i,em) { font-style:normal; }
:root.univer-mobile .univer-app-catalog tr.file>td[data-univer-field]::before { color:var(--u-muted)!important; font-size:12px!important; }
:root.univer-mobile .univer-app-faq>table.inner>tbody>tr>:is(td,th) { padding:12px 18px!important; }
:root.univer-mobile .univer-app-news .news { margin:0 0 16px!important; padding:20px 24px!important; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-news .news>td:first-child { padding-bottom:12px!important; margin-bottom:12px!important; border-bottom:1px solid var(--u-line)!important; }
:root.univer-mobile .univer-app-news .news>td:first-child .date { padding:4px 10px!important; border:0!important; border-radius:8px!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; font-weight:500; }
:root.univer-mobile .univer-app-news .news>td:first-child>div:not(.date) { padding:3px 10px!important; border:0!important; border-radius:6px!important; background:var(--u-danger-soft)!important; color:var(--u-danger)!important; font-size:12px!important; font-weight:500; }
:root.univer-mobile .univer-app-news .news>td:first-child>div:not(.date) * { color:inherit!important; background:none!important; }
:root.univer-mobile .univer-app-news .news>td:nth-child(2) :is(table) { max-width:100%; }
:root.univer-mobile table.univer-app-news { overflow-anchor:none; }
:root.univer-mobile .univer-app-news-body { display:flow-root; position:relative; }
:root.univer-mobile .univer-app-news-body[data-collapsed=true] { max-height:248px!important; overflow:hidden!important; }
:root.univer-mobile .univer-app-news-body[data-collapsed=true]::after { content:''; position:absolute; bottom:0; left:0; right:0; height:36px; pointer-events:none; background:linear-gradient(transparent,#fff); }
:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle { display:inline-flex!important; align-items:center; gap:10px; min-height:38px!important; margin:8px 0 0!important; padding:6px 10px!important; border:0!important; border-radius:6px!important; background:transparent!important; color:var(--u-ink)!important; font:600 14px/1.4 var(--u-font)!important; box-shadow:none!important; }
:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle:hover { background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-news-toggle::after { content:''; width:6px; height:6px; border-right:1.5px solid currentColor; border-bottom:1.5px solid currentColor; transform:translateY(-2px) rotate(45deg); }
:root.univer-mobile .univer-app-news-toggle[aria-expanded=true]::after { transform:translateY(2px) rotate(225deg); }
/* Course links keep the grade matrices intact; syllabi form a compact list below their trigger. */
:root.univer-mobile #l4 .univer-app-course-source[hidden], :root.univer-mobile #l4 a[data-univer-syllabus-source][hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-course-heading { display:flex; align-items:flex-start; gap:18px; margin:12px 0 8px; padding-top:16px; border-top:1px solid var(--u-line-strong); text-align:left; }
:root.univer-mobile #l4 .univer-app-course-title { flex:1; min-width:0; }
:root.univer-mobile #l4 .univer-app-course-name, :root.univer-mobile #l4 .univer-app-course-title>.univer-app-attendance-course { display:inline; font-size:20px!important; font-weight:500; line-height:1.4; }
:root.univer-mobile #l4 .univer-app-course-meta { display:flex; flex-wrap:wrap; gap:3px 18px; margin-top:4px; color:var(--u-muted); font-size:var(--u-fs-meta)!important; font-weight:400; }
:root.univer-mobile #l4 .univer-app-course-meta span { font-size:var(--u-fs-meta)!important; }
:root.univer-mobile #l4 button.univer-app-course-syllabus { display:inline-flex; align-items:center; justify-content:center; gap:8px; flex:0 0 auto; min-height:40px!important; padding:9px 14px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-family:inherit!important; font-size:14px!important; font-weight:500; cursor:pointer; }
:root.univer-mobile #l4 button.univer-app-course-syllabus::before { content:''; width:16px; height:16px; background:currentColor; -webkit-mask:${icons.doc} center/contain no-repeat; }
:root.univer-mobile #l4 button.univer-app-course-syllabus::after { content:''; width:6px; height:6px; margin:-3px 1px 0 5px; border-right:1.5px solid currentColor; border-bottom:1.5px solid currentColor; transform:rotate(45deg); }
:root.univer-mobile #l4 button.univer-app-course-syllabus[aria-expanded=true]::after { margin-top:3px; transform:rotate(225deg); }
:root.univer-mobile #l4 button.univer-app-course-syllabus:hover, :root.univer-mobile #l4 button.univer-app-course-syllabus[aria-expanded=true] { background:var(--u-brand-soft)!important; border-color:var(--u-brand)!important; }
:root.univer-mobile #l4 .univer-app-attendance-course { color:var(--u-ink)!important; text-decoration:underline; text-decoration-thickness:1px; text-underline-offset:3px; }
:root.univer-mobile #l4 .univer-app-attendance-course:hover { color:var(--u-ink)!important; }
/* Lesson names are reading labels; the independent filter controls carry the interaction. */
:root.univer-mobile #l4 a[data-univer-lesson-label][hidden], :root.univer-mobile #l4 .univer-app-attendance-part[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-lesson-label { display:flex; align-items:baseline; flex-wrap:wrap; gap:4px 14px; float:none!important; margin:8px 0 2px; color:var(--u-text)!important; font-size:14px!important; font-weight:400; cursor:text; }
:root.univer-mobile #l4 .univer-app-lesson-label strong { font-size:15px!important; font-weight:500; color:var(--u-ink); }
:root.univer-mobile #l4 .univer-app-lesson-label>span { font-size:13px!important; color:var(--u-muted); }
:root.univer-mobile #l4 .univer-app-attendance-tools { margin:0 0 8px; }
:root.univer-mobile #l4 .univer-app-attendance-courses { margin:0 0 14px; padding:0 0 14px; border-bottom:1px solid var(--u-line); text-align:left; }
:root.univer-mobile #l4 .univer-app-attendance-courses[hidden] { display:none!important; }
:root.univer-mobile #l4 p.univer-app-attendance-course-caption { margin:0 0 8px!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile #l4 .univer-app-attendance-course-list { display:flex; flex-wrap:wrap; gap:6px; }
:root.univer-mobile #l4 .univer-app-attendance-courses button { max-width:100%; min-height:38px!important; padding:8px 14px!important; border:1px solid var(--u-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font:600 14px/1.4 var(--u-font)!important; text-align:left; white-space:normal; overflow-wrap:anywhere; }
:root.univer-mobile #l4 .univer-app-attendance-courses button:hover { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; }
:root.univer-mobile #l4 .univer-app-attendance-courses button[aria-pressed=true] { background:var(--u-brand)!important; color:#fff!important; border-color:var(--u-brand)!important; }
:root.univer-mobile #l4 .univer-app-attendance-filters { display:flex; flex-wrap:wrap; align-items:center; gap:6px; margin:0; padding:6px; width:fit-content; max-width:100%; box-sizing:border-box; background:#fff; border:1px solid var(--u-line); border-radius:10px; }
:root.univer-mobile #l4 .univer-app-attendance-filter-caption { padding:0 10px; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile #l4 p.univer-app-attendance-filter-note { margin:8px 0 0!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile #l4 .univer-app-attendance-filters button { min-height:38px!important; padding:8px 16px!important; border:1px solid transparent!important; border-radius:6px!important; background:transparent!important; color:var(--u-muted)!important; font-family:inherit!important; font-size:14px!important; font-weight:500!important; cursor:pointer; }
:root.univer-mobile #l4 .univer-app-attendance-filters button:hover { color:var(--u-ink)!important; background:var(--u-surface-2)!important; }
:root.univer-mobile #l4 .univer-app-attendance-filters button[aria-pressed=true] { background:var(--u-brand-soft)!important; color:var(--u-ink)!important; border-color:var(--u-brand-line)!important; }
:root.univer-mobile #l4 tr.univer-app-attendance-owned, :root.univer-mobile #l4 tr.univer-app-attendance-owned>td { display:block!important; box-sizing:border-box; width:100%!important; }
:root.univer-mobile #l4 .univer-app-attendance-summary { display:grid; grid-template-columns:minmax(220px,1fr) minmax(280px,1.5fr); gap:12px 24px; box-sizing:border-box; width:100%; margin:0 0 12px; padding:14px 16px; border:1px solid var(--u-line); border-radius:10px; background:#fff; color:var(--u-text); text-align:left; }
:root.univer-mobile #l4 .univer-app-attendance-summary:not(:has(.univer-app-attendance-overall-block)) { grid-template-columns:1fr; }
:root.univer-mobile #l4 .univer-app-attendance-summary p { display:flex; flex-wrap:wrap; gap:4px 18px; margin:4px 0!important; font-size:14px!important; line-height:1.6!important; }
:root.univer-mobile #l4 .univer-app-attendance-summary strong { min-width:96px; font-weight:500; }
:root.univer-mobile #l4 .univer-app-attendance-summary p.univer-app-attendance-summary-title { margin:0 0 6px!important; font-size:var(--u-fs-meta)!important; font-weight:500; color:var(--u-muted); }
:root.univer-mobile #l4 .univer-app-type-row { display:flex; flex-wrap:wrap; align-items:center; gap:8px 12px; padding:8px 0; border-top:1px solid var(--u-line); }
:root.univer-mobile #l4 .univer-app-attendance-summary-title+.univer-app-type-row { border-top:0; padding-top:2px; }
:root.univer-mobile #l4 .univer-app-type-label { display:inline-flex; align-items:center; gap:8px; min-width:120px; color:var(--u-ink); font-size:14px; font-weight:500; }
:root.univer-mobile #l4 .univer-app-type-label::before { content:''; width:10px; height:10px; border-radius:3px; background:var(--u-brand); }
:root.univer-mobile #l4 .univer-app-type-row[data-kind=seminar] .univer-app-type-label::before { background:var(--u-teal); }
:root.univer-mobile #l4 .univer-app-type-row[data-kind=sro] .univer-app-type-label::before { background:#b45309; }
:root.univer-mobile #l4 .univer-app-type-empty { color:var(--u-muted); font-size:13px; }
:root.univer-mobile #l4 .univer-app-attendance-overall-block { border-right:1px solid var(--u-line); padding-right:24px; }
:root.univer-mobile #l4 p.univer-app-attendance-overall { margin:0 0 8px!important; font-size:var(--u-fs-meta)!important; font-weight:500; color:var(--u-muted); }
:root.univer-mobile #l4 p.univer-app-attendance-overall-value { color:var(--u-ink); font-size:var(--u-fs-section)!important; font-weight:500; }
/* Control periods (РК1, РК2, exam) are score tiles: the number leads, the maximum and a bar give scale. */
:root.univer-mobile #l4 :is(.univer-app-attendance-overview,.univer-app-attendance-filter-note,.univer-app-attendance-overall-value,.univer-app-attendance-actions)[hidden] { display:none!important; }
/* «Печать и PDF» opens the grade summary (grade-report.js): one quiet button at the right of the class-type row. */
:root.univer-mobile #l4 .univer-app-attendance-filter-row { display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:10px 16px; }
:root.univer-mobile #l4 .univer-app-attendance-actions { display:flex; flex-wrap:wrap; align-items:center; gap:10px 16px; margin:0 0 0 auto; }
/* The goal level: «Нет» and three levels, one pressed; what is left on the totals is counted to it. One :root more than
   the generic #l4 control rule below, which has the same weight and comes later. */
:root.univer-mobile #l4 .univer-app-goal { display:flex; flex-wrap:wrap; align-items:center; gap:6px; }
:root.univer-mobile #l4 .univer-app-goal-caption { margin-right:4px; color:var(--u-muted); font-size:13px; font-weight:500; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-goal button { width:auto!important; height:var(--u-control)!important; min-height:var(--u-control)!important; margin:0!important; padding:0 12px!important; border:1px solid var(--u-line)!important; border-radius:999px!important; background:var(--u-surface)!important; box-shadow:none!important; color:var(--u-text)!important; font-size:13px!important; font-weight:500!important; cursor:pointer; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-goal button:hover { background:var(--u-surface-2)!important; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-goal button[aria-pressed=true] { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; font-weight:600!important; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-goal button:focus-visible { outline:2px solid var(--u-brand)!important; outline-offset:2px; }
/* What is left to the mark stands out as a soft plate in the band's tint; absences are one line of the course card. */
:root:root:root:root.univer-mobile #l4 .univer-app-score-next[data-univer-left] { display:inline-block; max-width:100%; box-sizing:border-box; padding:2px 8px; border-radius:6px; background:var(--univer-band-soft); font-weight:600; }
:root:root:root:root.univer-mobile #l4 p.univer-app-attendance-absences { display:flex; flex-wrap:wrap; align-items:baseline; gap:4px 8px; margin:0!important; padding:10px 16px!important; color:var(--u-muted); font-size:14px!important; }
:root:root:root:root.univer-mobile #l4 p.univer-app-attendance-limit { margin:0 0 12px!important; padding:10px 14px!important; border-radius:12px; background:var(--univer-band-soft); border:1px solid var(--univer-band-line); color:var(--univer-band-ink)!important; font-size:14px!important; font-weight:600; line-height:1.4!important; text-align:start; }
:root:root:root:root.univer-mobile #l4 p.univer-app-attendance-limit[hidden] { display:none!important; }
:root.univer-mobile #l4 p.univer-app-attendance-absences b { color:var(--u-text); font-weight:600; }
:root.univer-mobile #l4 p.univer-app-attendance-absences small { font-size:13px; }
:root:root:root:root.univer-mobile #l4 button.univer-app-attendance-report { display:inline-flex!important; align-items:center; gap:8px; width:auto!important; height:36px!important; min-height:36px!important; margin:0!important; padding:0 14px!important; border:1px solid var(--u-line)!important; border-radius:10px!important; background:var(--u-surface)!important; box-shadow:none!important; color:var(--u-brand)!important; font-size:13px!important; font-weight:600!important; }
:root:root:root:root.univer-mobile #l4 button.univer-app-attendance-report:hover { background:var(--u-brand-soft)!important; }
:root:root:root:root.univer-mobile #l4 button.univer-app-attendance-report:focus-visible { outline:2px solid var(--u-brand)!important; outline-offset:2px; }
:root.univer-mobile #l4 button.univer-app-attendance-report svg { flex:none; width:18px; height:18px; fill:none; stroke:currentColor; stroke-width:1.8; stroke-linecap:round; stroke-linejoin:round; }
:root.univer-mobile #l4 .univer-app-attendance-scores { display:flex; flex-wrap:wrap; gap:8px; }
:root.univer-mobile #l4 .univer-app-score { position:relative; display:grid; grid-template-columns:auto 1fr; align-items:baseline; column-gap:4px; min-width:104px; box-sizing:border-box; padding:8px 12px 12px; overflow:hidden; border:1px solid var(--u-brand-line); border-radius:10px; background:var(--u-brand-soft); }
:root.univer-mobile #l4 .univer-app-score-label { grid-column:1/-1; color:var(--u-ink); font-size:var(--u-fs-caption); font-weight:600;  }
:root.univer-mobile #l4 .univer-app-score-value { color:var(--u-ink); font-size:var(--u-fs-display); font-weight:600; line-height:1.2; font-variant-numeric:tabular-nums; }
:root.univer-mobile #l4 .univer-app-score-max { color:var(--u-muted); font-size:var(--u-fs-meta); font-variant-numeric:tabular-nums; }
:root.univer-mobile #l4 .univer-app-score-bar { position:absolute; left:0; right:0; bottom:0; height:4px; background:var(--u-brand-line); }
:root.univer-mobile #l4 .univer-app-score-bar::after { content:''; position:absolute; top:0; bottom:0; left:0; width:var(--univer-score); background:var(--u-brand); }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact { gap:6px; }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact .univer-app-score { display:inline-flex; align-items:baseline; gap:6px; min-width:0; padding:3px 10px; border-radius:6px; }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact .univer-app-score-value { font-size:var(--u-fs-body); }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact .univer-app-score-max { font-size:var(--u-fs-caption); }
/* Course summary at the top of the journal; a course name opens that course. */
:root.univer-mobile #l4 .univer-app-attendance-overview { margin:12px 0 0; padding:2px 16px 4px; background:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); text-align:left; }
:root.univer-mobile #l4 p.univer-app-attendance-overview-title { margin:10px 0 6px!important; color:var(--u-muted); font-size:var(--u-fs-meta)!important; font-weight:500; }
:root.univer-mobile #l4 ul.univer-app-attendance-overview-list { margin:0!important; padding:0!important; list-style:none; }
:root.univer-mobile #l4 ul.univer-app-attendance-overview-list>li { display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:6px 16px; margin:0!important; padding:8px 0!important; border-top:1px solid var(--u-line); list-style:none; }
:root.univer-mobile #l4 button.univer-app-attendance-overview-name { flex:1 1 240px; min-width:0; min-height:32px!important; padding:4px 0!important; border:0!important; background:none!important; color:var(--u-ink)!important; font:600 var(--u-fs-body)/1.4 var(--u-font)!important; text-align:left; white-space:normal; overflow-wrap:anywhere; cursor:pointer; }
:root.univer-mobile #l4 button.univer-app-attendance-overview-name:hover { color:var(--u-ink)!important; text-decoration:underline; text-underline-offset:3px; }
:root.univer-mobile #l4 .univer-app-attendance-overview-empty { color:var(--u-muted); font-size:var(--u-fs-meta); }
/* The class log opens on request; an empty course is one line. */
:root.univer-mobile #l4 button.univer-app-attendance-details-toggle { grid-column:1/-1; justify-self:start; display:inline-flex; align-items:center; gap:8px; min-height:32px!important; margin:0 0 -4px -8px!important; padding:4px 8px!important; border:0!important; border-radius:6px!important; background:transparent!important; color:var(--u-ink)!important; font:600 var(--u-fs-body)/1.4 var(--u-font)!important; cursor:pointer; }
:root.univer-mobile #l4 button.univer-app-attendance-details-toggle:hover { background:var(--u-brand-soft)!important; }
:root.univer-mobile #l4 button.univer-app-attendance-details-toggle::after { content:''; width:6px; height:6px; border-right:1.5px solid currentColor; border-bottom:1.5px solid currentColor; transform:translateY(-2px) rotate(45deg); }
:root.univer-mobile #l4 button.univer-app-attendance-details-toggle[aria-expanded=true]::after { transform:translateY(2px) rotate(225deg); }
:root.univer-mobile #l4 .univer-app-attendance-summary.univer-app-attendance-summary-empty { display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:4px 16px; padding:10px 16px; border-style:dashed; background:var(--u-surface-2); color:var(--u-muted); }
:root.univer-mobile #l4 .univer-app-attendance-summary-empty p { margin:0!important; font-size:var(--u-fs-meta)!important; }
@media(max-width:860px) {
 :root.univer-mobile #l4 .univer-app-course-heading { flex-direction:column; gap:12px; }
 :root.univer-mobile #l4 .univer-app-attendance-summary { grid-template-columns:1fr; gap:12px; }
 :root.univer-mobile #l4 .univer-app-attendance-overall-block { border-right:0; border-bottom:1px solid var(--u-line); padding:0 0 10px; }
 :root.univer-mobile #l4 .univer-app-attendance-filter-caption { flex-basis:100%; padding:6px 10px 2px; }
}
:root.univer-mobile #l4 .univer-app-syllabi { box-sizing:border-box; margin:10px 0 14px; padding:12px 18px; background:#fff; border:1px solid var(--u-brand-line); border-radius:8px; text-align:left; font-size:14px!important; font-weight:400; }
:root.univer-mobile #l4 .univer-app-syllabi[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-syllabi ul { margin:0 0 12px!important; padding:0!important; list-style:none; }
:root.univer-mobile #l4 .univer-app-syllabi a { font-size:14px!important; font-weight:400; }
:root.univer-mobile #l4 .univer-app-syllabi li { display:flex; align-items:baseline; gap:12px; padding:10px 0; border-bottom:1px solid var(--u-line); }
:root.univer-mobile #l4 .univer-app-syllabi li a { flex:1; min-width:0; overflow-wrap:anywhere; }
:root.univer-mobile #l4 .univer-app-syllabi li span { color:var(--u-muted); font-size:var(--u-fs-meta); }
:root.univer-mobile #l4 .univer-app-syllabi p { margin:0 0 10px; }
:root.univer-mobile #l4 .univer-app-syllabi button { margin-right:12px; }
/* News: pagination always describes the currently filtered results. */
:root.univer-mobile #l4 table[data-univer-news-source], :root.univer-mobile #l4 .univer-app-news-pager[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-news-results { padding:0; background:none; border:0; border-radius:0; }
:root.univer-mobile #l4 .univer-app-news-results p.univer-app-news-status { margin:0 0 12px!important; padding:8px 0; }
:root.univer-mobile table.univer-app-news tr.news[hidden] { display:none!important; }
:root.univer-mobile #l4 td.tb.univer-app-news-pager-source { display:none!important; }
:root.univer-mobile #l4 .univer-app-news-tools { display:flex; flex-wrap:wrap; align-items:center; gap:8px 10px; margin:0 0 12px!important; padding:12px; background:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); }
:root.univer-mobile #l4 input.univer-app-news-query { flex:1 1 260px; min-width:200px; height:40px; padding-left:38px!important; background:#fff ${icons.search} 12px center/18px no-repeat!important; }
:root.univer-mobile #l4 .univer-app-news-date { display:inline-flex; align-items:center; gap:6px; margin:0!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile #l4 .univer-app-news-date input { width:150px; height:40px; }
:root.univer-mobile #l4 .univer-app-news-tools button { min-height:40px!important; padding:0 14px!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; }
:root.univer-mobile #l4 .univer-app-news-tools button:hover, :root.univer-mobile #l4 .univer-app-news-tools button[aria-pressed=true] { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-ink)!important; }
:root.univer-mobile #l4 .univer-app-news-tools button.univer-app-news-all { background:var(--u-brand)!important; border-color:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile #l4 .univer-app-news-tools button[hidden] { display:none!important; }
:root.univer-mobile .univer-app-news-count { margin-left:auto; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-news-pager { display:flex; flex-wrap:wrap; align-items:center; gap:6px; margin:4px 0 16px!important; }
:root.univer-mobile .univer-app-news-pager>a { display:inline-flex; align-items:center; justify-content:center; min-width:38px; height:38px; box-sizing:border-box; padding:0 12px; border:1px solid var(--u-line); border-radius:10px; background:#fff; color:var(--u-ink); font-weight:500; text-decoration:none; }
:root.univer-mobile .univer-app-news-pager>a:hover { background:var(--u-brand-soft); border-color:var(--u-brand-line); }
:root.univer-mobile .univer-app-news-pager>a.univer-app-news-current { background:var(--u-brand); border-color:var(--u-brand); color:#fff; }
:root.univer-mobile .univer-app-news-gap { padding:0 5px; color:var(--u-muted); }
:root.univer-mobile .univer-app-news-jump { display:inline-flex; align-items:center; gap:6px; margin-left:auto; }
:root.univer-mobile .univer-app-news-jump input { width:76px; height:38px!important; min-height:38px!important; }
:root.univer-mobile #l4 .univer-app-news-jump button { min-height:38px!important; padding:0 14px!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:500!important; cursor:pointer; }
:root.univer-mobile #l4 .univer-app-news-jump button:hover { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; }
:root.univer-mobile .univer-app-news-jump-label { color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-news-results { margin:0 0 16px!important; padding:14px 16px; background:var(--u-brand-soft); border:1px solid var(--u-brand-line); border-radius:12px; }
:root.univer-mobile .univer-app-news-results[hidden] { display:none!important; }
:root.univer-mobile .univer-app-news-results h3 { margin:0 0 10px!important; font-size:15px!important; color:var(--u-ink); }
:root.univer-mobile .univer-app-news-status { margin:-4px 0 10px!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-news-hit { margin:0 0 8px; background:#fff; border:1px solid var(--u-line); border-radius:10px; }
:root.univer-mobile .univer-app-news-hit>summary { display:flex; align-items:center; gap:12px; padding:10px 14px; cursor:pointer; }
:root.univer-mobile .univer-app-news-hit>summary time { flex:0 0 auto; padding:2px 8px; border-radius:6px; background:var(--u-brand-soft); color:var(--u-ink); font-size:var(--u-fs-meta); font-weight:600; }
:root.univer-mobile .univer-app-news-hit>summary span { flex:1 1 auto; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; color:var(--u-ink); }
:root.univer-mobile .univer-app-news-hit>summary a { flex:0 0 auto; font-size:13px; font-weight:500; }
:root.univer-mobile .univer-app-news-hit-body { padding:0 14px 12px; max-height:520px; overflow:auto; }
:root.univer-mobile .univer-app-news-hit-body img { max-width:100%; height:auto; }
/* УМКД: the duplicate "view by discipline" entry reloads the same list. */
:root.univer-mobile #l4 a#discipline_btn, :root.univer-mobile #l4 .ts:has(>a#discipline_btn):not(:has(>a:not(#discipline_btn))) { display:none!important; }
/* Schedule: today. */
:root.univer-mobile .univer-app-day.univer-app-today { border-color:var(--u-brand)!important; box-shadow:0 0 0 1px var(--u-brand),var(--u-shadow); }
:root.univer-mobile .univer-app-day.univer-app-today h3 { display:flex; align-items:center; gap:8px; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-today-badge { margin-left:auto; padding:1px 8px; border-radius:6px; background:var(--u-brand); color:#fff; font-size:var(--u-fs-caption); font-weight:600; }
:root.univer-mobile #l4 .univer-app-days button.univer-app-today:not([aria-pressed=true]) { border-color:var(--u-brand)!important; color:var(--u-ink)!important; font-weight:500!important; }
:root.univer-mobile table.schedule th.univer-app-today-col { background:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile #l4 iframe.univer-app-video { display:none!important; }
:root.univer-mobile .univer-app-video-link { display:inline-flex; align-items:center; gap:8px; min-height:40px; box-sizing:border-box; padding:8px 16px; margin:8px 0; border:1px solid var(--u-line); border-radius:10px; background:#fff; box-shadow:var(--u-shadow); color:var(--u-ink); font-weight:500; text-decoration:none; }
:root.univer-mobile .univer-app-video-link::before { content:'▶'; font-size:11px; }
:root.univer-mobile #l5, :root.univer-mobile #l5 td.center, :root.univer-mobile #copyright, :root.univer-mobile #copyright>tbody>tr>td { background:transparent!important; border:0!important; }
:root.univer-mobile #l5 td.center { padding:8px 32px 32px!important; }
:root.univer-mobile #l5 td.center>#copyright { width:100%!important; margin:0 auto 12px!important; }
:root.univer-mobile #copyright td { padding:0!important; text-align:start!important; }
:root.univer-mobile #copyright td { color:var(--u-muted)!important; font-size:12px!important; }
:root.univer-mobile .univer-app-info[data-univer-info=links] .links a { color:var(--u-ink)!important; }

/* Site dialogs and the login page. */
:root.univer-mobile #simplemodal-container { border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow-pop)!important; }
:root.univer-mobile .univer-app-modal-toolbar { border-color:var(--u-line)!important; border-radius:12px 14px 0 0!important; }
:root.univer-mobile #auth_modal, :root.univer-mobile #modal_agreement, :root.univer-mobile .modal { max-width:calc(100vw - 36px)!important; max-height:calc(100vh - 36px)!important; overflow:auto!important; box-sizing:border-box!important; }
:root.univer-mobile #modal_agreement .modal-body { max-height:65vh!important; overflow:auto!important; line-height:1.6!important; }
:root.univer-mobile .modal .modal-footer { padding:14px!important; }
:root.univer-mobile #attentionSt { line-height:1.65!important; }
:root.univer-mobile #login_form { box-sizing:border-box!important; padding:28px!important; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile #login_form :is(td,p,strong,b,label,span) { text-align:start!important; }
:root.univer-mobile #login_frm :is(td,p,strong,li) { font-size:14px!important; line-height:1.6!important; }
:root.univer-mobile #login_frm :is(input[type=text],input[type=password]) { min-height:42px!important; background:#fff!important; }
:root.univer-mobile #login_frm :is(input[type=submit],button[type=submit]) { width:100%; min-height:44px!important; padding:10px 22px!important; border:0!important; border-radius:8px!important; background:var(--u-brand)!important; color:#fff!important; font-weight:500!important; }
:root.univer-mobile #login_frm :is(input[type=submit],button[type=submit]):hover { background:var(--u-brand-strong)!important; }

/* Printed documents open in their own viewer as a paper sheet. */
:root.univer-mobile.univer-app-document, :root.univer-mobile.univer-app-document body { background:var(--u-bg)!important; }
:root.univer-mobile.univer-app-document body { box-sizing:border-box; max-width:1040px!important; margin:24px auto!important; padding:40px 48px!important; background:#fff!important; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); color:#1d2b39!important; }
:root.univer-mobile.univer-app-document .noprint a { border-radius:8px; }
:root.univer-mobile.univer-app-document .univer-app-table-scroll { box-shadow:none; border-radius:8px!important; }
:root.univer-mobile.univer-app-document .univer-app-table-scroll .univer-app-table-scroll { border:0!important; margin:0!important; }
:root.univer-mobile.univer-app-document .univer-app-pairs>tbody>tr { margin:0!important; border:0!important; border-bottom:1px solid var(--u-line)!important; border-radius:0!important; box-shadow:none!important; }
:root.univer-mobile.univer-app-document .univer-app-pairs>tbody>tr>td { border:0!important; }

/* Room after the page's last block, so «Наверх» at the end of a long page sits on empty space, not on its last figures. */
@media screen { :root.univer-mobile:not(.univer-app-document) body::after { content:''; display:block; height:32px; } }

@media (min-width:900px) {
 :root.univer-mobile .univer-app-cards>thead { display:table-header-group!important; }
 :root.univer-mobile .univer-app-cards>tfoot { display:table-footer-group!important; }
 :root.univer-mobile #l4 table.univer-app-sort-collapsed .univer-app-sort-head { display:table-row!important; }
 :root.univer-mobile #l4 .univer-app-cards:not(.univer-app-split-cards) :is(td,th) { overflow-wrap:break-word!important; word-break:normal!important; }
 :root.univer-mobile #l4 .univer-app-cards .univer-app-data-field { min-width:8ch!important; }
 :root.univer-mobile .univer-app-cards .univer-app-card-row { box-shadow:none; }
 :root.univer-mobile .univer-app-cards:not(.univer-app-split-cards) td:has(>a.univer-app-action-link:only-child) { width:1%; white-space:nowrap; }
 :root.univer-mobile #l4 .univer-app-form-table>tbody>tr:has(>:nth-child(2)):not(:has(>:nth-child(5))):not(:has(>:first-child :is(input,select,textarea,img,table))):not(.univer-app-form-spacer) { display:grid!important; grid-template-columns:minmax(160px,260px) minmax(0,1fr); column-gap:20px; align-items:center; }
 :root.univer-mobile #l4 .univer-app-form-table>tbody>tr:has(>:nth-child(2)):not(:has(>:nth-child(5))):not(:has(>:first-child :is(input,select,textarea,img,table)))>:is(td,th) { width:auto!important; }
 :root.univer-mobile .univer-app-shell table.inner.univer-app-courses { table-layout:fixed!important; }
 :root.univer-mobile .univer-app-shell .univer-app-courses>tbody>tr>:is(td,th):first-child { width:36px!important; min-width:36px!important; max-width:36px!important; padding:10px 8px 10px 14px!important; }
 :root.univer-mobile .univer-app-shell .univer-app-courses>tbody>tr>:is(td,th):nth-child(2) { width:auto!important; min-width:240px!important; }
 :root.univer-mobile .univer-app-shell .univer-app-courses>tbody>tr>:is(td,th):last-child { width:250px!important; }
 :root.univer-mobile .univer-app-shell .univer-app-materials>tbody>tr.file>td:first-child, :root.univer-mobile .univer-app-shell .univer-app-materials>tbody>tr:has(>th:nth-child(2))>th:first-child { display:none!important; }
 :root.univer-mobile .univer-app-shell .univer-app-materials>tbody>tr>:is(td,th):nth-child(2) { min-width:220px!important; }
 :root.univer-mobile .univer-app-shell .univer-app-materials>tbody>tr>:is(td,th):nth-child(n+3) { min-width:9ch!important; overflow-wrap:normal!important; word-break:normal!important; }
 :root.univer-mobile .univer-app-shell .univer-app-materials>tbody>tr>:is(td,th):last-child { min-width:140px!important; }
 :root.univer-mobile .univer-app-materials .stars { gap:0; flex-wrap:nowrap; }
 :root.univer-mobile .univer-app-materials svg.star { width:16px!important; height:18px!important; padding:4px!important; }
 :root.univer-mobile .univer-app-catalog tr.file { display:grid!important; grid-template-columns:minmax(0,1fr) 110px 100px 90px; align-items:center; gap:4px 16px; }
 :root.univer-mobile .univer-app-catalog tr.file>td:nth-child(3) { grid-column:1!important; display:flex!important; align-items:center; gap:12px; justify-content:space-between; }
 :root.univer-mobile .univer-app-catalog tr.file>td[data-univer-field] { font-size:13px!important; color:var(--u-text); }
}
@media (max-width:899px) {
 :root.univer-mobile .univer-app-table-scroll:has(>:is(.univer-app-cards,.univer-app-courses,.univer-app-materials)) { background:none!important; border:0!important; box-shadow:none!important; overflow:visible; }
 :root.univer-mobile .univer-app-table-scroll :is(.univer-app-cards,.univer-app-courses,.univer-app-materials) :is(td,th) { border-bottom:0!important; padding:0!important; }
 :root.univer-mobile .univer-app-table-scroll :is(.univer-app-courses,.univer-app-materials)>tbody>tr:is(.link,.file) { box-shadow:var(--u-shadow); border-color:var(--u-line)!important; border-radius:12px!important; }
 :root.univer-mobile #l4 td.center { padding:16px 16px 32px!important; }
 :root.univer-mobile #l1 .header { padding:0 16px!important; }
}
@media (max-width:1040px) {
 :root.univer-mobile .univer-app-brand { display:none!important; }
}
@media (max-width:860px) {
 :root.univer-mobile .univer-app-header { padding:8px 0; }
 :root.univer-mobile .univer-app-tabs { order:3; flex-basis:100%; overflow-x:auto; }
 :root.univer-mobile.univer-app-unified .univer-app-menu[open] { top:calc(var(--u-header) + 52px); left:12px; right:12px; }
}
@media (max-width:700px) {
 :root.univer-mobile .univer-app-schedule[data-all-days=true] { grid-template-columns:minmax(0,1fr); }
 :root.univer-mobile .univer-app-courses .univer-app-course-open { margin-top:8px!important; width:100%!important; }
}
@media print {
 :root.univer-mobile #l1 .header { position:static; box-shadow:none; }
 :root.univer-mobile :is(.univer-app-sections,.univer-app-menu,.univer-app-actions .univer-app-account) { display:none!important; }
 :root.univer-mobile.univer-app-document body { max-width:none!important; margin:0!important; padding:0!important; border:0; box-shadow:none; }
}
:root.univer-app-still * { scroll-behavior:auto!important; animation:none!important; transition:none!important; }
/* The app-drawn list for single-choice selects. */
.univer-app-select { position:fixed; z-index:950; box-sizing:border-box; overflow:auto; max-width:calc(100vw - 16px); padding:6px; border:1px solid #c8d3df; border-radius:8px; background:#fff; box-shadow:0 12px 36px rgba(16,40,70,.12);
  font:14px/1.4 'Segoe UI Variable Text','Segoe UI',Tahoma,system-ui,sans-serif; color:#16283b; outline:2px solid var(--u-brand,#1b5fa6); outline-offset:2px; }
.univer-app-select-option { padding:8px 12px; border-radius:4px; cursor:pointer; overflow-wrap:anywhere; }
.univer-app-select-grouped { padding-left:22px; }
.univer-app-select-group { padding:8px 12px 4px; color:#5b6b7c; font-size:12px; font-weight:500; }
.univer-app-select-option.univer-app-select-active { background:#e9f1fb; }
.univer-app-select-option[aria-selected=true] { color:#1b5fa6; font-weight:500; }
.univer-app-select-option[aria-selected=true]::after { content:'✓'; float:right; margin-left:16px; }
.univer-app-select-option[aria-disabled=true] { color:#98a6b5; cursor:default; }
.univer-app-select { transform-origin:top left; }
.univer-app-select-up { transform-origin:bottom left; }
:root.univer-mobile .univer-app-menu { transform-origin:top center; }
:root.univer-mobile .univer-app-account>:not(summary) { transform-origin:top right; }


/* Loading text remains readable, without a perpetual sweep across the workspace. */
:root.univer-mobile .univer-app-pending { width:fit-content; max-width:100%; padding:6px 12px; border-radius:6px; background:var(--u-surface-2); color:var(--u-muted)!important; }
/* Pointer hover and press retarget smoothly; keyboard selection never waits for movement. */
:root.univer-mobile :is(a,button,summary,label,select,textarea,input) { transition:none; }
:root.univer-mobile summary::after { transition:none; }
:root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) { scroll-behavior:smooth; }
:root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) :is(a,button,summary,label,select,textarea,input) { transition:background-color 140ms var(--u-ease-out),color 140ms var(--u-ease-out),border-color 140ms var(--u-ease-out); }
:root.univer-mobile .univer-app-tabs>a.active { transition:background-color 140ms var(--u-ease-out); }
:root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) summary::after { transition:transform 180ms var(--u-ease-out); }
:root.univer-mobile .univer-app-account[open]>summary::after { transform:rotate(-135deg); }
@media (hover:hover) and (pointer:fine) {
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) :is(button:not(.univer-app-unpin,.univer-app-reorder),input[type=button],input[type=submit],input[type=reset],.univer-app-quick a) { transition:transform var(--u-release) var(--u-ease-out),background-color 140ms var(--u-ease-out),color 140ms var(--u-ease-out),border-color 140ms var(--u-ease-out); }
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) :is(button:not(.univer-app-unpin,.univer-app-reorder),input[type=button],input[type=submit],input[type=reset],.univer-app-quick a):active:not(:disabled) { transform:scale(.97); transition-duration:var(--u-press); }
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) .univer-app-quick a { transition:transform 180ms var(--u-ease-out),background-color 160ms var(--u-ease-out),border-color 160ms var(--u-ease-out),box-shadow 160ms var(--u-ease-out),color 160ms var(--u-ease-out); }
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) .univer-app-quick a:hover { transform:translateY(-2px); }
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) .univer-app-quick a:active { transform:translateY(1px) scale(.98); transition-duration:140ms; }
}
@media (prefers-reduced-motion:reduce) {
 :root.univer-mobile *, :root.univer-mobile *::before, :root.univer-mobile *::after { animation:none!important; scroll-behavior:auto!important; }
 :root.univer-mobile :is(a,button,summary,label,select,textarea,input) { transition:none!important; }
 :root.univer-mobile .univer-app-quick a:hover { transform:none!important; }
 :root.univer-mobile :is(button:not(.univer-app-unpin),input[type=button],input[type=submit],input[type=reset],.univer-app-quick a):active { transform:none!important; }
}
/* The stageshow kit over the site's parts: one radius scale, one family, the kit's controls. */
:root.univer-mobile { --u-ease-out:cubic-bezier(.22,1,.36,1); --u-press:180ms; --u-release:180ms; --u-shadow:0 1px 2px rgba(16,40,70,.05); --u-shadow-pop:0 0 0 1px rgba(16,40,70,.08),0 4px 16px rgba(16,40,70,.12),0 12px 32px rgba(16,40,70,.08); }
/* Titles: the same family, one weight up. */
:root.univer-mobile #l4 table.mt>tbody>tr.top>:is(.ct,.tt), :root.univer-mobile #l4 td.center>table.mt>tbody>tr:first-child>td.ct:not(.message):not(:has(.univer-app-back-link)), :root.univer-mobile #l4 :is(h1,h2), :root.univer-mobile #l4 .ct>a.univer-app-page-link:only-child, :root.univer-mobile #l4 .univer-app-breadcrumb>a.univer-app-page-link:last-of-type, :root.univer-mobile.univer-app-schedule-page #l4 .ct.univer-app-schedule-title>a:first-child, :root.univer-mobile #l4 td.ct.univer-app-back-crumb :is(a:not(.univer-app-back-link),span:not(.univer-app-back-row),b), :root.univer-mobile #l4 .univer-app-task-name, :root.univer-mobile #l4 .univer-app-course-name, :root.univer-mobile #l4 .univer-app-course-title>.univer-app-attendance-course { font-weight:600!important; font-size:18px!important; line-height:1.3!important; color:var(--u-ink)!important; text-decoration:none!important; }
/* Buttons: the kit's height, weight and press. */
:root.univer-mobile #l4 :is(input[type=button],input[type=submit],button):not(.univer-app-days button):not(.univer-app-schedule-modes button):not(.univer-app-attendance-filters button):not(.univer-app-schedule-export button) { min-height:28px; border-radius:6px!important; font-size:13px!important; font-weight:500; transition:transform 180ms var(--u-ease-out),background-color 160ms ease,color 160ms ease,box-shadow 160ms ease; }
:root.univer-mobile:not(.univer-app-keyboard) #l4 :is(input[type=button],input[type=submit],button):active { transform:scale(.97); }
/* Fields: a hairline at rest; focus lights the edge and a soft ring without moving the text. */
:root.univer-mobile #l4 :is(input[type=text],input[type=search],input[type=password],input[type=date],select,textarea) { min-height:34px; box-sizing:border-box; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background-color:#fff; font-size:13px!important; transition:box-shadow .2s ease,border-color .2s ease; }
:root.univer-mobile #l4 :is(input[type=text],input[type=search],input[type=password],input[type=date],select,textarea):focus { outline:0; border-color:var(--u-brand)!important; box-shadow:0 0 0 3px var(--u-brand-soft); }
:root.univer-mobile #l4 input[type=checkbox], :root.univer-mobile #l4 input[type=radio] { accent-color:var(--u-brand); }
/* Tabs: words and a bar under the chosen one. */
:root.univer-mobile .univer-app-tabs>a { position:relative; height:34px; padding:0 2px!important; border-radius:0!important; background:none!important; color:var(--u-muted)!important; transition:color .2s ease,box-shadow .2s ease; }
:root.univer-mobile .univer-app-tabs>a:hover { background:none!important; color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-tabs>a.active, :root.univer-mobile .univer-app-tabs>a.active:hover { background:none!important; color:var(--u-brand)!important; box-shadow:inset 0 -2px 0 var(--u-brand); }
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon { height:34px!important; padding:0 2px!important; border-radius:0!important; font-size:13px!important; font-weight:500!important; }
:root.univer-mobile .univer-app-tabs { gap:18px!important; }
/* Segmented: a quiet track and a raised thumb that slides to the choice (Assets/kit.js places it). */
:root.univer-mobile .univer-app-schedule-modes, :root.univer-mobile #l4 .univer-app-attendance-filters { position:relative; gap:0; padding:2px; border:0!important; border-radius:8px; background:#e9eef4; }
:root.univer-mobile #l4 .univer-app-schedule-modes button, :root.univer-mobile #l4 .univer-app-attendance-filters button { position:relative; z-index:1; min-height:36px!important; padding:4px 12px!important; border:0!important; border-radius:6px!important; font-size:13px!important; font-weight:500; transition:color .2s ease; }
:root.univer-mobile #l4 .univer-app-attendance-filter-caption { position:relative; z-index:1; }
:root.univer-mobile .univer-kit-thumb { position:absolute; left:0; top:0; border-radius:6px; background:#fff; box-shadow:0 1px 2px rgba(16,40,70,.1),0 0 0 .5px rgba(16,40,70,.08); transition:transform .3s var(--u-ease-out),width .3s var(--u-ease-out),height .3s var(--u-ease-out); pointer-events:none; }
:root.univer-mobile #l4 .univer-kit-slid button[aria-pressed=true] { background:transparent!important; box-shadow:none!important; color:var(--u-brand)!important; }
/* IGPA's period switch (За учебный год | Накопительный): the site joins two links into one bordered box and marks the
   chosen one with a faint tint, so they read as one button. The same track and raised thumb as the segmented above, on
   screen only, so the site's print rules still hide its screen-only parts. */
@media screen {
:root:root:root.univer-mobile #l4 .igpa-period:not([hidden]) { display:inline-flex!important; gap:2px!important; padding:2px!important; border:0!important; border-radius:8px!important; background:#e9eef4!important; overflow:visible!important; }
:root:root:root.univer-mobile #l4 .igpa-period>a:not([hidden]) { display:inline-flex!important; align-items:center!important; min-height:32px!important; height:auto!important; margin:0!important; padding:0 14px!important; border:0!important; border-radius:6px!important; background:transparent!important; box-shadow:none!important; color:var(--u-muted)!important; font-weight:500!important; text-decoration:none!important; }
:root:root:root.univer-mobile #l4 .igpa-period>a:is(.igpa-selected,[aria-current]) { background:#fff!important; color:var(--u-brand)!important; font-weight:600!important; box-shadow:0 1px 2px rgba(16,40,70,.1),0 0 0 .5px rgba(16,40,70,.08)!important; }
}
@media (hover:hover) { :root:root:root.univer-mobile #l4 .igpa-period>a:not(.igpa-selected,[aria-current]):hover { color:var(--u-ink)!important; } }
/* IGPA's form buttons («Отправить на проверку», «Очистить») and its request filter (two lists and «Показать») stand apart,
   the rows (period switch, buttons, filter) do not touch, and a filter label keeps off its list. */
@media screen {
:root:root:root.univer-mobile #l4 .igpa-ui :is(.igpa-form-actions,.igpa-filter):not([hidden]) { display:flex!important; flex-wrap:wrap!important; align-items:flex-end!important; gap:8px 12px!important; }
:root:root:root.univer-mobile #l4 .igpa-ui :is(.igpa-form-actions,.igpa-filter)>* { margin:0!important; }
:root:root:root.univer-mobile #l4 .igpa-ui :is(.igpa-period,.igpa-form-actions,.igpa-filter):not([hidden]) { margin-bottom:12px!important; }
:root:root:root.univer-mobile #l4 .igpa-ui .igpa-filter-field>select { margin-inline-start:8px!important; }
}
/* Filter chips: a pill whose state fades in, its count a badge. */
:root.univer-mobile #l4 .univer-app-days button { min-height:28px!important; padding:3px 11px!important; border-radius:999px!important; font-size:13px!important; font-weight:500; transition:background-color .15s ease,color .15s ease,border-color .15s ease; }
/* Badges: pills, 20 px, the small label size. */
:root.univer-mobile :is(.univer-app-lesson-badge,.univer-app-overview-state,.univer-app-task-status) { display:inline-flex; align-items:center; min-height:20px; box-sizing:border-box; padding:0 9px!important; border-radius:999px!important; font-size:11px!important; font-weight:500!important; line-height:1.3; }
/* Popups: the raised surface and the float shadow; rows inset and rounded. */
:root.univer-mobile details.univer-app-menu>:not(summary), :root.univer-mobile details.univer-app-account>:not(summary) { border-radius:10px!important; }
/* One control size on every page: buttons, links that act as buttons, chips, tabs and fields share a height and type. */
:root.univer-mobile { --u-control:40px; --u-control-font:14px; --u-header:56px; }
:root:root:root.univer-mobile #l4 :is(input[type=button],input[type=submit],input[type=reset],button):not(.univer-app-add-tile,.univer-app-news-reset,.univer-app-news-all,.univer-app-news-preset,.univer-app-attendance-details-toggle,.univer-app-pin,.univer-app-attendance-overview-name,.univer-app-sort-toggle,.univer-app-menu-close),
:root:root:root.univer-mobile #l4 :is(.ts>a:not(.univer-app-service-card),a.univer-app-page-link:not(.ct>a:only-child,.univer-app-breadcrumb>a),.univer-app-semester-nav a,.univer-app-nav-table a,a.univer-app-action-link,.univer-app-materials a.downLoad,a.univer-app-back-link,.univer-app-news-pager>a,.univer-app-video-link,.univer-app-credential-button,.univer-app-overview-more) { min-height:var(--u-control)!important; box-sizing:border-box!important; padding-top:4px!important; padding-bottom:4px!important; font-size:var(--u-control-font)!important; line-height:1.25!important; }
:root:root:root.univer-mobile #l4 :is(input[type=text],input[type=search],input[type=password],input[type=number],input[type=date],input[type=email],input[type=tel],input:not([type]),select:not([multiple])) { height:var(--u-control)!important; min-height:var(--u-control)!important; box-sizing:border-box!important; font-size:var(--u-control-font)!important; }
:root:root:root.univer-mobile #l4 .univer-app-days button { padding-left:16px!important; padding-right:16px!important; }
:root:root:root.univer-mobile #l4 :is(.univer-app-schedule-modes,.univer-app-attendance-filters) button { padding-left:16px!important; padding-right:16px!important; font-weight:600; }
:root.univer-mobile .univer-app-news-pager>a { min-width:var(--u-control)!important; height:var(--u-control)!important; }
/* The header's controls: one round family, each a clear button at rest and lit on hover. */
:root:root:root.univer-mobile .univer-app-tabs { flex:0 0 auto; gap:4px!important; padding:4px; border-radius:14px; background:var(--u-surface-2); box-shadow:inset 0 0 0 1px var(--u-line); }
:root:root:root.univer-mobile #l1 .header .univer-app-tabs>a { box-sizing:border-box; height:36px!important; padding:0 20px!important; border-radius:10px!important; background:transparent!important; color:var(--u-ink)!important; box-shadow:none!important; font-size:15px!important; font-weight:600!important; }
:root:root:root.univer-mobile #l1 .header .univer-app-tabs>a:hover { background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
:root:root:root.univer-mobile #l1 .header .univer-app-tabs>a.active, :root:root:root.univer-mobile #l1 .header .univer-app-tabs>a.active:hover { background:var(--u-brand)!important; color:#fff!important; box-shadow:0 1px 3px rgba(16,40,70,.28)!important; }
:root:root:root.univer-mobile #l1 .header .univer-app-tabs>a:focus-visible { outline:2px solid var(--u-brand); outline-offset:2px; }
:root:root:root.univer-mobile #l1 .univer-app-sections, :root:root:root.univer-mobile .univer-app-actions .univer-app-account>summary { height:var(--u-control)!important; border-radius:12px!important; font-size:var(--u-control-font)!important; }
:root:root:root.univer-mobile .univer-app-actions .links>a { height:var(--u-control)!important; border-radius:12px!important; }
:root:root:root.univer-mobile #l1 .univer-app-flags { box-sizing:border-box; height:var(--u-control); padding:3px; border-radius:12px; }
:root:root:root.univer-mobile #l1 .univer-app-flags>:is(a,img) { width:38px!important; height:32px!important; border-radius:9px!important; }
/* Home tiles: an outline at rest and a clear highlight on hover. */
:root:root.univer-mobile .univer-app-quick { grid-template-columns:repeat(auto-fill,minmax(220px,1fr)); gap:12px; }
:root:root.univer-mobile .univer-app-quick .univer-app-tile>a, :root:root.univer-mobile .univer-app-quick .univer-app-tile.univer-app-pinned>a { min-height:60px; padding:10px 14px!important; border:1px solid var(--u-line-strong)!important; border-radius:12px!important; background:var(--u-surface)!important; overflow-wrap:break-word; word-break:normal; }
:root:root.univer-mobile .univer-app-quick .univer-app-tile>a:hover, :root:root.univer-mobile .univer-app-quick .univer-app-tile>a:focus-visible { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; box-shadow:inset 0 0 0 1px var(--u-brand),0 4px 12px rgba(16,40,70,.1); }
/* Grades read the same on the home screen and on the journal page: one row, one chip, one bar. */
:root:root.univer-mobile .univer-app-overview { overflow:clip; }
:root:root.univer-mobile .univer-app-overview-head { min-height:52px; box-sizing:border-box; padding:14px 20px; }
:root:root.univer-mobile .univer-app-overview-body { padding:6px 20px 12px; }
:root:root.univer-mobile .univer-app-overview-courses>li, :root:root.univer-mobile #l4 ul.univer-app-attendance-overview-list>li { display:flex; flex-wrap:nowrap; align-items:center; justify-content:space-between; gap:12px; padding:6px 0!important; }
/* The site's list links are tall tap targets; a course name in a row is plain text with a link's colour. */
:root:root:root.univer-mobile #l4 .univer-app-overview-courses>li>a.univer-app-overview-course-link { display:block!important; min-height:0!important; padding:0!important; line-height:1.4!important; }
:root:root:root.univer-mobile #l4 .univer-app-overview-work-list .univer-app-overview-lesson-main>a.univer-app-overview-course-link { display:block!important; min-height:0!important; margin:0!important; padding:0!important; line-height:1.4!important; }
:root:root.univer-mobile .univer-app-overview-work-list>li { padding:12px 0!important; }
:root:root.univer-mobile .univer-app-overview-scores { flex-wrap:nowrap; gap:6px; }
:root:root.univer-mobile .univer-app-overview-score, :root:root.univer-mobile .univer-app-overview-courses .univer-app-overview-score { box-sizing:border-box; min-width:68px; padding:3px 8px 6px; background:var(--u-surface); }
/* Score bands out of 100 (page-common.js band): below 50 a shortfall, 50–69 satisfactory, 70–89 good, 90+ excellent. Soft
   tints with a darker ink of the same hue, so each reads at a glance and keeps 4.5:1; the line under the tile is the whole
   scale and its fill the score. The words beside the colour ("ещё 7 до 70") carry the same meaning without it. */
:root.univer-mobile { --univer-band-low:#e58a80; --univer-band-low-ink:#a8473d; --univer-band-low-soft:#fdf0ef; --univer-band-low-line:#f3cdc8;
  --univer-band-fair:#e5b862; --univer-band-fair-ink:#7d5a17; --univer-band-fair-soft:#fdf5e4; --univer-band-fair-line:#eed9a8;
  --univer-band-good:#6cb38c; --univer-band-good-ink:#2f6b4b; --univer-band-good-soft:#ebf7f0; --univer-band-good-line:#c3e3d0;
  --univer-band-top:#7b9fdc; --univer-band-top-ink:#365e9f; --univer-band-top-soft:#eaf2fd; --univer-band-top-line:#c6d8f3; }
:root.univer-mobile [data-univer-band=low] { --univer-band:var(--univer-band-low); --univer-band-ink:var(--univer-band-low-ink); --univer-band-soft:var(--univer-band-low-soft); --univer-band-line:var(--univer-band-low-line); }
:root.univer-mobile [data-univer-band=fair] { --univer-band:var(--univer-band-fair); --univer-band-ink:var(--univer-band-fair-ink); --univer-band-soft:var(--univer-band-fair-soft); --univer-band-line:var(--univer-band-fair-line); }
:root.univer-mobile [data-univer-band=good] { --univer-band:var(--univer-band-good); --univer-band-ink:var(--univer-band-good-ink); --univer-band-soft:var(--univer-band-good-soft); --univer-band-line:var(--univer-band-good-line); }
:root.univer-mobile [data-univer-band=top] { --univer-band:var(--univer-band-top); --univer-band-ink:var(--univer-band-top-ink); --univer-band-soft:var(--univer-band-top-soft); --univer-band-line:var(--univer-band-top-line); }
:root:root:root.univer-mobile .univer-app-overview-score[data-univer-band] { flex-wrap:wrap; row-gap:0; border-color:var(--univer-band-line); background:var(--univer-band-soft); transition:background-color .2s ease,border-color .2s ease; }
:root:root:root.univer-mobile .univer-app-overview-score[data-univer-band]::before { content:''; position:absolute; left:0; right:0; bottom:0; height:3px; background:var(--univer-band-line); }
:root:root:root.univer-mobile .univer-app-overview-score[data-univer-band] .univer-app-overview-score-value { color:var(--univer-band-ink); }
:root:root:root.univer-mobile .univer-app-overview-score[data-univer-band] .univer-app-overview-score-bar { background:var(--univer-band); }
:root:root:root.univer-mobile .univer-app-overview-score-next { flex:0 0 100%; color:var(--univer-band-ink); font-size:12px; font-weight:500; line-height:1.35; white-space:nowrap; font-variant-numeric:tabular-nums; }
:root:root.univer-mobile #l4 .univer-app-attendance-overview { padding:0; overflow:clip; }
:root:root.univer-mobile #l4 p.univer-app-attendance-overview-title { display:flex; align-items:center; min-height:52px; box-sizing:border-box; margin:0!important; padding:14px 20px; border-bottom:1px solid var(--u-line); background:var(--u-surface-2); color:var(--u-ink); font-size:16px!important; font-weight:600; }
:root:root.univer-mobile #l4 ul.univer-app-attendance-overview-list { padding:6px 20px 12px!important; }
:root:root.univer-mobile #l4 ul.univer-app-attendance-overview-list>li:first-child { border-top:0; }
:root:root.univer-mobile #l4 button.univer-app-attendance-overview-name { flex:1 1 160px; min-height:0!important; padding:0!important; color:var(--u-brand)!important; font:500 14px/1.4 var(--u-font)!important; }
:root:root.univer-mobile #l4 .univer-app-attendance-overview-empty { flex:0 0 auto; }
/* What the filters choose is said in plain words, in the same voice as the courses above them. */
:root:root.univer-mobile #l4 :is(p.univer-app-attendance-course-caption,p.univer-app-attendance-filter-caption) { flex-basis:auto; margin:0 0 8px!important; padding:0; color:var(--u-ink); font-size:14px!important; font-weight:600; }
/* The journal of a course: as wide as its marks, with lines you can see and the total standing out. */
:root:root.univer-mobile #l4 .univer-app-table-scroll:has(>table.univer-app-matrix) { width:fit-content; max-width:100%; }
:root:root.univer-mobile #l4 .univer-app-table-scroll>table.univer-app-matrix { width:auto!important; min-width:0!important; }
:root:root.univer-mobile #l4 .univer-app-table-scroll .univer-app-matrix :is(td,th) { min-width:48px!important; padding:6px 10px!important; border-right:1px solid var(--u-line-strong)!important; border-bottom:1px solid var(--u-line-strong)!important; line-height:1.3; }
:root:root.univer-mobile #l4 .univer-app-table-scroll .univer-app-matrix tr>:first-child { min-width:76px!important; }
:root:root.univer-mobile #l4 .univer-app-table-scroll .univer-app-matrix tr:last-child>:is(td,th) { border-bottom:0!important; }
:root:root.univer-mobile #l4 .univer-app-table-scroll .univer-app-matrix tr>:last-child { border-right:0!important; }
:root:root.univer-mobile #l4 .univer-app-table-scroll .univer-app-matrix tr>.univer-app-sum { min-width:64px!important; border-left:1px solid var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-weight:700!important; }
:root:root.univer-mobile #l4 .univer-app-table-scroll .univer-app-matrix td.univer-app-sum { font-size:16px!important; }
/* A lesson reads alike on the home screen and in the schedule: the time first, then what, where and who. */
:root:root:root.univer-mobile :is(.univer-app-overview-lesson,.univer-app-lesson) time { display:flex!important; flex-direction:column; align-items:flex-start; flex:0 0 56px; box-sizing:border-box; min-width:0; margin:0; padding:0!important; border-radius:0!important; background:none!important; color:var(--u-ink)!important; line-height:1.2; font-variant-numeric:tabular-nums; }
:root:root:root.univer-mobile :is(.univer-app-overview-lesson,.univer-app-lesson) time>b { color:var(--u-ink); font-size:17px; font-weight:700; }
:root:root:root.univer-mobile :is(.univer-app-overview-lesson,.univer-app-lesson) time>span { margin-top:2px; color:var(--u-muted); font-size:13px; font-weight:400; }
:root:root:root:root.univer-mobile a.univer-app-lesson-name { display:inline!important; min-height:0!important; height:auto!important; margin:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:15px!important; font-weight:600!important; line-height:1.35!important; text-decoration:none!important; }
:root:root:root:root.univer-mobile :is(.univer-app-overview-lesson,.univer-app-lesson):hover a.univer-app-lesson-name, :root:root:root:root.univer-mobile a.univer-app-lesson-name:focus-visible { color:var(--u-brand)!important; }
:root:root:root.univer-mobile #l4 .univer-app-lesson-facts { display:flex!important; flex-wrap:wrap; align-items:center; gap:0 10px; margin:3px 0 0!important; color:var(--u-text)!important; font-size:13px!important; line-height:1.4!important; }
:root:root:root.univer-mobile .univer-app-lesson-room { color:var(--u-ink)!important; font-weight:600; }
:root:root:root.univer-mobile .univer-app-lesson-type { color:#b45309; font-weight:600; }
:root:root:root.univer-mobile .univer-app-lesson-type[data-kind=lecture] { color:var(--u-brand); }
:root:root:root.univer-mobile .univer-app-lesson-type[data-kind=seminar] { color:var(--u-teal); }
:root:root:root.univer-mobile #l4 .univer-app-lesson-teacher { display:block; margin:1px 0 0!important; color:var(--u-muted)!important; font-size:13px!important; line-height:1.4!important; }
:root:root.univer-mobile .univer-app-overview li.univer-app-overview-lesson { gap:12px; padding:12px 0!important; cursor:pointer; }
:root:root.univer-mobile .univer-app-overview li.univer-app-overview-lesson.univer-app-overview-now { margin:0 -12px!important; padding:12px!important; border-radius:10px; }
:root:root.univer-mobile .univer-app-overview-lesson-main { display:flex; flex-direction:column; align-items:flex-start; }
:root:root:root.univer-mobile #l4 .univer-app-lesson { display:grid!important; grid-template-columns:56px minmax(0,1fr); column-gap:12px; align-items:start; padding:12px 14px!important; cursor:pointer; transition:background-color .14s ease; }
:root:root:root.univer-mobile #l4 .univer-app-lesson:hover { background:var(--u-surface-2)!important; }
:root:root:root.univer-mobile #l4 .univer-app-lesson.univer-app-lesson-now:hover { background:var(--u-brand-soft)!important; }
:root:root:root.univer-mobile #l4 .univer-app-lesson-main { min-width:0; }
:root:root:root.univer-mobile #l4 .univer-app-lesson p.univer-app-subject { display:flex; flex-wrap:wrap; align-items:center; gap:2px 8px; margin:0!important; }
:root:root:root.univer-mobile #l4 .univer-app-lesson-badge { margin-left:0; }
:root:root:root.univer-mobile #l4 .univer-app-lesson a.univer-app-page-link { margin-top:6px!important; }
:root:root:root.univer-mobile #l4 .univer-app-lesson.univer-app-lesson-done>:not(.univer-app-lesson-badge) { opacity:.6; }
/* A link to another semester names that semester; the site's long sentence stays its accessible name. */
:root:root:root:root.univer-mobile #l4 td.ts>a[data-univer-short] { font-size:0!important; gap:8px; }
:root:root:root:root.univer-mobile #l4 td.ts>a[data-univer-short]:has(>img[src*="tb_left"])::after, :root:root:root:root.univer-mobile #l4 td.ts>a[data-univer-short]:has(>img[src*="tb_right"])::before { content:attr(data-univer-short); font-size:14px; }
:root:root:root:root.univer-mobile #l4 td.ts>a[data-univer-short]:has(>img[src*="tb_left"])::before, :root:root:root:root.univer-mobile #l4 td.ts>a[data-univer-short]:has(>img[src*="tb_right"])::after { font-size:14px; }
/* УМКД: every course a card; the whole card opens it. */
:root:root:root:root.univer-mobile #l4 .univer-app-table-scroll:has(>.univer-app-courses) { overflow:visible!important; border:0!important; border-radius:0!important; background:none!important; box-shadow:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses { display:block!important; width:100%!important; min-width:0!important; border:0!important; background:none!important; box-shadow:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses>tbody { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(300px,1fr)); gap:10px; width:100%!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses>tbody>tr:has(>th) { display:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link { position:relative; display:grid!important; grid-template-columns:40px minmax(0,1fr) 16px; grid-template-rows:auto auto; column-gap:12px; align-items:center; min-height:68px; box-sizing:border-box; margin:0!important; padding:10px 14px!important; border:1px solid var(--u-line-strong)!important; border-radius:12px!important; background:var(--u-surface)!important; box-shadow:none!important; transition:background-color .16s ease,border-color .16s ease; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link:hover { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link[hidden] { display:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link::after { content:''; grid-row:1/3; grid-column:3; width:8px; height:8px; margin-left:2px; border-top:2px solid var(--u-muted); border-right:2px solid var(--u-muted); transform:rotate(45deg); }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link>td { display:block!important; width:auto!important; min-width:0!important; margin:0!important; padding:0!important; border:0!important; background:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link>td::before { display:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link>td:first-child { grid-row:1/3; grid-column:1; display:grid!important; place-items:center; width:40px!important; height:40px; border-radius:10px; background:var(--u-brand-soft)!important; color:var(--u-brand); }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link>td:first-child::after { content:''; width:20px; height:20px; background:currentColor; -webkit-mask:var(--univer-book-icon) center/contain no-repeat; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link>td:first-child img { display:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link>td:nth-child(2) { grid-column:2; align-self:end; color:var(--u-ink)!important; font-size:15px!important; font-weight:600!important; line-height:1.3!important; overflow-wrap:break-word; }
:root:root:root:root.univer-mobile #l4 table.univer-app-courses tr.link>td:nth-child(3) { grid-column:2; align-self:start; color:var(--u-muted)!important; font-size:13px!important; }
:root:root:root:root.univer-mobile #l4 button.univer-app-course-open { position:absolute!important; inset:0; width:100%!important; height:100%!important; min-height:0!important; margin:0!important; padding:0!important; border:0!important; border-radius:12px!important; background:transparent!important; box-shadow:none!important; color:transparent!important; font-size:0!important; cursor:pointer; }
:root:root:root:root.univer-mobile #l4 button.univer-app-course-open:focus-visible { outline:2px solid var(--u-brand); outline-offset:1px; }
/* Inside a course: one sheet of files, grouped by the site's sections. A row is the file; size and date line up on the right. */
:root:root:root:root.univer-mobile #l4 table.univer-app-study-panel tr.brk>td.ct { padding:14px 0 6px!important; color:var(--u-ink)!important; font-size:15px!important; font-weight:600!important; }
:root:root:root:root.univer-mobile #l4 div.inner>div:has(>h4) { display:flex; flex-wrap:wrap; align-items:center; gap:8px; margin:0 0 12px; }
:root:root:root:root.univer-mobile #l4 div.inner>div:has(>h4)>h4 { flex:0 0 auto; margin:0 4px 0 0!important; color:var(--u-muted)!important; font-size:13px!important; font-weight:600!important; }
:root:root:root:root.univer-mobile #l4 div.inner>div:has(>h4)>a.univer-app-page-link { height:34px!important; min-height:34px!important; padding:0 12px!important; border-radius:10px!important; font-size:13px!important; }
:root:root:root:root.univer-mobile #l4 .univer-app-table-scroll:has(>.univer-app-materials) { overflow:hidden!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:var(--u-surface)!important; box-shadow:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials, :root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody { display:block!important; width:100%!important; min-width:0!important; border:0!important; background:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr:has(>th:nth-child(2)) { display:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr:has(>th:only-of-type) { display:block!important; border:0!important; background:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr>th:only-of-type { display:block!important; box-sizing:border-box; padding:10px 16px 8px!important; border:0!important; border-top:1px solid var(--u-line)!important; background:var(--u-surface-2)!important; color:var(--u-text)!important; font-size:13px!important; font-weight:600!important; line-height:1.4!important; text-align:start!important; white-space:normal!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials>tbody>tr:nth-child(2)>th:only-of-type { border-top:0!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file { position:relative; display:grid!important; grid-template-columns:34px minmax(0,1fr) 112px 70px 86px 80px; column-gap:12px; align-items:center; min-height:52px; box-sizing:border-box; margin:0!important; padding:8px 16px!important; border:0!important; border-top:1px solid var(--u-line)!important; border-radius:0!important; background:none!important; box-shadow:none!important; transition:background-color .14s ease; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:hover { background:var(--u-brand-soft)!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td { display:block!important; width:auto!important; min-width:0!important; margin:0!important; padding:0!important; border:0!important; background:none!important; color:var(--u-muted)!important; font-size:13px!important; line-height:1.35!important; text-align:end!important; white-space:nowrap; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td::before { display:none!important; content:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td[data-univer-blank] { visibility:hidden; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:first-child { grid-column:1; grid-row:1/3; display:grid!important; place-items:center; width:34px!important; height:34px; border-radius:8px; background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:10px!important; font-weight:700!important; text-align:center!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:first-child::after { content:attr(data-univer-ext); }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:first-child img { display:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td[data-univer-kind=pdf] { background:#fdeceb!important; color:#b42318!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td[data-univer-kind=word] { background:#e9f1fb!important; color:#1b5fa6!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td[data-univer-kind=excel] { background:#e3f3ef!important; color:#0f6b63!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td[data-univer-kind=slides] { background:#fdeee3!important; color:#a3440e!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(2) { grid-column:2; grid-row:1/3; align-self:center; text-align:start!important; white-space:normal; overflow-wrap:break-word; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials a.downLoad { display:inline!important; height:auto!important; min-height:0!important; margin:0!important; padding:0!important; border:0!important; border-radius:0!important; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:600!important; line-height:1.35!important; text-decoration:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials a.downLoad::before { content:''; position:absolute; inset:0; width:auto; height:auto; background:none; -webkit-mask:none; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:hover a.downLoad { color:var(--u-brand)!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials a.downLoad:focus-visible { outline:none; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:has(a.downLoad:focus-visible) { box-shadow:inset 0 0 0 2px var(--u-brand)!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(3):empty { display:none!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:has(>td:nth-child(3):not(:empty))>td:nth-child(2) { grid-row:1; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(3):not(:empty) { grid-column:2; grid-row:2; text-align:start!important; white-space:normal; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(4) { grid-column:4; grid-row:1/3; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(5) { grid-column:5; grid-row:1/3; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(6) { grid-column:6; grid-row:1/3; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:last-child { position:relative; z-index:1; grid-column:3; grid-row:1/3; display:flex!important; justify-content:flex-end; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars { display:flex; flex-wrap:nowrap; gap:0; height:auto!important; opacity:.0; transition:opacity .14s ease; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars[data-stars]:not([data-stars="0"]), :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:is(:hover,:focus-within) .stars { opacity:1; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials svg.star { box-sizing:content-box; width:16px!important; height:16px!important; padding:3px; color:var(--u-line-strong); overflow:visible; transition:color .12s ease, transform .12s ease; }
/* The site's stars have no viewBox, so a 16 px box used to cut their tips off: the shape is scaled into the box and its
   corners rounded. The site's own rating states (data-stars, hover preview) keep working; only the colours are ours. */
:root:root:root:root.univer-mobile #l4 table.univer-app-materials svg.star polygon { fill:currentColor!important; stroke:currentColor!important; stroke-width:2.4px; stroke-linejoin:round; transform:translate(1.2px,.1px) scale(.69); }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars[data-stars]:not([data-stars="0"]) svg.star { color:#f5a524; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars:not(:hover)[data-stars="1"] svg.star:nth-child(1)~svg.star, :root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars:not(:hover)[data-stars="2"] svg.star:nth-child(2)~svg.star,
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars:not(:hover)[data-stars="3"] svg.star:nth-child(3)~svg.star, :root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars:not(:hover)[data-stars="4"] svg.star:nth-child(4)~svg.star { color:var(--u-line-strong); }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars:hover svg.star { color:#fbbf24; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars:hover svg.star:hover~svg.star { color:var(--u-line-strong); }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials .stars svg.star:hover { transform:scale(1.15); }
/* Every file row ends in a download button, so a click visibly means "save this file"; a teacher's link opens instead. */
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file { grid-template-columns:34px minmax(0,1fr) 112px 70px 86px 80px 32px; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file::after { content:''; grid-column:7; grid-row:1/3; align-self:center; justify-self:end; width:32px; height:32px; box-sizing:border-box; border:1px solid var(--u-brand-line); border-radius:8px; background:var(--u-brand-soft) ${icons.saveBrand} center/16px no-repeat; pointer-events:none; transition:background-color .14s ease, border-color .14s ease; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:hover::after { border-color:var(--u-brand); background-color:var(--u-brand); background-image:${icons.saveWhite}; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:has(>td[data-univer-kind=link])::after { background-image:${icons.openBrand}; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:has(>td[data-univer-kind=link]):hover::after { background-image:${icons.openWhite}; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td[data-univer-kind=link] { background:#e3f3ef!important; color:#0f6b63!important; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td[data-univer-kind=link]::after { content:''; width:16px; height:16px; background:currentColor; -webkit-mask:${icons.link} center/contain no-repeat; }
:root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file:has(>td[data-univer-kind=link])>td:nth-child(5) { visibility:hidden; }
@media (max-width:860px) {
 :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file { grid-template-columns:34px minmax(0,1fr) 80px; }
 :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(4), :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(6) { display:none!important; }
 :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(5) { grid-column:3; }
 :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:nth-child(2) { grid-row:1; }
 :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file>td:last-child { grid-column:2/4; grid-row:3; justify-content:flex-start; }
 :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file { grid-template-columns:34px minmax(0,1fr) 80px 32px; }
 :root:root:root:root.univer-mobile #l4 table.univer-app-materials tr.file::after { grid-column:4; }
}
/* A course on the grades page: its name, one small table of numbers, and the class log folded under it. */
:root:root:root.univer-mobile #l4 .univer-app-course-heading { margin:6px 0 8px; padding-top:14px; }
:root:root:root.univer-mobile #l4 .univer-app-course-name, :root:root:root.univer-mobile #l4 .univer-app-course-title>.univer-app-attendance-course { font-size:18px!important; font-weight:600!important; text-decoration:none; }
:root:root:root.univer-mobile #l4 .univer-app-course-meta { display:flex; flex-wrap:wrap; gap:0 12px; margin-top:2px; color:var(--u-muted); font-size:13px; }
:root:root:root:root.univer-mobile #l4 button.univer-app-course-syllabus { height:36px!important; min-height:36px!important; padding:0 12px!important; border-radius:10px!important; font-size:13px!important; }
:root:root:root.univer-mobile #l4 .univer-app-attendance-summary { display:block; width:100%; max-width:560px; box-sizing:border-box; margin:0 0 6px!important; padding:0; overflow:hidden; border:1px solid var(--u-line); border-radius:12px; background:var(--u-surface); box-shadow:none; }
:root:root:root.univer-mobile #l4 .univer-app-score-table>div { display:grid; grid-template-columns:minmax(120px,1fr) repeat(var(--univer-columns,2),92px); align-items:center; min-height:38px; box-sizing:border-box; padding:4px 16px; border:0; border-top:1px solid var(--u-line); }
:root:root:root.univer-mobile #l4 .univer-app-score-table>div>:not(:first-child) { text-align:end; }
:root:root:root.univer-mobile #l4 .univer-app-score-head { min-height:34px!important; border-top:0!important; background:var(--u-surface-2); color:var(--u-muted); font-size:12px; font-weight:600; }
:root:root:root.univer-mobile #l4 .univer-app-score-head small { margin-left:5px; color:var(--u-muted); font-size:12px; font-weight:400; }
:root:root:root.univer-mobile #l4 .univer-app-score-table .univer-app-type-row { gap:0; flex-wrap:nowrap; }
:root:root:root.univer-mobile #l4 .univer-app-type-label { min-width:0; color:var(--u-ink); font-size:14px; font-weight:500; }
:root:root:root.univer-mobile #l4 .univer-app-score-table .univer-app-score { display:block; position:static; min-width:0; padding:0; border:0; border-radius:0; background:none; box-shadow:none; color:var(--u-ink); font-size:15px; font-weight:500; font-variant-numeric:tabular-nums; }
:root:root:root.univer-mobile #l4 .univer-app-score-total { min-height:46px!important; }
:root:root:root.univer-mobile #l4 .univer-app-score-total>:first-child { color:var(--u-ink); font-size:14px; font-weight:700; }
:root:root:root.univer-mobile #l4 .univer-app-score-total .univer-app-score-value { display:block; color:var(--u-ink); font-size:17px; font-weight:700; line-height:1.2; }
:root:root:root.univer-mobile #l4 .univer-app-score-total .univer-app-score-bar { position:relative; display:block; left:auto; right:auto; bottom:auto; height:3px; margin:3px 0 0 auto; width:56px; border-radius:2px; overflow:hidden; background:var(--u-brand-line); }
:root:root:root.univer-mobile #l4 .univer-app-score-total .univer-app-score-bar::after { border-radius:2px; }
/* A course total out of 100: its band's ink and fill, gaps in the line at 50, 70 and 90, and what is left to the next mark. */
:root:root:root.univer-mobile #l4 .univer-app-score-total [data-univer-band] .univer-app-score-value { color:var(--univer-band-ink); }
:root:root:root.univer-mobile #l4 .univer-app-score-total [data-univer-band] .univer-app-score-bar { width:72px; height:4px; background:var(--univer-band-line); }
:root:root:root.univer-mobile #l4 .univer-app-score-total [data-univer-band] .univer-app-score-bar::after { background:var(--univer-band); }
:root:root:root.univer-mobile #l4 .univer-app-score-total [data-univer-band] .univer-app-score-bar::before { content:''; position:absolute; inset:0; z-index:1; background:linear-gradient(90deg,transparent calc(50% - 1px),var(--u-surface) calc(50% - 1px) 50%,transparent 50% calc(70% - 1px),var(--u-surface) calc(70% - 1px) 70%,transparent 70% calc(90% - 1px),var(--u-surface) calc(90% - 1px) 90%,transparent 90%); }
/* Kazakh and English say what is left in longer words than the column is wide: the note wraps inside its cell. */
:root:root:root.univer-mobile #l4 .univer-app-score-next { display:block; margin-top:3px; color:var(--univer-band-ink); font-size:12px; font-weight:500; line-height:1.3; white-space:normal; overflow-wrap:break-word; text-align:right; }
:root:root:root.univer-mobile #l4 p.univer-app-attendance-nothing, :root:root:root.univer-mobile #l4 .univer-app-attendance-summary>.univer-app-type-row, :root:root:root.univer-mobile #l4 p.univer-app-attendance-overall-value { margin:0!important; padding:10px 16px!important; color:var(--u-muted); font-size:14px!important; }
:root:root:root:root.univer-mobile #l4 button.univer-app-attendance-details-toggle { display:flex!important; align-items:center; width:100%; height:38px!important; min-height:38px!important; margin:0!important; padding:0 16px!important; border:0!important; border-top:1px solid var(--u-line)!important; border-radius:0!important; background:transparent!important; box-shadow:none!important; color:var(--u-brand)!important; font-size:13px!important; font-weight:600!important; outline:none; }
:root:root:root:root.univer-mobile #l4 button.univer-app-attendance-details-toggle:hover { background:var(--u-brand-soft)!important; }
:root:root:root:root.univer-mobile #l4 button.univer-app-attendance-details-toggle:focus-visible { box-shadow:inset 0 0 0 2px var(--u-brand)!important; }
:root:root:root:root.univer-mobile #l4 tr.univer-app-attendance-part>td { padding-top:0!important; padding-bottom:0!important; }
:root:root:root:root.univer-mobile #l4 tr.univer-app-attendance-part:has(>td.pd), :root:root:root:root.univer-mobile #l4 tr:has(>td.pd):has(+tr.univer-app-attendance-part) { display:none!important; }
/* Course cards. A closed course is a card as wide as its totals, and the cards stand two or three in a row, so the
   column is filled instead of leaving a narrow table beside an empty field. Its rows are the site's own table rows:
   the body becomes a grid, a closed course takes one track, everything else (the title, the filters, an open course
   and its class log) spans the row. Inside a card the heading and the totals dissolve into one grid, which puts the
   log switch and the syllabus button on one footer line and lines the numbers up across the cards of a row. */
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary) { display:grid; grid-template-columns:repeat(auto-fill,minmax(min(100%,440px),1fr)); column-gap:16px; align-items:stretch; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr { display:table; grid-column:1/-1; width:100%; min-width:0; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false]) { display:flex; grid-column:auto; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct { display:grid; grid-template-columns:minmax(0,1fr) auto; grid-template-rows:auto 1fr; grid-auto-rows:auto; flex:1; min-width:0; height:auto!important; box-sizing:border-box; margin:0 0 16px; padding:0!important; overflow:hidden; border:1px solid var(--u-line); border-radius:12px; background:var(--u-surface)!important; text-align:left; font-weight:400; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct>.univer-app-course-heading, :root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct>.univer-app-attendance-summary { display:contents; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct .univer-app-course-title { grid-column:1/-1; padding:12px 16px; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct :is(.univer-app-score-table,.univer-app-type-row,p) { grid-column:1/-1; order:1; align-self:start; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct p { border-top:1px solid var(--u-line); }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct button.univer-app-attendance-details-toggle { grid-column:1; order:2; min-width:0; overflow:hidden; white-space:nowrap; text-overflow:ellipsis; }
:root:root:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct button.univer-app-course-syllabus { grid-column:2; order:3; justify-content:flex-end; height:38px!important; min-height:38px!important; padding:0 16px!important; border:0!important; border-top:1px solid var(--u-line)!important; border-radius:0!important; background:transparent!important; color:var(--u-muted)!important; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct:not(:has(.univer-app-attendance-details-toggle)) button.univer-app-course-syllabus { grid-column:1/-1; }
:root:root:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct button.univer-app-course-syllabus:is(:hover,[aria-expanded=true]) { background:var(--u-brand-soft)!important; color:var(--u-ink)!important; }
:root:root:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct button.univer-app-course-syllabus:focus-visible { box-shadow:inset 0 0 0 2px var(--u-brand)!important; }
:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr:has(>td.ct>.univer-app-attendance-summary[data-open=false])>td.ct .univer-app-syllabi { grid-column:1/-1; order:4; margin:0; border:0; border-top:1px solid var(--u-line); border-radius:0; }
:root:root:root.univer-mobile #l4 .univer-app-lesson-label { gap:2px 10px; margin:12px 0 6px; }
:root:root:root.univer-mobile #l4 .univer-app-lesson-label strong { font-size:14px!important; font-weight:600; }
:root:root:root.univer-mobile #l4 .univer-app-table-scroll:has(>table.univer-app-matrix) { margin:0!important; }
:root:root:root.univer-mobile #l4 .univer-app-table-scroll .univer-app-matrix :is(td,th) { padding:5px 10px!important; }
/* A card beside an open syllabus list keeps its own height (study-links.js marks the cards of that grid row). */
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-own-height] { align-self:start; }
/* An open course and its class log are one card (attendance.js marks the rows): the course row is its top, each class
   type is a block under a hairline with its name and teacher over the marks, and the last row closes the card with room
   before the next course. The rows are blocks, so their height can move when the log opens and folds. */
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] { display:block!important; grid-column:1/-1; box-sizing:border-box; width:100%!important; min-width:0; margin:0; padding:0 20px!important; overflow:hidden; border:1px solid var(--u-line)!important; border-top:0!important; border-bottom:0!important; border-radius:0!important; background:var(--u-surface)!important; box-shadow:none!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal]>td { display:block; box-sizing:border-box; width:auto!important; min-width:0; height:auto!important; padding:0!important; border:0!important; background:none!important; text-align:left; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal]>td:is(.lt,.rt):empty { display:none!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal=top] { margin-top:4px; border-top:1px solid var(--u-line)!important; border-radius:12px 12px 0 0!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal=top]>td.ct { padding:0 0 14px!important; font-weight:400; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal=top]>td.ct>.univer-app-course-heading { margin:0 0 10px; padding-top:14px; border-top:0; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal=top]>td.ct>.univer-app-attendance-summary { max-width:620px; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal=head]>td:not(:empty) { padding:14px 0 8px!important; border-top:1px solid var(--u-line)!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal=body]>td:not(:empty) { padding:0 0 6px!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal-end] { margin-bottom:24px; border-bottom:1px solid var(--u-line)!important; border-radius:0 0 12px 12px!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal-end]>td:not(:empty) { padding-bottom:18px!important; }
/* The rows' margins carry no !important: Chromium keeps an !important value through an animation, and the card's room
   before the next course grows and folds with its rows. */
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-lesson-label { display:flex; align-items:baseline; flex-wrap:wrap; gap:2px 12px; margin:0!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-lesson-label strong { color:var(--u-ink); font-size:15px!important; font-weight:600; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-lesson-label>span { color:var(--u-muted); font-size:13px!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] div.inner { margin:0!important; padding:0!important; border:0!important; background:none!important; overflow:visible!important; }
/* The marks: dates over marks in even columns that scroll sideways inside their block, never past the card. */
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-table-scroll { width:fit-content; max-width:100%; margin:0!important; overflow-x:auto!important; overflow-y:hidden; border:1px solid var(--u-line)!important; border-radius:10px!important; background:var(--u-surface)!important; box-shadow:none!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-matrix :is(td,th) { min-width:44px!important; padding:6px 8px!important; font-variant-numeric:tabular-nums; white-space:nowrap!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-matrix th { background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:12px!important; font-weight:600!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-matrix td { color:var(--u-ink); font-size:14px!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-matrix tr>:first-child:not(th) { min-width:44px!important; text-align:center!important; }
:root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] .univer-app-matrix tr>th:first-child { min-width:72px!important; text-align:start!important; }
/* An absence without a valid reason stands out in the low band's tint; one with a valid reason in the fair band's. */
:root:root:root:root:root.univer-mobile #l4 .univer-app-matrix td.univer-app-absent { background:var(--univer-band-low-soft)!important; color:var(--univer-band-low-ink)!important; font-weight:700!important; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-matrix td.univer-app-excused { background:var(--univer-band-fair-soft)!important; color:var(--univer-band-fair-ink)!important; font-weight:600!important; }
@media (max-width:600px) {
 :root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal] { padding:0 12px!important; }
 :root:root:root:root:root.univer-mobile #l4 tbody:has(>tr>td.ct>.univer-app-attendance-summary)>tr[data-univer-journal=top]>td.ct>.univer-app-course-heading { flex-wrap:wrap; gap:10px; }
}
/* A phone-wide course card: the totals' columns narrow so РК2 stays inside the card instead of being cut off. */
@media (max-width:420px) {
 :root:root:root:root.univer-mobile #l4 .univer-app-score-table>div { grid-template-columns:minmax(88px,1fr) repeat(var(--univer-columns,2),minmax(72px,auto)); padding:4px 10px; column-gap:6px; }
}
/* Absences: two labelled numbers per course, and the term's own pair above the courses. */
:root:root:root:root:root.univer-mobile #l4 p.univer-app-attendance-absences { display:flex; flex-wrap:wrap; align-items:flex-start; gap:4px 32px; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-attendance-absences-label { flex:1 0 100%; color:var(--u-ink); font-size:13px; font-weight:600; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-absence { display:inline-grid; grid-template-columns:auto auto; align-items:baseline; justify-content:start; gap:0 6px; color:var(--u-muted); font-size:13px; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-absence>b { color:var(--u-ink); font-size:15px; font-weight:700; font-variant-numeric:tabular-nums; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-absence>b.univer-app-absence-zero { color:var(--u-muted); font-weight:500; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-absence>small { grid-column:1/-1; color:var(--u-muted); font-size:12px; }
:root:root:root:root:root.univer-mobile #l4 p.univer-app-attendance-term { display:flex; flex-wrap:wrap; align-items:baseline; gap:6px 24px; margin:0 0 12px!important; padding:12px 16px!important; border:1px solid var(--u-line); border-radius:12px; background:var(--u-surface); color:var(--u-text); font-size:14px!important; line-height:1.4!important; text-align:start; }
:root:root:root:root:root.univer-mobile #l4 p.univer-app-attendance-term[hidden] { display:none!important; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-attendance-term-title { color:var(--u-ink); font-weight:600; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-attendance-term .univer-app-absence>b { font-size:17px; }
/* The student's own syllabus: a tinted chip with a tag that says why, on the course page and in a card's syllabus list. */
:root:root:root:root:root.univer-mobile #l4 [data-univer-empty-section] { display:none!important; }
:root:root:root:root:root:root.univer-mobile #l4 .univer-app-own-tag { display:inline-flex; align-items:center; flex:none; height:20px; margin:0 0 0 8px; padding:0 8px; box-sizing:border-box; border-radius:6px; background:var(--u-brand); color:#fff!important; font-size:12px!important; font-weight:600!important; line-height:1; white-space:nowrap; vertical-align:middle; text-decoration:none; }
:root:root:root:root:root:root.univer-mobile #l4 div.inner>div:has(>h4)>a.univer-app-syllabus-own { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; color:var(--u-ink)!important; font-weight:600!important; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-syllabi li.univer-app-syllabus-own { margin:4px 0; padding:10px 12px; border:1px solid var(--u-brand-line); border-radius:8px; background:var(--u-brand-soft); }
:root:root:root:root:root.univer-mobile #l4 .univer-app-syllabi li>.univer-app-own-tag { margin:0; }
:root:root:root:root:root.univer-mobile #l4 .univer-app-syllabi li.univer-app-syllabus-own>a { color:var(--u-ink)!important; font-weight:600!important; }
/* News: a search field and two quiet filters; what they found is said only when something is filtered. */
:root:root:root:root.univer-mobile #l4 .univer-app-news-tools { display:flex; flex-wrap:wrap; align-items:center; gap:8px; margin:-4px -4px 10px!important; padding:4px!important; border:0!important; border-radius:0!important; background:none!important; box-shadow:none!important; }
:root:root:root:root.univer-mobile #l4 input.univer-app-news-query { flex:1 1 300px; min-width:200px; background-color:var(--u-surface)!important; }
:root:root:root.univer-mobile .univer-app-news-period { position:relative; flex:0 0 auto; }
:root:root:root.univer-mobile .univer-app-news-period>summary { display:inline-flex; align-items:center; gap:8px; height:var(--u-control); box-sizing:border-box; padding:0 14px; border:1px solid var(--u-line-strong); border-radius:10px; background:var(--u-surface); color:var(--u-ink); font-size:14px; font-weight:500; list-style:none; cursor:pointer; user-select:none; }
:root:root:root.univer-mobile .univer-app-news-period>summary::-webkit-details-marker { display:none; }
:root:root:root.univer-mobile .univer-app-news-period>summary::after { content:''; width:6px; height:6px; margin:-3px 2px 0 2px; border-right:2px solid var(--u-muted); border-bottom:2px solid var(--u-muted); transform:rotate(45deg); }
:root:root:root.univer-mobile .univer-app-news-period>summary:hover, :root:root:root.univer-mobile .univer-app-news-period[open]>summary, :root:root:root.univer-mobile .univer-app-news-period-on>summary { border-color:var(--u-brand); background:var(--u-brand-soft); color:var(--u-brand); }
:root:root:root.univer-mobile .univer-app-news-period-box { position:absolute; top:calc(100% + 6px); right:0; z-index:40; display:flex; flex-direction:column; gap:8px; width:260px; box-sizing:border-box; padding:10px; border:1px solid var(--u-line-strong); border-radius:12px; background:var(--u-surface); box-shadow:var(--u-shadow-pop,0 12px 36px rgba(16,40,70,.2)); }
:root:root:root.univer-mobile .univer-app-news-period-box.univer-app-news-period-start { right:auto; left:0; }
:root:root:root.univer-mobile .univer-app-news-presets { display:flex; flex-direction:column; gap:2px; padding-bottom:8px; border-bottom:1px solid var(--u-line); }
:root:root:root:root.univer-mobile #l4 button.univer-app-news-preset { display:block; width:100%; height:36px!important; min-height:36px!important; padding:0 10px!important; border:0!important; border-radius:8px!important; background:transparent!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:500!important; text-align:start; }
:root:root:root:root.univer-mobile #l4 button.univer-app-news-preset:hover { background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
:root:root:root:root.univer-mobile #l4 .univer-app-news-date { display:grid; grid-template-columns:28px minmax(0,1fr); align-items:center; gap:6px; }
:root:root:root:root.univer-mobile #l4 .univer-app-news-date input { width:100%!important; }
:root:root:root:root.univer-mobile #l4 button.univer-app-news-important { border-radius:10px!important; background:var(--u-surface)!important; }
:root:root:root:root.univer-mobile #l4 button.univer-app-news-important[aria-pressed=true] { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
:root:root:root:root.univer-mobile #l4 .univer-app-news-state { flex:1 0 100%; display:flex; flex-wrap:wrap; align-items:center; gap:4px 14px; min-height:24px; }
:root:root:root:root.univer-mobile #l4 .univer-app-news-state[hidden] { display:none!important; }
:root:root:root:root.univer-mobile .univer-app-news-count { margin:0; color:var(--u-muted); font-size:13px!important; }
:root:root:root:root.univer-mobile #l4 button:is(.univer-app-news-reset,.univer-app-news-all) { height:auto!important; min-height:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-brand)!important; font-size:13px!important; font-weight:600!important; }
:root:root:root:root.univer-mobile #l4 button:is(.univer-app-news-reset,.univer-app-news-all):hover { text-decoration:underline; }
:root:root:root:root.univer-mobile #l4 .univer-app-news-pager-top { display:none!important; }
/* Collapse and the page numbers ride the bottom edge of the window while their block is on screen and stop where they
   stand in the page, so a long article or a long list is left without scrolling to its end. */
/* The site keeps the news in div.inner with overflow:auto, which scrolls sideways by a few pixels and is the scroller
   sticky would stick to. The same block is the reading column: about a sheet of paper wide however wide the window is. */
:root:root.univer-mobile #l4 div.inner:has(>table.univer-app-news) { overflow:visible!important; max-width:900px; margin-left:auto!important; margin-right:auto!important; }
:root:root.univer-mobile #l4 table.mt:has(table.univer-app-news) { max-width:900px!important; margin-left:auto!important; margin-right:auto!important; }
/* Collapse is the same quiet button as Show in full. While it rides the window edge a white band with a fade lies
   under it across the card, so the text ends above it the way a folded article ends. */
:root:root.univer-mobile #l4 .univer-app-news tr.news { overflow-x:clip; }
:root:root.univer-mobile #l4 .univer-app-news-body:has(+button.univer-app-news-toggle[aria-expanded=true]:not([hidden])) { padding-bottom:28px; }
:root:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle[aria-expanded=true] { --u-news-rise:12px; position:sticky; bottom:var(--u-news-rise); z-index:6; }
:root:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle[aria-expanded=true]::before { content:''; position:absolute; z-index:-1; left:-24px; right:-3000px; top:-44px; bottom:calc(-1 * var(--u-news-rise)); pointer-events:none; background:linear-gradient(transparent,#fff 36px); }
/* The page numbers ride the same edge on the right, clear of Collapse on the left. */
/* The floating pager sits centred under the reading column on a tinted plate with a deeper shadow, so it reads apart from the white news cards it floats over. */
:root:root.univer-mobile #l4 .univer-app-news-pager:not(.univer-app-news-pager-top) { position:sticky; bottom:12px; z-index:7; width:fit-content; max-width:100%; box-sizing:border-box; margin:4px auto 16px!important; padding:6px; border:1px solid var(--u-brand-line); border-radius:14px; background:var(--u-brand-soft); box-shadow:0 12px 32px rgba(16,40,70,.22),0 2px 6px rgba(16,40,70,.12); }
:root.univer-mobile .univer-app-news-pager>.univer-app-news-step { display:inline-flex; align-items:center; justify-content:center; box-sizing:border-box; width:var(--u-control); min-width:var(--u-control); height:var(--u-control); padding:0!important; border:1px solid var(--u-line); border-radius:10px; background:#fff; color:var(--u-ink); text-decoration:none; }
:root.univer-mobile .univer-app-news-pager>.univer-app-news-step::before { content:''; width:18px; height:18px; background:currentColor; -webkit-mask:${icons.back} center/contain no-repeat; }
:root.univer-mobile .univer-app-news-pager>.univer-app-news-next::before { -webkit-mask-image:${icons.forward}; }
:root.univer-mobile .univer-app-news-pager>a.univer-app-news-step:hover { background:var(--u-brand-soft); border-color:var(--u-brand-line); }
:root.univer-mobile .univer-app-news-pager>span.univer-app-news-step { opacity:.45; cursor:default; }
:root:root.univer-mobile #l4 .univer-app-news-pager .univer-app-news-jump { margin:0 0 0 6px!important; }
/* The page numbers with their steps and the page field fill most of the 900 px column, so Collapse stands one row above them. */
:root:root.univer-mobile #l4:has(.univer-app-news-pager:not(.univer-app-news-pager-top):not([hidden])) .univer-app-news button.univer-app-news-toggle[aria-expanded=true] { --u-news-rise:78px; }
:root:root.univer-mobile .univer-app-overview-more[aria-expanded=true] { position:sticky; bottom:16px; z-index:6; box-shadow:0 4px 14px rgba(16,40,70,.12); }
/* Controls are pressed, not read, so their words cannot be selected. */
:root.univer-mobile :is(button,summary,[role=button],[role=tab],input[type=button],input[type=submit],input[type=reset],#l1,.univer-app-menu,.univer-app-news-pager,a.univer-app-service-card,a.univer-app-desk-card,a.downLoad) { user-select:none; }
/* Arranging the home screen: a long press lifts a tile, the others make room, and the bin opens right under the grid. */
:root:root.univer-mobile .univer-app-quick .univer-app-tile { touch-action:pan-y; user-select:none; -webkit-user-drag:none; }
:root:root.univer-mobile .univer-app-quick .univer-app-tile.univer-app-holding>a { transform:scale(.97)!important; transition:transform .24s var(--u-ease-out)!important; }
:root:root.univer-mobile .univer-app-quick .univer-app-tile.univer-app-lifted { z-index:30; pointer-events:none; will-change:transform; }
:root:root.univer-mobile .univer-app-quick .univer-app-tile.univer-app-lifted>a { transform:scale(1.05)!important; border-color:var(--u-brand)!important; background:var(--u-surface)!important; box-shadow:0 14px 32px rgba(16,40,70,.24)!important; transition:transform .2s var(--u-ease-out),box-shadow .2s var(--u-ease-out)!important; cursor:grabbing; }
:root:root.univer-mobile .univer-app-quick .univer-app-tile.univer-app-lifted.univer-app-binning>a { transform:scale(.6)!important; }
:root:root.univer-mobile .univer-app-quick.univer-app-arranging .univer-app-tile:not(.univer-app-lifted)>a { pointer-events:none; }
:root.univer-mobile .univer-app-trash { position:fixed; z-index:1200; display:flex; align-items:center; justify-content:center; gap:8px; box-sizing:border-box; height:44px; padding:0 20px; border:1px solid rgba(180,35,24,.3); border-radius:10px; background:var(--u-danger-soft,#fdeceb); color:var(--u-danger,#b42318); font:500 14px var(--u-font); opacity:0; pointer-events:none; transform:translateY(-8px) scale(.985); transition:opacity .16s var(--u-ease-out),transform .2s var(--u-ease-out),box-shadow .12s ease,border-color .12s ease; }
:root.univer-mobile .univer-app-trash.univer-app-trash-on { opacity:1; transform:none; }
:root.univer-mobile .univer-app-trash.univer-app-trash-hot { border-color:var(--u-danger,#b42318); box-shadow:0 0 0 3px rgba(180,35,24,.16); }
:root.univer-mobile .univer-app-trash-icon { width:18px; height:18px; background:currentColor; -webkit-mask:var(--univer-trash-icon) center/contain no-repeat; }
:root.univer-mobile.univer-app-still .univer-app-trash { transition:none; }
/* While a tile is up the page moves down by the bin's height, so the bin covers nothing. */
:root:root.univer-mobile .univer-app-quick.univer-app-arranging { margin-bottom:68px!important; }
:root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) .univer-app-quick { transition:margin-bottom 180ms var(--u-ease-out); }
/* New section: the last place in the grid, quiet until pointed at. */
:root:root.univer-mobile .univer-app-quick .univer-app-add-tile { display:flex; align-items:center; gap:12px; min-width:0; min-height:60px; box-sizing:border-box; margin:0!important; padding:10px 14px!important; border:1px dashed var(--u-line-strong)!important; border-radius:12px!important; background:transparent!important; color:var(--u-muted)!important; font:600 14px/1.3 var(--u-font)!important; text-align:start; cursor:pointer; transition:background-color .16s ease,border-color .16s ease,color .16s ease; }
:root:root.univer-mobile .univer-app-quick .univer-app-add-tile:hover, :root:root.univer-mobile .univer-app-quick .univer-app-add-tile:focus-visible { border-color:var(--u-brand)!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; outline:none; }
:root.univer-mobile .univer-app-add-icon { flex:0 0 auto; display:grid; place-items:center; width:32px; height:32px; border-radius:6px; background:var(--u-surface-2); }
:root.univer-mobile .univer-app-add-icon::before { content:''; width:18px; height:18px; background:currentColor; -webkit-mask:var(--univer-plus-icon) center/contain no-repeat; }
:root.univer-mobile .univer-app-add-layer { position:fixed; inset:0; z-index:1100; display:flex; align-items:center; justify-content:center; padding:24px; box-sizing:border-box; background:rgba(16,28,44,.32); }
:root.univer-mobile .univer-app-add-panel { display:flex; flex-direction:column; width:min(520px,100%); max-height:min(640px,calc(100vh - 48px)); box-sizing:border-box; padding:20px; border-radius:16px; background:var(--u-surface); color:var(--u-ink); box-shadow:var(--u-shadow-pop,0 18px 48px rgba(16,40,70,.28)); font-family:var(--u-font); }
:root.univer-mobile .univer-app-add-head { display:flex; align-items:center; justify-content:space-between; gap:12px; margin:0 0 14px; }
:root.univer-mobile .univer-app-add-head>h2 { margin:0!important; padding:0!important; color:var(--u-ink)!important; font:600 18px/1.3 var(--u-font)!important; }
:root.univer-mobile .univer-app-add-close { display:grid; place-items:center; width:36px; height:36px; margin:0; padding:0; border:0; border-radius:10px; background:transparent; color:var(--u-muted); cursor:pointer; }
:root.univer-mobile .univer-app-add-close::before { content:''; width:18px; height:18px; background:currentColor; -webkit-mask:var(--univer-close-icon) center/contain no-repeat; }
:root.univer-mobile .univer-app-add-close:hover { background:var(--u-surface-2); color:var(--u-ink); }
:root.univer-mobile .univer-app-add-search { display:block; flex:0 0 auto; width:100%; height:40px; box-sizing:border-box; margin:0 0 8px; padding:0 14px; border:1px solid var(--u-line-strong); border-radius:10px; background:var(--u-surface); color:var(--u-ink); font:14px var(--u-font); outline:none; }
:root.univer-mobile .univer-app-add-search:focus { border-color:var(--u-brand); box-shadow:0 0 0 3px var(--u-brand-soft); }
:root.univer-mobile .univer-app-add-list { flex:1 1 auto; min-height:0; margin:0 -8px; padding:0 8px; overflow-y:auto; }
:root.univer-mobile .univer-app-add-group { margin:14px 0 4px!important; padding:0 8px; color:var(--u-muted); font:600 12px var(--u-font)!important; }
:root.univer-mobile .univer-app-add-row { display:flex; align-items:center; gap:12px; width:100%; min-height:48px; box-sizing:border-box; margin:0; padding:6px 8px; border:0; border-radius:10px; background:transparent; color:var(--u-ink); font:500 14px/1.3 var(--u-font); text-align:start; cursor:pointer; transition:background-color .12s ease; }
:root.univer-mobile .univer-app-add-row:hover, :root.univer-mobile .univer-app-add-row:focus-visible { background:var(--u-brand-soft); outline:none; }
:root.univer-mobile .univer-app-add-name { flex:1 1 auto; min-width:0; overflow-wrap:break-word; }
:root.univer-mobile .univer-app-add-plus { flex:0 0 auto; width:18px; height:18px; background:var(--u-brand); -webkit-mask:var(--univer-plus-icon) center/contain no-repeat; opacity:0; transition:opacity .12s ease; }
:root.univer-mobile .univer-app-add-row:is(:hover,:focus-visible) .univer-app-add-plus { opacity:1; }
:root.univer-mobile .univer-app-add-empty { margin:24px 0!important; color:var(--u-muted); font:14px var(--u-font)!important; text-align:center; }
`;
            (doc.head || doc.documentElement).appendChild(style);
            return true;
        }
        if (apply(document)) mutated = true;
        window.__univerNoticesV1?.present(enabled && !viewer);
        if (window.__univerKitV1) window.__univerKitV1.configure(enabled && !viewer);
        // «Наверх» reads the layout (button width, control boxes) and measure() writes scroll hints after its own reads:
        // in this order the page is laid out once, not again after the hint writes.
        if (window.__univerTopV1) window.__univerTopV1.configure(enabled, lang);
        if (measure) window.__univerMobileV1.measure();
        // Same-origin frames only. Polling sees late frames without rewriting page contents.
        Array.from(document.querySelectorAll('iframe,frame')).forEach(function (frame) {
            try { if (frame.contentWindow.location.origin === location.origin && frame.contentDocument && apply(frame.contentDocument)) mutated = true; } catch (_) { }
        });
        return mutated;
    };
})();
