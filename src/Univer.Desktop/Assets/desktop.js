/* Desktop presentation layered on the reversible Android DOM adapter. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz') return;
    if (!window.__univerMotionUI) {
        var motions = new Map(), layouts = new Map(), ghosts = new Map();
        var ease = 'cubic-bezier(.23,1,.32,1)';
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
                {opacity:gentle ? .4 : content ? .35 : .2, transform:gentle ? 'none' : horizontal ? 'translateX(' + (kind === 'back' ? '-16px' : '16px') + ') scale(.985)' : content ? 'translateY(8px) scale(.985)' : kind === 'tile' ? 'translateY(10px) scale(.97)' : 'translateY(-8px) scale(.97)'};
            cancel(node);
            var animation = node.animate([from, {opacity:1, transform:'none'}],
                {duration:gentle ? 180 : content ? 220 : kind === 'tile' ? 200 : 240, delay:gentle ? 0 : (delay || 0), easing:ease, fill:'backwards'});
            motions.set(node, animation);
            var finish = function () { if (motions.get(node) === animation) motions.delete(node); };
            animation.onfinish = animation.oncancel = finish;
        };
        var resize = function (node, before, duration) {
            if (!node?.isConnected || !allowed() || reduce() || !node.animate) return;
            var after = node.getBoundingClientRect().height;
            if (before <= 0 || after <= 0 || Math.abs(before - after) < 1) return;
            retire(layouts, node);
            track(layouts, node, node.animate([{height:before + 'px', maxHeight:'none', overflow:'hidden'}, {height:after + 'px', maxHeight:'none', overflow:'hidden'}],
                {duration:duration || 220, easing:ease, fill:'backwards'}));
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
                    {duration:180, easing:'cubic-bezier(.77,0,.175,1)', fill:'backwards'});
                motions.set(node, a); a.onfinish = a.oncancel = function () { if (motions.get(node) === a) motions.delete(node); };
            });
        };
        var exit = function (node) {
            if (!node?.isConnected || !allowed() || !node.animate) { stop(node); return; }
            var visual = getComputedStyle(node), opacity = visual.opacity, transform = visual.transform;
            cancel(node); var rect = node.getBoundingClientRect();
            if (!rect.width || !rect.height) { stop(node); return; }
            // A passive visual copy lets the real popup close and release focus immediately.
            // Computed styles avoid duplicate adapter classes/IDs and preserve the already painted shape.
            var clone = node.cloneNode(true), sources = [node].concat(Array.from(node.querySelectorAll('*'))), copies = [clone].concat(Array.from(clone.querySelectorAll('*')));
            var properties = 'display position top right bottom left box-sizing width height min-width min-height max-width max-height margin padding border border-radius background color opacity font font-family font-size font-weight line-height letter-spacing text-align text-decoration white-space overflow gap align-items justify-content flex flex-direction flex-wrap grid-template-columns grid-column transform transform-origin box-shadow list-style vertical-align object-fit -webkit-mask mask'.split(' ');
            copies.forEach(function (copy, index) {
                var css = getComputedStyle(sources[index]);
                Array.from(copy.attributes).forEach(function (attr) { if (/^(?:id|class|name|autofocus|on)/i.test(attr.name)) copy.removeAttribute(attr.name); });
                properties.forEach(function (property) { copy.style.setProperty(property, css.getPropertyValue(property)); });
                copy.style.pointerEvents = 'none'; copy.tabIndex = -1;
                ['::before','::after'].forEach(function (which) {
                    if (!window.CSS?.supports('content', '""')) return;
                    var pseudo = getComputedStyle(sources[index], which);
                    if (!pseudo.content || pseudo.content === 'none' || pseudo.content === 'normal') return;
                    var span = document.createElement('span');
                    properties.forEach(function (property) { span.style.setProperty(property, pseudo.getPropertyValue(property)); });
                    if (/^["'].*["']$/.test(pseudo.content)) span.textContent = pseudo.content.slice(1, -1);
                    if (which === '::before') copy.prepend(span); else copy.append(span);
                });
            });
            clone.querySelectorAll('script,style,iframe,frame,object,embed,link,meta').forEach(function (copy) { copy.remove(); });
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
        window.__univerMotionUI = {reveal:reveal, stop:stop, reduced:reduce, change:change, reflow:reflow, exit:exit, resize:resize, zoom:zoom};
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
        window.__univerDesktop = { enabled: true, viewer: false, close: false, language: '' };
        document.addEventListener('click', function (event) {
            if (!event.isTrusted || window !== window.top) return;
            var link = event.target.closest('a[href]');
            if (!link || !link.closest('.header')) return;
            var url = new URL(link.href, location.href);
            var match = url.pathname.match(/^\/lang\/change\/(ru|kz|en)\/?$/);
            if (url.origin === location.origin && match) {
                window.__univerDesktop.language = match[1];
                // Only a non-secret language code crosses to native code; no callable host objects.
                if (typeof window.__univerNativeNotify === 'function') window.__univerNativeNotify('univer-language:' + match[1]);
                else if (window.CefSharp) CefSharp.PostMessage('univer-language:' + match[1]);
            }
        }, true);
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
        close: icon("<path d='M6 6l12 12M18 6L6 18'/>")
    };
    window.__univerDesktop.apply = function (enabled, viewer, language) {
        window.__univerDesktop.enabled = enabled;
        window.__univerDesktop.viewer = viewer;
        if (window.__univerMobileV1) window.__univerMobileV1.setEnabled(enabled, language || 'ru');
        // The dark theme belongs to the reading layout only; Original removes it with the rest of the presentation.
        function apply(doc) {
            present(doc);
            if (window.__univerThemeV1) window.__univerThemeV1.apply(doc, enabled && window.__univerTheme === 'dark');
        }
        function present(doc) {
            // The explicit app preference disables all feedback transitions; system reduction is handled by CSS.
            var still = window.__univerMotion === false;
            if (still || !enabled) window.__univerMotionUI.stop();
            if (doc.documentElement.classList.contains('univer-app-still') !== still) doc.documentElement.classList.toggle('univer-app-still', still);
            var style = doc.getElementById('univer-desktop-style');
            if (!enabled) {
                if (openList) closeList(false);
                doc.querySelectorAll('.univer-app-select').forEach(function (list) { list.remove(); });
                doc.documentElement.classList.remove('univer-app-keyboard', 'univer-app-still');
                if (style) style.remove(); return;
            }
            if (style) return;
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
:root.univer-mobile .univer-app-tabs>a { display:inline-flex!important; align-items:center; height:36px; min-height:0!important; padding:0 14px!important; margin:0!important; border:0!important; border-radius:8px!important; background:none!important; color:var(--u-muted)!important; font-size:14px!important; font-weight:600!important; text-decoration:none!important; white-space:nowrap; float:none!important; width:auto!important; flex:0 0 auto!important; }
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
:root.univer-mobile #l1 .univer-app-sections { display:inline-flex!important; align-items:center; gap:8px; height:36px; min-height:0!important; box-sizing:border-box; padding:0 14px 0 12px!important; margin:0!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:600!important; white-space:nowrap; }
:root.univer-mobile #l1 .univer-app-sections::before { content:''; width:16px; height:12px; background:linear-gradient(currentColor,currentColor) top/100% 2px no-repeat,linear-gradient(currentColor,currentColor) center/100% 2px no-repeat,linear-gradient(currentColor,currentColor) bottom/100% 2px no-repeat; }
:root.univer-mobile #l1 .univer-app-sections:is(:hover,[aria-expanded=true]) { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-brand)!important; }
:root.univer-mobile #l1 .univer-app-sections[hidden] { display:none!important; }
:root.univer-mobile #l1 .univer-app-flags { display:flex!important; align-items:center; gap:2px; padding:3px; border:1px solid var(--u-line); border-radius:10px; background:var(--u-surface-2); }
:root.univer-mobile #l1 .univer-app-flags>:is(a,img) { box-sizing:border-box; width:34px!important; height:28px!important; min-height:0!important; padding:0!important; margin:0!important; display:inline-flex!important; align-items:center; justify-content:center; border:0!important; border-radius:7px!important; }
:root.univer-mobile #l1 .univer-app-flags>a img { width:20px!important; min-width:0!important; height:auto!important; opacity:.7; border:0!important; }
:root.univer-mobile #l1 .univer-app-flags>a:hover { background:#fff; }
:root.univer-mobile #l1 .univer-app-flags>a:hover img { opacity:1; }
:root.univer-mobile #l1 .univer-app-flags>img { padding:6px 7px!important; object-fit:contain!important; background:#fff; outline:1.5px solid var(--u-brand); outline-offset:-1.5px; }
:root.univer-mobile .univer-app-actions .links { position:relative; display:flex!important; align-items:center; gap:8px; width:auto!important; min-width:0!important; max-width:none!important; padding:0!important; margin:0!important; float:none!important; text-align:start!important; font-size:14px!important; white-space:nowrap!important; }
:root.univer-mobile .univer-app-actions .links>a { display:inline-flex!important; align-items:center; height:36px; padding:0 10px!important; border-radius:8px; color:var(--u-muted)!important; font-size:14px!important; font-weight:600; text-decoration:none!important; }
:root.univer-mobile .univer-app-actions .links>a:hover { background:var(--u-surface-2); color:var(--u-ink)!important; }
:root.univer-mobile .univer-app-actions .univer-app-account { position:relative; }
:root.univer-mobile .univer-app-actions .univer-app-account>summary { display:inline-flex!important; align-items:center; gap:8px; height:36px; min-height:0!important; box-sizing:border-box; padding:0 12px!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:600!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>summary::before { content:''; width:18px; height:18px; background:currentColor; -webkit-mask:${icons.person} center/contain no-repeat; opacity:.75; }
:root.univer-mobile .univer-app-actions .univer-app-account>summary::after { content:''; width:6px; height:6px; margin:-3px 0 0 2px; border-right:2px solid currentColor; border-bottom:2px solid currentColor; transform:rotate(45deg); opacity:.55; }
:root.univer-mobile .univer-app-actions .univer-app-account:is([open],:hover)>summary { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-brand)!important; }
:root.univer-mobile .univer-app-actions .univer-app-account[open]>div { position:absolute; right:0; top:calc(100% + 8px); z-index:850; box-sizing:border-box; width:max-content; min-width:260px; max-width:min(380px,calc(100vw - 32px)); padding:8px!important; background:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow-pop); white-space:normal; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a { display:block!important; min-height:0!important; line-height:1.45!important; padding:6px 12px!important; margin:0!important; border-radius:8px; color:var(--u-ink)!important; font-size:14px!important; font-weight:500; text-decoration:none!important; float:none!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a:hover { background:var(--u-surface-2); }
:root.univer-mobile .univer-app-actions .univer-app-account>div a :is(span,div,b,strong,font) { display:inline!important; float:none!important; width:auto!important; color:inherit!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a[href*="/user/pass"] { font-weight:600!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a[href*="/user/logout"] * { color:inherit!important; }
:root.univer-mobile .univer-app-actions .univer-app-account>div a[href*="/user/logout"] { color:var(--u-danger)!important; border-top:1px solid var(--u-line); border-radius:0 0 8px 8px; margin-top:4px!important; }
:root.univer-mobile .univer-app-academic-meta { list-style:none; display:flex; flex-wrap:wrap; gap:6px; padding:8px 12px 4px!important; margin:4px 0 0!important; border-top:1px solid var(--u-line); }
:root.univer-mobile .univer-app-academic-meta li { padding:2px 8px!important; border-radius:999px; background:var(--u-surface-2); color:var(--u-muted); font-size:12px!important; }
:root.univer-mobile.univer-app-unified :is(#l2,#l3).univer-app-header-row { display:none!important; }

/* "All sections" opens as a floating panel instead of pushing the page down. */
:root.univer-mobile .univer-app-menu { scroll-margin-top:calc(var(--u-header) + 12px); }
:root.univer-mobile.univer-app-unified .univer-app-menu:not([open]) { display:none!important; }
:root.univer-mobile.univer-app-unified .univer-app-menu[open] { position:fixed!important; z-index:900; top:calc(var(--u-header) + 8px); left:24px; right:24px; max-width:1200px!important; margin:0 auto!important; box-sizing:border-box; max-height:calc(100vh - var(--u-header) - 24px); overflow:auto; padding:16px 16px 20px!important; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:14px!important; box-shadow:var(--u-shadow-pop); }
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
:root.univer-mobile #tools>tbody>tr>td.center { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(250px,1fr)); gap:12px; align-items:start; padding:0!important; background:none!important; }
:root.univer-mobile .univer-app-category { margin:0!important; overflow:hidden; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:10px!important; }
:root.univer-mobile .univer-app-category>summary { padding:10px 14px!important; min-height:0!important; background:var(--u-surface-2); border-bottom:1px solid var(--u-line); color:var(--u-ink)!important; font-size:13px!important; font-weight:600!important; }
:root.univer-mobile #tools li { border:0!important; }
:root.univer-mobile #tools li a { display:block; padding:7px 14px!important; color:var(--u-ink)!important; font-size:14px!important; line-height:1.45!important; text-decoration:none; outline:none!important; }
:root.univer-mobile #tools li a:hover { background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
:root.univer-mobile #tools li a:focus-visible { background:var(--u-brand-soft)!important; box-shadow:inset 0 0 0 2px var(--u-brand); }
/* The current menu page: a tint plus a left bar, so the state does not rest on colour alone. */
:root.univer-mobile #tools a[aria-current=page] { background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-weight:600!important; box-shadow:inset 3px 0 0 var(--u-brand)!important; border-left:0!important; }

