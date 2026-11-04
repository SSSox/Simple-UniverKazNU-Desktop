/* Text and table helpers shared by the page adapters. It runs first in the page bundle; each adapter takes the
   helpers once, when it starts. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || window.__univerCommonV1) return;
    window.__univerCommonV1 = Object.freeze({
        // An element's text with runs of white space collapsed.
        clean: function (node) { return (node?.textContent || '').replace(/\s+/g, ' ').trim(); },
        // Text for matching a search: composed, lower case, "ё" read as "е".
        fold: function (value) { return String(value).normalize('NFC').toLocaleLowerCase().replace(/ё/g, 'е'); },
        // The rows of a table without the rows of tables nested in its cells.
        ownRows: function (table) { return Array.from(table.rows).filter(function (row) { return row.closest('table') === table; }); },
        // Writes text only when it changed, so observers of the page see no needless mutation.
        setText: function (node, text) { if (node.textContent !== text) node.textContent = text; }
    });
})();
