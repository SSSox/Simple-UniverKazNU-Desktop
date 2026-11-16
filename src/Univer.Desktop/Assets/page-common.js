/* Text and table helpers shared by the page adapters. It runs first in the page bundle; each adapter takes the
   helpers once, when it starts. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerCommonV1) return;
    // Enforce the byte limit while receiving chunked pages, before they can fill the renderer's heap.
    async function boundedText(response, limit) {
        if (+response.headers?.get('content-length') > limit) throw new Error('Oversized response');
        if (!response.body?.getReader) {
            var text = await response.text();
            if (new Blob([text]).size > limit) throw new Error('Oversized response');
            return text;
        }
        var reader = response.body.getReader(), decoder = new TextDecoder(), size = 0, parts = [], ended = false;
        try {
            for (;;) {
                var next = await reader.read();
                if (next.done) { ended = true; break; }
                size += next.value.byteLength;
                if (size > limit) throw new Error('Oversized response');
                parts.push(decoder.decode(next.value, {stream:true}));
            }
            parts.push(decoder.decode());
            return parts.join('');
        } finally {
            if (!ended) reader.cancel().catch(function () {});
            reader.releaseLock();
        }
    }
    function fold(value) { return String(value).normalize('NFC').toLocaleLowerCase().replace(/ё/g, 'е'); }
    // The site writes a class as "Name(type)", then the teacher, "Faculty / Ауд.: 309" and "Период с … по …".
    function lesson(texts) {
        var head = (texts[0] || '').match(/^(.*\S)\s*\(([^()]*(?:лекц|семинар|практ|лаборат|срс|сро|дәріс|зертхана|lecture|seminar|practice|lab)[^()]*)\)\s*$/i);
        var parts = {name:head ? head[1] : texts[0] || '', type:'', kind:'', room:'', faculty:'', teacher:'', period:'', rest:[]};
        if (head) {
            parts.type = head[2].charAt(0).toLocaleUpperCase() + head[2].slice(1);
            parts.kind = /лекц|дәріс|lecture/i.test(head[2]) ? 'lecture' : /семинар|seminar/i.test(head[2]) ? 'seminar' : 'other';
        }
        texts.slice(1).forEach(function (text) {
            var room = text.match(/^(?:(.*?)\s*\/\s*)?(?:ауд(?:итория)?\.?|дәрісхана|room)\s*:?\s*(.+)$/i);
            if (/^(период|кезең|period)(\s|:|$)/i.test(text)) parts.period = text;
            else if (room) { parts.faculty = (room[1] || '').trim(); parts.room = room[2].trim(); }
            else if (!parts.teacher) parts.teacher = text;
            else parts.rest.push(text);
        });
        return parts;
    }
    var umkd = null;
    // The observed course list: rows are tr.link[id], and the site's dblclick handler opens /student/umkd/ + id + /year/term.
    // Read once per page; a fetched page's scripts are parsed as text and never run.
    function umkdCourses(again) {
        if (umkd && !again) return umkd;
        var load = (async function () {
            var abort = new AbortController(), timer = setTimeout(function () { abort.abort(); }, 15000);
            try {
            var response = await fetch('/student/umkd/', {credentials:'same-origin', cache:'no-store', redirect:'error', signal:abort.signal}), final = new URL(response.url, location.origin);
            if (response.status !== 200 || final.origin !== location.origin || /^\/user\/login/i.test(final.pathname)) throw new Error('Unavailable');
            var html = await boundedText(response, 4 * 1024 * 1024);
            var doc = new DOMParser().parseFromString(html, 'text/html');
            var term = Array.from(doc.scripts).map(function (s) { return s.textContent; }).join('\n').match(/["']\/student\/umkd\/["']\s*\+\s*this\.id\s*\+\s*["'](\/\d{4}\/[12])["']/);
            if (!term) throw new Error('Semester unavailable');
            return Array.from(doc.querySelectorAll('#l4 table.inner tr.link[id]')).filter(function (r) { return /^\d+$/.test(r.id) && r.cells.length === 3; })
                .map(function (r) { return {name:(r.cells[1].textContent || '').replace(/\s+/g, ' ').trim(), href:'/student/umkd/' + r.id + term[1]}; });
            } finally { clearTimeout(timer); abort.abort(); }
        })();
        // One attempt per page: a failed read leaves the links on the course list instead of asking again for every class.
        // Only the user's own "retry" or "refresh" (again) reads the list once more.
        umkd = load; load.catch(function () { });
        return load;
    }
    // Where a course's materials are; the list itself when the course is not in the current semester.
    function umkdHref(name) {
        var key = fold(name);
        return umkdCourses().then(function (list) {
            var found = list.find(function (c) { return fold(c.name) === key; }) || list.find(function (c) { return key.startsWith(fold(c.name)) || fold(c.name).startsWith(key); });
            return found ? found.href : '/student/umkd/';
        }, function () { return '/student/umkd/'; });
    }
    window.__univerCommonV1 = Object.freeze({
        lesson: lesson,
        umkdHref: umkdHref,
        umkdCourses: umkdCourses,
        boundedText: boundedText,
        // An element's text with runs of white space collapsed.
        clean: function (node) { return (node?.textContent || '').replace(/\s+/g, ' ').trim(); },
        // Text for matching a search: composed, lower case, "ё" read as "е".
        fold: fold,
        // The rows of a table without the rows of tables nested in its cells.
        ownRows: function (table) { return Array.from(table.rows).filter(function (row) { return row.closest('table') === table; }); },
        // Writes text only when it changed, so observers of the page see no needless mutation.
        setText: function (node, text) { if (node.textContent !== text) node.textContent = text; }
    });
})();
