// The browser extension's own scripts (src/Univer.Extension): the page deadline, the part that drives the shared page
// scripts, the background's single writer of the home screen, and «Скачать все». The page scripts themselves are the
// Windows app's and have their own tests; here they are stand-ins that record how they are called.
const {test,after}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const {JSDOM}=require('jsdom');
const read=name=>fs.readFileSync('src/Univer.Extension/'+name,'utf8');
const SITE='https://univer.kaznu.kz';
const flush=async()=>{for(let i=0;i<6;i++)await new Promise(r=>setImmediate(r));};
async function until(check,ms=8000){const end=Date.now()+ms;while(!check()){if(Date.now()>end)throw new Error('timed out');await new Promise(r=>setTimeout(r,20));}}

// chrome.storage.local and chrome.runtime as the content scripts see them.
function chromeStub(store={}){
 const changed=[],sent=[];
 return {sent,store,storage:{local:{
  get:async keys=>{const r={};[].concat(keys).forEach(k=>{if(k in store)r[k]=structuredClone(store[k]);});return r;},
  set:async value=>{const changes={};for(const k in value){changes[k]={oldValue:store[k],newValue:structuredClone(value[k])};store[k]=structuredClone(value[k]);}changed.forEach(l=>l(changes,'local'));}},
  onChanged:{addListener:l=>changed.push(l)}},
  runtime:{sendMessage:async m=>{sent.push(structuredClone(m));return {ok:true};}}};
}
// A fake clock for the page deadline: setTimeout callbacks run only when the test moves time on.
function clock(w){
 let now=0,id=0;const tasks=[];
 w.setTimeout=(fn,ms=0)=>{tasks.push({id:++id,fn,at:now+ms});return id;};
 w.clearTimeout=n=>{const k=tasks.findIndex(t=>t.id===n);if(k>=0)tasks.splice(k,1);};
 return {tick(ms){now+=ms;for(;;){tasks.sort((a,b)=>a.at-b.at);if(!tasks.length||tasks[0].at>now)break;tasks.shift().fn();}}};
}
const header='<div class="header"><a href="/lang/change/ru/"><img src="/content/icons/ru.png"></a><img src="/content/icons/kz.png"><a href="/lang/change/en/"><img src="/img/en.png"></a></div>';
function site(path,{store={},html=header+'<p>page</p>',gate=true,before}={}){
 const dom=new JSDOM(html,{url:SITE+path,runScripts:'outside-only'}),w=dom.window;
 Object.defineProperty(w.document,'readyState',{value:'complete',configurable:true});
 w.matchMedia=()=>({matches:false,addEventListener(){}});
 const chrome=w.chrome=chromeStub(store),calls=[];
 w.print=()=>calls.push(['print']);
 w.__univerDesktop={apply:(...a)=>calls.push(['apply',...a])};
 w.__univerShortcutsV1={configure:(...a)=>calls.push(['shortcuts',...a.map(x=>x.length)])};
 w.__univerReportV1={opened:()=>7,done:a=>calls.push(['done',structuredClone(a)])};
 w.__univerExtDownloadsV1={start:p=>calls.push(['download',p])};
 if(gate)w.__univerPaintV1={pending:true,configure:f=>calls.push(['configure',f]),complete(...a){calls.push(['complete',...a]);this.pending=false;},finish(f){calls.push(['finish',f]);this.pending=false;}};
 if(before)before(w);
 return {w,d:w.document,chrome,calls,close:()=>w.close()};
}

test('the page deadline: a page whose scripts never finish is uncovered 1.5 s after it is parsed, and nothing runs off the site',()=>{
 const p=site('/student/bachelor/',{gate:false}),time=clock(p.w),root=p.d.documentElement;
 root.style.opacity='0';root.style.pointerEvents='none';
 p.w.eval(read('scripts/prelude.js'));
 assert.equal(p.w.__univerReportPictures,false,'the grade summary hides PNG and Copy picture');
 assert.equal(p.w.__univerOverviewKeep,false,'the home panels keep nothing in the browser');
 assert.equal(p.w.__univerUmkdSeenKeep,false,'UMKD pages keep no list of files seen');
 assert.equal(p.w.__univerScheduleExport,false,'the schedule export buttons are the apps own');
 time.tick(1400);assert.equal(root.style.opacity,'0','still within the deadline');
 time.tick(200);assert.equal(root.style.opacity,'');assert.equal(root.style.pointerEvents,'');
 p.close();
 // Shown the normal way: the deadline leaves the page alone afterwards.
 const q=site('/student/bachelor/',{gate:false}),later=clock(q.w);
 q.w.eval(read('scripts/prelude.js'));q.w.__univerExtV1.shown();
 q.d.documentElement.style.opacity='0';later.tick(16000);assert.equal(q.d.documentElement.style.opacity,'0');q.close();
 // A page still parsing after 5 s is shown as it comes.
 const slow=site('/student/bachelor/',{gate:false}),slowTime=clock(slow.w);
 Object.defineProperty(slow.d,'readyState',{value:'loading',configurable:true});
 slow.d.documentElement.style.opacity='0';slow.w.eval(read('scripts/prelude.js'));
 slowTime.tick(4900);assert.equal(slow.d.documentElement.style.opacity,'0');
 slowTime.tick(200);assert.equal(slow.d.documentElement.style.opacity,'');slow.close();
 const other=new JSDOM('<p>x</p>',{url:'https://example.com/',runScripts:'outside-only'});
 other.window.eval(read('scripts/prelude.js'));assert.equal(other.window.__univerExtV1,undefined);other.window.close();
});

// In Firefox a content script's globalThis is its own sandbox and window is the page's window seen through it, so a
// value written to globalThis never reaches a page script that reads window.X (the theme, motion and picture switches did
// exactly that). jsdom has one global, so this is checked on the sources.
test('what the page scripts read as window.__univer… the extension writes to window, never to globalThis',()=>{
 const assets='src/Univer.Desktop/Assets/';
 const readByPages=new Set(fs.readdirSync(assets).filter(f=>f.endsWith('.js')).flatMap(f=>[...fs.readFileSync(assets+f,'utf8').matchAll(/window\.(__univer\w+)/g)].map(m=>m[1])));
 const own=['background.js',...fs.readdirSync('src/Univer.Extension/scripts').map(f=>'scripts/'+f)];
 for(const file of own){
  for(const m of read(file).matchAll(/globalThis\.(__univer\w+)\s*=(?!=)/g))
   assert.ok(!readByPages.has(m[1]),file+' writes globalThis.'+m[1]+', which the page scripts read as window.'+m[1]);
 }
 for(const name of ['__univerTheme','__univerMotion','__univerReportPictures','__univerOverviewKeep','__univerUmkdSeenKeep'])assert.ok(readByPages.has(name),name+' is still read by the page scripts');
});

