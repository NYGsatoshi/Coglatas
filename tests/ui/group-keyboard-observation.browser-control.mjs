import assert from "node:assert/strict";
import { chromium, devices } from "@playwright/test";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  observeGroupKeyboardCase,
  validateGroupKeyboardObservation,
} from "./helpers/group-keyboard-observation.mjs";

// Recorder controls only: no product correction or canonical-owner qualification.
const html = `<aside class="dm-page__group-details" style="max-height:480px;overflow:auto"><details open data-testid="group-members-panel"><summary>Members</summary><ul class="group-members__list" tabindex="0" style="height:200px;overflow:auto">${Array.from({ length: 60 }, () => '<li style="height:40px">private-canary-not-published</li>').join("")}</ul></details></aside>`;
const browser = await chromium.launch({ headless: true }),
  original = process.cwd(),
  results = [];
try {
  for (const name of [
    "native-scrollend",
    "list-replacement",
    "finite-cap",
    "missing-list",
  ]) {
    const root = mkdtempSync(join(tmpdir(), "group-observer-native-"));
    const context = await browser.newContext({
        ...devices["Pixel 5"],
        viewport: { width: 1536, height: 600 },
      }),
      page = await context.newPage();
    process.chdir(root);
    try {
      await observeGroupKeyboardCase(
        page,
        { project: { name: "chromium-mobile" }, retry: 0 },
        async () => {
          await page.goto(
            "data:text/html," +
              encodeURIComponent(
                name === "missing-list" ? "<p>No list</p>" : html,
              ),
          );
          if (name === "missing-list") return;
          const list = page.locator(".group-members__list");
          await list.focus();
          if (name === "native-scrollend") {
            await list.evaluate((element) => {
              window.__endObserved = false;
              element.addEventListener(
                "scrollend",
                () => {
                  window.__endObserved = true;
                },
                { once: true },
              );
            });
            await list.press("End");
            await page.waitForFunction(
              () => window.__endObserved === true,
              {},
              { timeout: 2000 },
            );
            await list.press("Home");
            await page.waitForFunction(
              () =>
                document.querySelector(".group-members__list").scrollTop === 0,
              {},
              { timeout: 2000 },
            );
            await list.evaluate((element) =>
              element.dispatchEvent(new Event("scrollend")),
            );
          }
          if (name === "list-replacement") {
            await page.evaluate(() =>
              document.querySelector(".group-members__list").remove(),
            );
            await page.evaluate(() => {
              const list = document.createElement("ul");
              list.className = "group-members__list";
              list.tabIndex = 0;
              document.querySelector("details").append(list);
            });
            await page.locator(".group-members__list").focus();
            await page.keyboard.press("Home");
          }
          if (name === "finite-cap")
            for (let i = 0; i < 60; i++) await page.keyboard.press("Tab");
        },
      );
      const text = readFileSync(
          "artifacts/group-keyboard-observation/chromium-mobile-attempt-0.json",
          "utf8",
        ),
        value = validateGroupKeyboardObservation(JSON.parse(text));
      assert.equal(value.schemaVersion, 2);
      assert.equal(text.includes("private-canary"), false);
      if (name === "native-scrollend") {
        const keys = value.events.filter(
          (event) =>
            event.kind === "KEYDOWN" && ["HOME", "END"].includes(event.key),
        );
        assert.equal(keys.length, 2);
        assert.ok(
          keys.every(
            (event) =>
              event.listNode === event.targetNode &&
              event.listNode === event.activeNode &&
              event.listConnected &&
              event.targetConnected &&
              event.activeConnected,
          ),
        );
        assert.ok(
          value.events.some(
            (event) =>
              event.kind === "SCROLL_END" &&
              event.target === "LIST" &&
              event.trusted,
          ),
        );
        assert.ok(
          value.events.some(
            (event) => event.kind === "SCROLL_END" && event.trusted === false,
          ),
        );
      }
      if (name === "list-replacement") {
        assert.ok(
          value.events.some(
            (event) => event.listNode === 0 && !event.trackedListConnected,
          ),
        );
        assert.equal(
          new Set(value.events.map((event) => event.listNode).filter(Boolean))
            .size,
          2,
        );
      }
      if (name === "finite-cap") {
        assert.equal(value.events.length, 128);
        assert.equal(value.truncated, true);
        assert.equal(value.classification, "INCOMPLETE");
      }
      if (name === "missing-list")
        assert.equal(value.classification, "INCOMPLETE");
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
  process.chdir(original);
  await browser.close();
}
