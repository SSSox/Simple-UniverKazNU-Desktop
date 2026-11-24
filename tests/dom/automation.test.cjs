const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { JSDOM } = require('jsdom');
const script = fs.readFileSync('src/Univer.Desktop/Assets/univer-automation.js', 'utf8');
const desktop = fs.readFileSync('src/Univer.Desktop/Assets/desktop.js', 'utf8');
const common = fs.readFileSync('src/Univer.Desktop/Assets/page-common.js', 'utf8');
const layout = common + '\n' + fs.readFileSync('src/Univer.Desktop/Assets/univer-layout.js', 'utf8');
const paint = fs.readFileSync('src/Univer.Desktop/Assets/paint-gate.js', 'utf8');

function page(html, url = 'https://univer.kaznu.kz/user/login', bootstrap = script) {
  const dom = new JSDOM(html, { url, runScripts: 'outside-only' });
  const w = dom.window;
  Object.defineProperty(w.document, 'readyState', { value: 'complete', configurable: true });
  w.HTMLElement.prototype.getClientRects = function () {
    const hidden = this.closest('[hidden], [style*="display:none"], [style*="display: none"]');
    return hidden ? [] : [{ width: 100, height: 30 }];
  };
  const hooks = {};
  const jq = () => ({ bind: (event, fn) => { hooks[event] = fn; } });
  w.jQuery = jq;
  const run = source => new vm.Script(source).runInContext(dom.getInternalVMContext());
  run(bootstrap);
  return { w, api: w.__univerAutomationV1, hooks, run, close: () => w.close() };
}

// Execute the actual C# script expressions, with presentation work omitted. A hand-written native caller would miss
// a regression where the controller accidentally goes back to resolving the public window property.
const controller = fs.readFileSync('src/Univer.Desktop/Web/AutomationController.cs', 'utf8');
const nativeBinding = '__univerAutomation_fixture';
const reference = /private string AutomationReference\s*{\s*get\s*{\s*return ([\s\S]*?);\s*}\s*}/.exec(controller);
const nativeReference = reference ? new Function('automationBinding', `return ${reference[1]}`)(nativeBinding) : null;
function nativeExpression(signature, pattern, overrides = {}) {
  const body = controller.slice(controller.indexOf(signature));
  const expression = pattern.exec(body)[1];
  const values = {
    bundle: script, prefs: {}, viewer: false, active: true, TimingScript: '', WakeScript: '',
    ConfigurePaint: () => '', Early: () => '', ApplyView: () => '', ViewScript: () => '',
    NativeChannel: { Script: () => 'null' }, MessageKey: 'fixture-channel', automationBinding: nativeBinding,
    AutomationReference: nativeReference,
    Quote: JSON.stringify, string: { Join: (separator, items) => items.join(separator) }, ...overrides
  };
  return new Function(...Object.keys(values), `return ${expression}`)(...Object.values(values));
}
function nativePage(html, url) {
  return page(html, url, nativeExpression('public string DocumentScript(', /return ([\s\S]*?);\r?\n\s*}/));
}
function nativeProbe(p) {
  return p.run(nativeExpression('private string StepScript(', /return ([\s\S]*?);\r?\n\s*}/));
}
function nativeCall(p, method, ...args) {
  return p.run(nativeExpression('public async Task<string> CallAsync(', /return await EvaluateAsync\(browser, ([\s\S]*?)\);/, {
    method, quoted: args.map(JSON.stringify)
  }));
}
function replaceAutomation(p, received) {
  const fake = {
    probe: () => ({ trusted: true, ready: true, document: 'forged', reauth: true, reauthReady: true,
      reauthAttempt: 1, reauthChallenge: 'forged-challenge', loginReady: true, loginPage: true }),
    login: (...args) => received.push(args), reauthenticate: (...args) => received.push(args)
  };
  p.w.__univerAutomationV1 = fake;
  return fake;
}

test('native probes and credential calls ignore a page replacement on an ordinary page without a form', () => {
  const p = nativePage('<p>News</p>', 'https://univer.kaznu.kz/news/student/1/'), received = [];
  replaceAutomation(p, received);
  const probe = JSON.parse(nativeProbe(p)).probe;
  if (probe.reauthReady) nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password');
  if (probe.loginReady) nativeCall(p, 'login', probe.document, 'fixture-account', 'fixture-password');
  assert.deepEqual(received, []);
  assert.equal(probe.reauth, false);
  assert.equal(probe.loginReady, false);
  nativeCall(p, 'reauthenticate', 'forged', 'forged-challenge', 'fixture-account', 'fixture-password');
  nativeCall(p, 'login', 'forged', 'fixture-account', 'fixture-password');
  assert.deepEqual(received, []);
  p.close();
});

test('native login keeps the document-start methods when the page replaces the object or its methods', () => {
  const p = nativePage('<form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form>');
  p.w.document.querySelector('[name=btn]').addEventListener('click', e => e.preventDefault());
  const received = [], probe = JSON.parse(nativeProbe(p)).probe;
  const fake = replaceAutomation(p, received);
  try { p.api.login = fake.login; p.api.probe = fake.probe; } catch (_) { }
  p.w[nativeBinding] = fake;
  assert.throws(() => p.run(`${nativeBinding}=window.__univerAutomationV1`), /constant/i);
  assert.equal(nativeCall(p, 'login', probe.document, 'fixture-account', 'fixture-password'), 'submitted');
  assert.deepEqual(received, []);
  assert.equal(p.w.document.querySelector('[type=password]').value, 'fixture-password');
  assert.equal(p.w[nativeBinding], fake, 'a same-named window property cannot replace the private lexical reference');
  p.close();
});

