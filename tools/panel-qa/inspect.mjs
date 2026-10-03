import fs from 'node:fs/promises';
import path from 'node:path';
import { expect } from '@playwright/test';
import { artifacts, languages, states, loadFixtures, launch, routeFixtures, fixtureUrl, pauseClock } from './fixtures.mjs';

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
        const routes = await routeFixtures(context, { language, jobState: state.startsWith('startup-') ? state : 'job' });
        try {
          await pauseClock(page);
          await page.goto(fixtureUrl(state, language));
          if (state.startsWith('startup-')) {
            await expect(page.locator('#jp')).toBeVisible();
            if (state === 'startup-error') await expect(page.locator('#jp')).toHaveClass(/err/);
            else await expect(page.locator('#jn')).toHaveText(language === 'ru' ? 'Выполняется' : 'In progress');
            if (state === 'startup-delayed') await expect(page.locator('#job-status')).toHaveClass(/warn/);
          }

          if (state === 'settings') await page.locator('#settings > summary').click();
          await expect(page.locator('html')).toHaveAttribute('lang', language);
          await expect(page.locator('.fixture-banner')).toBeVisible();
          const overflow = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth }));
          await page.screenshot({ path: path.join(out, `${name}.png`), fullPage: true, animations: 'disabled' });
          expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.width);
          expect(pageErrors).toEqual([]);
          expect(routes.errors).toEqual([]);
          expect(routes.unexpected).toEqual([]);
          results.push({ name, status: 'passed', file: `${name}.png`, ...overflow });
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
