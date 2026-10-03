import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { chromium } from '@playwright/test';

export const root = process.env.CEHO_RENDER_DIR || '/tmp/cehoproxy-fixtures';
export const artifacts = process.env.CEHO_ARTIFACTS || 'artifacts';
export const origin = 'http://fixture.local';
export const languages = ['en', 'ru'];
export const states = ['state', 'idle', 'apps', 'wizard', 'subs', 'settings', 'doctor', 'help', 'leak', 'startup-progress', 'startup-delayed', 'startup-error'];
const html = new Map(), jobs = new Map();

export async function loadFixtures() {
  const files = [];
  for (const language of languages) for (const state of [...states, 'job']) {
    const name = `${state}-${language}.html`;
    const content = await fs.readFile(path.join(root, name), 'utf8');
    if (!content.includes('fixture-banner') || !new RegExp(`<html\\s+lang=["\']?${language}(?:["\']|[ >])`).test(content)) throw new Error(`Invalid production fixture: ${name}`);
    html.set(`${state}-${language}`, content);
    files.push({ name, sha256: crypto.createHash('sha256').update(content).digest('hex') });
    if (state === 'job' || state.startsWith('startup-')) {
      const jsonName = `${state}-${language}.json`;
      const json = await fs.readFile(path.join(root, jsonName), 'utf8');
      files.push({ name: jsonName, sha256: crypto.createHash('sha256').update(json).digest('hex') });
      const data = JSON.parse(json);
      if (data.id !== 'fixture-job') throw new Error(`Unexpected fixture job: ${state}-${language}`);
      jobs.set(`${state}-${language}`, data);
    }
  }
  return { commit: process.env.GITHUB_SHA || null, fixtureType: 'real C# RenderPage HTML; synthetic observations; no live engine', files };
}
export const fixtureHtml = (name = 'state', language = 'en') => {
  const value = html.get(`${name}-${language}`);
  if (!value) throw new Error(`Missing fixture ${name}-${language}`);
  return value;
};
export const jobPayload = (name = 'job', language = 'en', changes = {}) => ({ ...jobs.get(`${name}-${language}`), ...changes });
export const fixtureUrl = (name = 'state', language = 'en') => {
  const tab = ['apps', 'subs', 'doctor', 'help'].includes(name) ? name : 'state';
  return `${origin}/?${new URLSearchParams({ fixture: name, lang: language, tab, ...(name === 'job' || name.startsWith('startup-') ? { job: 'fixture-job' } : {}) })}`;
};
export const launch = () => chromium.launch({ headless: true, ...(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {}) });

// No HTTP listener, DNS lookup, external website, or real operation is needed.
// Every browser request is intercepted, including unexpected external resources.
export async function routeFixtures(context, options = {}) {
  const requests = [], unexpected = [], errors = [];
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url());
    const entry = { url: request.url(), method: request.method(), type: request.resourceType(), body: request.postData() };
    requests.push(entry);
    try {
      if (url.origin !== origin) { unexpected.push(entry); await route.abort('blockedbyclient'); return; }
      if (options.handle && await options.handle(route, entry, url)) return;
      if (request.method() !== 'GET') { unexpected.push(entry); await route.abort('blockedbyclient'); return; }
      if (url.pathname === '/icon' || url.pathname === '/favicon.ico') { await route.fulfill({ status: 204, body: '' }); return; }
      const language = url.searchParams.get('lang') || options.language || 'en';
      if (url.pathname === '/job') {
        await route.fulfill({ json: options.job ? options.job() : jobPayload(options.jobState || 'job', language) });
        return;
      }
      if (url.pathname !== '/') { unexpected.push(entry); await route.fulfill({ status: 404, body: 'Unexpected fixture path' }); return; }
      const name = url.searchParams.get('fixture') || options.defaultState || 'state';
      await route.fulfill({ contentType: 'text/html; charset=utf-8', body: fixtureHtml(name, language) });
    } catch (error) {
      errors.push(String(error));
      await route.abort('failed').catch(() => {});
    }
  });
  return { requests, unexpected, errors };
}

// A response-body hang cannot be expressed by Playwright route.fulfill, which
// supplies a complete body. Only this fault replaces fetch; UI/HTML stay real.
export async function injectHungBody(context) {
  await context.addInitScript(() => {
    const fetch = window.fetch.bind(window);
    window.__bodyFault = { calls: 0, aborted: 0, active: 0, maxActive: 0, enabled: true };
    window.fetch = async (input, init = {}) => {
      if (!window.__bodyFault.enabled || new URL(String(input), location.href).pathname !== '/job') return fetch(input, init);
      const state = window.__bodyFault;
      state.calls++; state.active++; state.maxActive = Math.max(state.maxActive, state.active);
      const stream = new ReadableStream({ start(controller) {
        controller.enqueue(new TextEncoder().encode('{"id":"fixture-job",'));
        const abort = () => { state.aborted++; state.active--; controller.error(new DOMException('Fixture body timeout', 'AbortError')); };
        if (init.signal?.aborted) abort(); else init.signal?.addEventListener('abort', abort, { once: true });
      } });
      return new Response(stream, { status: 200, headers: { 'Content-Type': 'application/json' } });
    };
  });
}

export async function pauseClock(page) {
  await page.clock.install({ time: new Date('2026-10-03T01:00:00Z') });
  // Pause on about:blank before the panel installs timers. A wall-clock read
  // followed by pauseAt(now) can race and attempt to fast-forward into the past.
  await page.clock.pauseAt(new Date('2026-10-03T02:00:00Z'));
}