test('messages sent before the extension listens wait for it, oversized ones are dropped',()=>{
 const p=site('/student/bachelor/',{gate:false});clock(p.w);
 p.w.eval(read('scripts/prelude.js'));
 const ext=p.w.__univerExtV1,got=[];
 ext.send('univer-dom');ext.send('x'.repeat(262145));ext.send(42);
 ext.listen(m=>got.push(m));ext.send('univer-busy');
 assert.deepEqual(got,['univer-dom','univer-busy']);
 assert.throws(()=>{'use strict';ext.send=null;},'the sender cannot be swapped');
 p.close();
});

test('the host lays the page out with the saved settings and the page\'s own language, then shows it',async()=>{
 const p=site('/student/bachelor/',{store:{settings:{readable:true,theme:'dark',motion:false,confirm:true},home:{pins:[{title:'A',url:SITE+'/a'}],order:[],hidden:[]}}});
 clock(p.w);
 p.w.eval(read('scripts/prelude.js'));p.w.eval(read('scripts/host.js'));
 await flush();
 assert.deepEqual(p.calls.filter(c=>c[0]!=='print'),[['configure',false],['shortcuts',1,0,0],['apply',true,false,'kz'],['complete',false,true,false,'kz']]);
 assert.equal(p.w.__univerTheme,'dark');assert.equal(p.w.__univerMotion,false);
 assert.equal(p.chrome.store.language,'kz','the language the site shows is remembered for pages without flags');
 assert.equal(p.w.__univerHostV1.language(),'kz');
 // The popup switches to the original look: open tabs follow at once.
 p.calls.length=0;
 await p.chrome.storage.local.set({settings:{readable:false,theme:'light',motion:true,confirm:true}});
 assert.deepEqual(p.calls,[['shortcuts',1,0,0],['apply',false,false,'kz']]);
 assert.equal(p.w.__univerTheme,'light');
 p.close();
 // A printed document is the viewer's page; without saved settings the defaults apply.
 const t=site('/student/transcript/');clock(t.w);
 t.w.eval(read('scripts/prelude.js'));t.w.eval(read('scripts/host.js'));await flush();
 assert.deepEqual(t.calls.find(c=>c[0]==='apply'),['apply',true,true,'kz']);t.close();
});

test('a paint gate that fails to finish still leaves the page visible',async()=>{
 const p=site('/student/bachelor/',{before:w=>{w.__univerPaintV1.complete=()=>{throw new Error('broken');};}});
 clock(p.w);
 const root=p.d.documentElement;root.style.opacity='0';root.style.pointerEvents='none';
 p.w.eval(read('scripts/prelude.js'));p.w.eval(read('scripts/host.js'));await flush();
 assert.ok(p.calls.some(c=>c[0]==='finish'&&c[1]===true));
 assert.equal(root.style.opacity,'');assert.equal(root.style.pointerEvents,'');
 p.close();
});

test('the host routes the page scripts\' messages: pins to the background, the summary to the print dialog, files to downloads',async()=>{
 const p=site('/student/attendance/full/'),time=clock(p.w);
 p.w.eval(read('scripts/prelude.js'));
 const ext=p.w.__univerExtV1;
 ext.send('univer-pin:{"pin":true,"title":"Сайт","url":"https://example.com/"}');
 p.w.eval(read('scripts/host.js'));await flush();
 assert.deepEqual(p.chrome.sent,[{type:'pin',payload:'{"pin":true,"title":"Сайт","url":"https://example.com/"}'}],'a message from before the host loaded');
 ext.send('univer-report:pdf');time.tick(0);
 assert.deepEqual(p.calls.filter(c=>c[0]==='print'||c[0]==='done'),[['print'],['done',{action:'print',ok:true,name:'',opening:7}]]);
 p.calls.length=0;ext.send('univer-report:png');
 assert.deepEqual(p.calls,[['done',{action:'png',ok:false,name:'',opening:7}]],'pictures are answered, so the sheet is never left busy');
 p.calls.length=0;ext.send('univer-download-all:{"section":"Лекции","urls":[]}');ext.send('univer-notices:3');ext.send('univer-unknown');
 assert.deepEqual(p.calls,[['download','{"section":"Лекции","urls":[]}']]);
 p.close();
});

// ---- background ----
// Objects made in the background's own realm compare by value only.
const plain=v=>v==null?v:JSON.parse(JSON.stringify(v));
function background({session=true}={}){
 const listeners=[],removed=[],store={},budgets={};
 // A slow write: without the queue, a second message would read the old value before the first one is stored.
 const slow=target=>({get:async key=>({[key]:target[key]&&structuredClone(target[key])}),set:async v=>{await new Promise(r=>setTimeout(r,10));Object.assign(target,structuredClone(v));}});
 const chrome={storage:Object.assign({local:slow(store)},session?{session:slow(budgets)}:{}),runtime:{onMessage:{addListener:l=>listeners.push(l)}},tabs:{onRemoved:{addListener:l=>removed.push(l)}}};
 const module={exports:{}};
 vm.runInNewContext(read('background.js'),{chrome,module,URL});
 const deliver=(message,sender={url:SITE+'/student/bachelor/',frameId:0,tab:{id:1}})=>new Promise(resolve=>{if(!listeners[0](message,sender,r=>resolve(plain(r))))resolve('ignored');});
 const api=module.exports,applyPin=(...a)=>plain(api.applyPin(...a));
 const closeTab=id=>Promise.all(removed.map(l=>l(id)));
 return {store,budgets,deliver,closeTab,api:Object.assign({},api,{applyPin,budgetStep:(...a)=>plain(api.budgetStep(...a))})};
}

