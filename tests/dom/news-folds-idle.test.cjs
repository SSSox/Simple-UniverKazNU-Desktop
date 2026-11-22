const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const {JSDOM} = require('jsdom');
const common = fs.readFileSync('src/Univer.Desktop/Assets/page-common.js', 'utf8');
const news = common+'\n'+fs.readFileSync('src/Univer.Desktop/Assets/news.js','utf8');
const origin = 'https://univer.kaznu.kz';
const wait = ms => new Promise(resolve=>setTimeout(resolve,ms));
function row(day,name) { return `<tr class="news"><td><div class="date"><span class="day">${day}</span> 10.2026</div></td><td><p>${name}</p></td></tr>`; }
const html = `<table><tr id="l4"><td><table class="univer-app-news"><tbody>${row('09','Длинная новость A')}${row('08','Новость B')}</tbody></table><div><table><tr><td class="tb">Страница 1</td></tr></table></div></td></tr></table>`;

test('an unchanged news page is not touched by repeated apply; a resize or a text change still refolds', async () => {
  const dom = new JSDOM(html,{url:origin+'/news/student',runScripts:'outside-only'}), w = dom.window, d = w.document;
  Object.defineProperty(d,'readyState',{value:'complete',configurable:true});
  w.eval(news);
  let tall = true;
  const proto = w.HTMLElement.prototype, rect = proto.getBoundingClientRect;
  const height = el => el.textContent.includes('Длинная') && tall ? 600 : 80;
  Object.defineProperty(proto,'scrollHeight',{configurable:true,get(){return this.classList.contains('univer-app-news-body') ? height(this) : 0;}});
  proto.getBoundingClientRect = function(){return this.classList.contains('univer-app-news-body') ? {width:720,top:0,height:height(this)} : rect.call(this);};
  w.__univerNewsV1.apply('ru'); await wait(10);
  const body = d.querySelector('.univer-app-news-body'), button = d.querySelector('.univer-app-news-toggle');
  assert.equal(body.getAttribute('data-collapsed'),'true');
  const seen = [], observer = new w.MutationObserver(list => seen.push(...list));
  observer.observe(d.body,{attributes:true,childList:true,characterData:true,subtree:true});
  for (let i = 0; i < 5; i++) w.__univerNewsV1.apply('ru');
  await wait(20); seen.push(...observer.takeRecords());
  assert.equal(seen.length,0,'no mutation: '+seen.map(r=>r.type+':'+(r.attributeName||'')+':'+(r.target.className||r.target.nodeName)).join(' ; '));
  tall = false; w.dispatchEvent(new w.Event('resize')); await wait(20);
  assert.equal(body.getAttribute('data-collapsed'),'false'); assert.equal(button.hidden,true);
  tall = true; body.querySelector('p').textContent = 'Длинная новость A, обновлена'; w.__univerNewsV1.apply('ru'); await wait(20);
  assert.equal(body.getAttribute('data-collapsed'),'true'); assert.equal(button.hidden,false);
  observer.disconnect(); w.__univerNewsV1.release(); w.close();
});
