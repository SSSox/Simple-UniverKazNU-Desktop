// Local stand-in for univer.kaznu.kz used by the browser checks: the synthetic pages of pages.mjs, flag images, UMKD
// files with the site's Content-Disposition, and a read notice (/student/notice/) and agreement (/student/news/) that
// stay until they are sent. Each page sets a session-like cookie, and the log records whether
// later requests (the page scripts' fetch reads, the extension's downloads) carried it back.
import http from 'node:http';
import zlib from 'node:zlib';
import { render, notice, agreement } from './pages.mjs';

const PIXEL = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64');
export const FILES = { 9001: 'Силлабус Социология.docx', 9002: 'Лекция 1. Введение.pdf', 9003: 'Лекция 2. Методы.pdf', 9004: 'Лекция 3. Общество.pdf' };

export function start(port) {
    const log = [], state = { notice: false, agreement: false };
    const server = http.createServer((request, response) => {
        const url = new URL(request.url, 'http://127.0.0.1');
        log.push({ method: request.method, path: url.pathname, cookie: /(^|;\s*)qa-session=1/.test(request.headers.cookie || ''), dest: request.headers['sec-fetch-dest'] || '' });
        // The notice and the agreement stay until they are sent; every POST is in the log, so a second one shows.
        if (request.method === 'POST' && url.pathname === '/student/attentionStudents/77') {
            state.notice = true; response.writeHead(302, { Location: '/student/bachelor/' }); response.end(); return;
        }
        if (request.method === 'POST' && url.pathname === '/user/setagree') {
            state.agreement = true; response.writeHead(200, { 'Content-Type': 'application/json' }); response.end('{"code":1}'); return;
        }
        if (url.pathname === '/student/notice/' || url.pathname === '/student/news/') {
            const page = url.pathname === '/student/notice/' ? (state.notice ? '/student/bachelor/' : notice) : (state.agreement ? '/student/bachelor/' : agreement);
            response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Set-Cookie': 'qa-session=1; Path=/; SameSite=Lax; HttpOnly' });
            response.end(render(page)); return;
        }
        if (/^\/img\//.test(url.pathname) || /^\/content\/icons\//.test(url.pathname)) { response.writeHead(200, { 'Content-Type': 'image/png' }); response.end(PIXEL); return; }
        const file = /^\/student\/umkd\/get\/(\d+)\//.exec(url.pathname);
        if (file) {
            const name = FILES[file[1]];
            if (!name) { response.writeHead(404); response.end(); return; }
            // Without the session the site answers with its sign-in page, which the extension must not save as a file.
            if (!/(^|;\s*)qa-session=1/.test(request.headers.cookie || '')) {
                response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); response.end(render('/user/login/')); return;
            }
            const body = name.endsWith('.pdf') ? Buffer.from('%PDF-1.4\n% QA ' + file[1] + '\n%%EOF\n') : Buffer.from('PK\u0003\u0004QA-docx');
            const headers = { 'Content-Type': 'application/octet-stream', 'Content-Disposition': "attachment; filename*=UTF-8''" + encodeURIComponent(name) };
            // One file comes compressed, as a server may send it: the extension then reads it as a stream with its own limit.
            const sent = file[1] === '9004' ? zlib.gzipSync(body) : body;
            if (sent !== body) headers['Content-Encoding'] = 'gzip';
            headers['Content-Length'] = sent.length;
            response.writeHead(200, headers);
            response.end(sent); return;
        }
        const html = render(url.pathname);
        if (!html) { response.writeHead(404, { 'Content-Type': 'text/html; charset=utf-8' }); response.end('<!doctype html><title>404</title>'); return; }
        response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Set-Cookie': 'qa-session=1; Path=/; SameSite=Lax; HttpOnly' });
        response.end(html);
    });
    return new Promise(resolve => server.listen(port, '127.0.0.1', () => resolve({ log, close: () => new Promise(done => server.close(done)) })));
}
