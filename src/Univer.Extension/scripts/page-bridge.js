/* The page's own world (MAIN) for the extension: univer-automation.js needs the site's jQuery and agree(), which the
   extension's isolated world cannot see. The isolated driver asks through DOM events with string details; this side
   answers only the read notice and the agreement calls. Sign-in is not offered: the extension holds no password.
   Everything here is as open to the site's scripts as the module itself is, so the driver treats every answer as data
   (a forged one can at most use up its confirmation budget). The build puts the module where the marker stands. */
(function () {
    'use strict';
    var ORIGIN = 'https://univer.kaznu.kz';
    if (location.origin !== ORIGIN) return;
    // Taken before any page script runs, so a page that replaces them later does not change what this bridge does.
    var apply = Reflect.apply, dispatch = EventTarget.prototype.dispatchEvent, listen = EventTarget.prototype.addEventListener;
    var Custom = CustomEvent, stringify = JSON.stringify, parse = JSON.parse, isArray = Array.isArray, root = document;
    function emit(type, detail) { apply(dispatch, root, [new Custom(type, { detail: detail })]); }
    // The module's only message to the app is the outcome of the agreement's own AJAX (and of a reauthentication, which
    // the extension never starts).
    var __univerPageSend = function (message) {
        if (typeof message === 'string' && message.length <= 512 && message.indexOf('univer-ajax|agreement|') === 0) emit('univer-ext-native', message);
    };
/*@MODULES@*/
    var automation = window.__univerAutomationV1;
    if (!automation || window !== window.top) return;
    // No prototype: a name such as "constructor" finds nothing.
    var methods = Object.create(null);
    ['probe', 'prepareNotice', 'submitNotice', 'checkAgreement', 'submitAgreement'].forEach(function (name) { methods[name] = automation[name]; });
    apply(listen, root, ['univer-ext-call', function (event) {
        var detail = event.detail, request;
        if (typeof detail !== 'string' || detail.length > 2048) return;
        try { request = parse(detail); } catch (_) { return; }
        if (!request || typeof request.id !== 'string' || request.id.length > 64 || typeof request.method !== 'string' ||
            typeof methods[request.method] !== 'function' || !isArray(request.args) || request.args.length > 2) return;
        for (var i = 0; i < request.args.length; i++) if (typeof request.args[i] !== 'string' || request.args[i].length > 256) return;
        var value = null;
        try {
            value = apply(methods[request.method], automation, request.args);
            value = request.method === 'probe' ? stringify(value) : String(value);
        } catch (_) { value = null; }
        emit('univer-ext-answer', stringify({ id: request.id, value: value }));
    }]);
})();
