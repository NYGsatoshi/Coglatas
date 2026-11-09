import assert from "node:assert/strict";
import { chromium, devices } from "@playwright/test";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  observeSearchDrawerCase,
  validateSearchDrawerObservation,
} from "./helpers/search-drawer-observation.mjs";
const browser = await chromium.launch({ headless: true });
const original = process.cwd(),
  results = [];
const html = `<app-message-search-filters><button data-testid="message-filter-drawer-toggle" aria-expanded="true">Toggle</button><button data-testid="message-advanced-filters-open">Open</button></app-message-search-filters><script>document.addEventListener('click',event=>{if(event.target.matches('[data-testid="message-advanced-filters-open"]')){const drawer=document.createElement('section');drawer.dataset.testid='message-advanced-filters-drawer';drawer.innerHTML='<input data-testid="message-advanced-author" />';document.querySelector('app-message-search-filters').append(drawer)}})</script>`;
try {
  for (const name of [
    "native-open-private-text",
    "host-replacement",
    "finite-cap",
  ]) {
    const root = mkdtempSync(join(tmpdir(), "search-native-")),
      context = await browser.newContext({ ...devices["Pixel 5"] });
    const page = await context.newPage();
    process.chdir(root);
    try {
      await observeSearchDrawerCase(
        page,
        { project: { name: "chromium-mobile" }, retry: 0 },
        async () => {
          await page.goto("data:text/html," + encodeURIComponent(html));
          await page
            .getByTestId("message-advanced-filters-open")
            .press("Enter");
          await page
            .getByTestId("message-advanced-author")
            .fill("private-canary-never-publish");
          if (name === "host-replacement") {
            await page.evaluate(() => {
              document.querySelector("app-message-search-filters").remove();
              const loading = document.createElement("p");
              loading.dataset.testid = "messages-list-loading";
              document.body.append(loading);
            });
            await page.evaluate(() => {
              document
                .querySelector('[data-testid="messages-list-loading"]')
                .remove();
              const host = document.createElement("app-message-search-filters");
              host.innerHTML =
                '<button data-testid="message-advanced-filters-open">Open</button>';
              document.body.append(host);
            });
          }
          if (name === "finite-cap")
            for (let i = 0; i < 60; i++) await page.keyboard.press("Tab");
        },
      );
      const text = readFileSync(
          "artifacts/search-drawer-observation/chromium-mobile-attempt-0.json",
          "utf8",
        ),
        value = validateSearchDrawerObservation(JSON.parse(text));
      assert.equal(text.includes("private-canary"), false);
      assert.ok(
        value.events.some(
          (event) =>
            event.kind === "KEYDOWN" &&
            event.target === "OPENER" &&
            event.key === "ENTER",
        ),
      );
      if (name === "host-replacement") {
        assert.ok(
          value.events.some(
            (event) =>
              event.parentLoading &&
              event.hostNode === 0 &&
              !event.trackedHostConnected,
          ),
        );
        assert.equal(
          new Set(value.events.map((event) => event.hostNode).filter(Boolean))
            .size,
          2,
        );
        assert.equal(value.events.at(-1).drawerOpen, false);
      } else if (name === "finite-cap") {
        assert.equal(value.events.length, 128);
        assert.equal(value.truncated, true);
        assert.equal(value.classification, "INCOMPLETE");
      } else {
        assert.equal(value.truncated, false);
        assert.ok(value.events.some((event) => event.drawerOpen));
      }
      results.push({
        name,
        events: value.events.length,
        truncated: value.truncated,
        classification: value.classification,
        bytes: Buffer.byteLength(text),
        outcome: "PASS",
      });
    } finally {
      process.chdir(original);
      await context.close();
      rmSync(root, { recursive: true, force: true });
    }
  }
  console.log(JSON.stringify(results));
} finally {
  await browser.close();
  process.chdir(original);
}
