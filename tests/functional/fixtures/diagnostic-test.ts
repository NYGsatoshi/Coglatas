import { test as base, type BrowserContext, type Locator, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { createBrowserObservation, OBSERVATION_ATTACHMENT, structuralSnapshotHtml, validateBrowserObservation } from './functional-diagnostics.mjs';

const failureSnapshots = new WeakMap<Page, () => Promise<void>>();

/** Capture structure before the owner's finally block changes product state. */
export async function captureFunctionalFailure(page: Page): Promise<void> {
  await failureSnapshots.get(page)?.().catch(() => {});
}

export async function diagnosticStep<T>(title: string, page: Page, body: () => Promise<T>): Promise<T> {
  return test.step(title, async () => {
    try { return await body(); }
    catch (error) { await captureFunctionalFailure(page); throw error; }
  });
}

/** Observe only structural booleans and status metadata; never read protected DOM text. */
export const test = base.extend<{ _functionalDiagnostics: void }>({
  _functionalDiagnostics: [async ({ browser, context, page, baseURL }, use, testInfo) => {
    if (process.env.COGLATAS_FUNCTIONAL_EVIDENCE !== '1') { await use(); return; }
    const observer = createBrowserObservation(baseURL ?? 'http://127.0.0.1:5080');
    const observePage = (observed: typeof page) => {
      observed.on('console', observer.onConsole);
      observed.on('pageerror', observer.onPageError);
    };
    context.on('request', observer.onRequest);
    context.on('response', observer.onResponse);
    context.on('requestfailed', observer.onRequestFailed);
    context.on('page', observePage);
    context.pages().forEach(observePage);
    const stopObservation = () => {
      context.off('request', observer.onRequest);
      context.off('response', observer.onResponse);
      context.off('requestfailed', observer.onRequestFailed);
      context.off('page', observePage);
      context.pages().forEach((observed) => {
        observed.off('console', observer.onConsole);
        observed.off('pageerror', observer.onPageError);
      });
    };
    let failureCaptured = false;
    const captureProjection = async (checkpoint: 'failed-step' | 'after-test-cleanup') => {
      const visible = async (locator: Locator) => locator.first().isVisible().catch(() => false);
      const enabled = async (locator: Locator) => (await locator.count().catch(() => 0)) > 0 && locator.first().isEnabled({ timeout: 250 }).catch(() => false);
      observer.data.projectionCheckpoint = checkpoint;
      if (page.isClosed()) return;
      const files = page.getByTestId('files-page'), detail = page.getByTestId('files-preview-pane');
      observer.data.projection.filesPage = await visible(files);
      if (observer.data.projection.filesPage) {
        observer.data.projection.uploaderEnabled = await enabled(files.locator('app-coglatas-file-uploader input[type="file"]'));
        observer.data.projection.inventoryRows = await visible(files.locator('[data-grid-row-id], [row-id]'));
        observer.data.projection.selectedRows = await visible(files.locator('[role="row"][aria-selected="true"], [data-grid-row-id][aria-selected="true"], [row-id][aria-selected="true"]'));
        observer.data.projection.detailPane = await visible(detail);
        observer.data.projection.detailHeading = await visible(detail.getByRole('heading'));
        observer.data.projection.downloadEnabled = await enabled(detail.getByTestId('files-preview-download'));
        observer.data.projection.errorIndicator = await visible(files.getByRole('alert'));
      }
    };
    failureSnapshots.set(page, async () => {
      if (failureCaptured) return;
      failureCaptured = true;
      stopObservation();
      await captureProjection('failed-step');
    });
    await use();
    failureSnapshots.delete(page);
    stopObservation();
    if (!failureCaptured) await captureProjection('after-test-cleanup');
    if (['failed', 'timedOut', 'interrupted'].includes(testInfo.status ?? '')) {
      const domain = process.env.COGLATAS_FUNCTIONAL_DOMAIN;
      const journey = testInfo.annotations.find((annotation) => annotation.type === 'journey')?.description;
      if (domain && ['core', 'files', 'collaboration', 'authz-negative'].includes(domain) && journey && /^FUNC-(?:TASK-001|FILE-002|MSG-001|NOTIF-001|ANN-001|AUTHZ-00[12])$/.test(journey)) {
        let safeContext: BrowserContext | undefined;
        try {
          safeContext = await browser.newContext({ javaScriptEnabled: false, serviceWorkers: 'block', storageState: { cookies: [], origins: [] }, deviceScaleFactor: 1, viewport: { width: 900, height: 1200 } });
          const safePage = await safeContext.newPage();
          await safePage.setContent(structuralSnapshotHtml(observer.data, {
            commitSha: process.env.TARGET_SHA, runId: process.env.GITHUB_RUN_ID, runAttempt: process.env.GITHUB_RUN_ATTEMPT, domain, journeyId: journey,
            gate: process.env.COGLATAS_FUNCTIONAL_SELECTED_GATES, retry: testInfo.retry,
          }), { timeout: 5000 });
          mkdirSync('artifacts/functional', { recursive: true });
          const name = `structural-${domain}-${journey}-${process.env.TARGET_SHA}-${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}-${testInfo.retry}.png`;
          const bytes = await safePage.screenshot({ path: `artifacts/functional/${name}`, fullPage: true, timeout: 5000 });
          observer.data.structuralSnapshot = { name, sha256: createHash('sha256').update(bytes).digest('hex') };
          observer.data.structuralSnapshotState = 'CAPTURED';
        } catch {
          observer.data.structuralSnapshotState = 'UNAVAILABLE';
        } finally {
          await safeContext?.close().catch(() => {});
        }
      } else observer.data.structuralSnapshotState = 'UNAVAILABLE';
    }
    await testInfo.attach(OBSERVATION_ATTACHMENT, { body: Buffer.from(JSON.stringify(validateBrowserObservation(observer.data))), contentType: 'application/json' });
  }, { auto: true }],
});
