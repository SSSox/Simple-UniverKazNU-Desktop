/* Home shortcuts: fixed study tiles with "СДО Moodle", student-pinned sections and useful links,
   and the useful links list inside "All sections". Only a real click pins or unpins; the app keeps the list. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerShortcutsV1) return;
    var common = window.__univerCommonV1, cleanText = common.clean;
    var pins = [], pinsKey = '[]', tileOrder = [], orderKey = '[]', draggedTile = null, language = 'ru', active = false, owned = [], pinButtons = new Set(), menuRows = new Map(), style = null;
    var moodlePath = '/user/loginMoodle';
    // Study tiles in the order students look for them; the site menu supplies their real links and names.
    // The news page menu has no study sections, so the observed routes are used directly there.
    var fixed = [['schedule', '/student/myschedule/'], ['grades', '/student/attendance/full/'], ['transcript', '/student/transcript/'], ['umkd', '/student/umkd/']];
        // Tiles open Главная and Бакалавр; Е-услуги is the services desk reached from its own tile.
    var homePages = /^\/news\/student\/?$|^\/student\/bachelor\/?$/i;
    var css = `
/* Home and Bachelor share a content gutter; tiles, headings and panels start on one line. */
:root.univer-mobile.univer-app-home { --u-home-gutter:24px; }
:root.univer-mobile.univer-app-home #l4>td { padding:24px var(--u-home-gutter) 32px!important; box-sizing:border-box; }
:root.univer-mobile.univer-app-home #l4 td.center.univer-app-home-body { padding:0!important; }
:root.univer-mobile.univer-app-home #l4>td>* { max-width:var(--u-page); margin-left:auto!important; margin-right:auto!important; box-sizing:border-box; }
:root.univer-mobile.univer-app-home .univer-app-quick { margin:0 auto 24px!important; align-items:stretch; gap:12px; }
:root.univer-mobile.univer-app-home #l4 h1 { margin-top:24px; margin-bottom:12px; }
:root.univer-mobile.univer-app-home #l4 table.mt td.univer-app-credentials-host { padding:8px 18px 12px!important; }
:root.univer-mobile #l4 td.univer-app-credentials-host .univer-app-credential { display:grid!important; grid-template-columns:110px minmax(0,1fr) auto; }
:root.univer-mobile #l4 td.univer-app-credentials-host .univer-app-credential-value { min-width:0; }
@media (max-width:900px) { :root.univer-mobile.univer-app-home { --u-home-gutter:16px; } }
@media (max-width:640px) { :root.univer-mobile #l4 td.univer-app-credentials-host .univer-app-credential { grid-template-columns:minmax(0,1fr); } }
:root.univer-mobile .univer-app-quick .univer-app-tile { position:relative; display:flex; min-width:0; }
:root.univer-mobile .univer-app-quick .univer-app-tile>a { flex:1 1 auto; min-width:0; overflow-wrap:break-word; }
:root.univer-mobile .univer-app-quick .univer-app-tile.univer-app-pinned>a { padding-right:46px!important; }
/* The icon already says where a tile leads; only links that leave the site keep a small outward arrow. */
:root.univer-mobile .univer-app-quick .univer-app-tile>a::after { content:none!important; }
:root.univer-mobile .univer-app-quick .univer-app-tile.univer-app-external .univer-app-tile-label::after { content:'↗'; margin-left:6px; color:var(--u-muted,#5b6b7c); font-weight:400; }
:root.univer-mobile .univer-app-unpin { position:absolute; top:50%; right:10px; display:grid; place-items:center; width:28px; height:28px; margin:0!important; padding:0!important; border:0!important; border-radius:8px!important; background:transparent!important; color:var(--u-muted,#5b6b7c)!important; cursor:pointer; transform:translateY(-50%); }
:root.univer-mobile .univer-app-unpin::before { content:''; width:14px; height:14px; background:currentColor; -webkit-mask:var(--univer-close-icon) center/contain no-repeat; }
:root.univer-mobile .univer-app-tile>a { padding-right:40px!important; }
:root.univer-mobile .univer-app-tile.univer-app-pinned>a { padding-right:72px!important; }
:root.univer-mobile .univer-app-reorder { position:absolute; right:8px; top:50%; transform:translateY(-50%); width:28px; height:28px; padding:0!important; margin:0!important; border:0!important; border-radius:6px; background:transparent!important; color:var(--u-muted,#5b6b7c)!important; cursor:grab; font-size:20px!important; touch-action:none; }
:root.univer-mobile .univer-app-pinned .univer-app-reorder { right:40px; }
:root.univer-mobile .univer-app-reorder:focus-visible { outline:2px solid var(--u-brand,#1b5fa6); outline-offset:1px; }
:root.univer-mobile .univer-app-reorder::before { content:''; display:block; width:16px; height:16px; margin:auto; background:currentColor; -webkit-mask:var(--univer-reorder-icon) center/contain no-repeat; }
:root.univer-mobile .univer-app-dragging { opacity:.55; }
:root.univer-mobile .univer-app-drop-target { outline:2px solid var(--u-brand,#1b5fa6); outline-offset:2px; }
:root.univer-mobile .univer-app-after-credentials { display:none!important; }
:root.univer-mobile .univer-app-unpin:hover, :root.univer-mobile .univer-app-unpin:focus-visible { background:var(--u-surface-2,#f3f6f9)!important; color:var(--u-danger,#b42318)!important; outline:2px solid var(--u-brand,#1b5fa6); outline-offset:-2px; }
:root.univer-mobile #tools .univer-app-pin-row { display:flex; align-items:center; width:100%; min-width:0; }
:root.univer-mobile #tools .univer-app-pin-row>a { flex:1 1 auto; min-width:0; display:flex!important; align-items:center; white-space:normal!important; overflow-wrap:anywhere; text-indent:0!important; }
:root.univer-mobile #tools .univer-app-menu-label { display:block; min-width:0; }
:root.univer-mobile .univer-app-pin { flex:0 0 auto; width:30px; height:30px; margin:0 6px 0 2px!important; padding:0!important; border:0!important; border-radius:8px!important; background:transparent!important; color:var(--u-muted,#5b6b7c)!important; cursor:pointer; opacity:.7; }
:root.univer-mobile .univer-app-pin::before { content:''; display:block; width:16px; height:16px; margin:auto; background:currentColor; -webkit-mask:var(--univer-pin-icon) center/contain no-repeat; }
:root.univer-mobile li:hover>.univer-app-pin, :root.univer-mobile li:focus-within>.univer-app-pin, :root.univer-mobile .univer-app-pin[aria-pressed=true] { opacity:1; }
:root.univer-mobile .univer-app-pin[aria-pressed=true] { color:var(--u-brand,#1b5fa6)!important; }
:root.univer-mobile .univer-app-pin[aria-pressed=true]::before { -webkit-mask-image:var(--univer-pinned-icon); }
:root.univer-mobile .univer-app-pin:hover, :root.univer-mobile .univer-app-pin:focus-visible { background:var(--u-brand-soft,#e9f1fb)!important; color:var(--u-brand,#1b5fa6)!important; outline:2px solid var(--u-brand,#1b5fa6); outline-offset:-2px; }
:root.univer-mobile .univer-app-useful ul { display:grid!important; grid-template-columns:repeat(auto-fill,minmax(230px,1fr)); gap:8px; margin:0!important; padding:12px!important; list-style:none!important; }
:root.univer-mobile .univer-app-useful li { display:flex!important; align-items:center; margin:0!important; padding:0 4px 0 0!important; list-style:none!important; border:1px solid var(--u-line,#dfe5ec); border-radius:8px; background:#fff; }
:root.univer-mobile .univer-app-useful li:hover, :root.univer-mobile .univer-app-useful li:focus-within { border-color:var(--u-brand-line,#bfd3ea); box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-useful li[hidden] { display:none!important; }
:root.univer-mobile .univer-app-useful li>a { flex:1 1 auto; min-width:0; display:flex!important; align-items:center; gap:10px; padding:7px 8px!important; border-radius:9px; color:var(--u-ink,#16283b)!important; font-size:14px!important; line-height:1.35!important; text-decoration:none; }
:root.univer-mobile .univer-app-useful li>a:hover, :root.univer-mobile .univer-app-useful li>a:focus-visible { color:var(--u-brand,#1b5fa6)!important; outline:none; }
:root.univer-mobile .univer-app-useful li>a:focus-visible { box-shadow:inset 0 0 0 2px var(--u-brand,#1b5fa6); }
:root.univer-mobile .univer-app-link-icon { flex:0 0 auto; display:grid; place-items:center; width:30px; height:30px; border-radius:6px; background:var(--tint,#1b5fa6); box-shadow:none; }
:root.univer-mobile .univer-app-link-icon::before { content:''; width:18px; height:18px; background:#fff; -webkit-mask:var(--glyph) center/contain no-repeat; }
:root.univer-mobile .univer-app-link-icon.univer-app-link-internal { background:var(--u-brand-soft,#e9f1fb); }
:root.univer-mobile .univer-app-link-icon.univer-app-link-internal::before { background:var(--u-brand,#1b5fa6); }
:root.univer-mobile .univer-app-link-icon.univer-app-link-art { box-shadow:inset 0 0 0 1px var(--u-line,#dfe5ec); }
:root.univer-mobile .univer-app-link-icon.univer-app-link-art::before { background:var(--art) center/contain no-repeat; -webkit-mask:none; }
:root.univer-mobile .univer-app-quick .univer-app-tile>a { gap:12px; }
:root.univer-mobile .univer-app-quick .univer-app-link-icon { width:32px; height:32px; }
:root.univer-mobile .univer-app-quick .univer-app-tile-label { flex:1 1 auto; min-width:0; }
:root.univer-mobile .univer-app-menu>.univer-app-useful { margin-top:12px!important; }
/* Header tabs carry an icon for what each one holds. */
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon::before { content:''; width:18px; height:18px; margin-right:8px; flex:0 0 auto; background:currentColor; -webkit-mask:var(--glyph) center/contain no-repeat; }
/* Бакалавр: the two GPA figures as the page's lead. */
:root.univer-mobile .univer-app-gpa { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,240px)); gap:12px; margin:4px 0 20px; }
:root.univer-mobile .univer-app-gpa>div { padding:14px 18px; border:1px solid var(--u-line,#dfe5ec); border-radius:12px; background:#fff; box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-gpa b { display:block; color:var(--u-ink,#16283b); font-size:28px; line-height:1.15; font-weight:650; font-variant-numeric:tabular-nums; }
:root.univer-mobile .univer-app-gpa span { display:block; margin-top:2px; color:var(--u-muted,#5b6b7c); font-size:13px; }
:root.univer-mobile .univer-app-gpa-source { display:none!important; }
:root.univer-mobile .univer-app-gpa-source-table { display:none!important; }
/* Е-услуги: one switch between the catalogue and the student's own requests on every services page. */
:root.univer-mobile .univer-app-ssc-nav { display:inline-flex; gap:4px; margin:16px 0 18px; padding:4px; border:1px solid var(--u-line,#dfe5ec); border-radius:12px; background:#fff; }
:root.univer-mobile .univer-app-ssc-nav[hidden] { display:none!important; }
:root.univer-mobile .univer-app-ssc-nav a { display:inline-flex; align-items:center; gap:8px; min-height:36px; padding:0 14px; border-radius:9px; color:var(--u-muted,#5b6b7c)!important; font-size:14px; font-weight:600; text-decoration:none!important; }
:root.univer-mobile .univer-app-ssc-nav a::before { content:''; width:18px; height:18px; background:currentColor; -webkit-mask:var(--glyph) center/contain no-repeat; }
:root.univer-mobile .univer-app-ssc-nav a:hover { background:var(--u-surface-2,#f3f6f9); color:var(--u-ink,#16283b)!important; }
:root.univer-mobile .univer-app-ssc-nav a[aria-current=page] { background:var(--u-brand-soft,#e9f1fb); color:var(--u-brand,#1b5fa6)!important; }
:root.univer-mobile .univer-app-ssc-nav a:focus-visible { outline:2px solid var(--u-brand,#1b5fa6); outline-offset:1px; }
:root.univer-mobile #l4 :is(.ts,.univer-app-desk-cards):has(>a.univer-app-service-card) { display:flex!important; flex-wrap:wrap; gap:12px; margin:0 0 16px; }
:root.univer-mobile #l4 a.univer-app-desk-card { border:1px solid var(--u-line,#dfe5ec); border-radius:14px; background:#fff; box-shadow:var(--u-shadow); }
:root.univer-mobile #l4 a.univer-app-desk-card:hover { border-color:var(--u-brand-line,#bfd3ea); }
:root.univer-mobile #l4 td.ct.univer-app-panel-head:not(:first-child), :root.univer-mobile #l4 tr:not(:first-child)>td.ct.univer-app-panel-head { border-top:1px solid var(--u-line,#dfe5ec)!important; }
:root.univer-mobile #l4 :is(.ts>a,a).univer-app-service-card { flex:1 1 300px; box-sizing:border-box; height:auto!important; display:grid!important; grid-template-columns:44px minmax(0,1fr); grid-template-rows:auto auto; column-gap:14px; align-items:center; min-height:76px; padding:14px 18px!important; text-align:start!important; text-decoration:none!important; color:var(--u-ink,#16283b)!important; font-size:16px!important; font-weight:650; }
:root.univer-mobile #l4 :is(.ts>a,a).univer-app-service-card>.univer-app-link-icon { grid-row:1/3; width:44px; height:44px; border-radius:12px; }
:root.univer-mobile #l4 :is(.ts>a,a).univer-app-service-card>.univer-app-link-icon::before { width:24px; height:24px; }
:root.univer-mobile #l4 :is(.ts>a,a).univer-app-service-card>:not(.univer-app-link-icon) { grid-column:2; }
:root.univer-mobile #l4 :is(.ts>a,a).univer-app-service-card::after { content:attr(data-univer-hint); grid-column:2; color:var(--u-muted,#5b6b7c); font-size:13px; font-weight:400; line-height:1.4; }
:root.univer-mobile #l4 :is(.ts>a,a).univer-app-service-card * { text-align:start!important; }
:root.univer-mobile .univer-app-service-search { display:block; width:100%; max-width:560px; height:42px; box-sizing:border-box; margin:0 0 14px!important; padding-left:38px!important; }
:root.univer-mobile .univer-app-service-empty { margin:4px 0 14px!important; color:var(--u-muted,#5b6b7c); }
:root.univer-mobile tr.univer-app-service-hidden { display:none!important; }
:root.univer-mobile .univer-app-status { white-space:nowrap; }
:root.univer-mobile .univer-app-status::before { content:''; display:inline-block; width:8px; height:8px; margin-right:8px; border-radius:50%; background:var(--u-muted,#5b6b7c); vertical-align:1px; }
:root.univer-mobile .univer-app-status[data-univer-tone=done]::before { background:#15803d; }
:root.univer-mobile .univer-app-status[data-univer-tone=done] { color:#166534!important; font-weight:600; }
:root.univer-mobile .univer-app-status[data-univer-tone=wait]::before { background:#d97706; }
:root.univer-mobile .univer-app-status[data-univer-tone=fail]::before { background:#b42318; }
:root.univer-mobile .univer-app-status[data-univer-tone=fail] { color:#b42318!important; font-weight:600; }
/* Print and Wi-Fi sign-in as its own panel: login visible, password hidden until shown, both copyable. */
:root.univer-mobile .univer-app-credentials-source { display:none!important; }
:root.univer-mobile #l4 td.univer-app-credentials-host { font-size:0!important; }
:root.univer-mobile #l4 td.univer-app-credentials-host>:not(.univer-app-credentials) { display:none!important; }
:root.univer-mobile #l4 td.univer-app-credentials-host>.univer-app-credentials { margin:0; padding:0; border:0; box-shadow:none; font-size:14px; }
:root.univer-mobile #l4 td.univer-app-credentials-host .univer-app-credential:first-child { border-top:0; }
:root.univer-mobile .univer-app-credentials { margin:8px 0 16px; padding:16px 20px; border:1px solid var(--u-line,#dfe5ec); border-radius:14px; background:#fff; box-shadow:var(--u-shadow); }
:root.univer-mobile .univer-app-credentials>h3 { display:flex; align-items:center; gap:12px; margin:0 0 12px!important; color:var(--u-ink,#16283b)!important; font-size:16px!important; font-weight:650; }
:root.univer-mobile .univer-app-credentials>h3>.univer-app-link-icon { width:32px; height:32px; border-radius:9px; }
:root.univer-mobile .univer-app-credential { display:grid; grid-template-columns:110px minmax(0,1fr) auto; align-items:center; gap:12px; padding:10px 0; border-top:1px solid var(--u-line,#dfe5ec); }
:root.univer-mobile .univer-app-credential-label { color:var(--u-muted,#5b6b7c); font-size:13px; }
:root.univer-mobile .univer-app-credential-value { color:var(--u-ink,#16283b); font-size:15px; font-weight:600; font-variant-numeric:tabular-nums; letter-spacing:.01em; overflow-wrap:anywhere; }
:root.univer-mobile .univer-app-credential-hidden { letter-spacing:.18em; color:var(--u-muted,#5b6b7c); }
:root.univer-mobile .univer-app-credential-actions { display:flex; gap:8px; }
:root.univer-mobile .univer-app-credential-button { min-height:34px; padding:0 14px!important; border:1px solid var(--u-line,#dfe5ec)!important; border-radius:8px!important; background:#fff!important; color:var(--u-ink,#16283b)!important; font-size:13px!important; font-weight:600; cursor:pointer; }
:root.univer-mobile .univer-app-credential-button:hover { background:var(--u-surface-2,#f3f6f9)!important; }
:root.univer-mobile .univer-app-credential-button:focus-visible { outline:2px solid var(--u-brand,#1b5fa6); outline-offset:2px; }
:root.univer-mobile .univer-app-credential-button.univer-app-copied { border-color:#86c7a0!important; background:#ebf8f1!important; color:#166534!important; }
@media (max-width:640px) { :root.univer-mobile .univer-app-credential { grid-template-columns:minmax(0,1fr); gap:6px; } }
/* Бакалавр and Е-услуги summaries: today's classes, grades and the latest requests as panels. */
:root.univer-mobile .univer-app-overview-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(340px,1fr)); gap:16px; margin:0 0 16px; align-items:start; }
:root.univer-mobile .univer-app-overview { margin:0 0 16px; border:1px solid var(--u-line,#dfe5ec); border-radius:14px; background:#fff; box-shadow:var(--u-shadow); overflow:hidden; }
:root.univer-mobile .univer-app-overview-grid>.univer-app-overview { margin:0; }
:root.univer-mobile .univer-app-overview-head { display:flex; align-items:center; gap:12px; padding:14px 20px; border-bottom:1px solid var(--u-line,#dfe5ec); background:var(--u-surface-2,#f7f9fb); }
:root.univer-mobile .univer-app-overview-head>h2 { flex:1 1 auto; margin:0!important; color:var(--u-ink,#16283b)!important; font-size:16px!important; font-weight:650; }
:root.univer-mobile .univer-app-overview-link { color:var(--u-brand,#1b5fa6)!important; font-size:13px!important; font-weight:600; text-decoration:none!important; white-space:nowrap; }
:root.univer-mobile .univer-app-overview-link:hover { text-decoration:underline!important; }
:root.univer-mobile .univer-app-overview-body { padding:12px 20px 16px; }
:root.univer-mobile .univer-app-overview-caption { margin:0 0 8px!important; color:var(--u-muted,#5b6b7c); font-size:13px!important; }
:root.univer-mobile .univer-app-overview-empty { margin:4px 0!important; color:var(--u-muted,#5b6b7c); }
:root.univer-mobile .univer-app-overview :is(ol,ul) { margin:0!important; padding:0!important; list-style:none!important; }
:root.univer-mobile .univer-app-overview li { display:flex; align-items:flex-start; gap:14px; margin:0!important; padding:10px 0!important; border-top:1px solid var(--u-line,#dfe5ec); list-style:none!important; }
:root.univer-mobile .univer-app-overview li:first-child { border-top:0; }
:root.univer-mobile .univer-app-overview-lesson>time { flex:0 0 96px; padding-top:1px; color:var(--u-muted,#5b6b7c); font-size:13px; font-variant-numeric:tabular-nums; }
:root.univer-mobile .univer-app-overview-lesson-main { flex:1 1 auto; min-width:0; }
:root.univer-mobile .univer-app-overview-lesson-main>b { display:block; color:var(--u-ink,#16283b); font-size:14px; font-weight:600; }
:root.univer-mobile .univer-app-overview-lesson-main>span { display:block; margin-top:2px; color:var(--u-muted,#5b6b7c); font-size:13px; }
:root.univer-mobile .univer-app-overview-done { opacity:.6; }
:root.univer-mobile .univer-app-overview-now { margin:0 -12px!important; padding:10px 12px!important; border-radius:10px; background:var(--u-brand-soft,#e9f1fb); border-top-color:transparent!important; }
:root.univer-mobile .univer-app-overview-state { flex:0 0 auto; padding:2px 10px; border-radius:999px; background:var(--u-surface-2,#f3f6f9); color:var(--u-muted,#5b6b7c); font-size:12px; font-weight:600; }
:root.univer-mobile .univer-app-overview-now .univer-app-overview-state { background:var(--u-brand,#1b5fa6); color:#fff; }
:root.univer-mobile .univer-app-overview-next .univer-app-overview-state { background:var(--u-teal-soft,#e3f3ef); color:var(--u-teal,#0f766e); }
:root.univer-mobile .univer-app-overview-courses>li { align-items:center; gap:12px; }
:root.univer-mobile .univer-app-overview-courses>li>.univer-app-overview-course-link { flex:1 1 160px; min-width:0; color:var(--u-brand,#1b5fa6)!important; font-size:14px; font-weight:500; text-decoration:none; }
:root.univer-mobile .univer-app-overview-course-link:hover { text-decoration:underline; }
:root.univer-mobile .univer-app-overview-scores { display:flex; flex:0 1 auto; flex-wrap:wrap; justify-content:flex-end; gap:6px; }
:root.univer-mobile .univer-app-overview-courses .univer-app-overview-score { min-width:70px; padding:4px 8px 7px; }
:root.univer-mobile .univer-app-overview-more { display:block; width:100%; margin:8px 0 0; min-height:34px; border:1px solid var(--u-line,#dfe5ec)!important; border-radius:8px!important; background:#fff!important; color:var(--u-brand,#1b5fa6)!important; font-size:13px!important; font-weight:600; cursor:pointer; }
:root.univer-mobile .univer-app-overview-more:hover { background:var(--u-brand-soft,#e9f1fb)!important; }
:root.univer-mobile .univer-app-overview li[hidden] { display:none!important; }
:root.univer-mobile .univer-app-overview-next-day { margin:10px 0 2px!important; color:var(--u-ink,#16283b); font-size:13px!important; font-weight:600; }
:root.univer-mobile .univer-app-overview-request { display:flex; align-items:center; gap:12px; width:100%; margin:-6px -10px; padding:6px 10px; border-radius:9px; color:inherit!important; text-decoration:none!important; }
:root.univer-mobile .univer-app-overview-request:hover { background:var(--u-brand-soft,#e9f1fb); }
:root.univer-mobile .univer-app-overview-request:focus-visible { outline:2px solid var(--u-brand,#1b5fa6); outline-offset:0; }
:root.univer-mobile .univer-app-overview-request>.univer-app-status { margin-left:auto; }
:root.univer-mobile .univer-app-overview-score { position:relative; display:inline-flex; align-items:baseline; gap:6px; min-width:84px; padding:6px 10px 9px; border:1px solid var(--u-line,#dfe5ec); border-radius:9px; overflow:hidden; }
:root.univer-mobile .univer-app-overview-score-label { color:var(--u-muted,#5b6b7c); font-size:12px; }
:root.univer-mobile .univer-app-overview-score-value { color:var(--u-ink,#16283b); font-size:15px; font-weight:650; font-variant-numeric:tabular-nums; }
:root.univer-mobile .univer-app-overview-score-bar { position:absolute; left:0; bottom:0; height:3px; width:var(--univer-score); background:var(--u-teal,#0f766e); }
:root.univer-mobile .univer-app-overview-requests>li { align-items:center; }
/* Two large study tabs; Е-услуги lives on the home screen as a tile. */
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-hidden { display:none!important; }
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon { height:44px!important; padding:0 20px!important; border-radius:10px!important; font-size:15px!important; font-weight:600!important; }
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon::before { width:20px!important; height:20px!important; }
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon:hover { background:var(--u-brand-soft,#e9f1fb)!important; color:var(--u-brand,#1b5fa6)!important; box-shadow:inset 0 0 0 1px var(--u-brand-line,#bfd3ea); }
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon.active { background:var(--u-brand,#1b5fa6)!important; color:#fff!important; box-shadow:0 1px 2px rgba(16,40,70,.15); }
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon.active:hover { background:var(--u-brand-strong,#154d88)!important; color:#fff!important; }
:root.univer-mobile .univer-app-tabs>a.univer-app-tab-icon:focus-visible { outline:2px solid var(--u-brand,#1b5fa6); outline-offset:2px; }
/* Services: a compact list; request rows open the request. */
:root.univer-mobile #l4 table.univer-app-service-table :is(td,th) { padding-top:6px!important; padding-bottom:6px!important; }
:root.univer-mobile #l4 table.univer-app-service-table tr { min-height:0!important; height:auto!important; }
:root.univer-mobile #l4 table.univer-app-service-table a { min-height:30px!important; padding:4px 12px!important; }
:root.univer-mobile #l4 table.univer-app-request-table tr[id] { cursor:pointer; }
:root.univer-mobile #l4 table.univer-app-request-table tr[id]:hover>td { background:var(--u-brand-soft,#e9f1fb)!important; }
/* Titled panels for the site's own sections on Бакалавр and Е-услуги. */
:root.univer-mobile #l4 table.mt.univer-app-panel { display:block!important; width:auto!important; margin:0 0 16px!important; overflow:hidden; background:#fff!important; border:1px solid var(--u-line,#dfe5ec)!important; border-radius:14px!important; box-shadow:var(--u-shadow); }
:root.univer-mobile #l4 table.mt.univer-app-panel>tbody, :root.univer-mobile #l4 table.mt.univer-app-panel>tbody>tr { display:block!important; }
:root.univer-mobile #l4 table.mt.univer-app-panel :is(td.lt,td.rt,td.pd) { display:none!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-head { display:flex!important; align-items:center; gap:12px; padding:14px 20px!important; border-bottom:1px solid var(--u-line,#dfe5ec)!important; background:var(--u-surface-2,#f7f9fb)!important; color:var(--u-ink,#16283b)!important; font-size:16px!important; font-weight:650!important; text-align:start!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-head>.univer-app-link-icon { width:32px; height:32px; border-radius:9px; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body { display:block!important; padding:16px 20px!important; text-align:start!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body :is(h1,h2,h3,h4) { margin:0 0 8px!important; color:var(--u-ink,#16283b)!important; font-size:16px!important; line-height:1.4!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body :is(ol,ul):not(.univer-app-gpa) { margin:4px 0!important; padding-left:20px!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body li { margin:0 0 8px!important; color:var(--u-text,#2a3d51)!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body font { color:inherit!important; }
/* Compact, contrasting content: the info icon goes, notices get a coloured edge, documents become link chips. */
:root.univer-mobile #l4 table.mt.univer-app-panel>tbody>tr>td.ct.univer-app-panel-head { padding:12px 18px!important; font-size:15px!important; }
:root.univer-mobile #l4 table.mt.univer-app-panel>tbody>tr>td.univer-app-panel-body { display:block!important; padding:12px 18px!important; box-sizing:border-box; }
:root.univer-mobile.univer-app-home #l4 .univer-app-panel-body table.univer-app-pairs>tbody>tr { display:grid!important; grid-template-columns:140px minmax(0,1fr); gap:4px 16px; margin:0!important; padding:12px 0!important; border:0!important; border-bottom:1px solid var(--u-line)!important; border-radius:0!important; background:none!important; }
:root.univer-mobile.univer-app-home #l4 .univer-app-panel-body table.univer-app-pairs>tbody>tr:last-child { border-bottom:0!important; }
:root.univer-mobile.univer-app-home #l4 .univer-app-panel-body table.univer-app-pairs>tbody>tr>td { padding:4px 0!important; margin:0!important; }
@media (min-width:1100px) { :root.univer-mobile.univer-app-home #l4 .univer-app-panel-body table.univer-app-pairs>tbody>tr { grid-template-columns:140px minmax(0,1fr) 140px minmax(0,1fr); } }
@media (max-width:640px) { :root.univer-mobile.univer-app-home #l4 .univer-app-panel-body table.univer-app-pairs>tbody>tr { grid-template-columns:minmax(0,1fr); gap:4px; } }
:root.univer-mobile #l4 td.ct.univer-app-panel-body img[src*="info"] { display:none!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body>b { display:block; margin:0 0 8px; color:var(--u-ink,#16283b); font-size:15px; font-weight:600; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body>b+br { display:none; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ol.univer-app-list { padding:0!important; list-style:none!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ol.univer-app-list>li { margin:0 0 8px!important; padding:10px 14px!important; border:1px solid #f1d6a6; border-radius:8px; background:#fff8eb; color:var(--u-ink,#16283b)!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ol.univer-app-list>li br:last-child { display:none; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ul.univer-app-list { display:flex; flex-wrap:wrap; gap:8px; padding:0!important; list-style:none!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ul.univer-app-list>li { margin:0!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ul.univer-app-list>li>a { display:inline-flex!important; align-items:center; gap:8px; padding:8px 14px!important; border:1px solid var(--u-brand-line,#bfd3ea)!important; border-radius:10px!important; background:var(--u-brand-soft,#e9f1fb)!important; color:var(--u-brand-strong,#154d88)!important; font-weight:600; text-decoration:none!important; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ul.univer-app-list>li>a::before { content:''; width:16px; height:16px; background:currentColor; -webkit-mask:var(--univer-doc-icon) center/contain no-repeat; }
:root.univer-mobile #l4 td.ct.univer-app-panel-body ul.univer-app-list>li>a:hover { background:#dbe8f7!important; }

`;
    // Stroke icons as CSS masks: no network requests, and each family keeps one tint so links are told apart at a glance.
    function svg(body, fill) { return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='" + (fill ? 'black' : 'none') + "' stroke='black' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>" + body + '</svg>') + '")'; }
    var glyphs = {
        calendar:svg("<rect x='4' y='5' width='16' height='16' rx='2'/><path d='M4 10h16M8 3v4M16 3v4'/>"),
        grades:svg("<path d='M4 20V10M10 20V4M16 20v-7M22 20H2'/>"),
        transcript:svg("<path d='M14 3H6v18h12V7z'/><path d='M14 3v4h4M9 12h6M9 16h6'/>"),
        book:svg("<path d='M4 19V5a2 2 0 0 1 2-2h14v16H6a2 2 0 0 0-2 2z'/><path d='M8 7h8'/>"),
        open:svg("<path d='M2 5h7a3 3 0 0 1 3 3v13a2 2 0 0 0-2-2H2zM22 5h-7a3 3 0 0 0-3 3v13a2 2 0 0 1 2-2h8z'/>"),
        cap:svg("<path d='M2 9l10-5 10 5-10 5z'/><path d='M6 11v5c2 2 10 2 12 0v-5M22 9v6'/>"),
        mail:svg("<rect x='3' y='5' width='18' height='14' rx='2'/><path d='M3 7l9 6 9-6'/>"),
        services:svg("<rect x='4' y='4' width='6' height='6' rx='1'/><rect x='14' y='4' width='6' height='6' rx='1'/><rect x='4' y='14' width='6' height='6' rx='1'/><rect x='14' y='14' width='6' height='6' rx='1'/>"),
        news:svg("<path d='M4 5h13v14H6a2 2 0 0 1-2-2z'/><path d='M17 9h3v8a2 2 0 0 1-2 2M8 9h5M8 13h5'/>"),
        help:svg("<circle cx='12' cy='12' r='9'/><path d='M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6M12 17h.01'/>"),
        folder:svg("<path d='M3 19V5h6l2 3h10v11z'/>"),
        calc:svg("<rect x='5' y='3' width='14' height='18' rx='2'/><path d='M8 7h8M8 12h.01M12 12h.01M16 12h.01M8 16h.01M12 16h.01M16 16h.01'/>"),
        list:svg("<path d='M9 6h11M9 12h11M9 18h11M4 6h.01M4 12h.01M4 18h.01'/>"),
        play:svg("<circle cx='12' cy='12' r='9'/><path d='M10 8.5l5 3.5-5 3.5z'/>"),
        youtube:svg("<rect x='2' y='5' width='20' height='14' rx='4'/><path d='M10 9l5 3-5 3z'/>"),
        facebook:svg("<path d='M15 3h-2.5A3.5 3.5 0 0 0 9 6.5V10H6v4h3v7h4v-7h3l1-4h-4V7a1 1 0 0 1 1-1h2z'/>"),
        twitter:svg("<path d='M4 4l16 16M20 4L4 20'/>"),
        phone:svg("<rect x='7' y='2' width='10' height='20' rx='2'/><path d='M11 18h2'/>"),
        journal:svg("<path d='M6 3h12v18H6z'/><path d='M9 7h6M9 11h6M9 15h3'/>"),
        idea:svg("<path d='M9 18h6M10 21h4'/><path d='M12 3a6 6 0 0 0-4 10.5c.8.7 1 1.5 1 2.5h6c0-1 .2-1.8 1-2.5A6 6 0 0 0 12 3z'/>"),
        wifi:svg("<path d='M2 9a15 15 0 0 1 20 0M5 12.5a10 10 0 0 1 14 0M8.5 16a5 5 0 0 1 7 0M12 19.5h.01'/>"),
        globe:svg("<circle cx='12' cy='12' r='9'/><path d='M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18'/>"),
        person:svg("<circle cx='12' cy='8' r='4'/><path d='M4 21c1.6-4 4.6-6 8-6s6.4 2 8 6'/>"),
        key:svg("<circle cx='8' cy='15' r='4'/><path d='M11 12l9-9M17 6l3 3'/>"),
        home:svg("<path d='M3 11l9-8 9 8'/><path d='M5 10v11h5v-6h4v6h5V10'/>"),
        link:svg("<path d='M10 14a4 4 0 0 0 6 0l3-3a4 4 0 0 0-6-6l-1 1'/><path d='M14 10a4 4 0 0 0-6 0l-3 3a4 4 0 0 0 6 6l1-1'/>")
    };
    // App-style icons: a solid colour square with a white mark; well-known services keep their own colours and marks.
    function fill(body) { return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='black'>" + body + '</svg>') + '")'; }
    function art(body) { return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'>" + body + '</svg>') + '")'; }
    glyphs.facebook = fill("<path d='M13.4 21v-7.6h2.6l.4-3h-3V8.5c0-.9.3-1.5 1.6-1.5H16.6V4.3a21 21 0 0 0-2.4-.1c-2.4 0-4 1.4-4 4.1v2.1H7.6v3h2.6V21z'/>");
    glyphs.youtube = fill("<path d='M9.5 7.8v8.4l7-4.2z'/>");
    glyphs.twitter = fill("<path d='M17.6 3.5h2.9l-6.3 7.2 7.4 9.8h-5.8l-4.5-5.9-5.2 5.9H3.2l6.7-7.7L2.8 3.5h5.9l4.1 5.4zm-1 15.3h1.6L7.6 5.1H5.9z'/>");
    glyphs.appstore = svg("<path d='M10.5 6.5l5.5 10M13.5 6.5L8 16.5M6 13.5h12M15.8 16.2l1.2 2.3'/>");
    glyphs.moodle = svg("<path d='M5.5 18v-5a2.7 2.7 0 0 1 5.4 0v5M10.9 13a2.7 2.7 0 0 1 5.4 0v5M8 7.5l6-2.5 6 2.5-6 2.5zM20 7.5v3.5'/>");
    var arts = {
        googleplay: art("<path fill='#00a0ff' d='M4.6 2.9 13.4 12l-8.8 9.1a1.3 1.3 0 0 1-.6-1.1V4a1.3 1.3 0 0 1 .6-1.1z'/><path fill='#00e676' d='M4.6 2.9 16.4 9.2 13.4 12z'/><path fill='#ff3d44' d='M13.4 12l3 2.8-11.8 6.3z'/><path fill='#ffc400' d='M16.4 9.2l3.3 1.8a1.1 1.1 0 0 1 0 2l-3.3 1.8-3-2.8z'/>")
    };
    var tints = {brand:'#1b5fa6', teal:'#0f766e', amber:'#b45309', violet:'#6d28d9', navy:'#14325c', green:'#15803d', ink:'#334155',
        facebook:'#1877f2', youtube:'#e62117', twitter:'#0f1419', apple:'#0d84ff', moodle:'#f98012', white:'#ffffff'};
    function glyphFor(href, title) {
        var url; try { url = new URL(href, location.href); } catch (_) { return ['link', 'brand']; }
        var host = url.hostname.toLowerCase(), p = url.pathname.toLowerCase(), t = (title || '').toLowerCase();
        if (url.origin === location.origin) {
            if (/^\/student\/myschedule/.test(p)) return ['calendar', 'brand'];
            if (/^\/student\/attendance/.test(p)) return ['grades', 'teal'];
            if (/^\/student\/transcript/.test(p)) return ['transcript', 'violet'];
            if (/^\/student\/umkd/.test(p)) return ['book', 'amber'];
            if (/moodle/.test(p)) return ['moodle', 'moodle'];
            if (/messages/.test(p)) return ['mail', 'brand'];
            if (/^\/ssc\/statementlist/.test(p)) return ['transcript', 'teal'];
            if (/^\/ssc/.test(p)) return ['services', 'brand'];
            if (/news|новост|жаңалық/.test(p + t)) return ['news', 'brand'];
            if (/faq|вопрос|сұрақ|question/.test(p + t)) return ['help', 'brand'];
            if (/calc|gpa/.test(p + t)) return ['calc', 'violet'];
            if (/catalog|каталог/.test(p + t)) return ['list', 'amber'];
            if (/doc|документ|құжат/.test(p + t)) return ['folder', 'amber'];
            if (/guide|руководств|нұсқаулы|manual/.test(p + t)) return ['book', 'brand'];
            return ['link', 'brand'];
        }
        if (/facebook\.com$/.test(host)) return ['facebook', 'facebook'];
        if (/youtube\.com$|youtu\.be$/.test(host)) return ['youtube', 'youtube'];
        if (/twitter\.com$|(^|\.)x\.com$/.test(host)) return ['twitter', 'twitter'];
        if (/apps\.apple\.com$/.test(host)) return ['appstore', 'apple'];
        if (/play\.google\.com$/.test(host)) return ['googleplay', 'white'];
        if (/moodle/.test(host + t)) return ['moodle', 'moodle'];
        if (/wifi|wi-fi/.test(host + p + t)) return ['wifi', 'green'];
        if (/kaizen|кайдзен/.test(host + t)) return ['idea', 'amber'];
        if (/oxford|journal|журнал/.test(host + t)) return ['journal', 'navy'];
        if (/lib|эбс|ebs|библиот|кітапхана|read\.kz|oqylyq/.test(host + t)) return ['open', 'amber'];
        if (/open\.kaznu|omc\.|master|видео|video/.test(host + t)) return ['play', 'brand'];
        return ['globe', 'brand'];
    }
    function linkIcon(href, title) {
        var pick = glyphFor(href, title), i = document.createElement('span');
        i.className = 'univer-app-link-icon'; i.setAttribute('aria-hidden', 'true');
        if (path(href) !== null) i.classList.add('univer-app-link-internal');
        i.style.setProperty('--tint', tints[pick[1]] || tints.brand);
        if (arts[pick[0]]) { i.classList.add('univer-app-link-art'); i.style.setProperty('--art', arts[pick[0]]); }
        else i.style.setProperty('--glyph', glyphs[pick[0]] || glyphs.link);
        return i;
    }
    function pinIcon(fill) {
        return 'url("data:image/svg+xml,' + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='" + (fill ? 'black' : 'none') +
            "' stroke='black' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M9 4h6l-1 6 4 4H6l4-4z'/><path d='M12 14v7'/></svg>") + '")';
    }
    function text() {
        return language === 'en' ? {useful:'Useful links', schedule:'Schedule', grades:'Grades', transcript:'Transcript', umkd:'Course materials', moodle:'Moodle LMS', eservices:'E-services', pin:'Pin to home screen', unpin:'Unpin from home screen', remove:'Unpin'} :
            language === 'kz' ? {useful:'Пайдалы сілтемелер', schedule:'Сабақ кестесі', grades:'Үлгерім', transcript:'Транскрипт', umkd:'ОӘК', moodle:'Moodle ҚОЖ', eservices:'Е-қызметтер', pin:'Басты бетке бекіту', unpin:'Басты беттен алып тастау', remove:'Бекітуден алу'} :
            {useful:'Полезные ссылки', schedule:'Расписание', grades:'Успеваемость', transcript:'Транскрипт', umkd:'УМКД', moodle:'СДО Moodle', eservices:'Е-услуги', pin:'Закрепить на главной', unpin:'Открепить с главной', remove:'Открепить'};
    }
    function own(node) { owned.push(node); return node; }
    function path(href) { try { var url = new URL(href, location.href); return url.origin === location.origin ? url.pathname.replace(/\/+$/, '') || '/' : null; } catch (_) { return null; } }
    function pinnable(a) {
        try { var url = new URL(a.href); } catch (_) { return false; }
        if (url.origin === location.origin) return !/^\/user\/(logout|login)\/?$/i.test(url.pathname);
        return (url.protocol === 'https:' || url.protocol === 'http:') && !url.username && !url.password;
    }
    function fixedHomeLink(href) {
        try { var url = new URL(href, location.href); } catch (_) { return false; }
        return url.origin === location.origin && !url.search && !url.hash && /^\/(student\/(myschedule|attendance\/full|transcript|umkd)|user\/loginMoodle|ssc(\/UserServiceList|\/StatementList)?)$/i.test(url.pathname.replace(/\/+$/, ''));
    }
    function pinned(href) { return pins.some(function (p) { return p.url === href; }); }
    function notify(value) {
        // Only the pin request crosses to native code, which validates and stores it; no callable host objects.
        if (typeof window.__univerNativeNotify === 'function') window.__univerNativeNotify(value);
        else if (window.CefSharp) CefSharp.PostMessage(value);
    }
    function request(event, pin, title, href) {
        event.preventDefault(); event.stopPropagation();
        // Scripted clicks and frames cannot change the student's home screen.
        if (!event.isTrusted || window !== window.top || (pin && fixedHomeLink(href))) return;
        if (pin && !pinned(href)) pins = pins.concat([{title:title, url:href}]);
        if (!pin) pins = pins.filter(function (p) { return p.url !== href; });
        notify('univer-pin:' + JSON.stringify({pin:pin, title:title, url:href}));
        render();
    }
    function usefulSource() {
        return Array.from(document.querySelectorAll('.links')).find(function (links) {
            return links.querySelectorAll('a').length >= 8 && links.querySelector('a[href*="apps.apple.com"]') && links.querySelector('a[href*="play.google.com"]');
        });
    }
    function pinButton(li, a, title) {
        if (fixedHomeLink(a.href)) return;
        var button = li.querySelector(':scope>.univer-app-pin');
        if (!button) {
            button = document.createElement('button'); button.type = 'button'; button.className = 'univer-app-pin';
            button.addEventListener('click', function (event) { request(event, button.getAttribute('aria-pressed') !== 'true', button.__univerTitle, button.__univerHref); });
            // Buttons of a rebuilt useful-links box are gone from the page; keep only live ones.
            pinButtons.forEach(function (old) { if (!old.isConnected) pinButtons.delete(old); });
            a.after(button); pinButtons.add(button);
        }
        var on = pinned(a.href), label = (on ? text().unpin : text().pin) + ': ' + title;
        button.__univerTitle = title; button.__univerHref = a.href;
        if (button.getAttribute('aria-pressed') !== String(on)) button.setAttribute('aria-pressed', String(on));
        if (button.getAttribute('aria-label') !== label) { button.setAttribute('aria-label', label); button.title = label; }
    }
    function menuPins(menu) {
        menu.querySelectorAll('#tools li a[href]').forEach(function (a) {
            if (!pinnable(a) || !cleanText(a)) return;
            var entry = menuRows.get(a);
            if (!entry) {
                var row = document.createElement('span'), label = document.createElement('span');
                row.className = 'univer-app-pin-row'; label.className = 'univer-app-menu-label';
                a.before(row); row.appendChild(a);
                while (a.firstChild) label.appendChild(a.firstChild);
                a.appendChild(label); entry = {row:row, label:label}; menuRows.set(a, entry);
            }
            pinButton(entry.row, a, cleanText(a));
        });
    }
    function usefulBox(menu) {
        var source = usefulSource(), box = menu.querySelector('.univer-app-useful');
        if (!source) { box?.remove(); return; }
        var anchors = Array.from(source.querySelectorAll('a[href]')).filter(function (a) { return cleanText(a) && pinnable(a); });
        var key = language + '|' + anchors.map(function (a) { return a.href + ' ' + cleanText(a); }).join('|');
        if (!box || box.__univerKey !== key) {
            box?.remove();
            box = own(document.createElement('details')); box.className = 'univer-app-category univer-app-useful'; box.open = true; box.__univerKey = key;
            var summary = document.createElement('summary'); summary.textContent = text().useful;
            var list = document.createElement('ul'); box.append(summary, list);
            anchors.forEach(function (original) {
                var li = document.createElement('li'), a = document.createElement('a');
                a.href = original.href; a.append(linkIcon(original.href, cleanText(original)), document.createTextNode(cleanText(original)));
                if (path(original.href) === null) { a.target = '_blank'; a.rel = 'noopener'; }
                // The site's own anchor keeps any handler and target it has.
                a.addEventListener('click', function (event) { event.preventDefault(); original.click(); });
                li.appendChild(a); list.appendChild(li);
            });
            // Kept outside #tools: the site menu's current-page marks and the window title read only real sections.
            menu.insertBefore(box, menu.querySelector(':scope>.univer-app-empty'));
            var search = menu.querySelector('input[type=search]');
            if (search && !search.__univerUseful) { search.__univerUseful = true; search.addEventListener('input', function () { filterUseful(menu); }); }
        }
        box.querySelectorAll('li').forEach(function (li) { var a = li.querySelector('a'); pinButton(li, a, cleanText(a)); });
        filterUseful(menu);
    }
    function filterUseful(menu) {
        var box = menu.querySelector('.univer-app-useful'); if (!box) return;
        var query = (menu.querySelector('input[type=search]')?.value || '').trim().toLocaleLowerCase().replace(/ё/g, 'е'), any = false;
        box.querySelectorAll('li').forEach(function (li) {
            var match = !query || li.textContent.toLocaleLowerCase().replace(/ё/g, 'е').includes(query);
            if (li.hidden === match) li.hidden = !match; any = any || match;
        });
        if (box.hidden !== !any) box.hidden = !any;
        if (query && any && !box.open) box.open = true;
        var empty = menu.querySelector(':scope>.univer-app-empty');
        if (empty && any && query && !empty.hidden) empty.hidden = true;
    }
    // The site's own menu link, looked up at click time: the menu can still be filling in when tiles are drawn.
    function siteLink(href) { return Array.from(document.querySelectorAll('.univer-app-menu #tools a[href]')).find(function (a) { return a.href === href || (path(href) !== null && path(a.href) === path(href) && new URL(a.href).search === new URL(href, location.href).search); }); }
    function tile(title, href, removable) {
        var span = document.createElement('span'); span.className = 'univer-app-tile';
        var a = document.createElement('a'), label = document.createElement('span'); a.href = href; label.className = 'univer-app-tile-label'; label.textContent = title;
        a.append(linkIcon(href, title), label); span.appendChild(a);
        if (path(href) === null) { a.target = '_blank'; a.rel = 'noopener'; span.classList.add('univer-app-external'); }
        a.addEventListener('click', function (event) { var original = siteLink(href); if (original) { event.preventDefault(); original.click(); } });
        var handle = document.createElement('button'); handle.type = 'button'; handle.className = 'univer-app-reorder'; handle.draggable = true;
        handle.title = handle.getAttribute('aria-label') || (language === 'en' ? 'Drag to reorder; arrow keys move the tile' : language === 'kz' ? 'Ретін өзгерту: сүйреңіз немесе бағыт пернелерін басыңыз' : 'Переставить: перетащите или используйте стрелки') + ': ' + title;
        handle.setAttribute('aria-label', handle.title);
        handle.addEventListener('click', function (event) { event.preventDefault(); event.stopPropagation(); });
        handle.addEventListener('dragstart', function (event) {
            if (!event.isTrusted || window !== window.top) { event.preventDefault(); return; }
            draggedTile = span; event.dataTransfer.effectAllowed = 'move'; event.dataTransfer.setData('text/plain', 'univer-tile'); span.classList.add('univer-app-dragging');
        });
        handle.addEventListener('dragend', function () { draggedTile = null; document.querySelectorAll('.univer-app-dragging,.univer-app-drop-target').forEach(function (node) { node.classList.remove('univer-app-dragging','univer-app-drop-target'); }); });
        span.addEventListener('dragover', function (event) { if (!draggedTile || draggedTile === span) return; event.preventDefault(); event.dataTransfer.dropEffect = 'move'; span.classList.add('univer-app-drop-target'); });
        span.addEventListener('dragleave', function (event) { if (!span.contains(event.relatedTarget)) span.classList.remove('univer-app-drop-target'); });
        span.addEventListener('drop', function (event) {
            if (!event.isTrusted || !draggedTile || draggedTile === span) return;
            event.preventDefault(); event.stopPropagation(); span.classList.remove('univer-app-drop-target');
            moveTile(draggedTile, Array.from(span.parentElement.querySelectorAll(':scope>.univer-app-tile')).indexOf(span));
        });
        handle.addEventListener('keydown', function (event) {
            if (!event.isTrusted || !['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(event.key)) return;
            event.preventDefault(); event.stopPropagation();
            var nodes = Array.from(span.parentElement.querySelectorAll(':scope>.univer-app-tile')), index = nodes.indexOf(span);
            moveTile(span, event.key === 'Home' ? 0 : event.key === 'End' ? nodes.length - 1 : index + (/Left|Up/.test(event.key) ? -1 : 1));
        });
        span.appendChild(handle);
        if (removable) {
            span.classList.add('univer-app-pinned');
            var remove = document.createElement('button'); remove.type = 'button'; remove.className = 'univer-app-unpin';
            var label = text().remove + ': ' + title; remove.setAttribute('aria-label', label); remove.title = label;
            remove.addEventListener('click', function (event) {
                event.preventDefault(); event.stopPropagation();
                if (!event.isTrusted || span.classList.contains('univer-app-leaving')) return;
                // The tile shrinks away first, then the pin is removed and the others close the gap.
                var done = function () { request({preventDefault:function () {}, stopPropagation:function () {}, isTrusted:true}, false, title, href); };
                if (event.detail === 0 || window.__univerMotion === false || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches || !span.animate) { done(); return; }
                span.classList.add('univer-app-leaving');
                var motion = span.animate([{opacity:1, transform:'none'}, {opacity:0, transform:'scale(.97)'}], {duration:140, easing:'cubic-bezier(.23,1,.32,1)', fill:'forwards'});
                motion.onfinish = motion.oncancel = done;
            });
            span.appendChild(remove);
        }
        return span;
    }
    function moveTile(node, index) {
        // A render during the drag can replace the dragged tile; a detached node has no order to change.
        if (!node || !node.isConnected || !node.parentElement) return;
        var nodes = Array.from(node.parentElement.querySelectorAll(':scope>.univer-app-tile')), from = nodes.indexOf(node);
        index = Math.max(0, Math.min(nodes.length - 1, index)); if (from < 0 || from === index) return;
        nodes.splice(from, 1); nodes.splice(index, 0, node);
        tileOrder = nodes.map(function (tile) { return tile.querySelector('a').href; }); orderKey = JSON.stringify(tileOrder);
        notify('univer-pin:' + JSON.stringify({order:tileOrder}));
        render();
    }
    function quickBar(menu) {
        var quick = document.querySelector('.univer-app-quick');
        if (!homePages.test(location.pathname)) { quick?.remove(); return; }
        var links = Array.from(menu.querySelectorAll('#tools a[href]')).filter(function (a) { return !a.closest('.univer-app-useful'); });
        var tiles = [];
        fixed.forEach(function (item) {
            var target = item[1].replace(/\/$/, ''), a = links.find(function (link) { return path(link.href) === target; });
            tiles.push({title:text()[item[0]], href:a ? a.href : location.origin + item[1]});
        });
        var moodle = Array.from(usefulSource()?.querySelectorAll('a[href]') || []).find(function (a) { return path(a.href) === moodlePath; });
        tiles.push({title:moodle ? cleanText(moodle) : text().moodle, href:location.origin + moodlePath});
        // One Е-услуги tile opens the services desk, where the catalogue and the student's requests live together.
        tiles.push({title:text().eservices, href:location.origin + '/ssc/'});
        var taken = tiles.map(function (t) { return path(t.href) ?? t.href; }).concat(['/ssc/UserServiceList', '/ssc/StatementList']);
        var extra = pins.filter(function (p) { return taken.indexOf(path(p.url) ?? p.url) < 0; });
        var key = language + '|' + JSON.stringify(tiles.map(function (t) { return [t.title, t.href]; })) + JSON.stringify(extra) + orderKey;
        if (quick && quick.__univerKey === key && quick.isConnected) return;
        var frame = document.querySelector('#l4>td.center') || document.querySelector('#l4>td');
        var body = frame?.querySelector(':scope>table.inner>tbody>tr>td.center') || frame;
        if (!body) return;
        if (body !== frame) mark(body, 'univer-app-home-body');
        if (!quick) { quick = document.createElement('nav'); quick.className = 'univer-app-quick'; owned.push(quick); }
        if (body.firstElementChild !== quick) body.prepend(quick);
        var first = !quick.__univerKey;
        quick.__univerKey = key; quick.setAttribute('aria-label', cleanText(menu.querySelector('summary')) || 'Univer');
        var previous = new Map(), oldUrls = new Set(), added = [], wanted = [];
        Array.from(quick.querySelectorAll(':scope>.univer-app-tile')).forEach(function (node) {
            previous.set(node.__univerTileKey, node); oldUrls.add(node.querySelector('a').href);
        });
        var append = function (title, href, removable) {
            var tileKey = JSON.stringify([title, href, removable]), node = previous.get(tileKey);
            if (!node) { node = tile(title, href, removable); node.__univerTileKey = tileKey; }
            wanted.push(node);
            if (!first && removable && !oldUrls.has(node.querySelector('a').href)) added.push(node);
        };
        tiles.forEach(function (t) { append(t.title, t.href, false); });
        extra.forEach(function (p) { append(p.title, p.url, true); });
        wanted.sort(function (a, b) {
            var first = tileOrder.indexOf(a.querySelector('a').href), second = tileOrder.indexOf(b.querySelector('a').href);
            return (first < 0 ? tileOrder.length : first) - (second < 0 ? tileOrder.length : second);
        });
        // Keep unchanged tiles (including keyboard focus) mounted when a pin arrives.
        var reconcile = function () {
            wanted.forEach(function (node, index) { if (quick.children[index] !== node) quick.insertBefore(node, quick.children[index] || null); });
            Array.from(quick.children).slice(wanted.length).forEach(function (node) { window.__univerMotionUI?.stop(node); node.remove(); });
        };
        if (window.__univerMotionUI) window.__univerMotionUI.reflow(quick, reconcile); else reconcile();
        if (first) Array.from(quick.querySelectorAll('.univer-app-tile')).forEach(function (node, index) {
            window.__univerMotionUI?.reveal(node, 'tile', Math.min(index, 3) * 24);
        });
        added.forEach(function (node) { window.__univerMotionUI?.reveal(node, 'tile'); });
    }

    // Tab pages: Главная has the tiles, Бакалавр leads with the GPA, Е-услуги switches between the catalogue and requests.
    var marked = new Set(), credentialSources = new Map(), hiddenPresentation = new Map();
    function hidePresentation(node) {
        if (!hiddenPresentation.has(node)) hiddenPresentation.set(node, {value:node.style.getPropertyValue('display'), priority:node.style.getPropertyPriority('display')});
        node.style.setProperty('display', 'none', 'important');
    }
    function wrapCredentialSource(host) {
        if (credentialSources.has(host)) return;
        var wrapper = document.createElement('div'); wrapper.className = 'univer-app-credential-original'; wrapper.style.setProperty('display','none','important');
        var children = Array.from(host.childNodes); host.appendChild(wrapper); children.forEach(function (node) { wrapper.appendChild(node); });
        credentialSources.set(host, wrapper);
    }
    function restoreCredentialSources() {
        credentialSources.forEach(function (wrapper, host) {
            if (wrapper.parentNode === host) { while (wrapper.firstChild) host.insertBefore(wrapper.firstChild, wrapper); wrapper.remove(); }
        });
        credentialSources.clear();
        hiddenPresentation.forEach(function (old, node) {
            if (old.value) node.style.setProperty('display', old.value, old.priority); else node.style.removeProperty('display');
            if (node.getAttribute('style') === '') node.removeAttribute('style');
        });
        hiddenPresentation.clear();
    }
    function hubText() {
        return language === 'en' ? {services:'Services', requests:'My requests', servicesHint:'Certificates, statements and other requests to the university', requestsHint:'Requests you have submitted and their status', find:'Find a service', none:'No services match', show:'Show', hide:'Hide', loading:'Loading…', copy:'Copy', copied:'Copied', login:'Login', password:'Password', nav:'E-services', gpa:'GPA'} :
            language === 'kz' ? {services:'Қызметтер', requests:'Менің өтінімдерім', servicesHint:'Анықтамалар, өтініштер және университетке басқа сұраулар', requestsHint:'Жіберілген өтінімдер және олардың күйі', find:'Қызметті табу', none:'Сәйкес қызмет жоқ', show:'Көрсету', hide:'Жасыру', loading:'Жүктелуде…', copy:'Көшіру', copied:'Көшірілді', login:'Логин', password:'Құпия сөз', nav:'Е-қызметтер', gpa:'GPA'} :
            {services:'Услуги', requests:'Мои заявки', servicesHint:'Справки, заявления и другие обращения в университет', requestsHint:'Поданные заявки и их статус', find:'Найти услугу', none:'Таких услуг нет', show:'Показать', hide:'Скрыть', loading:'Загрузка…', copy:'Копировать', copied:'Скопировано', login:'Логин', password:'Пароль', nav:'Е-услуги', gpa:'GPA'};
    }
    function mark(node, name, value) {
        if (value === undefined) { if (!node.classList.contains(name)) node.classList.add(name); }
        else if (node.getAttribute(name) !== value) node.setAttribute(name, value);
        marked.add(node);
    }
    function content(node) { return node && !node.closest('#tools,.univer-app-menu,.univer-app-header,.univer-app-quick,#l5,.univer-app-overview,.univer-app-overview-grid,.univer-app-credentials,.univer-app-ssc-nav'); }
    function tabIcons() {
        document.querySelectorAll('.univer-app-tabs>a[href]').forEach(function (a) {
            var p = path(a.href) || '';
            // Е-услуги moved to a home tile; the header keeps the two big study tabs.
            if (/^\/ssc/i.test(p)) { mark(a, 'univer-app-tab-hidden'); return; }
            var glyph = /^\/student\/bachelor/i.test(p) ? glyphs.cap : /^\/news|^\/$/.test(p) ? glyphs.home : null;
            if (!glyph) return;
            mark(a, 'univer-app-tab-icon'); if (a.style.getPropertyValue('--glyph') !== glyph) a.style.setProperty('--glyph', glyph);
        });
    }
    function glyphIcon(glyph, tint) {
        var i = document.createElement('span'); i.className = 'univer-app-link-icon'; i.setAttribute('aria-hidden', 'true');
        i.style.setProperty('--glyph', glyph); i.style.setProperty('--tint', tint); return i;
    }
    // Site sections on Бакалавр and Е-услуги are "heading row + content row" tables; they become titled panels.
    function panels() {
        if (!/^\/student\/bachelor\/?$|^\/ssc(\/|$)/i.test(location.pathname)) return;
        document.querySelectorAll('#l4 table.mt').forEach(function (table) {
            if (!content(table) || table.querySelector('table.mt')) return;
            var cells = Array.from(table.querySelectorAll('td.ct,td.tt')).filter(function (c) { return c.closest('table') === table; });
            if (cells.length < 2) return;
            // One site table can hold several sections (e.g. "Личные данные" then "Обучение"): every short title
            // followed by content becomes a section header inside the same panel.
            var heads = cells.filter(function (cell, i) {
                var title = cleanText(cell);
                return i < cells.length - 1 && title && (title.length <= 60 || /print\.kaznu|wi-?fi/i.test(title)) && !cell.querySelector('table,form,input,select,ul,ol,img,a,br,h1,h2,h3,h4,h5,h6,p,div');
            });
            if (!heads.length || heads[0] !== cells[0]) return;
            mark(table, 'univer-app-panel');
            cells.forEach(function (cell) {
                if (heads.indexOf(cell) < 0) { mark(cell, 'univer-app-panel-body'); return; }
                var t = cleanText(cell).toLocaleLowerCase();
                var pick = /личн|жеке|personal/.test(t) ? ['person', 'violet'] : /бюллет|bulletin|хабарланд/.test(t) ? ['news', 'amber'] : /обуч|оқу|study|learning/.test(t) ? ['cap', 'brand'] :
                    /услуг|қызмет|service/.test(t) ? ['services', 'teal'] : ['book', 'brand'];
                mark(cell, 'univer-app-panel-head');
                if (!cell.querySelector(':scope>.univer-app-link-icon')) cell.prepend(own(glyphIcon(glyphs[pick[0]], tints[pick[1]])));
            });
        });
    }
    // Login and password from the site's sign-in line; line breaks count as spaces so a login never runs into "Пароль".
    function readCredentials(node) {
        var parts = [], walk = (node.ownerDocument || document).createTreeWalker(node, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
        for (var n = walk.nextNode(); n; n = walk.nextNode()) parts.push(n.nodeType === 3 ? n.nodeValue : /^(BR|TD|TH|TR|P|DIV)$/.test(n.nodeName) ? ' ' : '');
        var text = parts.join('').replace(/\s+/g, ' ').trim();
        if (!/парол|password|құпия/i.test(text) || !/логин|login|print\.kaznu|wi-?fi/i.test(text)) return null;
        var login = text.match(/(?:логин|login)\s*:\s*(\S+)/i), secret = text.match(/(?:пароль|password|құпия\s*сөз)\s*:\s*(\S+)/i);
        if (!login || !secret) return null;
        return {heading:text.slice(0, text.search(/(?:логин|login)\s*:/i)).replace(/[\s.:]+$/, ''), login:login[1], secret:secret[1]};
    }
    // The panel keeps the values only in its own closures: nothing is logged or stored.
    function credentialPanel(found, withTitle) {
        var panel = own(document.createElement('section')); panel.className = 'univer-app-credentials'; panel.setAttribute('aria-label', found.heading);
        if (withTitle) { var head = document.createElement('h3'); head.append(glyphIcon(glyphs.wifi, tints.green), document.createTextNode(found.heading)); panel.appendChild(head); }
        [[hubText().login, found.login, false], [hubText().password, found.secret, true]].forEach(function (item) {
            var row = document.createElement('div'); row.className = 'univer-app-credential';
            var label = document.createElement('span'); label.className = 'univer-app-credential-label'; label.textContent = item[0];
            var value = document.createElement('span'); value.className = 'univer-app-credential-value';
            var actions = document.createElement('span'); actions.className = 'univer-app-credential-actions';
            var shown = !item[2], show = null;
            var paint = function () { value.textContent = shown ? item[1] : '••••••••'; value.classList.toggle('univer-app-credential-hidden', !shown); };
            var copy = document.createElement('button'); copy.type = 'button'; copy.className = 'univer-app-credential-button univer-app-credential-copy';
            var labels = function () {
                if (show) { show.textContent = shown ? hubText().hide : hubText().show; show.setAttribute('aria-pressed', String(shown)); show.setAttribute('aria-label', (shown ? hubText().hide : hubText().show) + ': ' + item[0]); }
                if (!copy.__univerDone) { copy.textContent = hubText().copy; copy.setAttribute('aria-label', hubText().copy + ': ' + item[0]); }
            };
            if (item[2]) {
                show = document.createElement('button'); show.type = 'button'; show.className = 'univer-app-credential-button univer-app-credential-show';
                show.addEventListener('click', function () { shown = !shown; paint(); labels(); });
                actions.appendChild(show);
            }
            copy.addEventListener('click', function () {
                var done = function () { copy.__univerDone = true; copy.textContent = hubText().copied; copy.classList.add('univer-app-copied'); clearTimeout(copy.__univerTimer);
                    copy.__univerTimer = setTimeout(function () { copy.__univerDone = false; copy.classList.remove('univer-app-copied'); labels(); }, 1600); };
                var fallback = function () { var area = document.createElement('textarea'); area.value = item[1]; area.setAttribute('readonly', ''); area.style.cssText = 'position:fixed;opacity:0;left:-9999px';
                    document.body.appendChild(area); area.select(); try { document.execCommand('copy'); done(); } catch (_) { } area.remove(); copy.focus(); };
                if (navigator.clipboard?.writeText) navigator.clipboard.writeText(item[1]).then(done, fallback); else fallback();
            });
            actions.appendChild(copy); paint(); labels();
            row.append(label, value, actions); panel.appendChild(row);
        });
        return panel;
    }
    function wifiTitle(title) {
        var icon = title?.querySelector(':scope>.univer-app-link-icon');
        if (icon) { icon.style.setProperty('--glyph', glyphs.wifi); icon.style.setProperty('--tint', tints.green); }
    }
    function credentials() {
        // The sign-in line is a list item on Е-услуги, or a whole cell under its own section title.
        document.querySelectorAll('li, #l4 td:is(.ct,.tt):not(.univer-app-panel-head)').forEach(function (li) {
            if (li.closest('#tools,.univer-app-menu,.univer-app-quick,.univer-app-overview,.univer-app-credentials,.univer-app-credential-original,.univer-app-useful,.univer-app-ssc-nav') ||
                li.classList.contains('univer-app-credentials-source') || li.classList.contains('univer-app-credentials-host')) return;
            var cell = li.tagName === 'TD';
            if (cell && li.querySelector('form')) return;
            var found = readCredentials(li); if (!found) return;
            var cells = cell ? Array.from(li.closest('table').querySelectorAll('td.ct,td.tt')).filter(function (c) { return c.closest('table') === li.closest('table'); }) : [], title = cells[cells.indexOf(li) - 1];
            if (!/print\.kaznu|wi-?fi/i.test(found.heading) && !(cell && title && /print\.kaznu|wi-?fi/i.test(cleanText(title)))) return;
            if (title && title.classList.contains('univer-app-panel-head')) { wifiTitle(title); if (!found.heading) found.heading = cleanText(title).replace(/[\s.:]+$/, ''); }
            // The original line keeps its place and comes back in Original mode.
            mark(li, cell ? 'univer-app-credentials-host' : 'univer-app-credentials-source');
            var panel = credentialPanel(found, !title);
            if (cell) { wrapCredentialSource(li); li.appendChild(panel); }
            else { hidePresentation(li); (li.closest('ul,ol') || li).before(panel); }
        });
        // Бакалавр shows only the section title; the values live on the Е-услуги page and are read from there.
        if (!/^\/student\/bachelor\/?$/i.test(location.pathname)) return;
        // Keep everything after the Wi-Fi section in the source DOM for Original mode.
        document.querySelectorAll('#l4 td.ct,#l4 td.tt').forEach(function (title) {
            if (!content(title) || title.closest('.univer-app-credential-original') || !/print\.kaznu|wi-?fi/i.test(cleanText(title)) || /(?:логин|login|пароль|password)\s*:/i.test(cleanText(title))) return;
            var table = title.closest('table'), cells = Array.from(table.querySelectorAll('td.ct,td.tt')).filter(function (c) { return c.closest('table') === table; });
            var body = cells[cells.indexOf(title) + 1];
            if (!body) return;
            for (var next = body.closest('tr')?.nextElementSibling; next; next = next.nextElementSibling) { mark(next, 'univer-app-after-credentials'); hidePresentation(next); }
            var root = document.getElementById('l4');
            for (var cursor = table; cursor && cursor !== root && root.contains(cursor); cursor = cursor.parentElement) {
                for (var below = cursor.nextElementSibling; below; below = below.nextElementSibling) { mark(below, 'univer-app-after-credentials'); hidePresentation(below); }
            }
            if (!body || cleanText(body) || body.classList.contains('univer-app-credentials-host') || body.querySelector('table,form,input')) return;
            wifiTitle(title); mark(body, 'univer-app-credentials-host'); wrapCredentialSource(body);
            var pending = own(document.createElement('p')); pending.className = 'univer-app-pending univer-app-credentials'; pending.textContent = hubText().loading; body.appendChild(pending);
            var reader = window.__univerOverviewV1?.page; if (!reader) { pending.remove(); return; }
            reader('/ssc/').then(function (doc) {
                pending.remove(); if (!body.classList.contains('univer-app-credentials-host')) return;
                var source = Array.from(doc.querySelectorAll('li')).map(readCredentials).find(Boolean);
                if (source) { if (!source.heading) source.heading = cleanText(title); body.appendChild(credentialPanel(source, false)); }
            }, function () { pending.remove(); });
        });
    }
    function gpaCards() {
        var box = document.querySelector('.univer-app-gpa');
        if (!/^\/student\/bachelor\/?$/i.test(location.pathname)) { box?.remove(); return; }
        var cell = Array.from(document.querySelectorAll('#l4 .ct.message')).find(function (c) {
            var items = Array.from(c.querySelectorAll('li')); return content(c) && items.length === 2 && items.every(function (li) { return /G[PР][AА]\s*[=:]/i.test(cleanText(li)); });
        });
        if (!cell) { box?.remove(); return; }
        var values = Array.from(cell.querySelectorAll('li')).map(function (li) { var m = cleanText(li).match(/^(.*?)\s*[=:]\s*(\S+)$/); return m ? [m[1], m[2]] : null; });
        if (values.some(function (v) { return !v; })) { box?.remove(); return; }
        var key = JSON.stringify(values);
        if (!box || box.__univerKey !== key) {
            box?.remove(); box = own(document.createElement('div')); box.className = 'univer-app-gpa'; box.__univerKey = key; box.setAttribute('role', 'group'); box.setAttribute('aria-label', hubText().gpa);
            values.forEach(function (v) { var card = document.createElement('div'), b = document.createElement('b'), label = document.createElement('span'); b.textContent = v[1]; label.textContent = v[0]; card.append(b, label); box.appendChild(card); });
            (cell.closest('table.mt') || cell).before(box);
        }
        mark(cell, 'univer-app-gpa-source');
        var sourceTable = cell.closest('table.mt');
        if (sourceTable && !sourceTable.querySelector('form,input,select,textarea,button,a') &&
            Array.from(sourceTable.querySelectorAll('td')).every(function (td) { return td === cell || !cleanText(td); })) mark(sourceTable, 'univer-app-gpa-source-table');
    }
    function serviceDesk() {
        var nav = document.querySelector('.univer-app-ssc-nav');
        if (!/^\/ssc(\/|$)/i.test(location.pathname)) { nav?.remove(); return; }
        var t = hubText(), list = location.origin + '/ssc/UserServiceList', mine = location.origin + '/ssc/StatementList';
        var key = language + location.pathname;
        if (!nav || nav.__univerKey !== key) {
            // Every layout pass calls this; build only once the page offers a place for the switch.
            var anchor = document.querySelector('#l4>td'); if (!anchor) return;
            nav?.remove(); nav = own(document.createElement('nav')); nav.className = 'univer-app-ssc-nav'; nav.__univerKey = key; nav.setAttribute('aria-label', t.nav);
            [[list, t.services, glyphs.services], [mine, t.requests, glyphs.transcript]].forEach(function (item) {
                var a = document.createElement('a'); a.href = item[0]; a.textContent = item[1]; a.style.setProperty('--glyph', item[2]);
                if (path(item[0]).toLowerCase() === location.pathname.replace(/\/+$/, '').toLowerCase()) a.setAttribute('aria-current', 'page');
                // The site's own link keeps its handler when there is one on the page.
                a.addEventListener('click', function (event) { var original = Array.from(document.querySelectorAll('#l4 a[href]')).find(function (o) { return o !== a && !o.closest('.univer-app-ssc-nav') && path(o.href) === path(item[0]); }); if (original) { event.preventDefault(); original.click(); } });
                nav.appendChild(a);
            });
            anchor.prepend(nav);
        }
        // On the desk itself the site's two buttons become cards that say what is behind them.
        document.querySelectorAll('#l4 a[href]').forEach(function (a) {
            if (!content(a) || a.closest('.univer-app-ssc-nav') || !/^\/ssc\/?$/i.test(location.pathname)) return;
            var p = path(a.href), hint = /^\/ssc\/UserServiceList$/i.test(p) ? t.servicesHint : /^\/ssc\/StatementList$/i.test(p) ? t.requestsHint : null;
            if (!hint) return;
            mark(a, 'univer-app-service-card'); mark(a, 'data-univer-hint', hint);
            if (!a.querySelector(':scope>.univer-app-link-icon')) a.prepend(own(linkIcon(a.href, cleanText(a))));
        });
        // The live desk has no buttons of its own; it gets two cards that say what each half of Е-услуги holds.
        var desk = /^\/ssc\/?$/i.test(location.pathname), cards = document.querySelector('.univer-app-desk-cards');
        if (desk && !document.querySelector('#l4 .univer-app-service-card') && (!cards || cards.__univerKey !== language)) {
            cards?.remove(); cards = own(document.createElement('div')); cards.className = 'univer-app-desk-cards'; cards.__univerKey = language;
            [[list, t.services, t.servicesHint], [mine, t.requests, t.requestsHint]].forEach(function (item) {
                var a = document.createElement('a'); a.href = item[0]; a.className = 'univer-app-service-card univer-app-desk-card'; a.setAttribute('data-univer-hint', item[2]);
                a.append(linkIcon(item[0], item[1]), document.createTextNode(item[1])); cards.appendChild(a);
            });
            nav.after(cards);
        }
        if (!desk) cards?.remove();
        if (nav.hidden !== desk) nav.hidden = desk;
        serviceSearch(); requestStatuses(); credentials();
    }
    function mainTable() {
        return Array.from(document.querySelectorAll('#l4 table')).filter(function (table) { return content(table) && table.rows.length > 2 && !table.querySelector('table'); })
            .sort(function (a, b) { return b.rows.length - a.rows.length; })[0];
    }
    function serviceSearch() {
        var input = document.querySelector('.univer-app-service-search');
        var table = /^\/ssc\/UserServiceList\/?$/i.test(location.pathname) ? mainTable() : null;
        if (!table) { input?.remove(); document.querySelector('.univer-app-service-empty')?.remove(); return; }
        mark(table, 'univer-app-service-table');
        if (!input) {
            input = own(document.createElement('input')); input.type = 'search'; input.className = 'univer-app-service-search univer-app-catalog-search';
            var empty = own(document.createElement('p')); empty.className = 'univer-app-service-empty'; empty.hidden = true; empty.setAttribute('role', 'status');
            input.addEventListener('input', function () { filterServices(); });
            table.before(input, empty);
        }
        if (input.placeholder !== hubText().find + '…') { input.placeholder = hubText().find + '…'; input.setAttribute('aria-label', hubText().find); }
        filterServices();
    }
    function filterServices() {
        var input = document.querySelector('.univer-app-service-search'), table = mainTable(); if (!input || !table) return;
        var query = input.value.trim().toLocaleLowerCase().replace(/ё/g, 'е'), shown = 0;
        Array.from(table.rows).forEach(function (row) {
            if (row.querySelector('th')) return;
            var hide = !!query && !cleanText(row).toLocaleLowerCase().replace(/ё/g, 'е').includes(query);
            if (row.classList.contains('univer-app-service-hidden') !== hide) row.classList.toggle('univer-app-service-hidden', hide);
            marked.add(row); if (!hide) shown++;
        });
        var empty = document.querySelector('.univer-app-service-empty');
        if (empty) { empty.textContent = hubText().none; if (empty.hidden !== (!query || shown > 0)) empty.hidden = !query || shown > 0; }
    }
    function requestStatuses() {
        var table = /^\/ssc\/StatementList\/?$/i.test(location.pathname) ? mainTable() : null; if (!table) return;
        // A click on a request row opens it, like the site's double click; the checkbox and links keep their own clicks.
        if (!table.__univerOpen) {
            table.__univerOpen = function (event) {
                if (!event.isTrusted || event.target.closest('input,a,button,label,select')) return;
                var row = event.target.closest('tr[id]'); if (!row || !/^\d{1,12}$/.test(row.id)) return;
                location.href = '/ssc/StatementPage?statementId=' + row.id;
            };
            table.addEventListener('click', table.__univerOpen); marked.add(table);
        }
        mark(table, 'univer-app-request-table');
        Array.from(table.rows).forEach(function (row) {
            var cell = Array.from(row.cells).find(function (c) { return /статус|күй|status/i.test(c.getAttribute('data-univer-field') || ''); }) || row.cells[row.cells.length - 1], value = cleanText(cell).toLocaleLowerCase();
            if (!cell || cell.tagName !== 'TD' || !value || value.length > 40) return;
            var tone = /заверш|выполн|готов|выдан|аяқта|орында|дайын|complete|done|ready|issued/.test(value) ? 'done' :
                /отклон|отказ|аннул|бас тарт|reject|declin|cancel/.test(value) ? 'fail' : /обработ|рассмотр|нов|ожида|қаралу|өңде|process|review|pending|new/.test(value) ? 'wait' : null;
            if (!tone) return;
            mark(cell, 'univer-app-status'); mark(cell, 'data-univer-tone', tone);
        });
    }
    function hubs() {
        tabIcons(); panels(); gpaCards(); serviceDesk();
        if (/^\/student\/bachelor\/?$/i.test(location.pathname)) credentials();
        window.__univerOverviewV1?.apply(language);
    }
    function releaseHubs() {
        marked.forEach(function (node) {
            ['univer-app-tab-icon','univer-app-tab-hidden','univer-app-panel','univer-app-panel-head','univer-app-panel-body','univer-app-gpa-source','univer-app-gpa-source-table','univer-app-home-body','univer-app-service-card','univer-app-service-hidden','univer-app-status','univer-app-credentials-source','univer-app-credentials-host','univer-app-after-credentials'].forEach(function (name) { node.classList.remove(name); });
            ['data-univer-hint','data-univer-tone'].forEach(function (name) { node.removeAttribute(name); });
            node.classList.remove('univer-app-request-table', 'univer-app-service-table');
            if (node.__univerOpen) { node.removeEventListener('click', node.__univerOpen); node.__univerOpen = null; }
            ['--glyph'].forEach(function (name) { node.style.removeProperty(name); });
            if (node.getAttribute('style') === '') node.removeAttribute('style');
        });
        marked.clear();
    }
    function render() {
        if (!active) return;
        if (!style || !style.isConnected) {
            style = document.createElement('style'); style.id = 'univer-shortcuts-style';
            style.textContent = ':root{--univer-pin-icon:' + pinIcon(false) + ';--univer-pinned-icon:' + pinIcon(true) + ';--univer-close-icon:' + svg("<path d='M6 6l12 12M18 6L6 18'/>") +
                ';--univer-reorder-icon:' + svg("<circle cx='8' cy='6' r='1'/><circle cx='16' cy='6' r='1'/><circle cx='8' cy='12' r='1'/><circle cx='16' cy='12' r='1'/><circle cx='8' cy='18' r='1'/><circle cx='16' cy='18' r='1'/>") +
                ';--univer-doc-icon:' + svg("<path d='M14 3H6v18h12V7z'/><path d='M14 3v4h4M9 13h6M9 17h6'/>") + ';--univer-eye-icon:' + svg("<path d='M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z'/><circle cx='12' cy='12' r='3'/>") + ';}' + css;
            (document.head || document.documentElement).appendChild(style);
        }
        document.documentElement.classList.toggle('univer-app-home', homePages.test(location.pathname));
        hubs();
        var menu = document.querySelector('.univer-app-menu');
        if (!menu) { document.querySelector('.univer-app-quick')?.remove(); return; }
        menuPins(menu); usefulBox(menu); quickBar(menu);
    }
    window.__univerShortcutsV1 = {
        apply: function (lang) { language = lang || 'ru'; active = true; render(); },
        configure: function (list, order) {
            var next = Array.isArray(list) ? list.filter(function (p) { return p && typeof p.title === 'string' && typeof p.url === 'string' && !fixedHomeLink(p.url); }) : [];
            var key = JSON.stringify(next);
            var nextOrder = Array.isArray(order) ? order.filter(function (url) { return typeof url === 'string'; }) : tileOrder;
            var nextOrderKey = JSON.stringify(nextOrder);
            if (key === pinsKey && nextOrderKey === orderKey) return;
            pins = next; pinsKey = key; tileOrder = nextOrder; orderKey = nextOrderKey; render();
        },
        release: function () {
            active = false;
            draggedTile = null;
            restoreCredentialSources();
            pinButtons.forEach(function (button) { button.remove(); }); pinButtons.clear();
            menuRows.forEach(function (entry, a) {
                while (entry.label.firstChild) a.insertBefore(entry.label.firstChild, entry.label);
                entry.label.remove(); entry.row.before(a); entry.row.remove();
            }); menuRows.clear();
            document.documentElement.classList.remove('univer-app-home');
            owned.forEach(function (node) { window.__univerMotionUI?.stop(node); node.remove(); }); owned = [];
            document.querySelectorAll('.univer-app-desk-cards,.univer-app-quick,.univer-app-useful,.univer-app-gpa,.univer-app-ssc-nav,.univer-app-service-search,.univer-app-service-empty,.univer-app-credentials').forEach(function (node) { node.remove(); });
            releaseHubs(); window.__univerOverviewV1?.release();
            style?.remove(); style = null;
        }
    };
})();
