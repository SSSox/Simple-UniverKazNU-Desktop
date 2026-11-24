const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const {JSDOM} = require('jsdom');

function helper() {
  const dom = new JSDOM('', {url:'https://univer.kaznu.kz/',runScripts:'outside-only'});
  dom.window.TextDecoder = TextDecoder;
  dom.window.eval(fs.readFileSync('src/Univer.Desktop/Assets/page-common.js','utf8'));
  return {read:dom.window.__univerCommonV1.boundedText,close:()=>dom.window.close()};
}

test('chunked pages stop at the byte cap, cancel the reader and never read the remaining response',async()=>{
  const h=helper(); let reads=0,cancels=0,releases=0;
  try {
    const response={body:{getReader:()=>({read:async()=>{reads++;return {value:new Uint8Array(12),done:false};},cancel:async()=>{cancels++;},releaseLock:()=>{releases++;}})}};
    await assert.rejects(h.read(response,20),/Oversized/);
    assert.equal(reads,2); assert.equal(cancels,1); assert.equal(releases,1);
  } finally {h.close();}
});
test('UTF8 split over chunks is decoded once and limits count bytes instead of characters',async()=>{
  const h=helper(); const bytes=new TextEncoder().encode('Тема дәріс'),pieces=[bytes.slice(0,3),bytes.slice(3)],chunks=pieces.slice();
  try {
    const response={body:{getReader:()=>({read:async()=>chunks.length?{value:chunks.shift(),done:false}:{done:true},releaseLock(){}})}};
    assert.equal(await h.read(response,bytes.length),'Тема дәріс');
    await assert.rejects(h.read({text:async()=>'я'.repeat(12)},20),/Oversized/);
    await assert.rejects(h.read({headers:{get:()=>1000},text:async()=>{throw new Error('Must not read');}},20),/Oversized/);
  } finally {h.close();}
});
test('boundedText decodes an explicit charset while streaming, falls back on unknown charsets, and supports arrayBuffer fallback',async()=>{
  const h=helper();
  // "Привет" in windows-1251: CF F0 E8 E2 E5 F2
  const win1251=new Uint8Array([0xCF,0xF0,0xE8,0xE2,0xE5,0xF2]);
  const pieces=[win1251.slice(0,3),win1251.slice(3)];
  try {
    const streamed={body:{getReader:()=>({read:async()=>pieces.length?{value:pieces.shift(),done:false}:{done:true},releaseLock(){}})}};
    assert.equal(await h.read(streamed,100,'windows-1251'),'Привет');
    // Unknown charset falls back to UTF-8.
    const utf8=new TextEncoder().encode('Тест'), utfChunks=[utf8];
    const unknown={body:{getReader:()=>({read:async()=>utfChunks.length?{value:utfChunks.shift(),done:false}:{done:true},releaseLock(){}})}};
    assert.equal(await h.read(unknown,100,'no-such-charset'),'Тест');
    // arrayBuffer fallback when body.getReader is absent and charset is given.
    assert.equal(await h.read({arrayBuffer:async()=>win1251.buffer},100,'windows-1251'),'Привет');
    await assert.rejects(h.read({arrayBuffer:async()=>new ArrayBuffer(30)},20,'utf-8'),/Oversized/);
    // Streaming stops at the byte cap even with a custom charset.
    let reads=0, cancels=0;
    const huge={body:{getReader:()=>({read:async()=>{reads++;return {value:new Uint8Array(12),done:false};},cancel:async()=>{cancels++;},releaseLock(){}})}};
    await assert.rejects(h.read(huge,20,'windows-1251'),/Oversized/);
    assert.equal(reads,2); assert.equal(cancels,1);
  } finally {h.close();}
});
