import fs from 'node:fs/promises';
import path from 'node:path';
import { expect } from '@playwright/test';
import { runTunnelBrowserCases } from './tunnel-browser-cases.mjs';
import { artifacts, languages, loadFixtures, launch, routeFixtures, fixtureUrl, fixtureHtml, jobPayload, injectHungBody, pauseClock } from './fixtures.mjs';

await fs.mkdir(artifacts, { recursive: true });
const provenance = await loadFixtures(), results = [];
let browser;
const waitFor = (fn) => expect.poll(fn, { timeout: 5000 }).toBeTruthy();
async function run(name, language, body) {
  const label = `${name}-${language}`;
  const context = await browser.newContext({ viewport: { width: 1280, height: 1000 }, serviceWorkers: 'block', reducedMotion: 'reduce' });
  const page = await context.newPage();
  page.setDefaultTimeout(5000); page.setDefaultNavigationTimeout(8000);
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(String(error)));
  await context.tracing.start({ screenshots: true, snapshots: true, sources: true });
  let routing;
  const install = async options => (routing = await routeFixtures(context, { language, ...options }));
  const open = async (state = 'state') => { await pauseClock(page); await page.goto(fixtureUrl(state, language)); };
  const text = (en, ru) => language === 'ru' ? ru : en;
  try {
    await body({ context, page, install, open, language, text });
    expect(pageErrors).toEqual([]);
    expect(routing?.errors || []).toEqual([]);
    expect(routing?.unexpected || []).toEqual([]);
    results.push({ name: label, status: 'passed' });
    await context.tracing.stop();
  } catch (error) {
    results.push({ name: label, status: 'failed', error: String(error), pageErrors, routingErrors: routing?.errors, unexpectedRequests: routing?.unexpected });
    await page.screenshot({ path: path.join(artifacts, `${label}-failure.png`), fullPage: true, animations: 'disabled' }).catch(() => {});
    await context.tracing.stop({ path: path.join(artifacts, `${label}-trace.zip`) }).catch(() => {});
  } finally { await context.close(); }
}
try {
  browser = await launch();
  for (const language of languages) {
    await runTunnelBrowserCases(run, language);
    await run('mode-theme-and-mobile-layout', language, async ({ page, install, open }) => {
      await install(); await open();
      await expect(page.locator('form.mode button[role=switch]')).toHaveCount(1);
      await expect(page.locator('form.mode button')).toHaveCount(1);
      await page.locator('#theme').click();
      const theme = await page.locator('html').getAttribute('data-theme');
      expect(['light', 'dark']).toContain(theme);
      expect(await page.evaluate(() => localStorage.getItem('ceho-theme'))).toBe(theme);
      await page.reload(); await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
      await page.setViewportSize({ width: 390, height: 844 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    });

    await run('startup-delayed-reload-and-terminal-error', language, async ({ page, install, open, text }) => {
      let payload = jobPayload('startup-delayed', language);
      const routing = await install({ job: () => payload });
      await open('job');
      await expect(page.locator('#job-status')).toHaveClass(/warn/);
      await expect(page.locator('#jn')).toHaveText(text('In progress', 'Выполняется'));
      await expect(page.locator('.bar')).toHaveClass(/indeterminate/);
      await expect(page.locator('#startup-steps [aria-current=step]')).toHaveAttribute('data-step', '2');
      await expect(page.locator('#job-retry-operation')).toBeHidden();
      expect(await page.locator('.bar').getAttribute('aria-valuenow')).toBeNull();
      expect(await page.locator('#jn').innerText()).not.toContain('%');
      const before = routing.requests.filter(r => new URL(r.url).pathname === '/job').length;
      await page.reload();
      await waitFor(() => routing.requests.filter(r => new URL(r.url).pathname === '/job').length > before);
      payload = jobPayload('startup-error', language);
      await page.clock.runFor(1000);
      await expect(page.locator('#job-status')).toContainText(payload.result);
      await expect(page.locator('#jp')).toHaveClass(/err/);
      await expect(page.locator('#job-retry-operation')).toBeVisible();
      const terminalCalls = routing.requests.filter(r => new URL(r.url).pathname === '/job').length;
      await page.clock.runFor(12000);
      expect(routing.requests.filter(r => new URL(r.url).pathname === '/job').length).toBe(terminalCalls);
    });

    await run('hung-response-body-timeout-and-retry', language, async ({ context, page, install, open, text }) => {
      await injectHungBody(context); await install(); await open('job');
      await waitFor(() => page.evaluate(() => window.__bodyFault.calls === 1));
      await page.clock.runFor(6100);
      await expect(page.locator('#job-status')).toContainText(text('result is unknown', 'пока неизвестен'));
      expect(Number((await page.locator('#jt').innerText()).match(/\d+/)?.[0])).toBeGreaterThanOrEqual(6);
      const fault = await page.evaluate(() => ({ ...window.__bodyFault }));
      expect(fault.aborted).toBe(1); expect(fault.maxActive).toBe(1); expect(fault.active).toBe(0);
      await page.evaluate(() => { window.__bodyFault.enabled = false; });
      await page.locator('#job-retry').click();
      await expect(page.locator('#job-status')).toContainText(text('background', 'фоне'));
      await expect(page.locator('#job-retry')).toBeHidden();
    });

    for (const fault of ['offline', 'http-error', 'invalid-json', 'wrong-job', 'missing-id', 'unknown-state']) {
      await run(`${fault}-remains-unknown-and-recovers`, language, async ({ page, install, open, text }) => {
        let broken = true;
        await install({ handle: async (route, request, url) => {
          if (url.pathname !== '/job' || !broken) return false;
          if (fault === 'offline') await route.abort('internetdisconnected');
          else if (fault === 'http-error') await route.fulfill({ status: 503, body: 'Synthetic unavailable' });
          else if (fault === 'invalid-json') await route.fulfill({ contentType: 'application/json', body: '{invalid' });
          else if (fault === 'missing-id') await route.fulfill({ json: jobPayload('job', language, { id: undefined, state: 'done', percent: 100 }) });
          else if (fault === 'unknown-state') await route.fulfill({ json: jobPayload('job', language, { state: 'unexpected' }) });
          else await route.fulfill({ json: jobPayload('job', language, { id: 'other-job' }) });
          return true;
        } });
        await open('job');
        await expect(page.locator('#job-status')).toContainText(text('result is unknown', 'пока неизвестен'));
        await expect(page.locator('#jp')).not.toHaveClass(/\bok\b/);
        broken = false; await page.locator('#job-retry').click();
        await expect(page.locator('#job-status')).toContainText(text('background', 'фоне'));
      });
    }

    await run('stale-response-cannot-rewind-newer-stage', language, async ({ page, install, open }) => {
      let polls = 0;
      await install({ job: () => jobPayload('job', language, ++polls === 1
        ? { revision: 0, stage: 'Fixture newer phase', lastUpdatedUtc: '2026-10-03T01:00:10Z' }
        : { revision: 0, stage: 'Fixture older phase', lastUpdatedUtc: '2026-10-03T01:00:01Z' }) });
      await open('job'); await expect(page.locator('#js')).toHaveText('Fixture newer phase');
      await page.clock.runFor(1000); await waitFor(() => polls >= 2);
      await expect(page.locator('#js')).toHaveText('Fixture newer phase');
    });

    await run('monotonic-revision-wins-over-clock-skew', language, async ({ page, install, open }) => {
      let payload = jobPayload('job', language, { revision: 9, stage: 'Before clock rollback', startupStep: 2, lastUpdatedUtc: '2026-10-03T01:00:20Z' });
      await install({ job: () => payload }); await open('job');
      await expect(page.locator('#js')).toHaveText('Before clock rollback');
      payload = { ...payload, revision: 10, stage: 'New revision after clock rollback', startupStep: 3, lastUpdatedUtc: '2026-10-03T01:00:10Z' };
      await page.clock.runFor(1000);
      await expect(page.locator('#js')).toHaveText(payload.stage);
      await expect(page.locator('#startup-steps [aria-current=step]')).toHaveAttribute('data-step', '3');
      payload = { ...payload, revision: 8, stage: 'Outdated response with later wall clock', startupStep: 1, lastUpdatedUtc: '2026-10-03T01:00:30Z' };
      await page.clock.runFor(1000);
      await expect(page.locator('#js')).toHaveText('New revision after clock rollback');
      await expect(page.locator('#startup-steps [aria-current=step]')).toHaveAttribute('data-step', '3');
    });

    await run('gone-job-never-becomes-success', language, async ({ page, install, open, text }) => {
      await install({ job: () => ({ state: 'gone' }) }); await open('job');
      await expect(page.locator('#job-status')).toContainText(text('no longer available', 'больше недоступны'));
      await expect(page.locator('#jp')).not.toHaveClass(/\bok\b/);
      await expect(page.locator('.bar')).toBeHidden();
      expect(new URL(page.url()).searchParams.get('job')).toBe('fixture-job');
    });

    await run('native-double-click-submits-once-with-button-value', language, async ({ page, install, open }) => {
      const posts = [];
      await install({ handle: async (route, request) => {
        if (request.method !== 'POST') return false;
        posts.push(request); await route.fulfill({ status: 204, body: '' }); return true;
      } }); await open();
      const button = page.locator('form.mode button');
      const value = await button.getAttribute('value'), name = await button.getAttribute('name');
      const submissions = await button.evaluate(b => {
        const events = []; b.form.addEventListener('submit', e => events.push(e));
        b.click(); b.click(); return events.map(e => e.defaultPrevented);
      });
      expect(submissions).toEqual([false, true]);
      await waitFor(() => posts.length === 1);
      expect(new URL(posts[0].url).pathname).toBe('/mode');
      if (name) expect(new URLSearchParams(posts[0].body).get(name)).toBe(value);
      await expect(button).toHaveAttribute('aria-disabled', 'true');
      await page.clock.runFor(12000); expect(posts).toHaveLength(1);
    });

    await run('dirty-dialog-cancel-escape-save-and-delete-confirmation', language, async ({ page, install, open }) => {
      const posts = [];
      await install({ handle: async (route, request) => {
        if (request.method !== 'POST') return false;
        posts.push(request); await route.fulfill({ status: 204, body: '' }); return true;
      } }); await open('subs');
      const editor = page.locator('dialog.sub-modal').first();
      await page.locator('[data-sub-edit]').first().click(); await expect(editor).toBeVisible();
      await editor.locator('input[name=name]').fill('Unsaved fixture name');
      let confirmations = 0, acceptClose = false;
      page.on('dialog', dialog => { confirmations++; void (acceptClose ? dialog.accept() : dialog.dismiss()); });
      await editor.locator('[data-sub-close]').click(); await expect(editor).toBeVisible();
      await page.keyboard.press('Escape'); await expect(editor).toBeVisible();
      await expect(editor.locator('[data-sub-close]')).toBeFocused();
      await expect(editor.locator('input[name=name]')).toHaveValue('Unsaved fixture name');
      expect(confirmations).toBe(2);
      await editor.locator('button[type=submit]').click();
      await waitFor(() => posts.length === 1);
      expect(new URL(posts[0].url).pathname).toBe('/subs/save');
      expect(new URLSearchParams(posts[0].body).get('name')).toBe('Unsaved fixture name');
      await page.reload();
      await page.locator('form[action="/subs/remove"] button').first().click();
      expect(confirmations).toBe(3); expect(posts).toHaveLength(1);
      expect(page.url()).toContain('fixture=subs');
      await page.locator('[data-sub-edit]').first().click();
      await editor.locator('input[name=name]').fill('Discard this fixture edit');
      acceptClose = true; await editor.locator('[data-sub-close]').click();
      await expect(editor).toBeHidden(); expect(confirmations).toBe(4);
      await expect(page.locator('[data-sub-edit]').first()).toBeFocused();
      expect(posts).toHaveLength(1);
    });

    await run('offline-state-refresh-retry-and-preserved-focus', language, async ({ page, install, open, text }) => {
      let offline = true;
      await install({ handle: async (route, request) => {
        if (request.type !== 'fetch') return false;
        if (offline) { await route.abort('internetdisconnected'); return true; }
        return false;
      } }); await open();
      await page.clock.runFor(10000);
      await expect(page.locator('body')).toHaveClass(/panel-stale/);
      await expect(page.locator('#freshness-text')).toContainText(text('unreachable', 'Нет связи'));
      const control = page.locator('[data-live] button').first(); await control.focus();
      const label = await control.innerText();
      offline = false; await page.locator('#refresh-retry').click();
      await expect(page.locator('body')).not.toHaveClass(/panel-stale/);
      // A retry click intentionally moves focus; automatic refresh must not.
      await control.focus(); await page.clock.runFor(10000);
      await expect(page.locator(':focus')).toHaveText(label);
      const details = page.locator('details.route-details').first();
      if (await details.count()) {
        await details.locator('summary').click(); await page.clock.runFor(10000);
        await expect(details).toHaveAttribute('open', '');
      }
    });

    await run('dirty-form-survives-refresh-after-blur', language, async ({ page, install, open, text }) => {
      await install(); await open('apps');
      const form = page.locator('[data-live] form[action="/apps/rename"]').first();
      const input = form.locator('input[type=text]').first();
      await form.locator('..').locator('summary').click();
      await input.fill('Unsaved app name'); await page.locator('#theme').focus();
      await page.clock.runFor(10000);
      await expect(input).toHaveValue('Unsaved app name');
      await expect(form).toHaveAttribute('data-dirty', '1');
      await expect(page.locator('[data-live].live-stale')).toHaveCount(1);
      await expect(page.locator('[data-deferred-notice]')).toContainText(text('may be stale', 'могут устареть'));
    });

    await run('navigation-back-forward-and-resume', language, async ({ page, install, open }) => {
      let polls = 0;
      await install({ job: () => { polls++; return jobPayload('job', language); } }); await open('job');
      await waitFor(() => polls > 0);
      await page.goto(fixtureUrl('apps', language)); await expect(page.locator('.app-cards')).toBeVisible();
      const afterLeave = polls; await page.clock.runFor(5000); expect(polls).toBe(afterLeave);
      await page.goBack(); await expect(page.locator('#jp')).toBeVisible(); await waitFor(() => polls > afterLeave);
      await page.goForward(); await expect(page.locator('.app-cards')).toBeVisible();
      expect(new URL(page.url()).searchParams.get('fixture')).toBe('apps');
      await page.goBack(); await expect(page.locator('#jp')).toBeVisible();
      const beforeResume = polls;
      await page.clock.fastForward(60000); // Sleep/resume, firing each due timer at most once.
      await waitFor(() => polls > beforeResume);
      await expect(page.locator('#jn')).not.toContainText('%');
      await expect(page.locator('#jp')).not.toHaveClass(/\bok\b/);
    });

    await run('pagehide-aborts-pending-body-and-stops-polling', language, async ({ context, page, install, open }) => {
      await injectHungBody(context); await install(); await open('job');
      await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pagehide')));
      await waitFor(() => page.evaluate(() => window.__bodyFault.aborted === 1));
      await page.clock.runFor(20000);
      expect(await page.evaluate(() => window.__bodyFault.calls)).toBe(1);
      expect(await page.evaluate(() => window.CehoPanel.active())).toBe(false);
    });
  }
} catch (error) { results.push({ name: 'browser-launch-or-setup', status: 'failed', error: String(error) }); }
finally {
  await fs.writeFile(path.join(artifacts, 'browser-results.json'), JSON.stringify({ ...provenance, results }, null, 2));
  await browser?.close();
}
console.log(results);
if (results.some(result => result.status === 'failed')) process.exitCode = 1;
