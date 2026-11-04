// In-app PDF reader: PDF.js canvases under a text layer, so Chromium's find works as in Word files. Text layers are
// built for every page up front, canvases only near the view; a file PDF.js cannot open goes to Chromium's viewer.

const PAGE_WIDTH = 900, LANDSCAPE_WIDTH = 1240, MIN_WIDTH = 160;
const KEEP_DRAWN = 12, MAX_CANVAS_PIXELS = 16777216, PRINT_DPI = 150;

// Width of the widest page in CSS pixels; available is the document width, zoom Chromium's own (device px per CSS px
// over the screen scale). Zooming in keeps this width, so pages grow and scroll sideways as in a PDF viewer.
export function fitWidth(available, zoom, landscape) {
    const unzoomed = available * zoom;
    const gutter = unzoomed > 900 ? 40 : 16;
    return Math.max(MIN_WIDTH, Math.min(landscape ? LANDSCAPE_WIDTH : PAGE_WIDTH, unzoomed - gutter));
}

// A link annotation's box as percentages of its page, whatever the page rotation.
export function linkBox(rect, viewport) {
    const [x1, y1] = viewport.convertToViewportPoint(rect[0], rect[1]);
    const [x2, y2] = viewport.convertToViewportPoint(rect[2], rect[3]);
    return {
        left: 100 * Math.min(x1, x2) / viewport.width, top: 100 * Math.min(y1, y2) / viewport.height,
        width: 100 * Math.abs(x2 - x1) / viewport.width, height: 100 * Math.abs(y2 - y1) / viewport.height
    };
}

export function format(text, ...values) {
    return String(text).replace(/\{(\d+)\}/g, (match, index) => (values[index] === undefined ? match : String(values[index])));
}

// Chromium's own viewer opens the same file: it asks for a password and reads files PDF.js rejects.
export function fallBack(window, config) {
    window.location.replace(config.file);
}

