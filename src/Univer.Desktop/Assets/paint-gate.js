/* Runs at document creation. Opacity keeps the site's visible-form detection and handlers intact. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerPaintV1) return;
    // shownAt: when this document was first uncovered, on the page's clock (QA compares it with DOMContentLoaded).
    var active = false, pending = false, failed = false, saved = null, sheet = null, shownAt = 0;
    var observer = null, watching = false, queued = false, dirty = false, frameId = 0, timerId = 0;
    var LOGIN_PATH = /^\/user\/login(?:moodle)?\/?$/i;
    var OBSERVER_OPTIONS = {childList:true, subtree:true, attributes:true, attributeFilter:['style','class','hidden']};
    function pauseObserver() {
        if (observer && watching) { observer.disconnect(); watching = false; }
    }
    function syncObserver() {
        if (!observer) return;
        var need = pending || (active && !failed);
        if (need && !watching) { observer.observe(document, OBSERVER_OPTIONS); watching = true; }
        else if (!need && watching) { observer.disconnect(); watching = false; }
    }
    function clearScheduled() {
        if (frameId && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(frameId);
        if (timerId) clearTimeout(timerId);
        frameId = 0; timerId = 0; queued = false; dirty = false;
    }
    function begin() {
        if (failed) return;
        clearScheduled();
        if (!sheet && 'adoptedStyleSheets' in document && typeof CSSStyleSheet !== 'undefined' && CSSStyleSheet.prototype.replaceSync) {
            sheet = new CSSStyleSheet(); sheet.replaceSync('html:root{opacity:0!important;pointer-events:none!important}');
            document.adoptedStyleSheets = document.adoptedStyleSheets.concat(sheet);
        }
        var root = document.documentElement;
        if (root && !saved) saved = {opacity:root.style.getPropertyValue('opacity'), priority:root.style.getPropertyPriority('opacity'), pointer:root.style.getPropertyValue('pointer-events'), pointerPriority:root.style.getPropertyPriority('pointer-events')};
        if (root) {
            var needOpacity = root.style.getPropertyValue('opacity') !== '0';
            var needPointer = root.style.getPropertyValue('pointer-events') !== 'none';
            if (needOpacity || needPointer) {
                pauseObserver();
                if (needOpacity) root.style.setProperty('opacity', '0', 'important');
                if (needPointer) root.style.setProperty('pointer-events', 'none', 'important');
            }
        }
        var first = !pending;
        pending = true;
        syncObserver();
        if (first && typeof __univerNative === 'function') __univerNative('univer-busy');
    }
    function hiddenAttr(node) {
        return !node || node.hidden || !!(node.closest && node.closest('[hidden]'));
    }
    function visible(node) {
        if (hiddenAttr(node) || !node.getClientRects().length) return false;
        var style = getComputedStyle(node); return style.display !== 'none' && style.visibility !== 'hidden';
    }
    function knownElements(loginPage) {
        var list = [];
        var auth = document.getElementById('auth_modal'); if (auth) list.push(auth);
        var notice = document.getElementById('attentionSt'); if (notice) list.push(notice);
        var agree = document.getElementById('modal_agreement'); if (agree) list.push(agree);
        if (loginPage) { var login = document.getElementById('login_frm'); if (login) list.push(login); }
        return list;
    }
    function touches(records, targets) {
        for (var i = 0; i < records.length; i++) {
            var r = records[i], t = r.target;
            if (r.type === 'attributes') {
                for (var j = 0; j < targets.length; j++) {
                    var el = targets[j];
                    if (t === el || el.contains(t) || t.contains(el)) return true;
                }
            } else if (r.type === 'childList') {
                for (var k = 0; k < targets.length; k++) {
                    var item = targets[k];
                    if (t === item || item.contains(t)) return true;
                }
                for (var n = 0; n < r.addedNodes.length; n++) {
                    var added = r.addedNodes[n];
                    if (added.nodeType !== 1) continue;
                    for (var m = 0; m < targets.length; m++) {
                        if (added === targets[m] || added.contains(targets[m])) return true;
                    }
                }
            }
        }
        return false;
    }
    function knownPending(fromObserver) {
        if (!active || failed) return false;
        var loginPage = LOGIN_PATH.test(location.pathname);
        var login = loginPage ? document.getElementById('login_frm') : null;
        var authBox = document.getElementById('auth_modal');
        var auth = authBox && !hiddenAttr(authBox) ? authBox.querySelector('#modal_auth_frm') : null;
        var notice = document.getElementById('attentionSt');
        if (notice && (notice.tagName !== 'FORM' || hiddenAttr(notice))) notice = null;
        var agreeBox = document.getElementById('modal_agreement');
        var agree = agreeBox && !hiddenAttr(agreeBox) ? agreeBox.querySelector('#agree_frm') : null;
        if (!loginPage && !auth && !notice && !agree) return false;
        if (fromObserver && loginPage && (!login || hiddenAttr(login)) && !auth && !notice && !agree) return false;
        var auto = window.__univerAutomationV1;
        if (auto) {
            if (typeof auto.hasPending === 'function') return auto.hasPending(!!fromObserver);
            var state = auto.probe();
            return state.trusted && (state.hasPendingForm || state.loginPage);
        }
        if (login) return !hiddenAttr(login);
        if (auth && visible(auth)) return true;
        if (notice) {
            try {
                var url = new URL(notice.action, location.href), label = notice.querySelector('label[for=check]');
                if (url.origin === location.origin && /^\/student\/attentionStudents\/\d+\/?$/i.test(url.pathname) && label && /i confirm that i have read this notice|я подтверждаю,? что проч[её]л данное уведомление|мен бұл хабарламаны оқығанымды растаймын/i.test(label.textContent) && visible(notice)) return true;
            } catch (_) { }
        }
        return !!(agree && agree.querySelector('#iconfirm') && agree.querySelector('.btn-confirm[onclick*="agree()"]') && /прочитал договор/.test((agree.textContent || '').replace(/\s+/g, ' ').toLowerCase()) && visible(agree));
    }
    function finish(failure) {
        clearScheduled();
        failed = !!failure;
        if (sheet) { document.adoptedStyleSheets = document.adoptedStyleSheets.filter(function (value) { return value !== sheet; }); sheet = null; }
        var root = document.documentElement;
        if (root && saved) {
            pauseObserver();
            if (saved.opacity) root.style.setProperty('opacity', saved.opacity, saved.priority); else root.style.removeProperty('opacity');
            if (saved.pointer) root.style.setProperty('pointer-events', saved.pointer, saved.pointerPriority); else root.style.removeProperty('pointer-events');
        }
        // Reveal immediately: navigation and F5 must not add a second, decorative loading phase.
        if (pending && !shownAt) shownAt = performance.now();
        saved = null; pending = false;
        syncObserver();
    }
    function documents() {
        var result = [document];
        for (var i = 0; i < result.length && result.length < 8; i++)
            for (var frame of result[i].querySelectorAll('iframe,frame')) {
                try { if (frame.contentWindow.location.origin === location.origin && frame.contentDocument && !result.includes(frame.contentDocument)) result.push(frame.contentDocument); } catch (_) { }
                if (result.length >= 8) break;
            }
        return result;
    }
    function flush() {
        if (frameId && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(frameId);
        if (timerId) clearTimeout(timerId);
        frameId = 0; timerId = 0;
        var again = dirty;
        queued = false; dirty = false;
        if (again && !pending && active && !failed && knownPending(true)) begin();
    }
    function scheduleTrailing() {
        queued = true;
        if (typeof requestAnimationFrame === 'function') {
            frameId = requestAnimationFrame(flush);
            timerId = setTimeout(flush, 100);
        } else {
            timerId = setTimeout(flush, 16);
        }
    }
    function onMutations(records) {
        if (!document || !document.documentElement || failed) return;
        if (pending) {
            var root = document.documentElement;
            if (!saved || root.style.getPropertyValue('opacity') !== '0' || root.style.getPropertyValue('pointer-events') !== 'none') begin();
            return;
        }
        if (!active) return;
        var targets = knownElements(LOGIN_PATH.test(location.pathname));
        if (!targets.length || !touches(records, targets)) return;
        if (queued) { dirty = true; return; }
        if (knownPending(true)) { begin(); return; }
        scheduleTrailing();
    }
    window.__univerPaintV1 = {
        begin:begin,
        configure:function (value) { if (value && !active) failed = false; active = !!value; if (active && knownPending(false)) begin(); syncObserver(); },
        complete:function (failure, readable, viewer, language) {
            documents().forEach(function (doc) {
                var win = doc.defaultView;
                if (doc.readyState === 'loading' || !win.__univerPaintV1) return;
                if (win.__univerMobileV1) win.__univerMobileV1.setEnabled(readable, language);
                win.__univerPaintV1.finish(failure);
            });
            return 'paint-ready';
        },
        finish:finish,
        get pending() { return pending; },
        get shown() { return shownAt; }
    };
    observer = new MutationObserver(onMutations);
    begin();
})();
