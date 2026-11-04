/* Read-only summaries on the tab pages: Бакалавр shows today's classes and the grade summary, the Е-услуги desk
   shows the latest requests. Each is read from the site's own GET page once per visit and never changes it. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerOverviewV1) return;
    var common = window.__univerCommonV1, clean = common.clean;
    var language = 'ru', active = false, owned = [], loads = new Map(), generation = 0, scheduleTimer;
    function text() {
        return language === 'en' ? {today:'Today', grades:'Grades', requests:'My requests', loading:'Loading…', failed:'Could not load. Open the page itself.', noClasses:'No classes today',
                allSchedule:'Full schedule', allGrades:'All grades', allRequests:'All requests', noRequests:'No requests yet', now:'Now', next:'Next', done:'Finished', noGrades:'No grades yet', showAll:'Show all', collapse:'Show less', nextClasses:'Next classes', open:'Open request',
                days:['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday']} :
            language === 'kz' ? {today:'Бүгін', grades:'Үлгерім', requests:'Менің өтінімдерім', loading:'Жүктелуде…', failed:'Жүктеу мүмкін болмады. Бетті ашыңыз.', noClasses:'Бүгін сабақ жоқ',
                allSchedule:'Толық кесте', allGrades:'Барлық бағалар', allRequests:'Барлық өтінімдер', noRequests:'Өтінімдер әлі жоқ', now:'Қазір', next:'Келесі', done:'Аяқталды', noGrades:'Бағалар әлі жоқ', showAll:'Барлығын көрсету', collapse:'Жасыру', nextClasses:'Келесі сабақтар', open:'Өтінімді ашу',
                days:['Жексенбі','Дүйсенбі','Сейсенбі','Сәрсенбі','Бейсенбі','Жұма','Сенбі']} :
            {today:'Сегодня', grades:'Успеваемость', requests:'Мои заявки', loading:'Загрузка…', failed:'Не удалось загрузить. Откройте страницу целиком.', noClasses:'Сегодня пар нет',
                allSchedule:'Всё расписание', allGrades:'Вся успеваемость', allRequests:'Все заявки', noRequests:'Заявок пока нет', now:'Идёт', next:'Следующая', done:'Прошла', noGrades:'Оценок пока нет', showAll:'Показать все', collapse:'Свернуть', nextClasses:'Ближайшие пары', open:'Открыть заявку',
                days:['Воскресенье','Понедельник','Вторник','Среда','Четверг','Пятница','Суббота']};
    }
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
                if (texts.length) lessons.push({start:minutes(from[0]), end:minutes(to[to.length - 1]), subject:texts[0], details:texts.slice(1).join(' · ')});
            });
        });
        return lessons.sort(function (a, b) { return a.start - b.start; });
    }
    function weekRange(value) {
        var dates = (value || '').match(/\d{1,2}\.\d{1,2}\.\d{4}/g) || [];
        return dates.length >= 2 ? dates.map(function (d) { var p = d.split('.'); return new Date(+p[2], +p[1] - 1, +p[0]); }) : null;
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
    function panel(cls, title, href, linkText) {
        var box = element('section', 'univer-app-overview ' + cls); box.setAttribute('aria-label', title);
        var head = element('div', 'univer-app-overview-head'), h = element('h2', '', title), link = element('a', 'univer-app-overview-link', linkText);
        link.href = href; head.append(h, link); box.appendChild(head);
        var body = element('div', 'univer-app-overview-body'); body.appendChild(element('p', 'univer-app-pending', text().loading)); box.appendChild(body);
        return {box:box, body:body};
    }
    function settled(body, render) {
        if (window.__univerMotionUI) window.__univerMotionUI.change(body, render, 'content'); else render();
    }
    function fail(body) { settled(body, function () { body.replaceChildren(element('p', 'univer-app-overview-empty', text().failed)); }); }
    function dayName(date) { return text().days[date.getDay()] + ', ' + date.toLocaleDateString(language === 'en' ? 'en-GB' : 'ru-RU', {day:'numeric', month:'long'}); }
    function renderToday(body, lessons, upcoming) {
        body.replaceChildren();
        var now = new Date(), at = now.getHours() * 60 + now.getMinutes();
        body.appendChild(element('p', 'univer-app-overview-caption', dayName(now)));
        if (!lessons || !lessons.length || lessons.every(function (l) { return l.end <= at; })) {
            var finished = lessons && lessons.length;
            body.appendChild(element('p', 'univer-app-overview-empty', finished ? (language === 'en' ? 'Today’s classes have finished' : language === 'kz' ? 'Бүгінгі сабақтар аяқталды' : 'Сегодняшние пары закончились') : text().noClasses));
            if (!upcoming) return;
            // A day off still answers "what is next": the first classes after it, with their day.
            body.appendChild(element('p', 'univer-app-overview-next-day', text().nextClasses + ' · ' + dayName(upcoming.date)));
            lessons = upcoming.lessons; at = -1;
        }
        var next = lessons.find(function (l) { return l.start > at; });
        var list = element('ol', 'univer-app-overview-lessons');
        lessons.forEach(function (lesson) {
            var state = lesson.start <= at && at < lesson.end ? 'now' : lesson === next ? 'next' : lesson.end <= at ? 'done' : '';
            var li = element('li', 'univer-app-overview-lesson' + (state ? ' univer-app-overview-' + state : ''));
            var time = element('time', '', clock(lesson.start) + '–' + clock(lesson.end)), main = element('div', 'univer-app-overview-lesson-main');
            main.appendChild(element('b', '', lesson.subject)); if (lesson.details) main.appendChild(element('span', '', lesson.details));
            li.append(time, main);
            if (state) li.appendChild(element('span', 'univer-app-overview-state', text()[state]));
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
            course.scores.forEach(function (score) {
                var chip = element('span', 'univer-app-overview-score'), ratio = score.max && score.value ? Number(score.value.replace(',', '.')) / Number(score.max.replace(',', '.')) : NaN;
                chip.setAttribute('aria-label', score.label + ': ' + (score.value || '–') + (score.max ? ' / ' + score.max : ''));
                chip.append(element('span', 'univer-app-overview-score-label', score.label), element('span', 'univer-app-overview-score-value', score.value || '–'));
                if (isFinite(ratio)) { var bar = element('span', 'univer-app-overview-score-bar'); bar.style.setProperty('--univer-score', Math.round(Math.max(0, Math.min(1, ratio)) * 100) + '%'); chip.appendChild(bar); }
                Array.from(chip.children).forEach(function (c) { c.setAttribute('aria-hidden', 'true'); });
                chips.appendChild(chip);
            });
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
        var epoch = generation;
        once('schedule', '/student/myschedule/').then(async function (doc) {
            var signature = '', updating = false, loadedDay = new Date().toDateString(), scheduleDoc = doc;
            async function update() {
                if (updating || epoch !== generation || (signature && document.hidden)) return;
                updating = true;
                try {
                    var now = new Date();
                    if (loadedDay !== now.toDateString()) {
                        try { scheduleDoc = await once('schedule-day:' + now.toDateString(), '/student/myschedule/'); loadedDay = now.toDateString(); }
                        catch (_) { if (epoch === generation && signature !== 'unavailable') { signature = 'unavailable'; fail(today.body); } return; }
                    }
                    var lessons = lessonsToday(scheduleDoc, now), upcoming = null, at = now.getHours() * 60 + now.getMinutes();
                    if (!lessons || !lessons.length || lessons.every(function (l) { return l.end <= at; })) { try { upcoming = await nextClasses(scheduleDoc, now); } catch (_) { upcoming = null; } }
                    var key = now.toDateString() + JSON.stringify([lessons, upcoming, (lessons || []).map(function (l) { return at < l.start ? 'next' : at < l.end ? 'now' : 'done'; })]);
                    if (epoch === generation && key !== signature) {
                        signature = key;
                        var expanded = today.body.querySelector('button[aria-expanded=true]'), focused = today.body.contains(document.activeElement);
                        settled(today.body, function () { renderToday(today.body, lessons, upcoming); });
                        var more = today.body.querySelector('button');
                        if (expanded && more) more.click();
                        if (focused && more) more.focus();
                    }
                } finally { updating = false; }
            }
            await update();
            // A summary rebuilt after the site replaced the page content takes over the single refresh timer.
            if (epoch === generation) { clearInterval(scheduleTimer); scheduleTimer = setInterval(update, 30000); }
        }, function () { if (epoch === generation) fail(today.body); });
        once('grades', '/student/attendance/full/').then(function (doc) { if (epoch === generation) settled(grades.body, function () { renderGrades(grades.body, courseScores(doc)); }); }, function () { if (epoch === generation) fail(grades.body); });
    }
    function desk() {
        if (document.querySelector('.univer-app-overview-requests-panel')) return;
        var anchor = document.querySelector('.univer-app-desk-cards') || document.querySelector('#l4 .univer-app-service-card')?.closest('.ts') || document.querySelector('.univer-app-credentials') || document.querySelector('.univer-app-ssc-nav');
        if (!anchor) return;
        var box = panel('univer-app-overview-requests-panel', text().requests, '/ssc/StatementList', text().allRequests); owned.push(box.box);
        anchor.after(box.box);
        var epoch = generation;
        once('requests', '/ssc/StatementList').then(function (doc) { if (epoch === generation) settled(box.body, function () { renderRequests(box.body, requests(doc)); }); }, function () { if (epoch === generation) fail(box.body); });
    }
    window.__univerOverviewV1 = {
        apply: function (lang) {
            if (lang !== language) { language = lang || 'ru'; if (active) this.release(); }
            active = true;
            if (/^\/student\/bachelor\/?$/i.test(location.pathname)) bachelor();
            else if (/^\/ssc\/?$/i.test(location.pathname)) desk();
        },
        release: function () { active = false; generation++; clearInterval(scheduleTimer); owned.forEach(function (node) { window.__univerMotionUI?.stop(node); node.remove(); }); owned = []; },
        // Test hooks: the same readers on a supplied document.
        // A site page read once per visit (GET, same origin), shared with the tab-page helpers.
        page: function (url) { return once(url, url); },
        lessonsToday: lessonsToday, courseScores: courseScores, requests: requests, nextClasses: nextClasses
    };
})();