test('pins follow the Windows app\'s rules: site and web links, no fixed tiles, no sign-in or logout, at most 24',()=>{
 const {api}=background(),home={pins:[],order:[],hidden:[]};
 const pin=(url,title='Ссылка')=>JSON.stringify({pin:true,title,url});
 let next=api.applyPin(home,pin(SITE+'/student/umkd/321/2026/1/','УМКД'));
 assert.deepEqual(next.pins,[{title:'УМКД',url:SITE+'/student/umkd/321/2026/1/'}]);
 assert.equal(api.applyPin(next,pin(SITE+'/student/umkd/321/2026/1/','Ещё раз')).pins.length,1,'no duplicates');
 assert.equal(api.applyPin(home,pin(SITE+'/student/myschedule/')).pins.length,0,'a fixed tile is already on the home screen');
 for(const bad of [SITE+'/user/logout',SITE+'/user/login/','javascript:alert(1)','file:///C:/x','https://user:pw@example.com/','ftp://example.com/'])
  assert.equal(api.applyPin(home,pin(bad)),null,bad);
 assert.equal(api.applyPin(home,pin('http://univer.kaznu.kz/student/')),null,'the site only over HTTPS');
 assert.equal(api.applyPin(home,'{bad json'),null);
 assert.equal(api.cleanTitle('  a\u0000\n b  '),'a b');assert.equal(api.cleanTitle('я'.repeat(200)).length,120);
 let full={pins:[],order:[],hidden:[]};
 for(let i=0;i<24;i++)full=api.applyPin(full,pin('https://example.com/'+i));
 assert.equal(full.pins.length,24);assert.equal(api.applyPin(full,pin('https://example.com/25')),null);
 assert.equal(api.applyPin(full,JSON.stringify({pin:false,url:'https://example.com/3'})).pins.length,23);
 assert.deepEqual(api.applyPin(home,JSON.stringify({order:[SITE+'/student/myschedule/','https://example.com/']})).order,[SITE+'/student/myschedule/','https://example.com/']);
 assert.equal(api.applyPin(home,JSON.stringify({order:['https://example.com/','https://example.com/']})),null,'an order with repeats');
 assert.deepEqual(api.applyPin(home,JSON.stringify({hidden:[SITE+'/student/transcript/']})).hidden,[SITE+'/student/transcript/']);
 assert.equal(api.applyPin(home,JSON.stringify({hidden:['https://example.com/']})),null,'only fixed tiles hide');
});

test('two tabs pinning at once both keep their pin; frames and other sites are not listened to',async()=>{
 const {store,deliver}=background();
 const a=deliver({type:'pin',payload:JSON.stringify({pin:true,title:'A',url:'https://example.com/a'})});
 const b=deliver({type:'pin',payload:JSON.stringify({pin:true,title:'B',url:'https://example.com/b'})});
 assert.deepEqual(await Promise.all([a,b]),[{ok:true},{ok:true}]);
 assert.deepEqual(store.home.pins.map(p=>p.title),['A','B']);
 const message={type:'pin',payload:JSON.stringify({pin:true,title:'C',url:'https://example.com/c'})};
 assert.equal(await deliver(message,{url:SITE+'/student/bachelor/',frameId:3}),'ignored');
 assert.equal(await deliver(message,{url:'https://example.com/',frameId:0}),'ignored');
 assert.equal(await deliver(message,{url:'http://univer.kaznu.kz/',frameId:0}),'ignored');
 assert.equal(store.home.pins.length,2);
});

// ---- «Скачать все» ----
function downloads(){
 const p=site('/student/umkd/321/2026/1/',{gate:false});
 const w=p.w,saved=[],progress=[],requested=[];let answers={};
 // Node's own Blob, Response and TextDecoder stand in for the browser's (jsdom has none that read bytes).
 w.Blob=Blob;w.TextDecoder=TextDecoder;
 w.URL.createObjectURL=blob=>{const href='blob:'+SITE+'/'+saved.length;w.__blobs=(w.__blobs||new Map()).set(href,blob);return href;};w.URL.revokeObjectURL=()=>{};
 w.fetch=async(url,init,hops=0)=>{
  requested.push([url,init.credentials,init.cache,init.redirect,init.signal]);
  const make=answers[url],response=make?await make():new Response('',{status:404});
  if([301,302,303,307,308].includes(response.status)&&response.headers.has('location')&&init.redirect!=='manual'){
   if(init.redirect==='error'||hops>=20)throw new TypeError('redirect');
   return w.fetch(new URL(response.headers.get('location'),url).href,init,hops+1);
  }
  return response;
 };
 w.document.addEventListener('click',e=>{const a=e.target.closest('a[download]');if(a){e.preventDefault();saved.push({name:a.download,blob:w.__blobs.get(a.href)});}},true);
 w.__univerPagesV1={downloading:(...a)=>progress.push(a.join(' '))};
 delete w.__univerExtDownloadsV1;
 w.eval(read('scripts/prelude.js'));w.eval(read('scripts/downloads.js'));
 const job=(urls,extra={})=>w.__univerExtDownloadsV1.start(JSON.stringify(Object.assign({section:'Лекции',urls},extra)));
 const toast=()=>{const t=w.document.querySelector('.univer-ext-toast');return t&&t.textContent;};
 return {w,saved,progress,requested,job,toast,set:value=>{answers=value;},close:p.close};
}
const file=n=>SITE+'/student/umkd/get/'+n+'/321/2026/1';
const named=(body,name,headers={})=>()=>new Response(body,{status:200,headers:Object.assign({'Content-Disposition':"attachment; filename*=UTF-8''"+encodeURIComponent(name),'Content-Length':String(Buffer.byteLength(body))},headers)});

