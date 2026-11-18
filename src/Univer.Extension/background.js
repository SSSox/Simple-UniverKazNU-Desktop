/* The extension's background: the only writer of the student's home screen (pins, tile order, hidden tiles) and of the
   confirmation budgets, so two tabs never overwrite each other's change. The rules are Core/Shortcuts.cs and the
   notice/agreement budgets of Web/AutomationController.cs. */
'use strict';
var ORIGIN = 'https://univer.kaznu.kz';
var api = globalThis.chrome;
var LIMIT = 24, TITLE_LIMIT = 120;
var FIXED = /^\/(student\/(myschedule|attendance\/full|transcript|umkd)|user\/loginMoodle|ssc(\/UserServiceList|\/StatementList)?)$/i;

function parse(url) { try { return new URL(url); } catch (_) { return null; } }
function control(text) { return /[\u0000-\u001f\u007f-\u009f]/.test(text); }
function trusted(uri) { return !!uri && uri.origin === ORIGIN && !uri.username && !uri.password; }
function fixedHomeLink(url) {
    var uri = parse(url);
    return trusted(uri) && !uri.search && !uri.hash && FIXED.test(uri.pathname.replace(/\/+$/, ''));
}
function allowed(url) {
    if (typeof url !== 'string' || !url || url.length > 2048 || control(url)) return false;
    var uri = parse(url);
    if (!uri) return false;
    if (trusted(uri)) return !/^\/user\/(logout|login)\/?$/i.test(uri.pathname);
    return (uri.protocol === 'https:' || uri.protocol === 'http:') && !uri.username && !uri.password && uri.hostname.toLowerCase() !== 'univer.kaznu.kz';
}
function cleanTitle(title) {
    var value = String(title == null ? '' : title).replace(/[\u0000-\u001f\u007f-\u009f\s]+/g, ' ').trim();
    if (value.length <= TITLE_LIMIT) return value;
    var cut = /[\ud800-\udbff]/.test(value[TITLE_LIMIT - 1]) ? TITLE_LIMIT - 1 : TITLE_LIMIT;
    return value.slice(0, cut).trim();
}
function normalize(pins) {
    var result = [];
    (Array.isArray(pins) ? pins : []).forEach(function (pin) {
        if (result.length >= LIMIT || !pin || !allowed(pin.url) || fixedHomeLink(pin.url)) return;
        var title = cleanTitle(pin.title);
        if (title && !result.some(function (p) { return p.url === pin.url; })) result.push({ title: title, url: pin.url });
    });
    return result;
}
function unique(list) { return list.filter(function (item, index) { return list.indexOf(item) === index; }); }
// One pin message ({pin,title,url}, {order:[…]} or {hidden:[…]}) applied to the stored home; null for anything malformed.
function applyPin(home, payload) {
    var message;
    try { message = typeof payload === 'string' && payload.length <= 65536 ? JSON.parse(payload) : null; } catch (_) { return null; }
    if (!message || typeof message !== 'object') return null;
    var next = { pins: normalize(home.pins), order: Array.isArray(home.order) ? home.order : [], hidden: Array.isArray(home.hidden) ? home.hidden : [] };
    if (Array.isArray(message.order)) {
        if (message.order.length > LIMIT + 6 || !message.order.every(function (url) { return allowed(url); })) return null;
        next.order = unique(message.order);
        return next.order.length === message.order.length ? next : null;
    }
    if (Array.isArray(message.hidden)) {
        if (message.hidden.length > 8 || !message.hidden.every(fixedHomeLink)) return null;
        next.hidden = unique(message.hidden);
        return next;
    }
    if (typeof message.pin !== 'boolean' || !allowed(message.url)) return null;
    if (!message.pin) { next.pins = next.pins.filter(function (p) { return p.url !== message.url; }); return next; }
    if (fixedHomeLink(message.url)) return next;
    var title = cleanTitle(message.title);
    if (!title || next.pins.some(function (p) { return p.url === message.url; })) return next;
    if (next.pins.length >= LIMIT) return null;
    next.pins.push({ title: title, url: message.url });
    return next;
}

