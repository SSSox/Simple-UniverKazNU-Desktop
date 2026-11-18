/* «Скачать все» in a UMKD section, for the extension: the Windows app's section download (UI/MainForm.SectionDownload.cs)
   and its file checks (Core/FileNames.cs) without the app's own downloads folder. Each file is read with the student's
   session, one at a time; a sign-in page, an empty or cut answer and anything off the site are not saved; the rest goes
   to the browser's downloads under the site's own name, a program or a script with ".bin" after it. */
(function () {
    'use strict';
    var ext = globalThis.__univerExtV1;
    if (!ext || location.origin !== ext.origin || window !== window.top || globalThis.__univerExtDownloadsV1) return;
    var FILE = /^\/student\/umkd\/get\/\d+\/\d+\/\d+\/\d+\/?$/;
    var MAX_FILES = 200, MAX_BYTES = 512 * 1024 * 1024, DEADLINE = 120000;
    var PROGRAMS = ['.exe', '.com', '.scr', '.pif', '.bat', '.cmd', '.msi', '.msp', '.msix', '.appx', '.appxbundle', '.msixbundle', '.ps1', '.psm1',
        '.vbs', '.vbe', '.js', '.jse', '.wsf', '.wsh', '.hta', '.cpl', '.dll', '.sys', '.lnk', '.url', '.reg', '.inf', '.jar', '.application',
        '.gadget', '.scf', '.chm', '.iso', '.img', '.vhd', '.vhdx', '.appref-ms', '.settingcontent-ms', '.library-ms',
        '.msc', '.diagcab', '.search-ms', '.searchconnector-ms', '.wsc', '.sct'];
    var MIME = {
        'application/pdf': '.pdf', 'application/msword': '.doc', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document': '.docx',
        'application/vnd.ms-excel': '.xls', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': '.xlsx',
        'application/vnd.ms-powerpoint': '.ppt', 'application/vnd.openxmlformats-officedocument.presentationml.presentation': '.pptx',
        'application/rtf': '.rtf', 'text/rtf': '.rtf', 'application/zip': '.zip', 'application/x-zip-compressed': '.zip',
        'application/x-rar-compressed': '.rar', 'application/vnd.rar': '.rar', 'application/x-7z-compressed': '.7z',
        'text/plain': '.txt', 'text/csv': '.csv', 'image/jpeg': '.jpg', 'image/png': '.png'
    };
    var WORDS = {
        ru: { done: 'Скачано файлов: {0} из {1}', failed: 'не удалось: {0}', stopped: 'Загрузка остановлена', busy: 'Уже идёт загрузка другого раздела' },
        kz: { done: 'Жүктелген файлдар: {1} ішінен {0}', failed: 'сәтсіз: {0}', stopped: 'Жүктеу тоқтатылды', busy: 'Басқа бөлім жүктеліп жатыр' },
        en: { done: 'Downloaded {0} of {1} files', failed: '{0} failed', stopped: 'Download stopped', busy: 'Another section is downloading' }
    };

    // ---- File names (Core/FileNames.cs) ----
    function extension(name) {
        var match = /(\.[^.\\/]*)$/.exec(name || '');
        var value = match ? match[1].toLowerCase() : '';
        return /^\.(?=[a-z0-9]*[a-z])[a-z0-9]{1,8}$/.test(value) ? value : '';
    }
    function safe(value) {
        var name = String(value || '').replace(/^"+|"+$/g, '').replace(/[\u0000-\u001f\u007f-\u009f­؀-؅؜۝܏᠎​-‏‪-‮⁠-⁤⁦-⁯﻿￹-￻/\\:*?"<>|]/g, '_').replace(/^[ .]+|[ .]+$/g, '');
        if (name.length > 150) {
            var ext = /(\.[^.]*)$/.exec(name); ext = ext && ext[1].length <= 12 ? ext[1] : '';
            var stem = name.slice(0, 150 - ext.length);
            if (/[\ud800-\udbff]$/.test(stem)) stem = stem.slice(0, -1);
            name = stem.replace(/[ .]+$/, '') + ext;
        }
        if (/[\ud800-\udbff]$/.test(name)) name = name.slice(0, -1);
        if (!name) name = 'Univer-document';
        if (/^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)/i.test(name)) name = '_' + name;
        return name;
    }
    function decode(label, bytes) {
        try { return new TextDecoder(label, { fatal: true }).decode(new Uint8Array(bytes)); } catch (_) { return null; }
    }
    // Content-Disposition: filename* (RFC 5987), then filename, which may be MIME-encoded or UTF-8 read as Latin-1.
    function headerName(disposition) {
        if (!disposition || !/\S/.test(disposition)) return null;
        var extended = /(?:^|;)\s*filename\*\s*=\s*(?:"([^"]*)"|([^;]*))/i.exec(disposition);
        if (extended) {
            var parts = (extended[1] != null ? extended[1] : extended[2]).trim().split("'");
            if (parts.length >= 3) {
                var raw = parts.slice(2).join("'"), bytes = [], ok = true;
                for (var i = 0; i < raw.length && ok; i++) {
                    if (raw[i] === '%' && i + 2 < raw.length && /^[0-9a-f]{2}$/i.test(raw.substr(i + 1, 2))) { bytes.push(parseInt(raw.substr(i + 1, 2), 16)); i += 2; }
                    else if (raw.charCodeAt(i) <= 127) bytes.push(raw.charCodeAt(i));
                    else ok = false;
                }
                var text = ok ? decode(parts[0] || 'utf-8', bytes) : null;
                if (text) return text;
            }
        }
        var plain = /(?:^|;)\s*filename\s*=\s*(?:"([^"]*)"|([^;]*))/i.exec(disposition);
        if (!plain) return null;
        var result = (plain[1] != null ? plain[1] : plain[2]).trim();
        var mime = /^=\?([^?]+)\?([bq])\?([^?]*)\?=$/i.exec(result);
        if (mime) {
            var data = [];
            if (mime[2].toLowerCase() === 'b') { try { data = Array.from(atob(mime[3]), function (c) { return c.charCodeAt(0); }); } catch (_) { data = null; } }
            else { var q = mime[3].replace(/_/g, ' '); for (var k = 0; k < q.length; k++) { if (q[k] === '=' && k + 2 < q.length) { data.push(parseInt(q.substr(k + 1, 2), 16)); k += 2; } else data.push(q.charCodeAt(k) & 255); } }
            var decoded = data && decode(mime[1], data);
            if (decoded) return decoded;
        }
        // The site sends UTF-8 in some plain filename parameters; header values reach scripts as Latin-1.
        for (var attempt = 0; attempt < 2 && /[ÐÑÂÃ]/.test(result); attempt++) {
            if (/[^\u0000-ÿ]/.test(result)) break;
            var again = decode('utf-8', Array.from(result, function (c) { return c.charCodeAt(0); }));
            if (!again || again === result) break;
            result = again;
        }
        return result;
    }
    function startsWith(bytes, list) { for (var i = 0; i < list.length; i++) if (bytes[i] !== list[i]) return false; return bytes.length >= list.length; }
    function contains(bytes, text, wide) {
        var pattern = [];
        for (var i = 0; i < text.length; i++) { pattern.push(text.charCodeAt(i)); if (wide) pattern.push(0); }
        outer: for (var j = 0; j <= bytes.length - pattern.length; j++) {
            if (bytes[j] !== pattern[0]) continue;
            for (var k = 1; k < pattern.length; k++) if (bytes[j + k] !== pattern[k]) continue outer;
            return true;
        }
        return false;
    }
    function signature(bytes) {
        if (bytes.length < 4) return '';
        if (startsWith(bytes, [0x25, 0x50, 0x44, 0x46])) return '.pdf';
        if (startsWith(bytes, [0x89, 0x50, 0x4e, 0x47])) return '.png';
        if (startsWith(bytes, [0xff, 0xd8, 0xff])) return '.jpg';
        if (startsWith(bytes, [0x47, 0x49, 0x46, 0x38])) return '.gif';
        if (startsWith(bytes, [0x7b, 0x5c, 0x72, 0x74, 0x66])) return '.rtf';
        if (startsWith(bytes, [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1])) return '.ole';
        if (startsWith(bytes, [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07])) return '.rar';
        if (startsWith(bytes, [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c])) return '.7z';
        if (startsWith(bytes, [0x50, 0x4b, 0x03, 0x04])) {
            // The entry names are stored as plain text in the archive's headers.
            if (contains(bytes, 'word/document.xml')) return '.docx';
            if (contains(bytes, 'xl/workbook.xml')) return '.xlsx';
            if (contains(bytes, 'ppt/presentation.xml')) return '.pptx';
            return '.zip';
        }
        return '';
    }
    function program(bytes) {
        if (bytes.length < 4) return false;
        if (startsWith(bytes, [0x7f, 0x45, 0x4c, 0x46])) return true;
        if (bytes[0] !== 0x4d || bytes[1] !== 0x5a) return false;
        for (var i = 2; i < bytes.length && i < 64; i++) if (bytes[i] === 0) return true;
        return false;
    }
    function blocked(name) {
        var lower = String(name || '').toLowerCase();
        return PROGRAMS.some(function (ext) { return lower.slice(-ext.length) === ext; });
    }
    function resolve(disposition, url, mediaType, bytes) {
        var name = headerName(disposition);
        if (!name || !/\S/.test(name)) {
            try {
                var segment = decodeURIComponent(new URL(url).pathname.split('/').pop() || '');
                if (/\.[a-z0-9]{1,8}$/i.test(segment)) name = segment;
            } catch (_) { }
        }
        name = safe(name);
        var found = signature(bytes), current = extension(name);
        if (found === '.zip' && /^\.(docx|xlsx|pptx|odt|ods|odp|epub|jar|zip)$/.test(current)) found = '';
        if (found === '.ole') {
            if (/^\.(doc|dot|xls|xlt|ppt|pps|pot|msg|vsd)$/.test(current)) found = '';
            else if (contains(bytes, 'WordDocument', true)) found = '.doc';
            else if (contains(bytes, 'Workbook', true)) found = '.xls';
            else if (contains(bytes, 'PowerPoint Document', true)) found = '.ppt';
            else found = /^\.(doc|xls|ppt)$/.test(MIME[mediaType] || '') ? MIME[mediaType] : '.doc';
        }
        var stem = current ? name.slice(0, -current.length) : name;
        if (found && current !== found) name = stem + found;
        else if (!current) name += MIME[(mediaType || '').toLowerCase().split(';')[0].trim()] || '.bin';
        name = safe(name);
        if ((program(bytes) || blocked(name)) && extension(name) !== '.bin') name = safe(name + '.bin');
        return name;
    }
    // A page instead of the file: the sign-in page, the read notice, any HTML (Core/FileNames.LoginHtml).
    function pageInstead(bytes) {
        var head = new TextDecoder('utf-8').decode(bytes.subarray(0, Math.min(bytes.length, 4096))).replace(/^[﻿\s]+/, '');
        return /^(<!doctype\s+html|<html\b|<head\b|<body\b|<form\b)/i.test(head) || /id=["']login_frm["']/i.test(head);
    }

    // ---- The run ----
    var run = null, toast = null, toastTimer = 0;
    function words() { var lang = globalThis.__univerHostV1 ? globalThis.__univerHostV1.language() : 'ru'; return WORDS[lang] || WORDS.ru; }
    function progress(section, done, total) {
        try { if (globalThis.__univerPagesV1 && globalThis.__univerPagesV1.downloading) globalThis.__univerPagesV1.downloading(section, done, total); } catch (_) { }
    }
    function say(text) {
        if (!toast || !toast.isConnected) {
            toast = document.createElement('div');
            toast.className = 'univer-ext-toast'; toast.setAttribute('role', 'status'); toast.setAttribute('aria-live', 'polite');
            toast.style.cssText = 'position:fixed;left:24px;bottom:24px;z-index:2147483000;max-width:calc(100vw - 48px);box-sizing:border-box;padding:12px 16px;border-radius:10px;' +
                'background:#16283b;color:#fff;font:500 14px/1.4 "Segoe UI",system-ui,sans-serif;box-shadow:0 8px 24px rgba(0,0,0,.25)';
            (document.body || document.documentElement).appendChild(toast);
        }
        toast.textContent = text; toast.hidden = false;
        clearTimeout(toastTimer); toastTimer = setTimeout(function () { if (toast) toast.hidden = true; }, 7000);
    }
    function parse(payload) {
        var message;
        try { message = JSON.parse(payload); } catch (_) { return null; }
        if (!message || typeof message.section !== 'string' || !Array.isArray(message.urls) || message.urls.length > MAX_FILES) return null;
        var urls = [];
        for (var i = 0; i < message.urls.length; i++) {
            var url;
            try { url = new URL(message.urls[i]); } catch (_) { return null; }
            if (url.origin !== ext.origin || url.username || url.password || !FILE.test(url.pathname)) return null;
            if (urls.indexOf(url.href) < 0) urls.push(url.href);
        }
        return { section: message.section.slice(0, 300), urls: urls, stop: message.stop === true };
    }
    async function read(url, signal) {
        var response = await fetch(url, { credentials: 'include', cache: 'no-store', redirect: 'follow', signal: signal });
        var final = new URL(response.url || url);
        // 213 is the site's sign-in answer; anything else that is not the file itself is not saved either.
        if (response.status !== 200 || final.origin !== ext.origin || /^\/user\/login/i.test(final.pathname)) throw new Error('answer');
        // A compressed answer's Content-Length counts the encoded bytes, not the file the blob holds.
        var encoded = !/^(identity)?$/i.test((response.headers.get('content-encoding') || '').trim());
        var length = encoded ? 0 : Number(response.headers.get('content-length'));
        if (length > MAX_BYTES) throw new Error('size');
        // Without a size given first (compressed, missing or unreadable), the body is read with its own limit.
        var blob = encoded || !(length > 0) ? await bounded(response) : await response.blob();
        if (!blob.size || blob.size > MAX_BYTES || (length > 0 && blob.size !== length)) throw new Error('size');
        // The head (signatures, a page in place of the file) and the tail (Office archives list their entries at the end).
        var head = Math.min(blob.size, 65536), tail = Math.min(blob.size - head, 65536);
        var bytes = await local(blob, 0, head);
        if (pageInstead(bytes)) throw new Error('page');
        var all = tail ? join(bytes, await local(blob, blob.size - tail, blob.size)) : bytes;
        var name = resolve(response.headers.get('content-disposition'), url, response.headers.get('content-type') || '', all);
        return { blob: new Blob([blob], { type: 'application/octet-stream' }), name: name };
    }
    // An answer with no size to check first is read piece by piece and dropped once it passes MAX_BYTES.
    async function bounded(response) {
        var reader = response.body && response.body.getReader ? response.body.getReader() : null;
        if (!reader) return response.blob();
        var parts = [], total = 0;
        for (;;) {
            var chunk = await reader.read();
            if (chunk.done) return new Blob(parts);
            total += chunk.value.byteLength;
            if (total > MAX_BYTES) { try { reader.cancel(); } catch (_) { } throw new Error('size'); }
            parts.push(chunk.value);
        }
    }
    // Firefox hands content scripts buffers that belong to the page's compartment, where typed-array methods that look up
    // "constructor" (subarray, slice) throw; reading by index into an array of this script's own is allowed everywhere.
    async function local(blob, start, end) {
        var source = new Uint8Array(await blob.slice(start, end).arrayBuffer()), copy = new Uint8Array(source.length);
        for (var i = 0; i < source.length; i++) copy[i] = source[i];
        return copy;
    }
    function join(first, second) {
        var all = new Uint8Array(first.length + second.length);
        all.set(first); all.set(second, first.length);
        return all;
    }
    function save(file) {
        var href = URL.createObjectURL(file.blob), link = document.createElement('a');
        link.href = href; link.download = file.name; link.style.display = 'none'; link.rel = 'noopener';
        (document.body || document.documentElement).appendChild(link);
        link.click(); link.remove();
        setTimeout(function () { URL.revokeObjectURL(href); }, 60000);
    }
    async function start(payload) {
        var job = parse(payload);
        if (!job) return;
        if (job.stop) { if (run && run.section === job.section) { run.controller.abort(); if (run.file) run.file.abort(); } return; }
        if (run) { if (run.section !== job.section) say(words().busy); return; }
        run = { section: job.section, controller: new AbortController(), file: null };
        var saved = 0, failed = 0, total = job.urls.length;
        try {
            for (var i = 0; i < total && !run.controller.signal.aborted; i++) {
                progress(job.section, i, total);
                // Each file has its own deadline: a file that takes too long fails alone, «Стоп» ends the whole run.
                var file = run.file = new AbortController();
                var timer = setTimeout(function () { file.abort(); }, DEADLINE);
                try {
                    var got = await read(job.urls[i], file.signal);
                    if (run.controller.signal.aborted) break;
                    save(got); saved++;
                }
                catch (_) { if (!run.controller.signal.aborted) failed++; }
                finally { clearTimeout(timer); run.file = null; }
                // Room between files for the browser's own download handling.
                await new Promise(function (done) { setTimeout(done, 350); });
            }
        } finally {
            var stopped = run.controller.signal.aborted && saved + failed < total;
            run = null;
            progress(job.section, 0, 0);
            var w = words();
            say(stopped ? w.stopped : w.done.replace('{0}', saved).replace('{1}', total) + (failed ? ' · ' + w.failed.replace('{0}', failed) : ''));
        }
    }
    Object.defineProperty(globalThis, '__univerExtDownloadsV1', { value: Object.freeze({ start: start, resolve: resolve, headerName: headerName, safe: safe, pageInstead: pageInstead }) });
})();
