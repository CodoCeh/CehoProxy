import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { JSDOM, VirtualConsole } from 'jsdom';

// Execute the rendered production page in jsdom. No resources are loaded and
// every fetch is supplied by the test. Fake time controls faults, not outcomes.
export function tunnelHarness({ language = 'en', state = 'tunnel-ready', fetcher, storage = {}, url } = {}) {
  const root = process.env.CEHO_RENDER_DIR || '/tmp/cehoproxy-fixtures';
  const html = fs.readFileSync(path.join(root, `${state}-${language}.html`), 'utf8');
  assert.match(html, /fixture-banner/);
  const errors = [], requests = [], virtualConsole = new VirtualConsole();
  virtualConsole.on('jsdomError', error => errors.push(String(error)));
  const dom = new JSDOM(html, {
    url: url || `http://fixture.local/?fixture=${state}&lang=${language}&tab=apps`,
    runScripts: 'outside-only', virtualConsole, pretendToBeVisual: true,
  });
  const w = dom.window;
  let now = 100000, id = 0;
  const tasks = new Map();
  w.Date.now = () => now;
  w.AbortController = AbortController;
  w.matchMedia = query => ({ matches: query.includes('reduced-motion'), media: query, addEventListener() {}, removeEventListener() {} });
  w.confirm = () => false;
  w.HTMLElement.prototype.scrollIntoView = function () { this.dataset.fixtureScrolled = '1'; };
  w.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  w.HTMLDialogElement.prototype.close = function () { this.open = false; this.dispatchEvent(new w.Event('close')); };
  w.fetch = async (url, options = {}) => {
    const item = { url: String(url), method: options.method || 'GET', body: options.body?.toString() || '', options };
    requests.push(item);
    if (!fetcher) throw new Error(`Unexpected fixture request: ${item.method} ${item.url}`);
    return fetcher(item, w);
  };
  w.setTimeout = (fn, delay = 0) => { const n = ++id; tasks.set(n, { fn, at: now + delay }); return n; };
  w.clearTimeout = n => tasks.delete(n);
  w.setInterval = (fn, delay) => { const n = ++id; tasks.set(n, { fn, at: now + delay, delay }); return n; };
  w.clearInterval = w.clearTimeout;
  for (const [key, value] of Object.entries(storage)) w.sessionStorage.setItem(key, value);
  const settle = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
  const advance = async ms => {
    const end = now + ms;
    for (let i = 0; i < 2000; i++) {
      const due = [...tasks].filter(([, value]) => value.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
      if (!due) { now = end; await settle(); return; }
      const [n, value] = due;
      now = value.at; tasks.delete(n);
      if (value.delay) tasks.set(n, { ...value, at: now + value.delay });
      value.fn(); await settle();
    }
    throw new Error('Fixture timer loop did not settle');
  };
  for (const node of w.document.querySelectorAll('script:not([src])')) w.eval(node.textContent);
  return {
    w, requests, errors, settle, advance,
    posts: () => requests.filter(request => request.method === 'POST'),
    storage: () => Object.fromEntries(Array.from({ length: w.sessionStorage.length }, (_, index) => {
      const key = w.sessionStorage.key(index); return [key, w.sessionStorage.getItem(key)];
    })),
    close: () => { w.dispatchEvent(new w.Event('pagehide')); w.close(); },
  };
}

export const jsonResponse = (data, overrides = {}) => ({
  ok: true, redirected: false, status: 200,
  text: async () => JSON.stringify(data), ...overrides,
});
export const htmlResponse = html => ({ ok: true, redirected: false, status: 200, text: async () => html });
export function submit(w, form, button = form.querySelector('button[type=submit]')) {
  const event = new w.SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: button });
  form.dispatchEvent(event); return event;
}
export function drop(w, target, { files = [], data = {} } = {}) {
  const event = new w.Event('drop', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', { value: {
    files, types: files.length ? ['Files'] : Object.keys(data),
    getData: type => data[type] || '', dropEffect: 'none',
  } });
  target.dispatchEvent(event); return event;
}

export function tunnelFixture(name = 'tunnel-ready', language = 'en', extension = 'html') {
  const file = fs.readFileSync(path.join(process.env.CEHO_RENDER_DIR || '/tmp/cehoproxy-fixtures', `${name}-${language}.${extension}`), 'utf8');
  return extension === 'json' ? JSON.parse(file) : file;
}

export function addResult(language = 'en', changes = {}) {
  const state = tunnelFixture('tunnel-pending', language, 'json');
  const app = state.apps.find(app => /(?:^|[\\/])notes\.exe$/.test(app.path));
  assert.ok(app, 'Production pending fixture must contain the synthetic Notes app');
  return { ok: true, duplicate: false, covered: false, appId: app.id, path: app.path,
    pending: true, applied: false, message: 'Fixture rule saved; apply separately', status: 'added', ...changes };
}
