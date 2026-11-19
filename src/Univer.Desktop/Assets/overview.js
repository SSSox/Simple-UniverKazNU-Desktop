/* Read-only summaries on the tab pages: Бакалавр shows today's classes and the grade summary, the Е-услуги desk
   shows the latest requests. Each is read from the site's own GET page once per visit and never changes it.
   What a panel showed is kept in the site's storage of this profile, so the next visit draws it at once and the page read
   after it redraws only what differs. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerOverviewV1) return;
    var common = window.__univerCommonV1, clean = common.clean;
    var language = 'ru', active = false, owned = [], loads = new Map(), generation = 0, scheduleTimer;
    function text() {
        return language === 'en' ? {room:'Room', today:'Today', grades:'Grades', requests:'My requests', loading:'Loading…', failed:'Could not load. Open the page itself.', stale:'Saved {0}. Could not refresh.', noClasses:'No classes today',
                allSchedule:'Full schedule', allGrades:'All grades', allRequests:'All requests', noRequests:'No requests yet', now:'Now', next:'Next', done:'Finished', noGrades:'No grades yet', showAll:'Show all', collapse:'Show less', nextClasses:'Next classes', open:'Open request', upNow:'Class in progress', upNext:'Next class', then:'Then', last:'Last class today',
                work:'Independent work this week', workRead:'Read from the schedule on {0}', points:'max score: ',
                days:['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday']} :
            language === 'kz' ? {room:'Дәрісхана', today:'Бүгін', grades:'Үлгерім', requests:'Менің өтінімдерім', loading:'Жүктелуде…', failed:'Жүктеу мүмкін болмады. Бетті ашыңыз.', stale:'{0} сақталған. Жаңарту мүмкін болмады.', noClasses:'Бүгін сабақ жоқ',
                allSchedule:'Толық кесте', allGrades:'Барлық бағалар', allRequests:'Барлық өтінімдер', noRequests:'Өтінімдер әлі жоқ', now:'Қазір', next:'Келесі', done:'Аяқталды', noGrades:'Бағалар әлі жоқ', showAll:'Барлығын көрсету', collapse:'Жасыру', nextClasses:'Келесі сабақтар', open:'Өтінімді ашу', upNow:'Сабақ жүріп жатыр', upNext:'Келесі сабақ', then:'Содан кейін', last:'Бүгінгі соңғы сабақ',
                work:'Осы аптадағы БӨЖ', workRead:'Кестеден оқылды: {0}', points:'ең жоғары балл: ',
                days:['Жексенбі','Дүйсенбі','Сейсенбі','Сәрсенбі','Бейсенбі','Жұма','Сенбі']} :
            {room:'Ауд.', today:'Сегодня', grades:'Успеваемость', requests:'Мои заявки', loading:'Загрузка…', failed:'Не удалось загрузить. Откройте страницу целиком.', stale:'Сохранено {0}. Обновить не удалось.', noClasses:'Сегодня пар нет',
                allSchedule:'Всё расписание', allGrades:'Вся успеваемость', allRequests:'Все заявки', noRequests:'Заявок пока нет', now:'Идёт', next:'Следующая', done:'Прошла', noGrades:'Оценок пока нет', showAll:'Показать все', collapse:'Свернуть', nextClasses:'Ближайшие пары', open:'Открыть заявку', upNow:'Идёт пара', upNext:'Следующая пара', then:'Затем', last:'Это последняя пара сегодня',
                work:'СРО на этой неделе', workRead:'Прочитано в расписании {0}', points:'макс. балл: ',
                days:['Воскресенье','Понедельник','Вторник','Среда','Четверг','Пятница','Суббота']};
    }
    // The same countdown words as the schedule page's summary card: "через 2 ч 15 мин", "ещё 25 мин".
    function duration(m) {
        var u = language === 'en' ? ['d','h','min'] : language === 'kz' ? ['күн','сағ','мин'] : ['д','ч','мин'];
        if (m < 60) return m + ' ' + u[2];
        if (m < 1440) return Math.floor(m / 60) + ' ' + u[1] + (m % 60 ? ' ' + m % 60 + ' ' + u[2] : '');
        var hours = Math.floor(m % 1440 / 60);
        return Math.floor(m / 1440) + ' ' + u[0] + (hours ? ' ' + hours + ' ' + u[1] : '');
    }
    function until(m) { return language === 'en' ? 'in ' + duration(m) : language === 'kz' ? duration(m) + ' кейін' : 'через ' + duration(m); }
    function left(m) { return language === 'en' ? duration(m) + ' left' : language === 'kz' ? duration(m) + ' қалды' : 'ещё ' + duration(m); }
    // A label that counts down to a moment; the half-minute refresh rewrites only its words, so the panel does not redraw every minute.
    function counter(node, prefix, kind, moment) { node.dataset.univerCountAt = String(moment); node.dataset.univerCountKind = kind; node.dataset.univerCountPrefix = prefix; tick(node, Date.now()); return node; }
    function tick(node, now) {
        var m = Math.max(1, Math.ceil((+node.dataset.univerCountAt - now) / 60000)), value = node.dataset.univerCountPrefix + (node.dataset.univerCountKind === 'left' ? left(m) : until(m));
        if (node.textContent !== value) node.textContent = value;
    }
    function tickCounts(root) { var now = Date.now(); root.querySelectorAll('[data-univer-count-at]').forEach(function (node) { tick(node, now); }); }
    function element(tag, cls, content) { var e = document.createElement(tag); if (cls) e.className = cls; if (content != null) e.textContent = content; return e; }
    async function read(url) {
        var abort = new AbortController(), timeout = setTimeout(function () { abort.abort(); }, 12000);
        try {
            var response = await fetch(url, {credentials:'same-origin', cache:'no-store', signal:abort.signal});
            var final = new URL(response.url, location.origin);
            if (response.status !== 200 || final.origin !== location.origin || /^\/user\/login/i.test(final.pathname)) throw new Error('Unavailable');
            var html = await response.text(); if (html.length > 4 * 1024 * 1024) throw new Error('Oversized');
            var doc = new DOMParser().parseFromString(html, 'text/html');
            if (doc.querySelector('#login_frm')) throw new Error('Session expired');
            return doc;
        } finally { clearTimeout(timeout); }
    }
    // A failed read (timeout, network) is forgotten so the next render tries again instead of reusing the rejection.
    function once(key, url) {
        if (!loads.has(key)) { var load = read(url); loads.set(key, load); load.catch(function () { if (loads.get(key) === load) loads.delete(key); }); }
        return loads.get(key);
    }
    // The last visit's panels. They belong to the account that signed in here (the mark the sign-in form leaves) and to one
    // language; without the mark nothing is read or written, and another account's or a damaged record is removed. Only
    // parsed text is kept, never the site's markup. The browser extension keeps none (__univerOverviewKeep === false).
    var KEY = 'univer.desktop.overview.v1', LIMIT = 200000, WEEK = 7 * 86400000, AGE = 14 * 86400000;
    var keeps = window.__univerOverviewKeep !== false;
    function owner() { try { return localStorage.getItem('univer.app.account.v1') || ''; } catch (_) { return ''; } }
    function forget() { try { localStorage.removeItem(KEY); } catch (_) { } }
    function kept() {
        var who = keeps ? owner() : ''; if (!who) return null;
        try {
            var raw = localStorage.getItem(KEY); if (!raw) return null;
            var all = raw.length <= LIMIT ? JSON.parse(raw) : null;
            if (!all || typeof all !== 'object' || all.owner !== who) { forget(); return null; }
            return all;
        } catch (_) { forget(); return null; }
    }
    function saved(name, valid) {
        var all = kept(), part = all && all.language === language ? all[name] : null;
        try { return part && Number.isFinite(part.at) && part.at <= Date.now() + 60000 && valid(part) ? part : null; } catch (_) { return null; }
    }
    // lang: the page's language when another page writes here (the schedule's independent work), since only apply() on
    // the home pages sets this module's own.
    function keep(name, part, valid, lang) {
        var who = keeps ? owner() : '', as = lang || language; if (!who) return;
        try {
            part.at = Date.now(); if (!valid(part)) return;
            var all = kept(); if (!all || all.language !== as) all = {owner:who, language:as};
            all[name] = part;
            var raw = JSON.stringify(all); if (raw.length <= LIMIT) localStorage.setItem(KEY, raw);
        } catch (_) { }
    }
    function words(value, max) { return typeof value === 'string' && value.length <= (max || 400); }
    function isLessons(list) {
        return Array.isArray(list) && list.length <= 40 && list.every(function (l) {
            return l && Number.isInteger(l.start) && Number.isInteger(l.end) && l.start >= 0 && l.end <= 1500 && words(l.subject) && words(l.details, 2000) &&
                l.parts && words(l.parts.name) && Array.isArray(l.parts.rest) && l.parts.rest.length <= 12 && l.parts.rest.every(function (v) { return words(v); }) &&
                ['type','kind','room','faculty','teacher','period'].every(function (k) { return words(l.parts[k]); });
        });
    }
    function isWeek(part) {
        return Number.isFinite(part.start) && Number.isFinite(part.end) && part.end > part.start && part.end - part.start <= WEEK && Array.isArray(part.days) && part.days.length <= 8 && part.days.every(isLessons) &&
            (part.following == null || Number.isFinite(part.following.date) && isLessons(part.following.lessons));
    }
    function isCourses(part) {
        return Date.now() - part.at < AGE && Array.isArray(part.list) && part.list.length <= 80 && part.list.every(function (c) {
            return c && words(c.name) && words(c.token, 2000) && Array.isArray(c.scores) && c.scores.length <= 8 &&
                c.scores.every(function (s) { return s && words(s.label, 40) && words(s.max, 40) && words(s.value, 40); });
        });
    }
    // The week's independent work as the schedule page listed it (syllabus-tasks.js): only the week it showed, only while
    // that week was the current one, consultations left out. The site's own scripts can write this storage too, so every
    // field is bounded and the course address must be a UMKD course page.
    var COURSE = /^\/student\/umkd\/\d{1,12}\/\d{4}\/[12]$/;
    function isWork(part) {
        return Number.isFinite(part.start) && Number.isFinite(part.end) && part.end > part.start && part.end - part.start <= WEEK &&
            Array.isArray(part.list) && part.list.length <= 40 && part.list.every(function (w) {
                return w && words(w.course) && typeof w.href === 'string' && COURSE.test(w.href) && words(w.name, 80) && words(w.topic, 1200) &&
                    Number.isFinite(w.score) && w.score >= 0 && w.score <= 1000;
            });
    }
    function isRequests(part) {
        return Date.now() - part.at < AGE && Array.isArray(part.list) && part.list.length <= 60 && part.list.every(function (r) {
            return r && words(r.service) && words(r.date, 40) && words(r.sort, 80) && words(r.status) && /^\/ssc\/(?:StatementPage\?statementId=\d{1,12}|StatementList)$/.test(r.href);
        });
    }
    // The site's own sign-out link: the next account must not find these panels (the app also replaces the whole profile).
    if (keeps) document.addEventListener('click', function (event) {
        var link = event.target && event.target.closest ? event.target.closest('a[href]') : null;
        try { if (link && /^\/user\/logout\/?$/i.test(new URL(link.getAttribute('href'), location.href).pathname)) forget(); } catch (_) { }
    }, true);
    function minutes(value) { var m = value.split(':'); return +m[0] * 60 + +m[1]; }
    function clock(value) { return String(Math.floor(value / 60)).padStart(2, '0') + ':' + String(value % 60).padStart(2, '0'); }
    // Today's column of the weekly table, rowspans resolved, the same reading as the schedule page itself.
    function lessonsToday(doc, now) { return lessonsOn(doc, now.getDay() === 0 ? -1 : now.getDay()); }
    function columns(doc) { var table = doc.querySelector('table.schedule'); return table && table.rows[0] ? table.rows[0].cells.length : 0; }
    function lessonsOn(doc, column) {
        var table = doc.querySelector('table.schedule'); if (!table) return null;
        var rows = Array.from(table.rows).filter(function (r) { return r.closest('table') === table; }), header = rows[0];
        if (!header || column < 1 || column >= header.cells.length) return [];
        var grid = [];
        rows.forEach(function (row, r) {
            grid[r] = grid[r] || []; var c = 0;
            Array.from(row.cells).forEach(function (cell) {
                while (grid[r][c]) c++;
                for (var y = 0; y < cell.rowSpan; y++) { grid[r + y] = grid[r + y] || []; for (var x = 0; x < cell.colSpan; x++) grid[r + y][c + x] = {cell:cell, start:r, end:r + cell.rowSpan - 1}; }
                c += cell.colSpan;
            });
        });
        var lessons = [];
        grid.slice(1).forEach(function (line, i) {
            var slot = line[column]; if (!slot || slot.start !== i + 1 || !clean(slot.cell)) return;
            var from = clean(grid[slot.start]?.[0]?.cell).match(/\d{1,2}:\d{2}/g) || [], to = clean(grid[Math.min(slot.end, grid.length - 1)]?.[0]?.cell).match(/\d{1,2}:\d{2}/g) || [];
            if (!from.length || !to.length) return;
            var groups = Array.from(slot.cell.querySelectorAll('.groups>div')); if (!groups.length) groups = [slot.cell];
            groups.forEach(function (group) {
                var parts = Array.from(group.querySelectorAll('p')); if (!parts.length) parts = [group];
                var texts = parts.map(function (p) { return p.classList.contains('params') ? Array.from(p.childNodes).map(clean).filter(Boolean).join(' / ') : clean(p); }).filter(Boolean);
                if (texts.length) lessons.push({start:minutes(from[0]), end:minutes(to[to.length - 1]), subject:texts[0], details:texts.slice(1).join(' · '), parts:common.lesson(texts)});
            });
        });
        return lessons.sort(function (a, b) { return a.start - b.start; });
    }
    function weekRange(value) {
        var dates = (value || '').match(/\d{1,2}\.\d{1,2}\.\d{4}/g) || [];
        return dates.length >= 2 ? dates.map(function (d) { var p = d.split('.'); return new Date(+p[2], +p[1] - 1, +p[0]); }) : null;
    }
    // The week a schedule page shows, when it is the current one: the site's own dates and every column's classes.
    function weekOf(doc, now) {
        var select = doc.getElementById('weekSelect'), table = doc.querySelector('table.schedule'); if (!select || !table) return null;
        var options = Array.from(select.options), current = options.find(function (o) { return o.selected || o.hasAttribute('selected'); }) || options[0];
        var range = current && weekRange(current.value + ' ' + current.textContent), start = range ? range[0].getTime() : NaN;
        // The week ends with the last day the site names: one that starts midweek is not next Monday's.
        var end = range ? range[range.length - 1].getTime() + 86400000 : NaN;
        if (!(now.getTime() >= start && now.getTime() < end)) return null;
        var days = []; for (var column = 1; column < columns(doc); column++) days.push(lessonsOn(doc, column) || []);
        return {start:start, end:end, days:days};
    }
    // On a day without classes: the next day with classes later this week, or the first one of the following week.
    // The following week is the site's own week link (the week selector's option address), read like the current one.
    async function nextClasses(doc, now) {
        for (var column = now.getDay() === 0 ? 7 : now.getDay() + 1; column < columns(doc); column++) {
            var found = lessonsOn(doc, column);
            if (found && found.length) return {date:new Date(now.getFullYear(), now.getMonth(), now.getDate() + column - now.getDay()), lessons:found};
        }
        var select = doc.getElementById('weekSelect'); if (!select) return null;
        var options = Array.from(select.options), current = options.find(function (o) { return o.selected || o.hasAttribute('selected'); }) || options[0];
        var range = current && weekRange(current.value + ' ' + current.textContent); if (!range) return null;
        var next = options.map(function (o) { return {option:o, range:weekRange(o.value + ' ' + o.textContent)}; })
            .filter(function (o) { return o.range && o.range[0] > range[range.length - 1]; }).sort(function (a, b) { return a.range[0] - b.range[0]; })[0];
        if (!next || !/^\/student\/myschedule\//.test(next.option.value)) return null;
        var following = await once('schedule:' + next.option.value, next.option.value);
        for (var c = 1; c < columns(following); c++) {
            var lessons = lessonsOn(following, c);
            if (lessons && lessons.length) return {date:new Date(next.range[0].getFullYear(), next.range[0].getMonth(), next.range[0].getDate() + c - 1), lessons:lessons};
        }
        return null;
    }
    // Long lists show their first items; the rest open in place with the same motion as other disclosures.
    function limit(list, count) {
        var items = Array.from(list.children); if (items.length <= count) return null;
        items.slice(count).forEach(function (li) { li.hidden = true; });
        var button = element('button', 'univer-app-overview-more'); button.type = 'button'; button.setAttribute('aria-expanded', 'false');
        var paint = function (open) { button.textContent = open ? text().collapse : text().showAll + ' (' + items.length + ')'; button.setAttribute('aria-expanded', String(open)); };
        button.addEventListener('click', function (event) {
            var open = button.getAttribute('aria-expanded') !== 'true';
            var before = list.getBoundingClientRect().height; window.__univerMotionUI?.stop(list);
            items.slice(count).forEach(function (li, index) {
                window.__univerMotionUI?.stop(li); li.hidden = !open;
                if (open && event.detail > 0) window.__univerMotionUI?.reveal(li, 'content', Math.min(index, 2) * 24);
            });
            if (event.detail > 0) window.__univerMotionUI?.resize(list, before, open ? 220 : 180);
            paint(open);
        });
        paint(false); return button;
    }
    function scores(value) {
        var found = [], pattern = /((?:РК|RK|МБ|АБ)\s*[12]|(?:Экз|Exam|Емтихан|Итог|Total)\p{L}*)\s*(?:\(\s*(\d+(?:[.,]\d+)?)\s*\))?\s*:\s*(-?\d+(?:[.,]\d+)?)?/giu, match;
        while ((match = pattern.exec(value))) found.push({label:match[1].replace(/\s+/g, ''), max:match[2] || '', value:match[3] || ''});
        return found;
    }
    // Course headings carry the teaching dates; the bold "РК1 (100): 51 …" cell after each is its total.
    function courseScores(doc) {
        var courses = [], current = null;
        doc.querySelectorAll('#l4 .ct').forEach(function (cell) {
            var value = clean(cell);
            if (/\d{2}\.\d{2}\.\d{4}\s*[-–]\s*\d{2}\.\d{2}\.\d{4}/.test(value)) {
                current = {name:(value.match(/^(.*?)\s*(?:\(\[|\d{2}\.\d{2}\.\d{4})/)?.[1] || value).trim(), token:value.normalize('NFC').toLocaleLowerCase(), scores:[]}; courses.push(current);
            } else if (current && !current.scores.length && cell.querySelector('b,strong') && /(?:РК|RK|МБ|АБ)\s*[12]/i.test(value)) current.scores = scores(value);
        });
        return courses.filter(function (c) { return c.name; });
    }
    function requests(doc) {
        var table = Array.from(doc.querySelectorAll('#l4 table')).filter(function (t) { return !t.closest('#tools') && !t.querySelector('table') && t.rows.length; })
            .sort(function (a, b) { return b.rows.length - a.rows.length; })[0];
        if (!table) return [];
        // Columns by their headings (Услуга, Дата, Статус); the row id is the request number the site opens.
        var head = Array.from(table.rows).find(function (r) { return r.querySelector('th'); }), names = head ? Array.from(head.cells).map(clean) : [];
        var column = function (pattern) { return names.findIndex(function (n) { return pattern.test(n); }); };
        var serviceAt = column(/услуг|қызмет|service/i), dateAt = column(/дата|күні|date/i), statusAt = column(/статус|күй|status/i);
        return Array.from(table.rows).filter(function (r) { return !r.querySelector('th'); }).map(function (row) {
            var cells = Array.from(row.cells).map(clean), at = dateAt >= 0 ? dateAt : cells.findIndex(function (c) { return /\d{2}\.\d{2}\.\d{4}/.test(c); });
            var date = (cells[at] || '').match(/(\d{2})\.(\d{2})\.(\d{4})/);
            if (!date) return null;
            var id = /^\d{1,12}$/.test(row.id) ? row.id : row.querySelector('input[type=checkbox][value]')?.value;
            return {service:cells[serviceAt >= 0 ? serviceAt : at - 1], date:cells[at].replace(/:\d{2}$/, ''), sort:date[3] + date[2] + date[1] + cells[at],
                status:cells[statusAt >= 0 ? statusAt : cells.length - 1], href:/^\d{1,12}$/.test(id || '') ? '/ssc/StatementPage?statementId=' + id : '/ssc/StatementList'};
        }).filter(Boolean).sort(function (a, b) { return a.sort < b.sort ? 1 : -1; });
    }
    function tone(status) {
        var v = status.toLocaleLowerCase();
        return /заверш|выполн|готов|выдан|аяқта|орында|дайын|complete|done|ready|issued/.test(v) ? 'done' : /отклон|отказ|аннул|бас тарт|reject|declin|cancel/.test(v) ? 'fail' :
            /обработ|рассмотр|нов|ожида|қаралу|өңде|process|review|pending|new/.test(v) ? 'wait' : '';
    }
    function pending() { return element('p', 'univer-app-pending', text().loading); }
    function panel(cls, title, href, linkText) {
        var box = element('section', 'univer-app-overview ' + cls); box.setAttribute('aria-label', title);
        var head = element('div', 'univer-app-overview-head'), h = element('h2', '', title), link = element('a', 'univer-app-overview-link', linkText);
        link.href = href; head.append(h, link); box.appendChild(head);
        var body = element('div', 'univer-app-overview-body'); body.appendChild(pending()); box.appendChild(body);
        return {box:box, body:body};
    }
    function settled(body, render) {
        if (window.__univerMotionUI) window.__univerMotionUI.change(body, render, 'content'); else render();
    }
    function fail(body) { settled(body, function () { body.replaceChildren(element('p', 'univer-app-overview-empty', text().failed)); }); }
    // Kazakh month names from our own list: whether Chromium 109 and old Android WebViews format 'kk' dates was never checked.
    var KZ_MONTHS = ['қаңтар','ақпан','наурыз','сәуір','мамыр','маусым','шілде','тамыз','қыркүйек','қазан','қараша','желтоқсан'];
    function dayMonth(date) {
        return language === 'kz' ? date.getDate() + ' ' + KZ_MONTHS[date.getMonth()] : date.toLocaleDateString(language === 'en' ? 'en-GB' : 'ru-RU', {day:'numeric', month:'long'});
    }
    function dayName(date) { return text().days[date.getDay()] + ', ' + dayMonth(date); }
    // A list drawn from the last visit that the site did not confirm says when it was read.
    function stale(body, at) {
        if (body.querySelector('.univer-app-overview-stale')) return;
        var date = new Date(at);
        body.appendChild(element('p', 'univer-app-overview-stale', text().stale.replace('{0}', dayMonth(date) + ', ' + clock(date.getHours() * 60 + date.getMinutes()))));
    }
    // A panel with one list: drawn at once from the last visit, then read from the site and redrawn only if it differs.
    function listed(box, name, url, valid, parse, render) {
        var epoch = generation, old = saved(name, valid), shown = '';
        if (old) {
            try { render(box.body, old.list); shown = JSON.stringify(old.list); }
            catch (_) { shown = ''; box.body.replaceChildren(pending()); }
        }
        once(name, url).then(function (doc) {
            var list = parse(doc);
            keep(name, {list:list}, valid);
            if (epoch === generation && JSON.stringify(list) !== shown) settled(box.body, function () { render(box.body, list); });
        }, function () { if (epoch !== generation) return; if (shown) stale(box.body, old.at); else fail(box.body); });
    }
    function renderToday(body, lessons, upcoming) {
        body.replaceChildren();
        var now = new Date(), at = now.getHours() * 60 + now.getMinutes(), day = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
        body.appendChild(element('p', 'univer-app-overview-caption', dayName(now)));
        if (!lessons || !lessons.length || lessons.every(function (l) { return l.end <= at; })) {
            var finished = lessons && lessons.length;
            body.appendChild(element('p', 'univer-app-overview-empty', finished ? (language === 'en' ? 'Today’s classes have finished' : language === 'kz' ? 'Бүгінгі сабақтар аяқталды' : 'Сегодняшние пары закончились') : text().noClasses));
            if (!upcoming) return;
            // A day off still answers "what is next": the first classes after it, with their day.
            body.appendChild(element('p', 'univer-app-overview-next-day', text().nextClasses + ' · ' + dayName(upcoming.date)));
            lessons = upcoming.lessons; at = -1; day = new Date(upcoming.date.getFullYear(), upcoming.date.getMonth(), upcoming.date.getDate()).getTime();
        }
        var next = lessons.find(function (l) { return l.start > at; }), live = lessons.find(function (l) { return l.start <= at && at < l.end; });
        var moment = function (minutes) { return day + minutes * 60000; }, t = text();
        // "What now" in one line before the list: the class under way and what follows it, or when the next one starts.
        var lead = live || next;
        if (lead) {
            var up = element('div', 'univer-app-overview-upnext'); up.dataset.state = live ? 'now' : 'next'; up.setAttribute('role', 'status');
            up.appendChild(live ? counter(element('b'), t.upNow + ' · ', 'left', moment(live.end)) : counter(element('b'), t.upNext + ' ', 'until', moment(next.start)));
            var then = live ? lessons.find(function (l) { return l.start >= live.end; }) : null;
            var detail = live ? (then ? t.then + ': ' + then.parts.name + ', ' + clock(then.start) : t.last)
                : [next.parts.name, clock(next.start) + '–' + clock(next.end), next.parts.room ? t.room + ' ' + next.parts.room : ''].filter(Boolean).join(' · ');
            up.appendChild(element('span', '', detail));
            body.appendChild(up);
        }
        var list = element('ol', 'univer-app-overview-lessons');
        lessons.forEach(function (lesson) {
            var state = lesson.start <= at && at < lesson.end ? 'now' : lesson === next ? 'next' : lesson.end <= at ? 'done' : '';
            var li = element('li', 'univer-app-overview-lesson' + (state ? ' univer-app-overview-' + state : ''));
            var time = element('time', ''), main = element('div', 'univer-app-overview-lesson-main'), parts = lesson.parts;
            time.append(element('b', '', clock(lesson.start)), element('span', '', clock(lesson.end)));
            time.setAttribute('aria-label', clock(lesson.start) + '–' + clock(lesson.end));
            // The time leads; then what the class is, where it is and who teaches it, each on its own line.
            var title = element('a', 'univer-app-lesson-name', parts.name); title.href = '/student/umkd/';
            common.umkdHref(parts.name).then(function (href) { title.href = href; });
            main.appendChild(title);
            var facts = element('span', 'univer-app-lesson-facts');
            if (parts.type) { var type = element('span', 'univer-app-lesson-type', parts.type); type.dataset.kind = parts.kind; facts.appendChild(type); }
            if (parts.room) facts.appendChild(element('span', 'univer-app-lesson-room', text().room + ' ' + parts.room));
            if (parts.faculty) facts.appendChild(element('span', '', parts.faculty));
            if (facts.childElementCount) main.appendChild(facts);
            var people = [parts.teacher].concat(parts.rest).filter(Boolean).join(' · ');
            if (people) main.appendChild(element('span', 'univer-app-lesson-teacher', people));
            if (parts.period) li.title = parts.period;
            li.append(time, main);
            // The line above the list already counts down to the class under way or the next one; its row is only
            // highlighted, so the time is never said twice. A finished class keeps its quiet "Прошла".
            if (state === 'done') li.appendChild(element('span', 'univer-app-overview-state', t.done));
            // The whole row leads to the course's materials.
            li.addEventListener('click', function (event) { if (!event.target.closest('a,button')) title.click(); });
            list.appendChild(li);
        });
        body.appendChild(list); var more = limit(list, 4); if (more) body.appendChild(more);
    }
    function renderGrades(body, courses) {
        body.replaceChildren();
        var graded = courses.filter(function (c) { return c.scores.length; });
        if (!graded.length) { body.appendChild(element('p', 'univer-app-overview-empty', text().noGrades)); return; }
        var list = element('ul', 'univer-app-overview-courses');
        graded.forEach(function (course) {
            var li = element('li', ''), name = element('a', 'univer-app-overview-course-link', course.name), chips = element('span', 'univer-app-overview-scores');
            name.href = '/student/attendance/full/';
            name.addEventListener('click', function () {
                try {
                    sessionStorage.setItem('univer.desktop.attendance-course.v1:/student/attendance/full/', course.token);
                    sessionStorage.setItem('univer.desktop.attendance-course.v1:/student/attendance/full', course.token);
                } catch (_) { }
            });
            course.scores.forEach(function (score) { chips.appendChild(common.scoreChip(score, language, true)); });
            li.append(name, chips); list.appendChild(li);
        });
        body.appendChild(list); var more = limit(list, 5); if (more) body.appendChild(more);
    }
    function renderRequests(body, items) {
        body.replaceChildren();
        if (!items.length) { body.appendChild(element('p', 'univer-app-overview-empty', text().noRequests)); return; }
        var list = element('ul', 'univer-app-overview-requests');
        items.forEach(function (item) {
            // Each request opens the site's own request page.
            var li = element('li', ''), link = element('a', 'univer-app-overview-request'), main = element('span', 'univer-app-overview-lesson-main'), status = element('span', 'univer-app-status', item.status);
            link.href = item.href; link.setAttribute('aria-label', text().open + ': ' + item.service + ', ' + item.date + ', ' + item.status);
            main.append(element('b', '', item.service), element('span', '', item.date));
            var t = tone(item.status); if (t) status.setAttribute('data-univer-tone', t);
            link.append(main, status); li.appendChild(link); list.appendChild(li);
        });
        body.appendChild(list); var more = limit(list, 4); if (more) body.appendChild(more);
    }
    // The week's independent work, drawn only from what the schedule page kept: the home page reads no syllabus itself.
    function renderWork(body, part) {
        body.replaceChildren();
        var t = text(), read = new Date(part.at), first = new Date(part.start), last = new Date(part.end - 86400000);
        // "16–22 ноября" within one month, "30 ноября – 6 декабря" across two.
        body.appendChild(element('p', 'univer-app-overview-caption', first.getMonth() === last.getMonth() ? first.getDate() + '–' + dayMonth(last) : dayMonth(first) + ' – ' + dayMonth(last)));
        var list = element('ul', 'univer-app-overview-work-list');
        part.list.forEach(function (item) {
            var li = element('li', ''), main = element('div', 'univer-app-overview-lesson-main'), course = element('a', 'univer-app-overview-course-link', item.course);
            course.href = item.href; main.appendChild(course);
            var score = item.score > 0 ? t.points + String(item.score).replace('.', language === 'en' ? '.' : ',') : '';
            var meta = [item.name, score].filter(Boolean).join(' · ');
            if (meta) main.appendChild(element('span', 'univer-app-overview-work-meta', meta));
            if (item.topic) { var topic = element('p', 'univer-app-overview-work-topic', item.topic); topic.title = item.topic; main.appendChild(topic); }
            li.appendChild(main); list.appendChild(li);
        });
        body.appendChild(list); var more = limit(list, 4); if (more) body.appendChild(more);
        body.appendChild(element('p', 'univer-app-overview-stale', t.workRead.replace('{0}', dayMonth(read) + ', ' + clock(read.getHours() * 60 + read.getMinutes()))));
    }
    // Shown only while the kept week lasts and when it has work in it; it goes when the week ends with the page open.
    function work(after) {
        var part = saved('work', isWork), now = Date.now();
        if (!part || now < part.start || now >= part.end || !part.list.length) return null;
        var box = panel('univer-app-overview-work', text().work, '/student/myschedule/', text().allSchedule); owned.push(box.box);
        try { renderWork(box.body, part); } catch (_) { box.box.remove(); return null; }
        after.after(box.box);
        return {box:box.box, end:part.end};
    }
    function bachelor() {
        if (document.querySelector('.univer-app-overview-grid')) return;
        // Every layout pass calls this; build only once the page offers a place for the summary.
        var anchor = document.querySelector('.univer-app-gpa') || document.querySelector('#l4 table.mt');
        if (!anchor) return;
        var grid = element('div', 'univer-app-overview-grid'); owned.push(grid);
        var today = panel('univer-app-overview-today', text().today, '/student/myschedule/', text().allSchedule);
        var grades = panel('univer-app-overview-grades', text().grades, '/student/attendance/full/', text().allGrades);
        grid.append(today.box, grades.box);
        if (anchor.classList.contains('univer-app-gpa')) anchor.after(grid); else anchor.before(grid);
        var weekWork = work(grid);
        var epoch = generation, signature = '', updating = false, loadedDay = new Date().toDateString(), scheduleDoc = null, keptDoc = null, keptMark = '', live = false;
        function show(lessons, upcoming, now, instant) {
            var at = now.getHours() * 60 + now.getMinutes();
            var key = now.toDateString() + JSON.stringify([lessons, upcoming, (lessons || []).map(function (l) { return at < l.start ? 'next' : at < l.end ? 'now' : 'done'; })]);
            if (key === signature) { tickCounts(today.body); return; }
            signature = key;
            var expanded = today.body.querySelector('button[aria-expanded=true]'), focused = today.body.contains(document.activeElement);
            if (instant) renderToday(today.body, lessons, upcoming); else settled(today.body, function () { renderToday(today.body, lessons, upcoming); });
            var more = today.body.querySelector('button');
            if (expanded && more) more.click();
            if (focused && more) more.focus();
        }
        var over = function (lessons, at) { return !lessons || !lessons.length || lessons.every(function (l) { return l.end <= at; }); };
        // The week the site showed, by column, with the first classes of the following week once they were read.
        function keepWeek(doc, now, upcoming) {
            var shown = weekOf(doc, now); if (!shown) return;
            var start = shown.start, end = shown.end;
            var following = upcoming && upcoming.date.getTime() >= end ? {date:upcoming.date.getTime(), lessons:upcoming.lessons} : null, mark = start + '|' + !!following;
            if (doc === keptDoc && mark === keptMark) return;
            if (!following) { var old = saved('schedule', isWeek); if (old && old.start === start) following = old.following || null; }
            keptDoc = doc; keptMark = mark; keep('schedule', {start:start, end:end, days:shown.days, following:following}, isWeek);
        }
        // This week's classes from the last visit, until the site's page arrives.
        var week = saved('schedule', isWeek), first = new Date();
        if (week && first.getTime() >= week.start && first.getTime() < week.end) {
            try {
                var column = first.getDay() === 0 ? 7 : first.getDay(), lessons = first.getDay() === 0 ? [] : week.days[column - 1] || [], upcoming = null;
                if (over(lessons, first.getHours() * 60 + first.getMinutes())) {
                    for (var c = column + 1; !upcoming && c <= week.days.length; c++)
                        if (week.days[c - 1].length) upcoming = {date:new Date(first.getFullYear(), first.getMonth(), first.getDate() + c - first.getDay()), lessons:week.days[c - 1]};
                    if (!upcoming && week.following && week.following.date > first.getTime()) upcoming = {date:new Date(week.following.date), lessons:week.following.lessons};
                }
                show(lessons, upcoming, first, true);
            } catch (_) { signature = ''; today.body.replaceChildren(pending()); }
        }
        once('schedule', '/student/myschedule/').then(async function (doc) {
            scheduleDoc = doc;
            async function update() {
                // A hidden page waits, except for the first reading after a panel drawn from the last visit.
                if (updating || epoch !== generation || (live && signature && document.hidden)) return;
                updating = true;
                try {
                    var now = new Date();
                    if (weekWork && now.getTime() >= weekWork.end) { weekWork.box.remove(); weekWork = null; }
                    if (loadedDay !== now.toDateString()) {
                        try { scheduleDoc = await once('schedule-day:' + now.toDateString(), '/student/myschedule/'); loadedDay = now.toDateString(); }
                        catch (_) { if (epoch === generation && signature !== 'unavailable') { signature = 'unavailable'; fail(today.body); } return; }
                    }
                    var lessons = lessonsToday(scheduleDoc, now), upcoming = null;
                    if (over(lessons, now.getHours() * 60 + now.getMinutes())) { try { upcoming = await nextClasses(scheduleDoc, now); } catch (_) { upcoming = null; } }
                    keepWeek(scheduleDoc, now, upcoming);
                    if (epoch === generation) { show(lessons, upcoming, now, false); live = true; }
                } finally { updating = false; }
            }
            await update();
            // A summary rebuilt after the site replaced the page content takes over the single refresh timer.
            if (epoch === generation) { clearInterval(scheduleTimer); scheduleTimer = setInterval(update, 30000); }
        }, function () { if (epoch === generation) fail(today.body); });
        listed(grades, 'grades', '/student/attendance/full/', isCourses, courseScores, renderGrades);
    }
    function desk() {
        if (document.querySelector('.univer-app-overview-requests-panel')) return;
        var anchor = document.querySelector('.univer-app-desk-cards') || document.querySelector('#l4 .univer-app-service-card')?.closest('.ts') || document.querySelector('.univer-app-credentials') || document.querySelector('.univer-app-ssc-nav');
        if (!anchor) return;
        var box = panel('univer-app-overview-requests-panel', text().requests, '/ssc/StatementList', text().allRequests); owned.push(box.box);
        anchor.after(box.box);
        listed(box, 'requests', '/ssc/StatementList', isRequests, requests, renderRequests);
    }
    window.__univerOverviewV1 = {
        apply: function (lang) {
            if (lang !== language) { language = lang || 'ru'; if (active) this.release(); }
            active = true;
            if (/^\/student\/bachelor\/?$/i.test(location.pathname)) bachelor();
            else if (/^\/ssc\/?$/i.test(location.pathname)) desk();
        },
        release: function () { active = false; generation++; clearInterval(scheduleTimer); owned.forEach(function (node) { window.__univerMotionUI?.stop(node); node.remove(); }); owned = []; },
        // The schedule page's list of this week's independent work, kept for the home page (syllabus-tasks.js).
        keepWork: function (lang, part) {
            if (['ru', 'kz', 'en'].indexOf(lang) < 0 || !part || !Array.isArray(part.list)) return;
            var list = part.list.map(function (w) { return w && {course:w.course, href:w.href, name:w.name, topic:w.topic, score:w.score}; });
            keep('work', {start:part.start, end:part.end, list:list}, isWork, lang);
        },
        // The current week for the Windows app's own copy (MainForm.Schedule.cs), which checks every field again: the schedule
        // page's table while it shows this week, elsewhere the home page's last reading of it (this account's, any language).
        // A JSON string, or '' when there is no current week to give.
        week: function () {
            try {
                var now = new Date(), shown = /^\/student\/myschedule\/?$/i.test(location.pathname) ? weekOf(document, now) : null;
                if (!shown) {
                    var all = kept(), part = all && all.schedule;
                    if (part && Number.isFinite(part.at) && isWeek(part) && now.getTime() >= part.start && now.getTime() < part.end) shown = part;
                }
                if (!shown) return '';
                return JSON.stringify({start:shown.start, end:shown.end, days:shown.days.map(function (day) {
                    return day.map(function (l) { return {start:l.start, end:l.end, name:l.parts.name || l.subject, type:l.parts.type, room:l.parts.room, teacher:l.parts.teacher}; });
                })});
            } catch (_) { return ''; }
        },
        // Test hooks: the same readers on a supplied document.
        // A site page read once per visit (GET, same origin), shared with the tab-page helpers.
        page: function (url) { return once(url, url); },
        lessonsToday: lessonsToday, courseScores: courseScores, requests: requests, nextClasses: nextClasses, renderToday: renderToday
    };
})();