// The confirmation budgets (Core/SessionGuard.cs ConfirmationBudget), one for the notice and one for the agreement:
// a form (its key: address and text) sent once is not sent again until a later page of the tab that sent it says it is
// gone (or that tab closes), at most 20
// forms are held, and at most three are sent a minute. A tab clicks only after "reserve" answered true.
// sent remembers when each form was last sent, past the reservation: a tab whose page began loading before that send
// shows a stale copy (another tab sent it and moved on), and it is answered "taken" even after the form is gone.
var KINDS = ['notice', 'agreement'], RESERVED = 20, SENT = 50, ATTEMPTS = 3, MINUTE = 60000, CLOSE_GRACE = 10000;
function time(value) { return typeof value === 'number' && isFinite(value); }
function times(value) {
    var result = {};
    if (value && typeof value === 'object' && !Array.isArray(value)) Object.keys(value).forEach(function (key) {
        if (time(value[key])) result[key] = value[key];
    });
    return result;
}
function budgetOf(value) {
    var ok = value && typeof value === 'object';
    return {
        reserved: times(ok && value.reserved), sent: times(ok && value.sent), owner: times(ok && value.owner),
        attempts: ok && Array.isArray(value.attempts) ? value.attempts.filter(time) : []
    };
}
// One budget message applied to the stored budgets at the background's own time. ok is the answer for the tab; a form
// already reserved answers "taken", which tells that tab another one sent it (so it never sends its copy later).
// message.tab is the sending tab's id, set by the listener from the sender, never by the page. changed says the stored
// budgets moved even for a "can", which is otherwise not written back.
function budgetStep(stored, message, now) {
    var all = {}, changed = false;
    KINDS.forEach(function (kind) { all[kind] = budgetOf(stored && stored[kind]); });
    var budget = all[message.kind], ok = false, reason = '';
    // A clock set back leaves times in the future: attempts and reservations count from now (the minute still holds and a
    // later page can still free a form), while sent keeps its time, so a stale copy stays taken.
    budget.attempts = budget.attempts.map(function (at) { if (at > now) changed = true; return Math.min(at, now); }).filter(function (at) { return now - at < MINUTE; });
    Object.keys(budget.reserved).forEach(function (key) { if (budget.reserved[key] > now) { budget.reserved[key] = now; changed = true; } });
    var own = Object.prototype.hasOwnProperty;
    // A form leaves its reservation for good: the send is dated to that moment, so a page that began loading before it
    // (a copy opened while the POST was still on its way) cannot send it again.
    function free(key, at) {
        delete budget.reserved[key]; delete budget.owner[key];
        budget.sent[key] = Math.max(budget.sent[key] || 0, at);
        changed = true;
    }
    var taken = own.call(budget.reserved, message.key) || (own.call(budget.sent, message.key) && message.since < budget.sent[message.key]);
    // A reservation needs its tab: only that tab's later page or its closing frees it.
    var open = !taken && Object.keys(budget.reserved).length < RESERVED && budget.attempts.length < ATTEMPTS && (message.op !== 'reserve' || time(message.tab));
    if (message.op === 'can' || message.op === 'reserve') {
        ok = open; reason = taken ? 'taken' : open ? '' : 'limit';
        if (ok && message.op === 'reserve') {
            budget.reserved[message.key] = now; budget.attempts.push(now);
            budget.owner[message.key] = message.tab;
            delete budget.sent[message.key]; budget.sent[message.key] = now;
            var keys = Object.keys(budget.sent);
            keys.slice(0, Math.max(0, keys.length - SENT)).forEach(function (key) { delete budget.sent[key]; });
        }
    } else if (message.op === 'release') {
        // Nothing was sent: the form may be sent later. The attempt comes back (as in the Windows app) only when the
        // extension itself stopped before the click (refund); "nothing was sent" from the page's world can be forged by
        // the site's scripts and must not lift the three-a-minute bound.
        // While a form is reserved its sent time is this reservation's own, so both go. Only the reserving tab releases.
        if (own.call(budget.reserved, message.key) && budget.owner[message.key] === message.tab) {
            var at = budget.reserved[message.key], index = budget.attempts.lastIndexOf(at);
            delete budget.reserved[message.key]; delete budget.owner[message.key]; delete budget.sent[message.key];
            if (message.refund === true && index >= 0) budget.attempts.splice(index, 1);
        }
        ok = true;
    } else if (message.op === 'absent') {
        // Only a later page in the tab that sent the form says it is gone, as the Windows app learns it from its one
        // browser: a page in another tab may have loaded while the POST was still on its way. And only a page that began
        // loading after the reservation: the sending page itself (the agreement answers without a reload) does not count.
        Object.keys(budget.reserved).forEach(function (key) {
            if (budget.reserved[key] < message.since && time(message.tab) && budget.owner[key] === message.tab) free(key, now);
        });
        ok = true;
    } else if (message.op === 'closed') {
        // The tab that sent the form is gone: no later page of it will report, so its forms are freed now. Its POST may still
        // be on its way, so pages that begin within CLOSE_GRACE count as stale copies too.
        Object.keys(budget.owner).forEach(function (key) { if (budget.owner[key] === message.tab) free(key, now + CLOSE_GRACE); });
        ok = true;
    }
    return { budgets: all, ok: ok, reason: reason, changed: changed };
}
function budgetMessage(message) {
    if (KINDS.indexOf(message.kind) < 0) return false;
    if (message.op === 'absent') return typeof message.since === 'number' && isFinite(message.since);
    // The keys univer-automation.js gives its forms: the notice's address and the hash of its text, the agreement's hash.
    var key = message.kind === 'notice' ? /^\/student\/attentionStudents\/\d{1,12}\/?:[0-9a-f]{1,8}$/i : /^additional:[0-9a-f]{1,8}$/;
    if (typeof message.key !== 'string' || !key.test(message.key)) return false;
    if (message.op === 'release') return true;
    // can and reserve carry when the asking page began loading.
    return (message.op === 'can' || message.op === 'reserve') && typeof message.since === 'number' && isFinite(message.since);
}

