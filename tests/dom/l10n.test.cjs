const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Every RU/KZ/EN dictionary in the page scripts: the three languages have the same keys, English has no Cyrillic,
// and Kazakh carries no Russian-only terms. The scripts keep dictionaries in two shapes:
//   lang === 'en' ? {...} : lang === 'kz' ? {...} : {...}
//   if (lang === 'en') return {...}; if (lang === 'kz') return {...}; return {...};
const assets = path.join(__dirname, '..', '..', 'src', 'Univer.Desktop', 'Assets');
const CYRILLIC = /[А-Яа-яЁёӘәҒғҚқҢңӨөҰұҮүҺһІі]/;
// Russian words a Kazakh line must not borrow: the site's Kazakh names are ОӘК, Moodle ҚОЖ, құпиясөз and Дәрісхана.
const RUSSIAN_IN_KAZAKH = /УМКД|СДО|[Пп]арол|Ауд\.|[Чч]ерез |[Сс]егодня|құпия сөз/;
// Words Kazakh writes the same way as Russian («Ср» is Kazakh short for Сәрсенбі too).
const SAME_IN_KAZAKH = /^(Логин|Кабинет|Аккаунт|Файл|Семинар|Транскрипт|Силлабус|Масштаб|Факультет|мин|семестр|Ср)$/;

function skipString(s, i) {
    const q = s[i++];
    while (i < s.length) {
        const c = s[i];
        if (c === '\\') { i += 2; continue; }
        if (q === '`' && c === '$' && s[i + 1] === '{') { i = match(s, i + 1) + 1; continue; }
        if (c === q) return i + 1;
        i++;
    }
    return i;
}
function regexStart(s, i) {
    let j = i - 1;
    while (j >= 0 && /\s/.test(s[j])) j--;
    if (j < 0 || '(,=:[!&|?{};+-*%<>~^'.includes(s[j])) return true;
    return /\b(return|typeof|case|in|of|void)$/.test(s.slice(Math.max(0, j - 6), j + 1));
}
function skipRegex(s, i) {
    let inClass = false; i++;
    while (i < s.length) {
        const c = s[i];
        if (c === '\\') { i += 2; continue; }
        if (c === '[') inClass = true; else if (c === ']') inClass = false;
        else if (c === '/' && !inClass) { i++; while (/[a-z]/.test(s[i] || '')) i++; return i; }
        i++;
    }
    return i;
}
// Skips a string, comment or regex at i; returns the index after it, or -1 when none starts there.
function skip(s, i) {
    const c = s[i];
    if (c === '\'' || c === '"' || c === '`') return skipString(s, i);
    if (c === '/' && s[i + 1] === '/') { const j = s.indexOf('\n', i); return j < 0 ? s.length : j; }
    if (c === '/' && s[i + 1] === '*') { const j = s.indexOf('*/', i + 2); return j < 0 ? s.length : j + 2; }
    if (c === '/' && regexStart(s, i)) return skipRegex(s, i);
    return -1;
}
// s[i] is an opening bracket; returns the index of its closing bracket.
function match(s, i) {
    const pairs = {'{': '}', '[': ']', '(': ')'}, stack = [pairs[s[i]]];
    i++;
    while (i < s.length && stack.length) {
        const j = skip(s, i);
        if (j >= 0) { i = j; continue; }
        const c = s[i];
        if (pairs[c]) stack.push(pairs[c]); else if (c === stack[stack.length - 1]) stack.pop();
        i++;
    }
    return i - 1;
}
// The value starting at i: an object, a string, or a bare expression up to the next top-level : , ; ?
function valueAt(s, i) {
    while (/\s/.test(s[i])) i++;
    if ('{[('.includes(s[i])) { const j = match(s, i); return {text: s.slice(i, j + 1), end: j + 1}; }
    if ('\'"`'.includes(s[i])) {
        const j = skipString(s, i);
        let k = j;
        while (/\s/.test(s[k] || '')) k++;
        // A lone string; one followed by an operator ('in ' + n) is read as the whole expression below.
        if (k >= s.length || ':,;)]}'.includes(s[k])) return {text: s.slice(i, j), end: j};
    }
    let j = i, depth = 0;
    while (j < s.length) {
        const k = skip(s, j);
        if (k >= 0) { j = k; continue; }
        const c = s[j];
        if ('([{'.includes(c)) depth++;
        else if (')]}'.includes(c)) { if (!depth) break; depth--; } else if (!depth && ':,;?'.includes(c)) break;
        j++;
    }
    return {text: s.slice(i, j).trim(), end: j};
}
// The top-level keys of an object literal; onKey(key, index of its value) is called for each.
function topKeys(obj, onKey) {
    const keys = [];
    let i = 1, depth = 1, expectKey = true;
    while (i < obj.length - 1) {
        const c = obj[i];
        if (depth === 1 && expectKey && /[\w$'"]/.test(c)) {
            const m = /^(?:([A-Za-z_$][\w$]*)|'([^']*)'|"([^"]*)")\s*:/.exec(obj.slice(i));
            if (m) { keys.push(m[1] || m[2] || m[3]); if (onKey) onKey(keys[keys.length - 1], i + m[0].length); i += m[0].length; expectKey = false; continue; }
        }
        const j = skip(obj, i);
        if (j >= 0) { i = j; expectKey = false; continue; }
        if ('{[('.includes(c)) depth++; else if ('}])'.includes(c)) depth--;
        else if (depth === 1 && c === ',') expectKey = true;
        else if (!/\s/.test(c)) expectKey = false;
        i++;
    }
    return keys;
}
// A key's whole value, up to the next top-level comma, so a ternary or a function is not cut short.
function entryValue(obj, i) {
    let j = i, depth = 0;
    while (j < obj.length - 1) {
        const k = skip(obj, j);
        if (k >= 0) { j = k; continue; }
        const c = obj[j];
        if ('([{'.includes(c)) depth++; else if (')]}'.includes(c)) depth--; else if (!depth && c === ',') break;
        j++;
    }
    return obj.slice(i, j).trim();
}
function entries(obj) { const out = new Map(); topKeys(obj, (key, at) => out.set(key, entryValue(obj, at))); return out; }
function literals(text) {
    const out = [];
    for (let i = 0; i < text.length;) {
        const c = text[i];
        if (c === '\'' || c === '"' || c === '`') { const j = skipString(text, i); out.push(text.slice(i + 1, j - 1)); i = j; continue; }
        const j = skip(text, i);
        i = j >= 0 ? j : i + 1;
    }
    return out;
}
const lineOf = (s, i) => s.slice(0, i).split('\n').length;