test('«Скачать все» saves the section\'s files one by one under the site\'s names and skips whatever is not the file',async()=>{
 const d=downloads();
 const login='<!DOCTYPE html><html><body><form id="login_frm"></form></body></html>';
 d.set({
  [file(1)]:named('%PDF-1.4 one','Лекция 1.pdf'),
  [file(2)]:()=>new Response('',{status:213}),
  [file(3)]:named(login,'Лекция 3.pdf'),
  [file(4)]:named('','Пусто.pdf'),
  [file(5)]:named('%PDF-1.4 cut','Обрыв.pdf',{'Content-Length':'9999'}),
  [file(6)]:()=>{const r=new Response('%PDF x',{status:200});Object.defineProperty(r,'url',{value:'https://example.com/file.pdf'});return r;},
  [file(7)]:named('MZ\u0000\u0000payload','Методичка.pdf'),
  // Compressed on the wire: Content-Length counts the encoded bytes, the blob the decoded file.
  [file(8)]:named('%PDF-1.4 zipped','Сжатый.pdf',{'Content-Length':'5','Content-Encoding':'gzip'}),
 });
 d.job([1,2,3,4,5,6,7,8].map(file));
 await until(()=>d.progress.includes('Лекции 0 0'),12000);
 assert.deepEqual(d.saved.map(s=>s.name),['Лекция 1.pdf','Методичка.pdf.bin','Сжатый.pdf']);
 assert.equal(await d.saved[0].blob.text(),'%PDF-1.4 one');
 assert.deepEqual(d.progress,['Лекции 0 8','Лекции 1 8','Лекции 2 8','Лекции 3 8','Лекции 4 8','Лекции 5 8','Лекции 6 8','Лекции 7 8','Лекции 0 0']);
 assert.ok(d.requested.every(([,credentials,cache])=>credentials==='include'&&cache==='no-store'),'the student\'s session, never a cached page');
 assert.equal(d.toast(),'Скачано файлов: 3 из 8 · не удалось: 5');
 d.close();
});

test('bulk downloads check every redirect target before contacting anything outside the HTTPS Univer origin',async()=>{
 const d=downloads();
 try{
  const targets=['https://teacher.example/material.pdf','//teacher.example/material.pdf','http://univer.kaznu.kz/material.pdf',
   'https://univer.kaznu.kz:444/material.pdf','https://univer.kaznu.kz.teacher.example/material.pdf','https://student@univer.kaznu.kz/material.pdf'];
  const answers={},expected=[];
  targets.forEach((target,i)=>{
   const hop=SITE+'/student/umkd/redirect/'+i;
   answers[file(i+1)]=()=>new Response(null,{status:302,headers:{Location:hop}});
   answers[hop]=()=>new Response(null,{status:[301,302,303,307,308,302][i],headers:{Location:target}});
   expected.push(file(i+1),hop);
  });
  d.set(answers);await d.job(targets.map((_,i)=>file(i+1)));
  assert.deepEqual(d.requested.map(([url])=>url),expected,'no request reaches a rejected redirect target');
  assert.equal(d.saved.length,0);
  assert.equal(d.toast(),'Скачано файлов: 0 из 6 · не удалось: 6');
 }finally{d.close();}
});

test('bulk downloads follow visible same-origin redirect hops with the same session and cancellation signal',async()=>{
 const d=downloads();
 try{
  const targets=['/student/umkd/cache/a','b','b?download=1','//univer.kaznu.kz/student/umkd/cache/c','/student/umkd/cache/file.pdf'];
  const answers={},expected=[file(1)];let current=file(1);
  targets.forEach((target,i)=>{
   answers[current]=()=>new Response(null,{status:[301,302,303,307,308][i],headers:{Location:target}});
   current=new URL(target,current).href;expected.push(current);
  });
  answers[current]=named('%PDF redirected','redirected.pdf');
  d.set(answers);await d.job([file(1)]);
  assert.deepEqual(d.requested.map(([url])=>url),expected);
  assert.ok(d.requested.every(([,credentials,cache,redirect,signal])=>credentials==='include'&&cache==='no-store'&&redirect==='manual'&&signal===d.requested[0][4]));
  assert.deepEqual(d.saved.map(s=>s.name),['redirected.pdf']);
  assert.equal(await d.saved[0].blob.text(),'%PDF redirected');
 }finally{d.close();}
});

test('bulk downloads fail closed on opaque redirects and bound same-origin redirect loops',async()=>{
 const d=downloads();let bodyReads=0;
 try{
  d.set({
   [file(1)]:()=>({type:'opaqueredirect',status:0,url:'',headers:new Headers(),get body(){bodyReads++;throw new Error('hidden body');}}),
   [file(2)]:()=>new Response(null,{status:302,headers:{Location:file(2)}}),
   [file(3)]:()=>new Response(null,{status:302})
  });
  await d.job([file(1),file(2),file(3)]);
  assert.equal(bodyReads,0);
  assert.equal(d.requested.filter(([url])=>url===file(1)).length,1);
  assert.ok(d.requested.filter(([url])=>url===file(2)).length<=21,'at most twenty redirect hops');
  assert.equal(d.saved.length,0);
  assert.equal(d.toast(),'Скачано файлов: 0 из 3 · не удалось: 3');
 }finally{d.close();}
});

test('bulk downloads reject HTML media types before reading even comment-prefixed response bodies',async()=>{
 const d=downloads();let bodyReads=0;
 try{
  const types=['text/html; charset=utf-8','TEXT/HTML ; charset=UTF-8','application/xhtml+xml','Application/XHTML+XML; charset=utf-8'];
  const answers={};
  types.forEach((type,i)=>{answers[file(i+1)]=()=>{
   const response=named('<!-- maintenance -->\n<html><body>Try later</body></html>','lecture.pdf',{'Content-Type':type})();
   if(i%2)response.headers.delete('Content-Length');
   const body=response.body,blob=response.blob.bind(response);
   Object.defineProperty(response,'body',{get(){bodyReads++;return body;}});
   response.blob=()=>{bodyReads++;return blob();};
   return response;
  };});
  d.set(answers);await d.job(types.map((_,i)=>file(i+1)));
  assert.equal(bodyReads,0,'neither the sized blob nor the unsized stream is read');
  assert.equal(d.saved.length,0);
  assert.equal(d.toast(),'Скачано файлов: 0 из 4 · не удалось: 4');
 }finally{d.close();}
});

