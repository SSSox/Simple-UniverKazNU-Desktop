// Starts the in-app PDF reader. PDF.js loads on its own, so a build this Chromium cannot run still ends in Chromium's
// own PDF viewer instead of an empty page.
import './compat.mjs';
import { createReader, fallBack } from './reader.mjs';

const config = JSON.parse(document.getElementById('reader-config').textContent);
import('../pdfjs/pdf.min.mjs').then(pdfjs => {
    const reader = createReader(pdfjs, window, config);
    window.__univerReader = reader;
    return reader.start();
}).catch(error => {
    window.console.warn('PDF reader: ' + (error && error.message || error));
    fallBack(window, config);
});
