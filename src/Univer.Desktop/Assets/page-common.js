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
    // A score's band, as the owner set them for a score out of 100: below 50 is a shortfall (недобор), 50–69 satisfactory,
    // 70–89 good, 90 and above excellent. `next` is the mark above the score and `left` how far it is. A score on another
    // scale, an empty cell or a word gets no band: nothing is guessed.
    var MARKS = [50, 70, 90], BANDS = ['low', 'fair', 'good', 'top'];
    function number(value) { var text = String(value == null ? '' : value).trim(); return /^-?\d+(?:[.,]\d+)?$/.test(text) ? Number(text.replace(',', '.')) : NaN; }
    function band(value, max) {
        var score = number(value);
        if (!isFinite(score) || number(max) !== 100) return null;
        score = Math.max(0, Math.min(100, score));
        var index = MARKS.filter(function (mark) { return score >= mark; }).length, next = MARKS[index] || null;
        // Rounded up to a tenth: 49.95 is 0.1 short of 50, never "0 left" under a band that is not yet reached.
        var gap = next ? Math.round((next - score) * 1000) / 1000 : 0;
        return { band: BANDS[index], score: score, next: next, left: next ? Math.ceil(gap * 10 - 1e-9) / 10 : 0 };
    }
    // Kazakh puts the dative ending on the mark itself.
    var BAND_WORDS = {
        ru: { low: 'Недобор', fair: 'Удовлетворительно', good: 'Хорошо', top: 'Отлично', left: function (n, mark) { return 'ещё ' + n + ' до ' + mark; } },
        kz: { low: 'Жеткіліксіз', fair: 'Қанағаттанарлық', good: 'Жақсы', top: 'Өте жақсы', left: function (n, mark) { return mark + ({ 50: '-ге', 70: '-ке', 90: '-ға' }[mark] || '') + ' дейін ' + n + ' балл'; } },
        en: { low: 'Insufficient', fair: 'Satisfactory', good: 'Good', top: 'Excellent', left: function (n, mark) { return n + (n === '1' ? ' point' : ' points') + ' to ' + mark; } }
    };
    var RANGES = { low: '0–49', fair: '50–69', good: '70–89', top: '90–100' };
    function figure(value, lang) { var text = String(value); return lang === 'en' ? text : text.replace('.', ','); }
    function bandWords(lang) { return BAND_WORDS[lang] || BAND_WORDS.ru; }
    // What the band says in a few words: what is left to the next mark, or the band's name at the top.
    function bandNote(found, lang) { var words = bandWords(lang); return found.next ? words.left(figure(found.left, lang), found.next) : words.top; }
    // One score tile (label, value, a bar of its share of the maximum), the same on the home page and the grades page.
    // A score out of 100 also takes its band's colour and says what is left to the next mark.
    // Compact (the home card): one line a course, the band in colour and the bar, its words in the label and tooltip only.
    function scoreChip(item, lang, compact) {
        var chip = document.createElement('span'), found = band(item.value, item.max), words = bandWords(lang);
        chip.className = 'univer-app-overview-score'; chip.setAttribute('role', 'group');
        var said = item.label + ': ' + (item.value || '–') + (item.max ? ' / ' + item.max : '');
        var label = document.createElement('span'), value = document.createElement('span');
        label.className = 'univer-app-overview-score-label'; label.textContent = item.label;
        value.className = 'univer-app-overview-score-value'; value.textContent = item.value || '–';
        chip.append(label, value);
        var ratio = item.max && item.value ? number(item.value) / number(item.max) : NaN;
        if (isFinite(ratio)) { var bar = document.createElement('span'); bar.className = 'univer-app-overview-score-bar'; bar.style.setProperty('--univer-score', Math.round(Math.max(0, Math.min(1, ratio)) * 100) + '%'); chip.appendChild(bar); }
        if (found) {
            var note = bandNote(found, lang);
            if (!compact) { var next = document.createElement('span'); next.className = 'univer-app-overview-score-next'; next.textContent = note; chip.appendChild(next); }
            chip.dataset.univerBand = found.band;
            chip.title = words[found.band] + ': ' + RANGES[found.band] + (found.next ? '. ' + note.charAt(0).toLocaleUpperCase() + note.slice(1) : '');
            said += '. ' + words[found.band] + (found.next ? ', ' + note : '');
        }
        chip.setAttribute('aria-label', said);
        Array.from(chip.children).forEach(function (child) { child.setAttribute('aria-hidden', 'true'); });
        return chip;
    }
    window.__univerCommonV1 = Object.freeze({
        band: band,
        bandWords: bandWords,
        bandNote: bandNote,
        bandRange: function (name) { return RANGES[name] || ''; },
        figure: figure,
        scoreChip: scoreChip,
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
