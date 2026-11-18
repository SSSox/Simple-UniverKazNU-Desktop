// The release build of the browser extension (scripts/build-extension.ps1): what the stores receive. It must ask for one
// site and storage only, run the Windows app's page scripts byte for byte in the Windows app's order, and carry no QA origin.
const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const crypto=require('node:crypto');
const {execFileSync}=require('node:child_process');

const SITE='https://univer.kaznu.kz/*';
const windows=process.platform==='win32';
const read=file=>fs.readFileSync(file,'utf8');
let work,out;

before(()=>{
 if(!windows)return;
 work=fs.mkdtempSync(path.join(os.tmpdir(),'univer-ext-'));
 out=path.join(work,'extension');
 // Started from PowerShell 7 (npm in pwsh, CI), the inherited PSModulePath points Windows PowerShell at PowerShell 7's
 // modules and Get-FileHash goes missing; without it Windows PowerShell rebuilds its own.
 const env={...process.env};
 for(const key of Object.keys(env))if(key.toLowerCase()==='psmodulepath')delete env[key];
 // A relative -OutputRoot, as release.yml passes it: ZIP entry names must still start at the extension's root.
 execFileSync('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.resolve('scripts/build-extension.ps1'),'-OutputRoot','extension'],{stdio:'pipe',env,cwd:work});
});
after(()=>{if(work)fs.rmSync(work,{recursive:true,force:true});});

// The page script list as the Windows app injects it (Web/AutomationController.cs).
function modules(){
 const match=/Bundle = new Lazy<string>\(\(\) => Scripts\(([^)]*)\)\)/.exec(read('src/Univer.Desktop/Web/AutomationController.cs'));
 assert.ok(match,'page script list not found');
 return [...match[1].matchAll(/"([^"]+\.js)"/g)].map(m=>m[1]);
}
const manifestOf=browser=>JSON.parse(read(path.join(out,browser,'manifest.json')));
// Entry names from a ZIP's central directory.
function zipNames(bytes){
 for(let end=bytes.length-22;end>=0;end--){
  if(bytes.readUInt32LE(end)!==0x06054b50)continue;
  const names=[];let at=bytes.readUInt32LE(end+16);
  for(let k=bytes.readUInt16LE(end+10);k>0;k--){
   assert.equal(bytes.readUInt32LE(at),0x02014b50,'central directory entry');
   const n=bytes.readUInt16LE(at+28),extra=bytes.readUInt16LE(at+30),comment=bytes.readUInt16LE(at+32);
   names.push(bytes.toString('utf8',at+46,at+46+n));at+=46+n+extra+comment;
  }
  return names;
 }
 throw new Error('no end of central directory');
}
const filesOf=dir=>fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?filesOf(path.join(dir,e.name)):[path.join(dir,e.name)]);

test('each release manifest asks for univer.kaznu.kz and storage only, with no page access elsewhere',{skip:!windows&&'needs Windows PowerShell'},()=>{
 const source=JSON.parse(read('src/Univer.Extension/manifest.json'));
 for(const browser of ['chromium','firefox']){
  const manifest=manifestOf(browser);
  assert.equal(manifest.manifest_version,3);
  assert.equal(manifest.version,source.version);
  assert.deepEqual(manifest.permissions,['storage'],browser);
  assert.deepEqual(manifest.host_permissions,[SITE],browser);
  assert.equal(manifest.optional_permissions,undefined);
  assert.equal(manifest.optional_host_permissions,undefined);
  for(const key of ['content_security_policy','externally_connectable','web_accessible_resources','update_url'])assert.equal(manifest[key],undefined,browser+' '+key);
  assert.equal(manifest.content_scripts.length,2);
  manifest.content_scripts.forEach(entry=>assert.deepEqual(entry.matches,[SITE],browser));
  assert.equal(manifest.content_scripts[1].world,'MAIN');
  assert.deepEqual(manifest.content_scripts[1].js,['scripts/page.js']);
 }
 const chromium=manifestOf('chromium'),firefox=manifestOf('firefox');
 assert.equal(chromium.background.service_worker,'background.js');
 assert.equal(chromium.minimum_chrome_version,'111');
 assert.deepEqual(firefox.background.scripts,['background.js']);
 assert.equal(firefox.browser_specific_settings.gecko.id,'simple-univer@sox-team');
 assert.deepEqual(firefox.browser_specific_settings.gecko.data_collection_permissions,{required:['none']});
});

