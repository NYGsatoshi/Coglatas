import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { chromium, expect } from '@playwright/test';
import { readSettledScrollTop } from './settled-scroll-position.ts';

let browser;
before(async () => { browser = await chromium.launch(); });
after(async () => { await browser?.close(); });

async function fixture(run) {
  const page = await browser.newPage();
  try {
    await page.setContent('<div id="pane" tabindex="0" style="height:80px;width:180px;overflow:auto;scroll-behavior:smooth"><div style="height:1600px">Synthetic scroll fixture</div></div>');
    await run(page, page.locator('#pane'));
  } finally {
    await page.close();
  }
}

test('native PageDown and Home must reach settled real scroll positions', async () => {
  await fixture(async (page, pane) => {
    await pane.focus();
    await page.keyboard.press('PageDown');
    await expect.poll(() => readSettledScrollTop(pane), { timeout: 2000 }).toBeGreaterThan(0);
    assert.equal(await readSettledScrollTop(pane), await pane.evaluate(element => element.scrollTop));
    await page.keyboard.press('Home');
    await expect.poll(() => readSettledScrollTop(pane), { timeout: 2000 }).toBe(0);
  });
});

test('a prevented PageDown cannot satisfy the original positive assertion', async () => {
  await fixture(async (page, pane) => {
    await pane.evaluate(element => element.addEventListener('keydown', event => {
      if (event.key === 'PageDown') event.preventDefault();
    }));
    await pane.focus();
    await page.keyboard.press('PageDown');
    assert.equal(await readSettledScrollTop(pane), 0);
    await assert.rejects(expect.poll(() => readSettledScrollTop(pane), { timeout: 250 }).toBeGreaterThan(0));
  });
});

test('a prevented Home cannot satisfy the original exact-zero assertion', async () => {
  await fixture(async (page, pane) => {
    await pane.evaluate(element => {
      element.scrollTo({ top: 200, behavior: 'instant' });
      element.addEventListener('keydown', event => {
        if (event.key === 'Home') event.preventDefault();
      });
    });
    await pane.focus();
    await page.keyboard.press('Home');
    assert.equal(await readSettledScrollTop(pane), 200);
    await assert.rejects(expect.poll(() => readSettledScrollTop(pane), { timeout: 250 }).toBe(0));
  });
});

test('a moving scroll position is unavailable rather than fabricated as success', async () => {
  await fixture(async (page, pane) => {
    await pane.evaluate(element => {
      element.style.scrollBehavior = 'auto';
      const move = () => {
        element.scrollTop += 1;
        requestAnimationFrame(move);
      };
      requestAnimationFrame(move);
    });
    assert.ok(Number.isNaN(await readSettledScrollTop(pane)));
  });
});
