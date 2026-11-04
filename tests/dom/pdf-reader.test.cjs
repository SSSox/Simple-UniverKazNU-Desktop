const { test } = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { Blob } = require('node:buffer');
const { JSDOM } = require('jsdom');
const load = () => import(pathToFileURL(path.resolve('src/Univer.Desktop/Assets/pdf-reader/reader.mjs')).href);

// A stand-in for PDF.js: A4 pages, one text run and one web link per page, and a canvas that draws nothing.
function fakePdfjs({ pages = 2, text = 'Univer QA', fail = null, password = false } = {}) {
  const page = number => ({
    userUnit: 1,
    getViewport: ({ scale }) => ({
      width: 595 * scale, height: 842 * scale, scale, rawDims: { pageWidth: 595, pageHeight: 842 },
      convertToViewportPoint: (x, y) => [x * scale, (842 - y) * scale]
    }),
    streamTextContent: () => ({}),
    getAnnotations: async () => [{ subtype: 'Link', rect: [72, 750, 400, 790], url: 'https://example.com/' + number }, { subtype: 'Widget', rect: [0, 0, 1, 1] }],
    render: () => ({ promise: Promise.resolve() }),
    cleanup() {}
  });
  const pdfjs = {
    GlobalWorkerOptions: {},
    tasks: [],
    getDocument(options) {
      const task = { options, destroyed: false, destroy() { this.destroyed = true; } };
      task.promise = fail ? Promise.reject(fail) : password ? new Promise(() => { setTimeout(() => task.onPassword(), 0); })
        : Promise.resolve({ numPages: pages, getPage: async number => page(number), getDestination: async () => null, getPageIndex: async () => 0 });
      pdfjs.tasks.push(task);
      return task;
    },
    TextLayer: class {
      constructor({ container }) { this.container = container; this.textContentItemsStr = []; this.updates = 0; }
      async render() { if (text) { const span = this.container.ownerDocument.createElement('span'); span.textContent = text; this.container.append(span); this.textContentItemsStr.push(text); } }
      update() { this.updates++; }
    }
  };
  return pdfjs;
}
// The page window the reader needs: its document, location, zoom and observers; navigation is recorded, not performed.
function fakeWindow(scale = 1) {
  const dom = new JSDOM('<!doctype html><html><body><main id="pages"><p class="status">Loading</p></main></body></html>');
  const document = dom.window.document;
  dom.window.HTMLCanvasElement.prototype.getContext = () => ({ fillRect() {}, set fillStyle(value) {} });
  dom.window.HTMLCanvasElement.prototype.toBlob = callback => callback(new Blob(['png']));
  dom.window.HTMLImageElement.prototype.decode = () => Promise.resolve();
  const observed = [];
  const window = {
    document, devicePixelRatio: scale, scrollX: 0, scrollY: 0, innerHeight: 800, replaced: null, printed: 0, listeners: {},
    location: { href: 'https://univer-files.local/token/QA.pdf', replace(url) { window.replaced = url; } },
    IntersectionObserver: class { constructor(callback) { this.callback = callback; } observe(target) { observed.push(target); this.callback([{ target, isIntersecting: true }]); } },
    addEventListener(name, handler) { window.listeners[name] = handler; },
    setTimeout: (handler, delay) => setTimeout(handler, delay), clearTimeout: id => clearTimeout(id),
    scrollTo() {}, print() { window.printed++; }, console: { warn() {} }
  };
  Object.defineProperty(document.documentElement, 'clientWidth', { value: 1200 });
  return { window, document, observed };
}
const config = { file: 'file/QA.pdf', scale: 1, strings: { page: 'Страница {0} из {1}', noText: 'Нет текста', printing: '{0}/{1}', link: 'Ссылка' } };
const settle = () => new Promise(resolve => setTimeout(resolve, 20));

test('page width fits the window at 100% and keeps its CSS width when zoomed', async () => {
  const { fitWidth } = await load();
  assert.equal(fitWidth(1200, 1, false), 900);
  assert.equal(fitWidth(1200, 1, true), 1160);
  assert.equal(fitWidth(600, 1, false), 584);
  assert.equal(fitWidth(600, 2, false), fitWidth(1200, 1, false), 'zooming in keeps the CSS width, so pages grow on screen');
  assert.ok(fitWidth(1200, 0.5, false) < fitWidth(1200, 1, false), 'zooming out shrinks pages');
  assert.equal(fitWidth(50, 1, false), 160);
});

