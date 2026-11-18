// Synthetic site pages for checking the extension in a real browser, built like the Windows QA fixtures
// (UI/Qa/MainForm.PolishQa.cs PolishShell, MainForm.StudyQa.cs, MainForm.SyllabusQa.cs, MainForm.UmkdQa.cs).
// No real student data; the names are made up.

const header = (lang = 'ru') => {
    const flag = code => code === lang ? `<img src='/img/${code}.png'>` : `<a href='/lang/change/${code}/'><img src='/img/${code}.png'></a>`;
    return "<tr id='l1'><td class='header'>КазНУ" + flag('kz') + flag('ru') + flag('en') + '</td></tr>' +
        "<tr id='l2'><td><div id='logo'></div><div class='links'><a href='/user/pass/'>Профиль</a><a href='/user/logout'>Выход</a></div></td></tr>";
};
const tabs = active => "<tr id='l3'><td class='center'><a " + (active === 'home' ? "class='active' " : '') + "href='/news/student'><div class='center'><span>Главная</span></div></a>" +
    '<a ' + (active === 'bachelor' ? "class='active' " : '') + "href='/student/bachelor/'><div class='center'>Бакалавр</div></a></td></tr>";
const menu = "<tr id='qa-menu-row'><td><table class='inner'><tr><td class='center'><div id='menu-home'><table id='tools'><tr><td class='center'><table class='tool'><tr><td><ul id='study'>" +
    "<li><a href='/student/umkd/'>УМКД</a></li><li><a href='/student/myschedule/'>Расписание</a></li><li><a href='/student/transcript/'>Транскрипт</a></li>" +
    "<li><a href='/student/attendance/full/'>Журнал посещаемости</a></li><li><a href='/student/iup/'>Индивидуальный учебный план</a></li></ul></td></tr><tr><td class='bottom-center'>Главная</td></tr></table></td></tr></table></div></td></tr></table></td></tr>";
const l4 = body => "<tr id='l4'><td><table class='inner'><tr><td class='left'></td><td class='center'>" + body + "</td><td class='right'></td></tr></table></td></tr>";
const shell = (body, { lang = 'ru', active = 'bachelor', withMenu = false } = {}) =>
    "<style>table{border-collapse:collapse;border-spacing:0}td{padding:0}</style><table class='wrappertable'>" + header(lang) + tabs(active) + (withMenu ? menu : '') + l4(body) + '</table>';
const panel = rows => "<table class='mt'>" + rows + "<tr class='bot'><td class='lt'></td><td class='ct'></td><td class='rt'></td></tr></table>";
const row = (cell, kind = 'mid') => "<tr class='" + kind + "'><td class='lt'></td>" + cell + "<td class='rt'></td></tr>";

function lesson(id, name, total) {
    return "<tr><td class='lt'></td><td class='ts'><a class='lt' href='/student/attendance/show/123/2026/1'>" + name + " — Преподаватель QA (Русский)</a><a class='rt syllabus-info' href='#'>Интерактивный силлабус</a></td><td class='rt'></td></tr>" +
        "<tr><td colspan='3'><div class='inner'><table class='inner' id='qa-" + id + "'><tr><th rowspan='2'>РК1 (100)</th><th colspan='2'>06.10</th><th></th><th>Сумма</th></tr><tr><td>2</td><td>3</td><td></td><td>" + total + '</td></tr></table></div></td></tr>';
}
function course(index, name, first, second) {
    return "<tr><td class='ct'>" + name + ' ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>' + lesson('c' + index, 'семинар', first) +
        "<tr><td class='ct'><b>РК1 (100): </b>" + first + ' <b>РК2 (100): </b>' + second + '</td></tr>';
}
function scheduleLesson(name, type, teacher) {
    return "<td class='field'><div class='groups'><div><p class='teacher'>" + name + '(' + type + ")</p><p class='teacher'>" + teacher + "</p><p class='params'><span class='aud_faculty'>ФИТ</span><span>Ауд.: 309</span></p></div></div></td>";
}
function schedule() {
    const empty = "<td class='field'></td>";
    const rowOf = (time, cells) => "<tr><td class='time'><div class='divIE' style='display:none'>" + time + '</div></td>' + cells + '</tr>';
    return "<select id='weekSelect'><option value='/student/myschedule/2026/1/4/' selected>Неделя 04</option></select>" +
        "<div class='inner'><table class='schedule'><tr><th>&nbsp;</th><th>ПН</th><th>ВТ</th><th>СР</th><th>ЧТ</th><th>ПТ</th><th>СБ</th></tr>" +
        rowOf('09:00-09:50', scheduleLesson('Политология', 'лекция', 'Учителева А. П.') + scheduleLesson('Культурология', 'лекция', 'Незнакомов Н. Н.') + scheduleLesson('Политология', 'семинар', 'Учителева А. П.') + scheduleLesson('Культурология', 'семинар', 'Незнакомов Н. Н.') + empty + empty) +
        rowOf('10:00-10:50', scheduleLesson('Политология', 'семинар', 'Помощников П. П.') + scheduleLesson('Иностранный язык', 'семинар', 'Языкова Я. Я.') + scheduleLesson('Политология', 'семинар', 'Учителева А. П.') + empty + empty + empty) +
        '</table></div>';
}
function umkd() {
    const file = (id, name) => "<tr class='file' id='" + id + "'><td><img src='/img/file.png'></td><td><a class='downLoad' id='" + id + "' href='/student/umkd/get/" + id + "/321/2026/1'>" + name + '</a></td>' +
        "<td class='description'></td><td>1</td><td class='description'>34,68 Кб</td><td class='description'>21.09.2026</td><td class='description'><div class='stars'></div></td></tr>";
    return panel(row("<td class='ct'><a href='/student/umkd/0/2026/1'>УМКД</a> \\ <a href='/student/umkd/321/2026/1'>Социология</a></td>", 'top') +
        row("<td class='ct'>Преподаватель: Синтетический П. П.</td>", 'brk') +
        row("<td class='tt'><div class='inner'><table class='inner'><tr><th width='3%'></th><th>Название файла</th><th>Описание</th><th>Язык. гр.</th><th>Размер</th><th>Дата загрузки</th><th>Оцените качество материала</th></tr>" +
            "<tr><th colspan='7'>Силлабус</th></tr>" + file(9001, 'Силлабус Социология.docx') +
            "<tr><th colspan='7'>Конспекты лекций</th></tr>" + file(9002, 'Лекция 1. Введение.pdf') + file(9003, 'Лекция 2. Методы.pdf') + file(9004, 'Лекция 3. Общество.pdf') +
            '</table></div></td>'));
}