// Every change goes through this one queue: read, decide, write, then the next.
var queue = Promise.resolve();
function serial(work) { var run = queue.then(work, work); queue = run.catch(function () { }); return run; }
function fromSite(sender) {
    var uri = sender && parse(sender.url);
    return trusted(uri) && sender.frameId === 0;
}

api.runtime.onMessage.addListener(function (message, sender, reply) {
    if (!message || typeof message.type !== 'string' || !fromSite(sender)) return false;
    if (message.type === 'pin') {
        serial(function () {
            return api.storage.local.get('home').then(function (value) {
                var next = applyPin(value.home || {}, message.payload);
                return next ? api.storage.local.set({ home: next }).then(function () { return true; }) : false;
            });
        }).then(function (stored) { reply({ ok: stored }); }, function () { reply({ ok: false }); });
        return true;
    }
    if (message.type === 'confirm') {
        // The budgets live in session storage, which only the background reads and which outlasts a restart of the
        // background itself (a service worker stops when idle). Without it nothing is confirmed.
        var session = api.storage.session;
        if (!session || !budgetMessage(message)) { reply({ ok: false }); return false; }
        var input = Object.assign({}, message, { tab: sender.tab && time(sender.tab.id) ? sender.tab.id : undefined });
        serial(function () {
            return session.get('budgets').then(function (value) {
                var step = budgetStep(value.budgets, input, Date.now());
                return (message.op === 'can' && !step.changed ? Promise.resolve() : session.set({ budgets: step.budgets })).then(function () { return step; });
            });
        }).then(function (step) { reply({ ok: step.ok === true, reason: step.reason }); }, function () { reply({ ok: false, reason: 'error' }); });
        return true;
    }
    return false;
});
// Without the "tabs" permission onRemoved still says which tab closed (only its id).
if (api.tabs && api.tabs.onRemoved && api.storage.session) api.tabs.onRemoved.addListener(function (tabId) {
    var session = api.storage.session;
    return serial(function () {
        return session.get('budgets').then(function (value) {
            var budgets = value.budgets, changed = false;
            KINDS.forEach(function (kind) {
                var step = budgetStep(budgets, { op: 'closed', kind: kind, tab: tabId }, Date.now());
                budgets = step.budgets; changed = changed || step.changed;
            });
            return changed ? session.set({ budgets: budgets }) : undefined;
        });
    });
});

if (typeof module === 'object' && module.exports) module.exports = { applyPin: applyPin, allowed: allowed, fixedHomeLink: fixedHomeLink, cleanTitle: cleanTitle, budgetStep: budgetStep, budgetMessage: budgetMessage };