test('bulk downloads still sniff mislabeled HTML while saving real files',async()=>{
 const d=downloads();
 try{
  d.set({
   [file(1)]:named('<html><body>Maintenance</body></html>','lecture.pdf',{'Content-Type':'application/octet-stream'}),
   [file(2)]:named('<!DOCTYPE html><html>Sign in</html>','lecture.pdf',{'Content-Type':'text/plain'}),
   [file(3)]:named('%PDF real','lecture.pdf',{'Content-Type':'text/plain'})
  });
  await d.job([file(1),file(2),file(3)]);
  assert.deepEqual(d.saved.map(s=>s.name),['lecture.pdf']);
  assert.equal(await d.saved[0].blob.text(),'%PDF real');
  assert.equal(d.toast(),'Скачано файлов: 1 из 3 · не удалось: 2');
 }finally{d.close();}
});

// The timeout turns a regression (the whole endless body read into memory) into a failure instead of a hang.
test('«Скачать все» stops reading an answer without a size once it passes 512 MB',{timeout:60000},async()=>{
 const piece=new Uint8Array(64*1024*1024);
 for(const headers of [{'Content-Encoding':'gzip'},{},{'Content-Length':'abc'}]){
  const d=downloads();let pulled=0;
  const endless=new ReadableStream({pull(c){pulled++;c.enqueue(piece);}});
  d.set({[file(1)]:()=>new Response(endless,{status:200,headers:Object.assign({'Content-Disposition':'attachment; filename="big.pdf"'},headers)})});
  d.job([file(1)]);
  await until(()=>d.progress.includes('Лекции 0 0'),12000);
  assert.equal(d.saved.length,0);
  assert.ok(pulled<=11,'read no further than the limit: '+pulled+' '+JSON.stringify(headers));
  d.close();
 }
});

test('«Скачать все» takes nothing from a message with an address off the site, stops on «Стоп» and runs one section at a time',async()=>{
 const d=downloads();
 d.job([file(1),'https://example.com/student/umkd/get/1/2/3/4']);d.job([file(1),SITE+'/student/bachelor/']);d.job([file(1),'https://u:p@univer.kaznu.kz/student/umkd/get/1/2/3/4']);
 await flush();assert.deepEqual(d.requested,[]);
 let release;const held=new Promise(r=>{release=r;});
 d.set({[file(1)]:async()=>{await held;return named('%PDF a','a.pdf')();},[file(2)]:named('%PDF b','b.pdf'),[file(3)]:named('%PDF c','c.pdf')});
 d.job([file(1),file(2),file(3)]);await flush();
 d.w.__univerExtDownloadsV1.start(JSON.stringify({section:'Практика',urls:[file(2)]}));
 assert.equal(d.toast(),'Уже идёт загрузка другого раздела');
 d.job([file(1),file(2),file(3)],{stop:true});release();
 await until(()=>d.progress.includes('Лекции 0 0'));
 assert.equal(d.toast(),'Загрузка остановлена');assert.deepEqual(d.saved,[],'a file that arrives after «Стоп» is not saved');
 assert.ok(!d.requested.some(([url])=>url===file(3)));
 d.close();
});

test('file names: RFC 5987, mis-decoded UTF-8 and MIME words, the content\'s real type, programs and reserved names',()=>{
 const d=downloads(),{resolve,pageInstead}=d.w.__univerExtDownloadsV1,bytes=s=>new Uint8Array(Buffer.from(s,'latin1'));
 const pdf=bytes('%PDF-1.4'),docx=bytes('PK\u0003\u0004....word/document.xml'),xlsx=bytes('PK\u0003\u0004....xl/workbook.xml');
 const url=file(1);
 assert.equal(resolve("attachment; filename*=UTF-8''%D0%9B%D0%B5%D0%BA%D1%86%D0%B8%D1%8F.pdf",url,'',pdf),'Лекция.pdf');
 assert.equal(resolve('attachment; filename="'+Buffer.from('Силлабус.docx').toString('latin1')+'"',url,'',docx),'Силлабус.docx');
 assert.equal(resolve('attachment; filename="=?UTF-8?B?'+Buffer.from('План.xlsx').toString('base64')+'?="',url,'',xlsx),'План.xlsx');
 assert.equal(resolve('attachment; filename="report.doc"',url,'',pdf),'report.pdf','the content decides the type');
 assert.equal(resolve('attachment; filename="table.zip"',url,'',docx),'table.docx');
 const ole=new Uint8Array([0xd0,0xcf,0x11,0xe0,0xa1,0xb1,0x1a,0xe1,...Buffer.from('WordDocument','utf16le')]);
 assert.equal(resolve('attachment; filename="old"',url,'',ole),'old.doc');
 assert.equal(resolve('attachment; filename="run.cmd"',url,'',bytes('echo')),'run.cmd.bin');
 assert.equal(resolve('attachment; filename="setup.pdf"',url,'',bytes('MZ\u0090\u0000\u0003')),'setup.pdf.bin');
 assert.equal(resolve('attachment; filename="CON.txt"',url,'text/plain',bytes('text')),'_CON.txt');
 assert.equal(resolve('attachment; filename="a/b:c?.pdf"',url,'',pdf),'a_b_c_.pdf');
 assert.equal(resolve('',url,'',pdf),'Univer-document.pdf');
 assert.equal(resolve('attachment; filename="notes"',url,'text/plain; charset=utf-8',bytes('text')),'notes.txt');
 assert.equal(resolve('attachment; filename="blob"',url,'',bytes('????')),'blob.bin');
 const long=resolve('attachment; filename="'+Buffer.from('я'.repeat(200)+'.pdf').toString('latin1')+'"',url,'',pdf);
 assert.equal(long.length,150);assert.ok(long.endsWith('.pdf'));
 assert.equal(pageInstead(bytes('<!DOCTYPE html><html>')),true);
 assert.equal(pageInstead(new Uint8Array(Buffer.from('\ufeff  <HTML>'))),true);
 assert.equal(pageInstead(bytes('<div id="login_frm">')),true);
 assert.equal(pageInstead(pdf),false);
 d.close();
});