const names = ['Социология', 'Психология', 'Культурология', 'Политология', 'История Казахстана'];
const marks = ['87', '90', '63', '49.95', '0'];

export const pages = {
    '/user/login/': () => shell("<form id='login_frm' action='/user/login' method='post'><table><tr><td>Логин</td><td><input type='text' name='login' id='login'></td></tr>" +
        "<tr><td>Пароль</td><td><input type='password' name='password' id='password'></td></tr><tr><td></td><td><input type='submit' value='Войти'></td></tr></table></form>", { active: '' }),
    '/student/bachelor/': () => shell("<table class='mt' id='qa-gpa-source'><tr class='top'><td class='ct message'><ul><li>Переводной GPA = 2.85</li><li>Общий GPA = 3.12</li></ul></td></tr></table>", { withMenu: true }),
    '/student/bachelor/kz/': () => shell("<table class='mt'><tr class='top'><td class='ct message'><ul><li>GPA = 3.12</li></ul></td></tr></table>", { lang: 'kz', withMenu: true }),
    '/student/myschedule/': () => shell(schedule()),
    '/student/attendance/full/2026/1/': () => shell("<table class='mt'><tr><td class='ct'>Журнал посещений и успеваемости</td></tr>" + names.map((name, i) => course(i, name, marks[i], i % 2 ? '15' : '0')).join('') + '</table>'),
    '/student/umkd/321/2026/1/': () => shell(umkd()),
    '/student/transcript/': () => "<table class='wrappertable'><tr id='l4'><td class='center'><h3>Транскрипт</h3><table class='inner'><tr><th>Дисциплина</th><th>Оценка</th></tr><tr><td>Социология</td><td>A</td></tr></table></td></tr></table>",
};

// Forms the extension confirms by itself (server.mjs shows them until they are sent): the read notice with the site's
// own calc() and the agreement modal with a stand-in for its jQuery 1.2.6 (bind, the ajaxSuccess event) and agree().
export const notice = () => shell("<form id='attentionSt' method='post' action='/student/attentionStudents/77'><h3>Уведомление</h3>" +
    '<p>Уважаемый студент! Синтетический текст уведомления для проверки расширения.</p>' +
    "<input type='checkbox' id='check' onclick='calc()'><label for='check'>Я подтверждаю, что прочёл данное уведомление</label><br>" +
    "<input type='submit' id='btn' value='Отправить' disabled></form>" +
    "<script>function calc(){document.getElementById('btn').disabled=!document.getElementById('check').checked;}</script>");
export const agreement = () => shell("<table class='mt'><tr class='top'><td class='ct message'>Новости</td></tr></table>" +
    "<div id='modal_agreement' style='position:fixed;left:20%;top:20%;width:60%;background:#fff;color:#000;border:1px solid #888;padding:20px;z-index:10'>" +
    "<form id='agree_frm' action='/user/setagree' method='post'><p>Договор об оказании образовательных услуг (синтетический).</p>" +
    "<input type='checkbox' id='iconfirm' onchange=\"document.querySelector('.btn-confirm').disabled=!this.checked\"><label for='iconfirm'>Прочитал договор</label> " +
    "<button type='button' class='btn-confirm' onclick='agree()' disabled>Согласен</button></form></div>" +
    '<script>(function(){var hooks={};window.jQuery=function(){return {bind:function(name,fn){(hooks[name]=hooks[name]||[]).push(fn);return this;}};};' +
    "window.agree=function(){var x=new XMLHttpRequest();x.open('POST','/user/setagree');x.onload=function(){(hooks['ajaxSuccess.univerApp']||[]).forEach(function(f){f(null,x,{url:'/user/setagree',type:'POST'});});" +
    "var m=document.getElementById('modal_agreement');if(m)m.remove();};x.send('agree=1');};})();</script>");

export function render(path) {
    const make = typeof path === 'function' ? path : pages[path] || pages[path + '/'];
    return make ? '<!doctype html><html><head><meta charset="utf-8"><title>Univer QA</title></head><body>' + make() + '</body></html>' : null;
}
