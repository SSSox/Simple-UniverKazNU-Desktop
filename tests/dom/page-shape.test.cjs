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

test('reads computed style and bounding rect once per visible element and reuses one Range across text nodes', () => {
  const body =
    '<div id="outer" style="background:#e8eef6;border-top-left-radius:12px;opacity:1">' +
      '<div id="inner" style="background:#ffffff;border-top-left-radius:8px;opacity:1">' +
        '<span id="t1" style="opacity:1">First</span>' +
        '<span id="t2" style="opacity:1">Second</span>' +
        '<span id="t3" style="opacity:1">Third</span>' +
      '</div>' +
    '</div>';
  const dom = new JSDOM('<!doctype html><html><body style="background:#ffffff">' + body + '</body></html>', {
    url: 'https://univer.kaznu.kz/student/myschedule/',
    runScripts: 'outside-only',
    pretendToBeVisual: true
  });
  const w = dom.window, d = w.document;
  Object.defineProperty(w, 'innerWidth', { configurable: true, value: 1000 });
  Object.defineProperty(w, 'innerHeight', { configurable: true, value: 800 });
  Object.defineProperty(d.documentElement, 'clientWidth', { configurable: true, value: 1000 });

  const rects = {
    outer: { left: 20, top: 40, width: 600, height: 200 },
    inner: { left: 40, top: 60, width: 560, height: 120 },
    t1: { left: 56, top: 80, width: 80, height: 18 },
    t2: { left: 140, top: 80, width: 90, height: 18 },
    t3: { left: 56, top: 110, width: 70, height: 18 }
  };
  const rectCounts = new Map();
  for (const [id, r] of Object.entries(rects)) {
    const el = d.getElementById(id);
    el.getBoundingClientRect = () => {
      rectCounts.set(id, (rectCounts.get(id) || 0) + 1);
      return { left: r.left, top: r.top, width: r.width, height: r.height, right: r.left + r.width, bottom: r.top + r.height };
    };
  }

  const origStyle = w.getComputedStyle.bind(w);
  const styleCounts = new Map();
  w.getComputedStyle = (el, pseudo) => {
    if (el && el.id) styleCounts.set(el.id, (styleCounts.get(el.id) || 0) + 1);
    return origStyle(el, pseudo);
  };

  let rangeCalls = 0;
  const origCreateRange = d.createRange.bind(d);
  d.createRange = () => {
    rangeCalls++;
    const range = origCreateRange();
    let current = null;
    range.selectNodeContents = (node) => { current = node; };
    range.getClientRects = () => {
      const id = current && current.parentElement && current.parentElement.id;
      const r = rects[id];
      return r ? [{ left: r.left, top: r.top, width: r.width, height: r.height, right: r.left + r.width, bottom: r.top + r.height }] : [];
    };
    return range;
  };

  const answer = JSON.parse(w.eval(script));
  w.close();

  for (const id of Object.keys(rects)) {
    assert.equal(styleCounts.get(id), 1, 'getComputedStyle count for #' + id);
    assert.equal(rectCounts.get(id), 1, 'getBoundingClientRect count for #' + id);
  }
  assert.equal(rangeCalls, 1, 'createRange should be called once across all text nodes');

  // outer (#e8eef6 on #fff -> plate 2 darker), inner (#fff on #e8eef6 -> plate 1 lighter),
  // t1+t2 merge on the same line (gap 4px < 8px) and t3 is a separate line below.
  const boxes = answer.items.filter((item) => item[0] === 0);
  const lines = answer.items.filter((item) => item[0] === 1);
  assert.deepEqual(boxes, [
    [0, 20, 40, 600, 200, 12, 2, 0],
    [0, 40, 60, 560, 120, 8, 1, 0]
  ]);
  assert.deepEqual(lines, [
    [1, 56, 84, 174, 10, 4, 0, 0],
    [1, 56, 114, 70, 10, 4, 0, 0]
  ]);
});
