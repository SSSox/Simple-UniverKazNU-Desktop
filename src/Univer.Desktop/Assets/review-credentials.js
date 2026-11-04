/* Read-only shape of the print/Wi-Fi block: never return text, values, attributes or screenshots. */
(function () {
    'use strict';
    if (location.origin !== 'https://univer.kaznu.kz' || !/^\/student\/bachelor\/?$/i.test(location.pathname)) return '';
    var root = document.getElementById('l4'), matches = [], seen = new Set();
    function flags(text) { return {print:/print\.kaznu\.kz/i.test(text), wifi:/wi[\s-]?fi/i.test(text), loginColon:/(?:логин|login)\s*[:=]/i.test(text), passwordColon:/(?:пароль|password|құпия\s*сөз)\s*[:=]/i.test(text)}; }
    function shape(node, depth) {
        if (node.nodeType === 3) return {tag:'#text', flags:flags(node.nodeValue)};
        if (node.nodeType !== 1 || /SCRIPT|STYLE/.test(node.tagName)) return null;
        return {tag:node.tagName, cls:node.className || '', display:getComputedStyle(node).display, flags:flags(node.textContent), children:depth ? Array.from(node.childNodes).slice(0,60).map(function(n){return shape(n, depth-1);}).filter(Boolean):[]};
    }
    if (root) {
        var walk=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
        for (var node=walk.nextNode(); node; node=walk.nextNode()) {
            if (!/print\.kaznu\.kz|wi[\s-]?fi/i.test(node.nodeValue) || node.parentElement.closest('.univer-app-credentials,.univer-app-overview,.univer-app-menu,.univer-app-quick,#tools')) continue;
            var cell=node.parentElement.closest('td,li,p,div') || node.parentElement;
            if (seen.has(cell)) continue; seen.add(cell);
            var chain=[], parent=cell;
            for(var i=0; parent && parent!==root && i<6; i++,parent=parent.parentElement) chain.push({tag:parent.tagName, cls:parent.className || ''});
            var row=cell.closest('tr'), rows=row?Array.from(row.parentElement.children):[];
            var bodyRow=row?.nextElementSibling, body=bodyRow?.querySelector('td.tt,td.ct');
            matches.push({chain:chain, source:shape(cell,4), body:body?{shape:shape(body,8), forms:body.querySelectorAll('form').length, listItems:body.querySelectorAll('li').length, inputs:Array.from(body.querySelectorAll('input')).map(function(n){return {type:n.type,readonly:n.readOnly,hasValue:!!n.value};})}:null, nextRows:row?rows.slice(rows.indexOf(row)+1,rows.indexOf(row)+7).map(function(n){return shape(n,4);}):[]});
        }
    }
    var panels=Array.from(document.querySelectorAll('.univer-app-credentials')).map(function(n){
        var values=n.querySelectorAll('.univer-app-credential-value'), source=n.parentElement.querySelector('.univer-app-credential-original table'), cells=source?.rows[0]?.cells;
        var masked=!!n.querySelector('.univer-app-credential-hidden'), show=n.querySelector('.univer-app-credential-show'), revealsSource=null;
        if (masked && show && cells?.length===4) {
            show.click(); revealsSource=values[1].textContent===cells[3].textContent.trim(); show.click();
        }
        return {visible:n.getBoundingClientRect().height>0,passwordMasked:masked,copyButtons:n.querySelectorAll('.univer-app-credential-copy').length,
            loginMatchesSource:cells?.length===4?values[0].textContent===cells[1].textContent.trim():null,revealMatchesSource:revealsSource,maskRestored:!!n.querySelector('.univer-app-credential-hidden'),parent:shape(n.parentElement,0)};
    });
    return JSON.stringify({blocks:matches, panels:panels, originalSources:Array.from(document.querySelectorAll('.univer-app-credential-original')).map(function(n){return {display:getComputedStyle(n).display,visible:n.getBoundingClientRect().height>0};}), hosts:document.querySelectorAll('.univer-app-credentials-host').length, hiddenBelow:Array.from(document.querySelectorAll('.univer-app-after-credentials')).map(function(n){return {tag:n.tagName,display:getComputedStyle(n).display};})});
})();