// ---- The read notice and the agreement ----
const automation=fs.readFileSync('src/Univer.Desktop/Assets/univer-automation.js','utf8');
// page.js as the build makes it: the bridge with the module in its sender's closure.
const pageScript=read('scripts/page-bridge.js').replace('/*@MODULES@*/',"(function(__univerNative){'use strict';\n"+automation+"\n})(__univerPageSend);\n");
const noticeHtml='<p>Новости и объявления</p><form id="attentionSt" action="/student/attentionStudents/77" method="post"><p>Синтетическое уведомление</p><input id="check" type="checkbox"><label for="check">Я подтверждаю, что прочёл данное уведомление</label><input id="btn" type="submit" value="Отправить" disabled></form>';
const agreementHtml='<p>Новости</p><div id="modal_agreement"><form id="agree_frm" action="/user/setagree"><input id="iconfirm" type="checkbox"><label for="iconfirm">Прочитал договор</label><button type="button" class="btn-confirm" onclick="agree()" disabled>Согласен</button></form></div>';
const jsdomUtils=require('jsdom/lib/jsdom/living/generated/utils');
function trusted(w,node,type='click'){const e=new w.MouseEvent(type,{bubbles:true,cancelable:true});jsdomUtils.implForWrapper(e).isTrusted=true;jsdomUtils.implForWrapper(node)._dispatch(jsdomUtils.implForWrapper(e));}
// A page of the site with the extension: the page's world (page.js) and the isolated driver, the site's own handlers
// (calc(), the agreement's change handler and agree() with its AJAX answer) and a background shared by all pages.
// A failed test leaves its pages' timers running; they all close at the end.
const opened=[];after(()=>opened.forEach(w=>w.close()));
function confirmPage(bg,path,html,{settings,storage=true,before}={}){
 const dom=new JSDOM(html,{url:SITE+path,runScripts:'outside-only',pretendToBeVisual:true}),w=dom.window,d=w.document;opened.push(w);
 Object.defineProperty(d,'readyState',{value:'complete',configurable:true});
 w.HTMLElement.prototype.getClientRects=function(){return this.closest('[hidden]')?[]:[{width:100,height:30}];};
 const sent={posts:0,agrees:0},hooks={};
 w.jQuery=()=>({bind(name,fn){hooks[name]=fn;return this;}});
 w.agree=()=>{sent.agrees++;setTimeout(()=>{hooks['ajaxSuccess.univerApp']&&hooks['ajaxSuccess.univerApp'](null,{responseText:'{"code":1}'},{url:'/user/setagree',type:'POST'});const m=d.getElementById('modal_agreement');if(m)m.remove();},30);};
 const check=d.querySelector('#check'),btn=d.querySelector('#btn');
 if(check)check.addEventListener('click',()=>{btn.disabled=!check.checked;});
 const box=d.querySelector('#iconfirm'),ok=d.querySelector('.btn-confirm');
 if(box){box.addEventListener('change',()=>{ok.disabled=!box.checked;});ok.addEventListener('click',()=>w.agree());}
 d.addEventListener('submit',e=>{e.preventDefault();sent.posts++;});
 w.chrome={storage:{local:{get:async()=>{if(!storage)throw new Error('no storage');return settings===undefined?{}:{settings:structuredClone(settings)};}},onChanged:{addListener(){}}},
  runtime:{sendMessage:m=>bg.deliver(structuredClone(m),{url:w.location.href,frameId:0,tab:{id:1}})}};
 const messages=[];const deliver=w.chrome.runtime.sendMessage;w.chrome.runtime.sendMessage=m=>{messages.push(structuredClone(m));return deliver(m);};
 if(before)before(w);
 w.eval(pageScript);w.eval(read('scripts/prelude.js'));w.eval(read('scripts/driver.js'));
 return {w,d,sent,messages,close:()=>w.close()};
}
const pause=ms=>new Promise(r=>setTimeout(r,ms));