/* Page body. */
:root.univer-mobile #l4 td.center { padding:8px 32px 40px!important; background:transparent!important; }
:root.univer-mobile #l4 td.center>*, :root.univer-mobile #l5 td.center>* { max-width:var(--u-page); margin-left:auto!important; margin-right:auto!important; box-sizing:border-box; }
:root.univer-mobile table.mt { background:transparent!important; border:0!important; }
:root.univer-mobile #l4 table.mt>tbody>tr.top>:is(.ct,.tt) { padding:16px 0 8px!important; background:none!important; color:var(--u-ink)!important; font-size:var(--u-fs-title)!important; font-weight:600!important; line-height:1.35!important; letter-spacing:-.005em; }
:root.univer-mobile #l4 td.center>:is(table.mt:first-of-type,table.univer-app-empty-panel+table.mt)>tbody>tr.top:first-child>:is(.ct,.tt) { padding-top:8px!important; }
:root.univer-mobile #l4 table.mt>tbody>tr>.tt { padding:4px 0 8px!important; }
:root.univer-mobile #l4 .univer-app-study-panel .ct.message { font-size:var(--u-fs-meta)!important; color:var(--u-muted)!important; }
:root.univer-mobile #l4 :is(h1,h2,h3) { color:var(--u-ink); line-height:1.3; letter-spacing:-.015em; }
:root.univer-mobile #l4 h4 { margin:12px 0 8px!important; padding:0!important; border:0!important; background:none!important; color:var(--u-ink)!important; font-size:var(--u-fs-meta)!important; font-weight:600!important; text-align:start!important; }
:root.univer-mobile .univer-app-nav-table>tbody>tr { gap:0!important; padding:0!important; border-bottom:1px solid var(--u-line); }
:root.univer-mobile .univer-app-nav-table>tbody>tr:last-child { border-bottom:0; }
:root.univer-mobile .univer-app-table-scroll .univer-app-nav-table>tbody>tr>td { border:0!important; padding:12px 14px!important; }
:root.univer-mobile table.univer-app-empty-panel, :root.univer-mobile .univer-app-study-panel>tbody>tr>.univer-app-secondary-heading { display:none!important; }

