const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync('src/Univer.Desktop/Assets/kit.js', 'utf8');
function page(body) {
  const dom = new JSDOM('<!doctype html><html><body>' + body + '</body></html>', { url: 'https://univer.kaznu.kz/student/myschedule/', runScripts: 'outside-only' });
  const w = dom.window;
  for (const [name, value] of [['offsetWidth', 80], ['offsetHeight', 28], ['offsetLeft', 0], ['offsetTop', 2]])
    Object.defineProperty(w.HTMLElement.prototype, name, { configurable: true, get() { return name === 'offsetLeft' ? (this.id === 'b' ? 82 : 2) : value; } });
  w.eval(script);
  return { w, d: w.document, kit: w.__univerKitV1 };
}
const modes = '<div class="univer-app-schedule-modes"><button id="a" aria-pressed="true">По дням</button><button id="b" aria-pressed="false">Сетка</button></div>';

test('a segmented group gets one thumb under its chosen option, and it follows the choice', () => {
  const p = page(modes); const group = p.d.querySelector('.univer-app-schedule-modes');
  p.kit.configure(true); p.kit.configure(true);
  const thumbs = group.querySelectorAll('.univer-kit-thumb');
  assert.equal(thumbs.length, 1); assert.equal(thumbs[0].getAttribute('aria-hidden'), 'true');
  assert.equal(thumbs[0].style.transform, 'translate(2px,2px)'); assert.ok(group.classList.contains('univer-kit-slid'));
  p.d.getElementById('a').setAttribute('aria-pressed', 'false'); p.d.getElementById('b').setAttribute('aria-pressed', 'true');
  p.kit.refresh();
  assert.equal(group.querySelectorAll('.univer-kit-thumb').length, 1); assert.equal(thumbs[0].style.transform, 'translate(82px,2px)');
  p.w.close();
});

test('Original removes every thumb and leaves the site buttons as they were', () => {
  const p = page(modes); const group = p.d.querySelector('.univer-app-schedule-modes'), buttons = Array.from(group.children);
  p.kit.configure(true); p.kit.configure(false);
  assert.equal(p.d.querySelector('.univer-kit-thumb'), null); assert.equal(group.classList.contains('univer-kit-slid'), false);
  assert.deepEqual(Array.from(group.children), buttons);
  p.kit.refresh(); assert.equal(p.d.querySelector('.univer-kit-thumb'), null);
  p.w.close();
});

test('a group with nothing chosen shows no thumb', () => {
  const p = page(modes.replace('aria-pressed="true"', 'aria-pressed="false"'));
  p.kit.configure(true);
  assert.equal(p.d.querySelector('.univer-kit-thumb'), null);
  p.w.close();
});

test('text rolls only its changed part and returns to plain text when the motion ends', () => {
  const p = page('<span id="left">ещё 50 мин</span>'); const node = p.d.getElementById('left'); let motion, frames;
  p.w.HTMLElement.prototype.animate = function (keys) { frames = keys; motion = {}; return motion; };
  p.kit.configure(true); p.kit.roll(node, 'ещё 49 мин');
  assert.equal(node.textContent, 'ещё 49 мин');
  assert.equal(node.querySelector('span').textContent, '49'); assert.equal(frames[1].opacity, 1);
  motion.onfinish(); assert.equal(node.childElementCount, 0); assert.equal(node.textContent, 'ещё 49 мин');
  p.w.close();
});

test('with motion off, or with nothing there before, text is simply set', () => {
  const p = page('<span id="left">ещё 50 мин</span><span id="empty"></span>'); let calls = 0;
  p.w.HTMLElement.prototype.animate = function () { calls++; return {}; };
  p.kit.configure(true); p.w.__univerMotion = false; p.kit.roll(p.d.getElementById('left'), 'ещё 49 мин');
  p.w.__univerMotion = true; p.kit.roll(p.d.getElementById('empty'), 'ещё 5 мин');
  assert.equal(calls, 0); assert.equal(p.d.getElementById('left').textContent, 'ещё 49 мин'); assert.equal(p.d.getElementById('empty').textContent, 'ещё 5 мин');
  p.w.close();
});