test('native reauthentication keeps its private handler after the page replaces the public object', () => {
  const p = nativePage(reauthHtml(), 'https://univer.kaznu.kz/news/student/1/'), received = [];
  p.w.ajl = () => {};
  p.w.document.querySelector('#modal_auth_ajl_btn').addEventListener('click', e => e.preventDefault());
  const probe = JSON.parse(nativeProbe(p)).probe;
  replaceAutomation(p, received);
  assert.equal(nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password'), 'submitted');
  assert.deepEqual(received, []);
  assert.equal(p.w.document.querySelector('#modal_auth_pass').value, 'fixture-password');
  p.close();
});

test('a missed document-start installation never falls back to the page handler for credentials', () => {
  const p = page('<p>News</p>', 'https://univer.kaznu.kz/news/student/1/', ''), received = [];
  replaceAutomation(p, received);
  assert.equal(nativeProbe(p), 'bootstrap');
  nativeCall(p, 'login', 'forged', 'fixture-account', 'fixture-password');
  nativeCall(p, 'reauthenticate', 'forged', 'forged-challenge', 'fixture-account', 'fixture-password');
  assert.deepEqual(received, []);
  p.close();
});

test('credentials are withheld if a field event changes the login or reauthentication form origin', () => {
  for (const reauth of [false, true]) {
    const p = page(reauth ? reauthHtml().replace('readonly', '') :
      '<form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form>',
    reauth ? 'https://univer.kaznu.kz/news/student/1/' : undefined);
    p.w.ajl = () => {};
    const form = p.w.document.querySelector('form'), password = form.querySelector('[type=password]');
    form.querySelector('[type=text]').addEventListener('input', () => { form.action = 'https://evil.test/collect'; });
    form.querySelector('[type=submit]').addEventListener('click', e => e.preventDefault());
    const probe = p.api.probe();
    const result = reauth ? p.api.reauthenticate(probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password') :
      p.api.login(probe.document, 'fixture-account', 'fixture-password');
    assert.equal(result, 'not-ready');
    assert.equal(password.value, '');
    p.close();
  }
});

test('a replaced DOM query cannot fill a detached credential form on an ordinary page', () => {
  const p = nativePage(reauthHtml(), 'https://univer.kaznu.kz/news/student/1/');
  p.w.ajl = () => {};
  const probe = JSON.parse(nativeProbe(p)).probe, form = p.w.document.querySelector('form');
  form.remove();
  const select = p.w.document.querySelector.bind(p.w.document);
  p.w.document.querySelector = selector => selector.includes('modal_auth_frm') ? form : select(selector);
  assert.equal(nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password'), 'not-ready');
  assert.equal(form.querySelector('[type=password]').value, '');
  p.close();
});

test('a form action getter replaced by the page cannot disguise a foreign submission target', () => {
  const p = nativePage(reauthHtml(), 'https://univer.kaznu.kz/news/student/1/');
  p.w.ajl = () => {};
  const probe = JSON.parse(nativeProbe(p)).probe, form = p.w.document.querySelector('form');
  form.setAttribute('action', 'https://evil.test/collect');
  Object.defineProperty(form, 'action', { value: 'https://univer.kaznu.kz/user/ajl' });
  assert.equal(nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password'), 'not-ready');
  assert.equal(form.querySelector('[type=password]').value, '');
  p.close();
});

test('a URL origin getter replaced by the page cannot disguise a foreign submission target', () => {
  const p = nativePage(reauthHtml(), 'https://univer.kaznu.kz/news/student/1/');
  p.w.ajl = () => {};
  const probe = JSON.parse(nativeProbe(p)).probe, form = p.w.document.querySelector('form');
  form.setAttribute('action', 'https://evil.test/collect');
  Object.defineProperty(p.w.URL.prototype, 'origin', { get: () => 'https://univer.kaznu.kz' });
  form.querySelector('[type=submit]').addEventListener('click', e => e.preventDefault());
  assert.equal(nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password'), 'not-ready');
  assert.equal(form.querySelector('[type=password]').value, '');
  p.close();
});

test('native credential calls reject a stale document, removed form, or changed login URL after the probe', () => {
  for (const change of ['stale', 'removed', 'url']) {
    const p = nativePage('<form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form>');
    const probe = JSON.parse(nativeProbe(p)).probe, form = p.w.document.querySelector('form');
    if (change === 'removed') form.remove();
    if (change === 'url') p.w.history.replaceState(null, '', '/news/student/1/');
    nativeCall(p, 'login', change === 'stale' ? 'previous-document' : probe.document, 'fixture-account', 'fixture-password');
    assert.equal(form.querySelector('[type=text]').value, '');
    assert.equal(form.querySelector('[type=password]').value, '');
    p.close();
  }
});

test('page callbacks cannot reach a native caller with credential arguments through caller or arguments', () => {
  const p = nativePage(reauthHtml(), 'https://univer.kaznu.kz/news/student/1/');
  p.w.ajl = () => {};
  const probe = JSON.parse(nativeProbe(p)).probe;
  p.w.document.querySelector('#modal_auth_ajl_btn').addEventListener('click', e => e.preventDefault());
  p.w.eval(`
    window.leaked = [];
    var select = document.querySelector;
    document.querySelector = function intercepted(selector) {
      try { if (intercepted.caller) leaked.push(intercepted.caller.arguments); } catch (_) {}
      return select.call(this, selector);
    };
  `);
  assert.equal(nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password'), 'submitted');
  assert.equal(p.w.leaked.length, 0);
  p.close();
});

for (const reauth of [false, true]) for (const interception of ['setAttribute', 'dispatchEvent', 'Event']) {
  test(`native ${reauth ? 'reauthentication' : 'login'} bypasses the page replacement of ${interception}`, () => {
    const p = nativePage(reauth ? reauthHtml().replace('readonly', '') :
      '<form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form>',
    reauth ? 'https://univer.kaznu.kz/news/student/1/' : undefined);
    p.w.ajl = () => {};
    const form = p.w.document.querySelector('form'), received = [], events = [], submitted = [];
    for (const type of ['input', 'change']) form.addEventListener(type, event => {
      events.push([type, event.target.value]);
    });
    form.querySelector('[type=submit]').addEventListener('click', event => {
      event.preventDefault();
      submitted.push([form.querySelector('[type=text]').value, form.querySelector('[type=password]').value]);
    });
    const probe = JSON.parse(nativeProbe(p)).probe;
    if (interception === 'setAttribute') {
      const original = p.w.Element.prototype.setAttribute;
      p.w.Element.prototype.setAttribute = function (name, value) {
        if (name === 'value') received.push(value);
        return Reflect.apply(original, this, [name, value]);
      };
    } else if (interception === 'dispatchEvent') {
      const original = p.w.EventTarget.prototype.dispatchEvent;
      p.w.EventTarget.prototype.dispatchEvent = function (event) {
        if (this instanceof p.w.HTMLInputElement) received.push(this.value);
        return Reflect.apply(original, this, [event]);
      };
    } else {
      const Original = p.w.Event;
      p.w.Event = function (type, options) {
        received.push(form.querySelector('[type=password]').value);
        return new Original(type, options);
      };
    }
    const result = reauth ? nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password') :
      nativeCall(p, 'login', probe.document, 'fixture-account', 'fixture-password');
    assert.equal(result, 'submitted');
    assert.deepEqual(received, [], 'replaced DOM APIs must not be called with credentials in the form');
    assert.deepEqual(submitted, [['fixture-account', 'fixture-password']], 'the original site handler still submits');
    assert.deepEqual(events, [['input', 'fixture-account'], ['change', 'fixture-account'],
      ['input', 'fixture-password'], ['change', 'fixture-password']], 'site input/change listeners still run');
    assert.equal(form.querySelector('[type=password]').getAttribute('value'), 'fixture-password', 'legacy jQuery still sees the value attribute');
    p.close();
  });
}

test('native reauthentication uses captured DOM APIs for a form in a same-origin frame', () => {
  const p = nativePage('<iframe></iframe>', 'https://univer.kaznu.kz/news/student/1/');
  const child = page(reauthHtml().replace('readonly', ''), 'https://univer.kaznu.kz/student/news');
  const frame = p.w.document.querySelector('iframe'), received = [], events = [];
  Object.defineProperty(frame, 'contentDocument', { value: child.w.document });
  Object.defineProperty(child.w, 'frameElement', { value: frame });
  child.w.ajl = () => {};
  const form = child.w.document.querySelector('form');
  let clicks = 0;
  form.addEventListener('input', event => events.push(event.target.value));
  form.querySelector('[type=submit]').addEventListener('click', event => { event.preventDefault(); clicks++; });
  const probe = JSON.parse(nativeProbe(p)).probe;
  for (const w of [p.w, child.w]) {
    const attribute = w.Element.prototype.setAttribute, dispatch = w.EventTarget.prototype.dispatchEvent, Original = w.Event;
    w.Element.prototype.setAttribute = function (name, value) {
      received.push(value);
      return Reflect.apply(attribute, this, [name, value]);
    };
    w.EventTarget.prototype.dispatchEvent = function (event) {
      received.push(this.value);
      return Reflect.apply(dispatch, this, [event]);
    };
    w.Event = function (type, options) { received.push(type); return new Original(type, options); };
  }
  assert.equal(nativeCall(p, 'reauthenticate', probe.document, probe.reauthChallenge, 'fixture-account', 'fixture-password'), 'submitted');
  assert.deepEqual(received, []);
  assert.deepEqual(events, ['fixture-account', 'fixture-password']);
  assert.equal(form.querySelector('[type=password]').value, 'fixture-password');
  assert.equal(clicks, 1);
  p.close(); child.close();
});

test('dynamic login uses visible inputs and clicks the original submit handler once', () => {
  const p = page('<form id="login_frm" action="/user/login"><input type="text" hidden value="decoy"><input type="text" id="dynamic-a"><input type="password" id="dynamic-b"><input type="hidden" name="key" value="fixture-nonce"><input name="btn" type="submit"></form>');
  let clicks = 0;
  p.w.document.querySelector('[name=btn]').addEventListener('click', e => { e.preventDefault(); clicks++; });
  const probe = p.api.probe();
  assert.equal(probe.loginReady, true);
  assert.equal(p.api.login(probe.document, 'local-fixture', 'local-fixture-password'), 'submitted');
  assert.equal(p.w.document.querySelector('#dynamic-a').value, 'local-fixture');
  assert.equal(p.w.document.querySelector('[name=key]').value, 'fixture-nonce');
  assert.equal(p.api.login(probe.document, 'local-fixture', 'local-fixture-password'), 'wait');
  assert.equal(clicks, 1);
  assert.equal(JSON.stringify(p.api.probe()).includes('local-fixture-password'), false);
  p.close();
});

test('a signed-in page is recognised by the header logout link, never on the login form or by another origin', () => {
  const signed = page('<div class="header"><a href="/user/logout">Выход</a></div><p>Новости</p>', 'https://univer.kaznu.kz/news/student');
  assert.equal(signed.api.probe().authenticated, true); signed.close();
  const foreign = page('<a href="https://evil.test/user/logout">Выход</a><p>Новости</p>', 'https://univer.kaznu.kz/news/student');
  assert.equal(foreign.api.probe().authenticated, false); foreign.close();
  const login = page('<form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form><a href="/user/logout">Выход</a>');
  assert.equal(login.api.probe().authenticated, false); login.close();
});

test('stale document token and off-origin login form never submit', () => {
  const p = page('<form id="login_frm" action="https://evil.test/"><input type="text"><input type="password"><input name="btn" type="submit"></form>');
  assert.equal(p.api.probe().loginReady, false);
  assert.equal(p.api.login('stale-token', 'local-fixture', 'local-fixture-password'), 'wait');
  p.close();
});

test('the whole automation refuses a lookalike origin', () => {
  const p = page('<form id="login_frm"></form>', 'https://univer.kaznu.kz.evil.test/user/login');
  assert.equal(p.api, undefined);
  p.w.eval(desktop); assert.equal(p.w.__univerDesktop, undefined); p.close();
});

function noticeHtml() {
  return '<form id="attentionSt" action="/student/attentionStudents/123"><p>Notice fixture</p><input id="check" type="checkbox"><label for="check">Я подтверждаю, что прочёл данное уведомление</label><input id="btn" type="submit" disabled></form>';
}
test('known notice runs checkbox handler and waits for enabled submit', () => {
  const p = page(noticeHtml(), 'https://univer.kaznu.kz/student/news');
  let clicks = 0;
  p.w.document.querySelector('#check').addEventListener('click', () => { p.w.document.querySelector('#btn').disabled = false; });
  p.w.document.querySelector('#btn').addEventListener('click', e => { e.preventDefault(); clicks++; });
  const probe = p.api.probe();
  assert.equal(probe.notice, true);
  assert.equal(p.api.prepareNotice(probe.document, probe.noticeEpisode), 'ready');
  assert.equal(p.api.submitNotice(probe.document, probe.noticeEpisode), 'submitted');
  assert.equal(p.api.submitNotice(probe.document, probe.noticeEpisode), 'wait');
  assert.equal(clicks, 1); p.close();
});

test('arbitrary confirmation and disabled notice cannot be submitted', () => {
  const p = page(noticeHtml().replace('Я подтверждаю, что прочёл данное уведомление', 'Подтверждаю оплату'), 'https://univer.kaznu.kz/student/news');
  assert.equal(p.api.probe().notice, false); p.close();
  const disabled = page(noticeHtml(), 'https://univer.kaznu.kz/student/news');
  const probe = disabled.api.probe();
  assert.equal(disabled.api.prepareNotice(probe.document, probe.noticeEpisode), 'wait');
  // 'not-ready' certifies that nothing was submitted, so the native reservation can be returned.
  assert.equal(disabled.api.submitNotice(probe.document, probe.noticeEpisode), 'not-ready'); disabled.close();
});

test('a login button disabled after the probe reports not-ready and the same page can still sign in', () => {
  const p = page('<form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form>');
  const button = p.w.document.querySelector('[name=btn]'); let clicks = 0;
  button.addEventListener('click', e => { e.preventDefault(); clicks++; });
  const probe = p.api.probe(); assert.equal(probe.loginReady, true);
  button.disabled = true;
  assert.equal(p.api.login(probe.document, 'local-fixture', 'local-fixture-password'), 'not-ready');
  assert.equal(clicks, 0);
  button.disabled = false;
  assert.equal(p.api.login(probe.document, 'local-fixture', 'local-fixture-password'), 'submitted');
  assert.equal(p.api.login(probe.document, 'local-fixture', 'local-fixture-password'), 'wait');
  assert.equal(clicks, 1); p.close();
});

test('login becomes ready from the parsed DOM when a stalled resource keeps readyState interactive', () => {
  const p = page('<form id="login_frm" action="/user/login"><input type="text"><input type="password"><input name="btn" type="submit"></form>');
  Object.defineProperty(p.w.document, 'readyState', { value: 'interactive', configurable: true });
  const realNow = p.w.Date.now; let offset = 0; p.w.Date.now = () => realNow() + offset;
  assert.equal(p.api.probe().loginReady, false);
  offset = 7000;
  assert.equal(p.api.probe().loginReady, true);
  p.close();
});

test('notice inside a same-origin visible frame is found and clicked', () => {
  const p = page('<iframe></iframe>', 'https://univer.kaznu.kz/student/news');
  const frame = p.w.document.querySelector('iframe');
  const child = page(noticeHtml(), 'https://univer.kaznu.kz/student/attentionStudents/123');
  Object.defineProperty(frame, 'contentDocument', { value: child.w.document });
  Object.defineProperty(child.w, 'frameElement', { value: frame });
  child.w.document.querySelector('#check').addEventListener('click', () => child.w.document.querySelector('#btn').disabled = false);
  child.w.document.querySelector('#btn').addEventListener('click', e => e.preventDefault());
  const probe = p.api.probe(); assert.equal(probe.notice, true);
  assert.equal(p.api.prepareNotice(probe.document, probe.noticeEpisode), 'ready');
  assert.equal(p.api.submitNotice(probe.document, probe.noticeEpisode), 'submitted');
  p.close(); child.close();
});

function reauthHtml() {
  return '<div id="auth_modal"><span id="auth_try">1</span><form id="modal_auth_frm" action="/user/ajl"><input id="modal_auth_login" name="login" type="text" readonly value="fixture-account"><input id="modal_auth_pass" name="password" type="password"><input id="modal_auth_key" name="key" type="hidden" value="fixture-key"><input name="cheat" type="hidden" value="fixture-cheat"><input id="modal_auth_ajl_btn" type="submit"></form></div>';
}
test('the login form notes a hash of the account it signs in, by a click or Enter, never the login itself', () => {
  const p = page('<form id="login_frm"><input type="text" value=" Student.One "><input type="password" value="x"><button type="button">Войти</button></form>', 'https://univer.kaznu.kz/user/login');
  p.w.document.querySelector('button').click();
  const mark = p.w.localStorage.getItem('univer.app.account.v1');
  assert.match(mark, /^a[0-9a-f]+$/); assert.ok(!mark.includes('student'));
  p.w.document.querySelector('input').value = 'student.one';
  p.w.document.querySelector('input').dispatchEvent(new p.w.KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
  assert.equal(p.w.localStorage.getItem('univer.app.account.v1'), mark, 'case and spaces do not make another account');
  p.w.document.querySelector('input').value = 'student.two'; p.w.document.querySelector('button').click();
  assert.notEqual(p.w.localStorage.getItem('univer.app.account.v1'), mark); p.close();
});
// The site's own submit handler may rewrite the visible field (its JS encrypts the form): the mark the press took stays.
test('a submit after the press does not replace the mark with what the site wrote into the field', () => {
  const p = page('<form id="login_frm"><input type="text" value="student.one"><input type="password" value="x"><button type="submit">Войти</button></form>', 'https://univer.kaznu.kz/user/login');
  const form = p.w.document.getElementById('login_frm'), field = p.w.document.querySelector('input');
  form.addEventListener('submit', event => { event.preventDefault(); });
  p.w.document.querySelector('button').addEventListener('click', () => { field.value = 'X9f0e1d2c3b4a'; });
  p.w.document.querySelector('button').click();
  assert.equal(p.w.localStorage.getItem('univer.app.account.v1'), 'ad6afa8d2', 'the typed login is the mark'); p.close();
});
// The app compares the page's mark with Core/PageAccount.cs's (the readings it keeps): the same vectors are pinned in
// tests/Univer.Desktop.Tests/PageAccountTests.cs, so neither side can change its hash alone.
test('the login mark of the page is the FNV-1a hash the app computes for the same login', () => {
  const vectors = [
    ['Student.One', 'ad6afa8d2'], [' Student.One ', 'ad6afa8d2'], ['student.one', 'ad6afa8d2'],
    ['Студент01', 'abd258a5c'], ['  СТУДЕНТ01 ', 'abd258a5c'], ['qa-grades', 'ae94b0d06'], ['a', 'ae40c292c']
  ];
  for (const [login, mark] of vectors) {
    const p = page('<form id="login_frm"><input type="text"><input type="password" value="x"><button type="button">Войти</button></form>', 'https://univer.kaznu.kz/user/login');
    p.w.document.querySelector('input').value = login;
    p.w.document.querySelector('button').click();
    assert.equal(p.w.localStorage.getItem('univer.app.account.v1'), mark, JSON.stringify(login)); p.close();
  }
  for (const empty of ['', '   ']) {
    const p = page('<form id="login_frm"><input type="text"><input type="password" value="x"><button type="button">Войти</button></form>', 'https://univer.kaznu.kz/user/login');
    p.w.document.querySelector('input').value = empty;
    p.w.document.querySelector('button').click();
    assert.equal(p.w.localStorage.getItem('univer.app.account.v1'), null, 'an empty login notes no mark'); p.close();
  }
});
test('reauth preserves readonly login and nonce; success requires AJAX code 1', () => {
  const p = page(reauthHtml(), 'https://univer.kaznu.kz/student/news'); p.w.ajl = () => {};
  let clicks = 0;
  p.w.document.querySelector('#modal_auth_ajl_btn').addEventListener('click', e => { e.preventDefault(); clicks++; });
  const probe = p.api.probe();
  assert.equal(p.api.reauthenticate(probe.document, probe.reauthChallenge, 'fixture-account', 'local-fixture-password'), 'submitted');
  assert.equal(p.w.document.querySelector('#modal_auth_login').readOnly, true);
  assert.equal(p.w.document.querySelector('#modal_auth_key').value, 'fixture-key');
  assert.equal(p.w.document.querySelector('[name=cheat]').value, 'fixture-cheat');
  p.w.document.querySelector('#auth_modal').hidden = true;
  assert.equal(p.api.probe().reauthOutcome, 'pending');
  p.hooks['ajaxSuccess.univerApp'](null, { responseText: '{"code":1}' }, { url: '/user/ajl', type: 'POST' });
  assert.equal(p.api.probe().reauthOutcome, 'success'); assert.equal(clicks, 1); p.close();
});
test('reauth accepts the bound login in another case or with spaces, and keeps the readonly field as the site wrote it', () => {
  const p = page(reauthHtml(), 'https://univer.kaznu.kz/student/news'); p.w.ajl = () => {};
  p.w.document.querySelector('#modal_auth_ajl_btn').addEventListener('click', e => e.preventDefault());
  p.w.document.querySelector('#modal_auth_login').value = ' Fixture-Account ';
  const probe = p.api.probe();
  assert.equal(p.api.reauthenticate(probe.document, probe.reauthChallenge, 'fixture-account', 'local-fixture-password'), 'submitted');
  assert.equal(p.w.document.querySelector('#modal_auth_login').value, ' Fixture-Account '); p.close();
});
test('reauth rejects a different account and a failed AJAX response', () => {
  const p = page(reauthHtml(), 'https://univer.kaznu.kz/student/news'); p.w.ajl = () => {};
  p.w.document.querySelector('#modal_auth_ajl_btn').addEventListener('click', e => e.preventDefault());
  const probe = p.api.probe();
  assert.equal(p.api.reauthenticate(probe.document, probe.reauthChallenge, 'other-account', 'local-fixture-password'), 'account-mismatch');
  assert.equal(p.w.document.querySelector('#modal_auth_pass').value, '');
  p.api.reauthenticate(probe.document, probe.reauthChallenge, 'fixture-account', 'local-fixture-password');
  p.hooks['ajaxSuccess.univerApp'](null, { responseText: '{"code":0}' }, { url: '/user/ajl', type: 'POST' });
  assert.equal(p.api.probe().reauthOutcome, 'failure'); p.close();
});
test('agreement label split by a line break or non-breaking space is still recognised', () => {
  const p = page('<div id="modal_agreement"><form id="agree_frm" action="/user/setagree"><input id="iconfirm" type="checkbox"><label for="iconfirm">Прочитал\n  договор</label><button class="btn-confirm" onclick="agree()">Согласен</button></form></div>', 'https://univer.kaznu.kz/student/news');
  p.w.agree = () => {};
  assert.equal(p.api.probe().agreement, true); p.close();
});
test('only the known agreement calls its own confirm handler', () => {
  const p = page('<div id="modal_agreement"><form id="agree_frm" action="/user/setagree"><input id="iconfirm" type="checkbox"><label for="iconfirm">Прочитал договор</label><button class="btn-confirm" onclick="agree()" disabled>Согласен</button></form></div>', 'https://univer.kaznu.kz/student/news');
  p.w.agree = () => {}; let clicks = 0;
  p.w.document.querySelector('#iconfirm').addEventListener('change', () => p.w.document.querySelector('.btn-confirm').disabled = false);
  p.w.document.querySelector('.btn-confirm').addEventListener('click', e => { e.preventDefault(); clicks++; });
  const probe = p.api.probe();
  assert.equal(probe.agreement, true);
  p.api.checkAgreement(probe.document, probe.agreementEpisode);
  assert.equal(p.api.submitAgreement(probe.document, probe.agreementEpisode), 'submitted');
  assert.equal(p.api.submitAgreement(probe.document, probe.agreementEpisode), 'wait'); assert.equal(clicks, 1);
  p.hooks['ajaxSuccess.univerApp'](null, {}, { url: '/user/setagree', type: 'POST' });
  assert.equal(p.api.probe().agreementOutcome, 'success'); p.close();
});
test('desktop mode keeps the original DOM, handlers and attendance matrix', () => {
  const p = page('<table><tr><td rowspan="2">Attendance</td><td>1</td></tr><tr><td>2</td></tr></table><input id="check" type="checkbox">', 'https://univer.kaznu.kz/student/news');
  const table = p.w.document.querySelector('table'); const cell = table.rows[0].cells[0]; let clicks = 0;
  p.w.document.querySelector('#check').addEventListener('click', () => clicks++);
  p.w.eval(desktop); p.w.__univerDesktop.apply(true, false); assert.ok(p.w.document.querySelector('#univer-desktop-style'));
  p.w.__univerDesktop.apply(false, false); assert.equal(p.w.document.querySelector('#univer-desktop-style'), null);
  assert.equal(p.w.document.querySelector('table'), table); assert.equal(cell.rowSpan, 2);
  p.w.document.querySelector('#check').click(); assert.equal(clicks, 1); p.close();
});
test('a flag click sends no language message: only the native navigation check changes the language', () => {
  const p = page('<div class="header"><a href="/lang/change/en/">English</a></div>', 'https://univer.kaznu.kz/student/news'), sent = [];
  p.w.__univerNative = value => sent.push(value);
  p.w.document.querySelector('a').addEventListener('click', e => e.preventDefault());
  p.w.eval(desktop); p.w.document.querySelector('a').click();
  assert.deepEqual(sent, []); assert.equal(p.w.__univerDesktop.language, undefined); p.close();
});
test('legacy document Back asks to close the viewer', () => {
  const p = page('<p>Document</p>', 'https://univer.kaznu.kz/student/transcript');
  p.w.eval(desktop); p.w.__univerDesktop.apply(true, true); p.w.history.back(); assert.equal(p.w.__univerDesktop.close, true); p.close();
});

function adapt(p, language = 'ru') { p.w.eval(layout); p.w.eval(desktop); p.w.__univerDesktop.apply(true, false, language); }
// The agenda opens on today when today is a lesson day of the shown week; tests that expect "All days" pin a Sunday.
function pinDate(p, at) { const Real = p.w.Date, time = at.getTime(); p.w.Date = class extends Real { constructor(...a) { if (a.length) super(...a); else super(time); } static now() { return time; } }; }

function scheduleFixture() {
  return '<table class="wrappertable"><tr id="l4"><td class="center"><table class="mt"><tr><td class="lt"></td><td class="ct">Schedule</td><td class="rt"></td></tr><tr><td class="lt"></td><td class="ts"><div style="float:right;width:30%"><select id="weekSelect"><option>Week 1</option><option>Week 2</option></select></div></td><td class="rt"></td></tr></table><table class="schedule"><tr><th></th><th>Пн</th><th>Вт</th></tr><tr><td class="time">09:00–09:50</td><td rowspan="2"><div class="groups"><div id="group-a"><p>Алгебра</p><p class="params"><span>101</span><span>Корпус 2</span></p><a id="lesson-link" href="#syllabus">Силлабус</a></div><div id="group-b" style="display:none"><p>Геометрия</p></div></div></td><td></td></tr><tr><td class="time">10:00–10:50</td><td><p>История</p></td></tr></table></td></tr></table>';
}

test('schedule retains rowspan times, visible-week groups, working links and both view controls', () => {
  const p = page(scheduleFixture(), 'https://univer.kaznu.kz/student/myschedule/'); pinDate(p, new Date(2026, 10, 1, 12));
  const table=p.w.document.querySelector('.schedule'); let clicks=0;
  p.w.document.querySelector('#lesson-link').addEventListener('click',e=>{e.preventDefault();clicks++;}); adapt(p);
  const agenda=p.w.document.querySelector('.univer-app-schedule');
  assert.equal(agenda.dataset.allDays,'true');
  assert.equal(agenda.querySelectorAll('.univer-app-lesson').length,2);
  assert.equal(agenda.querySelector('time').getAttribute('aria-label'),'09:00–10:50');assert.equal(agenda.querySelector('time>b').textContent,'09:00');
  assert.ok(agenda.textContent.includes('101 / Корпус 2'));
  assert.equal(agenda.textContent.includes('Геометрия'),false);
  agenda.querySelector('a.univer-app-page-link').click(); assert.equal(clicks,1);
  agenda.querySelectorAll('.univer-app-days button')[1].click();
  assert.equal(agenda.querySelectorAll('.univer-app-day:not([hidden])').length,1);
  agenda.querySelectorAll('.univer-app-schedule-modes button')[1].click();
  assert.ok(table.classList.contains('univer-app-schedule-grid-visible'));
  assert.equal(agenda.dataset.mode,'grid');
  p.w.__univerDesktop.apply(false,false,'ru');
  assert.equal(p.w.document.querySelector('.schedule'),table);
  assert.equal(table.rows[1].cells[1].rowSpan,2);
  assert.equal(p.w.document.querySelector('.univer-app-schedule'),null);
  assert.equal(p.w.document.querySelector('.univer-app-table-scroll'),null); p.close();
});

test('schedule week changes rebuild the agenda and retain the chosen day and view', async () => {
  const p = page(scheduleFixture(), 'https://univer.kaznu.kz/student/myschedule/'); adapt(p);
  p.w.document.querySelector('.univer-app-days button').click();
  p.w.document.querySelectorAll('.univer-app-schedule-modes button')[1].click();
  p.w.document.querySelector('#group-a').style.display='none';
  p.w.document.querySelector('#group-b').style.display='block';
  await new Promise(r=>setTimeout(r,240));
  const agenda=p.w.document.querySelector('.univer-app-schedule');
  assert.ok(agenda.textContent.includes('Геометрия'));
  assert.equal(agenda.textContent.includes('Алгебра'),false);
  assert.equal(agenda.dataset.mode,'grid');
  assert.equal(agenda.dataset.allDays,'false');
  let mutations=0;const observer=new p.w.MutationObserver(r=>{mutations+=r.length;});
  observer.observe(p.w.document.body,{childList:true,subtree:true,attributes:true});
  await new Promise(r=>setTimeout(r,240));assert.equal(mutations,0);observer.disconnect();p.close();
});

test('repeated GPA metadata moves under Account and restores the original direct LI nodes', () => {
  const p=page('<table class="wrappertable"><tr id="l2"><td><div class="links"><a href="/user/pass">Account</a></div></td></tr><tr id="l4"><td><table class="mt"><tr><td class="lt"></td><td class="ct message"><li id="gpa-a">Переводной GРА = 0</li><li id="gpa-b">Общий GPA = 0</li></td><td class="rt"></td></tr></table></td></tr></table>','https://univer.kaznu.kz/student/myschedule/');
  const item=p.w.document.querySelector('#gpa-a'),home=item.parentElement;adapt(p);
  assert.ok(item.closest('.univer-app-account'));
  assert.ok(home.closest('table').classList.contains('univer-app-empty-panel'));
  p.w.__univerDesktop.apply(false,false,'ru');assert.equal(item.parentElement,home);
  assert.equal(p.w.document.querySelector('.univer-app-academic-meta'),null);p.close();
});

test('blocked remote video frames get a real external instruction link and restore Original', () => {
  const p=page('<table><tr id="l4"><td><iframe src="https://www.youtube.com/embed/fixture123" width="560" height="315"></iframe></td></tr></table>','https://univer.kaznu.kz/student/myexam/schedule/');
  const frame=p.w.document.querySelector('iframe'),src=frame.src;adapt(p,'en');
  const link=p.w.document.querySelector('.univer-app-video-link');
  assert.equal(link.href,'https://www.youtube.com/watch?v=fixture123');assert.equal(link.target,'_blank');assert.equal(link.textContent,'Watch video instructions');
  p.w.__univerDesktop.apply(false,false,'en');assert.equal(frame.src,src);assert.equal(p.w.document.querySelector('.univer-app-video-link'),null);p.close();
});

test('nested e-service language links become individual flags and restore their source container', () => {
  const p=page('<table><tr id="l1"><td class="header">University<span id="flags"><a href="/lang/change/kz/"><img src="/img/kz.png"></a><br><img src="/img/ru.png"><a href="/lang/change/en/"><img src="/img/en.png"></a></span></td></tr><tr id="l4"><td>Services</td></tr></table>','https://univer.kaznu.kz/ssc/UserServiceList');
  const original=p.w.document.querySelector('#flags'),links=Array.from(original.children).filter(e=>e.tagName!=='BR');adapt(p);
  const flags=p.w.document.querySelector('.univer-app-flags');assert.equal(flags.children.length,3);
  assert.ok(links.every(e=>e.parentElement===flags));
  p.w.__univerDesktop.apply(false,false,'ru');assert.ok(links.every(e=>e.parentElement===original));p.close();
});

test('language flags appended after initial parsing join the existing header and restore their original home', async () => {
  const p=page('<table><tr id="l1"><td class="header"><a href="/lang/change/kz/"><img src="/img/kz.png"></a></td></tr><tr id="l4"><td>Services</td></tr></table>','https://univer.kaznu.kz/ssc/StatementList');adapt(p);
  const header=p.w.document.querySelector('.header');header.insertAdjacentHTML('beforeend','<img id="late-ru" src="/img/ru.png"><a href="/lang/change/en/"><img src="/img/en.png"></a>');
  await new Promise(r=>setTimeout(r,220));assert.equal(p.w.document.querySelector('.univer-app-flags').children.length,3);
  p.w.__univerDesktop.apply(false,false,'ru');assert.equal(p.w.document.querySelector('#late-ru').parentElement,header);p.close();
});

test('Android-derived site menu retains real links and restores original placement', () => {
  const p = page('<div id="home"><table id="tools"><tr><td><table class="tool"><tr><td><ul><li><a id="link" href="/student/news">Новости</a></li></ul></td></tr><tr><td class="bottom-center">Учёба</td></tr></table></td></tr></table></div>', 'https://univer.kaznu.kz/student/news');
  const tools = p.w.document.querySelector('#tools'), home = tools.parentNode, link = p.w.document.querySelector('#link'); let clicks = 0;
  link.addEventListener('click', e => { e.preventDefault(); clicks++; }); adapt(p);
  assert.ok(p.w.document.querySelector('.univer-app-menu input[type=search]'));
  assert.equal(p.w.document.querySelector('#link'), link); link.click(); assert.equal(clicks, 1);
  const search = p.w.document.querySelector('.univer-app-menu-search'); search.value = 'несуществующий'; search.dispatchEvent(new p.w.Event('input'));
  assert.equal(link.closest('li').hidden, true);
  p.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(tools.parentNode, home); assert.equal(link.closest('li').hidden, false);
  assert.equal(p.w.document.querySelector('.univer-app-menu'), null); p.close();
});

test('record labels preserve sorting, controls and original row nodes', () => {
  const p = page('<table class="inner table_sort"><tr><th><a id="sort" href="#">Дисциплина</a></th><th>Оценка</th></tr><tr id="row"><td><input type="checkbox">Математика</td><td>95</td></tr></table>', 'https://univer.kaznu.kz/student/statement');
  const row = p.w.document.querySelector('#row'), table = row.closest('table'); let sorts = 0;
  p.w.document.querySelector('#sort').addEventListener('click', e => { e.preventDefault(); sorts++; }); adapt(p);
  assert.equal(row.cells[0].dataset.univerField, 'Дисциплина'); assert.ok(table.classList.contains('univer-app-cards'));
  p.w.document.querySelector('#sort').click(); assert.equal(sorts, 1); row.querySelector('input').checked = true;
  p.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(p.w.document.querySelector('#row'), row); assert.equal(row.querySelector('input').checked, true);
  assert.equal(row.cells[0].hasAttribute('data-univer-field'), false); p.close();
});

test('attendance matrix keeps merged cells and a keyboard-accessible scroll region', () => {
  const p = page('<table class="inner"><tr><th rowspan="2">Студент</th><th>Неделя</th></tr><tr><td>1</td></tr></table>', 'https://univer.kaznu.kz/student/attendance/list');
  const cell = p.w.document.querySelector('th'), table = cell.closest('table'); adapt(p);
  assert.equal(cell.rowSpan, 2); assert.ok(table.classList.contains('univer-app-matrix'));
  assert.equal(table.parentNode.getAttribute('role'), 'region'); assert.equal(table.parentNode.tabIndex, 0);
  p.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(table.parentNode.tagName, 'BODY'); assert.equal(cell.rowSpan, 2); p.close();
});

test('profile labels adapt while original field values and form handlers remain', () => {
  const p = page('<form><table><tr><td>ФИО:</td><td><input value="synthetic student"></td></tr></table><button>Save</button></form>', 'https://univer.kaznu.kz/student/profile');
  const input = p.w.document.querySelector('input'); adapt(p, 'en'); assert.ok(input.closest('table').classList.contains('univer-app-pairs'));
  assert.equal(input.value, 'synthetic student'); p.w.__univerDesktop.apply(false, false, 'en'); assert.equal(p.w.document.querySelector('input'), input); p.close();
});

test('late AJAX rows get labels without a self-triggered observer loop', async () => {
  const p = page('<table class="inner"><tr><th>Курс</th><th>Оценка</th></tr></table>', 'https://univer.kaznu.kz/student/statement'); adapt(p);
  const row = p.w.document.querySelector('table').insertRow(); row.insertCell().textContent = 'synthetic course'; row.insertCell().textContent = '90';
  await new Promise(resolve => setTimeout(resolve, 240)); assert.equal(row.cells[1].dataset.univerField, 'Оценка');
  let mutations = 0; const observer = new p.w.MutationObserver(records => { mutations += records.length; }); observer.observe(p.w.document.body, { attributes:true, subtree:true, childList:true });
  await new Promise(resolve => setTimeout(resolve, 240)); assert.equal(mutations, 0); observer.disconnect(); p.close();
});

test('combined site adapter preserves login encryption submit and hidden fields', () => {
  const p = page('<form id="login_frm" action="/user/login"><table class="cols"><tr><td><div id="login_form"><input type="text"><input type="password"><input type="hidden" name="key" value="fixture"><input name="btn" type="submit"></div></td><td>Help</td></tr></table></form>');
  let clicks = 0; p.w.document.querySelector('[name=btn]').addEventListener('click', e => { e.preventDefault(); clicks++; }); adapt(p);
  const probe = p.api.probe(); assert.equal(p.api.login(probe.document, 'fixture student', 'fixture password'), 'submitted');
  assert.equal(clicks, 1); assert.equal(p.w.document.querySelector('[name=key]').value, 'fixture'); p.close();
});

test('site adapter styles same-origin frames and restores them; external frames stay untouched', () => {
  const p = page('<iframe id="local"></iframe><iframe id="external"></iframe>', 'https://univer.kaznu.kz/student/news');
  const child = page('<table class="inner"><tr><th>Курс</th><th>Оценка</th></tr><tr><td>fixture</td><td>90</td></tr></table>', 'https://univer.kaznu.kz/student/statement');
  const external = page('<p>external fixture</p>', 'https://external.test/');
  const table = child.w.document.querySelector('table');
  for (const [id, view] of [['local', child.w], ['external', external.w]]) {
    const frame = p.w.document.getElementById(id);
    Object.defineProperty(frame, 'contentWindow', { value:view });
    Object.defineProperty(frame, 'contentDocument', { value:view.document });
  }
  adapt(p, 'en');
  assert.ok(child.w.document.documentElement.classList.contains('univer-mobile'));
  assert.ok(child.w.document.getElementById('univer-app-mobile-style').textContent.includes('min-width:900px'));
  assert.equal(external.w.__univerMobileV1, undefined);
  p.w.__univerDesktop.apply(false, false, 'en');
  assert.equal(child.w.document.querySelector('table'), table);
  assert.equal(child.w.document.getElementById('univer-app-mobile-style'), null);
  assert.equal(child.w.document.getElementById('univer-desktop-style'), null);
  assert.equal(table.parentNode.tagName, 'BODY');
  p.close(); child.close(); external.close();
});

test('document paint gate hides the initial page without breaking visible login detection', () => {
  const p = page('<form id="login_frm"><input type="text"><input type="password"><input name="btn" type="submit"></form>');
  p.w.document.documentElement.style.opacity = '0.75'; p.w.eval(paint); p.w.__univerPaintV1.configure(true);
  assert.equal(p.w.document.documentElement.style.opacity, '0'); assert.equal(p.api.probe().loginReady, true);
  p.w.__univerPaintV1.complete(true, false, false, 'ru');
  assert.equal(p.w.document.documentElement.style.opacity, '0.75'); assert.equal(p.w.document.documentElement.style.pointerEvents, ''); p.close();
});

test('a late verified reauth dialog is covered before the next native polling tick', async () => {
  const p = page('<p>Content</p>', 'https://univer.kaznu.kz/student/news'); p.w.ajl = () => {};
  p.w.eval(paint); p.w.__univerPaintV1.configure(true); p.w.__univerPaintV1.complete(false, false, false, 'ru');
  p.w.document.body.insertAdjacentHTML('beforeend', reauthHtml()); await Promise.resolve();
  assert.equal(p.w.document.documentElement.style.opacity, '0'); assert.equal(p.api.probe().reauthReady, true);
  p.w.__univerPaintV1.complete(true, false, false, 'ru'); await Promise.resolve();
  assert.equal(p.w.document.documentElement.style.opacity, ''); p.close();
});

test('paint gate does not conceal an arbitrary confirmation or manual login', async () => {
  const p = page('<p>Content</p>', 'https://univer.kaznu.kz/student/news'); p.w.eval(paint);
  p.w.__univerPaintV1.configure(true); p.w.__univerPaintV1.complete(false, false, false, 'ru');
  p.w.document.body.insertAdjacentHTML('beforeend', '<form id="attentionSt" action="/student/attentionStudents/1"><input id="check" type="checkbox"><label for="check">Confirm payment</label><button id="btn" type="submit">Submit</button></form>');
  await Promise.resolve(); assert.equal(p.w.document.documentElement.style.opacity, ''); p.close();
  const manual = page('<form id="login_frm"><input type="text"><input type="password"><input type="submit"></form>');
  manual.w.eval(paint); manual.w.__univerPaintV1.configure(false); manual.w.__univerPaintV1.complete(false, false, false, 'ru');
  await Promise.resolve(); assert.equal(manual.w.document.documentElement.style.opacity, ''); manual.close();
});

test('successful AJAX reauth retains its episode during the closing animation', () => {
  const p = page(reauthHtml(), 'https://univer.kaznu.kz/student/news'); p.w.ajl = () => {};
  p.w.document.querySelector('#modal_auth_ajl_btn').addEventListener('click', e => e.preventDefault());
  const first = p.api.probe(); p.api.reauthenticate(first.document, first.reauthChallenge, 'fixture-account', 'fixture password');
  p.hooks['ajaxSuccess.univerApp'](null, { responseText:'{"code":1}' }, { url:'/user/ajl',type:'POST' });
  const closing = p.api.probe(); assert.equal(closing.reauthChallenge, first.reauthChallenge);
  assert.equal(closing.reauthOutcome, 'success'); assert.equal(p.api.reauthenticate(closing.document, closing.reauthChallenge, 'fixture-account', 'fixture password'), 'wait'); p.close();
});

test('UMKD course cards keep double-click navigation and restore original cells', () => {
  const p = page('<table><tr id="l4"><td><table class="inner"><tr><th>№</th><th>Дисциплина</th><th>Тип дисциплины</th></tr><tr class="link" id="fixture"><td>1</td><td>Fixture discipline</td><td>Required</td></tr></table></td></tr></table>', 'https://univer.kaznu.kz/student/umkd/');
  const row = p.w.document.getElementById('fixture'), table = row.closest('table'); let opens = 0;
  row.addEventListener('dblclick', () => opens++); adapt(p); const button = row.querySelector('button'); assert.ok(button); button.click(); assert.equal(opens, 1);
  assert.ok(table.parentNode.classList.contains('univer-app-table-scroll'));
  p.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(p.w.document.getElementById('fixture'), row); assert.equal(row.cells.length, 3); assert.equal(row.querySelector('button'), null); p.close();
});

test('AJAX outcome bridge sends only a matching challenge and result without account data', () => {
  const p = page(reauthHtml(), 'https://univer.kaznu.kz/student/news'), sent = [];
  p.w.__univerNative = value => sent.push(value); p.w.ajl = () => {};
  p.w.document.querySelector('#modal_auth_ajl_btn').addEventListener('click', e => e.preventDefault());
  const first = p.api.probe(); p.api.reauthenticate(first.document, first.reauthChallenge, 'fixture-account', 'fixture password');
  p.hooks['ajaxSuccess.univerApp'](null, { responseText:'{"code":1,"key":"not-for-the-bridge"}' }, { url:'https://external.test/user/ajl', type:'POST' });
  assert.equal(sent.length, 0);
  p.hooks['ajaxSuccess.univerApp'](null, { responseText:'{"code":1,"key":"not-for-the-bridge"}' }, { url:'/user/ajl', type:'POST' });
  assert.deepEqual(sent, ['univer-ajax|reauth|' + first.reauthChallenge + '|success']);
  assert.equal(sent.join('').includes('fixture'), false); assert.equal(sent.join('').includes('not-for-the-bridge'), false); p.close();
});

test('empty table spans and instruction layout retain original DOM and controls', () => {
  const p = page('<table><tr id="l4"><td><table class="inner"><tr><th>№</th><th>Обращение</th></tr><tr><td colspan="2">Обращения не найдены</td></tr></table><table class="cols"><tr><td>Instruction</td><td><button id="read">Read</button></td></tr></table></td></tr></table>', 'https://univer.kaznu.kz/my/appeals/index/');
  const cell = p.w.document.querySelector('td[colspan]'), button = p.w.document.getElementById('read'); let clicks = 0;
  button.onclick = () => clicks++; adapt(p); button.click(); assert.equal(clicks, 1);
  assert.ok(cell.parentNode.classList.contains('univer-app-card-section')); assert.ok(button.closest('.univer-app-content-columns'));
  p.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(cell.colSpan, 2); assert.equal(p.w.document.getElementById('read'), button);
  assert.equal(button.closest('.univer-app-content-columns'), null); assert.equal(cell.parentNode.className, ''); p.close();
});

test('calendar semester shortcuts keep the real anchors and restore their original cells', () => {
  const p = page('<table><tr id="l4"><td><table class="xp"><tr><td>Форма обучения:</td><td>очная</td><td colspan="2"></td><td><a href="#term1">Семестр 1</a><a href="#term2">Семестр 2</a></td></tr></table><div id="term1">Term</div></td></tr></table>', 'https://univer.kaznu.kz/student/academcalendar/');
  const anchor = p.w.document.querySelector('a'), home = anchor.parentNode; let calls = 0;
  anchor.addEventListener('click', e => { e.preventDefault(); calls++; }); adapt(p); anchor.click();
  assert.equal(calls, 1); assert.ok(anchor.closest('.univer-app-semester-nav')); assert.equal(anchor.getAttribute('href'), '#term1');
  p.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(anchor.parentNode, home); assert.equal(p.w.document.querySelector('.univer-app-semester-nav'), null); p.close();
});

test('empty decorative rows become visible again after a late AJAX update', async () => {
  const p = page('<table><tr id="l4"><td><table class="mt"><tr id="late"><td class="lt"></td><td class="ct"></td><td class="rt"></td></tr><tr><td class="lt"></td><td class="ts"><a href="/student/educplan/1/">Семестр 1</a></td><td class="rt"></td></tr></table></td></tr></table>', 'https://univer.kaznu.kz/student/educplan/');
  adapt(p); const row = p.w.document.getElementById('late'); assert.ok(row.classList.contains('univer-app-study-empty'));
  row.querySelector('.ct').textContent = 'Late server message'; await new Promise(resolve => setTimeout(resolve, 160));
  assert.equal(row.classList.contains('univer-app-study-empty'), false); p.close();
});

test('contract DIV columns and floating exam instructions keep their original nodes', () => {
  const p = page('<table><tr id="l4"><td><table class="inner"><tr><th><div class="full-width"><div>Название</div><div>Договор</div><div>Обязательно</div><div>Дата</div><div>Подписан</div><div></div></div></th></tr><tr><td>Данные не найдены</td></tr></table></td></tr></table>', 'https://univer.kaznu.kz/student/contract/list/');
  const table = p.w.document.querySelector('table.inner'), heading = table.querySelector('th'); adapt(p);
  assert.ok(table.classList.contains('univer-app-div-records')); assert.ok(table.parentNode.classList.contains('univer-app-table-scroll'));
  p.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(table.querySelector('th'), heading); assert.equal(table.className, 'inner'); p.close();
  const exam = page('<table><tr id="l4"><td><div style="float:left;width:50%"><a href="#schedule">Instruction</a></div></td></tr></table>', 'https://univer.kaznu.kz/student/myexam/schedule/');
  const block = exam.w.document.querySelector('div'); adapt(exam); assert.ok(block.classList.contains('univer-app-media-column'));
  exam.w.__univerDesktop.apply(false, false, 'ru'); assert.equal(block.style.cssFloat, 'left'); assert.equal(block.style.width, '50%'); exam.close();
});

test('page content is detected from the first visible non-blank text without reading the whole page', () => {
  const blank = page('<div>  \n\u00a0 <span> </span><script>var x = 1;</script></div><script>ignored()</script>', 'https://univer.kaznu.kz/student/transcript/');
  assert.equal(blank.api.probe().hasContent, true, 'nested script text counts, as textContent did');
  blank.close();
  const empty = page('<div> \u00a0 <span>\n</span></div><div hidden>Hidden grade</div><script>shown()</script>', 'https://univer.kaznu.kz/student/transcript/');
  assert.equal(empty.api.probe().hasContent, false);
  empty.close();
  const grades = page('<div>' + '<p> </p>'.repeat(200) + '<table><tr><td>Grade A</td></tr></table></div>', 'https://univer.kaznu.kz/student/transcript/');
  assert.equal(grades.api.probe().hasContent, true);
  grades.close();
});

test('paint gate observer skips work while pending, inactive, failed, or mutating outside known forms, and avoids full probe()', async () => {
  const p = page('<div id="l4"><p>News</p></div>' + noticeHtml().replace('id="attentionSt"', 'id="attentionSt" hidden'), 'https://univer.kaznu.kz/student/news');
  const sent = []; p.w.__univerNative = value => sent.push(value);
  let rects = 0, styles = 0, probes = 0;
  const realRects = p.w.HTMLElement.prototype.getClientRects;
  p.w.HTMLElement.prototype.getClientRects = function () { rects++; return realRects.call(this); };
  const realStyle = p.w.getComputedStyle;
  p.w.getComputedStyle = function (el, pseudo) { styles++; return realStyle.call(this, el, pseudo); };
  const realProbe = p.api.probe;
  p.api.probe = function () { probes++; return realProbe(); };
  p.w.eval(paint);
  const reset = () => { rects = 0; styles = 0; probes = 0; };

  // While pending (initial cover), DOM mutations elsewhere do no layout reads or probe().
  reset();
  const l4 = p.w.document.getElementById('l4');
  for (let i = 0; i < 10; i++) l4.appendChild(p.w.document.createElement('span'));
  await Promise.resolve();
  assert.equal(rects, 0); assert.equal(styles, 0); assert.equal(probes, 0);

  // Inactive: mutations (even inserting a reauth form) do nothing.
  p.w.__univerPaintV1.configure(false); p.w.__univerPaintV1.complete(false, false, false, 'ru');
  reset();
  p.w.document.body.insertAdjacentHTML('beforeend', reauthHtml());
  await Promise.resolve();
  assert.equal(p.w.document.documentElement.style.opacity, '');
  assert.equal(rects, 0); assert.equal(styles, 0); assert.equal(probes, 0);
  p.w.document.getElementById('auth_modal').remove();

  // Failed: observer stays idle even if a known form becomes visible.
  p.w.__univerPaintV1.configure(true); p.w.__univerPaintV1.complete(true, false, false, 'ru');
  reset();
  p.w.document.getElementById('attentionSt').hidden = false;
  await Promise.resolve();
  assert.equal(p.w.document.documentElement.style.opacity, '');
  assert.equal(rects, 0); assert.equal(styles, 0); assert.equal(probes, 0);
  p.w.document.getElementById('attentionSt').hidden = true;

  // Active and revealed: unrelated mutations skip layout reads and probe().
  p.w.__univerPaintV1.configure(false); p.w.__univerPaintV1.configure(true); p.w.__univerPaintV1.complete(false, false, false, 'ru');
  reset();
  for (let i = 0; i < 15; i++) {
    const item = p.w.document.createElement('div');
    item.className = 'row-' + i;
    l4.appendChild(item);
    p.w.document.body.appendChild(p.w.document.createElement('span'));
  }
  await Promise.resolve();
  assert.equal(p.w.document.documentElement.style.opacity, '');
  assert.equal(rects, 0); assert.equal(styles, 0); assert.equal(probes, 0);

  // When the hidden notice actually becomes visible, the gate covers at once without calling full probe().
  sent.length = 0;
  p.w.document.getElementById('attentionSt').hidden = false;
  await Promise.resolve();
  assert.equal(p.w.document.documentElement.style.opacity, '0');
  assert.deepEqual(sent, ['univer-busy']);
  assert.equal(probes, 0, 'observer uses lightweight hasPending instead of full probe()');
  p.close();
});

test('paint gate observer coalesces repeated bursts on a known element within a frame', async () => {
  const p = page('<p>Content</p>' + reauthHtml().replace('id="auth_modal"', 'id="auth_modal" style="display:none"'), 'https://univer.kaznu.kz/student/news');
  p.w.ajl = () => {};
  let rects = 0;
  const realRects = p.w.HTMLElement.prototype.getClientRects;
  p.w.HTMLElement.prototype.getClientRects = function () { rects++; return realRects.call(this); };
  p.w.eval(paint); p.w.__univerPaintV1.configure(true); p.w.__univerPaintV1.complete(false, false, false, 'ru');
  rects = 0;
  const modal = p.w.document.getElementById('auth_modal');
  for (let i = 0; i < 10; i++) {
    modal.setAttribute('class', 'step-' + i);
    await Promise.resolve();
  }
  assert.ok(rects <= 3, 'burst of 10 mutations coalesced to a single check, got ' + rects);
  // Revealing the modal during the cooldown is caught by the trailing frame flush.
  modal.style.display = '';
  await new Promise(resolve => setTimeout(resolve, 40));
  assert.equal(p.w.document.documentElement.style.opacity, '0');
  p.close();
});

test('StepScript runs probe() once when the view changed nothing and twice when it mutated', () => {
  const fullBundle = paint + '\n' + layout + '\n' + script + '\n' + desktop;
  const p = page('<div class="header"><a href="/user/logout">Выход</a></div><p>News</p>', 'https://univer.kaznu.kz/student/news',
    nativeExpression('public string DocumentScript(', /return ([\s\S]*?);\r?\n\s*}/, { bundle: fullBundle }));
  let probeRuns = 0;
  const realQueryAll = p.w.document.querySelectorAll.bind(p.w.document);
  p.w.document.querySelectorAll = function (selector) {
    if (selector === 'a[href*="/user/logout"]') probeRuns++;
    return realQueryAll(selector);
  };
  const runStep = (readable, version) => {
    const view = `try{window.__univerPaintV1.configure(true);}catch(_){}try{window.__univerMotion=true;window.__univerTheme='light';if(window.__univerDesktop.apply(${readable},false,"ru",${version}))__changed=true;}catch(_){__changed=true;}`;
    return JSON.parse(p.run(nativeExpression('private string StepScript(', /return ([\s\S]*?);\r?\n\s*}/, { ViewScript: () => view })));
  };
  // First tick applies Reading mode: mutates DOM, so probe() runs before and after.
  probeRuns = 0;
  const first = runStep(true, 1);
  assert.equal(probeRuns, 2);
  assert.equal(first.after.authenticated, true);

  // Repeated tick with identical view: nothing mutated, so probe() runs once and after reuses probe.
  probeRuns = 0;
  const steady = runStep(true, 1);
  assert.equal(probeRuns, 1);
  assert.deepEqual(steady.after, steady.probe);

  // A preference/version change mutates or updates view: probe() runs twice again.
  probeRuns = 0;
  const changed = runStep(false, 2);
  assert.equal(probeRuns, 2);
  assert.equal(p.w.document.getElementById('univer-desktop-style'), null);

  // Repeated tick in Original mode: nothing mutated, probe() runs once.
  probeRuns = 0;
  const originalSteady = runStep(false, 2);
  assert.equal(probeRuns, 1);
  assert.deepEqual(originalSteady.after, originalSteady.probe);
  p.close();
});
