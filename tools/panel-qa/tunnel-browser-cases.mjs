import { expect } from '@playwright/test';
import { fixtureHtml, fixtureUrl, tunnelPayload } from './fixtures.mjs';

const waitFor = fn => expect.poll(fn, { timeout: 5000 }).toBeTruthy();
const source = (page, label) => page.locator(`[data-tunnel-source][data-label="${label}"]`);
function resultFor(language, changes = {}) {
  const app = tunnelPayload('tunnel-pending', language).apps.find(app => /[\\/]notes\.exe$/.test(app.path));
  if (!app) throw new Error('Synthetic Notes identity missing from C# fixture');
  return { ok: true, duplicate: false, covered: false, appId: app.id, path: app.path,
    pending: true, applied: false, message: 'Fixture rule saved; apply separately', status: 'added', ...changes };
}
async function review(page) {
  await source(page, 'Fixture Notes').click();
  await expect(page.locator('#tunnel-confirm')).toBeVisible();
}
async function post(page) {
  await review(page); await page.locator('#tunnel-confirm-add').click();
}
async function fileDrop(page, name) {
  await page.locator('#tunnel-stage').evaluate((stage, name) => {
    const transfer = new DataTransfer();
    transfer.items.add(new File(['NEVER_UPLOAD_FIXTURE_FILE_CONTENT_7ac38'], name, { type: 'application/octet-stream' }));
    stage.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: transfer }));
    stage.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
  }, name);
}