test('link boxes are page percentages whatever the viewport flips', async () => {
  const { linkBox, format } = await load();
  const viewport = { width: 1190, height: 1684, convertToViewportPoint: (x, y) => [x * 2, (842 - y) * 2] };
  const box = linkBox([72, 750, 400, 790], viewport);
  assert.ok(Math.abs(box.left - 100 * 144 / 1190) < 1e-9 && Math.abs(box.top - 100 * 104 / 1684) < 1e-9);
  assert.ok(Math.abs(box.width - 100 * 656 / 1190) < 1e-9 && Math.abs(box.height - 100 * 80 / 1684) < 1e-9);
  assert.equal(format('Страница {0} из {1}', 3, 12), 'Страница 3 из 12');
  assert.equal(format('{0} {2}', 'a'), 'a {2}');
});

test('the reader builds every page with its text and links, and draws pages near the window', async () => {
  const { createReader } = await load();
  const { window, document } = fakeWindow();
  const pdfjs = fakePdfjs();
  const reader = createReader(pdfjs, window, config);
  await reader.start(); await settle();
  const options = pdfjs.tasks[0].options;
  assert.equal(options.url, 'file/QA.pdf');
  assert.equal(pdfjs.GlobalWorkerOptions.workerSrc, 'https://univer-files.local/assets/pdf-reader/worker.mjs');
  assert.ok(options.cMapUrl.endsWith('/assets/pdfjs/cmaps/') && options.wasmUrl.endsWith('/assets/pdfjs/wasm/') && options.enableXfa === false);
  const pages = document.querySelectorAll('.page');
  assert.equal(pages.length, 2);
  assert.equal(document.querySelector('.status'), null, 'the loading line goes away');
  assert.equal(pages[1].getAttribute('aria-label'), 'Страница 2 из 2');
  assert.equal(pages[0].style.width, '900px');
  assert.equal(pages[0].style.getPropertyValue('--total-scale-factor'), String(900 / 595));
  assert.equal(pages[0].querySelector('.textLayer').textContent, 'Univer QA');
  const link = pages[0].querySelector('.links a');
  assert.equal(link.getAttribute('href'), 'https://example.com/1');
  assert.ok(link.style.left.endsWith('%') && link.style.top.endsWith('%'));
  assert.equal(pages[0].querySelectorAll('.links a').length, 1, 'only link annotations become links');
  assert.ok(pages[0].querySelector('canvas'), 'a page near the window is drawn');
  const state = JSON.parse(reader.state());
  assert.deepEqual([state.pages, state.text, state.chars, state.hint, state.failed], [2, 2, 18, false, false]);
});

test('a PDF without text explains that find has nothing to match, outside the searchable text', async () => {
  const { createReader } = await load();
  const { window, document } = fakeWindow();
  const reader = createReader(fakePdfjs({ pages: 1, text: '' }), window, config);
  await reader.start(); await settle();
  const hint = document.querySelector('.hint');
  assert.equal(hint.dataset.text, 'Нет текста');
  assert.equal(hint.textContent, '', 'the hint is drawn from an attribute, so find never matches it');
  assert.equal(hint.getAttribute('role'), 'note');
  assert.equal(JSON.parse(reader.state()).hint, true);
});

test('files PDF.js cannot open, and password-protected ones, go to Chromium’s own viewer', async () => {
  const { createReader } = await load();
  for (const options of [{ fail: new Error('Invalid PDF structure.') }, { password: true }]) {
    const { window } = fakeWindow();
    const pdfjs = fakePdfjs(options);
    const reader = createReader(pdfjs, window, config);
    await Promise.race([reader.start(), settle()]); await settle();
    assert.equal(window.replaced, 'file/QA.pdf');
    if (options.password) assert.equal(pdfjs.tasks[0].destroyed, true);
  }
});

test('zooming keeps the CSS width, so pages grow on screen', async () => {
  const { createReader } = await load();
  const { window, document } = fakeWindow(1.25);
  const reader = createReader(fakePdfjs(), window, { ...config, scale: 1.25 });
  await reader.start(); await settle();
  assert.equal(document.querySelector('.page').style.width, '900px');
  window.devicePixelRatio = 2.5;
  reader.relayout();
  assert.equal(document.querySelector('.page').style.width, '900px');
});

test('printing prepares a picture per page and cleans up afterwards', async () => {
  const { createReader } = await load();
  const { window, document } = fakeWindow();
  const reader = createReader(fakePdfjs({ pages: 3 }), window, config);
  await reader.start(); await settle();
  assert.equal(await reader.print(false), 3);
  assert.equal(window.printed, 0, 'preparing only does not open the print dialog');
  assert.equal(document.getElementById('print'), null);
  assert.equal(document.querySelector('.overlay'), null);
  assert.equal(await reader.print(), 3);
  assert.equal(window.printed, 1);
});