test('the confirmation budget: one send per form, three a minute, given back only when nothing was sent',()=>{
 const {api}=background(),key='/student/attentionStudents/77:1a2b3c4d',other='/student/attentionStudents/78:1a2b3c4d';
 // Every message comes from tab 1 unless it names another one (the listener sets tab from the sender).
 let state={};const step=(message,now)=>{const r=api.budgetStep(state,Object.assign({tab:1},message),now);state=r.budgets;return r.ok?'ok':r.reason;};
 assert.equal(step({op:'can',kind:'notice',key},1000),'ok');
 assert.equal(step({op:'reserve',kind:'notice',key},1000),'ok');
 assert.equal(step({op:'reserve',kind:'notice',key},1001),'taken','the same form is not sent twice');
 assert.equal(step({op:'can',kind:'agreement',key:'additional:ff'},1001),'ok','the agreement has its own budget');
 assert.equal(step({op:'release',kind:'notice',key},1002),'ok');
 assert.equal(step({op:'reserve',kind:'notice',key},1003),'ok','nothing was sent: the form may be sent');
 assert.equal(step({op:'reserve',kind:'notice',key:other},1004),'ok');
 // The released attempt still counts (a forged "nothing was sent" cannot lift the bound): this is the fourth.
 assert.equal(step({op:'reserve',kind:'notice',key:'/student/attentionStudents/79:1'},1005),'limit','three a minute');
 assert.equal(step({op:'reserve',kind:'notice',key:'/student/attentionStudents/79:1'},61001),'ok','a minute later');
 assert.equal(step({op:'reserve',kind:'notice',key:'/student/attentionStudents/80:1'},61004),'ok');
 // The extension's own stop before the click gives the attempt back, as the Windows app does.
 for(const [k,at] of [['additional:a1',61005],['additional:a2',61005],['additional:a3',61005]]) assert.equal(step({op:'reserve',kind:'agreement',key:k},at),'ok');
 assert.equal(step({op:'release',kind:'agreement',key:'additional:a3',refund:true},61006),'ok');
 assert.equal(step({op:'reserve',kind:'agreement',key:'additional:a4'},61007),'ok','the refunded attempt is free again');
 assert.equal(step({op:'release',kind:'agreement',key:'additional:a4'},61008),'ok');
 assert.equal(step({op:'reserve',kind:'agreement',key:'additional:a5'},61009),'limit','a release without refund keeps the attempt');
 // Only a later page of the sending tab says the form is gone: not a page that began before the reservation, not another tab.
 assert.equal(step({op:'absent',kind:'notice',since:500},62000),'ok');assert.equal(step({op:'can',kind:'notice',key,since:62000},62000),'taken');
 assert.equal(step({op:'absent',kind:'notice',since:50000,tab:2},62000),'ok');assert.equal(step({op:'can',kind:'notice',key,since:62000},62000),'taken','another tab\'s page');
 assert.equal(step({op:'absent',kind:'notice',since:50000},62000),'ok');
 assert.equal(step({op:'can',kind:'notice',key,since:62100},62100),'ok');assert.equal(step({op:'can',kind:'notice',key:'/student/attentionStudents/80:1',since:62100},62100),'taken');
 // Once freed, a copy loaded before that moment stays taken; a page loaded after it may show the form again.
 assert.equal(step({op:'absent',kind:'notice',since:62500},63000),'ok');
 assert.equal(step({op:'can',kind:'notice',key:'/student/attentionStudents/80:1',since:61000},63000),'taken','a stale copy is never sent');
 assert.equal(step({op:'can',kind:'notice',key:'/student/attentionStudents/80:1',since:62600},63000),'taken','loaded while the POST was on its way');
 assert.equal(step({op:'can',kind:'notice',key:'/student/attentionStudents/80:1',since:63100},63200),'ok','the form shown again by a later page');
 // The sending tab closes: its forms are freed at that moment.
 assert.equal(step({op:'reserve',kind:'agreement',key:'additional:b1',since:130000,tab:3},130000),'ok');
 assert.equal(step({op:'absent',kind:'agreement',since:130001,tab:4},132000),'ok');
 assert.equal(step({op:'can',kind:'agreement',key:'additional:b1',since:132500},132500),'taken','a page in another tab frees nothing');
 assert.equal(step({op:'closed',kind:'agreement',tab:3},133000),'ok');
 assert.equal(step({op:'can',kind:'agreement',key:'additional:b1',since:132900},133100),'taken','opened before the tab closed');
 assert.equal(step({op:'can',kind:'agreement',key:'additional:b1',since:133050},133100),'taken','its POST may still be on its way');
 assert.equal(step({op:'can',kind:'agreement',key:'additional:b1',since:143100},143200),'ok','a page after the grace');
 // Only the reserving tab releases, and a reservation needs a tab.
 assert.equal(step({op:'reserve',kind:'agreement',key:'additional:c1',since:143200,tab:5},143200),'ok');
 step({op:'release',kind:'agreement',key:'additional:c1',tab:6},143300);
 assert.equal(step({op:'can',kind:'agreement',key:'additional:c1',since:143400},143400),'taken','another tab cannot release it');
 assert.equal(api.budgetStep({},{op:'reserve',kind:'notice',key,since:1},2).reason,'limit','no tab, no reservation');
 // A clock set back: the minute counts from now, a later page can still free a form, and a stale copy stays taken.
 const k90='/student/attentionStudents/90:1';
 const back={notice:{reserved:{[k90]:500000},sent:{[k90]:500000},owner:{[k90]:1},attempts:[500000,500000,500000]}};
 assert.equal(api.budgetStep(back,{op:'can',kind:'notice',key:other,since:1},100000).changed,true,'a "can" stores the clamped times');
 let r=api.budgetStep(back,{op:'reserve',kind:'notice',key:'/student/attentionStudents/91:1',since:100000,tab:1},100000);
 assert.deepEqual([r.ok,r.reason],[false,'limit']);
 r=api.budgetStep(r.budgets,{op:'can',kind:'notice',key:other,since:1},159000);assert.equal(r.reason,'limit');
 r=api.budgetStep(r.budgets,{op:'can',kind:'notice',key:other,since:1},160001);assert.equal(r.ok,true,'the minute ends a minute after the clock moved back');
 r=api.budgetStep(r.budgets,{op:'absent',kind:'notice',since:100500,tab:1},101000);
 assert.equal(Object.keys(r.budgets.notice.reserved).length,0,'the reservation is freed by a page after now');
 for(const since of [100600,101500])
  assert.equal(api.budgetStep(r.budgets,{op:'can',kind:'notice',key:k90,since},101600).reason,'taken','a copy from before the clock moved back is not sent: '+since);
 // A release after the clock moved back still undoes the reservation's sent time.
 r=api.budgetStep({notice:{reserved:{[k90]:1000000},sent:{[k90]:1000000},owner:{[k90]:1},attempts:[1000000]}},{op:'release',kind:'notice',key:k90,tab:1},400000);
 assert.equal(api.budgetStep(r.budgets,{op:'can',kind:'notice',key:k90,since:400100},400200).ok,true);
 // Stored values that are not times are dropped, never kept as a form that can never be freed.
 r=api.budgetStep({notice:{reserved:{a:NaN,b:'1',c:Infinity},sent:[1,2],owner:'x',attempts:[NaN,1e400]}},{op:'can',kind:'notice',key:'/student/attentionStudents/92:1',since:1},2);
 assert.deepEqual([r.ok,r.budgets.notice.reserved,r.budgets.notice.sent,r.budgets.notice.owner,r.budgets.notice.attempts],[true,{},{},{},[]]);
 for(const bad of [{op:'reserve',kind:'notice',key:'__proto__'},{op:'reserve',kind:'login',key},{op:'drop',kind:'notice',key},{op:'reserve',kind:'notice',key:'/student/attentionStudents/77'},{op:'absent',kind:'notice',since:'x'},{op:'reserve',kind:'agreement',key},{op:'closed',kind:'notice',tab:1},{op:'can',kind:'notice',key}])
  assert.equal(api.budgetMessage(bad),false,JSON.stringify(bad));
});

