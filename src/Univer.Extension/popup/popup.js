/* The extension's settings. A change is stored at once; open pages of the site follow it (host.js listens to storage).
   The popup speaks the language the site was last shown in, else the browser's. */
(function () {
    'use strict';
    var api = globalThis.chrome;
    var DEFAULTS = { readable: true, theme: 'system', motion: true, confirm: true };
    var WORDS = {
        subtitle: ['Удобный вид для univer.kaznu.kz', 'univer.kaznu.kz үшін ыңғайлы көрініс', 'A reading layout for univer.kaznu.kz'],
        layout: ['Вид страниц', 'Беттердің көрінісі', 'Page layout'],
        readable: ['Удобный вид', 'Ыңғайлы көрініс', 'Reading mode'],
        original: ['Оригинальный вид', 'Түпнұсқа көрініс', 'Original layout'],
        theme: ['Тема', 'Тақырып', 'Theme'],
        themeSystem: ['Как в системе', 'Жүйе сияқты', 'Same as system'],
        themeLight: ['Светлая', 'Ашық', 'Light'],
        themeDark: ['Тёмная', 'Қараңғы', 'Dark'],
        motion: ['Анимации', 'Анимациялар', 'Animations'],
        motionHint: ['Плавные переходы и отклик кнопок', 'Бірқалыпты ауысулар және түйме әсерлері', 'Smooth transitions and button feedback'],
        confirm: ['Подтверждать уведомление и соглашение', 'Хабарлама мен келісімді растау', 'Confirm the notice and agreement'],
        confirmHint: ['Ставит отметку о прочтении и один раз отправляет форму сайта', 'Оқығаныңызды белгілеп, сайт формасын бір рет жібереді', 'Ticks the confirmation box and sends the site\'s form once'],
        openSite: ['Открыть univer.kaznu.kz', 'univer.kaznu.kz ашу', 'Open univer.kaznu.kz'],
        privacy: ['Конфиденциальность', 'Құпиялылық', 'Privacy'],
        project: ['Все версии', 'Барлық нұсқалар', 'All versions'],
        version: ['Версия {0}', 'Нұсқа {0}', 'Version {0}'],
        saveFailed: ['Настройку не удалось сохранить', 'Баптау сақталмады', 'The setting was not saved']
    };
    var LANGS = ['ru', 'kz', 'en'], lang = 0, settings = clean(null);
    var dark = matchMedia('(prefers-color-scheme: dark)');

    function clean(value) {
        var result = {};
        Object.keys(DEFAULTS).forEach(function (key) { result[key] = value && typeof value[key] === typeof DEFAULTS[key] ? value[key] : DEFAULTS[key]; });
        if (!/^(system|light|dark)$/.test(result.theme)) result.theme = DEFAULTS.theme;
        return result;
    }
    function word(key) { return WORDS[key][lang]; }
    function localize() {
        document.documentElement.lang = ['ru', 'kk', 'en'][lang];
        document.querySelectorAll('[data-text]').forEach(function (node) { node.textContent = word(node.dataset.text); });
        var manifest = api.runtime.getManifest();
        document.querySelector('.version').textContent = word('version').replace('{0}', manifest.version);
    }
    function paint() {
        var root = document.documentElement;
        root.dataset.theme = settings.theme === 'dark' || (settings.theme === 'system' && dark.matches) ? 'dark' : 'light';
        root.dataset.motion = settings.motion ? 'on' : 'off';
        document.querySelector('input[name=layout][value=' + (settings.readable ? 'readable' : 'original') + ']').checked = true;
        document.querySelector('input[name=theme][value=' + settings.theme + ']').checked = true;
        document.querySelector('input[name=motion]').checked = settings.motion;
        document.querySelector('input[name=confirm]').checked = settings.confirm;
    }
    function save(change) {
        var previous = settings, next = clean(Object.assign({}, settings, change));
        settings = next; paint();
        var status = document.querySelector('.status');
        status.textContent = '';
        api.storage.local.set({ settings: next }).catch(function () {
            settings = previous; paint();
            status.textContent = word('saveFailed');
        });
    }

    document.querySelectorAll('input[name=layout]').forEach(function (input) {
        input.addEventListener('change', function () { if (input.checked) save({ readable: input.value === 'readable' }); });
    });
    document.querySelectorAll('input[name=theme]').forEach(function (input) {
        input.addEventListener('change', function () { if (input.checked) save({ theme: input.value }); });
    });
    ['motion', 'confirm'].forEach(function (name) {
        var input = document.querySelector('input[name=' + name + ']');
        input.addEventListener('change', function () { var change = {}; change[name] = input.checked; save(change); });
    });
    dark.addEventListener('change', paint);

    var browser = (navigator.language || '').toLowerCase();
    lang = /^(kk|kz)/.test(browser) ? 1 : /^en/.test(browser) ? 2 : 0;
    localize(); paint();
    api.storage.local.get(['settings', 'language']).then(function (value) {
        if (LANGS.indexOf(value.language) >= 0) { lang = LANGS.indexOf(value.language); localize(); }
        settings = clean(value.settings); paint();
    }, function () { paint(); });
})();