// Every language choice in a script: {file, line, branches: {en, kz, ru?, else?}} with each branch's source text.
function choices(file) {
    const s = fs.readFileSync(path.join(assets, file), 'utf8'), out = [], seen = new Set();
    const ternary = /([\w$]+(?:\.[\w$]+)*)\s*===\s*'(ru|kz|en)'\s*\?/y;
    for (const m of s.matchAll(/([\w$]+(?:\.[\w$]+)*)\s*===\s*'(ru|kz|en)'\s*\?/g)) {
        if (seen.has(m.index)) continue;
        const branches = {};
        let pos = m.index;
        for (;;) {
            ternary.lastIndex = pos;
            const t = ternary.exec(s);
            if (!t) break;
            seen.add(pos);
            const v = valueAt(s, ternary.lastIndex);
            branches[t[2]] = v.text;
            let k = v.end;
            while (/\s/.test(s[k])) k++;
            if (s[k] !== ':') break;
            pos = k + 1;
            while (/\s/.test(s[pos])) pos++;
            ternary.lastIndex = pos;
            if (ternary.test(s)) continue;
            branches.else = valueAt(s, pos).text;
            break;
        }
        out.push({file, line: lineOf(s, m.index), branches});
    }
    const guard = /if\s*\(\s*([\w$]+)\s*===\s*'(ru|kz|en)'\s*\)\s*return\s*(?=\{)/y;
    for (const m of s.matchAll(/if\s*\(\s*([\w$]+)\s*===\s*'(ru|kz|en)'\s*\)\s*return\s*\{/g)) {
        if (seen.has(m.index)) continue;
        const branches = {};
        let pos = m.index;
        for (;;) {
            guard.lastIndex = pos;
            const g = guard.exec(s);
            if (!g) break;
            seen.add(pos);
            const v = valueAt(s, guard.lastIndex);
            branches[g[2]] = v.text;
            pos = v.end;
            while (/[\s;]/.test(s[pos])) pos++;
            guard.lastIndex = pos;
            if (guard.test(s)) continue;
            const rest = /^return\s*(?=\{)/.exec(s.slice(pos));
            if (rest) branches.else = valueAt(s, pos + rest[0].length).text;
            break;
        }
        out.push({file, line: lineOf(s, m.index), branches});
    }
    // {ru: '…', kz: '…', en: '…'}
    for (const m of s.matchAll(/\{\s*(?:ru|kz|en)\s*:/g)) {
        const obj = s.slice(m.index, match(s, m.index) + 1), branches = {};
        for (const [key, value] of entries(obj)) if (/^(ru|kz|en)$/.test(key)) branches[key] = value;
        // One language alone is not a choice between translations (a Russian-only plural form, say).
        if (Object.keys(branches).length < 2) continue;
        // WORDS[language] || WORDS.ru: a missing language gets the Russian texts.
        branches.else = branches.ru;
        out.push({file, line: lineOf(s, m.index), branches});
    }
    // Object.assign(WORDS.en, {…}); Object.assign(WORDS.kz, {…}); Object.assign(WORDS.ru, {…})
    const added = {};
    for (const m of s.matchAll(/Object\.assign\(\s*([\w$]+)\.(ru|kz|en)\s*,\s*(?=\{)/g)) {
        const start = m.index + m[0].length, list = added[m[1]] = added[m[1]] || {ru: [], kz: [], en: [], line: lineOf(s, m.index)};
        list[m[2]].push(s.slice(start, match(s, start) + 1));
    }
    for (const list of Object.values(added))
        for (let i = 0; i < Math.max(list.en.length, list.kz.length, list.ru.length); i++) {
            const branches = {en: list.en[i], kz: list.kz[i], ru: list.ru[i], else: list.ru[i]};
            if (['ru', 'kz', 'en'].filter(l => branches[l] !== undefined).length >= 2) out.push({file, line: list.line, branches});
        }
    return out;
}
const all = fs.readdirSync(assets).filter(f => f.endsWith('.js')).flatMap(choices);
const branchOf = (c, lang) => c.branches[lang] !== undefined ? c.branches[lang] :
    (['ru', 'kz', 'en'].filter(l => c.branches[l] === undefined).length === 1 ? c.branches.else : undefined);

test('the scan finds the dictionaries it is meant to check', () => {
    const dictionaries = all.filter(c => c.branches.en && c.branches.en.startsWith('{'));
    assert.ok(dictionaries.length >= 12, 'dictionaries found: ' + dictionaries.length);
    for (const file of ['course-tasks.js', 'syllabus-tasks.js', 'univer-layout.js', 'overview.js', 'shortcuts.js', 'attendance.js'])
        assert.ok(dictionaries.some(c => c.file === file), file);
    assert.ok(all.length >= 40, 'choices found: ' + all.length);
    assert.ok(all.some(c => c.file === 'back-to-top.js' && c.branches.kz), 'back-to-top.js');
});

test('every dictionary has the same keys in Russian, Kazakh and English', () => {
    const broken = [];
    for (const c of all) {
        const texts = ['ru', 'kz', 'en'].map(l => branchOf(c, l));
        if (!texts.every(t => t && t.startsWith('{'))) continue;
        const [ru, kz, en] = texts.map(t => topKeys(t).sort().join(','));
        if (ru !== kz || ru !== en) broken.push(`${c.file}:${c.line}\n  ru ${ru}\n  kz ${kz}\n  en ${en}`);
    }
    assert.deepEqual(broken, []);
});

test('English text has no Cyrillic', () => {
    const found = [];
    for (const c of all) {
        const en = branchOf(c, 'en');
        if (en) { for (const text of literals(en)) if (CYRILLIC.test(text)) found.push(`${c.file}:${c.line} ${text}`); }
        // A choice with no English branch hands English the shared one.
        else if (literals(c.branches.else || '').some(t => CYRILLIC.test(t))) found.push(`${c.file}:${c.line} no English branch: ${c.branches.else.slice(0, 60)}`);
    }
    assert.deepEqual(found, []);
});

test('Kazakh text has its own branch and no Russian-only words', () => {
    const found = [];
    for (const c of all) {
        const kz = branchOf(c, 'kz');
        // A choice with no Kazakh branch hands Kazakh the Russian one.
        if (kz === undefined) {
            const shared = c.branches.else || '';
            if (literals(shared).some(t => CYRILLIC.test(t) && !/^семестр$/.test(t))) found.push(`${c.file}:${c.line} no Kazakh branch: ${shared.slice(0, 60)}`);
            continue;
        }
        for (const text of literals(kz)) if (RUSSIAN_IN_KAZAKH.test(text)) found.push(`${c.file}:${c.line} ${text}`);
        // A Kazakh text left as the Russian one: dictionaries compare key by key, other choices by position.
        const ru = branchOf(c, 'ru') || '', ruEntries = ru.startsWith('{') && kz.startsWith('{') ? entries(ru) : null;
        const pairs = ruEntries ? [...entries(kz)].filter(([key]) => ruEntries.has(key)).map(([key, value]) => [literals(value), literals(ruEntries.get(key))])
            : [[literals(kz), literals(ru)]];
        for (const [kzTexts, ruTexts] of pairs) if (kzTexts.length === ruTexts.length)
            kzTexts.forEach((text, i) => { if (text === ruTexts[i] && CYRILLIC.test(text) && !SAME_IN_KAZAKH.test(text.trim())) found.push(`${c.file}:${c.line} same as Russian: ${text}`); });
    }
    assert.deepEqual(found, []);
});