// What the stores' upload checks warn about: Edge cuts short_name at 12 characters, and Firefox's linter marks every
// assignment of HTML text ("Unsafe assignment to innerHTML"), even of a constant icon.
test('the release files pass the stores\' package checks: a short name of 12 characters, no HTML text written into the page',{skip:!windows&&'needs Windows PowerShell'},()=>{
 for(const browser of ['chromium','firefox']){
  const name=manifestOf(browser).short_name;
  assert.ok(name&&name.length<=12,browser+' short_name '+name);
  for(const file of filesOf(path.join(out,browser)).filter(file=>file.endsWith('.js'))){
   const found=/\.(?:inner|outer)HTML\s*\+?=(?!=)|\.insertAdjacentHTML\s*\(|document\.write(?:ln)?\s*\(/.exec(read(file));
   assert.equal(found,null,path.relative(out,file)+' writes HTML text: '+(found&&found[0]));
  }
 }
});

test('the page scripts run in the Windows app\'s order, each byte for byte in the Windows app\'s strict wrapper',{skip:!windows&&'needs Windows PowerShell'},()=>{
 const list=modules(),isolated=list.filter(name=>name!=='univer-automation.js');
 assert.ok(list.includes('univer-automation.js')&&isolated.length>=18,'unexpected list '+list.join());
 for(const browser of ['chromium','firefox']){
  const js=manifestOf(browser).content_scripts[0].js;
  assert.deepEqual(js,['scripts/prelude.js',...isolated.map(name=>'scripts/modules/'+name),'scripts/host.js','scripts/downloads.js','scripts/driver.js'],browser);
  for(const name of isolated){
   const expected="(function(__univerNative){'use strict';\n"+read('src/Univer.Desktop/Assets/'+name)+"\n})(globalThis.__univerExtV1?globalThis.__univerExtV1.send:null);\n";
   assert.equal(read(path.join(out,browser,'scripts','modules',name)),expected,browser+' '+name);
  }
  for(const name of ['prelude.js','host.js','downloads.js','driver.js'])
   assert.equal(read(path.join(out,browser,'scripts',name)),read('src/Univer.Extension/scripts/'+name),browser+' '+name);
  const page=read(path.join(out,browser,'scripts','page.js'));
  assert.ok(page.includes(read('src/Univer.Desktop/Assets/univer-automation.js')),browser+' page.js lacks univer-automation.js');
  assert.ok(!page.includes('/*@MODULES@*/'),browser+' page.js kept its placeholder');
 }
});

test('the release files name no local QA origin and the archives match their checksums',{skip:!windows&&'needs Windows PowerShell'},()=>{
 const version=JSON.parse(read('src/Univer.Extension/manifest.json')).version;
 for(const browser of ['chromium','firefox']){
  for(const file of filesOf(path.join(out,browser)).filter(f=>/\.(js|json|html)$/.test(f)))
   assert.doesNotMatch(read(file),/127\.0\.0\.1|localhost/,path.relative(out,file));
  const zip=path.join(work,`Univer-Extension-${version}-${browser}.zip`);
  const bytes=fs.readFileSync(zip);
  const hash=crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
  assert.equal(read(zip+'.sha256'),`${hash}  ${path.basename(zip)}`);
  // The stores and Firefox want manifest.json at the root and forward slashes; the archive holds exactly the build.
  const expected=filesOf(path.join(out,browser)).map(f=>path.relative(path.join(out,browser),f).split(path.sep).join('/')).sort();
  assert.deepEqual(zipNames(bytes).sort(),expected,browser+' zip entries');
  assert.ok(expected.includes('manifest.json')&&expected.includes('scripts/modules/page-common.js'));
 }
});
