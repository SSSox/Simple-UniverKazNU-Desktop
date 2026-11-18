/* The read notice and the agreement, confirmed for the student as the Windows app does (AutomationController
   NoticeAsync/AgreementAsync), when the popup's «Подтверждать уведомление и соглашение» is on. The forms and clicks are
   univer-automation.js's, in the page's world (page-bridge.js); this side decides. Every send is reserved first in the
   background, which keeps one budget for all tabs: a form already sent is not sent again, and at most three are sent a
   minute. Without a reservation nothing is clicked. A page the student has started answering is left to the student,
   and a form that is still there after 25 s stays on screen for the student too: the page is never reloaded and a POST
   is never repeated. */
(function () {
    'use strict';
    var ext = globalThis.__univerExtV1;
    if (!ext || location.origin !== ext.origin || window !== window.top || globalThis.__univerExtDriverV1) return;
    var api = globalThis.chrome;
    var POLL = 400, CALL_LIMIT = 3000, OUTCOME = 25000, IDLE = 30000, ABSENT = 2000;
    var FORMS = 'form#attentionSt, form[action*="/student/attentionStudents/"], #modal_agreement';
    // When this page began loading, compared with the background's Date.now(): the renderer's timeOrigin can drift from the
    // wall clock in a long-lived process, so the earlier of the two is taken (earlier only ever frees less and sends less).
    var started = Math.min(performance.timeOrigin || Infinity, Date.now());
    var enabled = false, timer = 0, busy = false, handedOver = false, seen = Date.now(), absentSince = 0, absentSent = false;
    var notice = { episode: '', since: 0, failed: '' };
    var agreement = { episode: '', since: 0, failed: false, pending: '', at: 0 };
    var calls = new Map(), prefix = Math.random().toString(36).slice(2) + ':', next = 0;

    // ---- The page's world ----
    document.addEventListener('univer-ext-answer', function (event) {
        var answer;
        try { answer = typeof event.detail === 'string' ? JSON.parse(event.detail) : null; } catch (_) { return; }
        var waiting = answer && typeof answer.id === 'string' && calls.get(answer.id);
        if (!waiting) return;
        calls.delete(answer.id); waiting(typeof answer.value === 'string' ? answer.value : null);
    });
    function call(method, args) {
        return new Promise(function (resolve) {
            var id = prefix + (++next);
            var timeout = setTimeout(function () { if (calls.delete(id)) resolve(null); }, CALL_LIMIT);
            calls.set(id, function (value) { clearTimeout(timeout); resolve(value); });
            document.dispatchEvent(new CustomEvent('univer-ext-call', { detail: JSON.stringify({ id: id, method: method, args: args }) }));
        });
    }
    // The agreement's own AJAX answer, correlated with the appearance it was sent for.
    document.addEventListener('univer-ext-native', function (event) {
        var parts = typeof event.detail === 'string' ? event.detail.split('|') : [];
        if (parts.length !== 4 || parts[0] !== 'univer-ajax' || parts[1] !== 'agreement' || !agreement.pending || parts[2] !== agreement.pending) return;
        agreement.pending = '';
        if (parts[3] !== 'success') agreement.failed = true;
    });

    // ---- The background's budget ----
    // {ok, taken}: ok only on the background's explicit yes; taken when another tab has already sent this form.
    // refund: the release is this script's own decision before any click, so the attempt is given back too.
    function budget(op, kind, key, refund) {
        var message = { type: 'confirm', op: op, kind: kind }, no = { ok: false, taken: false };
        if (op !== 'release') message.since = started;
        if (op !== 'absent') message.key = key;
        if (refund === true) message.refund = true;
        try {
            return api.runtime.sendMessage(message).then(function (answer) {
                return answer && typeof answer === 'object' ? { ok: answer.ok === true, taken: answer.reason === 'taken' } : no;
            }, function () { return no; });
        } catch (_) { return Promise.resolve(no); }
    }

    // ---- The student's own answer ----
    // The page is not covered here as it is in the Windows app: a student who clicks or presses a key in the form first
    // answers it, and a second click from here would send it twice.
    function own(event) {
        if (!event.isTrusted || handedOver) return;
        if (event.type === 'keydown' && event.key !== 'Enter' && event.key !== ' ') return;
        var target = event.target;
        if (target && target.closest && target.closest(FORMS)) { handedOver = true; stop(); }
    }
    ['pointerdown', 'click', 'keydown', 'submit'].forEach(function (type) { document.addEventListener(type, own, true); });

    // ---- One look at the page ----
    async function readNotice(p, now) {
        var episode = p.noticeEpisode, key = p.noticeKey;
        if (notice.episode !== episode) { notice.episode = episode; notice.since = now; }
        if (notice.failed === episode) return false;
        if (now - notice.since > OUTCOME) { notice.failed = episode; return false; }
        // A form another tab has sent is this page's stale copy: it is never sent from here, even once that tab's
        // next page frees the reservation.
        var can = await budget('can', 'notice', key);
        if (can.taken) { notice.failed = episode; return false; }
        if (!can.ok) return true;
        var ready = p.checked ? p.submitEnabled : await call('prepareNotice', [p.document, episode]) === 'ready';
        if (!ready || handedOver || !enabled) return true;
        var reserved = await budget('reserve', 'notice', key);
        if (reserved.taken) { notice.failed = episode; return false; }
        if (!reserved.ok) return true;
        if (handedOver || !enabled) { budget('release', 'notice', key, true); return true; }
        // Only an answer that certifies nothing was sent gives the reservation back; no answer keeps it.
        if (await call('submitNotice', [p.document, episode]) === 'not-ready') budget('release', 'notice', key);
        return true;
    }
    async function readAgreement(p, now) {
        var episode = p.agreementEpisode, key = p.agreementKey;
        if (agreement.episode !== episode) { agreement.episode = episode; agreement.since = now; }
        if (agreement.pending) {
            if (now - agreement.at > OUTCOME) { agreement.pending = ''; agreement.failed = true; }
            return !agreement.failed;
        }
        if (agreement.failed) return false;
        if (now - agreement.since > OUTCOME) { agreement.failed = true; return false; }
        var can = await budget('can', 'agreement', key);
        if (can.taken) { agreement.failed = true; return false; }
        if (!can.ok) return true;
        if (!p.agreementChecked) { await call('checkAgreement', [p.document, episode]); return true; }
        if (!p.agreementEnabled || handedOver || !enabled) return true;
        var reserved = await budget('reserve', 'agreement', key);
        if (reserved.taken) { agreement.failed = true; return false; }
        if (!reserved.ok) return true;
        if (handedOver || !enabled) { budget('release', 'agreement', key, true); return true; }
        agreement.pending = episode; agreement.at = Date.now();
        if (await call('submitAgreement', [p.document, episode]) === 'not-ready') { budget('release', 'agreement', key); agreement.pending = ''; }
        return true;
    }
    async function tick() {
        if (busy || !enabled || handedOver || document.hidden) return;
        busy = true;
        try {
            var raw = await call('probe', []), p = null;
            try { p = raw ? JSON.parse(raw) : null; } catch (_) { p = null; }
            if (!p || p.trusted !== true || !p.ready || typeof p.document !== 'string') return;
            var now = Date.now(), active = false;
            if (p.notice === true && typeof p.noticeKey === 'string' && typeof p.noticeEpisode === 'string') active = await readNotice(p, now) || active;
            else if (p.agreement === true && typeof p.agreementKey === 'string' && typeof p.agreementEpisode === 'string') active = await readAgreement(p, now) || active;
            if (p.notice || p.agreement) { absentSince = 0; } else if (p.hasContent) {
                // A page without either form, steady for 2 s, frees the reservations made before it began loading.
                if (!absentSince) absentSince = now;
                if (!absentSent && now - absentSince >= ABSENT) { absentSent = true; budget('absent', 'notice'); budget('absent', 'agreement'); }
            }
            // The poll ends 30 s after the last form it could act on, once a page without forms has been reported.
            if (active || agreement.pending || (!p.notice && !p.agreement && p.hasContent && !absentSent)) seen = now;
            else if (now - seen > IDLE) stop();
        } finally { busy = false; }
    }
    function stop() { clearInterval(timer); timer = 0; }
    function begin() {
        if (!enabled || handedOver || timer) return;
        seen = Date.now();
        timer = setInterval(tick, POLL);
        tick();
    }
    function configure(settings) {
        // Never saved: the default, on, as in the Windows app. Unreadable (null): off, so nothing is confirmed that the
        // student may not want confirmed.
        enabled = settings === undefined || (!!settings && typeof settings === 'object' && settings.confirm !== false);
        if (enabled) { if (document.readyState !== 'loading') begin(); } else stop();
    }
    api.storage.local.get('settings').then(function (value) { configure(value.settings); }, function () { configure(null); });
    api.storage.onChanged.addListener(function (changes, area) { if (area === 'local' && changes.settings) configure(changes.settings.newValue); });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', begin, { once: true });
    Object.defineProperty(globalThis, '__univerExtDriverV1', { value: true });
})();