/* Site links that act as navigation are chips, not text boxes. */
:root.univer-mobile #l4 .ts { display:flex!important; flex-wrap:wrap; align-items:center; gap:8px; height:auto!important; min-height:0!important; padding:4px 0 12px!important; overflow:visible!important; }
:root.univer-mobile #l4 :is(.ts>a,a.univer-app-page-link,.univer-app-semester-nav a,.univer-app-nav-table a) { display:inline-flex!important; align-items:center; gap:8px; box-sizing:border-box; width:auto!important; max-width:100%; min-height:38px!important; flex:0 1 auto!important; float:none!important; margin:0!important; padding:7px 14px!important; border:1px solid var(--u-line)!important; border-radius:10px!important; background:#fff!important; box-shadow:var(--u-shadow); color:var(--u-brand)!important; font-size:14px!important; font-weight:600!important; line-height:1.35!important; text-align:start!important; text-decoration:none!important; }
:root.univer-mobile #l4 :is(.ts>a,a.univer-app-page-link,.univer-app-semester-nav a,.univer-app-nav-table a):hover { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-brand-strong)!important; }
:root.univer-mobile #l4 .ts>a>img:is([src*="tb_left"],[src*="tb_right"]) { display:none!important; }
:root.univer-mobile #l4 .ts>a>img { width:16px; height:auto; }
:root.univer-mobile #l4 .ts>a:has(>img:is([src*="tb_left"],[src*="tb_right"])) { color:var(--u-muted)!important; font-weight:500!important; }
:root.univer-mobile #l4 .ts>a:has(>img[src*="tb_right"]) { margin-left:auto!important; }
:root.univer-mobile #l4 .ts>a:has(>img[src*="tb_left"])::before { content:'←'; font-weight:700; }
:root.univer-mobile #l4 .ts>a:has(>img[src*="tb_right"])::after { content:'→'; font-weight:700; }
:root.univer-mobile #l4 .ct>a.univer-app-page-link:only-child { min-height:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:20px!important; font-weight:600!important; }
:root.univer-mobile #l4 .ts>a.syllabus-info::before { content:''; width:16px; height:16px; flex:0 0 auto; background:currentColor; -webkit-mask:${icons.doc} center/contain no-repeat; }
:root.univer-mobile #l4 .univer-app-breadcrumb { display:flex!important; flex-wrap:wrap; align-items:center; gap:4px 8px; padding:4px 0 8px!important; }
:root.univer-mobile #l4 .univer-app-breadcrumb>a.univer-app-page-link { min-height:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-muted)!important; font-size:14px!important; font-weight:500!important; }
:root.univer-mobile #l4 .univer-app-breadcrumb>a.univer-app-page-link:not(:last-of-type)::after { content:'/'; margin-left:8px; color:var(--u-line-strong); }
:root.univer-mobile #l4 .univer-app-breadcrumb>a.univer-app-page-link:last-of-type { flex-basis:100%; color:var(--u-ink)!important; font-size:20px!important; font-weight:600!important; }
:root.univer-mobile .univer-app-quick { max-width:var(--u-page); margin:16px 32px 4px!important; display:grid!important; grid-template-columns:repeat(auto-fill,minmax(200px,1fr)); gap:10px; }
:root.univer-mobile .univer-app-quick a { display:flex!important; align-items:center; min-height:56px; box-sizing:border-box; padding:12px 16px!important; border:0!important; border-radius:8px!important; background:#fff!important; box-shadow:none; color:var(--u-ink)!important; font-size:14px!important; font-weight:600; text-align:start!important; justify-content:flex-start!important; text-decoration:none; }
:root.univer-mobile .univer-app-quick a::after { content:'→'; margin-left:auto; padding-left:12px; color:var(--u-brand); }
:root.univer-mobile .univer-app-quick a:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; }

