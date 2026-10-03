import fs from 'node:fs/promises';
import path from 'node:path';
import { expect } from '@playwright/test';
import { artifacts, languages, states, loadFixtures, launch, routeFixtures, fixtureUrl, pauseClock, tunnelPayload } from './fixtures.mjs';

const out = process.env.CEHO_SCREENSHOTS || path.join(artifacts, 'screenshots');
await fs.mkdir(out, { recursive: true });
const provenance = await loadFixtures();
const results = [];
let browser;
try {
  browser = await launch();
  for (const language of languages) for (const theme of ['light', 'dark'])
    for (const [device, width, height] of [['desktop', 1280, 1100], ['mobile', 390, 844]])
      for (const state of states) {
        const name = `${state}-${language}-${device}-${theme}`;
        const context = await browser.newContext({ viewport: { width, height }, colorScheme: theme, serviceWorkers: 'block', reducedMotion: 'reduce' });
        const page = await context.newPage();
        page.setDefaultTimeout(5000);
        const pageErrors = [];
        page.on('pageerror', error => pageErrors.push(String(error)));
        const routes = await routeFixtures(context, { language, jobState: state.startsWith('startup-') ? state : 'job', tunnelState: state.startsWith('tunnel-') ? state : 'tunnel-ready' });
        try {
          if (['tunnel-pending', 'tunnel-applied', 'tunnel-observed'].includes(state)) {
            const app = tunnelPayload(state, language).apps.find(app => /[\\/]notes\.exe$/.test(app.path));
            if (!app) throw new Error('Synthetic Notes identity missing');
            await context.addInitScript(app => {
              sessionStorage.setItem('ceho-tunnel-pending:' + location.origin, JSON.stringify({ id: app.id, path: app.path, name: 'Fixture Notes', at: Date.now() }));
            }, app);
          }
          await pauseClock(page);
          await page.goto(fixtureUrl(state, language));
          if (state.startsWith('startup-')) {
            await expect(page.locator('#jp')).toBeVisible();
            if (state === 'startup-error') await expect(page.locator('#jp')).toHaveClass(/err/);
            else await expect(page.locator('#jn')).toHaveText(language === 'ru' ? 'Выполняется' : 'In progress');
            if (state === 'startup-delayed') await expect(page.locator('#job-status')).toHaveClass(/warn/);
          }

          if (state === 'tunnel-confirmation') {
            await page.locator('[data-tunnel-source][data-label="Fixture Notes"]').click();
            await expect(page.locator('#tunnel-confirm')).toBeVisible();
          }
          if (state === 'tunnel-pending') await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'pending');
          if (['tunnel-applied', 'tunnel-observed'].includes(state)) await expect(page.locator('#tunnel-stage')).toHaveAttribute('data-phase', 'applied');
          if (state === 'settings') await page.locator('#settings > summary').click();
          await expect(page.locator('html')).toHaveAttribute('lang', language);
          await expect(page.locator('.fixture-banner')).toBeVisible();
          // Actual geometry catches SVG intrinsic grid sizing that can overlap the
          // live status even when the document has no horizontal overflow.
          const layout = await page.evaluate(() => {
            const box = element => {
              const { top, right, bottom, left, width, height } = element.getBoundingClientRect();
              return { top, right, bottom, left, width, height };
            };
            const stage = document.querySelector('#tunnel-stage');
            const portal = stage?.querySelector('.tunnel-portal svg');
            const status = document.querySelector('#tunnel-status');
            const rgb = color => (color.match(/[\d.]+/g) || []).map(Number);
            const luminance = color => {
              const values = rgb(color).slice(0, 3).map(value => {
                const channel = value / 255;
                return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
              });
              return values[0] * 0.2126 + values[1] * 0.7152 + values[2] * 0.0722;
            };
            const background = element => {
              for (let current = element; current; current = current.parentElement) {
                const color = getComputedStyle(current).backgroundColor;
                const channels = rgb(color);
                if (channels.length === 3 || channels[3] === 1) return color;
              }
              throw new Error('No opaque background for body link');
            };
            const links = Array.from(document.querySelectorAll('.protection-summary a, #added-apps > a, .app-card-actions > a, .route-details > a, #tunnel-existing-link'))
              .filter(link => link.getClientRects().length > 0)
              .map(link => {
                const foreground = getComputedStyle(link).color, behind = background(link);
                const light = luminance(foreground), dark = luminance(behind);
                return { text: link.textContent.trim(), foreground, background: behind,
                  contrast: (Math.max(light, dark) + 0.05) / (Math.min(light, dark) + 0.05) };
              });
            return { tunnel: stage && portal && status ? { stage: box(stage), portal: box(portal), status: box(status),
              portalOverflow: getComputedStyle(portal).overflow } : null, links };
          });
          if (layout.tunnel) {
            const { stage, portal, status, portalOverflow } = layout.tunnel;
            expect(stage.height, 'Tunnel stage has reserved layout height').toBeGreaterThan(200);
            expect(portal.top, 'Portal stays inside stage top').toBeGreaterThanOrEqual(stage.top - 1);
            expect(portal.left, 'Portal stays inside stage left').toBeGreaterThanOrEqual(stage.left - 1);
            expect(portal.right, 'Portal stays inside stage right').toBeLessThanOrEqual(stage.right + 1);
            expect(portal.bottom, 'Portal stays inside stage bottom').toBeLessThanOrEqual(stage.bottom + 1);
            expect(status.top, 'Status text must not overlap the portal').toBeGreaterThanOrEqual(portal.bottom - 1);
            expect(portalOverflow, 'SVG paint is contained').toBe('hidden');
          }
          for (const link of layout.links) expect(link.contrast, `Body link contrast: ${link.text}`).toBeGreaterThanOrEqual(4.5);
          const overflow = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth }));
          await page.screenshot({ path: path.join(out, `${name}.png`), fullPage: true, animations: 'disabled' });
          expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.width);
          expect(pageErrors).toEqual([]);
          expect(routes.errors).toEqual([]);
          expect(routes.unexpected).toEqual([]);
          results.push({ name, status: 'passed', file: `${name}.png`, ...overflow, layout });
        } catch (error) {
          results.push({ name, status: 'failed', error: String(error), pageErrors, routingErrors: routes.errors, unexpectedRequests: routes.unexpected });
          await page.screenshot({ path: path.join(out, `${name}-failure.png`), fullPage: true, animations: 'disabled' }).catch(() => {});
        } finally { await context.close(); }
      }
} catch (error) { results.push({ name: 'browser-launch', status: 'failed', error: String(error) }); }
finally {
  await fs.writeFile(path.join(out, 'screenshot-results.json'), JSON.stringify({ ...provenance, expectedScreenshots: languages.length * 2 * 2 * states.length, results }, null, 2));
  await browser?.close();
}
const failed = results.filter(result => result.status === 'failed');
console.log(`${results.length - failed.length} screenshot cases passed; ${failed.length} failed. See ${out}/screenshot-results.json.`);
if (failed.length) { console.error(failed); process.exitCode = 1; }