test('the background takes the tab from the sender, not the page, and frees a closed tab\'s forms',async()=>{
 const bg=background(),key='/student/attentionStudents/77:ab',t0=Date.now();
 const reserve={type:'confirm',op:'reserve',kind:'notice',key,since:t0,tab:9};
 assert.equal((await bg.deliver(reserve,{url:SITE+'/student/notice/',frameId:0,tab:{id:1}})).ok,true);
 // A page in tab 9 cannot free tab 1's form by naming tab 1 or 9 in its message.
 await bg.deliver({type:'confirm',op:'absent',kind:'notice',since:Date.now()+1000,tab:1},{url:SITE+'/student/bachelor/',frameId:0,tab:{id:9}});
 assert.equal((await bg.deliver({type:'confirm',op:'can',kind:'notice',key,since:Date.now()+5},{url:SITE+'/student/notice/',frameId:0,tab:{id:2}})).reason,'taken');
 await bg.closeTab(1);
 // After a clock set back a "can" that clamps the stored times writes them back (a tab that sees "limit" only ever asks "can").
 bg.budgets.budgets.agreement={reserved:{},sent:{},owner:{},attempts:[Date.now()+600000,Date.now()+600000,Date.now()+600000]};
 assert.equal((await bg.deliver({type:'confirm',op:'can',kind:'agreement',key:'additional:ab',since:Date.now()},{url:SITE+'/student/news/',frameId:0,tab:{id:2}})).reason,'limit');
 assert.ok(bg.budgets.budgets.agreement.attempts.every(at=>at<=Date.now()),'the clamped minute is stored');
 assert.equal((await bg.deliver({type:'confirm',op:'can',kind:'notice',key,since:Date.now()+10005},{url:SITE+'/student/notice/',frameId:0,tab:{id:2}})).ok,true,'freed when its tab closed (after the grace)');
});

test('without session storage, or from a frame or another site, nothing is reserved',async()=>{
 const none=background({session:false});
 assert.deepEqual(await none.deliver({type:'confirm',op:'reserve',kind:'notice',key:'/student/attentionStudents/77:ab'}),{ok:false});
 const bg=background(),message={type:'confirm',op:'reserve',kind:'notice',key:'/student/attentionStudents/77:ab',since:Date.now()};
 assert.equal(await bg.deliver(message,{url:SITE+'/student/bachelor/',frameId:2}),'ignored');
 assert.equal(await bg.deliver(message,{url:'https://example.com/',frameId:0}),'ignored');
 const [a,b]=await Promise.all([bg.deliver(message),bg.deliver(message)]);
 assert.deepEqual([a.ok,b.ok,b.reason],[true,false,'taken'],'two tabs at once: one reservation');
});

test('the read notice is confirmed once with the site\'s own checkbox and button',async()=>{
 const bg=background(),p=confirmPage(bg,'/student/news/',noticeHtml);
 await until(()=>p.sent.posts>0,3000);await pause(900);
 assert.equal(p.sent.posts,1);assert.equal(p.d.querySelector('#check').checked,true);
 p.close();
});

test('a "nothing was sent" answer from the page frees the form but never gives the attempt back',{timeout:30000},async()=>{
 for(const [kind,html,method] of [['notice',noticeHtml,'submitNotice'],['agreement',agreementHtml,'submitAgreement']]){
  // A page script answering the send with "not-ready" (it can forge that) before the bridge sees the call.
  const forge=w=>w.addEventListener('univer-ext-call',e=>{const c=JSON.parse(e.detail);if(c.method!==method)return;
   e.stopImmediatePropagation();w.document.dispatchEvent(new w.CustomEvent('univer-ext-answer',{detail:JSON.stringify({id:c.id,value:'not-ready'})}));},true);
  const bg=background(),p=confirmPage(bg,'/student/news/',html,{before:forge});
  await until(()=>p.messages.filter(m=>m.op==='release').length>=3,6000);await pause(1500);
  const releases=p.messages.filter(m=>m.op==='release');
  assert.ok(releases.every(m=>m.refund!==true&&m.kind===kind),kind+': no refund on an answer from the page');
  assert.equal(plain(bg.budgets.budgets[kind].attempts).length,3,kind+': three attempts, then the minute is spent');
  assert.equal(releases.length,3,kind);assert.equal(p.sent.posts+p.sent.agrees,0,kind);
  p.close();
 }
});

test('two tabs with the same notice send it once, and the other tab never sends its copy',async()=>{
 const bg=background(),a=confirmPage(bg,'/student/news/',noticeHtml),b=confirmPage(bg,'/student/news/',noticeHtml);
 await until(()=>a.sent.posts+b.sent.posts>0,3000);
 // The tab that sent it opens the next page, which reports the form gone and frees the reservation.
 const next=confirmPage(bg,'/student/bachelor/','<p>Главная страница</p>');
 await pause(3000);
 assert.equal(a.sent.posts+b.sent.posts,1);
 assert.deepEqual(plain(bg.budgets.budgets.notice.reserved),{},'the next page freed the form');
 a.close();b.close();next.close();
});

test('a notice the student starts answering is left to the student; with the setting off or unreadable nothing is sent',async()=>{
 const bg=background(),p=confirmPage(bg,'/student/news/',noticeHtml,{settings:{confirm:true}});
 trusted(p.w,p.d.querySelector('label[for=check]'));
 const off=confirmPage(bg,'/student/news/',noticeHtml,{settings:{readable:true,theme:'system',motion:true,confirm:false}});
 const broken=confirmPage(bg,'/student/news/',noticeHtml,{storage:false});
 await pause(1500);
 assert.deepEqual([p.sent.posts,off.sent.posts,broken.sent.posts],[0,0,0]);
 assert.equal(off.d.querySelector('#check').checked,false,'not even the checkbox');
 p.close();off.close();broken.close();
});

test('the agreement is ticked, sent once through agree(), and its AJAX answer ends the work',async()=>{
 const bg=background(),p=confirmPage(bg,'/student/news/',agreementHtml);
 await until(()=>p.sent.agrees>0,3000);await pause(1200);
 assert.equal(p.sent.agrees,1);assert.equal(p.d.getElementById('modal_agreement'),null);
 p.close();
});

test('the page world answers only the five confirmation calls, never sign-in',async()=>{
 const bg=background(),p=confirmPage(bg,'/student/news/','<p>x</p>',{settings:{confirm:false}}),answers=[];
 p.d.addEventListener('univer-ext-answer',e=>answers.push(JSON.parse(e.detail)));
 const ask=(id,method,args=[])=>p.d.dispatchEvent(new p.w.CustomEvent('univer-ext-call',{detail:JSON.stringify({id,method,args})}));
 ask('1','login',['a','b']);ask('2','reauthenticate',['a','b','c','d']);ask('3','constructor');ask('4','__proto__');ask('5','probe');ask('6','switchLanguage',['kz']);
 assert.deepEqual(answers.map(a=>a.id),['5']);
 assert.equal(JSON.parse(answers[0].value).trusted,true);
 p.close();
});
