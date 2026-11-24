/* Syllabus tasks on the schedule. For every class of the shown week it finds the topic in the course's interactive
   syllabus written for the student's own educational programme, and lists the week's independent work (СРО) by course.
   The group's schedule leads: where it holds a kind of class on other weeks than the syllabus says, the syllabus rows
   go to the classes in order, and independent work is named beside the nearest seminar.
   It reads the student's home page, the UMKD course pages, printed syllabi and the schedule's other weeks with plain
   GET and never runs their scripts; teacher consultations (СРОП) are left out; release() removes everything it added. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerSyllabusV1) return;
    var common = window.__univerCommonV1, clean = common.clean, fold = common.fold, ownRows = common.ownRows;
    // How much is read, kept and shown.
    var LIMITS = {
        page: 4 * 1024 * 1024,   // characters of one fetched page
        wait: 15000,             // milliseconds for one request
        courses: 20,             // courses of a semester
        candidates: 12,          // syllabi tried for one course, newest first
        entries: 300,            // rows of one syllabus plan
        weeks: 40,               // highest week number, and weeks of the schedule read for one semester
        near: 1,                 // weeks an independent work may stand from its own week to be beside a seminar
        topic: 1200,            // characters of a topic that are kept and shown when expanded
        preview: 180,            // characters of a collapsed topic; CSS also clamps it to three lines
        perLesson: 2,            // entries under one class before "show more"
        sro: 6,                  // rows of the week's list before "show more"
        names: 4,                // course names in one note
        folded: 60,              // remembered collapsed classes
        parallel: 2,             // courses resolved at once
        again: 20000,            // a course whose pages failed is read once more after this
        fresh: 400 * 86400000,   // a found plan is kept for the semester: a syllabus does not change, a new programme or Refresh rereads it
        missing: 86400000,       // "no syllabus for this programme" is rechecked after this
        program: 86400000,       // the student's programme is reread after this, so a change of speciality is noticed
        timetable: 7 * 86400000, // the group's weeks are reread after this
        list: 3 * 86400000,      // the semester's course list is reread after this
        store: 1500000           // characters of the stored cache
    };
    var STORE = 'univer.desktop.syllabus.v1', PREFS = 'univer.desktop.syllabus-ui.v1';
    var PRINT = /^\/student\/interactive\/syllabus\/print\/\d+\/\d+\/?$/, CODE = /\d[A-ZА-ЯЁ]\d{5}/;
    var WEEK = /^\/student\/myschedule\/(\d{4})\/([12])\/\d{1,2}\.\d{1,2}\.\d{4}\/\d{1,2}\.\d{1,2}\.\d{4}\/?$/;
    // The classes a seminar row or independent work can belong to.
    var CLASSES = ['seminar', 'practice', 'lab'];
    var enabled = false, language = 'ru', generation = 0, style = null, model = null, active = 0, programPromise = null, counter = 0, workKept = '';
    var views = new Map(), controllers = new Set(), teachers = new Map(), timers = new Set(), prefs = null;

    function text() {
        var number = function (n) { return String(n).replace('.', language === 'en' ? '.' : ','); };
        if (language === 'en') return {title:'Syllabus tasks', inLessons:'Show under classes', refresh:'Refresh', refreshHint:'Read the syllabi again', progress:function (d, t) { return 'Reading syllabi: ' + d + ' of ' + t; },
            sro:'Independent work this week', sroPending:'Looking for independent work in the syllabi…', topic:'Topic',
            same:function (t) { return t ? 'same topic as at ' + t : 'same topic as the class before'; }, loading:'Looking for the topic…', more:'Show in full', less:'Show less', rest:function (n) { return 'Show ' + n + ' more'; }, fewer:'Show fewer',
            points:function (n) { return 'max score: ' + number(n); }, open:'Open syllabus', cut:'The full text is in the syllabus', hide:'Hide the task', show:'Show the task', topics:function (n) { return n + ' entries'; },
            failed:'Could not read the syllabus', retry:'Retry', noProgram:'Could not find your educational programme. Open the portal home page and retry.', noCourses:'Could not read the course list. Check the connection and retry.',
            sroName:'IWS', noWeeks:'Could not read the other weeks of the schedule, topics are shown by syllabus weeks', andMore:function (n) { return 'and ' + n + ' more'; }};
        if (language === 'kz') return {title:'Силлабус бойынша тапсырмалар', inLessons:'Сабақтарда көрсету', refresh:'Жаңарту', refreshHint:'Силлабустарды қайта оқу', progress:function (d, t) { return 'Силлабустар оқылуда: ' + d + ' / ' + t; },
            sro:'Осы аптадағы БӨЖ', sroPending:'Силлабустардан БӨЖ ізделуде…', topic:'Тақырып',
            same:function (t) { return t ? 'тақырып ' + t + ' сабағындағыдай' : 'тақырып алдыңғы сабақтағыдай'; }, loading:'Тақырып ізделуде…', more:'Толық көрсету', less:'Қысқарту', rest:function (n) { return 'Тағы ' + n + ' көрсету'; }, fewer:'Азырақ көрсету',
            points:function (n) { return 'ең жоғары балл: ' + number(n); }, open:'Силлабусты ашу', cut:'Толық мәтін силлабуста', hide:'Тапсырманы жасыру', show:'Тапсырманы көрсету', topics:function (n) { return n + ' жазба'; },
            failed:'Силлабусты оқу мүмкін болмады', retry:'Қайталау', noProgram:'Білім беру бағдарламаңыз табылмады. Кабинеттің басты бетін ашып, қайталаңыз.', noCourses:'Пәндер тізімін оқу мүмкін болмады. Байланысты тексеріп, қайталаңыз.',
            sroName:'БӨЖ', noWeeks:'Кестенің басқа апталарын оқу мүмкін болмады, тақырыптар силлабус апталары бойынша көрсетілді', andMore:function (n) { return 'және тағы ' + n; }};
        return {title:'Задания по силлабусу', inLessons:'Показывать у занятий', refresh:'Обновить', refreshHint:'Прочитать силлабусы заново', progress:function (d, t) { return 'Читаем силлабусы: ' + d + ' из ' + t; },
            sro:'СРО на этой неделе', sroPending:'Ищем СРО в силлабусах…', topic:'Тема',
            same:function (t) { return t ? 'та же тема, что в ' + t : 'та же тема, что на предыдущей паре'; }, loading:'Ищем тему…', more:'Показать полностью', less:'Свернуть', rest:function (n) { return 'Показать ещё ' + n; }, fewer:'Показать меньше',
            points:function (n) { return 'макс. балл: ' + number(n); }, open:'Открыть силлабус', cut:'Полный текст в силлабусе', hide:'Скрыть задание', show:'Показать задание', topics:function (n) { return 'записей: ' + n; },
            failed:'Не удалось прочитать силлабус', retry:'Повторить', noProgram:'Не удалось определить вашу образовательную программу. Откройте главную страницу кабинета и повторите.', noCourses:'Не удалось прочитать список предметов. Проверьте связь и повторите.',
            sroName:'СРО', noWeeks:'Не удалось прочитать расписание других недель, темы показаны по неделям силлабуса', andMore:function (n) { return 'и ещё ' + n; }};
    }

    /* Reading the pages. */
    // Latin letters typed in place of the Cyrillic ones they look like ("БOӨЖ").
    function cyrillic(value) { return /[а-яёәіңғүұқөһ]/.test(value) ? value.replace(/o/g, 'о').replace(/c/g, 'с').replace(/p/g, 'р').replace(/e/g, 'е').replace(/a/g, 'а') : value; }
    // The kind of a class from its name on the schedule or in the syllabus, in the site's three languages.
    function kindOf(label) {
        var value = cyrillic(fold(label || ''));
        if (/сро|срс|бөж|сөж|бмөж|iws|siw|самостоятельн|өзіндік|independent/.test(value)) return 'sro';
        if (/лекц|дәріс|lecture/.test(value)) return 'lecture';
        if (/семинар|seminar/.test(value)) return 'seminar';
        if (/лаборат|зертхана|\blab/.test(value)) return 'lab';
        if (/практ|тәжірибе|practic/.test(value)) return 'practice';
        return 'other';
    }
    // "СРОП 2", "ОБӨЖ 1." or "IWST 3" at the start names a consultation; "СРО 1", "БӨЖ 2" or "IWS 1" the work itself.
    function marker(value) {
        var token = (fold(value || '').replace(/^[^a-zа-яёәіңғүұқөһ]+/, '').match(/^[^\s\d.:,;()«»"“”]+/) || [''])[0];
        token = cyrillic(token);
        if (/^(сроп|срсп|обөж|боөж|осөж|соөж|бмөж|оөж|iwst|siwt|tsis|tsiw|iwsp)$/.test(token)) return 'srop';
        if (/^(сро|срс|бөж|сөж|iws|siw|sis)$/.test(token)) return 'sro';
        return '';
    }
    // The site keeps both under one type, "СРО/СРОП". The entry's own name decides, then the start of its topic, then
    // whether it is worded as a consultation; an unnamed entry without points is a reminder, not work to hand in.
    function sroKind(name, topic, score) {
        var named = marker(name) || marker(topic);
        if (named) return named;
        if (/^[^a-zа-яёәіңғүұқөһ]*(консультаци|разъяснени|ознакомлени|consultation)|кеңес беру/.test(fold(topic || ''))) return 'srop';
        return score > 0 ? 'sro' : 'srop';
    }
    function cut(value, limit) {
        if (value.length <= limit) return value;
        var part = value.slice(0, limit), space = part.lastIndexOf(' ');
        return (space > limit * 0.6 ? part.slice(0, space) : part).replace(/[\s.,;:–—-]+$/, '') + '…';
    }
    function divisionOf(value) {
        var found = fold(value || '');
        return /рус|орыс|russian/.test(found) ? 'ru' : /каз|қаз|kazakh/.test(found) ? 'kz' : /англ|ағылш|english/.test(found) ? 'en' : '';
    }
    // The printed interactive syllabus: table.students holds "label / value" rows, and the plan is a table whose rows
    // carry the week in td.it and a nested table with one row per class: type and name, topic, maximum score.
    function parseSyllabus(doc) {
        var rows = Array.from(doc.querySelectorAll('table.students td.label')).map(function (cell) {
            return {label:clean(cell), value:cell.nextElementSibling ? clean(cell.nextElementSibling) : ''};
        });
        var find = function (pattern) { return rows.find(function (row) { return pattern.test(row.label); }); };
        var programRow = find(/образовательн\S*\s+программ|білім\s+беру\s+бағдарлам|educational\s+program/i) ||
            rows.find(function (row) { return CODE.test(row.value) && /№|no\./i.test(row.value); });
        var programs = [];
        if (programRow) programRow.value.split(/(?:Группа|Топ|Group)\s*(?:№|No\.?)\s*:?/i).forEach(function (part) {
            part = part.trim(); if (!part || programs.length >= 80) return;
            var division = part.match(/(?:языковое\s+отделение|тілдік\s+бөлімі|language\s+division)\s*:*\s*([^,;)]+)/i);
            programs.push({code:(part.match(CODE) || [''])[0], text:fold(part.slice(0, 400)), division:division ? divisionOf(division[1]) : ''});
        });
        var yearRow = find(/академическ\S*\s+год|академиялық\s+жыл|оқу\s+жылы|academic\s+year/i);
        var people = rows.filter(function (row) { return /^(автор|author|лектор|дәріскер|lecturer|ассистент|assistant|соавтор|co-?author)/i.test(row.label); })
            .map(function (row) { return row.value; }).join(' ');
        var table = Array.from(doc.querySelectorAll('table')).find(function (candidate) {
            return ownRows(candidate).some(function (row) { return row.cells[0]?.classList.contains('it') && !!row.cells[1]?.querySelector('table'); });
        });
        var entries = [];
        if (table) ownRows(table).forEach(function (row) {
            var first = row.cells[0]; if (!first || !first.classList.contains('it')) return;
            var week = parseInt(clean(first), 10), inner = row.cells[1]?.querySelector('table');
            if (!(week >= 1 && week <= LIMITS.weeks) || !inner) return;
            ownRows(inner).forEach(function (line) {
                var box = line.cells[0]?.firstElementChild, parts = box ? Array.from(box.children) : [];
                if (parts.length < 2 || entries.length >= LIMITS.entries) return;
                var head = Array.from(parts[0].children).map(clean);
                var label = (head[0] || clean(parts[0])).replace(/\s*:\s*$/, ''), name = (head[1] || '').slice(0, 60), topic = clean(parts[1]);
                var score = parts[2] ? parseFloat(clean(parts[2]).replace(',', '.')) : 0, kind = kindOf(label);
                if (!topic) return;
                if (!isFinite(score) || score < 0) score = 0;
                // The combined name "СРО/СРОП 4" says nothing once the entry is known to be the work itself.
                if (kind === 'sro') { kind = sroKind(name, topic, score); if (!marker(name)) name = ''; }
                entries.push({week:week, kind:kind, name:name, topic:cut(topic, LIMITS.topic), score:score});
            });
        });
        return {programs:programs, year:yearRow ? (yearRow.value.match(/\b(20\d{2})\b/) || ['', ''])[1] : '', people:fold(people), entries:entries};
    }
    // The student's programme: "Специальность: 6B06301 Системы информационной безопасности" on the home page.
    function parseProgram(doc) {
        var cells = Array.from(doc.querySelectorAll('#l4 td')); if (!cells.length) cells = Array.from(doc.querySelectorAll('td'));
        cells = cells.filter(function (cell) { return !cell.querySelector('table'); });
        var valueAfter = function (pattern) {
            var label = cells.find(function (cell) { return pattern.test(clean(cell)) && cell.nextElementSibling; });
            return label ? clean(label.nextElementSibling) : '';
        };
        var value = valueAfter(/^(специальность|мамандық\S*|specialty|speciality|образовательная\s+программа|білім\s+беру\s+бағдарламасы|educational\s+program(me)?)\s*:?$/i);
        if (!CODE.test(value)) value = clean(cells.find(function (cell) { return new RegExp('^' + CODE.source + '(\\s|-|$)').test(clean(cell)); }));
        var code = (value.match(CODE) || [''])[0];
        if (!code) return null;
        return {code:code, name:fold(value.replace(code, '').replace(/^[\s:–—-]+/, '').replace(/\s+\d+$/, '')).slice(0, 160),
            division:divisionOf(valueAfter(/^(отделение|языковое\s+отделение|бөлім\S*|тілдік\s+бөлімі|department|language\s+division)\s*:?$/i))};
    }
    // A syllabus counts only when it names the student's programme and, where it gives one, the semester's academic
    // year. Among those the student's language division and a teacher from the schedule decide.
    function rate(parsed, program, year, people) {
        var mine = parsed.programs.filter(function (group) { return group.code ? group.code === program.code : !!program.name && group.text.indexOf(program.name) >= 0; });
        if (!mine.length || (parsed.year && year && parsed.year !== year)) return -1;
        var score = 1;
        if (parsed.year && year) score += 8;
        if (program.division && mine.some(function (group) { return group.division === program.division; })) score += 4;
        if (people.some(function (name) { return parsed.people.indexOf(name) >= 0; })) score += 2;
        return score;
    }
    function group(entries) {
        var weeks = {}, kinds = {};
        entries.forEach(function (entry) { (weeks[entry.week] = weeks[entry.week] || []).push({week:entry.week, kind:entry.kind, name:entry.name, topic:entry.topic, score:entry.score}); kinds[entry.kind] = true; });
        return {weeks:weeks, kinds:kinds};
    }
    // The syllabus form has lectures and seminars only, so a practical or laboratory class reads the seminar rows
    // when the syllabus has no rows of its own kind.
    function rowKind(plan, kind) { return kind === 'lecture' || plan.kinds[kind] ? kind : 'seminar'; }
    function rowWeeks(plan, kind) {
        return Object.keys(plan.weeks).map(Number).filter(function (week) { return plan.weeks[week].some(function (entry) { return entry.kind === kind; }); }).sort(function (a, b) { return a - b; });
    }
    // What the syllabus sets for a class of this kind in this week. placed: where the group's classes of this kind are
    // and which syllabus weeks each of them takes; without it, or for a week it does not know, the syllabus week itself.
    function tasksFor(plan, week, kind, placed) {
        if (kind === 'other' || kind === 'sro') return [];
        var wanted = rowKind(plan, kind), from = placed && placed.classes.indexOf(week) >= 0 ? placed.rows[week] || [] : [week];
        return from.reduce(function (found, source) { return found.concat((plan.weeks[source] || []).filter(function (entry) { return entry.kind === wanted; })); }, []);
    }
    // One week of the schedule: which course has which kind of class in it. A page without the site's week list is
    // a notice or an error, not an empty week.
    function parseWeek(doc) {
        if (!doc.getElementById('weekSelect')) throw new Error('Not a schedule');
        var table = doc.querySelector('table.schedule'), found = [];
        if (table) ownRows(table).slice(1).forEach(function (row) {
            Array.from(row.cells).forEach(function (cell, index) {
                if (cell.classList.contains('time')) return;
                var blocks = Array.from(cell.querySelectorAll('.groups>div'));
                if (!blocks.length) { if (!index) return; blocks = [cell]; }
                blocks.forEach(function (block) {
                    if (block.hidden || /display\s*:\s*none/i.test(block.getAttribute('style') || '')) return;
                    var parts = Array.from(block.querySelectorAll('p')); if (!parts.length) parts = [block];
                    var facts = common.lesson(parts.map(clean).filter(Boolean)), key = fold(facts.name || '');
                    if (key && !found.some(function (item) { return item.key === key && item.kind === kindOf(facts.type); })) found.push({key:key, kind:kindOf(facts.type)});
                });
            });
        });
        return found;
    }
    // Lays the syllabus weeks of one kind of class over the weeks in which the group has that class: {class week:
    // [syllabus weeks]}. The rows keep their order and stand as near to their own weeks as that allows, so nothing
    // moves where every syllabus week has its class. With class weeks to spare a row takes one of its own, the later
    // one on a tie; with more syllabus weeks than class weeks every class gets a row and neighbours share.
    function align(rows, classes) {
        var m = rows.length, n = classes.length, cost = [], placed = {}, i, j;
        if (!m || !n) return placed;
        var apart = function (row, at) { return Math.abs(rows[row - 1] - classes[at - 1]); }, spare = n >= m;
        // cost[i][j]: the least total distance of the first i rows on the first j class weeks.
        for (i = 0; i <= m; i++) {
            cost[i] = [];
            for (j = 0; j <= n; j++) {
                if (!i) cost[i][j] = spare || !j ? 0 : Infinity;
                else if (!j) cost[i][j] = Infinity;
                // A class week is passed over, or takes its first row, or takes one more row.
                else cost[i][j] = spare ? Math.min(cost[i][j - 1], cost[i - 1][j - 1] + apart(i, j)) : apart(i, j) + Math.min(cost[i - 1][j - 1], cost[i - 1][j]);
            }
        }
        for (i = m, j = n; i > 0 && j > 0;) {
            var put = function () { (placed[classes[j - 1]] = placed[classes[j - 1]] || []).unshift(rows[i - 1]); };
            if (spare) { if (cost[i][j] === cost[i - 1][j - 1] + apart(i, j)) { put(); i--; } j--; }
            else { put(); if (cost[i][j] === cost[i - 1][j] + apart(i, j)) i--; else { i--; j--; } }
        }
        return placed;
    }
    async function read(url) {
        var abort = new AbortController(), timer = setTimeout(function () { abort.abort(); }, LIMITS.wait); controllers.add(abort);
        try {
            var response = await fetch(url, {credentials:'same-origin', cache:'no-store', redirect:'error', signal:abort.signal});
            var final = new URL(response.url || url, location.origin), expected = new URL(url, location.origin);
            if (response.status !== 200 || final.origin !== location.origin || final.pathname.replace(/\/$/, '') !== expected.pathname.replace(/\/$/, '') ||
                +response.headers?.get('content-length') > LIMITS.page) throw new Error('Unexpected response');
            var html = await common.boundedText(response, LIMITS.page);
            var doc = new DOMParser().parseFromString(html, 'text/html');
            if (doc.querySelector('#login_frm')) throw new Error('Session expired');
            return doc;
        } finally { clearTimeout(timer); abort.abort(); controllers.delete(abort); }
    }

    /* What is remembered between pages. Plans are course content, the programme code is the student's own; both stay in
       the site's storage inside the app profile and go with it on logout. */
    // The account that signed in last (univer-automation.js notes it at the login form). Another account's programme,
    // course list and weeks are dropped; course plans are the same for everyone and stay.
    var storeCache = null, storeOwner = null;
    function account() { try { return localStorage.getItem('univer.app.account.v1') || ''; } catch (_) { return ''; } }
    function stored(key) {
        if (key === STORE) {
            var who = account();
            if (storeCache && storeOwner === who) return storeCache;
            storeCache = null; storeOwner = null;
            try {
                var cached = JSON.parse(localStorage.getItem(STORE) || 'null') || {};
                if ((cached.owner || '') !== who) { delete cached.program; delete cached.list; delete cached.pattern; cached.owner = who; }
                storeCache = cached; storeOwner = who;
                return cached;
            } catch (_) { return {}; }
        }
        try {
            return JSON.parse(localStorage.getItem(key) || 'null') || {};
        } catch (_) { return {}; }
    }
    // Strict lower bound on JSON.stringify(node).length, used to evict excess plans before serializing.
    function estimateSize(node) {
        if (node === null) return 4;
        var type = typeof node;
        if (type === 'string') return node.length + 2;
        if (type === 'number') return isFinite(node) ? String(node).length : 4;
        if (type === 'boolean') return node ? 4 : 5;
        if (Array.isArray(node)) {
            var total = 2;
            for (var i = 0; i < node.length; i++) total += (node[i] === undefined ? 4 : estimateSize(node[i])) + (i ? 1 : 0);
            return total;
        }
        if (type === 'object') {
            var sum = 2, count = 0, keys = Object.keys(node);
            for (var j = 0; j < keys.length; j++) {
                var val = node[keys[j]];
                if (val === undefined) continue;
                sum += keys[j].length + 3 + estimateSize(val) + (count ? 1 : 0);
                count++;
            }
            return sum;
        }
        return 0;
    }
    function keep(key, value) {
        try {
            var who = key === STORE ? account() : '';
            if (key === STORE) value.owner = who;
            var order = null;
            if (value && value.courses) {
                var courses = value.courses, courseKeys = Object.keys(courses), entrySizes = {}, total = 2, topCount = 0;
                Object.keys(value).forEach(function (prop) {
                    if (prop === 'courses') { total += 12 + (topCount ? 1 : 0); topCount++; return; }
                    if (value[prop] === undefined) return;
                    total += prop.length + 3 + estimateSize(value[prop]) + (topCount ? 1 : 0);
                    topCount++;
                });
                var courseCount = 0;
                for (var i = 0; i < courseKeys.length; i++) {
                    var k = courseKeys[i];
                    if (courses[k] === undefined) continue;
                    entrySizes[k] = k.length + 3 + estimateSize(courses[k]);
                    total += entrySizes[k] + (courseCount ? 1 : 0);
                    courseCount++;
                }
                if (total > LIMITS.store && courseCount) {
                    order = courseKeys.filter(function (k) { return courses[k] !== undefined; })
                        .sort(function (a, b) { return (courses[a].at || 0) - (courses[b].at || 0); });
                    var at = 0;
                    while (total > LIMITS.store && at < order.length) {
                        var drop = order[at++];
                        delete courses[drop];
                        courseCount--;
                        total -= entrySizes[drop] + (courseCount > 0 ? 1 : 0);
                    }
                    order = order.slice(at);
                }
            }
            var serialized = JSON.stringify(value);
            // The oldest plans go first if string escaping pushed the exact JSON past the limit.
            if (serialized.length > LIMITS.store && value && value.courses) {
                if (!order) order = Object.keys(value.courses).sort(function (a, b) { return (value.courses[a].at || 0) - (value.courses[b].at || 0); });
                var index = 0;
                while (serialized.length > LIMITS.store && index < order.length) {
                    var excess = serialized.length - LIMITS.store;
                    while (excess > 0 && index < order.length) {
                        var oldest = order[index++];
                        var dropped = value.courses[oldest];
                        delete value.courses[oldest];
                        excess -= oldest.length + (index < order.length ? 4 : 3) + (dropped !== undefined ? JSON.stringify(dropped).length : 0);
                    }
                    serialized = JSON.stringify(value);
                }
            }
            localStorage.setItem(key, serialized);
            if (key === STORE) { storeCache = value; storeOwner = who; }
        } catch (_) {
            if (key === STORE) { storeCache = null; storeOwner = null; }
        }
    }
    function preferences() {
        if (!prefs) { var saved = stored(PREFS); prefs = {lessons:saved.lessons !== false, sro:saved.sro !== false, folded:saved.folded && typeof saved.folded === 'object' ? saved.folded : {}}; }
        return prefs;
    }
    function savePreferences() {
        var keys = Object.keys(prefs.folded);
        if (keys.length > LIMITS.folded) keys.slice(0, keys.length - LIMITS.folded).forEach(function (key) { delete prefs.folded[key]; });
        keep(PREFS, prefs);
    }
    function studentProgram(force) {
        if (programPromise && !force) return programPromise;
        var saved = stored(STORE).program;
        if (!force && saved && CODE.test(saved.code || '') && Date.now() - saved.at < LIMITS.program) return programPromise = Promise.resolve(saved);
        programPromise = (async function () {
            var sources = ['/student/bachelor/', '/student/educplan/'];
            for (var i = 0; i < sources.length; i++) {
                var found = null;
                try { found = parseProgram(await read(sources[i])); } catch (_) { }
                if (!found) continue;
                found.at = Date.now();
                var cache = stored(STORE); cache.program = found; keep(STORE, cache);
                return found;
            }
            throw new Error('Programme unavailable');
        })();
        programPromise.catch(function () { });
        return programPromise;
    }
    // The semester's courses are remembered too, so a page that finds every plan stored makes no request at all.
    async function courseList(force) {
        var saved = stored(STORE).list;
        var shown = shownWeek(), term = ((saved?.items?.[0]?.href || '').match(/(\/\d{4}\/[12])\/?$/) || ['', ''])[1];
        // Only for the semester on screen and the site's language: course names are written in it.
        if (!force && saved && Array.isArray(saved.items) && saved.items.length && saved.language === language && (!shown?.term || shown.term === term) && fresh(saved.at, LIMITS.list)) return saved.items.slice(0, LIMITS.courses);
        var items = (await common.umkdCourses(force)).slice(0, LIMITS.courses);
        if (items.length) { var cache = stored(STORE); cache.list = {at:Date.now(), language:language, items:items}; keep(STORE, cache); }
        return items;
    }
    function remember(job, program) {
        var cache = stored(STORE); cache.courses = cache.courses || {};
        cache.courses[job.href] = {at:Date.now(), code:program.code, division:program.division || '', status:job.status, certain:job.certain !== false, plan:job.plan || null};
        keep(STORE, cache);
    }
    function recall(job, program, courses) {
        var saved = ((courses || stored(STORE).courses) || {})[job.href];
        if (!saved || saved.code !== program.code || saved.division !== (program.division || '')) return false;
        var age = Date.now() - saved.at, limit = saved.status === 'ready' && saved.certain ? LIMITS.fresh : LIMITS.missing;
        if (!(age >= 0 && age < limit) || (saved.status === 'ready' && !saved.plan?.weeks)) return false;
        job.status = saved.status; job.plan = saved.plan; job.certain = saved.certain;
        // Plans remembered before the rows carried their week.
        if (job.plan) Object.keys(job.plan.weeks).forEach(function (week) { job.plan.weeks[week].forEach(function (entry) { entry.week = +week; }); });
        return true;
    }

    /* The group's own timetable. Every week of the site's week list is read once, so that a class held on another
       week than the syllabus says is given its topic by the order of the group's classes. */
    function fresh(at, limit) { var age = Date.now() - at; return age >= 0 && age < limit; }
    function weekNumber(option, index) {
        var label = clean(option).replace(/\d{1,2}\.\d{1,2}\.\d{4}/g, ' ');
        return parseInt((label.match(/\d{1,2}/) || [''])[0], 10) || index + 1;
    }
    function loadPattern(owner, epoch, force) {
        var select = document.getElementById('weekSelect'), pages = [], pattern = owner.pattern = {state:'loading', weeks:{}};
        Array.from(select && select.tagName === 'SELECT' ? select.options : []).slice(0, LIMITS.weeks).forEach(function (option, index) {
            var url; try { url = new URL(option.value, location.origin); } catch (_) { return; }
            var found = url.origin === location.origin && !url.search && !url.hash && url.pathname.match(WEEK), number = weekNumber(option, index);
            if (found && '/' + found[1] + '/' + found[2] === owner.term && number <= LIMITS.weeks && !pages.some(function (page) { return page.number === number; })) pages.push({number:number, href:url.pathname});
        });
        // A list of one week has nothing to lay the syllabus over.
        if (pages.length < 2) { pattern.state = 'none'; return; }
        var signature = owner.term + '|' + pages.map(function (page) { return page.number; }).join(','), saved = stored(STORE).pattern;
        if (!force && saved && saved.signature === signature && saved.weeks && fresh(saved.at, LIMITS.timetable)) { pattern.weeks = saved.weeks; pattern.state = 'ready'; return; }
        var next = 0, weeks = {}, current = function () { return generation === epoch && model === owner && owner.pattern === pattern; };
        var worker = async function () {
            while (next < pages.length && current()) {
                var page = pages[next++];
                parseWeek(await read(page.href)).forEach(function (item) {
                    var list = weeks[item.key + '|' + item.kind] = weeks[item.key + '|' + item.kind] || [];
                    if (list.indexOf(page.number) < 0) list.push(page.number);
                });
            }
        };
        var workers = []; for (var i = 0; i < LIMITS.parallel; i++) workers.push(worker());
        Promise.all(workers).then(function () {
            if (!current()) return;
            Object.keys(weeks).forEach(function (key) { weeks[key].sort(function (a, b) { return a - b; }); });
            pattern.weeks = weeks; pattern.state = 'ready';
            var cache = stored(STORE); cache.pattern = {at:Date.now(), signature:signature, weeks:weeks}; keep(STORE, cache);
        }, function () {
            // Half a timetable would move topics to the wrong classes: without all the weeks nothing is moved.
            next = pages.length; if (current()) pattern.state = 'failed';
        }).then(function () { if (current()) renderAll(true); });
    }
    // Where the group has one kind of class of a course, and which syllabus weeks each of those weeks takes.
    // What was worked out for a course holds for one timetable and one plan.
    function settled(job) {
        var weeks = model.pattern.weeks;
        if (job.placedFor !== weeks || job.placedPlan !== job.plan) { job.placed = {}; job.sro = null; job.placedFor = weeks; job.placedPlan = job.plan; }
        return weeks;
    }
    // Seminars written for every other week and held in the other weeks (rows 4, 6, 8, classes 5, 7, 9) are read one week ahead: the class
    // takes the row of the week after it, none when there is no such row (observed in Политология). Decided for each kind of class of the
    // course on its own, and only when the first row stands next to the first class; lectures keep their own weeks.
    function leadOf(plan, classes, kind) {
        var steps = function (list) { return list.length >= 3 && list.every(function (week, at) { return !at || week - list[at - 1] === 2; }); };
        if (CLASSES.indexOf(kind) < 0) return 0;
        var rows = rowWeeks(plan, rowKind(plan, kind));
        return steps(classes) && steps(rows) && Math.abs(classes[0] - rows[0]) === 1 ? 1 : 0;
    }
    function placement(job, subject, kind) {
        var weeks = settled(job), id = subject + '|' + kind;
        if (!job.placed[id]) {
            var classes = weeks[id] || [], lead = leadOf(job.plan, classes, kind), wanted = rowKind(job.plan, kind), rows = {};
            if (lead) classes.forEach(function (week) { rows[week] = (job.plan.weeks[week + lead] || []).some(function (entry) { return entry.kind === wanted; }) ? [week + lead] : []; });
            else rows = align(rowWeeks(job.plan, wanted), classes);
            job.placed[id] = {classes:classes, rows:rows};
        }
        return job.placed[id];
    }
    // The course's classes on the timetable as [subject, kind, placement].
    function classesOf(job) {
        return Object.keys(model.pattern.weeks).map(function (id) { var at = id.lastIndexOf('|'); return [id.slice(0, at), id.slice(at + 1)]; })
            .filter(function (item) { return item[1] !== 'other' && item[1] !== 'sro' && match(item[0]) === job; })
            .map(function (item) { return [item[0], item[1], placement(job, item[0], item[1])]; });
    }
    // Independent work is named beside the group's nearest seminar: in its own week when a seminar is held then,
    // otherwise at a seminar a week away, first the one whose own rows stand nearest in the syllabus, then the earlier.
    function sroOf(job) {
        settled(job); if (job.sro) return job.sro;
        var seminars = classesOf(job).filter(function (item) { return CLASSES.indexOf(item[1]) >= 0; }), list = [];
        // A week that has work of its own takes no work from a neighbour, and a seminar takes one moved work at most:
        // two works of one course never meet in a week unless the syllabus itself sets them so.
        var taken = {};
        Object.keys(job.plan.weeks).forEach(function (source) { if (job.plan.weeks[source].some(function (entry) { return entry.kind === 'sro'; })) taken[source] = true; });
        Object.keys(job.plan.weeks).forEach(function (source) {
            job.plan.weeks[source].forEach(function (entry, index) {
                if (entry.kind !== 'sro') return;
                var at = entry.week, best = Infinity;
                seminars.forEach(function (item) {
                    item[2].classes.forEach(function (week) {
                        var away = Math.abs(week - entry.week); if (away > LIMITS.near || (away && taken[week])) return;
                        var near = (item[2].rows[week] || []).reduce(function (least, row) { return Math.min(least, Math.abs(row - entry.week)); }, LIMITS.weeks);
                        var rank = away * 10000 + near * 100 + week;
                        if (rank < best) { best = rank; at = week; }
                    });
                });
                taken[at] = true;
                list.push({entry:entry, at:at, key:job.href + '|' + entry.week + '|sro|' + index});
            });
        });
        return job.sro = list;
    }

    /* Finding the syllabus of each course. */
    function surnames(job) {
        var found = [];
        teachers.forEach(function (names, key) { if (key === job.key || key.indexOf(job.key) === 0 || job.key.indexOf(key) === 0) names.forEach(function (name) { if (found.indexOf(name) < 0) found.push(name); }); });
        return found;
    }
    async function resolve(job, program) {
        var doc = await read(job.href), seen = new Set(), links = [];
        doc.querySelectorAll('#l4 a[href]').forEach(function (a) {
            var url; try { url = new URL(a.getAttribute('href'), location.origin); } catch (_) { return; }
            if (url.origin !== location.origin || url.search || url.hash || !PRINT.test(url.pathname) || seen.has(url.pathname)) return;
            seen.add(url.pathname); links.push({href:url.pathname, title:clean(a).slice(0, 160)});
        });
        if (!links.length) return {status:'nosyllabus'};
        // Copies for a new year are added at the end of the list, so the newest are read first.
        links = links.slice(-LIMITS.candidates);
        var year = (job.href.match(/\/(\d{4})\/[12]\/?$/) || [])[1] || '', people = surnames(job), best = null, failed = 0;
        var full = 9 + (program.division ? 4 : 0) + (people.length ? 2 : 0);
        for (var i = links.length - 1; i >= 0; i--) {
            var parsed;
            try { parsed = parseSyllabus(await read(links[i].href)); } catch (_) { failed++; continue; }
            var score = rate(parsed, program, year, people);
            if (score < 0 || (best && score <= best.score)) continue;
            best = {score:score, link:links[i], parsed:parsed};
            if (score >= full) break;
        }
        if (!best) { if (failed) throw new Error('Syllabus unavailable'); return {status:'unmatched'}; }
        var plan = group(best.parsed.entries);
        return {status:'ready', certain:!failed || best.score >= full, plan:{href:best.link.href, title:best.link.title, weeks:plan.weeks, kinds:plan.kinds}};
    }
    function wanted(job) {
        var found = false;
        views.forEach(function (state) { if (state.subjects.some(function (subject) { return match(subject.key) === job; })) found = true; });
        return found;
    }
    function pump() {
        if (!enabled || !model || model.state !== 'ready') return;
        // Nothing is read for a schedule of another semester.
        if (!Array.from(views.values()).some(function (state) { return !foreign(state); })) return;
        var owner = model, epoch = generation;
        while (active < LIMITS.parallel) {
            var queued = owner.jobs.filter(function (job) { return job.status === 'queued'; });
            // Courses of the shown schedule come before the rest of the semester.
            var job = queued.find(wanted) || queued[0];
            if (!job) break;
            job.status = 'loading'; active++;
            (function (current) {
                resolve(current, owner.program).then(function (result) {
                    if (generation !== epoch || model !== owner) return;
                    current.status = result.status; current.plan = result.plan || null; current.certain = result.certain;
                    remember(current, owner.program);
                }, function (error) {
                    if (generation !== epoch || model !== owner) return;
                    current.status = 'error'; current.reason = String(error?.message || error).slice(0, 80);
                    // The site sometimes drops a request while a page is still loading: one more attempt by itself, then the user's button.
                    if (current.retried) return;
                    current.retried = true;
                    var timer = setTimeout(function () {
                        timers.delete(timer);
                        if (generation !== epoch || model !== owner || current.status !== 'error') return;
                        current.status = 'queued'; renderAll(true); pump();
                    }, LIMITS.again);
                    timers.add(timer);
                }).catch(function () { }).then(function () {
                    if (generation !== epoch) return;
                    active--;
                    if (model === owner) { renderAll(true); pump(); }
                });
            })(job);
        }
    }
    function boot(force) {
        var epoch = generation, owner = model = {state:'loading', reason:'', program:null, term:'', jobs:[], pattern:{state:'none', weeks:{}}};
        (async function () {
            var program = null;
            try {
                program = await studentProgram(force);
                var list = await courseList(force);
                if (generation !== epoch || model !== owner) return;
                owner.program = program;
                owner.term = ((list[0]?.href || '').match(/(\/\d{4}\/[12])\/?$/) || ['', ''])[1];
                owner.jobs = list.map(function (course) { return {name:course.name, key:fold(course.name), href:course.href, status:'queued', plan:null, certain:true}; });
                if (!force) {
                    var cachedCourses = stored(STORE).courses;
                    if (cachedCourses) owner.jobs.forEach(function (job) { recall(job, program, cachedCourses); });
                }
                owner.state = 'ready';
                loadPattern(owner, epoch, force);
            } catch (_) {
                if (generation !== epoch || model !== owner) return;
                owner.state = 'failed'; owner.reason = program ? 'courses' : 'program';
            }
            renderAll(true); pump();
        })();
    }
    function match(key) {
        if (!model || model.state !== 'ready') return null;
        return model.jobs.find(function (job) { return job.key === key; }) || model.jobs.find(function (job) { return key.indexOf(job.key) === 0 || job.key.indexOf(key) === 0; }) || null;
    }
    function refresh() {
        generation++; controllers.forEach(function (abort) { abort.abort(); }); controllers.clear(); active = 0;
        timers.forEach(clearTimeout); timers.clear();
        var cache = stored(STORE); delete cache.courses; delete cache.pattern; delete cache.list; keep(STORE, cache);
        boot(true); renderAll(true);
    }
    function retry() {
        if (!model || model.state !== 'ready') { refresh(); return; }
        model.jobs.forEach(function (job) { if (job.status === 'error') job.status = 'queued'; });
        if (model.pattern.state === 'failed') loadPattern(model, generation, true);
        renderAll(true); pump();
    }

    /* The shown week: "Неделя 11. 09.11.2026 - 15.11.2026" in the site's week list, whose option values carry the semester. */
    function shownWeek() {
        var select = document.getElementById('weekSelect');
        if (!select || select.tagName !== 'SELECT' || select.selectedIndex < 0) return null;
        var option = select.options[select.selectedIndex], number = weekNumber(option, select.selectedIndex);
        var term = (String(option.value || '').match(/\/myschedule\/(\d{4})\/([12])\//) || location.pathname.match(/^\/student\/myschedule\/(\d{4})\/([12])(?:\/|$)/) || []);
        // The week's first and last day as the option names them; the end is the day after the last one.
        var dates = (String(option.value || '') + ' ' + clean(option)).match(/\d{1,2}\.\d{1,2}\.\d{4}/g) || [];
        var day = function (value, after) { var p = value.split('.'); return new Date(+p[2], +p[1] - 1, +p[0] + after).getTime(); };
        var start = dates.length >= 2 ? day(dates[0], 0) : NaN, end = dates.length >= 2 ? day(dates[1], 1) : NaN;
        return number >= 1 && number <= LIMITS.weeks ? {number:number, term:term[1] ? '/' + term[1] + '/' + term[2] : '', start:start, end:end} : null;
    }
    // The home page lists this week's independent work from what this page found (overview.js keeps it with the account and
    // language): only the week on screen, only while it is the current one, and only from a finished reading in which every
    // course was read and the group's weeks were laid over the syllabi, so the list is the one this page shows without notes.
    function keepWork(state, items) {
        var week = state.week, now = Date.now(), overview = window.__univerOverviewV1;
        if (!overview || !overview.keepWork || !(week.start < week.end) || week.end - week.start > 7 * 86400000 || now < week.start || now >= week.end) return;
        var list = items.slice(0, 40).map(function (item) {
            return {course:item.job.name.slice(0, 400), href:item.job.href, name:(item.entry.name || '').slice(0, 80), topic:cut(item.entry.topic || '', LIMITS.topic), score:Number.isFinite(item.entry.score) && item.entry.score > 0 ? item.entry.score : 0};
        });
        var signature = language + '|' + week.start + '|' + JSON.stringify(list);
        if (signature === workKept) return;
        workKept = signature;
        overview.keepWork(language, {start:week.start, end:week.end, list:list});
    }

    /* Drawing. */
    function element(tag, className, content) {
        var node = document.createElement(tag); if (className) node.className = className;
        if (content !== undefined && content !== null) node.textContent = content;
        return node;
    }
    function button(className, content) { var node = element('button', className, content); node.type = 'button'; return node; }
    function setText(node, value) { if (node.textContent !== value) node.textContent = value; }
    function setHidden(node, hidden) { if (node.hidden !== hidden) node.hidden = hidden; }
    function setAttribute(node, name, value) { if (node.getAttribute(name) !== value) node.setAttribute(name, value); }
    function motion() { return window.__univerMotionUI; }
    // A height change is animated from where the box was; a keyboard press (detail 0) changes it at once.
    function resized(node, change, animate) {
        var before = animate && node.isConnected ? node.getBoundingClientRect().height : 0;
        motion()?.stop(node); change();
        if (animate && before > 0) motion()?.resize(node, before);
    }
    // "семинар 10 · макс. балл: 20": the entry's own name and its score. label stands for a missing name.
    function meta(entry, label) {
        var L = text(), parts = entry.name || label ? [entry.name || label] : [];
        if (entry.score > 0) parts.push(L.points(entry.score));
        return parts.join(' · ');
    }
    // One topic: three lines and 180 characters when collapsed, with its own "show in full" button.
    function topicBlock(entry, expandedSet, key, href) {
        var L = text(), box = element('div', 'univer-app-syl-entry'), topic = element('div', 'univer-app-syl-topic'), foot = element('div', 'univer-app-syl-foot');
        var more = button('univer-app-syl-more'), long = entry.topic.length > LIMITS.preview;
        var draw = function (animate) {
            var open = expandedSet.has(key);
            resized(box, function () {
                setText(topic, open ? entry.topic : cut(entry.topic, LIMITS.preview));
                topic.classList.toggle('univer-app-syl-clamped', !open);
                setText(more, open ? L.less : L.more); more.setAttribute('aria-expanded', String(open));
                note.hidden = !(open && entry.topic.length >= LIMITS.topic && /…$/.test(entry.topic));
            }, animate);
            if (animate && open) motion()?.reveal(topic, 'content');
        };
        var note = element('span', 'univer-app-syl-cut', L.cut);
        more.addEventListener('click', function (event) { if (expandedSet.has(key)) expandedSet.delete(key); else expandedSet.add(key); draw(event.detail > 0); });
        foot.append(more, note);
        if (href) { var source = element('a', 'univer-app-syl-source', L.open); source.href = href; foot.appendChild(source); }
        box.append(topic, foot); draw(false);
        // A short topic that still wraps past three lines also gets the button.
        var fitNeed = function () { return long || expandedSet.has(key) ? false : !(topic.scrollHeight - topic.clientHeight > 2); };
        box.measureFit = fitNeed;
        box.applyFit = function (hidden) { setHidden(more, hidden); };
        box.fit = function () { setHidden(more, fitNeed()); };
        more.hidden = !long;
        return box;
    }
    function fitBoxes(boxes) {
        if (!boxes.length) return;
        var hidden = boxes.map(function (box) { return box.measureFit(); });
        boxes.forEach(function (box, index) { box.applyFit(hidden[index]); });
    }
    function buildPanel(state) {
        var L = text(), P = preferences(), panel = element('section', 'univer-app-syl-panel'); panel.setAttribute('aria-label', L.title);
        var head = element('div', 'univer-app-syl-head'), heading = element('div', 'univer-app-syl-heading'), actions = element('div', 'univer-app-syl-actions');
        var title = element('strong', 'univer-app-syl-title', L.title);
        var progress = element('span', 'univer-app-syl-progress'); progress.setAttribute('role', 'status');
        heading.append(title, progress);
        var toggle = button('univer-app-syl-switch'); toggle.setAttribute('role', 'switch');
        toggle.append(element('span', 'univer-app-syl-track'), element('span', '', L.inLessons));
        var again = button('univer-app-syl-refresh', L.refresh); again.title = L.refreshHint;
        actions.append(toggle, again); head.append(heading, actions);
        var alert = element('div', 'univer-app-syl-alert'), alertText = element('span'), alertRetry = button('univer-app-syl-retry', L.retry); alert.append(alertText, alertRetry);
        var section = element('div', 'univer-app-syl-sro'), fold = button('univer-app-syl-fold'), body = element('div', 'univer-app-syl-sro-body');
        var count = element('span', 'univer-app-syl-count'); fold.append(element('span', 'univer-app-syl-fold-label', L.sro), count);
        body.id = 'univer-syl-sro-' + (++counter); fold.setAttribute('aria-controls', body.id);
        var list = element('ul', 'univer-app-syl-list'), rest = button('univer-app-syl-rest'), notes = element('div', 'univer-app-syl-notes');
        body.append(list, rest, notes); section.append(fold, body);
        panel.append(head, alert, section);
        toggle.addEventListener('click', function (event) {
            // The day cards give up or take back the room of their tasks in one move.
            var days = event.detail > 0 ? Array.from(state.view.querySelectorAll('.univer-app-day:not([hidden])')) : [];
            var before = days.map(function (day) { return day.getBoundingClientRect().height; });
            P.lessons = !P.lessons; savePreferences();
            views.forEach(function (other) { render(other, false); });
            days.forEach(function (day, index) { motion()?.stop(day); if (before[index] > 0) motion()?.resize(day, before[index]); });
        });
        again.addEventListener('click', refresh);
        alertRetry.addEventListener('click', retry);
        fold.addEventListener('click', function (event) {
            P.sro = !P.sro; savePreferences();
            views.forEach(function (other) { render(other, event.detail > 0 && other === state); });
            if (P.sro && event.detail > 0) motion()?.reveal(body, 'content');
        });
        rest.addEventListener('click', function (event) { state.allSro = !state.allSro; state.sroSignature = ''; render(state, event.detail > 0); });
        panel.addEventListener('click', function (event) { var again = event.target.closest?.('.univer-app-syl-note-retry'); if (again) retry(); });
        return {root:panel, progress:progress, toggle:toggle, alert:alert, alertText:alertText, section:section, fold:fold, count:count, body:body, list:list, rest:rest, notes:notes};
    }
    function names(list) {
        var shown = list.slice(0, LIMITS.names).join(', ');
        return list.length > LIMITS.names ? shown + ' ' + text().andMore(list.length - LIMITS.names) : shown;
    }
    function planPanel(state, animate) {
        var L = text(), P = preferences(), ui = state.panel, week = state.week.number;
        var failed = model.state === 'failed';
        var jobs = model.jobs, pending = jobs.filter(function (job) { return job.status === 'queued' || job.status === 'loading'; }).length;
        var reading = model.state === 'loading' || pending > 0 || model.pattern.state === 'loading';
        var line = reading ? (model.state === 'loading' ? L.sroPending : L.progress(jobs.length - pending, jobs.length)) : '';
        var plan = {L:L, P:P, ui:ui, failed:failed, reading:reading, line:line, items:[], notes:[], signature:'', changed:false, first:false, animateResize:false, before:0, boxes:[], reveals:[]};
        if (failed) return plan;
        // Courses in the order of their first class this week, then the rest of the semester by name.
        var order = state.subjects.map(function (subject) { return match(subject.key); }).filter(Boolean);
        var sorted = jobs.slice().sort(function (a, b) {
            var x = order.indexOf(a), y = order.indexOf(b);
            return x >= 0 && y >= 0 ? x - y : x >= 0 ? -1 : y >= 0 ? 1 : a.name.localeCompare(b.name);
        });
        var items = [], errors = [];
        sorted.forEach(function (job) {
            if (job.status === 'ready') {
                // The work is listed in the week of the seminar it stands beside.
                var found = sroOf(job).filter(function (item) { return item.at === week; });
                found.forEach(function (item) { items.push({job:job, entry:item.entry, key:item.key}); });
            } else if (job.status === 'error') errors.push(job.name);
        });
        var notes = [];
        if (errors.length) notes.push([L.failed, errors, true]);
        if (model.pattern.state === 'failed') notes.push([L.noWeeks, [], true]);
        var signature = language + '|' + week + '|' + P.sro + '|' + state.allSro + '|' + reading + '|' + items.map(function (item) { return item.key + ':' + item.entry.topic.length + ':' + item.entry.score; }).join(',') + '|' + JSON.stringify(notes);
        plan.items = items; plan.notes = notes; plan.signature = signature;
        plan.changed = signature !== state.sroSignature;
        plan.first = !state.sroSignature;
        plan.animateResize = plan.changed && animate && !plan.first;
        return plan;
    }
    function writePanel(state, plan) {
        var L = plan.L, P = plan.P, ui = plan.ui;
        setAttribute(ui.toggle, 'aria-checked', String(P.lessons));
        setHidden(ui.alert, !plan.failed); if (plan.failed) setText(ui.alertText, model.reason === 'program' ? L.noProgram : L.noCourses);
        setHidden(ui.progress, !plan.reading);
        if (plan.reading) { if (window.__univerKitV1 && ui.progress.textContent) window.__univerKitV1.roll(ui.progress, plan.line); else setText(ui.progress, plan.line); }
        if (plan.failed) return;
        if (!plan.reading && !plan.notes.length && model.state === 'ready') keepWork(state, plan.items);
        setText(ui.count, plan.items.length ? String(plan.items.length) : '');
        setHidden(ui.count, !plan.items.length);
        setAttribute(ui.fold, 'aria-expanded', String(P.sro));
        // Nothing to hand in and nothing to report: the section stays out of the way.
        setHidden(ui.section, !(plan.items.length || plan.reading || plan.notes.length));
        if (!plan.changed) return;
        state.sroSignature = plan.signature;
        motion()?.stop(ui.root);
        setHidden(ui.body, !P.sro);
        var limit = state.allSro ? plan.items.length : LIMITS.sro, known = new Map();
        Array.from(ui.list.children).forEach(function (row) { known.set(row.dataset.key, row); });
        ui.list.replaceChildren();
        plan.items.slice(0, limit).forEach(function (item, index) {
            var row = known.get(item.key);
            if (!row) {
                row = element('li', 'univer-app-syl-item'); row.dataset.key = item.key;
                var top = element('div', 'univer-app-syl-item-head'), course = element('a', 'univer-app-syl-course', item.job.name); course.href = item.job.href;
                top.append(course, element('span', 'univer-app-syl-meta', meta(item.entry, '')));
                row.block = topicBlock(item.entry, state.expanded, item.key, item.job.plan.href);
                row.append(top, row.block); row.fresh = true;
            }
            ui.list.appendChild(row);
            plan.boxes.push(row.block);
            if (row.fresh) { row.fresh = false; if (plan.animateResize && P.sro) plan.reveals.push([row, Math.min(index, 3) * 24]); }
        });
        setHidden(ui.list, !plan.items.length);
        setHidden(ui.rest, plan.items.length <= LIMITS.sro);
        setText(ui.rest, state.allSro ? L.fewer : L.rest(Math.max(0, plan.items.length - LIMITS.sro)));
        ui.notes.replaceChildren();
        plan.notes.forEach(function (note) {
            var line = element('p', 'univer-app-syl-note'); line.append(element('span', 'univer-app-syl-note-label', note[1].length ? note[0] + ': ' : note[0]), names(note[1]));
            if (note[2]) line.append(' ', button('univer-app-syl-note-retry', L.retry));
            ui.notes.appendChild(line);
        });
        setHidden(ui.notes, !plan.notes.length);
    }
    // The classes of one course and kind in a week, in order: with as many syllabus rows as classes, each class takes its own.
    function share(state, card, entries) {
        if (entries.length < 2) return entries;
        var same = state.cards.filter(function (other) { return other.key === card.key && other.kind === card.kind; })
            .sort(function (a, b) { return a.day - b.day || a.order - b.order; });
        return same.length === entries.length ? [entries[same.indexOf(card)]] : entries;
    }
    // The week's first seminar-like class of a course: the one the week's independent work is named under.
    function sroCard(state, job) {
        return state.cards.filter(function (card) { return CLASSES.indexOf(card.kind) >= 0 && match(card.key) === job; }).sort(function (a, b) { return a.day - b.day || a.order - b.order; })[0] || null;
    }
    function planLesson(state, card, animate) {
        var L = text(), P = preferences(), week = state.week.number, job = match(card.key), want = 'none', entries = [], work = [], placed = null;
        if (card.kind !== 'other' && card.kind !== 'sro') {
            if (model.state === 'loading' || model.pattern.state === 'loading' || (job && (job.status === 'queued' || job.status === 'loading'))) want = 'loading';
            else if (job && job.status === 'ready') {
                placed = placement(job, card.key, card.kind);
                entries = share(state, card, tasksFor(job.plan, week, card.kind, placed));
                if (sroCard(state, job) === card) work = sroOf(job).filter(function (item) { return item.at === week; }).map(function (item) { return item.entry; });
                if (entries.length || work.length) want = 'ready';
            }
        }
        // A double period: the later class of the same course and kind on that day names the first one instead of repeating its topic.
        var first = want === 'ready' ? state.cards.filter(function (other) { return other.day === card.day && other.key === card.key && other.kind === card.kind; }).sort(function (a, b) { return a.order - b.order; })[0] : card;
        if (first !== card) { var given = share(state, first, tasksFor(job.plan, week, first.kind, placed)); if (given.length === entries.length && given.every(function (entry, index) { return entry === entries[index]; })) want = 'same'; }
        var foldKey = card.key + '|' + card.kind, folded = !!P.folded[foldKey];
        // The class's own rows first, then the work named beside it.
        var all = entries.map(function (entry) { return {entry:entry, kind:card.kind}; }).concat(work.map(function (entry) { return {entry:entry, kind:'sro', label:L.sroName}; }));
        var signature = want + '|' + (want === 'same' ? first.time : '') + '|' + language + '|' + week + '|' + folded + '|' + card.all + '|' + all.map(function (item) { return item.kind + ':' + item.entry.week + ':' + item.entry.name + ':' + item.entry.topic.length + ':' + item.entry.score; }).join(',');
        var changed = signature !== card.signature, appear = changed && animate && P.lessons && card.signature !== undefined;
        return {state:state, card:card, L:L, P:P, job:job, want:want, first:first, foldKey:foldKey, folded:folded, all:all, signature:signature, changed:changed, appear:appear, before:0, boxes:[]};
    }
    function writeLesson(plan) {
        var state = plan.state, card = plan.card, L = plan.L, P = plan.P, job = plan.job, want = plan.want, first = plan.first, foldKey = plan.foldKey, folded = plan.folded, all = plan.all;
        if (card.block) setHidden(card.block, !P.lessons);
        if (!plan.changed) return;
        card.signature = plan.signature;
        motion()?.stop(card.card);
        if (want === 'none') { if (card.block) { motion()?.stop(card.block); card.block.remove(); card.block = null; } return; }
        var block = card.block;
        if (!block) {
            block = card.block = element('div', 'univer-app-syl-task');
            // The class card opens the course materials on a click; a click inside the task stays here.
            block.addEventListener('click', function (event) { event.stopPropagation(); });
            var anchor = card.main.querySelector(':scope>a.univer-app-page-link');
            if (anchor) anchor.before(block); else card.main.appendChild(block);
        }
        block.hidden = !P.lessons; block.dataset.state = want; block.replaceChildren();
        if (want === 'loading') { block.append(element('span', 'univer-app-syl-wait', L.loading)); return; }
        if (want === 'same') { block.append(element('span', 'univer-app-syl-same', L.same(first.time))); return; }
        var fold = button('univer-app-syl-fold'), body = element('div', 'univer-app-syl-body');
        body.id = 'univer-syl-task-' + (++counter); fold.setAttribute('aria-controls', body.id); fold.setAttribute('aria-expanded', String(!folded));
        fold.title = folded ? L.show : L.hide;
        var caption = element('span', 'univer-app-syl-fold-text');
        var name = all.length === 1 ? meta(all[0].entry, all[0].label) || L.topic : L.topics(all.length);
        caption.append(element('span', 'univer-app-syl-meta', name)); fold.setAttribute('aria-label', (folded ? L.show : L.hide) + ': ' + name);
        fold.appendChild(caption);
        fold.addEventListener('click', function (event) {
            if (P.folded[foldKey]) delete P.folded[foldKey]; else P.folded[foldKey] = 1;
            savePreferences();
            var matching = [];
            views.forEach(function (other) { other.cards.forEach(function (item) { if (item.key === card.key && item.kind === card.kind) matching.push({state:other, card:item, animate:event.detail > 0 && item === card}); }); });
            drawLessons(matching);
            if (!P.folded[foldKey] && event.detail > 0 && card.block) motion()?.reveal(card.block.querySelector('.univer-app-syl-body'), 'content');
            card.block?.querySelector('.univer-app-syl-fold')?.focus({preventScroll:true});
        });
        body.hidden = folded;
        var shown = card.all ? all : all.slice(0, LIMITS.perLesson), blocks = [];
        shown.forEach(function (item, index) {
            var box = topicBlock(item.entry, state.expanded, job.href + '|' + item.entry.week + '|' + item.kind + '|' + item.entry.name + '|' + index, index === shown.length - 1 ? job.plan.href : '');
            if (all.length > 1) box.prepend(element('span', 'univer-app-syl-entry-name', meta(item.entry, item.label)));
            body.appendChild(box); blocks.push(box);
        });
        if (all.length > LIMITS.perLesson) {
            var rest = button('univer-app-syl-rest', card.all ? L.fewer : L.rest(all.length - LIMITS.perLesson));
            rest.addEventListener('click', function (event) { card.all = !card.all; drawLessons([{state:state, card:card, animate:event.detail > 0}]); });
            body.appendChild(rest);
        }
        block.append(fold, body); block.blocks = blocks;
        plan.boxes = blocks;
    }
    function drawLessons(items) {
        var plans = items.map(function (item) { return planLesson(item.state, item.card, item.animate); });
        plans.forEach(function (plan) { if (plan.appear && plan.card.card.isConnected) plan.before = plan.card.card.getBoundingClientRect().height; });
        var boxes = [];
        plans.forEach(function (plan) { writeLesson(plan); if (plan.boxes.length) Array.prototype.push.apply(boxes, plan.boxes); });
        fitBoxes(boxes);
        plans.forEach(function (plan) {
            if (plan.appear && plan.before > 0) motion()?.resize(plan.card.card, plan.before);
            if (plan.appear && plan.card.block && plan.want === 'ready') motion()?.reveal(plan.card.block, 'content');
        });
    }
    // A schedule of another semester has other courses; nothing is read or shown for it.
    function foreign(state) { return !!model && model.state === 'ready' && !!state.week.term && !!model.term && state.week.term !== model.term; }
    function render(state, animate) {
        if (!state.view.isConnected || !model) return;
        var idle = foreign(state) || (model.state === 'ready' && !model.jobs.length);
        if (idle) {
            setHidden(state.panel.root, true);
            setAttribute(state.view, 'data-syllabus', preferences().lessons ? 'on' : 'off');
            state.cards.forEach(function (card) { if (card.block) { card.block.remove(); card.block = null; card.signature = undefined; } });
            return;
        }
        var panelPlan = planPanel(state, animate);
        var lessonPlans = state.cards.map(function (card) { return planLesson(state, card, animate); });
        // Measure heights before any DOM mutations, then write all cards, then measure and apply topic clamps in one batch.
        if (panelPlan.animateResize && panelPlan.ui.root.isConnected) panelPlan.before = panelPlan.ui.root.getBoundingClientRect().height;
        lessonPlans.forEach(function (plan) { if (plan.appear && plan.card.card.isConnected) plan.before = plan.card.card.getBoundingClientRect().height; });
        setHidden(state.panel.root, false);
        setAttribute(state.view, 'data-syllabus', preferences().lessons ? 'on' : 'off');
        writePanel(state, panelPlan);
        lessonPlans.forEach(writeLesson);
        var boxes = panelPlan.boxes.slice();
        lessonPlans.forEach(function (plan) { if (plan.boxes.length) Array.prototype.push.apply(boxes, plan.boxes); });
        fitBoxes(boxes);
        if (panelPlan.animateResize && panelPlan.before > 0) motion()?.resize(panelPlan.ui.root, panelPlan.before);
        panelPlan.reveals.forEach(function (item) { motion()?.reveal(item[0], 'content', item[1]); });
        lessonPlans.forEach(function (plan) {
            if (plan.appear && plan.before > 0) motion()?.resize(plan.card.card, plan.before);
            if (plan.appear && plan.card.block && plan.want === 'ready') motion()?.reveal(plan.card.block, 'content');
        });
    }
    function renderAll(animate) { if (enabled) views.forEach(function (state) { render(state, animate); }); }
    function attach(record) {
        var week = shownWeek(); if (!week || !record.cards?.length || !record.card?.root?.isConnected) return;
        var state = {view:record.view, week:week, cards:[], subjects:[], expanded:new Set(), allSro:false, sroSignature:''};
        record.cards.forEach(function (source) {
            var key = fold(source.subject || ''); if (!key) return;
            state.cards.push({card:source.card, main:source.main, day:source.day, order:source.order, time:source.time || '', key:key, kind:kindOf(source.type), block:null, signature:undefined, all:false});
            if (!state.subjects.some(function (subject) { return subject.key === key; })) state.subjects.push({key:key, name:source.subject});
            var surname = fold(source.teacher || '').split(/\s+/)[0];
            if (surname.length >= 3) { if (!teachers.has(key)) teachers.set(key, new Set()); teachers.get(key).add(surname); }
        });
        state.panel = buildPanel(state);
        record.card.root.after(state.panel.root);
        views.set(record.view, state);
        if (!model) boot(false);
        render(state, false);
        pump();
    }
    function css() {
        return [
            ':root.univer-mobile .univer-app-syl-panel { grid-column:1/-1; min-width:0; margin:0 0 12px; padding:12px 16px; background-color:#fff; border:1px solid var(--u-line); border-radius:12px; box-shadow:var(--u-shadow); }',
            ':root:root:root:root:root:root:root.univer-mobile #l4 :is(.univer-app-syl-panel,.univer-app-syl-task) [hidden],:root:root:root:root:root:root:root.univer-mobile #l4 :is(.univer-app-syl-panel,.univer-app-syl-task)[hidden] { display:none!important; }',
            ':root.univer-mobile .univer-app-schedule[data-mode=grid]>.univer-app-syl-panel { display:none!important; }',
            ':root.univer-mobile .univer-app-syl-head { display:flex; align-items:center; justify-content:space-between; gap:4px 16px; flex-wrap:wrap; }',
            ':root.univer-mobile .univer-app-syl-heading { display:flex; align-items:center; gap:4px 12px; flex-wrap:wrap; min-width:0; min-height:40px; }',
            ':root.univer-mobile .univer-app-syl-title { color:var(--u-ink); font-size:var(--u-fs-section); font-weight:600; line-height:1.3; }',
            ':root.univer-mobile .univer-app-syl-progress { color:var(--u-muted); font-size:var(--u-fs-meta); line-height:1.4; font-variant-numeric:tabular-nums; }',
            ':root.univer-mobile .univer-app-syl-progress { display:inline-flex; align-items:center; gap:8px; }',
            ':root.univer-mobile :is(.univer-app-syl-progress,.univer-app-syl-wait)::before { content:""; flex:0 0 auto; width:12px; height:12px; box-sizing:border-box; border:1.5px solid var(--u-brand-line); border-top-color:var(--u-brand); border-radius:50%; animation:univer-app-syl-turn .9s linear infinite; }',
            '@keyframes univer-app-syl-turn { to { transform:rotate(360deg); } }',
            ':root.univer-mobile .univer-app-syl-actions { display:flex; align-items:center; gap:4px; flex-wrap:wrap; margin-left:-8px; }',
            ':root:root:root:root:root:root.univer-mobile #l4 button:is(.univer-app-syl-switch,.univer-app-syl-refresh,.univer-app-syl-retry) { display:inline-flex; align-items:center; gap:8px; height:var(--u-control)!important; min-height:var(--u-control)!important; min-width:0!important; margin:0!important; padding:0 12px!important; border:0!important; border-radius:8px!important; background:transparent!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:var(--u-control-font)!important; font-weight:500!important; line-height:1.25!important; white-space:nowrap; cursor:pointer; transition:background-color .14s ease; }',
            ':root:root:root:root:root:root.univer-mobile #l4 button:is(.univer-app-syl-refresh,.univer-app-syl-retry) { color:var(--u-brand)!important; }',
            ':root:root:root:root:root:root.univer-mobile #l4 button:is(.univer-app-syl-switch,.univer-app-syl-refresh,.univer-app-syl-retry):hover { background:var(--u-brand-soft)!important; }',
            ':root.univer-mobile .univer-app-syl-track { position:relative; flex:0 0 32px; width:32px; height:20px; border-radius:10px; background:var(--u-line-strong); transition:background-color .18s ease; }',
            ':root.univer-mobile .univer-app-syl-track::after { content:""; position:absolute; top:2px; left:2px; width:16px; height:16px; border-radius:50%; background-color:#fff; box-shadow:0 1px 2px rgba(16,40,70,.24); transition:transform .18s var(--u-ease-out, ease); }',
            ':root.univer-mobile .univer-app-syl-switch[aria-checked=true] .univer-app-syl-track { background:var(--u-brand); }',
            ':root.univer-mobile .univer-app-syl-switch[aria-checked=true] .univer-app-syl-track::after { transform:translateX(12px); }',
            ':root.univer-mobile .univer-app-syl-alert { display:flex; align-items:center; gap:4px 12px; flex-wrap:wrap; margin-top:8px; padding:8px 12px; border-radius:8px; background:var(--u-danger-soft); color:var(--u-danger); font-size:var(--u-fs-body); line-height:1.45; }',
            ':root.univer-mobile .univer-app-syl-sro { margin-top:8px; border-top:1px solid var(--u-line); }',
            ':root:root:root:root:root:root.univer-mobile #l4 .univer-app-syl-panel button.univer-app-syl-fold { display:flex; align-items:center; gap:8px; width:100%; height:var(--u-control)!important; min-height:var(--u-control)!important; margin:0!important; padding:0!important; border:0!important; border-radius:8px!important; background:transparent!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:var(--u-fs-strong)!important; font-weight:600!important; line-height:1.25!important; text-align:start; cursor:pointer; }',
            ':root.univer-mobile #l4 button.univer-app-syl-fold::after { content:""; flex:0 0 auto; width:6px; height:6px; margin-left:auto; margin-right:4px; border-right:1.5px solid currentColor; border-bottom:1.5px solid currentColor; transform:translateY(-2px) rotate(45deg); transition:transform .18s var(--u-ease-out, ease); }',
            ':root.univer-mobile #l4 button.univer-app-syl-fold[aria-expanded=true]::after { transform:translateY(2px) rotate(225deg); }',
            ':root.univer-mobile #l4 button.univer-app-syl-fold:hover .univer-app-syl-fold-label { color:var(--u-brand); }',
            ':root.univer-mobile .univer-app-syl-count { min-width:20px; padding:0 6px; box-sizing:border-box; border-radius:6px; background:var(--u-brand); color:#fff; font-size:var(--u-fs-caption); font-weight:600; line-height:20px; text-align:center; font-variant-numeric:tabular-nums; }',
            ':root.univer-mobile #l4 ul.univer-app-syl-list { margin:0!important; padding:0!important; list-style:none; }',
            ':root.univer-mobile #l4 li.univer-app-syl-item { display:block; margin:0!important; padding:12px 0!important; border-top:1px solid var(--u-line); list-style:none; }',
            ':root.univer-mobile #l4 li.univer-app-syl-item:first-child { border-top:0; padding-top:4px!important; }',
            ':root.univer-mobile .univer-app-syl-item-head { display:flex; align-items:baseline; justify-content:space-between; gap:0 16px; flex-wrap:wrap; }',
            ':root:root:root:root:root:root.univer-mobile #l4 a.univer-app-syl-course { display:inline!important; min-height:0!important; margin:0!important; padding:0!important; border:0!important; background:none!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:var(--u-fs-strong)!important; font-weight:600!important; line-height:1.35!important; text-decoration:none!important; }',
            ':root:root:root:root:root:root.univer-mobile #l4 a.univer-app-syl-course:hover { color:var(--u-brand)!important; }',
            ':root.univer-mobile .univer-app-syl-meta { color:var(--u-muted); font-size:var(--u-fs-meta); font-weight:500; line-height:1.4; font-variant-numeric:tabular-nums; }',
            ':root.univer-mobile .univer-app-syl-topic { margin:4px 0 0; color:var(--u-text); font-size:var(--u-fs-body); font-weight:400; line-height:1.5; overflow-wrap:anywhere; }',
            ':root.univer-mobile .univer-app-syl-topic.univer-app-syl-clamped { display:-webkit-box; -webkit-box-orient:vertical; -webkit-line-clamp:3; overflow:hidden; }',
            ':root.univer-mobile .univer-app-syl-foot { display:flex; align-items:center; gap:0 16px; flex-wrap:wrap; }',
            ':root.univer-mobile .univer-app-syl-foot:not(:has(>:not([hidden]))) { display:none; }',
            ':root:root:root:root:root:root.univer-mobile #l4 :is(button.univer-app-syl-more,button.univer-app-syl-rest,button.univer-app-syl-note-retry,a.univer-app-syl-source) { display:inline-flex!important; align-items:center; width:auto!important; height:32px!important; min-height:32px!important; min-width:0!important; margin:0!important; padding:0!important; border:0!important; border-radius:6px!important; background:none!important; box-shadow:none!important; color:var(--u-brand)!important; font-size:var(--u-fs-meta)!important; font-weight:500!important; line-height:1.25!important; text-decoration:none!important; white-space:nowrap; cursor:pointer; }',
            ':root:root:root:root:root:root.univer-mobile #l4 :is(button.univer-app-syl-more,button.univer-app-syl-rest,button.univer-app-syl-note-retry,a.univer-app-syl-source):hover { text-decoration:underline!important; text-underline-offset:3px; }',
            ':root:root:root:root:root:root.univer-mobile #l4 button.univer-app-syl-note-retry { display:inline!important; height:auto!important; min-height:0!important; font-size:inherit!important; }',
            ':root.univer-mobile .univer-app-syl-cut { color:var(--u-muted); font-size:var(--u-fs-meta); }',
            ':root.univer-mobile .univer-app-syl-notes { padding:8px 0 4px; border-top:1px solid var(--u-line); }',
            ':root.univer-mobile #l4 p.univer-app-syl-note { margin:0!important; padding:2px 0!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; line-height:1.45!important; }',
            ':root.univer-mobile .univer-app-syl-note-label { color:var(--u-text); font-weight:500; }',
            /* Under a class: a quiet block hung on a hairline, so it reads as a note to the class and not as another card. */
            ':root.univer-mobile #l4 .univer-app-syl-task { margin:8px 0 0; padding:0 0 0 12px; border-left:2px solid var(--u-brand-line); cursor:default; }',
            ':root.univer-mobile #l4 .univer-app-lesson-now .univer-app-syl-task { border-left-color:var(--u-brand); }',
            ':root:root:root:root:root:root.univer-mobile #l4 .univer-app-syl-task button.univer-app-syl-fold { display:flex; align-items:flex-start; gap:8px; width:100%; height:auto!important; min-height:28px!important; min-width:0!important; margin:0!important; padding:4px 0!important; border:0!important; border-radius:6px!important; background:transparent!important; box-shadow:none!important; color:var(--u-ink)!important; font-size:var(--u-fs-meta)!important; font-weight:400!important; line-height:1.4!important; text-align:start; cursor:pointer; }',
            ':root.univer-mobile #l4 .univer-app-syl-task button.univer-app-syl-fold::after { margin-top:5px; }',
            ':root.univer-mobile .univer-app-syl-fold-text { display:flex; align-items:baseline; gap:0 8px; flex-wrap:wrap; flex:1 1 auto; min-width:0; }',
            ':root.univer-mobile .univer-app-syl-task:is([data-state=loading],[data-state=same]) { display:flex; align-items:center; gap:0 8px; flex-wrap:wrap; min-height:28px; }',
            ':root.univer-mobile .univer-app-syl-same { color:var(--u-muted); font-size:var(--u-fs-meta); line-height:1.4; font-variant-numeric:tabular-nums; }',
            ':root.univer-mobile .univer-app-syl-wait { display:inline-flex; align-items:center; gap:8px; color:var(--u-muted); font-size:var(--u-fs-meta); line-height:1.4; }',
            ':root.univer-mobile .univer-app-syl-task .univer-app-syl-topic { margin-top:0; }',
            ':root.univer-mobile .univer-app-syl-entry+.univer-app-syl-entry { margin-top:8px; }',
            ':root.univer-mobile .univer-app-syl-entry-name { display:block; color:var(--u-muted); font-size:var(--u-fs-meta); font-weight:500; line-height:1.4; font-variant-numeric:tabular-nums; }',
            ':root.univer-mobile .univer-app-schedule[data-syllabus=off] .univer-app-syl-task { display:none!important; }'
        ].join('\n');
    }
    // records: the schedule adapter's agenda views with their class cards.
    function apply(lang, records) {
        if (document.readyState === 'loading') return;
        language = lang; enabled = true;
        if (!style || !style.isConnected) { style = document.createElement('style'); style.id = 'univer-syllabus-style'; style.textContent = css(); (document.head || document.documentElement).appendChild(style); }
        views.forEach(function (state, view) { if (!view.isConnected) views.delete(view); });
        (records || []).forEach(function (record) { if (record?.view && !views.has(record.view)) attach(record); });
    }
    function release() {
        enabled = false; generation++; active = 0; model = null; programPromise = null; prefs = null; storeCache = null; storeOwner = null;
        controllers.forEach(function (abort) { abort.abort(); }); controllers.clear(); teachers.clear(); timers.forEach(clearTimeout); timers.clear();
        views.forEach(function (state) {
            motion()?.stop(state.panel.root); state.panel.root.remove(); state.view.removeAttribute('data-syllabus');
            state.cards.forEach(function (card) { card.block?.remove(); });
        });
        views.clear(); style?.remove(); style = null;
    }
    // state: what QA reads to tell a finished page from a waiting one; course names and statuses only.
    function summary() { return {model:model ? model.state : 'off', reason:model?.reason || '', weeks:model ? model.pattern.state : 'off', jobs:(model?.jobs || []).map(function (job) { return {name:job.name, status:job.status, reason:job.reason || ''}; })}; }
    window.__univerSyllabusV1 = {apply:apply, release:release, state:summary, limits:LIMITS, parseSyllabus:parseSyllabus, parseProgram:parseProgram, sroKind:sroKind, kindOf:kindOf, rate:rate, leadOf:leadOf, tasksFor:tasksFor, parseWeek:parseWeek, align:align};
})();
