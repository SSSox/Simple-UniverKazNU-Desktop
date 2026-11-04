/* Distance courses: reads the pages behind the site's double-click routes with plain GET (never forms or the task
   page) to list current assignments, skip one-row curricula and show a task as a card; release() undoes it all. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerTasksV1) return;
    var common = window.__univerCommonV1, clean = common.clean;
    var routes = {
        index: /^\/student\/discussion\/index(?:\/\d+\/\d+)?\/?$/i,
        plans: /^\/student\/discussion\/acpos\/list\/\d+\/\d+\/\d+\/?$/i,
        weeks: /^\/student\/discussion\/acpos\/week\/list\/\d+\/\d+\/\d+\/\d+\/?$/i,
        tasks: /^\/student\/discussion\/task\/list\/\d+\/\d+\/\d+\/\d+\/(\d+)\/?$/i,
        task: /^\/student\/discussion\/task\/preview\/\d+\/\d+\/\d+\/\d+\/\d+\/(\d+)(?:\/\d+){0,4}\/?$/i,
        resource: /^\/student\/discussion\/resource\/\d+\/\d+\/\d+\/\d+\/\d+\/?$/i
    };
    var enabled = false, language = 'ru', style = null, generation = 0, forwarded = false;
    var added = [], marked = new Set(), wrapped = [], courses = null, course = null, scriptText = null;
    var CACHE_MS = 10 * 60 * 1000, PARALLEL = 3;

    function own(node) { added.push(node); return node; }
    function element(tag, className, content) {
        var node = document.createElement(tag); if (className) node.className = className;
        if (content !== undefined && content !== null) node.textContent = content;
        return node;
    }
    function mark(node, name) { if (!node.classList.contains(name)) node.classList.add(name); marked.add(node); }
    function plural(n, one, few, many) { var a = n % 10, b = n % 100; return a === 1 && b !== 11 ? one : a >= 2 && a <= 4 && (b < 12 || b > 14) ? few : many; }
    function text() {
        if (language === 'en') return {heading:'Assignments', checking:function (d, t) { return 'Checking courses: ' + d + ' of ' + t; },
            scope:"From each course's previous, current and next week", none:'No current assignments', noneCourse:'This course has no assignments yet',
            failed:'Could not check: ', loading:'Loading assignments…', refresh:'Refresh', open:'Open', openPlan:'Open weeks: ', openWeek:'Open week ', openTask:'Open assignment: ', week:function (n) { return 'Week ' + n; },
            current:'This week', due:function (d) { return 'due ' + d; }, opens:function (d) { return 'opens ' + d; }, today:'last day today',
            left:function (n) { return n === 1 ? '1 day left' : n + ' days left'; }, closed:'closed', count:function (n) { return n === 1 ? '1 assignment' : n + ' assignments'; },
            materials:function (n) { return 'Materials: ' + n; }, reply:'Write a reply', description:'Task'};
        if (language === 'kz') return {heading:'Тапсырмалар', checking:function (d, t) { return 'Пәндер тексерілуде: ' + d + ' / ' + t; },
            scope:'Әр пәннің өткен, ағымдағы және келесі аптасы бойынша', none:'Ағымдағы тапсырмалар жоқ', noneCourse:'Бұл пәнде әзірге тапсырма жоқ',
            failed:'Тексеру мүмкін болмады: ', loading:'Тапсырмалар жүктелуде…', refresh:'Жаңарту', open:'Ашу', openPlan:'Апталарды ашу: ', openWeek:'Аптаны ашу: ', openTask:'Тапсырманы ашу: ', week:function (n) { return n + '-апта'; },
            current:'Ағымдағы апта', due:function (d) { return d + ' дейін'; }, opens:function (d) { return d + ' ашылады'; }, today:'бүгін соңғы күн',
            left:function (n) { return n + ' күн қалды'; }, closed:'мерзімі өтті', count:function (n) { return n + ' тапсырма'; },
            materials:function (n) { return 'Материалдар: ' + n; }, reply:'Жауап жазу', description:'Тапсырма'};
        return {heading:'Задания', checking:function (d, t) { return 'Проверяю дисциплины: ' + d + ' из ' + t; },
            scope:'По прошлой, текущей и следующей неделе каждой дисциплины', none:'Текущих заданий нет', noneCourse:'В этой дисциплине пока нет заданий',
            failed:'Не удалось проверить: ', loading:'Загружаю задания…', refresh:'Обновить', open:'Открыть', openPlan:'Открыть недели: ', openWeek:'Открыть неделю ', openTask:'Открыть задание: ', week:function (n) { return 'Неделя ' + n; },
            current:'Текущая неделя', due:function (d) { return 'до ' + d; }, opens:function (d) { return 'откроется ' + d; }, today:'сегодня последний день',
            left:function (n) { return plural(n, 'остался ', 'осталось ', 'осталось ') + n + ' ' + plural(n, 'день', 'дня', 'дней'); }, closed:'срок прошёл',
            count:function (n) { return n + ' ' + plural(n, 'задание', 'задания', 'заданий'); },
            materials:function (n) { return 'Материалы: ' + n; }, reply:'Написать ответ', description:'Задание'};
    }

    // Routes. The site's handlers build each next page from a row id ("…/acpos/list/" + this.id + "/2026/1/"); the same
    // template is read from the page's script text, and the result must still match the expected route exactly.
    function template(source, path) {
        var match = new RegExp('["\'](\\/student\\/discussion\\/' + path + '\\/[\\d\\/]*)["\']\\s*\\+\\s*(?:this\\.id|checked\\.attr\\(["\']id["\']\\)|\\$\\(checked\\)\\.attr\\(["\']id["\']\\))\\s*(?:\\+\\s*["\']([\\d\\/]*)["\'])?').exec(source || '');
        return match ? {prefix:match[1], suffix:match[2] || ''} : null;
    }
    function build(pattern, id, route) {
        if (!pattern || !/^\d+$/.test(id || '')) return null;
        var url = pattern.prefix + id + pattern.suffix;
        return route.test(url) ? url : null;
    }
    function scripts() { if (scriptText === null) scriptText = Array.from(document.scripts).filter(function (s) { return !s.src; }).map(function (s) { return s.text; }).join('\n'); return scriptText; }
    // Row text without the controls this module adds to it.
    function plain(cell) { var copy = cell.cloneNode(true); copy.querySelectorAll('.univer-app-row-open,.univer-app-task-status,.univer-app-week-current,.univer-app-week-materials,.univer-app-course-tasks,.univer-app-course-open').forEach(function (n) { n.remove(); }); return clean(copy); }
    function links(scope) { return Array.from((scope || document).querySelectorAll('table.inner tr.link[id]')); }
    function number(cell) { var value = parseInt(clean(cell), 10); return isNaN(value) ? 0 : value; }

    // Background reads: same origin, GET only, no redirects (an expired session redirects to the login page).
    function load(url, run) {
        if (!url) return Promise.reject(new Error('route'));
        return fetch(url, {credentials:'same-origin', redirect:'error'}).then(function (response) {
            if (run !== generation) throw new Error('stale');
            var path = new URL(response.url || url, location.href);
            if (!response.ok || path.origin !== location.origin || path.pathname.replace(/\/$/, '') !== url.replace(/\/$/, '')) throw new Error('response');
            return response.text();
        }).then(function (html) {
            if (run !== generation) throw new Error('stale');
            var doc = new DOMParser().parseFromString(html, 'text/html');
            if (doc.querySelector('#login_frm') || !doc.querySelector('table.mt')) throw new Error('page');
            return {html:html, doc:doc};
        });
    }
    function queue(jobs, limit) {
        var next = 0;
        function worker() { if (next >= jobs.length) return Promise.resolve(); var job = jobs[next++]; return job().catch(function () {}).then(worker); }
        var workers = []; for (var i = 0; i < Math.min(limit, jobs.length); i++) workers.push(worker());
        return Promise.all(workers);
    }

    // Dates: "30.10.2026" (task lists) or "26.10.2026 00:00-30.10.2026 23:00" (the task itself).
    function date(value, endOfDay) {
        var m = /(\d{1,2})\.(\d{1,2})\.(\d{4})(?:\s+(\d{1,2}):(\d{2}))?/.exec(value || '');
        if (!m) return null;
        return m[4] ? new Date(+m[3], m[2] - 1, +m[1], +m[4], +m[5]) : endOfDay ? new Date(+m[3], m[2] - 1, +m[1], 23, 59, 59) : new Date(+m[3], m[2] - 1, +m[1]);
    }
    function short(when, withYear) {
        var pad = function (n) { return (n < 10 ? '0' : '') + n; };
        var day = pad(when.getDate()) + '.' + pad(when.getMonth() + 1) + (withYear ? '.' + when.getFullYear() : '');
        var whole = (!when.getHours() && !when.getMinutes()) || (when.getHours() === 23 && when.getMinutes() === 59);
        return !whole ? day + (withYear ? ', ' : ' ') + pad(when.getHours()) + ':' + pad(when.getMinutes()) : day;
    }
    function status(start, end, withYear) {
        var now = new Date(), t = text();
        if (start && now < start) return {state:'soon', label:t.opens(short(start, withYear)), order:2};
        if (!end) return {state:'open', label:'', order:1};
        if (now > end) return {state:'past', label:t.due(short(end, withYear)) + ' · ' + t.closed, order:3};
        var days = Math.round((new Date(end.getFullYear(), end.getMonth(), end.getDate()) - new Date(now.getFullYear(), now.getMonth(), now.getDate())) / 864e5);
        return {state:days <= 1 ? 'urgent' : 'open', label:t.due(short(end, withYear)) + ' · ' + (days === 0 ? t.today : t.left(days)), order:days <= 1 ? 0 : 1};
    }
    // A task list row: "Title (0). Автор: Name" in the name cell, then start, end, created and participants.
    function taskRow(row, week, preview) {
        var name = clean(row.cells[1]), m = /^([\s\S]*?)\s*\(\d+\)\.?\s*(?:[^:]{2,24}:\s*([\s\S]*))?$/.exec(name);
        var start = date(clean(row.cells[2])), end = date(clean(row.cells[3]), true);
        return {id:row.id, title:m ? m[1] : name, author:m && m[2] ? m[2] : '', week:week, start:start ? +start : 0, end:end ? +end : 0, url:build(preview, row.id, routes.task)};
    }
    function at(value) { return value ? new Date(value) : null; }
    // The site marks the current week with .normal; a week whose period contains today counts too.
    function current(row) {
        if (row.classList.contains('normal')) return true;
        var m = /(\d{1,2}\.\d{1,2}\.\d{4}(?:\s+\d{1,2}:\d{2})?)\s*-\s*(\d{1,2}\.\d{1,2}\.\d{4}(?:\s+\d{1,2}:\d{2})?)/.exec(clean(row.cells[2])), now = new Date();
        return !!m && date(m[1]) <= now && now < date(m[2]);
    }
    function weekRows(doc) {
        return links(doc).filter(function (row) { return row.cells.length >= 5 && /^\d+$/.test(row.id); }).map(function (row) {
            return {id:row.id, number:number(row.cells[1]) || +row.id, resources:number(row.cells[3]), tasks:number(row.cells[4]), current:current(row)};
        });
    }
    function tasksOf(weekUrl, weeks, choose, run) {
        // Task list routes come from the weeks page; the task route comes from each task list page.
        return load(weekUrl, run).then(function (page) {
            var list = template(page.html, 'task\\/list'), rows = weekRows(page.doc), picked = choose(rows);
            weeks.push.apply(weeks, rows);
            return Promise.all(picked.map(function (week) {
                var url = build(list, week.id, routes.tasks);
                if (!url) return [];
                return load(url, run).then(function (tasks) {
                    var preview = template(tasks.html, 'task\\/preview');
                    return links(tasks.doc).filter(function (row) { return row.cells.length >= 4; }).map(function (row) { return taskRow(row, week.number, preview); });
                });
            }));
        }).then(function (groups) { return [].concat.apply([], groups); });
    }
    function near(rows) {
        var current = rows.find(function (w) { return w.current; });
        if (!current) return [];
        return rows.filter(function (w) { return w.tasks > 0 && Math.abs(w.number - current.number) <= 1; });
    }

    // One list of assignments, nearest deadline first, closed ones last.
    function list(items, withCourse) {
        var box = element('ol', 'univer-app-task-list'), t = text();
        items.map(function (item) { return {item:item, status:status(at(item.start), at(item.end))}; })
            .sort(function (a, b) { return a.status.order - b.status.order || (a.status.order === 3 ? b.item.end - a.item.end : a.item.end - b.item.end); })
            .forEach(function (entry) {
                var item = entry.item, li = element('li', 'univer-app-task-item'); li.dataset.state = entry.status.state;
                var link = element(item.url ? 'a' : 'span', 'univer-app-task-link');
                if (item.url) { link.href = item.url; link.setAttribute('aria-label', t.openTask + item.title + (entry.status.label ? ', ' + entry.status.label : '')); }
                var meta = [withCourse ? item.course : '', t.week(item.week), item.author].filter(Boolean).join(' · ');
                link.append(element('span', 'univer-app-task-title', item.title), element('span', 'univer-app-task-meta', meta));
                if (entry.status.label) { var chip = element('span', 'univer-app-task-status', entry.status.label); chip.dataset.state = entry.status.state; link.appendChild(chip); }
                li.appendChild(link); box.appendChild(li);
            });
        return box;
    }
    function panel(anchor, record) {
        var t = text(), signature = JSON.stringify([language, record.done, record.total, record.items, record.failed, record.course]);
        var box = record.box && record.box.isConnected ? record.box : null;
        if (box && box.__univerSignature === signature) return;
        if (!box) {
            box = record.box = own(element('section', 'univer-app-tasks')); box.setAttribute('aria-live', 'polite');
            anchor.parentNode.insertBefore(box, anchor);
        }
        box.__univerSignature = signature; box.textContent = '';
        var head = element('div', 'univer-app-tasks-head'), title = element('h2', 'univer-app-tasks-title', t.heading);
        head.appendChild(title);
        var busy = record.done < record.total;
        var state = element('span', 'univer-app-tasks-state', busy ? (record.course ? t.loading : t.checking(record.done, record.total)) : record.course ? '' : t.scope);
        head.appendChild(state);
        if (!busy) {
            var refresh = element('button', 'univer-app-tasks-refresh', t.refresh); refresh.type = 'button';
            refresh.addEventListener('click', function () { record.refresh(); });
            head.appendChild(refresh);
        }
        box.dataset.busy = busy ? 'true' : 'false'; box.appendChild(head);
        if (record.items.length) box.appendChild(list(record.items, !record.course));
        else if (!busy) box.appendChild(element('p', 'univer-app-tasks-empty', record.course ? t.noneCourse : t.none));
        if (record.failed.length && !busy) box.appendChild(element('p', 'univer-app-tasks-note', t.failed + record.failed.join(', ')));
    }
    function stored(key) { try { var value = JSON.parse(sessionStorage.getItem(key) || 'null'); return value && Date.now() - value.at < CACHE_MS ? value : null; } catch (_) { return null; } }
    function store(key, value) { try { sessionStorage.setItem(key, JSON.stringify(value)); } catch (_) {} }

    // Course list: every course's curriculum, its weeks and the task lists of the weeks around the current one.
    function courseName(cell) {
        return Array.from(cell.childNodes).filter(function (n) { return n.nodeType === 3; }).map(function (n) { return n.data; }).join('').replace(/\s*\(\d+\)\s*$/, '').replace(/\s+/g, ' ').trim();
    }
    function indexPage() {
        var rows = links(document.getElementById('l4')).filter(function (row) { return /^\d+$/.test(row.id) && row.cells.length === 2; });
        var plans = template(scripts(), 'acpos\\/list');
        if (!rows.length || !plans) return;
        var anchor = rows[0].closest('table.mt');
        if (!anchor) return;
        if (!courses) {
            var key = 'univer-app-course-tasks:' + plans.prefix + plans.suffix;
            courses = {key:key, done:0, total:rows.length, items:[], failed:[], weeks:{}, open:{}, course:'', refresh:function () { try { sessionStorage.removeItem(key); } catch (_) {} courses = null; apply(language); }};
            var cached = stored(key);
            if (cached) { courses.done = courses.total = cached.total; courses.items = cached.items; courses.failed = cached.failed; courses.weeks = cached.weeks; courses.open = cached.open; }
            else collect(courses, rows, plans);
        }
        panel(anchor, courses);
        rows.forEach(function (row) { chip(row, courses); });
    }
    function collect(record, rows, plans) {
        var run = generation, order = rows.map(function (row) { return courseName(row.cells[1]); });
        var jobs = rows.map(function (row) {
            var name = courseName(row.cells[1]);
            return function () {
                return load(build(plans, row.id, routes.plans) || '', run).then(function (page) {
                    var weeksTemplate = template(page.html, 'acpos\\/week\\/list');
                    var plansRows = links(page.doc).filter(function (r) { return /^\d+$/.test(r.id); }).slice(0, 3);
                    return Promise.all(plansRows.map(function (plan) {
                        var url = build(weeksTemplate, plan.id, routes.weeks);
                        if (!url) return [];
                        if (plansRows.length === 1) record.weeks[row.id] = url;
                        var weeks = [];
                        return tasksOf(url, weeks, near, run).then(function (tasks) {
                            record.open[row.id] = tasks.filter(function (task) { return status(at(task.start), at(task.end)).order < 3; }).length;
                            return tasks;
                        });
                    }));
                }).then(function (groups) {
                    [].concat.apply([], groups).forEach(function (task) { task.course = name; record.items.push(task); });
                }, function (error) {
                    if (error && error.message === 'stale') throw error;
                    record.failed.push(name); record.failed.sort(function (a, b) { return order.indexOf(a) - order.indexOf(b); });
                }).then(function () {
                    if (run !== generation) return;
                    record.done++;
                    if (record.done === record.total) store(record.key, {at:Date.now(), total:record.total, items:record.items, failed:record.failed, weeks:record.weeks, open:record.open});
                    if (enabled) apply(language);
                });
            };
        });
        queue(jobs, PARALLEL);
    }
    // The course row gets "N assignments", a link straight to its weeks.
    function chip(row, record) {
        var cell = row.cells[1], count = record.open[row.id] || 0, url = record.weeks[row.id];
        var existing = cell.querySelector(':scope>.univer-app-course-tasks');
        if (!count) { if (existing) existing.remove(); return; }
        var label = text().count(count);
        if (existing && existing.textContent === label) return;
        if (existing) existing.remove();
        var link = own(element(url ? 'a' : 'span', 'univer-app-course-tasks', label));
        if (url) { link.href = url; link.addEventListener('click', function (event) { event.stopPropagation(); }); }
        var button = cell.querySelector(':scope>.univer-app-course-open');
        cell.insertBefore(link, button || null);
    }

    // Curriculum: with a single row, coming from the course list, go straight to its weeks (the entry is replaced, so
    // Back returns to the course list). The breadcrumb from the weeks page still shows this page.
    function plansPage() {
        var rows = links(document.getElementById('l4')).filter(function (row) { return /^\d+$/.test(row.id); });
        rows.forEach(function (row) { open(row, row.cells[1] || row.cells[0], text().openPlan + plain(row.cells[1] || row.cells[0])); });
        if (forwarded || rows.length !== 1) return;
        var from; try { from = new URL(document.referrer); } catch (_) { return; }
        if (from.origin !== location.origin || !routes.index.test(from.pathname)) return;
        var url = build(template(scripts(), 'acpos\\/week\\/list'), rows[0].id, routes.weeks);
        if (!url) return;
        forwarded = true; location.replace(url);
    }
    // An Open button for rows the site opens only by double-click.
    function open(row, cell, name) {
        if (!cell) return;
        var button = cell.querySelector(':scope>.univer-app-row-open');
        if (!button) {
            button = own(element('button', 'univer-app-course-open univer-app-row-open')); button.type = 'button';
            button.addEventListener('click', function (event) {
                event.preventDefault(); event.stopPropagation();
                row.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true, view:window}));
            });
            mark(cell, 'univer-app-open-cell'); cell.appendChild(button);
        }
        if (button.textContent !== text().open) button.textContent = text().open;
        if (name && button.getAttribute('aria-label') !== name) button.setAttribute('aria-label', name);
    }
    function wrap(cell, className, state) {
        var existing = cell.querySelector(':scope>.' + className);
        if (existing) { if (existing.dataset.state !== state) existing.dataset.state = state; return existing; }
        var node = Array.from(cell.childNodes).find(function (n) { return n.nodeType === 3 && n.data.trim(); });
        if (!node) return null;
        var span = element('span', className); span.dataset.state = state;
        node.parentNode.insertBefore(span, node); span.appendChild(node); wrapped.push(span);
        return span;
    }

    // Weeks: the whole list without the short scroll box, the current week and the weeks with assignments marked,
    // and this course's assignments above it.
    function weeksPage() {
        var l4 = document.getElementById('l4'), rows = links(l4).filter(function (row) { return row.cells.length >= 5 && /^\d+$/.test(row.id); });
        if (!rows.length) return;
        var t = text(), resource = template(scripts(), 'resource');
        l4.querySelectorAll('div.inner.scrool').forEach(function (box) { mark(box, 'univer-app-weeks-box'); });
        rows.forEach(function (row) {
            var tasks = number(row.cells[4]), resources = number(row.cells[3]);
            mark(row, 'univer-app-week');
            wrap(row.cells[4], 'univer-app-week-count', tasks ? 'has' : 'none');
            wrap(row.cells[3], 'univer-app-week-count', resources ? 'has' : 'none');
            var tag = row.cells[2].querySelector(':scope>.univer-app-week-current');
            if (row.classList.contains('normal') && !tag) { tag = own(element('span', 'univer-app-week-current')); row.cells[2].appendChild(tag); }
            if (tag && tag.textContent !== t.current) tag.textContent = t.current;
            var url = resources ? build(resource, row.id, routes.resource) : null, material = row.cells[2].querySelector(':scope>.univer-app-week-materials');
            if (url && !material) {
                material = own(element('a', 'univer-app-week-materials')); material.href = url;
                material.addEventListener('click', function (event) { event.stopPropagation(); });
                row.cells[2].appendChild(material);
            }
            if (material && material.textContent !== t.materials(resources)) material.textContent = t.materials(resources);
            // Weeks with nothing in them keep the site's double-click but get no button.
            if (tasks || resources || row.classList.contains('normal')) open(row, row.cells[2], t.openWeek + clean(row.cells[1]));
        });
        // The course's assignments go under its heading, above the weeks.
        var anchor = rows[0].closest('td.tt')?.firstElementChild, list = template(scripts(), 'task\\/list');
        if (!anchor || !list) return;
        if (!course) {
            var weeks = rows.map(function (row) { return {id:row.id, number:number(row.cells[1]) || +row.id, tasks:number(row.cells[4]), current:current(row)}; });
            var now = (weeks.find(function (w) { return w.current; }) || {number:0}).number;
            var picked = weeks.filter(function (w) { return w.tasks > 0; }).sort(function (a, b) { return Math.abs(a.number - now) - Math.abs(b.number - now); }).slice(0, 10);
            var record = course = {done:0, total:picked.length ? 1 : 0, items:[], failed:[], course:location.pathname, refresh:function () { course = null; apply(language); }};
            var run = generation;
            Promise.all(picked.map(function (week) {
                var url = build(list, week.id, routes.tasks);
                if (!url) return [];
                return load(url, run).then(function (page) {
                    var preview = template(page.html, 'task\\/preview');
                    return links(page.doc).filter(function (row) { return row.cells.length >= 4; }).map(function (row) { return taskRow(row, week.number, preview); });
                }, function (error) { if (error && error.message === 'stale') throw error; record.failed.push(t.week(week.number)); return []; });
            })).then(function (groups) {
                if (run !== generation || course !== record) return;
                record.items = [].concat.apply([], groups); record.done = record.total;
                if (enabled) apply(language);
            }, function () {});
        }
        panel(anchor, course);
    }

    // A week's task list: deadline status next to the end date and an Open button on each row.
    function tasksPage() {
        var t = text();
        links(document.getElementById('l4')).filter(function (row) { return row.cells.length >= 4 && /^\d+$/.test(row.id); }).forEach(function (row) {
            var start = date(clean(row.cells[2])), end = date(clean(row.cells[3]), true), info = status(start, end);
            var tag = row.cells[3].querySelector(':scope>.univer-app-task-status');
            var label = info.state === 'past' ? t.closed : info.state === 'soon' ? info.label : info.label.replace(/^.*? · /, '');
            if (label && !tag) { tag = own(element('span', 'univer-app-task-status')); row.cells[3].appendChild(tag); }
            if (tag && tag.dataset.state !== info.state) tag.dataset.state = info.state;
            if (tag && tag.textContent !== label) tag.textContent = label;
            open(row, row.cells[1], t.openTask + plain(row.cells[1]));
        });
    }

    // The task: its fields come from labelled spans ("Название задания:", "Автор:", "Формат ответа:", …) followed by
    // their values; the card reuses the site's own labels, so it reads in the site's language.
    function fields(cell) {
        var list = [], current = null;
        Array.from(cell.childNodes).forEach(function (node) {
            if (node.nodeType === 1 && node.matches('span.title,span.capt,span.duration,span.header')) {
                current = {label:clean(node).replace(/\s*:\s*$/, ''), kind:node.className, nodes:[]}; list.push(current);
            } else if (current && !(node.nodeType === 1 && node.matches('span.btn'))) current.nodes.push(node);
        });
        list.forEach(function (field) { field.value = field.nodes.map(function (n) { return n.nodeType === 1 && n.tagName === 'BR' ? '\n' : n.textContent; }).join('').replace(/[ \t]+/g, ' ').replace(/\s*\n\s*/g, '\n').trim(); });
        return list;
    }
    // "План: 1. … 2. … 3. …" written as one paragraph becomes a numbered list when the numbers run 1, 2, 3, … in order.
    function plan(value) {
        var marks = [], pattern = /(^|[\s:;.,)])(\d{1,2})\.(?=\s*[A-ZА-ЯЁӘҒҚҢӨҰҮҺІ«"(])/g, m;
        while ((m = pattern.exec(value))) marks.push({at:m.index + m[1].length, n:+m[2], body:m.index + m[0].length});
        var items = [], expected = 1;
        marks.forEach(function (mark) { if (mark.n === expected) { items.push(mark); expected++; } });
        if (items.length < 3) return null;
        var intro = value.slice(0, items[0].at).trim(), parts = items.map(function (item, i) { return value.slice(item.body, i + 1 < items.length ? items[i + 1].at : value.length).trim(); });
        // The last item often runs on into the closing paragraph: split it at its first sentence.
        var last = parts[parts.length - 1], end = /^([\s\S]{10,}?[.!?])\s+(?=[A-ZА-ЯЁӘҒҚҢӨҰҮҺІ])/.exec(last), outro = '';
        if (end && last.length - end[0].length >= 40) { outro = last.slice(end[0].length).trim(); parts[parts.length - 1] = end[1]; }
        return {intro:intro, items:parts, outro:outro};
    }
    function description(field) {
        var body = element('div', 'univer-app-task-text');
        var plain = field.nodes.every(function (n) { return n.nodeType === 3 || (n.nodeType === 1 && n.tagName === 'BR'); });
        var parsed = plain ? plan(field.value) : null;
        if (!parsed) {
            if (plain) field.value.split(/\n{1,}/).forEach(function (line) { if (line.trim()) body.appendChild(element('p', '', line.trim())); });
            else field.nodes.forEach(function (n) { body.appendChild(n.cloneNode(true)); });
            return body;
        }
        if (parsed.intro) body.appendChild(element('p', 'univer-app-task-intro', parsed.intro));
        var ol = element('ol'); parsed.items.forEach(function (item) { ol.appendChild(element('li', '', item)); }); body.appendChild(ol);
        if (parsed.outro) body.appendChild(element('p', '', parsed.outro));
        return body;
    }
    function taskPage() {
        var row = document.querySelector('#l4 tr.message'), info = row?.cells[0]?.querySelector(':scope>div.inf'), side = row?.cells[1];
        var box = row?.closest('div.inner'), host = box?.parentNode;
        if (!info || !side || !host) return;
        var t = text(), left = fields(info), right = fields(side);
        var name = left.find(function (f) { return /\btitle\b/.test(f.kind); }), author = left.find(function (f) { return /\bcapt\b/.test(f.kind); });
        var when = left.find(function (f) { return /\bduration\b/.test(f.kind); });
        if (!name || !name.value) return;
        var text2 = right.slice().sort(function (a, b) { return b.value.length - a.value.length; })[0];
        var signature = JSON.stringify([language, left.map(function (f) { return f.value; }), right.map(function (f) { return f.value; })]);
        var card = document.querySelector('#l4 .univer-app-task-card');
        if (card && card.__univerSignature === signature) return;
        if (card) card.remove();
        card = own(element('article', 'univer-app-task-card')); card.__univerSignature = signature;
        var week = routes.task.exec(location.pathname), subject = Array.from(document.querySelectorAll('#l4 table.xp td')).find(function (td) { return /предмет|пән|subject|discipline/i.test(clean(td)); });
        var kicker = [subject ? clean(subject.nextElementSibling) : '', week ? t.week(+week[1]) : ''].filter(Boolean).join(' · ');
        if (kicker) card.appendChild(element('p', 'univer-app-task-kicker', kicker));
        card.appendChild(element('h2', 'univer-app-task-name', name.value));
        var facts = element('div', 'univer-app-task-facts');
        if (when) {
            var range = /(\d{1,2}\.\d{1,2}\.\d{4}(?:\s+\d{1,2}:\d{2})?)\s*-\s*(\d{1,2}\.\d{1,2}\.\d{4}(?:\s+\d{1,2}:\d{2})?)/.exec(when.value);
            var info2 = range ? status(date(range[1]), date(range[2], true), true) : null;
            if (info2 && info2.label) { var due = element('span', 'univer-app-task-status', info2.label); due.dataset.state = info2.state; facts.appendChild(due); }
            facts.appendChild(fact(when.label, when.value.replace(/\s*-\s*/, ' – ')));
        }
        right.filter(function (f) { return f !== text2 && f.value; }).concat(left.filter(function (f) { return f !== name && f !== when && f.value; })).forEach(function (f) { facts.appendChild(fact(f.label, f.value)); });
        card.appendChild(facts);
        if (text2 && text2.value) {
            var section = element('section', 'univer-app-task-body');
            section.append(element('h3', 'univer-app-task-heading', text2.label || t.description), description(text2));
            card.appendChild(section);
        }
        // The site's reply link fills in the addressee; the card's button runs it and scrolls to the message form.
        var reply = info.querySelector('a.ans'), form = document.getElementById('send_message');
        if (reply && form) {
            var actions = element('div', 'univer-app-task-actions'), button = element('button', 'univer-app-task-reply', reply.title || t.reply); button.type = 'button';
            button.addEventListener('click', function () {
                var stop = function (event) { event.preventDefault(); };
                reply.addEventListener('click', stop); reply.click(); reply.removeEventListener('click', stop);
                form.scrollIntoView({block:'start', behavior:'smooth'});
                var first = form.querySelector('select,input:not([type=hidden]),a#fileS'); if (first) first.focus({preventScroll:true});
            });
            actions.appendChild(button); card.appendChild(actions);
        }
        // The task comes first; the course details table follows it.
        var details = document.querySelector('#l4 table.xp')?.closest('td');
        if (details) details.insertBefore(card, details.firstChild); else host.insertBefore(card, box);
        mark(box, 'univer-app-task-source');
        // An empty message history is only its header row.
        var history = document.querySelector('#l4 tr#group_list table.inner');
        if (history && !Array.from(history.rows).some(function (r) { return r.querySelector('td'); })) mark(history.closest('tr'), 'univer-app-task-source');
        function fact(label, value) { var node = element('span', 'univer-app-task-fact'); node.append(element('b', '', label), document.createTextNode(value)); return node; }
    }

    function css() {
        return `
/* Assignments: one calm panel, each assignment a row with its deadline on the right. */
:root.univer-mobile #l4 .univer-app-tasks { box-sizing:border-box; margin:0 0 18px; padding:16px 18px 18px; border:1px solid var(--u-line); border-radius:14px; background:#fff; color:var(--u-text); }
:root.univer-mobile #l4 .univer-app-tasks-head { display:flex; align-items:center; flex-wrap:wrap; gap:6px 14px; margin:0 0 12px; }
:root.univer-mobile #l4 .univer-app-tasks-title { margin:0!important; padding:0!important; color:var(--u-ink)!important; font-size:var(--u-fs-section)!important; font-weight:700!important; line-height:1.3!important; }
:root.univer-mobile #l4 .univer-app-tasks-state { color:var(--u-muted); font-size:var(--u-fs-meta); }
:root.univer-mobile #l4 .univer-app-tasks[data-busy=true] .univer-app-tasks-state::before { content:''; display:inline-block; width:10px; height:10px; margin-right:8px; border:2px solid var(--u-brand-line); border-top-color:var(--u-brand); border-radius:50%; vertical-align:-1px; animation:univer-app-spin .9s linear infinite; }
@keyframes univer-app-spin { to { transform:rotate(360deg); } }
@media (prefers-reduced-motion:reduce) { :root.univer-mobile #l4 .univer-app-tasks[data-busy=true] .univer-app-tasks-state::before { animation:none; } }
:root.univer-mobile #l4 button.univer-app-tasks-refresh { margin-left:auto!important; min-height:30px!important; padding:4px 12px!important; border:1px solid var(--u-line)!important; border-radius:8px!important; background:#fff!important; color:var(--u-muted)!important; font:600 13px var(--u-font)!important; cursor:pointer; }
:root.univer-mobile #l4 button.univer-app-tasks-refresh:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; color:var(--u-brand)!important; }
:root.univer-mobile #l4 .univer-app-task-list { display:grid; gap:8px; margin:0!important; padding:0!important; list-style:none; }
:root.univer-mobile #l4 .univer-app-task-item { margin:0!important; padding:0!important; }
:root.univer-mobile #l4 .univer-app-task-link { display:grid; grid-template-columns:minmax(0,1fr) auto; align-items:center; gap:3px 16px; box-sizing:border-box; padding:11px 14px!important; border:1px solid var(--u-line)!important; border-radius:12px!important; background:#fff!important; color:var(--u-text)!important; text-decoration:none!important; }
:root.univer-mobile #l4 a.univer-app-task-link:hover { border-color:var(--u-brand-line)!important; background:var(--u-brand-soft)!important; }
:root.univer-mobile #l4 a.univer-app-task-link:focus-visible { outline:2px solid var(--u-brand); outline-offset:2px; }
:root.univer-mobile #l4 .univer-app-task-title { grid-column:1; color:var(--u-ink); font-size:var(--u-fs-body); font-weight:600; line-height:1.4; overflow-wrap:anywhere; }
:root.univer-mobile #l4 .univer-app-task-meta { grid-column:1; color:var(--u-muted); font-size:var(--u-fs-meta); line-height:1.4; }
:root.univer-mobile #l4 .univer-app-task-link>.univer-app-task-status { grid-column:2; grid-row:1 / span 2; }
:root.univer-mobile #l4 .univer-app-task-item[data-state=past] .univer-app-task-title { color:var(--u-muted); font-weight:500; }
:root.univer-mobile #l4 .univer-app-task-status { display:inline-flex; align-items:center; box-sizing:border-box; min-height:24px; padding:2px 10px; border-radius:999px; background:var(--u-brand-soft); color:var(--u-brand); font-size:12.5px; font-weight:600; line-height:1.3; white-space:nowrap; }
:root.univer-mobile #l4 .univer-app-task-status[data-state=urgent] { background:#fdf3dd; color:#80540a; }
:root.univer-mobile #l4 .univer-app-task-status:is([data-state=past],[data-state=soon]) { background:var(--u-surface-2); color:var(--u-muted); }
:root.univer-mobile #l4 .univer-app-tasks-empty, :root.univer-mobile #l4 .univer-app-tasks-note { margin:0!important; color:var(--u-muted); font-size:var(--u-fs-meta); }
:root.univer-mobile #l4 .univer-app-tasks-note { margin-top:10px!important; }
@media (max-width:599px) {
 :root.univer-mobile #l4 .univer-app-task-link { grid-template-columns:minmax(0,1fr); }
 :root.univer-mobile #l4 .univer-app-task-link>.univer-app-task-status { grid-column:1; grid-row:auto; justify-self:start; margin-top:4px; }
}
/* Course list: "N assignments" next to the name opens the course's weeks. */
:root.univer-mobile #l4 .univer-app-course-tasks { display:inline-flex; align-items:center; min-height:22px; margin-left:10px; padding:1px 10px; border-radius:999px; background:#fdf3dd; color:#80540a!important; font-size:12px; font-weight:700; text-decoration:none!important; white-space:nowrap; vertical-align:1px; }
:root.univer-mobile #l4 a.univer-app-course-tasks:hover { background:#f8e6b8; }
/* Weeks: the whole list, the current week labelled, counts as quiet badges. */
:root.univer-mobile #l4 div.univer-app-weeks-box { height:auto!important; max-height:none!important; overflow:visible!important; }
:root.univer-mobile #l4 .univer-app-week>td { vertical-align:middle; }
:root.univer-mobile #l4 tr.univer-app-week.normal>td { background:#e8f4ef!important; }
:root.univer-mobile #l4 tr.univer-app-week:not(.normal) .univer-app-row-open { border:1px solid var(--u-brand-line)!important; background:#fff!important; color:var(--u-brand)!important; }
:root.univer-mobile #l4 tr.univer-app-week:not(.normal) .univer-app-row-open:hover { background:var(--u-brand-soft)!important; }
:root.univer-mobile #l4 .univer-app-open-cell { position:relative; padding-right:120px!important; }
:root.univer-mobile #l4 .univer-app-open-cell>.univer-app-row-open { position:absolute; right:12px; top:50%; transform:translateY(-50%); min-width:0!important; min-height:30px!important; padding:4px 14px!important; }
:root.univer-mobile #l4 .univer-app-week-count { display:inline-flex; align-items:center; justify-content:center; min-width:26px; height:22px; box-sizing:border-box; padding:0 8px; border-radius:999px; background:var(--u-surface-2); color:var(--u-muted); font-size:12px; font-weight:600; }
:root.univer-mobile #l4 .univer-app-week-count[data-state=has] { background:var(--u-brand-soft); color:var(--u-brand); font-weight:700; }
:root.univer-mobile #l4 .univer-app-week-current { display:inline-flex; align-items:center; min-height:22px; margin-left:10px; padding:0 10px; border-radius:999px; background:var(--u-teal-soft); color:var(--u-teal); font-size:12px; font-weight:700; white-space:nowrap; }
:root.univer-mobile #l4 a.univer-app-week-materials { margin-left:10px; color:var(--u-brand)!important; font-size:12.5px; font-weight:600; }
@media (max-width:899px) {
 :root.univer-mobile #l4 .univer-app-open-cell { padding-right:0!important; }
 :root.univer-mobile #l4 .univer-app-open-cell>.univer-app-row-open { position:static; transform:none; margin:8px 0 0 auto!important; }
}
/* The task: one card, the name as a heading, facts as chips, the description at a readable width. */
:root.univer-mobile #l4 .univer-app-task-source { display:none!important; }
:root.univer-mobile #l4 .univer-app-task-card { box-sizing:border-box; max-width:980px; margin:4px 0 20px; padding:22px 24px 24px; border:1px solid var(--u-line); border-radius:16px; background:#fff; color:var(--u-text); text-align:start; }
:root.univer-mobile #l4 .univer-app-task-kicker { margin:0 0 6px!important; color:var(--u-muted); font-size:var(--u-fs-meta); font-weight:600; }
:root.univer-mobile #l4 .univer-app-task-name { margin:0 0 14px!important; padding:0!important; color:var(--u-ink)!important; font-size:var(--u-fs-title)!important; font-weight:700!important; line-height:1.35!important; overflow-wrap:anywhere; }
:root.univer-mobile #l4 .univer-app-task-facts { display:flex; flex-wrap:wrap; align-items:center; gap:8px; margin:0 0 20px; }
:root.univer-mobile #l4 .univer-app-task-facts>.univer-app-task-status { min-height:30px; padding:4px 12px; font-size:13px; }
:root.univer-mobile #l4 .univer-app-task-fact { display:inline-flex; align-items:baseline; flex-wrap:wrap; gap:6px; box-sizing:border-box; min-height:30px; padding:5px 12px; border-radius:10px; background:var(--u-surface-2); color:var(--u-text); font-size:13px; line-height:1.45; }
:root.univer-mobile #l4 .univer-app-task-fact>b { color:var(--u-muted); font-weight:600; }
:root.univer-mobile #l4 .univer-app-task-body { padding-top:16px; border-top:1px solid var(--u-line); }
:root.univer-mobile #l4 .univer-app-task-heading { margin:0 0 8px!important; color:var(--u-muted)!important; font-size:var(--u-fs-meta)!important; font-weight:700!important; letter-spacing:.02em; }
:root.univer-mobile #l4 .univer-app-task-text { max-width:76ch; color:var(--u-text); font-size:var(--u-fs-strong); line-height:1.65; overflow-wrap:anywhere; }
:root.univer-mobile #l4 .univer-app-task-text p { margin:0 0 10px!important; }
:root.univer-mobile #l4 .univer-app-task-text ol { margin:4px 0 12px!important; padding-left:1.6em!important; }
:root.univer-mobile #l4 .univer-app-task-text li { display:list-item!important; height:auto!important; min-height:0!important; margin:0 0 6px!important; padding:0 0 0 4px!important; border:0!important; background:none!important; line-height:1.6!important; list-style:decimal!important; }
:root.univer-mobile #l4 .univer-app-task-intro { font-weight:600; color:var(--u-ink); }
:root.univer-mobile #l4 .univer-app-task-actions { margin-top:18px; }
:root.univer-mobile #l4 button.univer-app-task-reply { min-height:36px!important; padding:7px 16px!important; border:0!important; border-radius:8px!important; background:var(--u-brand)!important; color:#fff!important; font:600 14px var(--u-font)!important; cursor:pointer; }
:root.univer-mobile #l4 button.univer-app-task-reply:hover { background:var(--u-brand-strong)!important; }
@media (max-width:599px) { :root.univer-mobile #l4 .univer-app-task-card { padding:16px; border-radius:12px; } }
`;
    }

    function apply(lang) {
        if (lang && lang !== language) { language = lang; }
        if (document.readyState === 'loading' || !/^\/student\/discussion\//i.test(location.pathname) || !window.fetch || !window.DOMParser) return;
        enabled = true;
        if (!style || !style.isConnected) { style = document.createElement('style'); style.id = 'univer-tasks-style'; style.textContent = css(); (document.head || document.documentElement).appendChild(style); }
        var path = location.pathname;
        if (routes.index.test(path)) indexPage();
        else if (routes.plans.test(path)) plansPage();
        else if (routes.weeks.test(path)) weeksPage();
        else if (routes.tasks.test(path)) tasksPage();
        else if (routes.task.test(path)) taskPage();
    }
    function release() {
        if (!enabled && !style) return;
        enabled = false; generation++; courses = null; course = null;
        style?.remove(); style = null;
        added.forEach(function (node) { node.remove(); }); added = [];
        wrapped.forEach(function (span) { if (span.parentNode) { var parent = span.parentNode; span.replaceWith(...span.childNodes); parent.normalize(); } }); wrapped = [];
        var names = ['univer-app-weeks-box', 'univer-app-week', 'univer-app-open-cell', 'univer-app-task-source'];
        marked.forEach(function (node) { node.classList.remove(...names); if (!node.getAttribute('class')) node.removeAttribute('class'); }); marked.clear();
    }
    window.__univerTasksV1 = {apply:apply, release:release};
})();
