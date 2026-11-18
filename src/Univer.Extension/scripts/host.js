/* The part of the Windows app that drives the shared page scripts (AutomationController: ApplyView, RevealAsync; the
   pins, the grade summary), for the extension. Last of the isolated-world scripts, so every page script is defined. */
(function () {
    'use strict';
    var ext = globalThis.__univerExtV1;
    if (!ext || location.origin !== ext.origin || globalThis.__univerHostV1) return;
    var api = globalThis.chrome;
    // The printed documents the Windows app opens in its own viewer (SitePolicy.PrintPath); here they stay in their tab.
    var PRINT = /^\/student\/(?:iup(?:\/\d+)*\/?|transcript(?:\/\d+)?\/?|recbook(?:\/\d+)?\/?|interactive\/syllabus\/print\/\d+\/\d+\/?|myschedule\/print\/\d+\/\d+\/?)$/i;
    var viewer = PRINT.test(location.pathname);
    var DEFAULTS = { readable: true, theme: 'system', motion: true, confirm: true };
    var settings = null, home = { pins: [], order: [], hidden: [] }, stored = 'ru', language = null;
    var parsed = document.readyState !== 'loading', revealed = false, early = false;
    var dark = matchMedia('(prefers-color-scheme: dark)');

    function clean(value) {
        var result = {};
        Object.keys(DEFAULTS).forEach(function (key) { result[key] = value && typeof value[key] === typeof DEFAULTS[key] ? value[key] : DEFAULTS[key]; });
        if (!/^(system|light|dark)$/.test(result.theme)) result.theme = DEFAULTS.theme;
        return result;
    }
    function list(value) { return Array.isArray(value) ? value : []; }
    function theme() { return settings.theme === 'dark' || (settings.theme === 'system' && dark.matches) ? 'dark' : 'light'; }
    // The site marks its language by leaving that flag without a link (/lang/change/xx/) in the header; the flags are
    // /img/xx.png or /content/icons/xx.png depending on the page (univer-layout.js finds them the same way).
    function pageLanguage() {
        var header = document.querySelector('.header');
        if (!header) return null;
        var flags = header.querySelectorAll('img');
        for (var i = 0; i < flags.length; i++) {
            var code = /\/(kz|ru|en)\.png$/i.exec(flags[i].getAttribute('src') || '');
            if (code && !flags[i].closest('a[href*="/lang/change/"]')) return code[1].toLowerCase();
        }
        return null;
    }
    function currentLanguage() {
        if (!language && parsed) {
            language = pageLanguage();
            if (language && language !== stored && window === window.top) {
                stored = language;
                try { api.storage.local.set({ language: language }); } catch (_) { }
            }
        }
        return language || stored;
    }
    // Before the page is parsed: the reading layout in the dark theme starts dark instead of on the site's white.
    function begin() {
        if (early || !settings) return;
        early = true;
        try { globalThis.__univerPaintV1 && globalThis.__univerPaintV1.configure(false); } catch (_) { }
        try {
            // The page scripts read these as window.X; in Firefox a globalThis write would stay in this script's sandbox.
            window.__univerTheme = theme();
            if (globalThis.__univerThemeV1) globalThis.__univerThemeV1.early(settings.readable);
        } catch (_) { }
    }
    function apply() {
        if (!settings || !parsed) return;
        var lang = currentLanguage();
        try {
            window.__univerMotion = settings.motion;
            window.__univerTheme = theme();
            if (globalThis.__univerShortcutsV1) globalThis.__univerShortcutsV1.configure(list(home.pins), list(home.order), list(home.hidden));
        } catch (_) { }
        try { if (globalThis.__univerDesktop) globalThis.__univerDesktop.apply(settings.readable, viewer, lang); } catch (_) { }
        if (revealed) return;
        revealed = true;
        try { if (globalThis.__univerPaintV1) globalThis.__univerPaintV1.complete(false, settings.readable, viewer, lang); } catch (_) { }
        var gate = globalThis.__univerPaintV1;
        if (gate && gate.pending) ext.uncover(); else ext.shown();
    }

    // The Windows app asks the page again on every tick (200 ms); a repeated apply only refits an open dialog.
    var tick = 0;
    function schedule() {
        clearInterval(tick);
        if (!document.hidden) tick = setInterval(apply, 400);
    }
    document.addEventListener('visibilitychange', function () { schedule(); if (!document.hidden) apply(); });
    if (!parsed) document.addEventListener('DOMContentLoaded', function () { parsed = true; apply(); schedule(); }, { once: true });
    dark.addEventListener('change', function () { if (settings && settings.theme === 'system') apply(); });

    function report(action) {
        var sheet = globalThis.__univerReportV1;
        if (!sheet) return;
        var opening = sheet.opened();
        if (action === 'print' || action === 'pdf') {
            // Print and PDF are the browser's print dialog («Save as PDF» is one of its printers). While the sheet is
            // open, printing the page prints the sheet alone.
            setTimeout(function () {
                var ok = true;
                try { window.print(); } catch (_) { ok = false; }
                sheet.done({ action: 'print', ok: ok, name: '', opening: opening });
            }, 0);
        } else sheet.done({ action: action, ok: false, name: '', opening: opening });
    }
    ext.listen(function (message) {
        if (message.indexOf('univer-pin:') === 0) {
            if (window === window.top) api.runtime.sendMessage({ type: 'pin', payload: message.slice(11) }).catch(function () { });
        } else if (message.indexOf('univer-report:') === 0) report(message.slice(14));
        else if (message.indexOf('univer-download-all:') === 0) {
            if (globalThis.__univerExtDownloadsV1) globalThis.__univerExtDownloadsV1.start(message.slice(20));
        }
        // univer-busy (the app's loading cover), univer-dom and univer-notices: (the app's bell) have no browser part.
    });

    api.storage.onChanged.addListener(function (changes, area) {
        if (area !== 'local') return;
        if (changes.settings) settings = clean(changes.settings.newValue);
        if (changes.home) home = changes.home.newValue || { pins: [], order: [], hidden: [] };
        if (changes.settings || changes.home) apply();
    });
    api.storage.local.get(['settings', 'home', 'language']).then(function (value) {
        settings = clean(value.settings);
        if (value.home) home = value.home;
        if (/^(ru|kz|en)$/.test(value.language)) stored = value.language;
    }, function () { settings = clean(null); }).then(function () {
        begin();
        if (parsed) { apply(); schedule(); }
    });

    Object.defineProperty(globalThis, '__univerHostV1', { value: Object.freeze({ language: function () { return currentLanguage(); } }) });
})();