export function createReader(pdfjs, window, config) {
    const document = window.document;
    const main = document.getElementById('pages');
    const strings = config.strings || {};
    const assets = new URL('/assets/pdfjs/', window.location.href).href;
    const worker = new URL('/assets/pdf-reader/worker.mjs', window.location.href).href;
    const entries = [];
    const queue = new Set();
    let pdf = null, scale = 1, failed = false, printing = false, pumping = false;
    let textPages = 0, textChars = 0, hint = null, observer = null, resizeTimer = 0;
    // Milliseconds from start to the page list, the first drawn page and the text of every page (self-test timing).
    const begun = Date.now(), timing = { pages: 0, drawn: 0, text: 0 };

    function zoom() {
        const screen = config.scale > 0 ? config.scale : 1;
        return Math.max(0.25, Math.min(5, (window.devicePixelRatio || 1) / screen));
    }
    function fail(error) {
        if (failed) return;
        failed = true;
        if (error && window.console) window.console.warn('PDF reader: ' + (error.name || 'Error') + ': ' + (error.message || error));
        fallBack(window, config);
    }
    async function start() {
        try {
            pdfjs.GlobalWorkerOptions.workerSrc = worker;
            const task = pdfjs.getDocument({
                url: config.file, disableRange: true, enableXfa: false, cMapUrl: assets + 'cmaps/', cMapPacked: true,
                standardFontDataUrl: assets + 'standard_fonts/', wasmUrl: assets + 'wasm/', iccUrl: assets + 'iccs/'
            });
            task.onPassword = () => { task.destroy(); fail(); };
            pdf = await task.promise;
            for (let number = 1; number <= pdf.numPages; number++) {
                const page = await pdf.getPage(number);
                const view = page.getViewport({ scale: 1 });
                entries.push({ number, page, width: view.width, height: view.height, near: false, canvas: null, drawnScale: 0, drawnRatio: 0, layer: null });
            }
            if (!entries.length) throw new Error('No pages');
            timing.pages = Date.now() - begun;
            build();
            layout();
            observe();
            window.addEventListener('resize', () => { window.clearTimeout(resizeTimer); resizeTimer = window.setTimeout(relayout, 120); });
            await buildText();
        } catch (error) {
            fail(error);
        }
    }
    function build() {
        main.textContent = '';
        const pages = document.createDocumentFragment();
        for (const entry of entries) {
            const div = document.createElement('div');
            div.className = 'page';
            div.dataset.page = String(entry.number);
            div.setAttribute('role', 'region');
            div.setAttribute('aria-label', format(strings.page || '{0} / {1}', entry.number, entries.length));
            const text = document.createElement('div');
            text.className = 'textLayer';
            const links = document.createElement('div');
            links.className = 'links';
            div.append(text, links);
            pages.append(div);
            Object.assign(entry, { div, text, links });
        }
        main.append(pages);
    }
    function layout() {
        const widest = entries.reduce((wide, entry) => (entry.width > wide.width ? entry : wide), entries[0]);
        const width = fitWidth(document.documentElement.clientWidth, zoom(), widest.width > widest.height);
        scale = width / widest.width;
        for (const entry of entries) {
            const viewport = entry.page.getViewport({ scale });
            entry.div.style.width = Math.round(viewport.width) + 'px';
            entry.div.style.height = Math.round(viewport.height) + 'px';
            entry.div.style.setProperty('--total-scale-factor', String(scale * (entry.page.userUnit || 1)));
        }
        if (hint) hint.style.width = Math.round(width) + 'px';
    }
    // Keeps the page at the top of the window in place while pages change size.
    function relayout() {
        if (!entries.length || failed) return;
        const top = window.scrollY;
        const anchor = entries.find(entry => entry.div.offsetTop + entry.div.offsetHeight > top) || entries[0];
        const fraction = (top - anchor.div.offsetTop) / Math.max(1, anchor.div.offsetHeight);
        layout();
        window.scrollTo(window.scrollX, anchor.div.offsetTop + fraction * anchor.div.offsetHeight);
        updateText();
        for (const entry of entries) if (entry.near) request(entry);
    }
    function observe() {
        observer = new window.IntersectionObserver(items => {
            for (const item of items) {
                const entry = entries[Number(item.target.dataset.page) - 1];
                entry.near = item.isIntersecting;
                if (entry.near) request(entry);
            }
        }, { rootMargin: '150% 0px' });
        for (const entry of entries) observer.observe(entry.div);
    }
    function request(entry) {
        queue.add(entry);
        pump();
    }
    // One page is drawn at a time, the one closest to the middle of the window first.
    async function pump() {
        if (pumping) return;
        pumping = true;
        try {
            while (queue.size && !failed) {
                const middle = window.scrollY + window.innerHeight / 2;
                let next = null;
                for (const entry of queue) {
                    if (!next || Math.abs(entry.div.offsetTop - middle) < Math.abs(next.div.offsetTop - middle)) next = entry;
                }
                queue.delete(next);
                if (next.near && (next.drawnScale !== scale || next.drawnRatio !== window.devicePixelRatio)) await draw(next);
            }
        } finally { pumping = false; }
        release();
    }
    async function draw(entry) {
        const viewport = entry.page.getViewport({ scale });
        let ratio = window.devicePixelRatio || 1;
        if (viewport.width * viewport.height * ratio * ratio > MAX_CANVAS_PIXELS) ratio = Math.sqrt(MAX_CANVAS_PIXELS / (viewport.width * viewport.height));
        const canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.floor(viewport.width * ratio));
        canvas.height = Math.max(1, Math.floor(viewport.height * ratio));
        const context = canvas.getContext('2d', { alpha: false });
        context.fillStyle = '#fff';
        context.fillRect(0, 0, canvas.width, canvas.height);
        const drawnScale = scale, drawnRatio = window.devicePixelRatio;
        try {
            await entry.page.render({ canvasContext: context, viewport, transform: ratio === 1 ? null : [ratio, 0, 0, ratio, 0, 0] }).promise;
        } catch (error) {
            // A damaged page stays blank; the other pages and the text still work.
        }
        // The new drawing replaces the old one only when complete, so a page never flashes empty while it is redrawn.
        if (entry.canvas) { entry.canvas.width = entry.canvas.height = 0; entry.canvas.remove(); }
        entry.div.prepend(canvas);
        Object.assign(entry, { canvas, drawnScale, drawnRatio });
        if (!timing.drawn) timing.drawn = Date.now() - begun;
    }
    // Canvases far from the window are released; their pages keep their size and text.
    function release() {
        const drawn = entries.filter(entry => entry.canvas);
        if (drawn.length <= KEEP_DRAWN) return;
        for (const entry of drawn) {
            if (entry.near || queue.has(entry)) continue;
            entry.canvas.width = entry.canvas.height = 0;
            entry.canvas.remove();
            Object.assign(entry, { canvas: null, drawnScale: 0, drawnRatio: 0 });
            entry.page.cleanup();
        }
    }
    async function buildText() {
        for (const entry of entries) {
            if (failed) return;
            const layer = new pdfjs.TextLayer({ textContentSource: entry.page.streamTextContent(), container: entry.text, viewport: entry.page.getViewport({ scale }) });
            try {
                await layer.render();
                entry.layer = layer;
                textChars += layer.textContentItemsStr.join('').trim().length;
            } catch (error) {
                // A page whose text cannot be read is still shown; find skips it.
            }
            await addLinks(entry);
            textPages++;
            if (textPages % 8 === 0) await new Promise(resolve => window.setTimeout(resolve, 0));
        }
        timing.text = Date.now() - begun;
        if (!textChars && strings.noText) {
            // The hint is drawn from an attribute, so find (which reads only the document's text) never matches it.
            hint = document.createElement('p');
            hint.className = 'hint';
            hint.setAttribute('role', 'note');
            hint.dataset.text = strings.noText;
            main.prepend(hint);
            layout();
        }
    }
    // Text positions are relative to the page; only the measured widths follow the new scale.
    function updateText() {
        for (const entry of entries) if (entry.layer) entry.layer.update({ viewport: entry.page.getViewport({ scale }) });
    }
    async function addLinks(entry) {
        let annotations;
        try { annotations = await entry.page.getAnnotations({ intent: 'display' }); } catch (error) { return; }
        const viewport = entry.page.getViewport({ scale: 1 });
        for (const annotation of annotations) {
            if (annotation.subtype !== 'Link' || !annotation.rect || (!annotation.url && !annotation.dest)) continue;
            const link = document.createElement('a');
            const box = linkBox(annotation.rect, viewport);
            link.style.left = box.left + '%';
            link.style.top = box.top + '%';
            link.style.width = box.width + '%';
            link.style.height = box.height + '%';
            if (annotation.url) {
                // PDF.js keeps only absolute http(s), ftp, mailto and tel addresses; the app opens allowed ones outside.
                link.href = annotation.url;
                link.title = annotation.url;
                link.setAttribute('aria-label', annotation.url);
            } else {
                link.href = '#';
                link.setAttribute('aria-label', strings.link || 'Link');
                link.addEventListener('click', event => { event.preventDefault(); goTo(annotation.dest); });
            }
            entry.links.append(link);
        }
    }
    async function goTo(destination) {
        try {
            const explicit = typeof destination === 'string' ? await pdf.getDestination(destination) : destination;
            if (!Array.isArray(explicit)) return;
            const index = typeof explicit[0] === 'object' && explicit[0] !== null ? await pdf.getPageIndex(explicit[0]) : explicit[0];
            const entry = entries[index];
            if (entry) entry.div.scrollIntoView({ block: 'start' });
        } catch (error) {
            // A broken internal link does nothing, like in other viewers.
        }
    }
    // Printing draws every page at print resolution first; Chromium then prints those pictures. confirm = false only
    // prepares the pictures and reports their number (used by the app's self-test, which cannot answer a print dialog).
    async function print(confirm = true) {
        if (printing || !pdf || failed) return 0;
        printing = true;
        const overlay = document.createElement('div');
        overlay.className = 'overlay';
        overlay.setAttribute('role', 'status');
        document.body.append(overlay);
        const holder = document.createElement('div');
        holder.id = 'print';
        const addresses = [];
        let style = null;
        try {
            for (const entry of entries) {
                overlay.textContent = format(strings.printing || '{0} / {1}', entry.number, entries.length);
                const viewport = entry.page.getViewport({ scale: PRINT_DPI / 72 });
                const canvas = document.createElement('canvas');
                canvas.width = Math.max(1, Math.floor(viewport.width));
                canvas.height = Math.max(1, Math.floor(viewport.height));
                const context = canvas.getContext('2d', { alpha: false });
                context.fillStyle = '#fff';
                context.fillRect(0, 0, canvas.width, canvas.height);
                await entry.page.render({ canvasContext: context, viewport, intent: 'print' }).promise;
                const blob = await new Promise(resolve => canvas.toBlob(resolve));
                canvas.width = canvas.height = 0;
                if (!blob) continue;
                const image = document.createElement('img');
                const address = URL.createObjectURL(blob);
                addresses.push(address);
                image.alt = '';
                image.src = address;
                holder.append(image);
            }
            await Promise.all(Array.from(holder.children, image => image.decode().catch(() => null)));
            const first = entries[0].page.getViewport({ scale: 1 });
            style = document.createElement('style');
            style.textContent = '@page{size:' + first.width + 'pt ' + first.height + 'pt;margin:0}';
            document.head.append(style);
            document.body.append(holder);
            overlay.remove();
            if (confirm) window.print();
            return holder.children.length;
        } finally {
            overlay.remove();
            holder.remove();
            if (style) style.remove();
            for (const address of addresses) URL.revokeObjectURL(address);
            printing = false;
        }
    }
    function state() {
        return JSON.stringify({
            pages: entries.length, text: textPages, chars: textChars, drawn: entries.filter(entry => entry.canvas).length,
            width: entries.length ? parseFloat(entries[0].div.style.width) : 0, hint: !!hint, failed, timing
        });
    }
    return { start, print, state, relayout };
}
