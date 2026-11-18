const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync('src/Univer.Desktop/Assets/page-shape.js', 'utf8');
// A 1000×800 window at the top of the page; each box gets the rectangle given in `rects` (viewport coordinates).
function shape(body, rects) {
  const dom = new JSDOM('<!doctype html><html><body style="background:#fff">' + body + '</body></html>', { url: 'https://univer.kaznu.kz/news/student/1/', runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window, d = w.document;
  Object.defineProperty(w, 'innerWidth', { configurable: true, value: 1000 });
  Object.defineProperty(w, 'innerHeight', { configurable: true, value: 800 });
  Object.defineProperty(d.documentElement, 'clientWidth', { configurable: true, value: 1000 });
  w.Range.prototype.getClientRects = () => [];
  for (const [id, r] of Object.entries(rects)) {
    d.getElementById(id).getBoundingClientRect = () => ({ left: r.left, top: r.top, width: r.width, height: r.height, right: r.left + r.width, bottom: r.top + r.height });
  }
  const answer = JSON.parse(w.eval(script));
  w.close();
  return answer;
}
const card = 'background:#e8eef6;border-radius:12px;opacity:1';

test('a bar stuck to the window bottom (the news pager) is left out; the same bar in its own place is kept', () => {
  const body = '<div id="card" style="' + card + '"></div>' +
    '<div id="pager" style="position:sticky;bottom:12px;' + card + '"></div>' +
    '<div id="toggle" style="position:sticky;bottom:12px;' + card + '"></div>';
  const answer = shape(body, {
    card: { left: 32, top: 100, width: 900, height: 200 },
    // Stuck: its bottom edge is 12 px above the window's.
    pager: { left: 400, top: 740, width: 200, height: 48 },
    // Not stuck: it stands where the page put it, well above the window's bottom.
    toggle: { left: 32, top: 320, width: 160, height: 36 }
  });
  assert.equal(answer.v, 2);
  const boxes = answer.items.filter((item) => item[0] === 0).map((item) => item.slice(1, 5).join(','));
  assert.deepEqual(boxes, ['32,100,900,200', '32,320,160,36']);
});

test('a header bar pinned across the top stays part of the first screen', () => {
  const answer = shape('<div id="bar" style="position:fixed;top:0;' + card + '"></div><div id="menu" style="position:fixed;' + card + '"></div>', {
    bar: { left: 0, top: 0, width: 1000, height: 52 },
    menu: { left: 700, top: 60, width: 260, height: 300 }
  });
  assert.deepEqual(answer.items.filter((item) => item[0] === 0).map((item) => item.slice(1, 5).join(',')), ['0,0,1000,52']);
});
