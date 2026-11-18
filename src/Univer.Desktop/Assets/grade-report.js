/* «Сводка успеваемости»: one sheet a parent can read at a glance, made from the grades journal's own numbers (attendance.js
   hands them over), shown over the page with Print, PDF, PNG and Copy picture; a long one is cut into pages, one picture
   each, copied one by one. The sheet is paper: light in both themes and
   of one width, so its picture has the same size at any zoom (a narrow window shows it scaled down). It lives in its own
   shadow root, out of the site's styles and the layout's observers. Its buttons only say which was pressed (univer-report:,
   from a real click); the app makes the files. While it is open, printing the page prints the sheet alone. Nothing is
   kept: the sheet is made again from the page every time it opens. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerReportV1 || window !== window.top) return;
    var common = window.__univerCommonV1;
    // A summary taller than PAGE CSS px becomes several pictures, cut between courses: one picture stays readable on a phone
    // and in a messenger, which shrink or crop a very tall one. The app takes at most MAX_PAGES (GradeReport.MaxPages).
    var ID = 'univer-app-report', MARK = 'data-univer-report', WIDTH = 760, PAGE = 1500, MAX_PAGES = 12;
    var WORDS = {
        ru: { dialog: 'Сводка успеваемости', title: 'Успеваемость', asOf: 'Данные на {0}', locale: 'ru-RU',
            how: 'РК — рубежный контроль, за каждый можно получить до 100 баллов. Цвет показывает уровень, полоса — сколько баллов набрано, отметки на ней — 50, 70 и 90.',
            print: 'Печать', pdf: 'Сохранить PDF', png: 'Сохранить PNG', copy: 'Копировать картинку', close: 'Закрыть', outOf: 'из', none: 'Оценки пока не выставлены',
            working: 'Готовим файл…', saved: 'Сохранено в «Загрузки»: {0}', copied: 'Картинка скопирована — вставьте её в сообщение (Ctrl+V)', failed: 'Не удалось сохранить. Попробуйте ещё раз.', copyFailed: 'Не удалось скопировать картинку. Попробуйте ещё раз.',
            reveal: 'Показать в папке', source: 'Источник: журнал посещений и успеваемости, univer.kaznu.kz',
            page: 'Страница {0} из {1}', copyPage: 'Копировать', copyPageLabel: 'Копировать страницу {0}', copiedPage: 'Страница {0} скопирована — вставьте её в сообщение (Ctrl+V)',
            pictures: function (n) { var t = n % 10, h = n % 100; return n + ' ' + (t === 1 && h !== 11 ? 'картинка' : t >= 2 && t <= 4 && (h < 12 || h > 14) ? 'картинки' : 'картинок'); } },
        kz: { dialog: 'Үлгерім қорытындысы', title: 'Үлгерім', asOf: '{0} күнгі деректер', locale: 'kk-KZ',
            how: 'РК — аралық бақылау, әрқайсысы үшін 100 балға дейін алуға болады. Түс деңгейді көрсетеді, жолақ — жиналған балл, ондағы белгілер — 50, 70 және 90.',
            print: 'Басып шығару', pdf: 'PDF сақтау', png: 'PNG сақтау', copy: 'Суретті көшіру', close: 'Жабу', outOf: '/', none: 'Бағалар әлі қойылмаған',
            working: 'Файл дайындалуда…', saved: '«Жүктеулер» бумасына сақталды: {0}', copied: 'Сурет көшірілді — оны хабарламаға қойыңыз (Ctrl+V)', failed: 'Сақтау мүмкін болмады. Қайталап көріңіз.', copyFailed: 'Суретті көшіру мүмкін болмады. Қайталап көріңіз.',
            reveal: 'Қалтада көрсету', source: 'Дереккөз: сабаққа қатысу және үлгерім журналы, univer.kaznu.kz',
            page: '{1} беттің {0}-беті', copyPage: 'Көшіру', copyPageLabel: '{0}-бетті көшіру', copiedPage: '{0}-бет көшірілді — оны хабарламаға қойыңыз (Ctrl+V)',
            pictures: function (n) { return n + ' сурет'; } },
        en: { dialog: 'Grade summary', title: 'Grades', asOf: 'As of {0}', locale: 'en-GB',
            how: 'RK is a midterm checkpoint, each worth up to 100 points. The colour shows the level, the bar how many points are earned, and its marks are 50, 70 and 90.',
            print: 'Print', pdf: 'Save PDF', png: 'Save PNG', copy: 'Copy picture', close: 'Close', outOf: 'of', none: 'No grades yet',
            working: 'Preparing the file…', saved: 'Saved to Downloads: {0}', copied: 'Picture copied — paste it into a message (Ctrl+V)', failed: 'Could not save. Try again.', copyFailed: 'Could not copy the picture. Try again.',
            reveal: 'Show in folder', source: 'Source: attendance and grades journal, univer.kaznu.kz',
            page: 'Page {0} of {1}', copyPage: 'Copy', copyPageLabel: 'Copy page {0}', copiedPage: 'Page {0} copied — paste it into a message (Ctrl+V)',
            pictures: function (n) { return n + (n === 1 ? ' picture' : ' pictures'); } }
    };
    // Shapes of each icon, built node by node in icon(): the stores' checks refuse HTML text written into the page.
    var ICONS = {
        print: [['path', { d: 'M6 9V3h12v6' }], ['rect', { x: '3', y: '9', width: '18', height: '8', rx: '2' }], ['path', { d: 'M7 14h10v7H7z' }]],
        pdf: [['path', { d: 'M12 3v12' }], ['path', { d: 'm7 10 5 5 5-5' }], ['path', { d: 'M5 21h14' }]],
        png: [['rect', { x: '3', y: '4', width: '18', height: '16', rx: '2' }], ['circle', { cx: '9', cy: '10', r: '2' }], ['path', { d: 'm21 17-5-5-9 8' }]],
        copy: [['rect', { x: '8', y: '8', width: '13', height: '13', rx: '2' }], ['path', { d: 'M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3' }]],
        close: [['path', { d: 'M6 6l12 12M18 6 6 18' }]]
    };
    // The bands' fills and ink, the same as on the page (desktop.js); the legend's tints are the sheet's own. The sheet is
    // paper, so they never turn dark.
    var CSS = ':host{all:initial}' +
        '.scrim{position:fixed;inset:0;z-index:2147483000;display:flex;justify-content:center;align-items:flex-start;box-sizing:border-box;padding:24px 16px;' +
        'background:rgba(15,23,32,.55);font:400 14px/1.45 var(--u-font,"Segoe UI",Tahoma,sans-serif);color:#16283b;animation:in .16s ease}' +
        '@keyframes in{from{opacity:0}}@keyframes up{from{opacity:0;transform:translateY(10px)}}' +
        '.dialog{display:flex;flex-direction:column;width:min(860px,100%);max-height:100%;overflow:hidden;border-radius:14px;background:#e9edf1;box-shadow:0 18px 50px rgba(0,0,0,.35);animation:up .22s cubic-bezier(.22,1,.36,1)}' +
        // Close keeps the top right corner however the title and the actions wrap; a narrow window gets the actions two by two.
        '.bar,.status{flex:none}.bar{position:relative;display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px;min-height:36px;padding:12px 60px 12px 20px;background:#fff;border-bottom:1px solid #dfe5ec}' +
        '.bar h2{flex:1 1 160px;margin:0;font-size:16px;font-weight:600}' +
        '.actions{display:flex;flex-wrap:wrap;gap:8px}' +
        '@media (max-width:640px){.actions{display:grid;grid-template-columns:1fr 1fr;flex:1 1 100%}.actions button{justify-content:center}}' +
        'button{display:inline-flex;align-items:center;gap:8px;box-sizing:border-box;height:36px;margin:0;padding:0 14px;border:1px solid #cfd8e2;border-radius:10px;background:#fff;color:#16283b;font:500 13px/1 var(--u-font,"Segoe UI",Tahoma,sans-serif);white-space:nowrap;cursor:pointer;transition:background-color .14s ease,border-color .14s ease}' +
        'button[hidden]{display:none}button:hover{background:#f1f5f9}button:active{background:#e6ecf2}' +
        'button.primary{border-color:#1b5fa6;background:#1b5fa6;color:#fff}button.primary:hover{background:#154d88}' +
        'button:focus-visible{outline:2px solid #1b5fa6;outline-offset:2px}button:disabled{opacity:.55;cursor:default}' +
        'button svg{flex:none;width:18px;height:18px;fill:none;stroke:currentColor;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round}' +
        'button.close{position:absolute;top:12px;right:12px;width:36px;padding:0;justify-content:center;border-color:transparent}' +
        '.status{display:flex;flex-wrap:wrap;align-items:center;gap:6px 12px;margin:0;padding:8px 20px;background:#f4f8fc;border-bottom:1px solid #dfe5ec;font-size:13px}' +
        '.status[hidden]{display:none}.status.bad{background:#fdf0ef;color:#a32c22}.status button{height:28px;padding:0 10px}' +
        '.scroll{flex:1 1 auto;overflow:auto;overscroll-behavior:contain;padding:20px}' +
        // Pages of a long summary: each picture is its own sheet, with its number and «Копировать» above it, off the paper.
        '.page+.page{margin-top:24px}' +
        '.page-bar{display:flex;align-items:center;justify-content:space-between;gap:12px;max-width:' + WIDTH + 'px;margin:0 auto 8px;color:#3d4f61;font-size:13px;font-weight:500}' +
        '.page-bar button{height:32px;padding:0 12px}' +
        '.cont-title{margin:0;font-size:18px;font-weight:600;line-height:1.25}.cont-head+.course{padding-top:18px;border-top:0}' +
        '.sheet{box-sizing:border-box;width:' + WIDTH + 'px;margin:0 auto;padding:36px 40px 28px;background:#fff;color:#16283b;box-shadow:0 1px 3px rgba(16,40,70,.14);' +
        '-webkit-print-color-adjust:exact;print-color-adjust:exact}' +
        '.sheet *{-webkit-print-color-adjust:exact;print-color-adjust:exact}' +
        'h1{margin:0;font-size:26px;font-weight:600;line-height:1.2}.meta{margin:6px 0 0;color:#5b6b7c;font-size:13px}' +
        '.how{margin:20px 0 8px;padding:14px 16px 12px;border-radius:10px;background:#f4f7fa}.how p{margin:0 0 12px;font-size:13.5px}' +
        '.scale{display:flex;height:26px;overflow:hidden;border-radius:6px}.scale span{display:flex;align-items:center;justify-content:center;min-width:0;padding:0 4px;' +
        'font-size:12px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}' +
        '.scale span+span{border-left:2px solid #fff}' +
        '.ticks{position:relative;height:18px;margin-top:3px;color:#5b6b7c;font-size:12px;font-variant-numeric:tabular-nums}.ticks span{position:absolute;top:0;transform:translateX(-50%)}' +
        '.ticks span:first-child{transform:none}.ticks span:last-child{transform:translateX(-100%)}' +
        '[data-band=low]{--band:#e58a80;--ink:#a8473d;--soft:#fbe3e0}[data-band=fair]{--band:#e5b862;--ink:#7d5a17;--soft:#fbedcd}' +
        '[data-band=good]{--band:#6cb38c;--ink:#2f6b4b;--soft:#d9f0e3}[data-band=top]{--band:#7b9fdc;--ink:#365e9f;--soft:#dbe7fb}' +
        '.scale [data-band]{background:var(--soft);color:var(--ink)}' +
        '.course{padding:14px 0 12px;border-top:1px solid #e3e8ee;break-inside:avoid}.how+.course{padding-top:18px;border-top:0}' +
        'h3{margin:0 0 8px;font-size:16px;font-weight:600;line-height:1.3}' +
        '.row{display:grid;grid-template-columns:52px minmax(0,1fr) 88px 200px;align-items:center;gap:14px;min-height:40px}' +
        '.label{color:#5b6b7c;font-size:13px;font-weight:500}' +
        '.track{position:relative;height:10px;overflow:hidden;border-radius:5px;background:#e8ecf0}' +
        '.fill{position:absolute;left:0;top:0;bottom:0;border-radius:5px;background:var(--band,#9aa8b6)}' +
        // The marks are plain white strips: a gradient with clear stops printed to PDF as dark lines in MuPDF-based readers.
        '.track .mark{position:absolute;top:0;bottom:0;width:2px;margin-left:-1px;background:#fff}' +
        '.value{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.value b{font-size:18px;font-weight:600;color:var(--ink,#16283b)}.value small{margin-left:4px;color:#5b6b7c;font-size:12px}' +
        '.note{font-size:13px;line-height:1.35}.note b{display:block;color:var(--ink,#16283b);font-weight:600}.note span{color:#3d4f61}' +
        '.none{margin:0;color:#5b6b7c}' +
        '.source{margin:18px 0 0;padding-top:12px;border-top:1px solid #e3e8ee;color:#5b6b7c;font-size:12px}' +
        // The picture: the sheet alone, flat on the page, at its own width.
        ':host([data-mode=capture]) .scrim{position:absolute;left:0;top:0;right:auto;bottom:auto;display:block;padding:0;background:none;animation:none}' +
        ':host([data-mode=capture]) .dialog{display:block;width:auto;max-height:none;overflow:visible;border-radius:0;background:none;box-shadow:none;animation:none}' +
        ':host([data-mode=capture]) .bar,:host([data-mode=capture]) .status{display:none}' +
        ':host([data-mode=capture]) .scroll{overflow:visible;padding:0}:host([data-mode=capture]) .sheet{margin:0;box-shadow:none;zoom:1!important}' +
        ':host([data-mode=capture]) .page-bar{display:none}' +
        '@media (prefers-reduced-motion:reduce){.scrim,.dialog{animation:none}}' +
        ':host(.still) .scrim,:host(.still) .dialog{animation:none}' +
        '@media print{.scrim{position:static;display:block;padding:0;background:none;animation:none}' +
        '.dialog{display:block;width:auto;max-height:none;overflow:visible;border-radius:0;background:none;box-shadow:none;animation:none}' +
        '.bar,.status{display:none!important}.scroll{overflow:visible;padding:0}.sheet{width:auto;margin:0;padding:0;box-shadow:none;zoom:1!important}' +
        // On paper the pages flow as one sheet: Chromium breaks them between courses itself.
        '.page-bar,.cont-head,.pageno,.sheet:not(.last) .source{display:none!important}.page+.page{margin:0}' +
        '.cont-head+.course{padding-top:14px;border-top:1px solid #e3e8ee}}';
    // While the sheet is open the page prints as the sheet alone, on A4 with the margins the app's PDF uses. The paper is
    // white: under a transparent root Chromium would paint the browser's own background (the app's theme colour).
    var PAGE_CSS = '@page{size:A4;margin:12mm}' +
        '@media print{:root:root:root[' + MARK + ']>body>:not(#' + ID + '){display:none!important}' +
        ':root:root:root[' + MARK + '],:root:root:root[' + MARK + ']>body{height:auto!important;min-height:0!important;margin:0!important;padding:0!important;overflow:visible!important;background:transparent!important}' +
        ':root:root:root:root[' + MARK + ']{background:#fff!important;color-scheme:light!important}' +
        ':root:root:root[' + MARK + ']>body>#' + ID + '{display:block!important;position:static!important}}';
    var host = null, shadow = null, sheets = [], scroller = null, status = null, buttons = {}, courses = [], language = 'ru', opener = null, busy = false, timer = 0, kept = null, made = null, signature = '', pressed = '', preparing = false, opening = Math.floor(Math.random() * 1e9) + 1;
    function words() { return WORDS[language] || WORDS.ru; }
    function element(tag, cls, text) { var node = document.createElement(tag); if (cls) node.className = cls; if (text != null) node.textContent = text; return node; }
    function icon(name) {
        var ns = 'http://www.w3.org/2000/svg', svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('aria-hidden', 'true');
        ICONS[name].forEach(function (part) {
            var shape = document.createElementNS(ns, part[0]);
            Object.keys(part[1]).forEach(function (key) { shape.setAttribute(key, part[1][key]); });
            svg.appendChild(shape);
        });
        return svg;
    }
    function button(name, primary) {
        var node = element('button', primary ? 'primary' : name === 'close' ? 'close' : '');
        node.type = 'button'; node.appendChild(icon(name));
        if (name === 'close') node.setAttribute('aria-label', words().close); else node.appendChild(element('span', '', words()[name]));
        node.addEventListener('click', function (event) { if (name === 'close') close(); else press(name, event); });
        buttons[name] = node; return node;
    }
    // A request goes to the app only from the student's own click, one at a time. A document that cannot reach the app (it
    // missed the document-start script) says so at once instead of waiting.
    function press(name, event) {
        if (!event.isTrusted || busy || !host) return;
        if (typeof __univerNative !== 'function') { say(words().failed, true); return; }
        busy = true; pressed = name; Object.keys(buttons).forEach(function (key) { if (key !== 'close') buttons[key].disabled = true; });
        if (name !== 'print') say(words().working, false);
        // The app takes the pages one after another: each one more picture gets more time.
        clearTimeout(timer); timer = setTimeout(function () { if (status && preparing) say(''); idle(); }, 40000 + 15000 * (sheets.length - 1));
        __univerNative('univer-report:' + name);
    }
    // The pressed button was disabled, so Chromium took focus from it: it comes back, and Escape keeps working.
    function idle() {
        busy = false; clearTimeout(timer); Object.keys(buttons).forEach(function (key) { buttons[key].disabled = false; });
        if (host && !shadow.activeElement) (buttons[pressed] || buttons.print).focus({ preventScroll: true });
    }
    function say(text, bad, reveal) {
        // «Preparing» is known by the press, not by its words: the language may change while the file is made.
        preparing = !!text && text === words().working;
        status.replaceChildren(); status.hidden = !text; status.classList.toggle('bad', !!bad);
        if (!text) return;
        status.appendChild(element('span', '', text));
        if (reveal) { var show = element('button', '', words().reveal); show.type = 'button'; show.addEventListener('click', function (event) { if (event.isTrusted && typeof __univerNative === 'function') __univerNative('univer-report:reveal'); }); status.appendChild(show); }
    }
    function scale() {
        var box = element('div', 'scale'); box.setAttribute('aria-hidden', 'true');
        [['low', 50], ['fair', 20], ['good', 20], ['top', 10]].forEach(function (part) {
            var span = element('span', '', common.bandWords(language)[part[0]]); span.dataset.band = part[0]; span.style.flex = part[1] + ' 1 0'; box.appendChild(span);
        });
        var ticks = element('div', 'ticks'); ticks.setAttribute('aria-hidden', 'true');
        [0, 50, 70, 90, 100].forEach(function (mark) { var span = element('span', '', String(mark)); span.style.left = mark + '%'; ticks.appendChild(span); });
        // The same scale for a screen reader, in words.
        var read = element('p', ''); read.style.cssText = 'position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)';
        read.textContent = ['top', 'good', 'fair', 'low'].map(function (name) { return common.bandWords(language)[name] + ' ' + common.bandRange(name); }).join('; ');
        return [box, ticks, read];
    }
    function row(period) {
        var line = element('div', 'row'), found = common.band(period.value, period.max), names = common.bandWords(language);
        line.appendChild(element('span', 'label', period.label));
        var track = element('span', 'track'), fill = element('span', 'fill'); track.setAttribute('aria-hidden', 'true');
        // A score on another scale still shows its share, in the plain colour: no band is guessed for it.
        var share = Number(String(period.value).replace(',', '.')) / Number(String(period.max).replace(',', '.'));
        if (found) { fill.style.width = found.score + '%'; track.appendChild(fill); line.dataset.band = found.band; }
        else if (isFinite(share) && share >= 0) { fill.style.width = Math.min(1, share) * 100 + '%'; track.appendChild(fill); }
        [50, 70, 90].forEach(function (at) { var mark = element('i', 'mark'); mark.style.left = at + '%'; track.appendChild(mark); });
        line.appendChild(track);
        var value = element('span', 'value'); value.appendChild(element('b', '', period.value || '–'));
        if (period.max) value.appendChild(element('small', '', words().outOf + ' ' + period.max));
        line.appendChild(value);
        var note = element('span', 'note');
        if (found) { note.appendChild(element('b', '', names[found.band])); if (found.next) note.appendChild(element('span', '', common.bandNote(found, language))); }
        line.appendChild(note);
        return line;
    }
    // One page of the summary: the first has the title, the legend and the scale; the next ones a short heading of their own,
    // so each picture says what it is. Every page ends with the source.
    function page(first, w, when) {
        var node = element('article', 'sheet'), wrap = element('div', 'page');
        if (first) {
            node.appendChild(element('h1', '', w.title)); node.appendChild(element('p', 'meta', w.asOf.replace('{0}', when)));
            var how = element('section', 'how'); how.appendChild(element('p', '', w.how)); scale().forEach(function (part) { how.appendChild(part); }); node.appendChild(how);
        } else {
            var head = element('div', 'cont-head'); head.appendChild(element('p', 'cont-title', w.title)); head.appendChild(element('p', 'meta', w.asOf.replace('{0}', when))); node.appendChild(head);
        }
        node.appendChild(element('p', 'source', w.source));
        wrap.appendChild(node); scroller.appendChild(wrap); sheets.push(node);
        return node;
    }
    // Courses go on a page until it grows past PAGE; the course that crosses it starts the next page (a course is never cut,
    // and one taller than a page has a page of its own). The sheet has its own width and no zoom yet, so the cut is the same
    // in any window and at any zoom.
    function render() {
        var w = words(); scroller.replaceChildren(); sheets = [];
        Object.keys(buttons).forEach(function (key) { if (key.indexOf('copy:') === 0) delete buttons[key]; });
        var when = made.toLocaleString(w.locale, { day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' });
        var current = page(true, w, when);
        courses.forEach(function (course) {
            var block = element('section', 'course'); block.appendChild(element('h3', '', course.name));
            if (course.periods.length) course.periods.forEach(function (period) { block.appendChild(row(period)); });
            else block.appendChild(element('p', 'none', course.note || w.none));
            current.insertBefore(block, current.lastChild);
            if (current.offsetHeight > PAGE && current.querySelector('.course') !== block && sheets.length < MAX_PAGES) {
                current = page(false, w, when); current.insertBefore(block, current.lastChild);
            }
        });
        sheets[sheets.length - 1].classList.add('last');
        var count = sheets.length;
        // One page: the picture as before. Several: each says its number, and is copied from beside it.
        if (buttons.copy) buttons.copy.hidden = count > 1;
        if (count > 1) sheets.forEach(function (node, index) {
            var number = String(index + 1), label = w.page.replace('{0}', number).replace('{1}', String(count));
            var meta = node.querySelector('.meta'); meta.appendChild(element('span', 'pageno', ' · ' + label));
            var bar = element('div', 'page-bar'); bar.appendChild(element('span', '', label));
            if (buttons.copy) {
                var copy = element('button', '');
                copy.type = 'button'; copy.appendChild(icon('copy')); copy.appendChild(element('span', '', w.copyPage));
                copy.setAttribute('aria-label', w.copyPageLabel.replace('{0}', number)); copy.disabled = busy;
                copy.addEventListener('click', function (event) { press('copy:' + number, event); });
                buttons['copy:' + number] = copy; bar.appendChild(copy);
            }
            node.parentNode.insertBefore(bar, node);
        });
        fit();
    }
    // A window narrower than the sheet shows it scaled down; the picture and the printout keep its own size.
    function fit() {
        if (!sheets.length || host.getAttribute('data-mode') === 'capture') return;
        var room = scroller.clientWidth - 40, zoom = room > 0 && room < WIDTH ? String(Math.max(0.4, room / WIDTH)) : '';
        sheets.forEach(function (node) { node.style.zoom = zoom; });
    }
    function build() {
        host = element('div', ''); host.id = ID;
        shadow = host.attachShadow({ mode: 'open' });
        var style = element('style', ''); style.textContent = CSS;
        var scrim = element('div', 'scrim'), dialog = element('div', 'dialog'), bar = element('div', 'bar'), title = element('h2', '', words().dialog), actions = element('div', 'actions');
        title.id = 'title'; dialog.setAttribute('role', 'dialog'); dialog.setAttribute('aria-modal', 'true'); dialog.setAttribute('aria-labelledby', 'title');
        buttons = {};
        actions.append(button('print', true), button('pdf'));
        // The browser extension cannot take a picture of the tab (that needs every site's permission) and says so first.
        if (window.__univerReportPictures !== false) actions.append(button('png'), button('copy'));
        bar.append(title, actions, button('close'));
        status = element('p', 'status'); status.setAttribute('role', 'status'); status.hidden = true;
        scroller = element('div', 'scroll'); sheets = [];
        dialog.append(bar, status, scroller); scrim.appendChild(dialog); shadow.append(style, scrim);
        // A click on the dim backdrop closes, as in the app's other sheets, but not the second click of a double click on the
        // button that opened it.
        scrim.addEventListener('mousedown', function (event) { if (event.target === scrim && event.detail < 2) close(); });
        window.addEventListener('resize', fit);
    }
    // Escape closes and Tab stays inside, wherever the focus went (a click on the sheet's text moves it to the page).
    function key(event) {
        if (!host) return;
        if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(); return; }
        if (event.key !== 'Tab') return;
        var list = Array.prototype.filter.call(shadow.querySelectorAll('button'), function (node) { return !node.disabled && node.getClientRects().length; });
        if (!list.length) return;
        var first = list[0], last = list[list.length - 1], at = shadow.activeElement;
        if (event.shiftKey && (at === first || !at)) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && (at === last || !at)) { event.preventDefault(); first.focus(); }
    }
    function open(list, lang, from) {
        if (!Array.isArray(list)) return;
        language = WORDS[lang] ? lang : 'ru'; courses = list; signature = JSON.stringify(list) + language; made = new Date();
        opener = from || document.activeElement;
        if (host) close(true);
        opening += 1; build(); host.classList.toggle('still', window.__univerMotion === false);
        (document.body || document.documentElement).appendChild(host);
        var style = document.getElementById(ID + '-page');
        if (!style) { style = element('style', ''); style.id = ID + '-page'; style.textContent = PAGE_CSS; (document.head || document.documentElement).appendChild(style); }
        document.documentElement.setAttribute(MARK, '');
        document.addEventListener('keydown', key, true);
        render(); buttons.print.focus({ preventScroll: true });
    }
    function close(quiet) {
        if (!host) return;
        idle(); window.removeEventListener('resize', fit); document.removeEventListener('keydown', key, true);
        host.remove(); host = shadow = scroller = status = null; sheets = []; buttons = {}; kept = null;
        document.documentElement.removeAttribute(MARK);
        var style = document.getElementById(ID + '-page'); if (style) style.remove();
        if (!quiet && opener && opener.isConnected) opener.focus({ preventScroll: true });
        opener = null;
    }
    // Fixed in place: a page script cannot swap the sheet the app asks before it makes a file.
    Object.defineProperty(window, '__univerReportV1', { configurable: false, enumerable: false, writable: false, value: Object.freeze({
        open: open,
        close: function () { close(); },
        shown: function () { return !!(host && host.isConnected); },
        // Which opening of the sheet is on screen (0: none): the app's answer to an earlier opening is not shown on this one.
        opened: function () { return host && host.isConnected ? opening : 0; },
        // New numbers or another language while the sheet is open: it is made again (the time it shows, too).
        update: function (list, lang) {
            if (!host || !Array.isArray(list)) return;
            var next = JSON.stringify(list) + (WORDS[lang] ? lang : 'ru');
            if (next === signature) return;
            language = WORDS[lang] ? lang : 'ru'; courses = list; signature = next; made = new Date();
            shadow.getElementById('title').textContent = words().dialog;
            // The pages' own buttons are made again by render().
            Object.keys(buttons).forEach(function (key) { if (key.indexOf('copy:') === 0) return; var label = buttons[key].querySelector('span'); if (label) label.textContent = words()[key]; else buttons[key].setAttribute('aria-label', words().close); });
            render();
        },
        // The app's answer to a pressed button.
        done: function (answer) {
            if (!host || (answer && answer.opening != null && String(answer.opening) !== String(opening))) return;
            idle();
            // The app was still making an earlier file: the buttons are free again and «preparing» goes, nothing else to tell.
            if (answer && answer.busy) { if (preparing) say(''); return; }
            if (!answer || answer.action === 'print') { if (answer && !answer.ok) say(words().failed, true); return; }
            var w = words(), number = String(answer.page || ''), count = Number(answer.count) || 1;
            if (!answer.ok) say(answer.action === 'copy' ? w.copyFailed : w.failed, true);
            else if (answer.action === 'copy') say(answer.page ? w.copiedPage.replace('{0}', number) : w.copied, false);
            else say(w.saved.replace('{0}', answer.action === 'png' && count > 1 ? w.pictures(count) : String(answer.name || '')), false, true);
        },
        // For the pictures the pages lie flat at the top of the document at their own width; the app takes these boxes, one
        // picture each, in CSS pixels of the document.
        capture: function (on) {
            if (!host) return null;
            if (!on) {
                host.removeAttribute('data-mode');
                if (kept) { scroller.scrollTop = kept.top; window.scrollTo(kept.x, kept.y); kept = null; }
                fit(); return null;
            }
            kept = { x: window.scrollX, y: window.scrollY, top: scroller.scrollTop };
            // The entrance has played; coming back from the flat layout must not fade the sheet in again.
            host.classList.add('still');
            host.setAttribute('data-mode', 'capture'); sheets.forEach(function (node) { node.style.zoom = ''; });
            return { pages: sheets.map(function (node) {
                var box = node.getBoundingClientRect();
                return { x: box.left + window.scrollX, y: box.top + window.scrollY, width: box.width, height: box.height };
            }), ratio: window.devicePixelRatio || 1 };
        }
    }) });
})();
