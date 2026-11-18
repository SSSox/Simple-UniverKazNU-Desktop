/* First script of the extension in the site's pages. It runs in the extension's isolated world, which the site's own
   scripts cannot read, before the page scripts shared with the Windows app. It gives them their sender (the Windows app
   passes __univerNative) and a deadline: whatever happens to the scripts after it, the page is never left covered. */
(function () {
    'use strict';
    var ORIGIN = 'https://univer.kaznu.kz';
    if (location.origin !== ORIGIN || globalThis.__univerExtV1) return;
    var queue = [], handler = null, done = false, timer = 0;
    // The grade summary's PNG and «Copy picture» need a capture of the tab, which only an extension with every site's
    // permission may take. The sheet reads this before it draws its buttons. Values the page scripts read as window.X are
    // written to window: in Firefox globalThis is the content script's own sandbox, which window.X does not see.
    window.__univerReportPictures = false;
    function send(message) {
        if (typeof message !== 'string' || message.length > 262144) return;
        if (!handler) { if (queue.length < 64) queue.push(message); return; }
        try { handler(message); } catch (_) { }
    }
    function gateSheet(sheet) {
        try { return /^html:root\s*\{\s*opacity:\s*0\s*!important/.test(sheet.cssRules[0].cssText); } catch (_) { return false; }
    }
    // Takes off what the paint gate put on, without relying on the gate: in Firefox a content script may not be able to
    // remove an adopted sheet, and a gate that throws there would otherwise keep the page transparent.
    function uncover() {
        if (done) return;
        done = true; clearTimeout(timer);
        try { if (globalThis.__univerPaintV1 && globalThis.__univerPaintV1.pending) globalThis.__univerPaintV1.finish(true); } catch (_) { }
        var root = document.documentElement;
        try {
            if (root && root.style.getPropertyValue('opacity') === '0') root.style.removeProperty('opacity');
            if (root && root.style.getPropertyValue('pointer-events') === 'none') root.style.removeProperty('pointer-events');
        } catch (_) { }
        try {
            var sheets = document.adoptedStyleSheets;
            if (sheets && sheets.some(gateSheet)) document.adoptedStyleSheets = sheets.filter(function (sheet) { return !gateSheet(sheet); });
        } catch (_) { }
    }
    // host.js normally uncovers the page right after the layout at DOMContentLoaded; this is only for when it cannot.
    function arm() { clearTimeout(timer); if (!done) timer = setTimeout(uncover, 1500); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', arm, { once: true }); else arm();
    // A page that is still parsing is drawn as it comes after 5 s, as the browser would draw it without the extension.
    setTimeout(uncover, 5000);
    globalThis.__univerExtV1 = Object.freeze({
        origin: ORIGIN,
        send: send,
        listen: function (next) { handler = next; queue.splice(0).forEach(send); },
        // The page was uncovered the normal way: the deadline has nothing left to do.
        shown: function () { done = true; clearTimeout(timer); },
        uncover: uncover
    });
})();
