const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');

// The sender expression exactly as Core/NativeChannel.Script builds it (C# concatenation resolved here).
const KEY = 'kSECRET:';
const cs = fs.readFileSync('src/Univer.Desktop/Core/NativeChannel.cs', 'utf8');
const literal = /return ("\(function\(raw,key\).*?");\s*\n/.exec(cs)[1];
const sender = literal.replace(/" \+ Binding \+ "/g, '__univerNativeNotify').replace(/" \+ key \+ "/g, KEY).replace(/^"|"$/g, '');

function page() {
  const w = new JSDOM('', { url: 'https://univer.kaznu.kz/news/student/1/', runScripts: 'outside-only' }).window, sent = [];
  w.__univerNativeNotify = message => sent.push(message);
  // An app module as the bundle has them: strict, and building its message with a built-in the page can replace.
  w.eval(`(function(__univerNative){ (function(){ 'use strict'; window.appScan = function () { __univerNative('univer-notices:' + JSON.stringify({ v: 1 })); }; })(); })(${sender});`);
  return { w, sent };
}

test('the page never sees the binding, and only the app sender adds the key', () => {
  const p = page();
  assert.equal(typeof p.w.__univerNativeNotify, 'undefined');
  p.w.appScan();
  assert.deepEqual(p.sent, [KEY + 'univer-notices:{"v":1}']);
});

test('a built-in swapped by the page cannot hand it the keyed sender through .caller (review L2 bypass)', () => {
  const p = page();
  p.w.eval(`
    window.stolen = [];
    var S = String, J = JSON.stringify;
    window.String = function f(x) { try { stolen.push(f.caller); } catch (_) {} return S(x); };
    JSON.stringify = function g(x) { try { stolen.push(g.caller); } catch (_) {} return J(x); };
  `);
  p.w.appScan();
  const stolen = p.w.stolen.filter(Boolean);
  stolen.forEach(fn => { try { fn('univer-pin:{"pin":true,"url":"https://evil.example/"}'); } catch (_) {} });
  assert.ok(p.sent.every(message => !message.includes('evil.example')), 'nothing the page called carried the key');
});

test('the bundle runs strict in both entry points, and the late bootstrap never gets the key (review L2, second pass)', () => {
  const controller = fs.readFileSync('src/Univer.Desktop/Web/AutomationController.cs', 'utf8');
  const entries = controller.match(/"\(function\(__univerNative\)\{[^"]*"/g) || [];
  assert.equal(entries.length, 2, 'document script and bootstrap fallback');
  entries.forEach(entry => assert.ok(entry.includes("'use strict'"), entry));
  // The fallback is the branch right under the check (a comment and one call), and its sender is null.
  assert.match(controller, /raw == "bootstrap"\)\s*\{(?:\s*\/\/[^\n]*)*\s*raw = [^\n]*\}\)\(null\);/);
  // Every bundle asset is strict from its first statement, so no frame between a page getter and the sender is sloppy.
  const sloppy = fs.readdirSync('src/Univer.Desktop/Assets').filter(f => f.endsWith('.js'))
    .filter(f => !/^\s*(?:\/\/[^\n]*\n|\/\*[\s\S]*?\*\/\s*)*\s*\(function\s*\w*\s*\([^)]*\)\s*\{\s*'use strict'/.test(fs.readFileSync('src/Univer.Desktop/Assets/' + f, 'utf8')));
  assert.deepEqual(sloppy, []);
});