// These cases intentionally run only in the same lockfile-pinned browser harness.
// All routes are synthetic; no network listener or live engine is used.
export async function runTunnelBrowserCases(run, language) {
  await run('tunnel-keyboard-drag-cancel-and-mobile', language, async ({ page, install, open }) => {
    const routing = await install(); await open('tunnel-ready');
    const notes = source(page, 'Fixture Notes');
    await notes.focus(); await page.keyboard.press('Enter');
    await expect(page.locator('#tunnel-confirm')).toBeVisible();
    await expect(page.locator('#tunnel-confirm-path-input')).toHaveValue(await notes.getAttribute('data-path'));
    await page.keyboard.press('Escape'); await expect(page.locator('#tunnel-confirm')).toBeHidden();
    await expect(notes).toBeFocused();
    await notes.dragTo(page.locator('#tunnel-stage'));
    await expect(page.locator('#tunnel-confirm')).toBeVisible();
    await page.locator('#tunnel-cancel').click();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('#tunnel-choose').focus(); await page.keyboard.press('Enter');
    await expect(page.locator('#tunnel-search')).toBeFocused();
    await page.locator('#tunnel-search').fill('Fixture Notes');
    await expect(page.locator('[data-tunnel-source]:visible')).toHaveCount(1);
    await notes.focus(); await page.keyboard.press('Space');
    await expect(page.locator('#tunnel-confirm')).toBeVisible();
    await page.keyboard.press('Escape');
    expect(routing.requests.filter(request => request.method === 'POST')).toHaveLength(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });

  await run('tunnel-existing-card-focus-without-save-or-restart', language, async ({ page, install, open }) => {
    const routing = await install(); await open('tunnel-ready');
    const browser = source(page, 'Fixture Browser'), card = page.locator('article[data-app-id]').first();
    const identity = await card.getAttribute('data-app-id');
    await browser.click(); await expect(card).toBeFocused();
    await expect(page.locator('#tunnel-confirm')).toBeHidden();
    await browser.dragTo(page.locator('#tunnel-stage')); await expect(card).toBeFocused();
    await page.reload(); await source(page, 'Fixture Browser').click();
    await expect(page.locator(`#app-${identity}`)).toBeFocused();
    expect(routing.requests.filter(request => request.method === 'POST')).toHaveLength(0);
  });

  await run('tunnel-external-file-is-name-hint-never-uploaded', language, async ({ context, page, install, open }) => {
    const posts = [];
    await context.addInitScript(() => {
      window.__fileReads = 0;
      const forbidden = () => { window.__fileReads++; throw new Error('External file content must not be read'); };
      File.prototype.text = forbidden; File.prototype.arrayBuffer = forbidden;
      FileReader.prototype.readAsText = forbidden; FileReader.prototype.readAsArrayBuffer = forbidden; FileReader.prototype.readAsDataURL = forbidden;
    });
    await install({ handle: async (route, request, url) => {
      if (request.method !== 'POST') return false;
      expect(url.pathname).toBe('/apps/installed'); posts.push(request);
      await route.fulfill({ json: { ok: false, status: 'error', message: 'Fixture stop after inspecting submitted fields' } }); return true;
    } }); await open('tunnel-ready');
    await fileDrop(page, 'totally-unknown.lnk');
    await expect(page.locator('#tunnel-confirm')).toBeVisible();
    await expect(page.locator('#tunnel-confirm-add')).toBeHidden();
    await expect(page.locator('#tunnel-candidates button')).toHaveCount(0);
    await expect(page.locator('#tunnel-confirm-path-input')).toHaveValue('');
    await expect(page.locator('#tunnel-confirm a.pick')).toHaveAttribute('href', '/apps/pick');
    await page.keyboard.press('Escape');
    await fileDrop(page, 'editor.exe');
    await expect(page.locator('#tunnel-candidates button')).toHaveCount(2);
    await expect(page.locator('#tunnel-confirm-add')).toBeHidden();
    expect(posts).toHaveLength(0);
    await page.locator('#tunnel-candidates button').nth(1).click();
    const installedPath = await source(page, 'Fixture Editor Beta').getAttribute('data-path');
    await expect(page.locator('#tunnel-confirm-path-input')).toHaveValue(installedPath);
    expect(posts).toHaveLength(0);
    await page.locator('#tunnel-confirm-add').click(); await waitFor(() => posts.length === 1);
    const body = new URLSearchParams(posts[0].body);
    expect([...body.keys()].sort()).toEqual(['confirm_add', 'intent', 'path', 'tab']);
    expect(body.get('path')).toBe(installedPath);
    expect(posts[0].body).not.toContain('NEVER_UPLOAD_FIXTURE_FILE_CONTENT');
    expect(await page.evaluate(() => window.__fileReads)).toBe(0);
  });

  await run('tunnel-double-save-pending-authoritative-apply-and-reduced-motion', language, async ({ page, install, open, text }) => {
    let state = 'tunnel-ready', release;
    const posts = [];
    await install({ tunnel: () => tunnelPayload(state, language), handle: async (route, request, url) => {
      if (request.method === 'POST') {
        expect(url.pathname).toBe('/apps/installed'); posts.push(request);
        await new Promise(resolve => { release = resolve; });
        await route.fulfill({ json: resultFor(language) }); return true;
      }
      if (request.type === 'fetch' && url.pathname === '/') {
        await route.fulfill({ contentType: 'text/html', body: fixtureHtml(state, language) }); return true;
      }
      return false;
    } }); await open('tunnel-ready');
    await review(page);
    await page.locator('#tunnel-confirm-form').evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
    await waitFor(() => posts.length === 1);
    await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'saving');
    await page.clock.runFor(6100);
    await expect(page.locator('#tunnel-status')).toContainText(text('delayed', 'задерживается'));
    expect(posts).toHaveLength(1);
    state = 'tunnel-pending'; release();
    await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
    await expect(page.locator('#tunnel-token')).toBeVisible();
    await page.clock.runFor(2600);
    await expect(page.locator('#tunnel-stage')).not.toHaveAttribute('data-phase', 'applied');
    const fields = new URLSearchParams(posts[0].body);
    expect(fields.get('intent')).toBe('tunnel'); expect(fields.get('confirm_add')).toBe('1');
    expect(fields.has('confirm_apply')).toBe(false);
    state = 'tunnel-applied'; await page.clock.runFor(2600);
    await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'applied');
    await expect(page.locator('#tunnel-status')).toContainText(text('check its traffic', 'проверьте её трафик'));
    expect(await page.locator('#tunnel-token').evaluate(element => getComputedStyle(element).animationName)).toBe('none');
    expect(await page.locator('#tunnel-token').evaluate(element => getComputedStyle(element).opacity)).toBe('0');
    const appId = resultFor(language).appId;
    await expect(page.locator(`#app-${appId} [data-observation]`)).toHaveAttribute('data-observation', 'quiet');
    await source(page, 'Fixture Notes').click(); await expect(page.locator(`#app-${appId}`)).toBeFocused();
    expect(posts).toHaveLength(1);
  });

  for (const fault of ['offline', 'http-error', 'invalid-json', 'rejected']) {
    await run(`tunnel-${fault}-save-and-explicit-retry`, language, async ({ page, install, open }) => {
      let broken = true, state = 'tunnel-ready';
      const posts = [];
      await install({ tunnel: () => tunnelPayload(state, language), handle: async (route, request, url) => {
        if (request.type === 'fetch' && request.method === 'GET' && url.pathname === '/') {
          await route.fulfill({ contentType: 'text/html', body: fixtureHtml(state, language) }); return true;
        }
        if (request.method !== 'POST') return false;
        expect(url.pathname).toBe('/apps/installed'); posts.push(request);
        if (!broken) await route.fulfill({ json: resultFor(language) });
        else if (fault === 'offline') await route.abort('internetdisconnected');
        else if (fault === 'http-error') await route.fulfill({ status: 503, body: 'Fixture unavailable' });
        else if (fault === 'invalid-json') await route.fulfill({ contentType: 'application/json', body: '{invalid' });
        else await route.fulfill({ json: { ok: false, status: 'error', message: 'Fixture rejected the selected path' } });
        return true;
      } }); await open('tunnel-ready'); await post(page);
      await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', /unknown|error/);
      await expect(page.locator('#tunnel-retry-save')).toBeVisible();
      expect(posts).toHaveLength(1);
      broken = false; state = 'tunnel-pending';
      await page.locator('#tunnel-retry-save').click();
      await expect(page.locator('#tunnel-confirm')).toBeVisible(); expect(posts).toHaveLength(1);
      await page.locator('#tunnel-confirm-add').click();
      await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
      expect(posts).toHaveLength(2);
      expect(posts[0].body).toBe(posts[1].body);
    });
  }

  await run('tunnel-pending-reload-back-forward-never-reposts', language, async ({ page, install, open }) => {
    let state = 'tunnel-ready'; const posts = [];
    await install({ tunnel: () => tunnelPayload(state, language), handle: async (route, request, url) => {
      if (request.method === 'POST') {
        expect(url.pathname).toBe('/apps/installed'); posts.push(request); state = 'tunnel-pending';
        await route.fulfill({ json: resultFor(language) }); return true;
      }
      if (url.pathname === '/' && url.searchParams.get('tab') === 'apps') {
        await route.fulfill({ contentType: 'text/html', body: fixtureHtml(state, language) }); return true;
      }
      return false;
    } }); await open('tunnel-ready'); await post(page);
    await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
    await page.reload(); await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
    await page.goto(fixtureUrl('help', language));
    await page.goBack(); await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
    await page.goForward(); expect(new URL(page.url()).searchParams.get('fixture')).toBe('help');
    await page.goBack(); await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
    await page.goto(fixtureUrl('tunnel-pending', language) + '&app_result=added&applied=true&job=unrelated-success');
    await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
    expect(posts).toHaveLength(1);
  });
}