/* Buttons that the adapter adds, and the site's own content buttons. */
:root.univer-mobile #l4 :is(input[type=button],input[type=submit],button):not(.univer-app-days button):not(.univer-app-schedule-modes button) { border-radius:8px!important; font-weight:600; }
:root.univer-mobile .univer-app-course-open, :root.univer-mobile .univer-app-catalog-download { display:inline-flex!important; align-items:center; justify-content:center; gap:6px; width:auto!important; min-width:96px; min-height:34px!important; margin:0!important; padding:6px 14px!important; border:0!important; border-radius:8px!important; background:var(--u-brand)!important; color:#fff!important; font-size:var(--u-fs-body)!important; font-weight:600!important; cursor:pointer; }
:root.univer-mobile .univer-app-course-open:hover, :root.univer-mobile .univer-app-catalog-download:hover { background:var(--u-brand-strong)!important; }
:root.univer-mobile .univer-app-catalog-download::before { content:''; width:14px; height:14px; background:currentColor; -webkit-mask:${icons.download} center/contain no-repeat; }
:root.univer-mobile a.univer-app-action-link { display:inline-flex!important; align-items:center; min-height:32px!important; padding:5px 12px!important; border:1px solid var(--u-brand-line); border-radius:8px; background:var(--u-brand-soft); color:var(--u-brand)!important; font-size:13px!important; font-weight:600; white-space:nowrap; text-decoration:none; }
:root.univer-mobile a.univer-app-action-link:hover { background:var(--u-brand)!important; color:#fff!important; }

/* Tables. */
:root.univer-mobile .univer-app-table-scroll { margin:6px 0 16px!important; overflow:auto; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow); scrollbar-color:#a9b8c8 #eef2f6; }
:root.univer-mobile .univer-app-table-scroll>table { border:0!important; background:none!important; margin:0!important; }
:root.univer-mobile .univer-app-table-scroll table { border-collapse:collapse!important; border-spacing:0!important; }
:root.univer-mobile .univer-app-table-scroll :is(th,td) { border:0!important; border-bottom:1px solid var(--u-line)!important; padding:10px 14px!important; vertical-align:middle!important; font-size:14px!important; }
:root.univer-mobile .univer-app-table-scroll tr:last-child>td { border-bottom:0!important; }
:root.univer-mobile .univer-app-table-scroll th { background:var(--u-surface-2)!important; color:var(--u-muted)!important; font-size:13px!important; font-weight:600!important; text-align:start; }
:root.univer-mobile .univer-app-table-scroll tbody>tr:hover>td { background:#fafcfe; }
:root.univer-mobile .univer-app-scroll-hint { margin:8px 0 -2px!important; font-size:var(--u-fs-meta)!important; color:var(--u-muted)!important; }
:root.univer-mobile .univer-app-matrix { table-layout:auto!important; }
:root.univer-mobile .univer-app-table-scroll .univer-app-matrix :is(th,td) { white-space:normal!important; text-align:center!important; border-right:1px solid #edf1f5!important; }
:root.univer-mobile .univer-app-table-scroll .univer-app-matrix tr>:first-child { text-align:start!important; }
:root.univer-mobile .univer-app-table-scroll .univer-app-matrix tr>:last-child { border-right:0!important; font-weight:600; }
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
:root.univer-mobile #l4 .univer-app-form-table li { list-style:none!important; margin:0!important; padding:0!important; color:var(--u-muted); font-size:13px!important; font-weight:600; }
:root.univer-mobile #l4 .univer-app-sort-head>th { cursor:pointer; }
:root.univer-mobile .univer-app-sort-toggle { border-radius:8px!important; }
:root.univer-mobile .univer-app-pairs>tbody>tr { border-radius:12px!important; border-color:var(--u-line)!important; box-shadow:var(--u-shadow); background:#fff!important; }
:root.univer-mobile .univer-app-pairs>tbody>tr>td:nth-child(odd) { color:var(--u-muted)!important; font-size:13px!important; font-weight:600; }

/* УМКД: course list and material files. */
:root.univer-mobile .univer-app-courses tr.link { cursor:default; }
:root.univer-mobile .univer-app-courses tr.link>td:nth-child(2) { color:var(--u-ink)!important; font-weight:600; }
:root.univer-mobile .univer-app-courses tr.link>td:last-child { color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-courses tr.link>td:last-child .univer-app-course-open { margin-left:12px!important; vertical-align:middle; }
:root.univer-mobile input[type=file] { box-sizing:border-box; max-width:100%; min-height:44px; padding:6px!important; border:1px dashed var(--u-line-strong)!important; border-radius:10px!important; background:var(--u-surface-2)!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile input[type=file]::file-selector-button { margin-right:12px; padding:7px 14px; border:1px solid var(--u-brand-line); border-radius:8px; background:#fff; color:var(--u-brand); font:600 13px var(--u-font); cursor:pointer; }
:root.univer-mobile .univer-app-materials>tbody>tr>th:only-child { padding:14px 14px 8px!important; background:#fff!important; color:var(--u-brand)!important; font-size:13px!important; font-weight:700!important; letter-spacing:.02em; text-align:start!important; }
:root.univer-mobile .univer-app-materials a.downLoad { display:inline-flex!important; align-items:center; gap:8px; min-height:32px!important; padding:5px 12px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-size:var(--u-fs-body)!important; font-weight:600!important; text-decoration:none!important; overflow-wrap:anywhere; }
:root.univer-mobile .univer-app-materials a.downLoad::before { content:''; width:15px; height:15px; flex:0 0 auto; background:currentColor; -webkit-mask:${icons.download} center/contain no-repeat; }
:root.univer-mobile .univer-app-materials a.downLoad:hover { background:var(--u-brand)!important; border-color:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile :is(.univer-app-courses,.univer-app-materials) td { overflow-wrap:break-word!important; }

/* Schedule: a day agenda at every width, plus the unchanged source grid on request. */
:root.univer-mobile .univer-app-schedule { display:block!important; width:100%; }
:root.univer-mobile .univer-app-schedule-modes { display:flex; gap:2px; margin:4px 0 12px; padding:3px; width:max-content; max-width:100%; border:1px solid var(--u-line); border-radius:10px; background:#e9eef4; }
:root.univer-mobile #l4 .univer-app-schedule-modes button { min-height:34px!important; padding:6px 16px!important; border:0!important; border-radius:8px!important; background:transparent!important; color:var(--u-muted)!important; font-size:14px!important; font-weight:600!important; }
:root.univer-mobile #l4 .univer-app-schedule-modes button[aria-pressed=true] { background:#fff!important; color:var(--u-brand)!important; box-shadow:0 1px 3px rgba(16,40,70,.12); }
:root.univer-mobile .univer-app-days { display:flex!important; flex-wrap:wrap; gap:6px; margin:4px 0 16px!important; }
:root.univer-mobile #l4 .univer-app-days button { display:inline-flex; align-items:center; gap:8px; min-height:36px!important; padding:6px 12px!important; border:1px solid var(--u-line)!important; border-radius:6px!important; background:#fff!important; color:var(--u-ink)!important; font-size:14px!important; font-weight:500!important; }
:root.univer-mobile #l4 .univer-app-days button:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; }
:root.univer-mobile #l4 .univer-app-days button[aria-pressed=true] { background:var(--u-brand)!important; border-color:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile .univer-app-days button:last-child { order:-1; }
:root.univer-mobile .univer-app-day-count { min-width:20px; padding:0 6px; box-sizing:border-box; border-radius:999px; background:var(--u-surface-2); color:var(--u-muted); font-size:var(--u-fs-caption)!important; font-weight:700; line-height:20px; text-align:center; }
:root.univer-mobile .univer-app-days button[aria-pressed=true] .univer-app-day-count { background:rgba(255,255,255,.22); color:#fff; }
/* All days: up to six columns, so a full-width window shows the week in one row; narrower windows reflow. */
:root.univer-mobile .univer-app-schedule[data-all-days=true] { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(220px,1fr)); gap:12px; align-items:start; }
:root.univer-mobile .univer-app-schedule-modes, :root.univer-mobile .univer-app-days { grid-column:1/-1; }
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-days { margin:0!important; }
:root.univer-mobile .univer-app-day { min-width:0; overflow:hidden; background:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-day h3 { margin:0!important; padding:10px 14px!important; background:var(--u-surface-2); border-bottom:1px solid var(--u-line); color:var(--u-ink)!important; font-size:var(--u-fs-strong)!important; font-weight:600; }
:root.univer-mobile .univer-app-lesson { margin:0!important; padding:12px 14px!important; background:#fff!important; border:0!important; border-bottom:1px solid #edf1f5!important; border-radius:0!important; }
:root.univer-mobile .univer-app-lesson:last-child { border-bottom:0!important; }
:root.univer-mobile .univer-app-lesson time { display:inline-block; padding:2px 8px!important; border-radius:6px!important; background:var(--u-teal-soft)!important; color:var(--u-teal)!important; font-size:var(--u-fs-meta)!important; font-weight:700!important; font-variant-numeric:tabular-nums; }
:root.univer-mobile .univer-app-lesson p { margin:4px 0 0!important; font-size:var(--u-fs-body)!important; line-height:1.45!important; color:var(--u-muted); }
:root.univer-mobile .univer-app-lesson p.univer-app-subject { margin-top:6px!important; color:var(--u-ink)!important; font-size:var(--u-fs-strong)!important; font-weight:600!important; line-height:1.35!important; }
:root.univer-mobile .univer-app-lesson a.univer-app-page-link { margin-top:8px!important; min-height:32px!important; padding:4px 10px!important; font-size:var(--u-fs-meta)!important; box-shadow:none; }
:root.univer-mobile .univer-app-no-classes { margin:0!important; padding:12px 14px!important; background:transparent!important; border:0!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; }
/* A free day in All days is one compact line instead of a tall empty card. */
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-day:has(>.univer-app-no-classes) { display:flex; align-items:center; flex-wrap:wrap; background:var(--u-surface-2); box-shadow:none; }
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-day:has(>.univer-app-no-classes) h3 { flex:1 1 auto; border-bottom:0; background:none; color:var(--u-muted)!important; }
:root.univer-mobile .univer-app-schedule[data-all-days=true] .univer-app-day:has(>.univer-app-no-classes)>.univer-app-no-classes { flex:0 0 auto; padding:10px 14px 10px 0!important; }
/* Current and next classes of today; the inset bar on the left marks that state, not decoration. */
:root.univer-mobile .univer-app-lesson.univer-app-lesson-now { background:var(--u-brand-soft)!important; box-shadow:inset 3px 0 0 var(--u-brand); }
:root.univer-mobile .univer-app-lesson.univer-app-lesson-next { box-shadow:inset 3px 0 0 var(--u-brand-line); }
:root.univer-mobile .univer-app-lesson-badge { display:inline-block; margin-left:8px; padding:1px 8px; border-radius:999px; background:var(--u-brand); color:#fff; font-size:var(--u-fs-caption); font-weight:700; vertical-align:1px; }
:root.univer-mobile .univer-app-lesson-next .univer-app-lesson-badge { background:var(--u-surface-2); color:var(--u-brand); box-shadow:inset 0 0 0 1px var(--u-brand-line); }
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
:root.univer-mobile .univer-app-now-title { color:var(--u-muted); font-size:var(--u-fs-meta); font-weight:600; }
:root.univer-mobile .univer-app-now[data-state=now] .univer-app-now-title { color:var(--u-brand); }
:root.univer-mobile .univer-app-now-count { color:var(--u-ink); font-size:var(--u-fs-title); font-weight:700; font-variant-numeric:tabular-nums; }
:root.univer-mobile .univer-app-now[data-state=now] .univer-app-now-count { color:var(--u-brand); }
:root.univer-mobile .univer-app-now:is([data-state=pause],[data-state=gap],[data-state=first]) .univer-app-now-count { color:var(--u-teal); }
:root.univer-mobile #l4 .univer-app-now p { margin:6px 0 0!important; line-height:1.4!important; }
:root.univer-mobile #l4 .univer-app-now p.univer-app-now-subject { color:var(--u-ink)!important; font-size:var(--u-fs-section)!important; font-weight:600!important; }
:root.univer-mobile #l4 .univer-app-now p.univer-app-now-meta { color:var(--u-muted)!important; font-size:var(--u-fs-body)!important; }
:root.univer-mobile .univer-app-now-progress { height:6px; margin-top:10px; border-radius:999px; background:var(--u-brand-line); overflow:hidden; }
:root.univer-mobile .univer-app-now-progress>span { display:block; height:100%; border-radius:inherit; background:var(--u-brand); }
:root.univer-mobile .univer-app-now-foot { display:flex; align-items:center; justify-content:space-between; gap:8px 12px; flex-wrap:wrap; margin-top:10px; }
:root.univer-mobile .univer-app-now-foot:not(:has(>:not([hidden]))) { display:none; }
:root.univer-mobile .univer-app-now-note { color:var(--u-text); font-size:var(--u-fs-meta); }
:root.univer-mobile #l4 .univer-app-now-action { min-height:32px!important; padding:4px 14px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-brand)!important; font-size:var(--u-fs-meta)!important; font-weight:600!important; cursor:pointer; }
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
:root.univer-mobile.univer-app-schedule-page #l4 .ct.univer-app-schedule-title>a:first-child { flex:1 1 420px; min-height:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:20px!important; font-weight:600!important; line-height:1.35!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct>a.univer-app-print-action { flex:0 0 auto!important; color:var(--u-ink)!important; font-weight:500!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct>a.univer-app-print-action::before { content:''; width:16px; height:16px; background:currentColor; -webkit-mask:${icons.print} center/contain no-repeat; }
:root.univer-mobile table.schedule :is(th,td) { font-size:var(--u-fs-body)!important; white-space:normal!important; min-width:0!important; vertical-align:top!important; padding:8px!important; border:1px solid var(--u-line)!important; }
:root.univer-mobile table.schedule th { background:var(--u-surface-2)!important; color:var(--u-ink)!important; font-weight:600; }
:root.univer-mobile table.schedule td.time { width:100px!important; color:var(--u-teal); font-variant-numeric:tabular-nums; background:var(--u-surface-2)!important; text-align:center!important; vertical-align:middle!important; }
:root.univer-mobile table.schedule tr:first-child>:first-child, :root.univer-mobile table.schedule col:first-child { width:96px!important; min-width:96px!important; }
:root.univer-mobile table.schedule td.time { min-width:96px!important; }
:root.univer-mobile table.schedule tr:first-child>th:not(:first-child) { width:14%; }
:root.univer-mobile table.schedule td.time[data-univer-time]>* { display:none!important; }
:root.univer-mobile table.schedule td.time[data-univer-time]::before { content:attr(data-univer-time); display:block; color:var(--u-teal); font-size:var(--u-fs-meta); font-weight:700; line-height:1.4; white-space:nowrap; }
:root.univer-mobile table.schedule .groups>div { box-sizing:border-box; padding:8px!important; border-radius:8px; }
:root.univer-mobile table.schedule .groups p { margin:0 0 3px!important; padding:0!important; border:0!important; background:none!important; line-height:1.4!important; }
:root.univer-mobile table.schedule :is(p,.groups) { width:auto!important; font-size:var(--u-fs-meta)!important; overflow-wrap:break-word; }
:root.univer-mobile table.schedule .groups p:first-child { color:var(--u-ink); font-size:var(--u-fs-body)!important; font-weight:600; }
:root.univer-mobile #l4 .univer-app-schedule-controls { display:block!important; height:auto!important; min-height:0!important; position:static!important; padding:4px 0 8px!important; clear:both; }
:root.univer-mobile #l4 .univer-app-schedule-controls>div { float:none!important; position:static!important; width:100%!important; height:auto!important; }
:root.univer-mobile #weekSelect { display:block!important; width:100%!important; max-width:420px!important; min-height:40px!important; margin:0!important; font-size:14px!important; font-weight:600; color:var(--u-ink)!important; }
:root.univer-mobile.univer-app-schedule-page #l4 .ct { clear:both; height:auto!important; }

/* Documents, FAQ, GPA, news and the website footer. */
:root.univer-mobile #l4 input.univer-app-catalog-search { display:block; clear:both; width:100%!important; max-width:560px!important; height:42px; margin:4px 0 16px!important; padding-left:38px!important; background:#fff ${icons.search} 12px center/18px no-repeat!important; }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university) { margin:0 0 10px!important; overflow:hidden; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:12px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)>summary { display:flex!important; align-items:center; gap:12px; min-height:0!important; padding:14px 18px!important; color:var(--u-ink)!important; font-size:15px!important; font-weight:600!important; }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)>summary::after { content:''; width:8px; height:8px; margin:-4px 2px 0 auto; flex:0 0 auto; border-right:2px solid var(--u-muted); border-bottom:2px solid var(--u-muted); transform:rotate(45deg); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)[open]>summary::after { margin-top:4px; transform:rotate(-135deg); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university)[open]>summary { border-bottom:1px solid var(--u-line); }
:root.univer-mobile :is(.univer-app-catalog,.univer-app-faq,.univer-app-info,#l5 .univer-app-university):hover>summary { background:var(--u-surface-2); }
:root.univer-mobile #l5 .univer-app-university>div { padding:12px 18px!important; font-size:13px!important; color:var(--u-muted); }
:root.univer-mobile .univer-app-catalog tr.file { margin:0!important; border:0!important; border-bottom:1px solid var(--u-line)!important; border-radius:0!important; padding:12px 18px!important; }
:root.univer-mobile .univer-app-catalog tr.file:last-child { border-bottom:0!important; }
:root.univer-mobile .univer-app-catalog tr.file>td:nth-child(3) { font-weight:600; font-style:normal; color:var(--u-ink); }
:root.univer-mobile .univer-app-catalog tr.file>td:nth-child(3) :is(i,em) { font-style:normal; }
:root.univer-mobile .univer-app-catalog tr.file>td[data-univer-field]::before { color:var(--u-muted)!important; font-size:12px!important; }
:root.univer-mobile .univer-app-faq>table.inner>tbody>tr>:is(td,th) { padding:12px 18px!important; }
:root.univer-mobile .univer-app-news .news { margin:0 0 16px!important; padding:20px 24px!important; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:14px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-news .news>td:first-child { padding-bottom:12px!important; margin-bottom:12px!important; border-bottom:1px solid var(--u-line)!important; }
:root.univer-mobile .univer-app-news .news>td:first-child .date { padding:4px 10px!important; border:0!important; border-radius:8px!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; font-weight:600; }
:root.univer-mobile .univer-app-news .news>td:first-child>div:not(.date) { padding:3px 10px!important; border:0!important; border-radius:999px!important; background:var(--u-danger-soft)!important; color:var(--u-danger)!important; font-size:12px!important; font-weight:600; }
:root.univer-mobile .univer-app-news .news>td:first-child>div:not(.date) * { color:inherit!important; background:none!important; }
:root.univer-mobile .univer-app-news .news>td:nth-child(2) :is(table) { max-width:100%; }
:root.univer-mobile table.univer-app-news { overflow-anchor:none; }
:root.univer-mobile .univer-app-news-body { display:flow-root; position:relative; }
:root.univer-mobile .univer-app-news-body[data-collapsed=true] { max-height:248px!important; overflow:hidden!important; }
:root.univer-mobile .univer-app-news-body[data-collapsed=true]::after { content:''; position:absolute; bottom:0; left:0; right:0; height:36px; pointer-events:none; background:linear-gradient(transparent,#fff); }
:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle { display:inline-flex!important; align-items:center; gap:10px; min-height:38px!important; margin:8px 0 0!important; padding:6px 10px!important; border:0!important; border-radius:6px!important; background:transparent!important; color:var(--u-brand)!important; font:600 14px/1.4 var(--u-font)!important; box-shadow:none!important; }
:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-news button.univer-app-news-toggle:hover { background:var(--u-brand-soft)!important; color:var(--u-brand-strong)!important; }
:root.univer-mobile .univer-app-news-toggle::after { content:''; width:6px; height:6px; border-right:1.5px solid currentColor; border-bottom:1.5px solid currentColor; transform:translateY(-2px) rotate(45deg); }
:root.univer-mobile .univer-app-news-toggle[aria-expanded=true]::after { transform:translateY(2px) rotate(225deg); }
/* Course links keep the grade matrices intact; syllabi form a compact list below their trigger. */
:root.univer-mobile #l4 .univer-app-course-source[hidden], :root.univer-mobile #l4 a[data-univer-syllabus-source][hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-course-heading { display:flex; align-items:flex-start; gap:18px; margin:12px 0 8px; padding-top:16px; border-top:1px solid var(--u-line-strong); text-align:left; }
:root.univer-mobile #l4 .univer-app-course-title { flex:1; min-width:0; }
:root.univer-mobile #l4 .univer-app-course-name, :root.univer-mobile #l4 .univer-app-course-title>.univer-app-attendance-course { display:inline; font-size:20px!important; font-weight:600; line-height:1.4; }
:root.univer-mobile #l4 .univer-app-course-meta { display:flex; flex-wrap:wrap; gap:3px 18px; margin-top:4px; color:var(--u-muted); font-size:var(--u-fs-meta)!important; font-weight:400; }
:root.univer-mobile #l4 .univer-app-course-meta span { font-size:var(--u-fs-meta)!important; }
:root.univer-mobile #l4 button.univer-app-course-syllabus { display:inline-flex; align-items:center; justify-content:center; gap:8px; flex:0 0 auto; min-height:40px!important; padding:9px 14px!important; border:1px solid var(--u-brand-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-brand)!important; font-family:inherit!important; font-size:14px!important; font-weight:600; cursor:pointer; }
:root.univer-mobile #l4 button.univer-app-course-syllabus::before { content:''; width:16px; height:16px; background:currentColor; -webkit-mask:${icons.doc} center/contain no-repeat; }
:root.univer-mobile #l4 button.univer-app-course-syllabus::after { content:''; width:6px; height:6px; margin:-3px 1px 0 5px; border-right:1.5px solid currentColor; border-bottom:1.5px solid currentColor; transform:rotate(45deg); }
:root.univer-mobile #l4 button.univer-app-course-syllabus[aria-expanded=true]::after { margin-top:3px; transform:rotate(225deg); }
:root.univer-mobile #l4 button.univer-app-course-syllabus:hover, :root.univer-mobile #l4 button.univer-app-course-syllabus[aria-expanded=true] { background:var(--u-brand-soft)!important; border-color:var(--u-brand)!important; }
:root.univer-mobile #l4 .univer-app-attendance-course { color:var(--u-brand)!important; text-decoration:underline; text-decoration-thickness:1px; text-underline-offset:3px; }
:root.univer-mobile #l4 .univer-app-attendance-course:hover { color:var(--u-brand-strong)!important; }
/* Lesson names are reading labels; the independent filter controls carry the interaction. */
:root.univer-mobile #l4 a[data-univer-lesson-label][hidden], :root.univer-mobile #l4 .univer-app-attendance-part[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-lesson-label { display:flex; align-items:baseline; flex-wrap:wrap; gap:4px 14px; float:none!important; margin:8px 0 2px; color:var(--u-text)!important; font-size:14px!important; font-weight:400; cursor:text; }
:root.univer-mobile #l4 .univer-app-lesson-label strong { font-size:15px!important; font-weight:600; color:var(--u-ink); }
:root.univer-mobile #l4 .univer-app-lesson-label>span { font-size:13px!important; color:var(--u-muted); }
:root.univer-mobile #l4 .univer-app-attendance-tools { margin:0 0 8px; }
:root.univer-mobile #l4 .univer-app-attendance-courses { margin:0 0 14px; padding:0 0 14px; border-bottom:1px solid var(--u-line); text-align:left; }
:root.univer-mobile #l4 .univer-app-attendance-courses[hidden] { display:none!important; }
:root.univer-mobile #l4 p.univer-app-attendance-course-caption { margin:0 0 8px!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile #l4 .univer-app-attendance-course-list { display:flex; flex-wrap:wrap; gap:6px; }
:root.univer-mobile #l4 .univer-app-attendance-courses button { max-width:100%; min-height:38px!important; padding:8px 14px!important; border:1px solid var(--u-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-brand)!important; font:600 14px/1.4 var(--u-font)!important; text-align:left; white-space:normal; overflow-wrap:anywhere; }
:root.univer-mobile #l4 .univer-app-attendance-courses button:hover { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; }
:root.univer-mobile #l4 .univer-app-attendance-courses button[aria-pressed=true] { background:var(--u-brand)!important; color:#fff!important; border-color:var(--u-brand)!important; }
:root.univer-mobile #l4 .univer-app-attendance-filters { display:flex; flex-wrap:wrap; align-items:center; gap:6px; margin:0; padding:6px; width:fit-content; max-width:100%; box-sizing:border-box; background:#fff; border:1px solid var(--u-line); border-radius:10px; }
:root.univer-mobile #l4 .univer-app-attendance-filter-caption { padding:0 10px; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile #l4 p.univer-app-attendance-filter-note { margin:8px 0 0!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile #l4 .univer-app-attendance-filters button { min-height:38px!important; padding:8px 16px!important; border:1px solid transparent!important; border-radius:7px!important; background:transparent!important; color:var(--u-muted)!important; font-family:inherit!important; font-size:14px!important; font-weight:600!important; cursor:pointer; }
:root.univer-mobile #l4 .univer-app-attendance-filters button:hover { color:var(--u-brand)!important; background:var(--u-surface-2)!important; }
:root.univer-mobile #l4 .univer-app-attendance-filters button[aria-pressed=true] { background:var(--u-brand-soft)!important; color:var(--u-brand-strong)!important; border-color:var(--u-brand-line)!important; }
:root.univer-mobile #l4 tr.univer-app-attendance-owned, :root.univer-mobile #l4 tr.univer-app-attendance-owned>td { display:block!important; box-sizing:border-box; width:100%!important; }
:root.univer-mobile #l4 .univer-app-attendance-summary { display:grid; grid-template-columns:minmax(220px,1fr) minmax(280px,1.5fr); gap:12px 24px; box-sizing:border-box; width:100%; margin:0 0 12px; padding:14px 16px; border:1px solid var(--u-line); border-radius:10px; background:#fff; color:var(--u-text); text-align:left; }
:root.univer-mobile #l4 .univer-app-attendance-summary:not(:has(.univer-app-attendance-overall-block)) { grid-template-columns:1fr; }
:root.univer-mobile #l4 .univer-app-attendance-summary p { display:flex; flex-wrap:wrap; gap:4px 18px; margin:4px 0!important; font-size:14px!important; line-height:1.6!important; }
:root.univer-mobile #l4 .univer-app-attendance-summary strong { min-width:96px; font-weight:600; }
:root.univer-mobile #l4 .univer-app-attendance-summary p.univer-app-attendance-summary-title { margin:0 0 6px!important; font-size:var(--u-fs-meta)!important; font-weight:600; color:var(--u-muted); }
:root.univer-mobile #l4 .univer-app-type-row { display:flex; flex-wrap:wrap; align-items:center; gap:8px 12px; padding:8px 0; border-top:1px solid var(--u-line); }
:root.univer-mobile #l4 .univer-app-attendance-summary-title+.univer-app-type-row { border-top:0; padding-top:2px; }
:root.univer-mobile #l4 .univer-app-type-label { display:inline-flex; align-items:center; gap:8px; min-width:120px; color:var(--u-ink); font-size:14px; font-weight:600; }
:root.univer-mobile #l4 .univer-app-type-label::before { content:''; width:10px; height:10px; border-radius:3px; background:var(--u-brand); }
:root.univer-mobile #l4 .univer-app-type-row[data-kind=seminar] .univer-app-type-label::before { background:var(--u-teal); }
:root.univer-mobile #l4 .univer-app-type-row[data-kind=sro] .univer-app-type-label::before { background:#b45309; }
:root.univer-mobile #l4 .univer-app-type-empty { color:var(--u-muted); font-size:13px; }
:root.univer-mobile #l4 .univer-app-attendance-overall-block { border-right:1px solid var(--u-line); padding-right:24px; }
:root.univer-mobile #l4 p.univer-app-attendance-overall { margin:0 0 8px!important; font-size:var(--u-fs-meta)!important; font-weight:600; color:var(--u-muted); }
:root.univer-mobile #l4 p.univer-app-attendance-overall-value { color:var(--u-ink); font-size:var(--u-fs-section)!important; font-weight:600; }
/* Control periods (РК1, РК2, exam) are score tiles: the number leads, the maximum and a bar give scale. */
:root.univer-mobile #l4 :is(.univer-app-attendance-overview,.univer-app-attendance-filter-note,.univer-app-attendance-overall-value)[hidden] { display:none!important; }
:root.univer-mobile #l4 .univer-app-attendance-scores { display:flex; flex-wrap:wrap; gap:8px; }
:root.univer-mobile #l4 .univer-app-score { position:relative; display:grid; grid-template-columns:auto 1fr; align-items:baseline; column-gap:4px; min-width:104px; box-sizing:border-box; padding:8px 12px 12px; overflow:hidden; border:1px solid var(--u-brand-line); border-radius:10px; background:var(--u-brand-soft); }
:root.univer-mobile #l4 .univer-app-score-label { grid-column:1/-1; color:var(--u-brand); font-size:var(--u-fs-caption); font-weight:700; letter-spacing:.02em; }
:root.univer-mobile #l4 .univer-app-score-value { color:var(--u-ink); font-size:var(--u-fs-display); font-weight:700; line-height:1.2; font-variant-numeric:tabular-nums; }
:root.univer-mobile #l4 .univer-app-score-max { color:var(--u-muted); font-size:var(--u-fs-meta); font-variant-numeric:tabular-nums; }
:root.univer-mobile #l4 .univer-app-score-bar { position:absolute; left:0; right:0; bottom:0; height:4px; background:var(--u-brand-line); }
:root.univer-mobile #l4 .univer-app-score-bar::after { content:''; position:absolute; top:0; bottom:0; left:0; width:var(--univer-score); background:var(--u-brand); }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact { gap:6px; }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact .univer-app-score { display:inline-flex; align-items:baseline; gap:6px; min-width:0; padding:3px 10px; border-radius:999px; }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact .univer-app-score-value { font-size:var(--u-fs-body); }
:root.univer-mobile #l4 .univer-app-attendance-scores-compact .univer-app-score-max { font-size:var(--u-fs-caption); }
/* Course summary at the top of the journal; a course name opens that course. */
:root.univer-mobile #l4 .univer-app-attendance-overview { margin:12px 0 0; padding:2px 16px 4px; background:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); text-align:left; }
:root.univer-mobile #l4 p.univer-app-attendance-overview-title { margin:10px 0 6px!important; color:var(--u-muted); font-size:var(--u-fs-meta)!important; font-weight:600; }
:root.univer-mobile #l4 ul.univer-app-attendance-overview-list { margin:0!important; padding:0!important; list-style:none; }
:root.univer-mobile #l4 ul.univer-app-attendance-overview-list>li { display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:6px 16px; margin:0!important; padding:8px 0!important; border-top:1px solid var(--u-line); list-style:none; }
:root.univer-mobile #l4 button.univer-app-attendance-overview-name { flex:1 1 240px; min-width:0; min-height:32px!important; padding:4px 0!important; border:0!important; background:none!important; color:var(--u-brand)!important; font:600 var(--u-fs-body)/1.4 var(--u-font)!important; text-align:left; white-space:normal; overflow-wrap:anywhere; cursor:pointer; }
:root.univer-mobile #l4 button.univer-app-attendance-overview-name:hover { color:var(--u-brand-strong)!important; text-decoration:underline; text-underline-offset:3px; }
:root.univer-mobile #l4 .univer-app-attendance-overview-empty { color:var(--u-muted); font-size:var(--u-fs-meta); }
/* The class log opens on request; an empty course is one line. */
:root.univer-mobile #l4 button.univer-app-attendance-details-toggle { grid-column:1/-1; justify-self:start; display:inline-flex; align-items:center; gap:8px; min-height:32px!important; margin:0 0 -4px -8px!important; padding:4px 8px!important; border:0!important; border-radius:6px!important; background:transparent!important; color:var(--u-brand)!important; font:600 var(--u-fs-body)/1.4 var(--u-font)!important; cursor:pointer; }
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
:root.univer-mobile #l4 .univer-app-news-tools button { min-height:40px!important; padding:0 14px!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink)!important; font-size:var(--u-fs-body)!important; font-weight:600!important; }
:root.univer-mobile #l4 .univer-app-news-tools button:hover, :root.univer-mobile #l4 .univer-app-news-tools button[aria-pressed=true] { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; color:var(--u-brand)!important; }
:root.univer-mobile #l4 .univer-app-news-tools button.univer-app-news-all { background:var(--u-brand)!important; border-color:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile #l4 .univer-app-news-tools button[hidden] { display:none!important; }
:root.univer-mobile .univer-app-news-count { margin-left:auto; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-news-pager { display:flex; flex-wrap:wrap; align-items:center; gap:6px; margin:4px 0 16px!important; }
:root.univer-mobile .univer-app-news-pager>a { display:inline-flex; align-items:center; justify-content:center; min-width:38px; height:38px; box-sizing:border-box; padding:0 12px; border:1px solid var(--u-line); border-radius:10px; background:#fff; color:var(--u-brand); font-weight:600; text-decoration:none; }
:root.univer-mobile .univer-app-news-pager>a:hover { background:var(--u-brand-soft); border-color:var(--u-brand-line); }
:root.univer-mobile .univer-app-news-pager>a.univer-app-news-current { background:var(--u-brand); border-color:var(--u-brand); color:#fff; }
:root.univer-mobile .univer-app-news-gap { padding:0 5px; color:var(--u-muted); }
:root.univer-mobile .univer-app-news-jump { display:inline-flex; align-items:center; gap:6px; margin-left:auto; }
:root.univer-mobile .univer-app-news-jump input { width:76px; height:38px!important; min-height:38px!important; }
:root.univer-mobile #l4 .univer-app-news-jump button { min-height:38px!important; padding:0 14px!important; border:1px solid var(--u-line-strong)!important; border-radius:8px!important; background:#fff!important; color:var(--u-brand)!important; font-size:var(--u-fs-body)!important; font-weight:600!important; cursor:pointer; }
:root.univer-mobile #l4 .univer-app-news-jump button:hover { background:var(--u-brand-soft)!important; border-color:var(--u-brand-line)!important; }
:root.univer-mobile .univer-app-news-jump-label { color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-news-results { margin:0 0 16px!important; padding:14px 16px; background:var(--u-brand-soft); border:1px solid var(--u-brand-line); border-radius:12px; }
:root.univer-mobile .univer-app-news-results[hidden] { display:none!important; }
:root.univer-mobile .univer-app-news-results h3 { margin:0 0 10px!important; font-size:15px!important; color:var(--u-ink); }
:root.univer-mobile .univer-app-news-status { margin:-4px 0 10px!important; color:var(--u-muted); font-size:13px!important; }
:root.univer-mobile .univer-app-news-hit { margin:0 0 8px; background:#fff; border:1px solid var(--u-line); border-radius:10px; }
:root.univer-mobile .univer-app-news-hit>summary { display:flex; align-items:center; gap:12px; padding:10px 14px; cursor:pointer; }
:root.univer-mobile .univer-app-news-hit>summary time { flex:0 0 auto; padding:2px 8px; border-radius:6px; background:var(--u-brand-soft); color:var(--u-brand); font-size:var(--u-fs-meta); font-weight:700; }
:root.univer-mobile .univer-app-news-hit>summary span { flex:1 1 auto; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; color:var(--u-ink); }
:root.univer-mobile .univer-app-news-hit>summary a { flex:0 0 auto; font-size:13px; font-weight:600; }
:root.univer-mobile .univer-app-news-hit-body { padding:0 14px 12px; max-height:520px; overflow:auto; }
:root.univer-mobile .univer-app-news-hit-body img { max-width:100%; height:auto; }
/* УМКД: the duplicate "view by discipline" entry reloads the same list. */
:root.univer-mobile #l4 a#discipline_btn, :root.univer-mobile #l4 .ts:has(>a#discipline_btn):not(:has(>a:not(#discipline_btn))) { display:none!important; }
/* Schedule: today. */
:root.univer-mobile .univer-app-day.univer-app-today { border-color:var(--u-brand)!important; box-shadow:0 0 0 1px var(--u-brand),var(--u-shadow); }
:root.univer-mobile .univer-app-day.univer-app-today h3 { display:flex; align-items:center; gap:8px; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
:root.univer-mobile .univer-app-today-badge { margin-left:auto; padding:1px 8px; border-radius:999px; background:var(--u-brand); color:#fff; font-size:var(--u-fs-caption); font-weight:700; }
:root.univer-mobile #l4 .univer-app-days button.univer-app-today:not([aria-pressed=true]) { border-color:var(--u-brand)!important; color:var(--u-brand)!important; font-weight:600!important; }
:root.univer-mobile table.schedule th.univer-app-today-col { background:var(--u-brand)!important; color:#fff!important; }
:root.univer-mobile #l4 iframe.univer-app-video { display:none!important; }
:root.univer-mobile .univer-app-video-link { display:inline-flex; align-items:center; gap:8px; min-height:40px; box-sizing:border-box; padding:8px 16px; margin:8px 0; border:1px solid var(--u-line); border-radius:10px; background:#fff; box-shadow:var(--u-shadow); color:var(--u-brand); font-weight:600; text-decoration:none; }
:root.univer-mobile .univer-app-video-link::before { content:'▶'; font-size:11px; }
:root.univer-mobile #l5, :root.univer-mobile #l5 td.center, :root.univer-mobile #copyright, :root.univer-mobile #copyright>tbody>tr>td { background:transparent!important; border:0!important; }
:root.univer-mobile #l5 td.center { padding:8px 32px 32px!important; }
:root.univer-mobile #l5 td.center>#copyright { width:100%!important; margin:0 auto 12px!important; }
:root.univer-mobile #copyright td { padding:0!important; text-align:start!important; }
:root.univer-mobile #copyright td { color:var(--u-muted)!important; font-size:12px!important; }
:root.univer-mobile .univer-app-info[data-univer-info=links] .links a { color:var(--u-brand)!important; }

/* Site dialogs and the login page. */
:root.univer-mobile #simplemodal-container { border:1px solid var(--u-line)!important; border-radius:14px!important; box-shadow:var(--u-shadow-pop)!important; }
:root.univer-mobile .univer-app-modal-toolbar { border-color:var(--u-line)!important; border-radius:14px 14px 0 0!important; }
:root.univer-mobile #auth_modal, :root.univer-mobile #modal_agreement, :root.univer-mobile .modal { max-width:calc(100vw - 36px)!important; max-height:calc(100vh - 36px)!important; overflow:auto!important; box-sizing:border-box!important; }
:root.univer-mobile #modal_agreement .modal-body { max-height:65vh!important; overflow:auto!important; line-height:1.6!important; }
:root.univer-mobile .modal .modal-footer { padding:14px!important; }
:root.univer-mobile #attentionSt { line-height:1.65!important; }
:root.univer-mobile #login_form { box-sizing:border-box!important; padding:28px!important; background:#fff!important; border:1px solid var(--u-line)!important; border-radius:16px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile #login_form :is(td,p,strong,b,label,span) { text-align:start!important; }
:root.univer-mobile #login_frm :is(td,p,strong,li) { font-size:14px!important; line-height:1.6!important; }
:root.univer-mobile #login_frm :is(input[type=text],input[type=password]) { min-height:42px!important; background:#fff!important; }
:root.univer-mobile #login_frm :is(input[type=submit],button[type=submit]) { width:100%; min-height:44px!important; padding:10px 22px!important; border:0!important; border-radius:8px!important; background:var(--u-brand)!important; color:#fff!important; font-weight:600!important; }
:root.univer-mobile #login_frm :is(input[type=submit],button[type=submit]):hover { background:var(--u-brand-strong)!important; }

/* Printed documents open in their own viewer as a paper sheet. */
:root.univer-mobile.univer-app-document, :root.univer-mobile.univer-app-document body { background:var(--u-bg)!important; }
:root.univer-mobile.univer-app-document body { box-sizing:border-box; max-width:1040px!important; margin:24px auto!important; padding:40px 48px!important; background:#fff!important; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); color:#1d2b39!important; }
:root.univer-mobile.univer-app-document .noprint a { border-radius:8px; }
:root.univer-mobile.univer-app-document .univer-app-table-scroll { box-shadow:none; border-radius:8px!important; }
:root.univer-mobile.univer-app-document .univer-app-table-scroll .univer-app-table-scroll { border:0!important; margin:0!important; }
:root.univer-mobile.univer-app-document .univer-app-pairs>tbody>tr { margin:0!important; border:0!important; border-bottom:1px solid var(--u-line)!important; border-radius:0!important; box-shadow:none!important; }
:root.univer-mobile.univer-app-document .univer-app-pairs>tbody>tr>td { border:0!important; }

@media (min-width:900px) {
 :root.univer-mobile .univer-app-cards>thead { display:table-header-group!important; }
 :root.univer-mobile .univer-app-cards>tfoot { display:table-footer-group!important; }
 :root.univer-mobile #l4 table.univer-app-sort-collapsed .univer-app-sort-head { display:table-row!important; }
 :root.univer-mobile #l4 .univer-app-cards :is(td,th) { overflow-wrap:normal!important; word-break:normal!important; }
 :root.univer-mobile #l4 .univer-app-cards .univer-app-data-field { min-width:8ch!important; }
 :root.univer-mobile .univer-app-cards .univer-app-card-row { box-shadow:none; }
 :root.univer-mobile .univer-app-cards td:has(>a.univer-app-action-link:only-child) { width:1%; white-space:nowrap; }
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
.univer-app-select-group { padding:8px 12px 4px; color:#5b6b7c; font-size:12px; font-weight:600; }
.univer-app-select-option.univer-app-select-active { background:#e9f1fb; }
.univer-app-select-option[aria-selected=true] { color:#1b5fa6; font-weight:600; }
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
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) .univer-app-quick a { transition:transform 180ms var(--u-ease-out); }
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) .univer-app-quick a:hover { transform:translateY(-2px); }
 :root.univer-mobile:not(.univer-app-still):not(.univer-app-keyboard) .univer-app-quick a:active { transform:translateY(1px) scale(.98); transition-duration:140ms; }
}
@media (prefers-reduced-motion:reduce) {
 :root.univer-mobile *, :root.univer-mobile *::before, :root.univer-mobile *::after { animation:none!important; scroll-behavior:auto!important; }
 :root.univer-mobile :is(a,button,summary,label,select,textarea,input) { transition:none!important; }
 :root.univer-mobile .univer-app-quick a:hover { transform:none!important; }
 :root.univer-mobile :is(button:not(.univer-app-unpin),input[type=button],input[type=submit],input[type=reset],.univer-app-quick a):active { transform:none!important; }
}
`;
            (doc.head || doc.documentElement).appendChild(style);
        }
        apply(document);
        // Same-origin frames only. Polling sees late frames without rewriting page contents.
        Array.from(document.querySelectorAll('iframe,frame')).forEach(function (frame) {
            try { if (frame.contentWindow.location.origin === location.origin && frame.contentDocument) apply(frame.contentDocument); } catch (_) { }
        });
    };
})();
