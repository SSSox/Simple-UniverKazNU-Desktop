/* Reused Android DOM adapter; desktop CSS below changes presentation, not site handlers. */
(function initializeMobile() {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz') return;
    if (window.__univerMobileV1) return;
    var enabled = false, observer = null, queued = false, viewport = null, originalViewport = null;
    var madeViewport = false, menuHome = null, menuTitle = 'Разделы Univer';
    var infoTitles = {contacts:'Контакты и информация КазНУ', links:'Полезные ссылки'};
    var categories = [], searchLabel = 'Найти раздел…', emptyLabel = 'Разделы не найдены';
    var menuHidden = new WeakMap();
    var closeLabel = 'Закрыть', frameListeners = new WeakSet(), childAdapters = new Set();
    var selectedLanguage = 'ru';
    var settingsCorner = 'BOTTOM_RIGHT';
    var headerMoves = [], schedules = new Map(), weekViews = new Map(), upcomingTimer = 0, finishedWeek = null;
    var punctuation = new Map(), usabilityMoves = [], catalogHidden = new WeakMap(), pageMarkers = new Map();
    function labels() {
        return selectedLanguage === 'en' ? {account:'Account', university:'About KazNU', open:'Open', all:'All days', empty:'No classes', files:'Download', sections:'All sections', close:'Close', today:'Today', now:'In progress', next:'Next', done:'Done', days:['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday']} :
            selectedLanguage === 'kz' ? {account:'Аккаунт', university:'ҚазҰУ туралы', open:'Ашу', all:'Барлық күндер', empty:'Сабақ жоқ', files:'Жүктеу', sections:'Барлық бөлімдер', close:'Жабу', today:'Бүгін', now:'Жүріп жатыр', next:'Келесі', done:'Өтті', days:['Дүйсенбі','Сейсенбі','Сәрсенбі','Бейсенбі','Жұма','Сенбі','Жексенбі']} :
            {account:'Аккаунт', university:'О КазНУ', open:'Открыть', all:'Все дни', empty:'Нет занятий', files:'Скачать', sections:'Все разделы', close:'Закрыть', today:'Сегодня', now:'Идёт', next:'Следующая', done:'Прошла', days:['Понедельник','Вторник','Среда','Четверг','Пятница','Суббота','Воскресенье']};
    }
    function weekDates(option) {
        return (cleanText(option).match(/\d{1,2}\.\d{1,2}\.\d{4}/g) || []).map(function (value) {
            var p = value.split('.'); return new Date(+p[2], +p[1] - 1, +p[0]);
        });
    }
    function dayStart(date, shift) { return new Date(date.getFullYear(), date.getMonth(), date.getDate() + (shift || 0)); }
    // The displayed week from "Неделя N. дд.мм.гггг - дд.мм.гггг"; columns start on its Monday. Undated weeks are taken as the current one.
    function shownWeek(now) {
        var week = document.getElementById('weekSelect'), today = dayStart(now);
        var dates = week?.tagName === 'SELECT' && week.selectedIndex >= 0 ? weekDates(week.options[week.selectedIndex]) : [];
        var first = dates.length >= 2 ? dates[0] : today, last = dates.length >= 2 ? dates[dates.length - 1] : dayStart(today, 6 - (today.getDay() + 6) % 7);
        return {select:dates.length >= 2 ? week : null, first:first, last:last, monday:dayStart(first, -((first.getDay() + 6) % 7)), today:today};
    }
    // Index of today in the displayed week (Monday = 0), or -1 when another week is shown.
    function todayInWeek(now) {
        var week = shownWeek(now);
        if (week.today < week.first || week.today > week.last) return -1;
        return (week.today.getDay() + 6) % 7;
    }
    function agendaLabels() {
        function duration(minutes, units) {
            if (minutes < 60) return minutes + ' ' + units[2];
            if (minutes < 1440) return Math.floor(minutes / 60) + ' ' + units[1] + (minutes % 60 ? ' ' + minutes % 60 + ' ' + units[2] : '');
            var hours = Math.floor(minutes % 1440 / 60);
            return Math.floor(minutes / 1440) + ' ' + units[0] + (hours ? ' ' + hours + ' ' + units[1] : '');
        }
        if (selectedLanguage === 'en') return {
            now:'Class in progress', pause:'Break', gap:'Free window', first:'Until the first class', over:'Classes are over for today',
            dayOff:'Day off today', free:'No classes today', shown:'First class of this week', weekDone:'No more classes this week',
            hint:'Open the next week to see upcoming classes', last:'Last class today', then:'Then', showDay:'Show day', nextWeek:'Next week', region:'Schedule summary',
            left:function (m) { return duration(m, ['d','h','min']) + ' left'; }, until:function (m) { return 'in ' + duration(m, ['d','h','min']); },
            count:function (n) { return n + (n === 1 ? ' class' : ' classes'); },
            today:function (n, range) { return 'Today: ' + this.count(n) + ', ' + range; }, remain:function (n, end) { return this.count(n) + ' left, until ' + end; },
            when:function (shift, day, time) { return (shift === 0 ? 'today' : shift === 1 ? 'tomorrow' : labels().days[day]) + ' at ' + time; }};
        if (selectedLanguage === 'kz') return {
            now:'Сабақ жүріп жатыр', pause:'Үзіліс', gap:'Бос уақыт', first:'Бірінші сабаққа дейін', over:'Бүгінгі сабақтар аяқталды',
            dayOff:'Бүгін демалыс күні', free:'Бүгін сабақ жоқ', shown:'Осы аптадағы бірінші сабақ', weekDone:'Бұл аптада сабақ қалмады',
            hint:'Жақын сабақтарды көру үшін келесі аптаны ашыңыз', last:'Бүгінгі соңғы сабақ', then:'Содан кейін', showDay:'Күнді көрсету', nextWeek:'Келесі апта', region:'Кесте қорытындысы',
            left:function (m) { return duration(m, ['күн','сағ','мин']) + ' қалды'; }, until:function (m) { return duration(m, ['күн','сағ','мин']) + ' кейін'; },
            count:function (n) { return n + ' сабақ'; },
            today:function (n, range) { return 'Бүгін ' + this.count(n) + ', ' + range; }, remain:function (n, end) { return this.count(n) + ' қалды, ' + end + ' дейін'; },
            when:function (shift, day, time) { return (shift === 0 ? 'бүгін ' : shift === 1 ? 'ертең ' : labels().days[day] + ', ') + time; }};
        return {
            now:'Идёт пара', pause:'Перемена', gap:'Окно', first:'До первой пары', over:'Пары на сегодня закончились',
            dayOff:'Сегодня выходной', free:'Сегодня пар нет', shown:'Первая пара этой недели', weekDone:'На этой неделе пар больше нет',
            hint:'Откройте следующую неделю, чтобы увидеть ближайшие пары', last:'Это последняя пара сегодня', then:'Затем', showDay:'Показать день', nextWeek:'Следующая неделя', region:'Сводка расписания',
            left:function (m) { return 'ещё ' + duration(m, ['д','ч','мин']); }, until:function (m) { return 'через ' + duration(m, ['д','ч','мин']); },
            count:function (n) { var t = n % 10, h = n % 100; return n + (t === 1 && h !== 11 ? ' пара' : t >= 2 && t <= 4 && (h < 12 || h > 14) ? ' пары' : ' пар'); },
            today:function (n, range) { return 'Сегодня ' + this.count(n) + ', ' + range; }, remain:function (n, end) { return 'Осталось ' + this.count(n) + ', до ' + end; },
            when:function (shift, day, time) { return (shift === 0 ? 'сегодня' : shift === 1 ? 'завтра' : ['в понедельник','во вторник','в среду','в четверг','в пятницу','в субботу','в воскресенье'][day]) + ' в ' + time; }};
    }
    var css = `
html.univer-mobile { -webkit-text-size-adjust:100%; font-size:16px; }
html.univer-mobile body { margin:0!important; padding:0!important; font-size:16px!important; line-height:1.5; overflow-wrap:anywhere; }
html.univer-mobile body, html.univer-mobile td, html.univer-mobile th,
html.univer-mobile p, html.univer-mobile li, html.univer-mobile label { font-size:16px!important; }
html.univer-mobile .univer-app-shell { width:100%!important; max-width:100%!important; table-layout:fixed!important; height:auto!important; }
html.univer-mobile .univer-app-shell>tbody>tr>td { min-width:0; }
html.univer-mobile #l1 .header { font-size:12px!important; line-height:1.5; height:auto; padding:8px 12px!important; white-space:normal!important; }
html.univer-mobile #l1 .header a { display:inline-block; padding:6px 4px; }
html.univer-mobile #l1 .header .bvi-open { display:block!important; margin:0!important; width:auto!important; float:none!important; text-align:start; overflow-wrap:normal; padding:6px 0; }
html.univer-mobile #l1 img { min-width:24px; height:auto; vertical-align:middle; }
html.univer-mobile #l2, html.univer-mobile #l3, html.univer-mobile #l4, html.univer-mobile #l5 { height:auto!important; }
html.univer-mobile .univer-app-shell table.inner,
html.univer-mobile #tools, html.univer-mobile table.mt { width:100%!important; max-width:100%!important; table-layout:fixed; height:auto!important; }
html.univer-mobile .univer-app-shell table.inner>tbody>tr>td.left,
html.univer-mobile .univer-app-shell table.inner>tbody>tr>td.right,
html.univer-mobile #tools>tbody>tr>td.left, html.univer-mobile #tools>tbody>tr>td.right,
html.univer-mobile table.mt>tbody>tr>td.lt, html.univer-mobile table.mt>tbody>tr>td.rt { display:none!important; }
html.univer-mobile #l2 td.center { padding:10px 12px!important; }
html.univer-mobile #l2 #logo { float:none!important; max-width:100%; }
html.univer-mobile #l2 .links { float:none!important; min-width:0!important; padding:8px 0 0!important; overflow-wrap:anywhere; font-size:14px!important; }
html.univer-mobile #l2 .links a { display:inline-block; min-height:32px; padding:4px 8px 4px 0; }
html.univer-mobile .univer-app-shell .links { min-width:0!important; max-width:100%!important; float:none!important; white-space:normal!important; overflow-wrap:anywhere; }
html.univer-mobile .univer-app-shell .links a { float:none!important; display:inline-block; white-space:normal!important; font-size:15px!important; padding:6px 4px; }
html.univer-mobile #l3 td.center { padding:4px 8px!important; white-space:normal!important; }
html.univer-mobile #l3 a { display:inline-block!important; float:none!important; font-size:15px!important; line-height:1.4; min-height:40px; box-sizing:border-box; padding:10px 8px!important; }
html.univer-mobile #l4 td.center { padding:8px!important; }
html.univer-mobile table.mt>tbody>tr>td.ct,
html.univer-mobile table.mt>tbody>tr>td.tt { padding:8px!important; white-space:normal; overflow-wrap:anywhere; }
html.univer-mobile table.mt>tbody>tr.top>td.ct { font-weight:600; }
html.univer-mobile .univer-app-shell table.mt :is(p,li) { text-align:start!important; word-spacing:normal!important; }
html.univer-mobile .univer-app-shell table.mt :is(ol,ul) { padding-inline-start:24px!important; margin-inline:4px!important; }
html.univer-mobile #tools .tool { float:none!important; width:100%!important; height:auto!important; margin:0!important; background:white!important; border:0!important; }
html.univer-mobile #tools .top-center { height:auto!important; overflow:visible!important; padding:0!important; background:white!important; }
html.univer-mobile #tools .tool>tbody>tr { height:auto!important; }
html.univer-mobile #tools .tool>tbody>tr>td { height:auto!important; }
html.univer-mobile #tools :is(.top-left,.top-right,.bottom-left,.bottom-right,.middle-left,.middle-right) { display:none!important; }
html.univer-mobile #tools ul { padding:0!important; margin:0!important; list-style:none!important; }
html.univer-mobile #tools li { margin:0!important; border-bottom:1px solid #e2eaf3; background:none!important; list-style:none!important; }
html.univer-mobile #tools li:last-child { border-bottom:0; }
html.univer-mobile #tools li, html.univer-mobile #tools li a { line-height:1.5!important; font-size:16px!important; }
html.univer-mobile #tools li a { display:block; padding:12px!important; min-height:24px; color:#164e80; text-decoration:none; }
html.univer-mobile .univer-app-category { margin:8px; background:#fff; border:1px solid #d7e3ef; border-radius:8px; }
html.univer-mobile .univer-app-category>summary { padding:12px; min-height:24px; font-size:16px; font-weight:600; color:#184f86; cursor:pointer; }
html.univer-mobile .univer-app-category[open]>summary { border-bottom:1px solid #d7e3ef; }
html.univer-mobile #tools .univer-app-menu-footer { display:none!important; }
html.univer-mobile .univer-app-menu [hidden] { display:none!important; }
html.univer-mobile .univer-app-menu input[type=search] { display:block; box-sizing:border-box; width:calc(100% - 16px); margin:8px; padding:12px; border:1px solid #bdcfe3; border-radius:8px; min-height:48px; font:inherit; background:white; }
html.univer-mobile .univer-app-menu .univer-app-empty { margin:12px; color:#46596b; }
html.univer-mobile .univer-app-menu, html.univer-mobile .univer-app-info { margin:4px 8px 10px; background:#eef4fb; border:1px solid #c7d9ed; border-radius:10px; }
html.univer-mobile .univer-app-menu>summary, html.univer-mobile .univer-app-info>summary { cursor:pointer; padding:12px; font-size:16px; font-weight:600; color:#184f86; }
html.univer-mobile .univer-app-menu[open]>summary, html.univer-mobile .univer-app-info[open]>summary { border-bottom:1px solid #c7d9ed; }
html.univer-mobile #login_frm table.cols,
html.univer-mobile #login_frm table.cols>tbody,
html.univer-mobile #login_frm table.cols>tbody>tr,
html.univer-mobile #login_frm table.cols>tbody>tr>td { display:block; box-sizing:border-box; width:100%!important; }
html.univer-mobile #login_frm table.cols>tbody>tr>td { padding:12px 4px; }
html.univer-mobile #login_form { max-width:480px; margin:0 auto; }
html.univer-mobile #login_form input.text { box-sizing:border-box; width:100%!important; min-height:48px; font-size:16px!important; }
html.univer-mobile input[type=submit], html.univer-mobile input[type=button], html.univer-mobile button,
html.univer-mobile select { font-size:16px!important; min-height:44px; max-width:100%; }
html.univer-mobile input[type=text], html.univer-mobile input[type=password], html.univer-mobile textarea { font-size:16px!important; max-width:100%; box-sizing:border-box; }
html.univer-mobile img { max-width:100%; height:auto; }
html.univer-mobile #attentionSt .parent { position:static!important; width:auto!important; height:auto!important; overflow:visible!important; }
html.univer-mobile:has(#attentionSt), html.univer-mobile body:has(#attentionSt) { height:auto!important; overflow:auto!important; }
html.univer-mobile #attentionSt .block { position:static!important; top:auto!important; left:auto!important; width:auto!important; height:auto!important; margin:0!important; padding:20px 16px 32px!important; }
html.univer-mobile #attentionSt #check { width:24px; height:24px; vertical-align:middle; }
html.univer-mobile #attentionSt #btn { float:none!important; width:100%; margin-top:20px; }
html.univer-mobile #copyright { width:100%!important; font-size:12px!important; }
html.univer-mobile #copyright td { font-size:12px!important; overflow-wrap:anywhere; }
html.univer-mobile .univer-app-table-scroll { width:100%; max-width:100%; overflow:auto; -webkit-overflow-scrolling:touch; overscroll-behavior-x:contain; margin:8px 0; }
html.univer-mobile .univer-app-table-scroll>table { max-width:none!important; }
html.univer-mobile .univer-app-table-scroll table:not(.inner):not(.mt) { table-layout:auto!important; }
html.univer-mobile .univer-app-table-scroll table:not(.inner):not(.mt)>:is(thead,tbody,tfoot)>tr>:is(td,th) { min-width:7rem; }
html.univer-mobile table.univer-app-data, html.univer-mobile .univer-app-shell table.inner.univer-app-data { table-layout:auto!important; width:100%!important; max-width:none!important; }
html.univer-mobile .univer-app-data-field { min-width:7rem; }
html.univer-mobile .jexcel_container, html.univer-mobile .jss_container { max-width:100%; overflow:auto; }
html.univer-mobile #list { padding:0!important; margin:0!important; }
html.univer-mobile #list .inner { max-width:100%; overflow-wrap:anywhere; }
html.univer-mobile #list img { display:block; width:auto!important; max-width:100%!important; }
html.univer-mobile #list>li { box-sizing:border-box; width:100%!important; margin:0!important; padding:12px!important; }
html.univer-mobile #list :is(.left,.right) { float:none!important; max-width:100%!important; }
html.univer-mobile .univer-app-shell table.univer-app-news,
html.univer-mobile table.univer-app-news>tbody { display:block; width:100%!important; }
html.univer-mobile .univer-app-news>tbody>tr:has(>th) { display:none; }
html.univer-mobile .univer-app-news .news { display:block; margin:0 0 16px; padding:12px; background:white; border:1px solid #d7e3ef; border-radius:8px; }
html.univer-mobile .univer-app-news .news>td { display:block; width:auto!important; padding:0!important; }
html.univer-mobile .univer-app-news .news>td:first-child { display:flex; gap:8px; align-items:center; margin-bottom:12px; }
html.univer-mobile .univer-app-news .news>td:first-child>div { width:auto!important; padding:4px 8px; font-size:13px!important; margin:0!important; white-space:nowrap; }
html.univer-mobile .univer-app-news .news>td:first-child .date { display:flex; align-items:baseline; gap:6px; }
html.univer-mobile .univer-app-news .date br { display:none; }
html.univer-mobile .univer-app-news .news>td:nth-child(2) :is(p,div,span) { text-indent:0!important; max-width:100%!important; box-sizing:border-box; font-size:16px!important; line-height:1.55!important; }
html.univer-mobile .univer-app-news .news img { display:block; max-width:100%!important; width:auto!important; margin:8px auto; }
html.univer-mobile #simplemodal-overlay { position:fixed!important; inset:0!important; width:100%!important; height:100%!important; }
html.univer-mobile #simplemodal-container { position:fixed!important; box-sizing:border-box!important; left:50%!important; top:var(--univer-dialog-top,12px)!important; transform:translateX(-50%)!important; width:calc(100% - 24px)!important; max-width:560px!important; height:auto!important; max-height:var(--univer-dialog-height,calc(100dvh - 24px))!important; overflow:auto!important; padding:64px 12px 12px!important; border:1px solid #c7d9ed!important; border-radius:12px; box-shadow:0 8px 32px #193a5b55; background:white!important; color:#172b40; }
html.univer-mobile #simplemodal-container .simplemodal-wrap { height:auto!important; width:100%!important; max-width:100%; overflow:visible!important; }
html.univer-mobile #simplemodal-container .simplemodal-data { box-sizing:border-box!important; max-width:100%!important; }
html.univer-mobile .univer-app-modal-toolbar { position:fixed; top:var(--univer-dialog-top,12px); left:50%; transform:translateX(-50%); box-sizing:border-box; width:calc(100% - 24px); max-width:560px; height:56px; padding:6px 8px; display:flex; justify-content:flex-end; background:white; border-radius:12px 12px 0 0; border:1px solid #c7d9ed; border-bottom:0; }
html.univer-mobile .univer-app-modal-toolbar[hidden] { display:none!important; }
html.univer-mobile .univer-app-modal-toolbar .simplemodal-close { position:static!important; display:block!important; float:none!important; width:44px!important; height:44px!important; margin:0!important; background:white!important; border-radius:8px; color:#184f86; text-decoration:none; }
html.univer-mobile .univer-app-modal-toolbar .simplemodal-close::after { content:'×'; display:block; text-align:center; font:28px/44px sans-serif; }
html.univer-mobile #auth_modal { width:100%!important; padding:0!important; }
html.univer-mobile #modal_auth_frm>table { width:100%!important; table-layout:fixed; }
html.univer-mobile #modal_auth_frm>table>tbody>tr>td:first-child,
html.univer-mobile #modal_auth_frm>table>tbody>tr>td:last-child { display:none!important; }
html.univer-mobile #modal_auth_frm>table>tbody>tr>td:nth-child(2) { width:auto!important; height:auto!important; padding:8px!important; }
html.univer-mobile #modal_auth_frm label { display:block; margin:10px 0 4px; }
html.univer-mobile #modal_auth_frm input:not([type=hidden]) { display:block; width:100%!important; min-height:48px!important; font-size:16px!important; box-sizing:border-box; margin:4px 0!important; }
html.univer-mobile #modal_auth_frm input[type=text], html.univer-mobile #modal_auth_frm input[type=password] { border:1px solid #a9bbce; border-radius:6px; padding:10px; }
html.univer-mobile #modal_auth_login { background:#f4f7fa; color:#405468; text-align:start!important; }
html.univer-mobile #modal_auth_ajl_btn, html.univer-mobile #modal_agreement .btn-confirm,
html.univer-mobile #attentionSt #btn { background:#246298; color:white; border:0; border-radius:8px; padding:10px 12px; font-weight:600; }
html.univer-mobile #modal_auth_ajl_btn:disabled, html.univer-mobile #modal_agreement .btn-confirm:disabled,
html.univer-mobile #attentionSt #btn:disabled { opacity:0.5; }
html.univer-mobile #modal_agreement button:not(.btn-confirm) { border:1px solid #a9bbce; border-radius:8px; color:#184f86; background:#f4f7fa; padding:10px 12px; }
html.univer-mobile #modal_agreement { width:100%!important; max-width:100%!important; box-sizing:border-box!important; padding:8px!important; font-size:16px!important; }
html.univer-mobile #modal_agreement :is(p,span,label) { font-size:16px!important; line-height:1.55!important; white-space:normal!important; }
html.univer-mobile #agree_frm p { margin:12px 0!important; text-indent:0!important; text-align:start!important; }
html.univer-mobile #agree_frm :is(table,td) { max-width:100%; }
html.univer-mobile #modal_agreement #iconfirm { width:24px; height:24px; vertical-align:middle; margin:12px; }
html.univer-mobile #modal_agreement center { text-align:start; }
html.univer-mobile #modal_agreement center>div { display:flex; flex-wrap:wrap; gap:8px; margin-top:12px; }
html.univer-mobile #modal_agreement center>div>div { float:none!important; width:auto!important; flex:1; min-width:120px; }
html.univer-mobile #modal_agreement button { width:100%; min-height:48px; white-space:normal; }
html.univer-mobile #simplemodal-container iframe { display:block; box-sizing:border-box; max-width:100%!important; width:100%!important; height:var(--univer-frame-height,65dvh)!important; border:0; }
/* Compact header: original links and account nodes stay interactive. */
html.univer-mobile body { font-family:system-ui,-apple-system,Arial,sans-serif!important; color:#20374d; background:#eef3f8!important; }
html.univer-mobile #l1 .header { background:#164e80!important; color:white!important; padding:0 12px!important; text-align:start!important; }
html.univer-mobile .univer-app-header { display:flex; align-items:center; justify-content:space-between; gap:8px; }
html.univer-mobile[data-univer-corner^=TOP] .univer-app-header { min-height:68px; }
html.univer-mobile[data-univer-corner=TOP_LEFT] #l1 .header { padding-left:68px!important; }
html.univer-mobile[data-univer-corner=TOP_RIGHT] #l1 .header { padding-right:68px!important; }
html.univer-mobile[data-univer-corner^=BOTTOM] body { padding-bottom:76px!important; }
html.univer-mobile[data-univer-corner=TOP_RIGHT] .univer-app-modal-toolbar { padding-right:68px; }
html.univer-mobile[data-univer-corner=TOP_LEFT] .univer-app-modal-toolbar { padding-left:68px; }
html.univer-mobile .univer-app-university { flex:1; min-width:0; }
html.univer-mobile .univer-app-university>summary { cursor:pointer; font-size:12px; padding:12px 0; min-height:20px; }
html.univer-mobile .univer-app-university>div { padding:0 0 12px; font-size:12px; }
html.univer-mobile #l1 .univer-app-flags { display:flex; align-items:center; gap:0; flex-shrink:0; }
html.univer-mobile #l1 .univer-app-flags>a, html.univer-mobile #l1 .univer-app-flags>img { box-sizing:border-box; width:44px; min-height:44px; padding:12px 10px; display:inline-flex; align-items:center; justify-content:center; }
html.univer-mobile #l1 .univer-app-flags>a img { width:24px; }
html.univer-mobile #l1 .univer-app-flags>img { object-fit:contain; height:44px; }
html.univer-mobile #l1 .univer-app-flags>img { border:1px solid #ffffff70; border-radius:8px; }
html.univer-mobile #l2 td.center { background:white!important; display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:8px; padding:8px 12px!important; }
html.univer-mobile #l2 #logo { width:140px!important; height:44px!important; background-size:contain!important; background-position:left center!important; background-repeat:no-repeat!important; margin:0!important; }
html.univer-mobile #l2 .links { width:auto!important; padding:0!important; text-align:start!important; }
html.univer-mobile #l2 .univer-app-account>summary { padding:10px 12px; border:1px solid #d7e3ef; border-radius:8px; cursor:pointer; font-size:14px; min-height:24px; color:#164e80; }
html.univer-mobile #l2 .links:has(.univer-app-account[open]) { width:100%!important; }
html.univer-mobile #l2 .univer-app-account[open]>div { padding:10px 0; }
html.univer-mobile #l2 .univer-app-account a { font-size:14px!important; line-height:1.5; min-height:32px; }
html.univer-mobile #l3 td.center { background:#fff!important; display:flex; gap:6px; padding:6px 8px 10px!important; }
html.univer-mobile #l3 td.center>a { flex:1; min-width:0; display:flex!important; align-items:center; justify-content:center; border-radius:8px; border:1px solid #d7e3ef; background:#f4f7fb!important; min-height:44px; padding:8px 4px!important; font-size:14px!important; text-decoration:none; color:#164e80; }
html.univer-mobile #l3 td.center>a.active { background:#164e80!important; border-color:#164e80; color:#fff!important; }
html.univer-mobile #l3 a>.left, html.univer-mobile #l3 a>.right { display:none!important; }
html.univer-mobile #l3 a>.center { height:auto!important; background:none!important; padding:0!important; float:none!important; color:inherit!important; font:inherit!important; }
/* Week controls and a day-by-day representation of the actual schedule grid. */
html.univer-mobile table.mt.univer-app-study-panel,
html.univer-mobile table.mt.univer-app-study-panel>tbody,
html.univer-mobile table.mt.univer-app-study-panel>tbody>tr { display:block; box-sizing:border-box; width:100%!important; background:none!important; height:auto!important; border:0!important; }
html.univer-mobile table.mt.univer-app-study-panel { margin:8px 0 16px; }
html.univer-mobile table.mt.univer-app-study-panel>tbody>tr>:is(.ct,.tt) { display:block; box-sizing:border-box; width:100%!important; height:auto!important; background:none!important; border:0!important; }
html.univer-mobile table.mt.univer-app-study-panel>tbody>tr:has(>.pd), html.univer-mobile table.mt.univer-app-study-panel>tbody>.bot,
html.univer-mobile table.mt.univer-app-study-panel>tbody>.empty, html.univer-mobile .univer-app-study-empty { display:none!important; }
html.univer-mobile .univer-app-study-panel .ct.message { font-size:14px!important; padding:4px 8px!important; color:#61758a; }
html.univer-mobile .univer-app-study-panel .ct.message li { display:inline-block; font-size:14px!important; margin-inline-end:12px; }
html.univer-mobile .univer-app-page-link { display:block; box-sizing:border-box; margin:6px 0; padding:10px 12px; min-height:44px; border:1px solid #d7e3ef; border-radius:8px; background:white; color:#164e80; text-decoration:none; }
html.univer-mobile .univer-app-breadcrumb { display:flex!important; flex-wrap:wrap; gap:6px; }
html.univer-mobile .univer-app-breadcrumb>.univer-app-page-link { margin:0; font-size:14px!important; }
html.univer-mobile .univer-app-breadcrumb>.univer-app-page-link:last-of-type { flex-basis:100%; font-size:18px!important; font-weight:600; }
html.univer-mobile #weekSelect { display:block; box-sizing:border-box; width:100%!important; border:1px solid #bdcfe3; border-radius:8px; padding:10px 8px; background:white; color:#20374d; }
html.univer-mobile table.univer-app-schedule-source { display:none!important; }
html.univer-mobile .univer-app-schedule { min-width:0; width:100%; }
html.univer-mobile .univer-app-days { display:grid; grid-template-columns:repeat(6,minmax(44px,1fr)); gap:6px; margin:12px 0 14px; }
html.univer-mobile .univer-app-days button { min-width:44px; border:1px solid #bdcfe3; border-radius:8px; padding:8px; font:inherit; background:white; color:#164e80; }
html.univer-mobile .univer-app-days button:last-child { grid-column:1/-1; }
html.univer-mobile .univer-app-week-label { display:none; }
@media(max-width:360px) {
    html.univer-mobile .univer-app-days { grid-template-columns:repeat(3,minmax(44px,1fr)); }
    html.univer-mobile .univer-app-week-label { display:block; margin:8px 0 12px; font-size:14px!important; line-height:1.5; color:#61758a; }
}
html.univer-mobile .univer-app-days button[aria-pressed=true] { background:#164e80; border-color:#164e80; color:white; }
html.univer-mobile .univer-app-day h3 { font-size:18px; margin:12px 0; color:#164e80; }
html.univer-mobile .univer-app-day[hidden] { display:none!important; }
html.univer-mobile .univer-app-lesson { background:white; border:1px solid #bfd3ea; border-radius:10px; padding:14px; margin:0 0 12px; }
html.univer-mobile .univer-app-lesson time { display:inline-block; padding:4px 8px; border-radius:6px; background:#eef4fb; color:#164e80; font-size:14px; font-weight:700; }
html.univer-mobile .univer-app-lesson p { margin:8px 0 0; line-height:1.5; }
html.univer-mobile .univer-app-lesson .univer-app-subject { font-weight:700; }
html.univer-mobile .univer-app-no-classes { background:#fff; padding:16px; border:1px dashed #bdcfe3; border-radius:10px; }
/* UMKD cards use the original rows, download anchors and rating handlers. */
html.univer-mobile .univer-app-shell table.inner.univer-app-materials,
html.univer-mobile .univer-app-shell table.inner.univer-app-courses { display:block; width:100%!important; min-width:0!important; table-layout:fixed!important; }
html.univer-mobile :is(.univer-app-materials,.univer-app-courses)>tbody { display:block; width:100%; }
html.univer-mobile :is(.univer-app-materials,.univer-app-courses)>tbody>tr:has(>th:nth-child(2)) { display:none!important; }
html.univer-mobile .univer-app-materials>tbody>tr:has(>th:only-child) { display:block; }
html.univer-mobile .univer-app-materials>tbody>tr>th:only-child { display:block; padding:16px 4px 8px!important; background:none!important; color:#164e80!important; text-align:start!important; line-height:1.5; border:0!important; }
html.univer-mobile .univer-app-materials tr.file, html.univer-mobile .univer-app-courses tr.link { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); gap:10px; background:white!important; border:1px solid #d7e3ef; border-radius:10px; margin:8px 0 12px; padding:14px; }
html.univer-mobile :is(.univer-app-materials,.univer-app-courses)>tbody>tr>td { display:block; width:auto!important; min-width:0!important; border:0!important; padding:0!important; text-align:start!important; line-height:1.5; background:none!important; }
html.univer-mobile :is(.univer-app-materials tr.file,.univer-app-courses tr.link)>td:first-child { display:none!important; }
html.univer-mobile :is(.univer-app-materials tr.file,.univer-app-courses tr.link)>td:nth-child(2) { grid-column:1/-1; font-weight:700; }
html.univer-mobile .univer-app-materials tr.file>td:nth-child(3), html.univer-mobile .univer-app-materials tr.file>td:last-child { grid-column:1/-1; }
html.univer-mobile .univer-app-materials td[data-univer-field]::before { content:attr(data-univer-field); display:block; font-size:12px; font-weight:500; color:#61758a; }
html.univer-mobile .univer-app-materials tr.file>td:nth-child(3):empty { display:none; }
html.univer-mobile .univer-app-materials a.downLoad { display:block; min-height:44px; padding:10px 12px; border-radius:8px; background:#eef4fb; text-decoration:none; color:#164e80; overflow-wrap:anywhere; }
html.univer-mobile .univer-app-materials .stars { display:flex; flex-wrap:wrap; gap:4px; height:auto!important; }
html.univer-mobile .univer-app-materials svg.star { box-sizing:content-box; width:23px!important; height:25px!important; padding:11px; flex-shrink:0; }
html.univer-mobile .univer-app-courses tr.link>td:last-child { grid-column:1/-1; font-size:14px!important; color:#61758a; }
html.univer-mobile .univer-app-course-open { display:block; margin-top:12px; width:100%; padding:10px 12px; border:0; border-radius:8px; background:#246298; color:white; font:inherit; }
html.univer-mobile .univer-app-info[data-univer-info=links] :is(.links,table,tbody,tr,td) { display:block!important; box-sizing:border-box; width:100%!important; min-width:0!important; max-width:100%!important; height:auto!important; background:none!important; border:0!important; margin:0!important; padding:0!important; }
html.univer-mobile .univer-app-info[data-univer-info=links] .links a { display:flex!important; align-items:center; gap:8px; padding:10px 12px!important; min-height:24px; text-decoration:none; }
html.univer-mobile .univer-app-info[data-univer-info=links] .univer-app-table-scroll { margin:0!important; overflow:visible; }
html.univer-mobile #tools li:has(>a+a)>a { border-bottom:1px solid #e2eaf3; }
html.univer-mobile .univer-app-study-panel>tbody>tr>.ts { display:flow-root!important; box-sizing:border-box; width:100%!important; padding:8px 0!important; white-space:normal!important; background:none!important; border:0!important; }
html.univer-mobile .univer-app-study-panel :is(td,th) { white-space:normal!important; }
html.univer-mobile .univer-app-pairs, html.univer-mobile .univer-app-form-table,
html.univer-mobile .univer-app-pairs>tbody, html.univer-mobile .univer-app-form-table>tbody { display:block!important; width:100%!important; min-width:0!important; table-layout:fixed!important; }
html.univer-mobile .univer-app-pairs>tbody>tr { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); gap:4px 12px; padding:12px; margin:8px 0; background:white; border:1px solid #d7e3ef; border-radius:8px; }
html.univer-mobile .univer-app-pairs>tbody>tr>td { display:block; width:auto!important; min-width:0!important; border:0!important; padding:4px 0!important; line-height:1.5; }
html.univer-mobile .univer-app-pairs>tbody>tr>td:nth-child(odd) { font-size:13px!important; color:#61758a; }
@media(max-width:600px) { html.univer-mobile .univer-app-pairs>tbody>tr { grid-template-columns:minmax(0,1fr); }
html.univer-mobile .univer-app-pairs>tbody>tr>td:nth-child(even) { margin-bottom:8px; } }
html.univer-mobile .univer-app-form-table>tbody>tr, html.univer-mobile .univer-app-form-table>tbody>tr>:is(td,th) { display:block; width:100%!important; min-width:0!important; box-sizing:border-box; height:auto!important; border:0!important; padding:8px!important; }
html.univer-mobile .univer-app-form-table input:not([type=hidden]):not([type=checkbox]):not([type=radio]), html.univer-mobile .univer-app-form-table :is(select,textarea) { width:100%!important; box-sizing:border-box; min-height:44px; }
html.univer-mobile .univer-app-form-table input:is([type=checkbox],[type=radio]) { width:24px; height:24px; min-height:0; vertical-align:middle; margin:10px 8px 10px 0; }
html.univer-mobile .univer-app-form-table label:has(input:is([type=checkbox],[type=radio])) { display:flex; align-items:center; gap:8px; }
html.univer-mobile .univer-app-form-table :is(table,tbody,tr,td) { max-width:100%!important; min-width:0!important; }
html.univer-mobile .univer-app-form-table label { display:block; margin:8px 0; }
html.univer-mobile .univer-app-form-table img { max-width:100%; }
html.univer-mobile .univer-app-cards, html.univer-mobile .univer-app-shell table.inner.univer-app-cards,
html.univer-mobile .univer-app-cards>tbody, html.univer-mobile .univer-app-cards>thead { display:block; box-sizing:border-box; width:100%!important; min-width:0!important; }
html.univer-mobile .univer-app-card-header { display:none!important; }
html.univer-mobile .univer-app-card-header.univer-app-sort-head { display:flex!important; flex-wrap:wrap; gap:6px; margin:8px 0; }
html.univer-mobile .univer-app-sort-head>th { display:block!important; min-width:0!important; width:auto!important; padding:12px!important; border:1px solid #d7e3ef!important; border-radius:8px; font-size:12px!important; }
html.univer-mobile .univer-app-card-row { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); gap:10px; margin:8px 0 12px; padding:14px; background:white; border:1px solid #d7e3ef; border-radius:10px; }
html.univer-mobile .univer-app-card-row>:is(td,th) { display:block; width:auto!important; min-width:0!important; border:0!important; padding:0!important; white-space:normal!important; overflow-wrap:anywhere; line-height:1.5; }
html.univer-mobile .univer-app-card-row>td:first-child { grid-column:1/-1; font-weight:600; }
html.univer-mobile .univer-app-card-row>td[data-univer-field]::before { content:attr(data-univer-field); display:block; font-size:12px; font-weight:400; color:#61758a; margin-bottom:4px; }
html.univer-mobile .univer-app-card-row>td:has(a,input,textarea,select), html.univer-mobile .univer-app-card-row>.univer-app-long-field { grid-column:1/-1; }
html.univer-mobile .univer-app-card-row a { display:block; min-height:24px; padding:10px 0; overflow-wrap:anywhere; }
html.univer-mobile .univer-app-card-section, html.univer-mobile .univer-app-card-section>:is(td,th) { display:block; box-sizing:border-box; width:100%!important; min-width:0!important; padding:8px!important; border:0!important; background:none!important; }
html.univer-mobile .univer-app-matrix :is(td,th) { min-width:3rem!important; padding:8px!important; overflow-wrap:normal!important; word-break:normal!important; }
html.univer-mobile .univer-app-matrix tr>:first-child { min-width:6rem!important; }
html.univer-mobile .univer-app-nav-table, html.univer-mobile .univer-app-nav-table>tbody { display:block; width:100%!important; min-width:0!important; }
html.univer-mobile .univer-app-nav-table>tbody>tr { display:flex; flex-wrap:wrap; gap:6px; padding:6px 0; }
html.univer-mobile .univer-app-nav-table>tbody>tr>td { display:block; flex:1; min-width:110px!important; width:auto!important; padding:6px!important; }
html.univer-mobile .univer-app-nav-table a { display:block; box-sizing:border-box; padding:10px; min-height:24px; border:1px solid #d7e3ef; border-radius:8px; background:white; text-decoration:none; }
html.univer-mobile .univer-app-list { max-width:100%!important; width:auto!important; box-sizing:border-box; margin:12px 0!important; padding-inline-start:24px!important; }
html.univer-mobile .univer-app-list>li { width:auto!important; max-width:100%!important; float:none!important; padding:8px 0!important; height:auto!important; }
html.univer-mobile .univer-app-list a { display:inline-block; min-height:24px; padding:10px 0; overflow-wrap:anywhere; }
/* Printed HTML: reflow layout containers, scroll only genuine matrices. */
html.univer-mobile.univer-app-document body { padding:12px!important; box-sizing:border-box; width:100%!important; max-width:100%!important; background:white!important; }
html.univer-mobile.univer-app-document :is(p,span,div,h1,h2,h3) { max-width:100%!important; box-sizing:border-box; white-space:normal!important; text-indent:0!important; line-height:1.6!important; }
html.univer-mobile.univer-app-document :is(p,span) { font-size:16px!important; }
html.univer-mobile.univer-app-document h1 { font-size:22px!important; }
html.univer-mobile .univer-app-document-layout, html.univer-mobile .univer-app-document-layout>tbody,
html.univer-mobile .univer-app-document-layout>tbody>tr, html.univer-mobile .univer-app-document-layout>tbody>tr>:is(td,th) { display:block!important; width:100%!important; min-width:0!important; max-width:100%!important; box-sizing:border-box; height:auto!important; padding:4px 0!important; border:0!important; }
html.univer-mobile.univer-app-document .univer-app-table-scroll { margin:12px 0; border:1px solid #d7e3ef; border-radius:8px; }
html.univer-mobile.univer-app-document .univer-app-table-scroll>table:not(.univer-app-document-layout):not(.univer-app-pairs) { width:auto!important; min-width:100%; table-layout:auto!important; }
html.univer-mobile.univer-app-document .noprint { font-size:14px!important; line-height:1.4!important; padding:4px 0!important; }
html.univer-mobile.univer-app-document .noprint br { display:none!important; }
html.univer-mobile.univer-app-document .noprint a { display:inline-flex; align-items:center; min-height:44px; padding:0 10px; font-size:14px!important; }
html.univer-mobile .univer-app-pairs li { margin:0!important; padding:0!important; list-style:none!important; }
html.univer-mobile.univer-app-document .univer-app-table-scroll>table>:is(thead,tbody,tfoot)>tr>:is(td,th) { min-width:4rem!important; max-width:24rem; font-size:14px!important; padding:8px!important; white-space:normal!important; overflow-wrap:normal!important; word-break:normal!important; }
html.univer-mobile .univer-app-scroll-hint { margin:12px 0 4px; font-size:13px!important; line-height:1.4; color:#526b82; }
html.univer-mobile .univer-app-scroll-hint[hidden] { display:none!important; }
html.univer-mobile.univer-app-document[data-univer-corner^=BOTTOM] body { padding-bottom:76px!important; }
html.univer-mobile[data-univer-corner^=TOP] body:has(#attentionSt):not(:has(#l1)) { padding-top:76px!important; }
/* Keep study content first; auxiliary information belongs after it. */
html.univer-mobile .univer-app-quick { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:8px; margin:8px; }
html.univer-mobile .univer-app-quick a { display:flex; align-items:center; justify-content:center; min-height:48px; box-sizing:border-box; padding:10px; border:1px solid #c7d9ed; border-radius:10px; background:white; color:#164e80; text-decoration:none; text-align:center; font-weight:600; }
/* The current menu page: a tint plus a left bar, so the state does not rest on colour alone. */
html.univer-mobile #tools a[aria-current=page] { background:#e4effb; color:#123f69; font-weight:700; border-left:3px solid #246298; }
html.univer-mobile .univer-app-menu-return { position:fixed; top:8px; left:8px; z-index:999; padding:10px 14px; min-height:44px; border:1px solid #bdcfe3; border-radius:10px; background:#fff; color:#164e80; box-shadow:0 2px 8px #193a5b22; }
html.univer-mobile[data-univer-corner=TOP_LEFT] .univer-app-menu-return { left:76px; }
html.univer-mobile .univer-app-menu-return[hidden] { display:none!important; }
html.univer-mobile .univer-app-shell :is(.scrool,.scrool200) { width:100%!important; max-width:100%!important; min-width:0!important; box-sizing:border-box; }
html.univer-mobile .univer-app-catalog .scrool200 { height:auto!important; max-height:none!important; overflow:visible!important; }
html.univer-mobile .univer-app-catalog, html.univer-mobile .univer-app-faq { margin:8px 0; border:1px solid #d7e3ef; border-radius:10px; background:white; }
html.univer-mobile :is(.univer-app-catalog,.univer-app-faq)>summary { cursor:pointer; padding:12px; min-height:24px; font-weight:600; color:#164e80; }
html.univer-mobile .univer-app-catalog-title { display:none!important; }
html.univer-mobile .univer-app-catalog>table.mt, html.univer-mobile .univer-app-catalog>table.mt>tbody,
html.univer-mobile .univer-app-catalog>table.mt>tbody>tr, html.univer-mobile .univer-app-catalog>table.mt>tbody>tr>td { display:block; width:100%!important; min-width:0!important; box-sizing:border-box; border:0!important; background:none!important; padding:0!important; }
html.univer-mobile .univer-app-catalog>table.mt>tbody>tr:has(>.ts) { display:none!important; }
html.univer-mobile .univer-app-faq-row { display:block!important; }
html.univer-mobile .univer-app-faq>table.inner, html.univer-mobile .univer-app-faq>table.inner>tbody,
html.univer-mobile .univer-app-faq>table.inner>tbody>tr { display:block; width:100%!important; border:0!important; background:none!important; }
html.univer-mobile .univer-app-faq>table.inner>tbody>tr>:is(td,th) { display:block; border:0!important; background:none!important; padding:8px 12px!important; line-height:1.5; text-align:start!important; }
html.univer-mobile .univer-app-faq>table.inner>tbody>tr>th { color:#61758a; font-size:13px!important; }
html.univer-mobile .univer-app-catalog table.inner, html.univer-mobile .univer-app-catalog table.inner>tbody { display:block; width:100%!important; }
html.univer-mobile .univer-app-catalog tr.file { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1fr); gap:8px; padding:12px; margin:8px; border:1px solid #d7e3ef; border-radius:8px; }
html.univer-mobile .univer-app-catalog tr.file[hidden] { display:none!important; }
html.univer-mobile .univer-app-catalog tr.file>td { display:block; min-width:0!important; width:auto!important; border:0!important; padding:0!important; overflow-wrap:anywhere; }
html.univer-mobile .univer-app-catalog tr.file>td:nth-child(-n+2) { display:none; }
html.univer-mobile .univer-app-catalog tr.file>td:nth-child(3) { grid-column:1/-1; font-weight:600; }
html.univer-mobile .univer-app-catalog tr.file>td[data-univer-field]::before { content:attr(data-univer-field); display:block; color:#61758a; font-size:12px; font-weight:400; }
html.univer-mobile .univer-app-catalog-search { width:100%; box-sizing:border-box; min-height:48px; margin:8px 0 12px; padding:12px; border:1px solid #bdcfe3; border-radius:8px; font:inherit; }
html.univer-mobile .univer-app-catalog-download { display:block; width:100%; margin-top:10px; border:0; border-radius:8px; background:#eef4fb; padding:10px; color:#164e80; }
html.univer-mobile .univer-app-catalog-head { display:none!important; }
html.univer-mobile .univer-app-gpa-layout, html.univer-mobile .univer-app-gpa-layout>tbody, html.univer-mobile .univer-app-gpa-layout>tbody>tr, html.univer-mobile .univer-app-gpa-layout>tbody>tr>td { display:block; width:100%!important; box-sizing:border-box; min-width:0!important; border:0!important; background:none!important; }
html.univer-mobile #gpa-table.univer-app-cards input, html.univer-mobile #gpa-table.univer-app-cards select { width:100%!important; min-height:48px; padding:10px; border:1px solid #bdcfe3; border-radius:8px; }
html.univer-mobile .univer-app-cards :is(input[type=text],input[type=number],textarea,select) { max-width:100%; min-height:44px; box-sizing:border-box; }
html.univer-mobile .univer-app-cards input:is([type=checkbox],[type=radio]) { width:24px; height:24px; vertical-align:middle; }
html.univer-mobile .univer-app-cards a, html.univer-mobile .univer-app-list a { display:inline-flex; align-items:center; min-height:44px; }
html.univer-mobile table.univer-app-sort-collapsed .univer-app-sort-head { display:none!important; }
html.univer-mobile .univer-app-sort-toggle { width:100%; text-align:start; border:1px solid #bdcfe3; border-radius:8px; padding:12px; background:#eef4fb; color:#164e80; }
html.univer-mobile :is(button,input,select,textarea,a,summary):focus-visible { outline:3px solid #2b79bd; outline-offset:2px; }
html.univer-mobile #l4 .ts { white-space:normal!important; }
html.univer-mobile #l4 td.center { background:#eef3f8!important; }
html.univer-mobile #l5, html.univer-mobile #l5 td.center, html.univer-mobile #copyright,
html.univer-mobile #copyright>tbody>tr>td { background:#eef3f8!important; border:0!important; color:#61758a; }
html.univer-mobile .univer-app-study-panel>tbody>tr>.ct { font-size:18px!important; line-height:1.45; }
html.univer-mobile #l4 .ts a { display:inline-flex; align-items:center; min-height:44px; padding:0 10px; }
html.univer-mobile #l4 .univer-app-media-column { width:100%!important; float:none!important; position:static!important; margin:12px 0; }
html.univer-mobile #l4 iframe.univer-app-video { display:block; width:100%!important; height:auto!important; aspect-ratio:16/9; border:0; }
html.univer-mobile #modal_auth_frm label { margin:6px 0 2px; font-size:14px!important; }
html.univer-mobile #modal_auth_frm>table>tbody>tr>td:nth-child(2) { padding:4px!important; }
/* PC presentation. Responsive cards remain available in a narrow window. */
html.univer-mobile body { font-family:Segoe UI,Arial,sans-serif!important; background:#f3f6f9!important; color:#16283b!important; padding-bottom:0!important; }
html.univer-mobile :is(td,th,p,li,label,h1,h2,h3,h4,h5,h6) { font-family:Segoe UI,Arial,sans-serif!important; }
html.univer-mobile[data-univer-corner^=BOTTOM] body { padding-bottom:0!important; }
html.univer-mobile .univer-app-shell { max-width:1600px!important; margin:0 auto!important; }
html.univer-mobile #l1 .header { background:#1b5fa6!important; padding:0 24px!important; color:white!important; }
html.univer-mobile #l1 .header :is(a,span,td) { color:inherit!important; }
html.univer-mobile #l2 td.center { padding:14px 24px!important; border-bottom:1px solid #dfe5ec!important; }
html.univer-mobile #l4 td.center { padding:20px 24px!important; background:transparent!important; }
html.univer-mobile table.mt { background:transparent!important; border:0!important; }
html.univer-mobile table.mt>tbody>tr.top>:is(.ct,.tt) { background:none!important; font-size:20px!important; color:#16283b!important; padding:12px 0!important; }
html.univer-mobile table.mt>tbody>tr>:is(.lt,.rt,.pd), html.univer-mobile table.mt>tbody>.bot { display:none!important; }
html.univer-mobile .univer-app-menu { margin:0 0 20px; border:1px solid #dfe5ec; background:white; }
html.univer-mobile #l4 .univer-app-content-columns,
html.univer-mobile #l4 .univer-app-content-columns>tbody,
html.univer-mobile #l4 .univer-app-content-columns>tbody>tr,
html.univer-mobile #l4 .univer-app-content-columns>tbody>tr>td { display:block!important; width:100%!important; min-width:0!important; box-sizing:border-box; padding:0!important; }
html.univer-mobile #l4 .univer-app-content-columns>tbody>tr>td+td { margin-top:16px; }
html.univer-mobile #l4 :is(input[type=button],input[type=submit],button):not(.univer-app-course-open):not(.univer-app-catalog-download):not(.univer-app-sort-toggle):not(.univer-app-days button):not(.univer-app-schedule-modes button):not(.univer-app-news-tools button):not(.univer-app-news-jump button):not(.univer-app-attendance-overview-name):not(.univer-app-attendance-details-toggle) { padding:9px 16px!important; border:1px solid #1b5fa6; border-radius:6px; background:#1b5fa6; color:white; cursor:pointer; }
html.univer-mobile #l4 :is(input[type=button],input[type=submit],button):disabled { opacity:.55; cursor:default; }
html.univer-mobile .univer-app-data th { padding:10px!important; color:#16283b!important; background:#e9f1fb!important; font-weight:600; }
html.univer-mobile .univer-app-empty-result { height:auto!important; min-height:64px!important; max-height:none!important; overflow:auto!important; }
html.univer-mobile .univer-app-empty-result .univer-app-card-section td { text-align:center!important; }
html.univer-mobile #l4 .ct>a { margin-inline-end:8px; }
html.univer-mobile #l4 .univer-app-div-records { min-width:760px!important; table-layout:auto!important; }
html.univer-mobile .univer-app-div-records .full-width:has(>div) { display:grid!important; grid-template-columns:minmax(140px,1fr) minmax(200px,1.5fr) minmax(84px,.5fr) minmax(128px,1fr) minmax(96px,.7fr) minmax(84px,.5fr); width:100%!important; float:none!important; }
html.univer-mobile .univer-app-div-records .full-width>div { width:auto!important; min-width:0!important; float:none!important; padding:8px!important; box-sizing:border-box; overflow-wrap:normal!important; }
html.univer-mobile .univer-app-semester-nav { display:flex; flex-wrap:wrap; gap:8px; margin:12px 0; }
html.univer-mobile .univer-app-semester-nav a { display:inline-flex; align-items:center; min-height:44px; padding:0 14px; border:1px solid #dfe5ec; border-radius:6px; background:white; text-decoration:none; }
html.univer-mobile .univer-app-calendar-meta>tbody>tr { display:grid!important; grid-template-columns:minmax(140px,.5fr) minmax(0,1fr); gap:4px; }
html.univer-mobile .univer-app-calendar-meta>tbody>tr>td { min-width:0!important; padding:8px!important; }
html.univer-mobile .univer-app-empty-nav-cell { display:none!important; }
html.univer-mobile .univer-app-menu>summary { color:#1b5fa6; padding:14px 18px; }
html.univer-mobile .univer-app-category>summary { color:#16283b; }
html.univer-mobile #tools li a:hover, html.univer-mobile #tools li a:focus-visible { background:#e9f1fb; outline:2px solid #1b5fa6; outline-offset:-2px; }
html.univer-mobile .univer-app-table-scroll { background:white; border:1px solid #dfe5ec; border-radius:8px; margin:12px 0; }
html.univer-mobile .univer-app-matrix :is(td,th) { padding:8px 10px!important; white-space:normal; }
html.univer-mobile :is(input,select,textarea,button,a,summary):focus-visible { outline:2px solid #1b5fa6; outline-offset:2px; }
html.univer-mobile #login_form { border:1px solid #dfe5ec; background:white; border-radius:12px; padding:24px; box-sizing:border-box; }
html.univer-mobile #login_frm, html.univer-mobile #login_frm :is(td,p,strong,b,span,label,li,a,font) { font-family:Segoe UI,Arial,sans-serif!important; color:#16283b; }
html.univer-mobile #login_frm table.cols { background:transparent!important; border:0!important; }
html.univer-mobile #login_frm table.cols>tbody>tr>td { background:transparent!important; border:0!important; }
html.univer-mobile #login_frm>table.mt>tbody>tr.mid>td.tt { background:transparent!important; border:0!important; padding:0!important; }
html.univer-mobile #login_frm a { color:#1b5fa6!important; text-decoration:underline; }
html.univer-mobile #login_form :is(p,strong,label) { line-height:1.5!important; }
html.univer-mobile #login_form input.text { border:1px solid #bccbda; border-radius:7px; padding:10px 12px; background:white; }
html.univer-mobile #login_form input[type=submit] { background:#1b5fa6; color:white; border:0; border-radius:7px; padding:10px 22px; cursor:pointer; }
html.univer-mobile #simplemodal-container, html.univer-mobile .univer-app-modal-toolbar { max-width:720px!important; }
html.univer-mobile.univer-app-document body { padding:24px!important; background:white!important; }
@media (min-width:900px) {
  html.univer-mobile .univer-app-sort-toggle { display:none!important; }
  html.univer-mobile .univer-app-shell table.inner.univer-app-materials,
  html.univer-mobile .univer-app-shell table.inner.univer-app-courses { display:table!important; table-layout:auto!important; width:100%!important; }
  html.univer-mobile .univer-app-shell :is(.univer-app-materials,.univer-app-courses)>tbody>tr>:is(td,th) { display:table-cell!important; width:auto!important; min-width:7ch!important; padding:10px 12px!important; }
  html.univer-mobile .univer-app-shell :is(.univer-app-materials,.univer-app-courses)>tbody>tr>:nth-child(2) { min-width:24ch!important; overflow-wrap:break-word!important; }
  html.univer-mobile .univer-app-shell :is(.univer-app-materials tr.file,.univer-app-courses tr.link)>td:first-child { display:table-cell!important; }
  html.univer-mobile :is(td,th,p,li,label) { font-size:14px!important; }
  html.univer-mobile #login_frm table.cols>tbody>tr { display:grid!important; grid-template-columns:minmax(320px,.85fr) minmax(0,1.15fr); gap:28px; align-items:start; }
  html.univer-mobile #login_frm table.cols>tbody>tr>td { width:auto!important; min-width:0; padding:12px 0!important; }
  html.univer-mobile #login_form { margin:0!important; max-width:none; }
  html.univer-mobile .univer-app-pairs tr { display:grid!important; grid-template-columns:minmax(160px,.3fr) minmax(0,1fr); padding:10px 16px!important; }
  html.univer-mobile .univer-app-pairs td { width:auto!important; padding:4px 8px!important; }
  html.univer-mobile .univer-app-cards { display:table!important; width:100%!important; border-collapse:collapse!important; }
  html.univer-mobile .univer-app-cards>tbody { display:table-row-group!important; }
  html.univer-mobile .univer-app-cards :is(.univer-app-card-row,.univer-app-card-header,.univer-app-sort-head,.univer-app-card-section) { display:table-row!important; }
  html.univer-mobile .univer-app-cards .univer-app-card-section>:is(td,th) { width:auto!important; padding:20px 14px!important; color:#61758a; font-weight:400; }
  html.univer-mobile .univer-app-cards :is(td,th) { display:table-cell!important; width:auto!important; border-bottom:1px solid #dfe5ec!important; padding:12px 14px!important; background:white!important; text-align:start!important; }
  html.univer-mobile .univer-app-cards th { background:#e9f1fb!important; color:#16283b!important; }
  html.univer-mobile .univer-app-cards td::before { display:none!important; }
  html.univer-mobile .univer-app-cards .univer-app-card-row:hover td { background:#f3f6f9!important; }
  html.univer-mobile .univer-app-materials, html.univer-mobile .univer-app-courses { width:100%!important; }
  html.univer-mobile :is(.univer-app-materials,.univer-app-courses)>tbody { display:table-row-group!important; }
  html.univer-mobile :is(.univer-app-materials,.univer-app-courses)>tbody>tr { display:table-row!important; }
  html.univer-mobile :is(.univer-app-materials,.univer-app-courses)>tbody>tr>td { display:table-cell!important; padding:10px 12px!important; border-bottom:1px solid #dfe5ec!important; }
  html.univer-mobile .univer-app-materials>tbody>tr:has(>th:nth-child(2)), html.univer-mobile .univer-app-courses>tbody>tr:has(>th:nth-child(2)) { display:table-row!important; }
  html.univer-mobile .univer-app-materials td::before { display:none!important; }
}
`;
    function moveHeader(node, destination) {
        var home = document.createComment('univer-original-header');
        node.parentNode.insertBefore(home, node); destination.appendChild(node);
        headerMoves.push({node:node, home:home});
    }
    function adaptHeader() {
        var text = labels(), header = document.querySelector('#l1 .header');
        if (header && !header.querySelector('.univer-app-header')) {
            // E-services nests all three flags in one span with line breaks.
            // Move the actual links/images, retaining their original homes.
            var flagItems=Array.from(header.querySelectorAll('img')).filter(function(img){return /\/(kz|ru|en)\.png$/i.test(img.getAttribute('src')||'');}).map(function(img){return img.closest('a')||img;});
            var nodes = Array.from(header.childNodes);
            var bar = document.createElement('div'); bar.className = 'univer-app-header';
            var info = document.createElement('details'); info.className = 'univer-app-university';
            var title = document.createElement('summary'); title.textContent = text.university;
            var content = document.createElement('div'); info.append(title, content);
            var flags = document.createElement('div'); flags.className = 'univer-app-flags';
            flags.setAttribute('role', 'group');
            bar.append(info, flags); header.appendChild(bar);
            nodes.forEach(function (node) {
                moveHeader(node, flagItems.includes(node) ? flags : content);
            });
            flagItems.forEach(function(flag){if(flag.parentElement!==flags)moveHeader(flag,flags);});
        }
        // Some server pages append the remaining flags after the first pass.
        var flagBar=header?.querySelector('.univer-app-flags');
        if(flagBar)Array.from(header.querySelectorAll('img')).filter(function(img){return /\/(kz|ru|en)\.png$/i.test(img.getAttribute('src')||'');}).forEach(function(img){
            var item=img.closest('a')||img;if(item.parentElement!==flagBar)moveHeader(item,flagBar);
        });
        header?.querySelector('.univer-app-flags')?.setAttribute('aria-label', selectedLanguage === 'en' ? 'Language' : selectedLanguage === 'kz' ? 'Тіл' : 'Язык');
        var account = document.querySelector('#l2 .links');
        if (account?.querySelector('a[href^="/user/pass"]') && !account.querySelector('.univer-app-account')) {
            var children = Array.from(account.childNodes);
            var details = document.createElement('details'); details.className = 'univer-app-account';
            var summary = document.createElement('summary'); summary.textContent = text.account;
            var body = document.createElement('div'); details.append(summary, body); account.appendChild(details);
            children.forEach(function (node) { moveHeader(node, body); });
        }
        [['.univer-app-university>summary',text.university],['.univer-app-account>summary',text.account]].forEach(function (pair) {
            var node = document.querySelector(pair[0]); if (node && node.textContent !== pair[1]) node.textContent = pair[1];
        });
        unifyHeader(header);
    }
    // Desktop: one compact bar instead of three stacked rows. Flags stay inside .header, where
    // language detection and the trusted flag-click listener look for them; every move is restorable.
    function unifyHeader(header) {
        var bar = header?.querySelector('.univer-app-header');
        if (!bar) return;
        // Late server nodes (line breaks, separators) would add an empty line under the bar.
        var info = document.querySelector('.univer-app-university>div');
        if (info) Array.from(header.childNodes).forEach(function (node) {
            if (node !== bar && node.nodeType !== 8 && !(node.nodeType === 3 && !node.data.trim())) moveHeader(node, info);
        });
        var text = labels(), brand = bar.querySelector('.univer-app-brand'), tabs = bar.querySelector('.univer-app-tabs');
        var actions = bar.querySelector('.univer-app-actions');
        if (!brand) {
            brand = document.createElement('div'); brand.className = 'univer-app-brand';
            tabs = document.createElement('nav'); tabs.className = 'univer-app-tabs';
            actions = document.createElement('div'); actions.className = 'univer-app-actions';
            var sections = document.createElement('button'); sections.type = 'button'; sections.className = 'univer-app-sections';
            sections.setAttribute('aria-expanded', 'false');
            sections.addEventListener('click', function (event) { event.stopPropagation(); toggleMenu(); });
            bar.prepend(brand, tabs); bar.append(actions); actions.appendChild(sections);
        }
        var flags = bar.querySelector('.univer-app-flags');
        if (flags && flags.parentElement !== actions) actions.appendChild(flags);
        var logo = document.querySelector('#l2 #logo');
        if (logo) { moveHeader(logo, brand); logo.closest('#l2')?.classList.add('univer-app-header-row'); }
        var links = document.querySelector('#l2 .links');
        if (links && (links.querySelector('.univer-app-account') || links.querySelector('a[href]'))) {
            var row = links.closest('#l2'); moveHeader(links, actions); row?.classList.add('univer-app-header-row');
        }
        var tabRow = document.querySelector('#l3');
        if (tabRow) {
            var moved = false;
            tabRow.querySelectorAll('td.center>a[href]').forEach(function (tab) { moveHeader(tab, tabs); moved = true; });
            // The login page keeps an empty decorative tab strip.
            if (moved || (!cleanText(tabRow) && !tabRow.querySelector('a[href],input,select,textarea,button,img,iframe'))) tabRow.classList.add('univer-app-header-row');
        }
        tabs.setAttribute('aria-label', selectedLanguage === 'en' ? 'Main sections' : selectedLanguage === 'kz' ? 'Негізгі бөлімдер' : 'Основные разделы');
        var university = bar.querySelector('.univer-app-university'), footer = document.querySelector('#l5 td.center');
        if (university && footer) moveHeader(university, footer);
        var button = bar.querySelector('.univer-app-sections');
        if (button && button.textContent !== text.sections) button.textContent = text.sections;
        if (button) button.hidden = !document.getElementById('tools');
        document.documentElement.classList.add('univer-app-unified');
    }
    function toggleMenu(force) {
        var menu = document.querySelector('.univer-app-menu');
        if (!menu) return;
        var open = typeof force === 'boolean' ? force : !menu.open;
        if (!open) { closePopup(menu); return; }
        menu.open = true;
        menu.querySelector('input[type=search]')?.focus({preventScroll:true});
    }
    // Frequent menus close immediately, so Escape never leaves invisible focusable content behind.
    function closePopup(popup) {
        if (!popup?.open) return;
        var hadFocus = popup.contains(document.activeElement);
        window.__univerMotionUI?.exit(popup);
        popup.open = false;
        if (hadFocus) {
            var trigger = popup.classList.contains('univer-app-menu') ? document.querySelector('.univer-app-sections') : popup.querySelector('summary');
            trigger?.focus({preventScroll:true});
        }
    }
    function syncMenuButton() {
        // "toggle" is queued; it can arrive after Original mode removed the adapter's nodes.
        if (!enabled) return;
        var menu = document.querySelector('.univer-app-menu'), button = document.querySelector('.univer-app-sections');
        if (button && button.getAttribute('aria-expanded') !== String(!!menu?.open)) button.setAttribute('aria-expanded', String(!!menu?.open));
    }
    function dismissMenu(event) {
        // Header popups close like ordinary menus: Escape or a click elsewhere.
        [document.querySelector('.univer-app-menu'), document.querySelector('.univer-app-actions .univer-app-account')].forEach(function (popup) {
            if (!popup?.open) return;
            var outside = event.type === 'keydown' ? event.key === 'Escape' :
                !popup.contains(event.target) && !event.target.closest?.('.univer-app-sections');
            if (outside) closePopup(popup);
        });
    }
    // This adapter copies itself into same-origin frames (initializeMobile.toString()), where the shared helpers of
    // page-common.js may be missing, so it keeps its own.
    function ownRows(table) { return Array.from(table.rows).filter(function (r) { return r.closest('table') === table; }); }
    function cleanText(node) { return (node?.textContent || '').replace(/\s+/g, ' ').trim(); }
    function setText(node, text) { if (node.textContent !== text) node.textContent = text; }
    function adaptStudyPages() {
        weekViews.forEach(function (record, select) { if (!select.isConnected) { select.removeEventListener('change', record.update); record.caption.remove(); weekViews.delete(select); } });
        if (/^\/student\/(myschedule|umkd)(\/|$)/.test(location.pathname)) {
            document.querySelectorAll('#l4 table.mt').forEach(function (table) {
                if (table.closest('#tools,.univer-app-info,form')) return;
                table.classList.add('univer-app-study-panel');
                ownRows(table).forEach(function (row) { if (!cleanText(row) && !row.querySelector('input,select,img')) row.classList.add('univer-app-study-empty'); });
            });
            document.querySelectorAll('#l4 a[href]').forEach(function (a) {
                if (!a.closest('#tools') && /^\/student\/(myschedule|umkd|interactive\/syllabus)\//.test(new URL(a.href, location.href).pathname) && !a.classList.contains('downLoad'))
                    a.classList.add('univer-app-page-link');
            });
        }
        var week = document.getElementById('weekSelect');
        if (week?.tagName === 'SELECT') {
            var controls = week.closest('.ts') || week.parentElement;
            controls.classList.add('univer-app-schedule-controls');
            if (!weekViews.has(week)) {
                var caption = document.createElement('p'); caption.className = 'univer-app-week-label';
                var update = function () { var value = cleanText(week.options[week.selectedIndex]); if (caption.textContent !== value) caption.textContent = value; };
                week.after(caption); week.addEventListener('change', update); weekViews.set(week, {caption:caption, update:update});
            }
            weekViews.get(week).update();
        }
        if (!/^\/student\/umkd(\/|$)/.test(location.pathname)) return;
        document.querySelectorAll('#l4 .ct').forEach(function (cell) {
            if (cell.querySelectorAll(':scope>a.univer-app-page-link').length < 2) return;
            cell.classList.add('univer-app-breadcrumb');
            Array.from(cell.childNodes).forEach(function (node) {
                if (node.nodeType === 3 && /^[\s\\/]+$/.test(node.data) && /[\\/]/.test(node.data)) {
                    if (!punctuation.has(node)) punctuation.set(node, node.data);
                    node.data = '';
                }
            });
        });
        document.querySelectorAll('#l4 table.inner').forEach(function (table) {
            if (table.closest('#tools')) return;
            var rows = ownRows(table), header = rows.find(function (r) { return r.cells.length >= 3 && r.cells[0].tagName === 'TH'; });
            if (!header) return;
            if (table.querySelector('tr.file a.downLoad[href^="/student/umkd/get/"]')) {
                table.classList.add('univer-app-materials');
                wrapTable(table);
                rows.filter(function (r) { return r.matches('tr.file'); }).forEach(function (row) {
                    Array.from(row.cells).forEach(function (cell, i) {
                        if (i > 1 && cleanText(header.cells[i]) && !cell.hasAttribute('data-univer-field'))
                            cell.setAttribute('data-univer-field', cleanText(header.cells[i]));
                    });
                });
            } else if (table.querySelector('tr.link[id]') && header.cells.length === 3) {
                table.classList.add('univer-app-courses');
                wrapTable(table);
                rows.filter(function (r) { return r.matches('tr.link[id]'); }).forEach(function (row) {
                    var button = row.querySelector('.univer-app-course-open');
                    if (!button) {
                        button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-course-open';
                        button.addEventListener('click', function (event) {
                            event.preventDefault(); event.stopPropagation();
                            // The observed site attaches its navigation to jQuery's dblclick handler.
                            row.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true, view:window}));
                        });
                        row.cells[row.cells.length - 1].appendChild(button);
                    }
                    if (button.textContent !== labels().open) button.textContent = labels().open;
                });
            }
        });
    }
    function setHidden(node, hidden) { if (node.hidden !== hidden) node.hidden = hidden; }
    function slots(lessons) { return lessons.filter(function (l, i) { return !lessons.slice(0, i).some(function (o) { return o.from === l.from; }); }).length; }
    function minutesUntil(from, to) { return Math.max(1, Math.ceil((to - from) / 60000)); }
    function clock(minutes) { return String(Math.floor(minutes / 60)).padStart(2, '0') + ':' + String(minutes % 60).padStart(2, '0'); }
    // The option of the week right after the displayed one, found by its dates rather than by list order.
    function followingWeek(week) {
        if (!week.select) return -1;
        var found = -1, best = null;
        Array.from(week.select.options).forEach(function (option, i) {
            var dates = weekDates(option);
            if (dates.length >= 2 && dates[0] > week.last && (!best || dates[0] < best)) { best = dates[0]; found = i; }
        });
        return found;
    }
    // Statuses follow the clock (in progress, next, finished today); the summary card answers "what now".
    // Only adapter-made nodes change, and only when their text or state differs, so a steady page makes no mutations.
    function markUpcoming(record, now) {
        var week = shownWeek(now), at = now.getTime(), today = todayInWeek(now), L = agendaLabels();
        var current = week.today >= week.first && week.today <= week.last;
        // A later week only says what comes first in it: the current week may still hold classes.
        var ahead = week.first > week.today && week.monday - week.today < 7 * 86400000;
        record.lessons.forEach(function (lesson) {
            var date = dayStart(week.monday, lesson.day).getTime();
            lesson.from = date + lesson.start * 60000; lesson.to = date + lesson.end * 60000;
        });
        var live = record.lessons.filter(function (l) { return l.from <= at && at < l.to; });
        var upcoming = record.lessons.filter(function (l) { return l.from > at; }).sort(function (a, b) { return a.from - b.from; });
        var next = upcoming.length && (current || ahead) ? upcoming.filter(function (l) { return l.from === upcoming[0].from; }) : [];
        if (!current) live = [];
        record.lessons.forEach(function (lesson) {
            var state = live.indexOf(lesson) >= 0 ? 'now' : next.indexOf(lesson) >= 0 ? 'next' : current && lesson.day === today && lesson.to <= at ? 'done' : '';
            var text = state === 'now' ? labels().now + ' · ' + L.left(minutesUntil(at, lesson.to)) : state === 'next' ? labels().next + ' · ' + L.until(minutesUntil(at, lesson.from)) : state ? labels().done : '';
            if (lesson.state !== state) {
                lesson.state = state;
                ['now','next','done'].forEach(function (name) { lesson.card.classList.toggle('univer-app-lesson-' + name, state === name); });
            }
            var badge = lesson.card.querySelector('.univer-app-lesson-badge');
            if (!state) { if (badge) badge.remove(); return; }
            if (!badge) { badge = document.createElement('span'); badge.className = 'univer-app-lesson-badge'; lesson.card.querySelector('time').after(badge); }
            setText(badge, text);
        });
        var card = record.card;
        if (!current && !ahead) { setHidden(card.root, true); return; }
        setHidden(card.root, false);
        var todays = record.lessons.filter(function (l) { return l.day === today; }).sort(function (a, b) { return a.from - b.from; });
        var lastEnd = todays.length ? Math.max.apply(null, todays.map(function (l) { return l.end; })) : 0;
        var shown = live.length ? live : next, lead = shown[0], state, count = '', note = '', action = null, actionText = '', progress = -1;
        if (live.length) {
            state = 'now'; count = L.left(minutesUntil(at, lead.to));
            progress = Math.round(Math.min(1, (at - lead.from) / Math.max(1, lead.to - lead.from)) * 100);
            var then = next.filter(function (l) { return l.day === today; });
            note = then.length ? L.then + ': ' + then[0].subject + ', ' + clock(then[0].start) : L.last;
        } else if (!current) {
            // After the finished current week was seen today, the first class here is the real next one.
            var known = finishedWeek && finishedWeek.today === week.today.getTime() && finishedWeek.last < week.first.getTime() && week.first - finishedWeek.last <= 7 * 86400000;
            state = known ? finishedWeek.state : 'ahead'; count = next.length ? L.until(minutesUntil(at, lead.from)) : '';
        } else if (next.length && lead.day === today) {
            var ended = todays.filter(function (l) { return l.to <= at; });
            var gap = ended.length ? (lead.from - Math.max.apply(null, ended.map(function (l) { return l.to; }))) / 60000 : 0;
            state = !ended.length ? 'first' : gap > 30 ? 'gap' : 'pause'; count = L.until(minutesUntil(at, lead.from));
            note = ended.length ? L.remain(slots(todays.filter(function (l) { return l.from > at; })), clock(lastEnd)) : L.today(slots(todays), clock(todays[0].start) + '–' + clock(lastEnd));
        } else {
            state = todays.length ? 'over' : today >= 5 ? 'dayOff' : 'free';
            if (next.length) count = L.until(minutesUntil(at, lead.from));
            else finishedWeek = {today:week.today.getTime(), last:week.last.getTime(), state:state};
        }
        var title = state === 'now' ? L.now : state === 'ahead' ? L.shown : state === 'first' ? L.first : L[state];
        if (lead) {
            setText(card.subject, shown.map(function (l) { return l.subject; }).filter(function (v, i, a) { return a.indexOf(v) === i; }).join(' / '));
            var shift = Math.round((dayStart(new Date(lead.from)) - week.today) / 86400000);
            setText(card.meta, (state === 'now' || shift === 0 ? clock(lead.start) + '–' + clock(lead.end) : L.when(shift, lead.day, clock(lead.start))) + (lead.details ? ' · ' + lead.details : ''));
            if (state !== 'now' && lead.day !== today && record.selected !== -1 && record.selected !== lead.day) {
                actionText = L.showDay; action = function () { record.select(lead.day); };
            }
        } else {
            setText(card.subject, L.weekDone); setText(card.meta, '');
            var following = current ? followingWeek(week) : -1;
            if (following >= 0) {
                note = L.hint; actionText = L.nextWeek;
                // The site's own week selector loads the week through its change handler.
                action = function () { week.select.selectedIndex = following; week.select.dispatchEvent(new Event('change', {bubbles:true})); };
            }
        }
        if (card.root.dataset.state !== state) card.root.dataset.state = state;
        setText(card.title, title); setText(card.count, count); setText(card.note, note); setText(card.action, actionText);
        setHidden(card.meta, !card.meta.textContent); setHidden(card.count, !count); setHidden(card.note, !note); setHidden(card.action, !action);
        setHidden(card.progress, progress < 0);
        if (progress >= 0 && card.bar.style.width !== progress + '%') card.bar.style.width = progress + '%';
        if (progress >= 0 && card.progress.getAttribute('aria-valuenow') !== String(progress)) card.progress.setAttribute('aria-valuenow', String(progress));
        card.run = action;
    }
    function tickUpcoming() {
        upcomingTimer = setTimeout(function () { upcomingTimer = 0; if (enabled && schedules.size) { schedule(); tickUpcoming(); } }, 60500 - Date.now() % 60000);
    }
    function agendaCard() {
        var root = document.createElement('section'); root.className = 'univer-app-now'; root.setAttribute('aria-label', agendaLabels().region);
        var head = document.createElement('div'); head.className = 'univer-app-now-head';
        var title = document.createElement('span'); title.className = 'univer-app-now-title';
        var count = document.createElement('span'); count.className = 'univer-app-now-count';
        head.append(title, count);
        var subject = document.createElement('p'); subject.className = 'univer-app-now-subject';
        var meta = document.createElement('p'); meta.className = 'univer-app-now-meta';
        var progress = document.createElement('div'); progress.className = 'univer-app-now-progress';
        progress.setAttribute('role', 'progressbar'); progress.setAttribute('aria-valuemin', '0'); progress.setAttribute('aria-valuemax', '100'); progress.setAttribute('aria-label', agendaLabels().now);
        var bar = document.createElement('span'); progress.appendChild(bar);
        var foot = document.createElement('div'); foot.className = 'univer-app-now-foot';
        var note = document.createElement('span'); note.className = 'univer-app-now-note';
        var action = document.createElement('button'); action.type = 'button'; action.className = 'univer-app-now-action';
        foot.append(note, action);
        root.append(head, subject, meta, progress, foot);
        var card = {root:root, title:title, count:count, subject:subject, meta:meta, progress:progress, bar:bar, note:note, action:action, run:null};
        action.addEventListener('click', function () { if (card.run) card.run(); });
        return card;
    }
    function adaptSchedules() {
        schedules.forEach(function (record, table) { if (!table.isConnected) { record.view.remove(); schedules.delete(table); } });
        if (!schedules.size && upcomingTimer) { clearTimeout(upcomingTimer); upcomingTimer = 0; }
        if (!/^\/student\/myschedule(\/|$)/.test(location.pathname) || /^\/student\/myschedule\/print\//.test(location.pathname)) return;
        document.documentElement.classList.add('univer-app-schedule-page');
        document.querySelectorAll('table.schedule').forEach(function (table) {
            table.closest('.scrool,.scrool200,div.inner')?.classList.add('univer-app-schedule-content');
            var rows = ownRows(table), header = rows[0];
            if (!header || header.cells.length < 3 || header.cells[1].tagName !== 'TH') return;
            // The site draws times as rotated SVG text that disappears in a narrow column.
            // A readable copy is set before the signature so the agenda is not rebuilt for it.
            table.querySelectorAll('td.time').forEach(function (cell) {
                var marks = cleanText(cell).match(/\d{1,2}:\d{2}/g) || [], time = marks.length ? marks[0] + (marks.length > 1 ? '–' + marks[marks.length - 1] : '') : '';
                if (time && cell.dataset.univerTime !== time) cell.dataset.univerTime = time;
            });
            var today = todayInWeek(new Date());
            Array.from(header.cells).forEach(function (cell, i) { cell.classList.toggle('univer-app-today-col', today >= 0 && i === today + 1); });
            var signature = table.innerHTML + selectedLanguage;
            var previous = schedules.get(table);
            if (previous?.signature === signature) { markUpcoming(previous, new Date()); return; }
            var selected = previous ? previous.selected : -1;
            // A rowspan-aware logical grid keeps each class attached to its actual day/time.
            var grid = [];
            rows.forEach(function (row, r) {
                grid[r] = grid[r] || []; var column = 0;
                Array.from(row.cells).forEach(function (cell) {
                    while (grid[r][column]) column++;
                    for (var y = 0; y < cell.rowSpan; y++) {
                        grid[r+y] = grid[r+y] || [];
                        for (var x = 0; x < cell.colSpan; x++) grid[r+y][column+x] = {cell:cell, start:r, end:r+cell.rowSpan-1};
                    }
                    column += cell.colSpan;
                });
            });
            var view = document.createElement('section'); view.className = 'univer-app-schedule';
            var modes = document.createElement('div'); modes.className = 'univer-app-schedule-modes';
            var modesLabel = selectedLanguage === 'en' ? ['By day', 'Week grid', 'Schedule view'] : selectedLanguage === 'kz' ? ['Күн бойынша', 'Апта кестесі', 'Кесте көрінісі'] : ['По дням', 'Сетка недели', 'Вид расписания'];
            modes.setAttribute('role','group'); modes.setAttribute('aria-label', modesLabel[2]);
            var dayMode = document.createElement('button'), gridMode = document.createElement('button');
            dayMode.type = gridMode.type = 'button'; dayMode.textContent = modesLabel[0]; gridMode.textContent = modesLabel[1];
            modes.append(dayMode, gridMode); view.appendChild(modes);
            var tabs = document.createElement('div'); tabs.className = 'univer-app-days';
            tabs.setAttribute('aria-label', selectedLanguage === 'en' ? 'Day of week' : selectedLanguage === 'kz' ? 'Апта күні' : 'День недели');
            view.appendChild(tabs); var sections = [], buttons = [];
            var record = {view:view, signature:signature, selected:selected, mode:previous?.mode || 'days', lessons:[], card:agendaCard(), select:select};
            tabs.before(record.card.root);
            function select(index, event) {
                var previousIndex = record.selected;
                if (previousIndex !== index) sections.forEach(function (section) { window.__univerMotionUI?.stop(section); });
                record.selected = index;
                view.dataset.allDays = String(index === -1);
                sections.forEach(function (section, i) { section.hidden = index !== -1 && index !== i; });
                buttons.forEach(function (button, i) { button.setAttribute('aria-pressed', String(index === (i === sections.length ? -1 : i))); });
                if (event?.detail > 0 && previousIndex !== index) sections.forEach(function (section, i) {
                    if (!section.hidden) window.__univerMotionUI?.reveal(section, index >= previousIndex ? 'forward' : 'back', Math.min(i, 2) * 24);
                });
            }
            Array.from(header.cells).slice(1).forEach(function (day, dayIndex) {
                var button = document.createElement('button'); button.type = 'button';
                button.textContent = labels().days[dayIndex] || cleanText(day);
                button.setAttribute('aria-label', labels().days[dayIndex] || cleanText(day));
                button.addEventListener('click', function (event) { select(dayIndex, event); }); tabs.appendChild(button); buttons.push(button);
                var section = document.createElement('section'); section.className = 'univer-app-day';
                var title = document.createElement('h3'); title.textContent = labels().days[dayIndex] || cleanText(day);
                section.appendChild(title); sections.push(section); view.appendChild(section);
                var count = 0;
                grid.slice(1).forEach(function (line, i) {
                    var slot = line[dayIndex+1]; if (!slot || slot.start !== i+1 || !cleanText(slot.cell)) return;
                    var start = cleanText(grid[slot.start]?.[0]?.cell).match(/\d{1,2}:\d{2}/g) || [];
                    var end = cleanText(grid[Math.min(slot.end, grid.length-1)]?.[0]?.cell).match(/\d{1,2}:\d{2}/g) || [];
                    var timeText = start.length && end.length ? start[0] + '–' + end[end.length-1] : cleanText(line[0]?.cell);
                    var groups = Array.from(slot.cell.querySelectorAll('.groups>div'));
                    if (!groups.length) groups = [slot.cell];
                    groups.forEach(function (group) {
                        if (!cleanText(group) || group.hidden || getComputedStyle(group).display === 'none') return;
                        var card = document.createElement('article'); card.className = 'univer-app-lesson';
                        var time = document.createElement('time'); time.textContent = timeText; card.appendChild(time);
                        var parts = Array.from(group.querySelectorAll('p')), texts = []; if (!parts.length) parts = [group];
                        parts.forEach(function (part, index) {
                            var content = cleanText(part); if (!content) return;
                            // Add space between metadata spans; textContent alone joins faculty and room.
                            if (part.classList.contains('params')) content = Array.from(part.childNodes).map(cleanText).filter(Boolean).join(' / ');
                            var p = document.createElement('p'); p.textContent = content; texts.push(content);
                            if (index === 0) p.className = 'univer-app-subject'; card.appendChild(p);
                        });
                        group.querySelectorAll('a[href]').forEach(function (original) {
                            var link = document.createElement('a'); link.textContent = cleanText(original); link.href = original.href;
                            link.className = 'univer-app-page-link'; link.addEventListener('click', function (e) { e.preventDefault(); original.click(); }); card.appendChild(link);
                        });
                        section.appendChild(card); count++;
                        var from = (start[0] || '').split(':'), to = (end[end.length - 1] || '').split(':');
                        if (from.length === 2 && to.length === 2)
                            record.lessons.push({card:card, day:dayIndex, start:+from[0] * 60 + +from[1], end:+to[0] * 60 + +to[1], state:'', subject:texts[0] || '', details:texts.slice(1).join(' · ')});
                    });
                });
                var times = record.lessons.filter(function (l) { return l.day === dayIndex; });
                if (times.length) {
                    var range = document.createElement('span'); range.className = 'univer-app-day-range';
                    range.textContent = clock(Math.min.apply(null, times.map(function (l) { return l.start; }))) + '–' + clock(Math.max.apply(null, times.map(function (l) { return l.end; })));
                    title.appendChild(range);
                }
                var total = document.createElement('span'); total.className = 'univer-app-day-count'; total.textContent = String(count); total.setAttribute('aria-hidden','true'); button.appendChild(total);
                if (!count) { var empty = document.createElement('p'); empty.className = 'univer-app-no-classes'; empty.textContent = labels().empty; section.appendChild(empty); }
            });
            var all = document.createElement('button'); all.type = 'button'; all.textContent = labels().all;
            all.addEventListener('click', function (event) { select(-1, event); }); tabs.appendChild(all); buttons.push(all);
            // Today opens first and stays marked in "All days"; a rebuilt week keeps the user's choice.
            if (today >= 0 && sections[today]) {
                sections[today].classList.add('univer-app-today'); buttons[today].classList.add('univer-app-today');
                var badge = document.createElement('span'); badge.className = 'univer-app-today-badge'; badge.textContent = labels().today;
                sections[today].querySelector('h3').appendChild(badge);
                buttons[today].setAttribute('aria-label', buttons[today].getAttribute('aria-label') + ', ' + labels().today);
                if (!previous) selected = record.selected = today;
            }
            wrapTable(table, true);
            var scroll = table.parentElement; scroll.classList.add('univer-app-schedule-scroll');
            function mode(value, event) {
                var changed = record.mode !== value;
                if (changed) { window.__univerMotionUI?.stop(view); window.__univerMotionUI?.stop(scroll); }
                record.mode = value; view.dataset.mode = value;
                table.classList.toggle('univer-app-schedule-grid-visible', value === 'grid');
                scroll.classList.toggle('univer-app-schedule-grid-visible', value === 'grid');
                dayMode.setAttribute('aria-pressed',String(value === 'days')); gridMode.setAttribute('aria-pressed',String(value === 'grid'));
                if (event?.detail > 0 && changed) {
                    if (value === 'grid') window.__univerMotionUI?.reveal(scroll, 'forward');
                    else sections.filter(function (s) { return !s.hidden; }).forEach(function (s) { window.__univerMotionUI?.reveal(s, 'back'); });
                }
            }
            dayMode.addEventListener('click',function(event){mode('days',event);}); gridMode.addEventListener('click',function(event){mode('grid',event);});
            tabs.addEventListener('keydown',function(e){
                var at=buttons.indexOf(document.activeElement), next;
                if(at<0)return;
                if(e.key==='ArrowRight')next=(at+1)%buttons.length;
                else if(e.key==='ArrowLeft')next=(at+buttons.length-1)%buttons.length;
                else if(e.key==='Home')next=0; else if(e.key==='End')next=buttons.length-1; else return;
                e.preventDefault();buttons[next].focus();buttons[next].click();
            });
            if (previous) { window.__univerMotionUI?.stop(previous.view); previous.view.replaceWith(view); } else scroll.before(view);
            schedules.set(table, record); table.classList.add('univer-app-schedule-source'); select(selected); mode(record.mode); markUpcoming(record, new Date());
            // A tick at each minute boundary re-runs the adapter, so statuses and countdowns follow the clock.
            if (!upcomingTimer) tickUpcoming();
            // The marker alters innerHTML only through its table class, so the grid signature stays stable.
        });
    }
    function dialogViewport() {
        if (!enabled) return;
        var view = window.visualViewport, height = view ? view.height : window.innerHeight;
        var root = document.documentElement;
        var values = {'--univer-dialog-top': ((view ? view.offsetTop : 0) + 12) + 'px',
            '--univer-dialog-height': Math.max(120, height - 24) + 'px',
            '--univer-frame-height': Math.max(100, height - 100) + 'px'};
        Object.keys(values).forEach(function (key) { if (root.style.getPropertyValue(key) !== values[key]) root.style.setProperty(key, values[key]); });
    }
    // These classes describe observed table structures; original controls and handlers stay in place.
    function adaptRecords() {
        document.querySelectorAll('#l4 .ct,#l4 .ts,table.xp td').forEach(function (cell) {
            Array.from(cell.childNodes).forEach(function (node) {
                if (node.nodeType === 3 && /^[\s\\/|]+$/.test(node.data) && /[\\/|]/.test(node.data)) {
                    if (!punctuation.has(node)) punctuation.set(node, node.data); node.data = '';
                } else if (node.nodeType === 3 && node.previousSibling?.nodeType === 1 && node.previousSibling.tagName === 'A' && /^\s*[\\/]\s*\S/.test(node.data)) {
                    if (!punctuation.has(node)) punctuation.set(node, node.data); node.data = node.data.replace(/^\s*[\\/]\s*/, ' / ');
                }
            });
        });
        document.querySelectorAll('#tools li').forEach(function (li) {
            if (Array.from(li.children).filter(function (n) { return n.tagName === 'A'; }).length < 2) return;
            Array.from(li.childNodes).forEach(function (node) {
                if (node.nodeType === 3 && /^[\s()]+$/.test(node.data) && /[()]/.test(node.data)) {
                    if (!punctuation.has(node)) punctuation.set(node, node.data);
                    node.data = '';
                }
            });
        });
        var path = location.pathname;
        var records = /^\/(student|ssc|my)(\/|$)/i.test(path);
        var print = /^\/student\/(iup|transcript|recbook)(\/|$)/i.test(path) ||
            /^\/student\/(interactive\/syllabus|myschedule)\/print\//i.test(path);
        document.querySelectorAll('table.mt').forEach(function (table) {
            if (table.closest('#tools,#login_frm,#modal_auth_frm,#modal_agreement,.univer-app-info')) return;
            if (Array.from(table.rows).some(function (r) { return Array.from(r.cells).some(function (c) { return c.matches('.lt,.rt'); }); })) {
                table.classList.add('univer-app-study-panel');
                ownRows(table).forEach(function (row) { row.classList.toggle('univer-app-study-empty', !cleanText(row) && !row.querySelector('input,select,textarea,button,a,img,iframe')); });
            }
        });
        if (!records) return;
        document.querySelectorAll('#l4 ul,#l4 ol').forEach(function (list) {
            if (!list.closest('#tools,#login_frm,#modal_agreement')) list.classList.add('univer-app-list');
        });
        document.querySelectorAll('table.xp').forEach(function (table) { table.classList.add('univer-app-nav-table'); });
        if (/^\/student\/academcalendar\/?$/.test(path)) document.querySelectorAll('#l4 table.xp').forEach(function (table) {
            var links = Array.from(table.querySelectorAll('a[href^="#"]'));
            if (!links.length) return;
            var nav = document.createElement('nav'); nav.className = 'univer-app-semester-nav';
            nav.setAttribute('aria-label', selectedLanguage === 'en' ? 'Semesters' : selectedLanguage === 'kz' ? 'Семестрлер' : 'Семестры'); table.before(nav);
            links.forEach(function (link) { moveUsability(link, nav); });
            table.classList.add('univer-app-calendar-meta');
            ownRows(table).forEach(function (row) { Array.from(row.cells).forEach(function (cell) { if (!cleanText(cell) && !cell.querySelector('input,select,button,img')) cell.classList.add('univer-app-empty-nav-cell'); }); });
        });
        if (print) document.documentElement.classList.add('univer-app-document');
        Array.from(document.querySelectorAll('table')).forEach(function (table) {
            if (table.closest('#tools,#login_frm,#modal_auth_frm,#modal_agreement,.univer-app-info') ||
                table.matches('.mt,.tool,.cols,.wrappertable,.univer-app-shell,.univer-app-news,.univer-app-courses,.univer-app-materials,.schedule')) return;
            var rows = Array.from(table.rows), own = rows.filter(function (r) { return r.closest('table') === table; });
            if (!own.length || own.some(function (r) { return Array.from(r.cells).some(function (c) { return c.matches('.left,.right,.center'); }); })) return;
            var header = own.find(function (r) { return r.cells.length > 1 && Array.from(r.cells).every(function (c) { return c.tagName === 'TH'; }); });
            var personal = /^\/student\/(bachelor|profile|advicer|mydata)(\/|$)/i.test(path) || path === '/student' || path === '/student/';
            var pairs = !header && own.some(function (r) { return /:|фио|ф\.и\.о|факульт|кафедр|телефон|e.?mail/i.test(r.cells[0]?.textContent || ''); }) && own.every(function (r) {
                var cells = Array.from(r.cells);
                return (cells.length === 2 || cells.length === 4) && cells.every(function (c) { return c.colSpan === 1 && c.rowSpan === 1; }) &&
                    cells.filter(function (_, i) { return i % 2 === 0; }).every(function (c) {
                        return !c.textContent.trim() || /:\s*$/.test(c.textContent) || /^(фио|ф\.и\.о\.?|факультет|кафедра|(?:рабочий )?телефон|e.?mail|курс|специальность|адрес)$/i.test(c.textContent.trim());
                    });
            });
            if ((personal || print) && pairs) { table.classList.add('univer-app-pairs'); return; }
            if (personal && (table.querySelector('input[type=file]') || (!header && table.querySelector('input,select,textarea')))) {
                table.classList.add('univer-app-form-table'); return;
            }
            if (print) {
                if (/^(ф\.и\.о\.?|фио|аты.?жөні|full name|student name|name)$/i.test(own[0].cells[0]?.textContent.trim() || '')) {
                    table.classList.add('univer-app-pairs'); return;
                }
                // The outer print sheet and syllabus text blocks are layout tables. Only real grids scroll.
                if (own.every(function (r) { return r.cells.length <= 2; }) ||
                    (table.matches('table.students') && own.every(function (r) { return r.cells.length <= 2; }))) {
                    table.classList.add('univer-app-document-layout'); return;
                }
                wrapTable(table, true); return;
            }
            if (!table.matches('.inner,.inner2,#scheduleList')) return;
            // A one-cell result such as "no data" reads as a quiet empty state, not as a record.
            if (own.length === 1 && own[0].cells.length === 1 && !table.querySelector('input,select,textarea,button,a,img,iframe,table') &&
                cleanText(own[0]).length > 0 && cleanText(own[0]).length < 120) { table.classList.add('univer-app-empty-note'); return; }
            if (/^\/student\/contract\/list(\/|$)/.test(path) && table.querySelector('th>.full-width>div')) {
                table.classList.add('univer-app-div-records','univer-app-data'); wrapTable(table); return;
            }
            if (/\/attendance\//i.test(path) || table.querySelector('td[rowspan],th[rowspan]')) {
                table.classList.add('univer-app-matrix'); wrapTable(table); return;
            }
            // Exam and Midterm use a separate table for column headings and the AJAX result rows.
            if (!header && /\/my(exam|midterm)\/schedule\/|^\/my\/messages\//i.test(path)) {
                var previous = Array.from(document.querySelectorAll('table.inner')).filter(function (t) {
                    return !!(t.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING);
                }).pop();
                header = previous && Array.from(previous.rows).find(function (r) {
                    return r.cells.length > 1 && Array.from(r.cells).every(function (c) { return c.tagName === 'TH'; });
                });
                if (header) header.classList.add('univer-app-card-header');
            }
            if (!header || Array.from(header.cells).some(function (c) { return c.colSpan !== 1 || c.rowSpan !== 1; })) return;
            if (own.length === 1 && own[0].cells.length === 1 && !table.querySelector('input,select,textarea,button,a') && /^(данные не найдены|обращени[яе] не найден[ыо]|нет данных|не заполнено расписание midterm|файлы не загружены)$/i.test(cleanText(own[0])))
                table.closest('.scrool,.scrool200')?.classList.add('univer-app-empty-result');
            if (own.some(function (r) { return r !== header && r.cells.length > 1 && Array.from(r.cells).some(function (c) { return c.colSpan > 1 && c.colSpan < header.cells.length; }); })) {
                table.classList.add('univer-app-matrix'); wrapTable(table); return;
            }
            table.classList.add('univer-app-cards'); header.classList.add('univer-app-card-header');
            // Keep the original clickable sort headings available above the statement cards.
            if (table.matches('.table_sort')) header.classList.add('univer-app-sort-head');
            own.forEach(function (r) {
                if (r === header) return;
                var cells = Array.from(r.cells);
                if (cells.length === 1 && cells[0].colSpan > 1) { r.classList.add('univer-app-card-section'); return; }
                r.classList.add('univer-app-card-row');
                // A short single link in the last column is the row's action ("Подать заявку", "Открыть").
                var last = cells[cells.length - 1], action = last?.children.length === 1 && last.firstElementChild.tagName === 'A' ? last.firstElementChild : null;
                if (action && cleanText(last) === cleanText(action) && cleanText(action).length <= 28) action.classList.add('univer-app-action-link');
                cells.forEach(function (c, i) {
                    if (c.tagName === 'TH') return;
                    var label = header.cells[i]?.textContent.trim();
                    if (label && c.dataset.univerField !== label) c.dataset.univerField = label;
                    if (/дисциплин|название|описание|услуга|содержание|преподаватель/i.test(label || '') || c.textContent.length > 100)
                        c.classList.add('univer-app-long-field');
                });
            });
        });
    }
    function wrapTable(table, nested) {
        if (table.parentElement.matches('tr,tbody,thead,tfoot,table,.univer-app-table-scroll') ||
            (!nested && table.closest('.univer-app-table-scroll'))) return;
        var box = document.createElement('div'); box.className = 'univer-app-table-scroll'; box.tabIndex = 0;
        box.setAttribute('role', 'region');
        box.setAttribute('aria-label', selectedLanguage === 'en' ? 'Table, scroll horizontally' :
            selectedLanguage === 'kz' ? 'Кесте, көлденең айналдыру' : 'Таблица, прокрутка по горизонтали');
        table.parentNode.insertBefore(box, table); box.appendChild(table);
    }
    function scrollHints() {
        // The arrow shows which way the table moves.
        var caption = selectedLanguage === 'en' ? '↔ Swipe the table sideways to see all columns' :
            selectedLanguage === 'kz' ? '↔ Барлық бағанды көру үшін кестені көлденең жылжытыңыз' :
            '↔ Сдвиньте таблицу вбок, чтобы увидеть все столбцы';
        document.querySelectorAll('.univer-app-table-scroll').forEach(function (box) {
            var hint = box.__univerHint;
            var wide = box.getClientRects().length && box.scrollWidth > box.clientWidth + 2;
            if (!hint && wide) {
                hint = document.createElement('p'); hint.className = 'univer-app-scroll-hint';
                box.before(hint); box.__univerHint = hint;
            }
            if (hint) { if (hint.textContent !== caption) hint.textContent = caption; hint.hidden = !wide; }
            box.setAttribute('aria-label', caption);
        });
        document.querySelectorAll('.univer-app-scroll-hint').forEach(function (hint) {
            if (!hint.nextElementSibling?.classList.contains('univer-app-table-scroll')) hint.remove();
        });
    }
    function adaptFrames() {
        document.querySelectorAll('iframe,frame').forEach(function (frame) {
            if (!frameListeners.has(frame)) { frameListeners.add(frame); frame.addEventListener('load', schedule); }
            try {
                var win = frame.contentWindow;
                if (win && win.location.origin === location.origin && win.document.readyState !== 'loading') {
                    if (!win.__univerMobileV1) win.eval('(' + initializeMobile.toString() + ')();');
                    win.__univerMobileV1.setEnabled(enabled, selectedLanguage);
                    win.__univerPresentationV1?.release(); childAdapters.add(win);
                }
            } catch (_) { /* Never alter external frames. */ }
        });
    }
    function moveUsability(node, destination) {
        var home = document.createComment('univer-original-content');
        node.before(home); destination.appendChild(node); usabilityMoves.push({node:node, home:home});
    }
    function returnToMenuVisibility() {
        var button = document.querySelector('.univer-app-menu-return'), menu = document.querySelector('.univer-app-menu');
        if (!button) return;
        var hidden = !enabled || !menu || menu.getBoundingClientRect().bottom >= 0 ||
            !!document.querySelector('#simplemodal-container')?.getClientRects().length || (window.visualViewport?.height || innerHeight) < 300;
        // Scroll fires many times per gesture. An unchanged boolean attribute still wakes the DOM observer.
        if (button.hidden !== hidden) button.hidden = hidden;
    }
    function filterCatalog() {
        var input = document.querySelector('.univer-app-catalog-search');
        var query = (input?.value || '').trim().toLocaleLowerCase().replace(/ё/g,'е');
        document.querySelectorAll('.univer-app-catalog').forEach(function (group) {
            var count = 0;
            group.querySelectorAll('tr.file').forEach(function (row) {
                if (!catalogHidden.has(row)) catalogHidden.set(row, row.hidden);
                var title = Array.from(row.cells[2]?.childNodes || []).filter(function(n){return !n.classList?.contains('univer-app-catalog-download');}).map(cleanText).join(' ');
                var matches = !catalogHidden.get(row) && (!query || title.toLocaleLowerCase().replace(/ё/g,'е').includes(query));
                if (row.hidden === matches) row.hidden = !matches;
                if (matches) count++;
            });
            if (group.hidden !== (!!query && !count)) group.hidden = !!query && !count;
            if (query && count && !group.open) group.open = true;
        });
        var empty = document.querySelector('.univer-app-catalog-empty');
        if (empty) { empty.textContent = selectedLanguage==='en'?'No documents found':selectedLanguage==='kz'?'Құжаттар табылмады':'Документы не найдены'; empty.hidden = !query || !!document.querySelector('.univer-app-catalog:not([hidden])'); }
    }
    function adaptUsability() {
        // The two GPA counters repeat above every student section; keep their original nodes under Account instead.
        var accountBody=document.querySelector('.univer-app-account>div');
        if(accountBody && !/^\/student\/bachelor\/?$/.test(location.pathname)) {
            document.querySelectorAll('#l4 .ct.message').forEach(function(cell){
                var list=cell.querySelector(':scope>ul'), items=Array.from((list||cell).children);
                if(items.length!==2||!items.every(function(li){return li.tagName==='LI'&&/G[PР][AА]/i.test(cleanText(li));})||cell.querySelector('input,select,button,form'))return;
                var home=cell.closest('table.mt');
                if(list)moveUsability(list,accountBody);
                else {
                    var destination=document.createElement('ul');destination.className='univer-app-academic-meta';accountBody.appendChild(destination);
                    items.forEach(function(item){moveUsability(item,destination);});
                }
                if(home && ownRows(home).every(function(r){return !cleanText(r)&&!r.querySelector('input,select,textarea,button,a,img,iframe');}))home.classList.add('univer-app-empty-panel');
            });
        }
        if(/^\/student\/myschedule(\/|$)/.test(location.pathname)) {
            var headingLink=Array.from(document.querySelectorAll('#l4 .ct>a[href]')).find(function(a){return /^\/student\/myschedule\/\d+\/\d+\/?$/.test(new URL(a.href).pathname);});
            if(headingLink) {
                headingLink.closest('.ct').classList.add('univer-app-schedule-title');
                document.querySelectorAll('#l4 .ct').forEach(function(cell){
                    if(cell!==headingLink.closest('.ct') && !cell.querySelector('a,input,select,textarea,button,form') && cleanText(cell).startsWith(cleanText(headingLink)))cell.classList.add('univer-app-secondary-heading');
                });
            }
            document.querySelectorAll('#l4 a[href]').forEach(function(a){if(/^\/student\/myschedule\/print\/\d+\/\d+\/?$/.test(new URL(a.href).pathname))a.classList.add('univer-app-print-action');});
        }
        var footer = document.querySelector('#l5 td.center');
        if (footer) document.querySelectorAll('.univer-app-info').forEach(function (box) {
            if (!box.closest('#l5')) moveUsability(box, footer);
        });
        var menu = document.querySelector('.univer-app-menu'), path = location.pathname;
        if (menu) {
            if (!document.querySelector('.univer-app-menu-return')) {
                var button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-menu-return';
                button.addEventListener('click', function () { menu.open = true; menu.scrollIntoView({block:'start'}); menu.querySelector('summary').focus({preventScroll:true}); });
                document.body.appendChild(button);
            }
            var back = document.querySelector('.univer-app-menu-return');
            // The arrow says the button takes the reader back up to the sections menu.
            var caption = selectedLanguage === 'en' ? '↑ Sections' : selectedLanguage === 'kz' ? '↑ Бөлімдер' : '↑ Разделы';
            if (back.textContent !== caption) back.textContent = caption;
            returnToMenuVisibility();
            menu.querySelectorAll('#tools a[href]').forEach(function (a) {
                if (new URL(a.href).origin !== location.origin || new URL(a.href).pathname.replace(/\/+$/, '') !== path.replace(/\/+$/, '')) return;
                if (!pageMarkers.has(a)) pageMarkers.set(a, a.getAttribute('aria-current'));
                a.setAttribute('aria-current','page');
            });
        }
        // Home tiles, pins and the useful links inside "All sections" (shortcuts.js) follow the menu's current links.
        window.__univerShortcutsV1?.apply(selectedLanguage);
        document.querySelectorAll('#l4 form table.cols').forEach(function (table) { table.classList.add('univer-app-form-table'); });
        // Single-cell "no data" rows inside real tables become a quiet empty state.
        document.querySelectorAll('#l4 table tr').forEach(function (row) {
            if (row.cells.length !== 1 || row.classList.contains('univer-app-empty-row') || row.querySelector('input,select,textarea,button,a,img')) return;
            if (/^(нет данных|данные не найдены|файлы не загружены|в журнале нет данных|обращени[яе] не найден[ыо]|нет расписания|no data( found)?|data not found|деректер (жоқ|табылмады))\.?$/i.test(cleanText(row)))
                row.classList.add('univer-app-empty-row');
        });
        if(/^\/student\/mydata\/edit\/?$/.test(path))document.querySelectorAll('#l4 .univer-app-form-table').forEach(function(table){
            ownRows(table).forEach(function(row){
                var cells=Array.from(row.cells);
                if(!cleanText(row)&&!row.querySelector('input,select,textarea,button,a,img,iframe'))row.classList.add('univer-app-form-spacer');
                if(cells.length===3&&cells[0].tagName==='TH'&&cells[2].tagName==='TH'&&!cleanText(cells[1])&&!row.querySelector('input,select,textarea,button,a')&&/^(данные|data|деректер)$/i.test(cleanText(cells[0]))&&/^(фото|photo|сурет)$/i.test(cleanText(cells[2])))row.classList.add('univer-app-form-spacer');
            });
        });
        document.querySelectorAll('#l4 table.cols').forEach(function (table) {
            if (!table.closest('#login_frm,#modal_auth_frm,#modal_agreement') && !table.querySelector('form') && !table.closest('form'))
                table.classList.add('univer-app-content-columns');
        });
        if (/^\/student\/(myexam\/schedule|discussion\/index)(\/|$)/.test(path)) document.querySelectorAll('#l4 div').forEach(function (block) {
            if (block.closest('th,.univer-app-data') || getComputedStyle(block).cssFloat === 'none') return;
            block.classList.add('univer-app-media-column');
            if (!cleanText(block) && !block.querySelector('input,select,button,a,img,iframe')) block.classList.add('univer-app-study-empty');
        });
        document.querySelectorAll('#l4 iframe[src]').forEach(function(frame){
            var host;try{host=new URL(frame.src).hostname;}catch(_){return;}
            if(!/^(www\.)?youtube(-nocookie)?\.com$/.test(host))return;
            frame.classList.add('univer-app-video');
            if(!frame.__univerVideoLink?.isConnected) {
                var link=document.createElement('a');link.className='univer-app-video-link';link.target='_blank';link.rel='noopener noreferrer';
                var source=new URL(frame.src),video=source.pathname.match(/^\/embed\/([A-Za-z0-9_-]+)\/?$/);
                link.href=video?'https://www.youtube.com/watch?v='+encodeURIComponent(video[1]):source.href;
                frame.after(link);frame.__univerVideoLink=link;
            }
            var videoLabel=selectedLanguage==='en'?'Watch video instructions':selectedLanguage==='kz'?'Бейненұсқаулықты көру':'Смотреть видеоинструкцию';
            if(frame.__univerVideoLink.textContent!==videoLabel)frame.__univerVideoLink.textContent=videoLabel;
            var parent=frame.parentElement;
            if(parent.tagName==='DIV' && parent.style.cssFloat)Array.from(parent.parentElement.children).forEach(function(node){
                if(node.tagName==='DIV' && node.style.cssFloat)node.classList.add('univer-app-media-column');
            });
        });
        var copyright=document.getElementById('copyright');
        if(copyright) {
            var walker=document.createTreeWalker(copyright,NodeFilter.SHOW_TEXT),piece;
            while((piece=walker.nextNode()))if(/^\s*IP:\s*[\d.:]+\s*$/.test(piece.data)){if(!punctuation.has(piece))punctuation.set(piece,piece.data);piece.data='';}
        }
        // Real general document catalog: separate TD headings, tr.file.link[id], /my/docsget/{id}/ via dblclick.
        if (/^\/my\/docs\/?$/.test(path)) {
            document.querySelectorAll('#l4 table.mt').forEach(function (table) {
                if (table.closest('.univer-app-catalog') || !table.querySelector('.scrool200 tr.file.link[id]')) return;
                var rows = ownRows(table); if (rows.length !== 3 || rows[0].cells.length !== 1) return;
                var details = document.createElement('details'); details.className = 'univer-app-catalog';
                var summary = document.createElement('summary'); summary.textContent = cleanText(rows[0]); details.appendChild(summary);
                table.before(details); moveUsability(table,details); rows[0].classList.add('univer-app-catalog-title');
                table.querySelectorAll('table.inner').forEach(function (inner) {
                    if (inner.closest('.scrool200')) return;
                    if (ownRows(inner).length === 1) inner.classList.add('univer-app-catalog-head');
                });
            });
            document.querySelectorAll('.univer-app-catalog tr.file.link[id]').forEach(function (row) {
                if (row.cells.length !== 6) return;
                var words = selectedLanguage === 'en' ? ['Date','Size','Downloads'] : selectedLanguage === 'kz' ? ['Күні','Көлемі','Жүктеулер'] : ['Дата','Размер','Скачали'];
                [3,4,5].forEach(function(i){if(row.cells[i].dataset.univerField!==words[i-3])row.cells[i].dataset.univerField=words[i-3];});
                var button = row.querySelector('.univer-app-catalog-download');
                if (!button) {
                    button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-catalog-download';
                    button.addEventListener('click',function(e){e.preventDefault();e.stopPropagation();row.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,cancelable:true}));});
                    row.cells[2].appendChild(button);
                }
                if (button.textContent !== labels().files) button.textContent = labels().files;
            });
            var group = document.querySelector('.univer-app-catalog');
            if (group && !document.querySelector('.univer-app-catalog-search')) {
                var input = document.createElement('input'); input.type='search'; input.className='univer-app-catalog-search';
                input.addEventListener('input',filterCatalog); group.before(input);
                var empty = document.createElement('p'); empty.className='univer-app-catalog-empty'; empty.setAttribute('role','status'); empty.hidden=true; input.after(empty);
            }
            var search = document.querySelector('.univer-app-catalog-search');
            if (search) { var title=selectedLanguage==='en'?'Find a document…':selectedLanguage==='kz'?'Құжатты іздеу…':'Найти документ…'; search.placeholder=title; search.setAttribute('aria-label',title); }
            filterCatalog();
        }
        if (/^\/my\/faq\/?$/.test(path)) { document.querySelectorAll('#l4 td.tt>table.inner').forEach(function (table) {
            if (table.closest('.univer-app-faq') || ownRows(table).length !== 2) return;
            var answer = table.closest('tr'), question = answer?.previousElementSibling?.querySelector('.ct');
            if (!question || !cleanText(question)) return;
            var box=document.createElement('details'); box.className='univer-app-faq';
            var summary=document.createElement('summary'); summary.textContent=cleanText(question); box.appendChild(summary);
            table.before(box); moveUsability(table,box); question.closest('tr').classList.add('univer-app-catalog-title');
            // The real FAQ keeps answer rows display:none until the old question handler runs.
            // Our summary lives in that row, so expose it only while mobile mode is enabled.
            answer.classList.add('univer-app-faq-row');
            var panel=question.closest('table.mt');if(panel)panel.classList.add('univer-app-study-panel');
        });
        document.querySelectorAll('#l4 table.mt:has(.univer-app-faq)').forEach(function(panel){
            ownRows(panel).forEach(function(row){
                if(!cleanText(row)&&!row.querySelector('input,select,textarea,button,a,img,iframe'))row.classList.add('univer-app-study-empty');
            });
        }); }
        var gpa = document.querySelector('#gpa-table');
        if (gpa && /^\/my\/calculatorgpa\/?$/.test(path)) {
            var head=ownRows(gpa)[0];
            gpa.classList.add('univer-app-cards'); head.classList.add('univer-app-card-header');
            ownRows(gpa).slice(1).forEach(function(r){
                if(r.cells.length!==3)return; r.classList.add('univer-app-card-row'); r.cells[0].classList.add('univer-app-long-field');
                Array.from(r.cells).forEach(function(c,i){var label=cleanText(head.cells[i]);if(c.dataset.univerField!==label)c.dataset.univerField=label;});
            });
            var outer=gpa.parentElement.closest('table'); if(outer) outer.classList.add('univer-app-gpa-layout');
        }
        document.querySelectorAll('table.table_sort.univer-app-cards').forEach(function(table){
            var caption=selectedLanguage==='en'?'Sort by column':selectedLanguage==='kz'?'Баған бойынша сұрыптау':'Сортировка по столбцу';
            if(table.__univerSortButton?.isConnected){if(table.__univerSortButton.textContent!==caption)table.__univerSortButton.textContent=caption;return;}
            var button=document.createElement('button'); button.type='button'; button.className='univer-app-sort-toggle'; button.setAttribute('aria-expanded','false');
            button.textContent=caption;
            button.addEventListener('click',function(event){var before=table.getBoundingClientRect().height;window.__univerMotionUI?.stop(table);var closed=table.classList.toggle('univer-app-sort-collapsed');button.setAttribute('aria-expanded',String(!closed));if(event.detail>0)window.__univerMotionUI?.resize(table,before,closed?180:220);});
            table.classList.add('univer-app-sort-collapsed');table.before(button);table.__univerSortButton=button;
        });
        // Not every observed .inner grid has TH headings (testing and split AJAX results).
        document.querySelectorAll('#l4 table.inner').forEach(function(table){
            if(table.closest('#tools,.univer-app-info,.univer-app-catalog,.univer-app-table-scroll') ||
                table.matches('.univer-app-cards,.univer-app-pairs,.univer-app-form-table,.univer-app-news,.univer-app-courses,.univer-app-materials'))return;
            var rows=ownRows(table);
            if(rows.some(function(r){return Array.from(r.cells).some(function(c){return c.matches('.left,.right,.center');});}))return;
            if(rows.some(function(r){return r.cells.length>1 || r.cells[0]?.colSpan>1;}))wrapTable(table);
        });
    }
    function adapt() {
        queued = false;
        if (!enabled || !document.body) return;
        // Observe website updates, not the adapter's own class/DOM changes.
        observer?.disconnect();
        try {
        var shell = document.querySelector('body>table.wrappertable') ||
            Array.from(document.querySelectorAll('body>table')).find(function (t) { return !!t.querySelector('#l1,#l4'); });
        if (shell && !shell.classList.contains('univer-app-shell')) shell.classList.add('univer-app-shell');
        adaptHeader();
        adaptStudyPages();
        window.__univerStudyV1?.apply(selectedLanguage);
        window.__univerAttendanceV1?.apply(selectedLanguage);
        adaptSchedules();
        adaptRecords();
        document.querySelectorAll('table.inner').forEach(function (table) {
            if (!table.classList.contains('univer-app-news') && table.querySelector('tr.news') &&
                Array.from(table.rows).some(function (row) { return row.cells.length === 2 && row.cells[0].tagName === 'TH' &&
                    /^(дата|date|күні)$/i.test(row.cells[0].textContent.trim()) && /новость|news|жаңалық/i.test(row.cells[1].textContent); }))
                table.classList.add('univer-app-news');
        });
        // Univer also uses .inner for real data tables, not only its decorative layout.
        document.querySelectorAll('table.inner:not(.univer-app-news)').forEach(function (table) {
            if (table.classList.contains('univer-app-data') || table.closest('#tools,#login_frm,#modal_auth_frm')) return;
            var header = Array.from(table.rows).find(function (row) { return row.cells.length >= 2 &&
                Array.from(row.cells).every(function (cell) { return cell.tagName === 'TH'; }); });
            if (!header) return;
            table.classList.add('univer-app-data');
            Array.from(table.rows).forEach(function (row) { Array.from(row.cells).forEach(function (cell, i) {
                if (header.cells[i]?.textContent.trim()) cell.classList.add('univer-app-data-field');
            }); });
        });
        // Page-specific layouts read the record and data classes set above.
        window.__univerPagesV1?.apply(selectedLanguage);
        window.__univerTasksV1?.apply(selectedLanguage);
        dialogViewport();
        document.querySelectorAll('.univer-app-modal-toolbar').forEach(function (bar) {
            if (!bar.__univerContainer?.isConnected) { bar.remove(); return; }
            var hidden = !bar.__univerContainer.getClientRects().length;
            if (bar.hidden !== hidden) bar.hidden = hidden;
        });
        document.querySelectorAll('#simplemodal-container .simplemodal-close,.univer-app-modal-toolbar .simplemodal-close').forEach(function (close) {
            if (!close.dataset.univerClose) {
                close.dataset.univerClose = '1'; close.dataset.univerOldTitle = close.title;
                close.dataset.univerOldLabel = close.getAttribute('aria-label') || '';
                close.dataset.univerOldRole = close.getAttribute('role') || '';
                close.dataset.univerOldTab = close.getAttribute('tabindex') || '';
                close.setAttribute('role', 'button'); close.tabIndex = 0;
                close.__univerKeydown = function (event) { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); close.click(); } };
                close.addEventListener('keydown', close.__univerKeydown);
            }
            if (!close.closest('.univer-app-modal-toolbar')) {
                var container = close.closest('#simplemodal-container');
                var home = document.createComment('univer-original-close'); close.parentNode.insertBefore(home, close);
                var toolbar = document.createElement('div'); toolbar.className = 'univer-app-modal-toolbar'; toolbar.__univerCloseHome = home;
                toolbar.__univerContainer = container;
                toolbar.style.zIndex = String((parseInt(getComputedStyle(container).zIndex, 10) || 1002) + 1);
                toolbar.hidden = !container.getClientRects().length;
                document.body.appendChild(toolbar); toolbar.appendChild(close);
            }
            if (close.title !== closeLabel) close.title = closeLabel;
            if (close.getAttribute('aria-label') !== closeLabel) close.setAttribute('aria-label', closeLabel);
        });
        // Only the observed static help box and app/resource links are folded, never account notices/forms.
        Array.from(document.querySelectorAll('table.mt')).forEach(function (table) {
            var text = table.textContent;
            if (!table.closest('form,#simplemodal-container,#attentionSt,#modal_agreement,#modal_auth_frm') && !table.querySelector('form,input') && text.includes('it@kaznu.kz') &&
                text.includes('wifi-support@kaznu.kz') && text.includes('help@kaznu.kz')) foldInfo(table, 'contacts');
        });
        Array.from(document.querySelectorAll('.links')).forEach(function (links) {
            if (links.querySelectorAll('a').length >= 8 && links.querySelector('a[href*="apps.apple.com"]') &&
                links.querySelector('a[href*="play.google.com"]')) foldInfo(links, 'links');
        });
        document.querySelectorAll('.univer-app-info').forEach(function (box) {
            var summary = box.querySelector('summary'), title = infoTitles[box.dataset.univerInfo];
            if (summary && summary.textContent !== title) summary.textContent = title;
        });
        var tools = document.getElementById('tools');
        if (tools && !tools.closest('.univer-app-menu')) {
            var details = document.createElement('details'); details.className = 'univer-app-menu';
            var summary = document.createElement('summary'); summary.textContent = menuTitle;
            details.appendChild(summary);
            menuHome = document.createComment('univer-original-menu');
            var parent = tools.parentNode;
            parent.insertBefore(menuHome, tools);
            // The observed support block can be in a separate Tr1 row above the main content.
            var help = document.querySelector('.univer-app-info[data-univer-info="contacts"]');
            if (help && !help.closest('form,#simplemodal-container')) help.parentNode.insertBefore(details, help);
            else parent.insertBefore(details, parent.firstChild);
            details.appendChild(tools);
            var search = document.createElement('input'); search.type = 'search';
            search.className = 'univer-app-menu-search'; search.autocomplete = 'off';
            search.addEventListener('input', filterMenu); details.insertBefore(search, tools);
            var close = document.createElement('button'); close.type = 'button'; close.className = 'univer-app-menu-close';
            close.addEventListener('click', function () { toggleMenu(false); }); details.insertBefore(close, search);
            details.addEventListener('toggle', syncMenuButton);
            var empty = document.createElement('p'); empty.className = 'univer-app-empty'; empty.hidden = true;
            empty.setAttribute('role', 'status'); details.appendChild(empty);
        }
        var summary = document.querySelector('.univer-app-menu>summary');
        if (summary && summary.textContent !== menuTitle) summary.textContent = menuTitle;
        var search = document.querySelector('.univer-app-menu-search');
        if (search && search.placeholder !== searchLabel) { search.placeholder = searchLabel; search.setAttribute('aria-label', searchLabel); }
        var menuClose = document.querySelector('.univer-app-menu-close');
        if (menuClose && menuClose.getAttribute('aria-label') !== closeLabel) { menuClose.setAttribute('aria-label', closeLabel); menuClose.title = closeLabel; }
        syncMenuButton();
        if (tools) {
            tools.querySelectorAll('table.tool').forEach(function (table) {
                if (table.closest('.univer-app-category')) return;
                var footer = table.querySelector('.bottom-center');
                if (!footer || !footer.textContent.trim()) return;
                var box = document.createElement('details'); box.className = 'univer-app-category'; box.open = true;
                var title = document.createElement('summary'); title.textContent = footer.textContent.trim(); box.appendChild(title);
                var home = document.createComment('univer-original-category');
                table.parentNode.insertBefore(home, table); table.parentNode.insertBefore(box, table); box.appendChild(table);
                footer.closest('tr').classList.add('univer-app-menu-footer');
                categories.push({table:table, box:box, home:home});
            });
            filterMenu();
        }
        // Keep semantic data tables intact. Decorative layout/login/menu tables already fit above.
        Array.from(document.querySelectorAll('table')).forEach(function (table) {
            if (table === shell || (table.matches('.inner') && !table.classList.contains('univer-app-data')) || table.matches('.mt,.tool,.cols,#tools,#copyright') ||
                table.closest('#login_frm,#modal_auth_frm,#tools,.jexcel_container,.jss_container,.univer-app-table-scroll')) return;
            // Wrapping tables that participate in another table's structure would corrupt its DOM.
            if (table.parentElement.matches('tr,tbody,thead,tfoot,table')) return;
            var box = document.createElement('div'); box.className = 'univer-app-table-scroll';
            box.tabIndex = 0; box.setAttribute('role', 'region');
            box.setAttribute('aria-label', menuTitle === 'Univer sections' ? 'Table, scroll horizontally' :
                menuTitle === 'Univer бөлімдері' ? 'Кесте, көлденең айналдыру' : 'Таблица, прокрутка по горизонтали');
            table.parentNode.insertBefore(box, table); box.appendChild(table);
        });
        adaptUsability();
        window.__univerNewsV1?.apply(selectedLanguage);
        adaptFrames();
        scrollHints();
        } finally {
            window.__univerPresentationV1?.release();
            if (enabled) observer?.observe(document.documentElement, {childList:true, subtree:true, characterData:true,
                attributes:true, attributeFilter:['style','class','hidden']});
        }
    }
    function filterMenu() {
        var menu = document.querySelector('.univer-app-menu');
        if (!menu) return;
        var query = (menu.querySelector('input[type=search]')?.value || '').trim().toLocaleLowerCase().replace(/ё/g, 'е');
        var count = 0;
        menu.querySelectorAll('#tools li').forEach(function (item) {
            if (!menuHidden.has(item)) menuHidden.set(item, item.hidden);
            var matches = !menuHidden.get(item) && (!query || item.textContent.toLocaleLowerCase().replace(/ё/g, 'е').includes(query));
            if (item.hidden === matches) item.hidden = !matches;
            if (matches) count++;
        });
        categories.forEach(function (category) {
            var matches = Array.from(category.table.querySelectorAll('li')).some(function (li) { return !li.hidden; });
            if (category.box.hidden !== (!!query && !matches)) category.box.hidden = !!query && !matches;
            if (query && matches && !category.box.open) category.box.open = true;
        });
        var empty = menu.querySelector('.univer-app-empty');
        if (empty.textContent !== emptyLabel) empty.textContent = emptyLabel;
        if (empty.hidden !== (!query || count > 0)) empty.hidden = !query || count > 0;
    }
    function foldInfo(node, kind) {
        if (node.closest('.univer-app-info')) return;
        var details = document.createElement('details'); details.className = 'univer-app-info';
        details.dataset.univerInfo = kind;
        var summary = document.createElement('summary'); summary.textContent = infoTitles[kind];
        details.appendChild(summary); node.parentNode.insertBefore(details, node); details.appendChild(node);
    }
    // Style-only changes (a site animation rewrites an inline style every frame) adapt at most five times a second;
    // structure, classes and hidden still adapt before the next frame.
    var styleTimer = 0;
    function observed(records) {
        for (var i = 0; i < records.length; i++)
            if (records[i].type !== 'attributes' || records[i].attributeName !== 'style') { schedule(); return; }
        if (!styleTimer) styleTimer = setTimeout(function () { styleTimer = 0; schedule(); }, 200);
    }
    function schedule() {
        if (queued || !enabled) return;
        queued = true;
        // Adapt before the next frame: late AJAX content must not paint in the original layout first.
        // The timer is a fallback for hidden views, where animation frames are throttled.
        var run = function () { if (queued) adapt(); };
        if (typeof requestAnimationFrame === 'function') requestAnimationFrame(run);
        setTimeout(run, 100);
    }
    function setEnabled(value, language) {
        // AJAX is handled by the observer. Repeated native renders need no full DOM/layout traversal.
        if (enabled === !!value && selectedLanguage === language) {
            if (enabled) dialogViewport();
            return;
        }
        selectedLanguage = language;
        menuTitle = language === 'en' ? 'Univer sections' : language === 'kz' ? 'Univer бөлімдері' : 'Разделы Univer';
        infoTitles = language === 'en' ? {contacts:'KazNU contacts and information', links:'Useful links'} :
            language === 'kz' ? {contacts:'ҚазҰУ байланыстары мен ақпараты', links:'Пайдалы сілтемелер'} :
            {contacts:'Контакты и информация КазНУ', links:'Полезные ссылки'};
        searchLabel = language === 'en' ? 'Find a section…' : language === 'kz' ? 'Бөлімді іздеу…' : 'Найти раздел…';
        emptyLabel = language === 'en' ? 'No sections found' : language === 'kz' ? 'Бөлімдер табылмады' : 'Разделы не найдены';
        closeLabel = language === 'en' ? 'Close' : language === 'kz' ? 'Жабу' : 'Закрыть';
        if (value) document.documentElement.dataset.univerCorner = settingsCorner;
        if (value && !enabled) {
            enabled = true;
            document.documentElement.classList.add('univer-mobile');
            viewport = document.querySelector('meta[name=viewport]');
            madeViewport = !viewport;
            if (!viewport) { viewport = document.createElement('meta'); viewport.name = 'viewport'; (document.head || document.documentElement).appendChild(viewport); }
            originalViewport = viewport.getAttribute('content');
            viewport.content = 'width=device-width, initial-scale=1';
            var style = document.createElement('style'); style.id = 'univer-app-mobile-style'; style.textContent = css;
            (document.head || document.documentElement).appendChild(style);
            observer = new MutationObserver(observed);
            observer.observe(document.documentElement, {childList:true, subtree:true, characterData:true, attributes:true, attributeFilter:['style','class','hidden']});
            window.addEventListener('resize', schedule);
            window.addEventListener('univer-frame-ready', schedule);
            window.addEventListener('scroll', returnToMenuVisibility, {passive:true});
            window.visualViewport?.addEventListener('resize', dialogViewport);
            window.visualViewport?.addEventListener('scroll', dialogViewport);
            document.addEventListener('click', dismissMenu); document.addEventListener('keydown', dismissMenu);
        } else if (!value && enabled) {
            enabled = false;
            if (observer) observer.disconnect();
            clearTimeout(styleTimer); styleTimer = 0;
            document.removeEventListener('click', dismissMenu); document.removeEventListener('keydown', dismissMenu);
            window.removeEventListener('resize', schedule);
            window.removeEventListener('univer-frame-ready', schedule);
            window.removeEventListener('scroll', returnToMenuVisibility);
            window.visualViewport?.removeEventListener('resize', dialogViewport);
            window.visualViewport?.removeEventListener('scroll', dialogViewport);
            ['--univer-dialog-top','--univer-dialog-height','--univer-frame-height'].forEach(function (key) { document.documentElement.style.removeProperty(key); });
            document.documentElement.classList.remove('univer-mobile','univer-app-document','univer-app-schedule-page','univer-app-unified');
            delete document.documentElement.dataset.univerCorner;
            punctuation.forEach(function (value, node) { node.data = value; }); punctuation.clear();
            var recordClasses = ['univer-app-pairs','univer-app-form-table','univer-app-cards','univer-app-card-header',
                'univer-app-card-row','univer-app-card-section','univer-app-long-field','univer-app-matrix',
                'univer-app-nav-table','univer-app-list','univer-app-document-layout','univer-app-sort-head','univer-app-breadcrumb',
                'univer-app-gpa-layout','univer-app-catalog-title','univer-app-catalog-head','univer-app-sort-collapsed','univer-app-faq-row',
                'univer-app-video','univer-app-media-column','univer-app-content-columns','univer-app-empty-result','univer-app-calendar-meta','univer-app-empty-nav-cell','univer-app-div-records','univer-app-schedule-controls','univer-app-schedule-grid-visible',
                'univer-app-schedule-content','univer-app-empty-panel','univer-app-secondary-heading','univer-app-schedule-title','univer-app-print-action','univer-app-form-spacer',
                'univer-app-header-row','univer-app-semester-link','univer-app-empty-note','univer-app-action-link','univer-app-empty-row',
                'univer-app-today-col','univer-app-news-pager-source'];
            document.querySelectorAll(recordClasses.map(function (c) { return '.' + c; }).join(',')).forEach(function (node) {
                node.classList.remove(...recordClasses);
            });
            document.getElementById('univer-app-mobile-style')?.remove();
            headerMoves.slice().reverse().forEach(function (move) { if (move.home.parentNode) move.home.parentNode.replaceChild(move.node, move.home); }); headerMoves = [];
            document.querySelectorAll('.univer-app-header,.univer-app-account,.univer-app-course-open').forEach(function (node) { node.remove(); });
            schedules.forEach(function (record) { record.view.remove(); }); schedules.clear();
            if (upcomingTimer) { clearTimeout(upcomingTimer); upcomingTimer = 0; }
            finishedWeek = null;
            weekViews.forEach(function (record, select) { select.removeEventListener('change', record.update); record.caption.remove(); }); weekViews.clear();
            document.querySelectorAll('[data-univer-field]').forEach(function (node) { node.removeAttribute('data-univer-field'); });
            document.querySelectorAll('[data-univer-time]').forEach(function (node) { node.removeAttribute('data-univer-time'); });
            document.querySelectorAll('.univer-app-schedule-source,.univer-app-courses,.univer-app-materials,.univer-app-page-link,.univer-app-study-panel,.univer-app-study-empty').forEach(function (node) {
                node.classList.remove('univer-app-schedule-source','univer-app-courses','univer-app-materials','univer-app-page-link','univer-app-study-panel','univer-app-study-empty');
            });
            if (madeViewport) viewport.remove();
            else if (originalViewport === null) viewport.removeAttribute('content');
            else viewport.setAttribute('content', originalViewport);
            document.querySelectorAll('.univer-app-table-scroll').forEach(function (box) { box.replaceWith(...box.childNodes); });
            document.querySelectorAll('.univer-app-scroll-hint').forEach(function (hint) { hint.remove(); });
            document.querySelectorAll('.univer-app-catalog tr.file').forEach(function(row){if(catalogHidden.has(row))row.hidden=catalogHidden.get(row);}); catalogHidden=new WeakMap();
            usabilityMoves.slice().reverse().forEach(function(move){if(move.home.parentNode)move.home.parentNode.replaceChild(move.node,move.home);}); usabilityMoves=[];
            pageMarkers.forEach(function(value,node){if(value===null)node.removeAttribute('aria-current');else node.setAttribute('aria-current',value);});pageMarkers.clear();
            window.__univerNewsV1?.release();
            window.__univerShortcutsV1?.release();
            window.__univerStudyV1?.release();
            window.__univerPagesV1?.release();
            window.__univerTasksV1?.release();
            window.__univerAttendanceV1?.release();
            document.querySelectorAll('.univer-app-menu-close,.univer-app-quick,.univer-app-menu-return,.univer-app-catalog,.univer-app-catalog-search,.univer-app-catalog-empty,.univer-app-catalog-download,.univer-app-faq,.univer-app-sort-toggle,.univer-app-semester-nav,.univer-app-academic-meta,.univer-app-video-link').forEach(function(node){node.remove();});
            document.querySelectorAll('#tools li').forEach(function (li) { if (menuHidden.has(li)) li.hidden = menuHidden.get(li); });
            menuHidden = new WeakMap();
            categories.forEach(function (category) {
                if (category.home.parentNode) category.home.parentNode.replaceChild(category.table, category.home);
                category.box.remove();
            }); categories = [];
            document.querySelectorAll('.univer-app-menu-footer').forEach(function (row) { row.classList.remove('univer-app-menu-footer'); });
            document.querySelectorAll('.univer-app-menu').forEach(function (box) {
                var table = box.querySelector('#tools');
                if (table && menuHome?.parentNode) { menuHome.parentNode.replaceChild(table, menuHome); box.remove(); }
                else if (table) box.replaceWith(table);
            });
            menuHome = null;
            document.querySelectorAll('.univer-app-info').forEach(function (box) {
                box.querySelector('summary')?.remove(); box.replaceWith(...box.childNodes);
            });
            document.querySelectorAll('[data-univer-close]').forEach(function (close) {
                close.title = close.dataset.univerOldTitle;
                if (close.dataset.univerOldLabel) close.setAttribute('aria-label', close.dataset.univerOldLabel); else close.removeAttribute('aria-label');
                if (close.dataset.univerOldRole) close.setAttribute('role', close.dataset.univerOldRole); else close.removeAttribute('role');
                if (close.dataset.univerOldTab) close.setAttribute('tabindex', close.dataset.univerOldTab); else close.removeAttribute('tabindex');
                close.removeEventListener('keydown', close.__univerKeydown); delete close.__univerKeydown;
                delete close.dataset.univerClose; delete close.dataset.univerOldTitle; delete close.dataset.univerOldLabel;
                delete close.dataset.univerOldRole; delete close.dataset.univerOldTab;
            });
            document.querySelectorAll('.univer-app-modal-toolbar').forEach(function (bar) {
                var close = bar.querySelector('.simplemodal-close'), home = bar.__univerCloseHome;
                if (close && home?.parentNode) home.parentNode.replaceChild(close, home);
                bar.remove();
            });
            childAdapters.forEach(function (win) { try { win.__univerMobileV1?.setEnabled(false, language); } catch (_) {} });
            childAdapters.clear();
            document.querySelectorAll('iframe,frame').forEach(function (frame) {
                try {
                    if (frame.contentWindow.location.origin === location.origin)
                        frame.contentWindow.__univerPresentationV1?.release();
                } catch (_) { /* External documents are untouched. */ }
            });
            document.querySelectorAll('.univer-app-shell,.univer-app-news,.univer-app-data,.univer-app-data-field').forEach(function (node) {
                node.classList.remove('univer-app-shell','univer-app-news','univer-app-data','univer-app-data-field');
            });
        }
        adapt();
    }
    window.__univerMobileV1 = {setEnabled:setEnabled, setSettingsCorner:function (corner) {
        if (!/^(TOP|BOTTOM)_(LEFT|RIGHT)$/.test(corner)) return;
        if (settingsCorner === corner) return;
        settingsCorner = corner; if (enabled) document.documentElement.dataset.univerCorner = settingsCorner; schedule();
    }};
})();
