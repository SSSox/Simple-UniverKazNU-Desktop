/* Runs at document creation. Opacity keeps the site's visible-form detection and handlers intact. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerPaintV1) return;
    var active = false, pending = false, failed = false, saved = null, sheet = null;
    function begin() {
        if (failed) return;
        if (!sheet && 'adoptedStyleSheets' in document && typeof CSSStyleSheet !== 'undefined' && CSSStyleSheet.prototype.replaceSync) {
            sheet = new CSSStyleSheet(); sheet.replaceSync('html:root{opacity:0!important;pointer-events:none!important}');
            document.adoptedStyleSheets = document.adoptedStyleSheets.concat(sheet);
        }
        var root = document.documentElement;
        if (root && !saved) saved = {opacity:root.style.getPropertyValue('opacity'), priority:root.style.getPropertyPriority('opacity'), pointer:root.style.getPropertyValue('pointer-events'), pointerPriority:root.style.getPropertyPriority('pointer-events')};
        if (root) {
            if (root.style.getPropertyValue('opacity') !== '0') root.style.setProperty('opacity', '0', 'important');
            if (root.style.getPropertyValue('pointer-events') !== 'none') root.style.setProperty('pointer-events', 'none', 'important');
        }
        if (!pending) {
            pending = true;
            if (typeof window.__univerNativeNotify === 'function') window.__univerNativeNotify('univer-busy');
            else if (window.CefSharp) CefSharp.PostMessage('univer-busy');
        }
    }
    function visible(node) {
        if (!node || !node.getClientRects().length || node.closest('[hidden]')) return false;
        var style = getComputedStyle(node); return style.display !== 'none' && style.visibility !== 'hidden';
    }
    function knownPending() {
        if (!active || failed) return false;
        if (window.__univerAutomationV1 && (/^\/user\/login(?:moodle)?\/?$/i.test(location.pathname) || document.querySelector('#auth_modal #modal_auth_frm,form#attentionSt,#modal_agreement #agree_frm'))) {
            var state = window.__univerAutomationV1.probe();
            return state.trusted && (state.hasPendingForm || state.loginPage);
        }
        if (/^\/user\/login(?:moodle)?\/?$/i.test(location.pathname) && document.querySelector('#login_frm')) return true;
        var auth = document.querySelector('#auth_modal #modal_auth_frm');
        if (auth && visible(auth)) return true;
        var notice = document.querySelector('form#attentionSt');
        if (notice && visible(notice)) {
            try {
                var url = new URL(notice.action, location.href), label = notice.querySelector('label[for=check]');
                if (url.origin === location.origin && /^\/student\/attentionStudents\/\d+\/?$/.test(url.pathname) && label && /i confirm that i have read this notice|я подтверждаю,? что проч[её]л данное уведомление|мен бұл хабарламаны оқығанымды растаймын/i.test(label.textContent)) return true;
            } catch (_) { }
        }
        var agree = document.querySelector('#modal_agreement #agree_frm');
        return !!(agree && visible(agree) && agree.querySelector('#iconfirm') && agree.querySelector('.btn-confirm[onclick*="agree()"]') && /прочитал договор/.test((agree.textContent || '').replace(/\s+/g, ' ').toLowerCase()));
    }
    function finish(failure) {
        failed = !!failure;
        if (sheet) { document.adoptedStyleSheets = document.adoptedStyleSheets.filter(function (value) { return value !== sheet; }); sheet = null; }
        var root = document.documentElement;
        if (root && saved) {
            if (saved.opacity) root.style.setProperty('opacity', saved.opacity, saved.priority); else root.style.removeProperty('opacity');
            if (saved.pointer) root.style.setProperty('pointer-events', saved.pointer, saved.pointerPriority); else root.style.removeProperty('pointer-events');
        }
        // Reveal immediately: navigation and F5 must not add a second, decorative loading phase.
        saved = null; pending = false;
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
    window.__univerPaintV1 = {
        begin:begin,
        configure:function (value) { if (value && !active) failed = false; active = !!value; if (active && knownPending()) begin(); },
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
        get pending() { return pending; }
    };
    begin();
    new MutationObserver(function () {
        if (!document || !document.documentElement) return;
        if (pending) begin();
        else if (knownPending()) begin();
    }).observe(document, {childList:true, subtree:true, attributes:true, attributeFilter:['style','class','hidden']});
})();
