import { test as base, type Browser, type BrowserContext, type Locator, type Page, type TestInfo } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { createBrowserObservation, OBSERVATION_ATTACHMENT, structuralSnapshotHtml, validateBrowserObservation, type BrowserObservation } from './functional-diagnostics.mjs';

const failureSnapshots = new WeakMap<Page, () => Promise<void>>();
type Observer = ReturnType<typeof createBrowserObservation>;
type Projection = Pick<BrowserObservation, 'projection' | 'projectionCheckpoint'>;
type Snapshot = Pick<BrowserObservation, 'structuralSnapshot' | 'structuralSnapshotState'>;

/** Capture structure before the owner's finally block changes product state. */
export async function captureFunctionalFailure(page: Page): Promise<void> {
  await failureSnapshots.get(page)?.().catch(() => undefined);
}

export async function diagnosticStep<T>(title: string, page: Page, body: () => Promise<T>): Promise<T> {
  return test.step(title, async () => {
    try { return await body(); }
    catch (error) { await captureFunctionalFailure(page); throw error; }
  });
}

function observeContext(context: BrowserContext, observer: Observer): () => void {
  const observePage = (observed: Page): void => {
    observed.on('console', observer.onConsole);
    observed.on('pageerror', observer.onPageError);
  };
  context.on('request', observer.onRequest);
  context.on('response', observer.onResponse);
  context.on('requestfailed', observer.onRequestFailed);
  context.on('page', observePage);
  context.pages().forEach(observePage);
  return () => {
    context.off('request', observer.onRequest);
    context.off('response', observer.onResponse);
    context.off('requestfailed', observer.onRequestFailed);
    context.off('page', observePage);
    context.pages().forEach((observed) => {
      observed.off('console', observer.onConsole);
      observed.off('pageerror', observer.onPageError);
    });
  };
}

async function visible(locator: Locator): Promise<boolean> {
  return locator.first().isVisible().catch(() => false);
}

async function enabled(locator: Locator): Promise<boolean> {
  const count = await locator.count().catch(() => 0);
  return count > 0 && locator.first().isEnabled({ timeout: 250 }).catch(() => false);
}

async function captureProjection(page: Page, projectionCheckpoint: string): Promise<Projection> {
  const unobserved = { filesPage: null, uploaderEnabled: null, inventoryRows: null, selectedRows: null,
    detailPane: null, detailHeading: null, downloadEnabled: null, errorIndicator: null };
  if (page.isClosed()) { return { projection: unobserved, projectionCheckpoint }; }
  const detail = page.getByTestId('files-preview-pane');
  const files = page.getByTestId('files-page');
  const filesPage = await visible(files);
  if (!filesPage) { return { projection: { ...unobserved, filesPage }, projectionCheckpoint }; }
  const uploaderEnabled = await enabled(files.locator('app-coglatas-file-uploader input[type="file"]'));
  const inventoryRows = await visible(files.locator('[data-grid-row-id], [row-id]'));
  const selectedRows = await visible(files.locator('[role="row"][aria-selected="true"], [data-grid-row-id][aria-selected="true"], [row-id][aria-selected="true"]'));
  const detailPane = await visible(detail);
  const detailHeading = await visible(detail.getByRole('heading'));
  const downloadEnabled = await enabled(detail.getByTestId('files-preview-download'));
  const errorIndicator = await visible(files.getByRole('alert'));
  return { projection: { filesPage, uploaderEnabled, inventoryRows, selectedRows, detailPane, detailHeading, downloadEnabled, errorIndicator }, projectionCheckpoint };
}

async function captureSnapshot(browser: Browser, observation: BrowserObservation, testInfo: TestInfo): Promise<Snapshot> {
  const unavailable: Snapshot = { structuralSnapshot: null, structuralSnapshotState: 'UNAVAILABLE' };
  const domain = process.env.COGLATAS_FUNCTIONAL_DOMAIN;
  const journey = testInfo.annotations.find((annotation) => annotation.type === 'journey')?.description;
  if (!domain || !['core', 'files', 'collaboration', 'authz-negative'].includes(domain) || !journey || !/^FUNC-(?:TASK-001|FILE-002|MSG-001|NOTIF-001|ANN-001|AUTHZ-00[12])$/.test(journey)) { return unavailable; }
  try {
    const safeContext = await browser.newContext({ javaScriptEnabled: false, serviceWorkers: 'block', storageState: { cookies: [], origins: [] }, deviceScaleFactor: 1, viewport: { width: 900, height: 1200 } });
    try {
      const safePage = await safeContext.newPage();
      await safePage.setContent(structuralSnapshotHtml(observation, {
        commitSha: process.env.TARGET_SHA, runId: process.env.GITHUB_RUN_ID, runAttempt: process.env.GITHUB_RUN_ATTEMPT, domain, journeyId: journey,
        gate: process.env.COGLATAS_FUNCTIONAL_SELECTED_GATES, retry: testInfo.retry,
      }), { timeout: 5000 });
      mkdirSync('artifacts/functional', { recursive: true });
      const name = `structural-${domain}-${journey}-${process.env.TARGET_SHA}-${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}-${testInfo.retry}.png`;
      const bytes = await safePage.screenshot({ path: `artifacts/functional/${name}`, fullPage: true, timeout: 5000 });
      return { structuralSnapshot: { name, sha256: createHash('sha256').update(bytes).digest('hex') }, structuralSnapshotState: 'CAPTURED' };
    } finally {
      await safeContext.close().catch(() => undefined);
    }
  } catch {
    return unavailable;
  }
}

/** Observe only structural booleans and status metadata; never read protected DOM text. */
export const test = base.extend<{ _functionalDiagnostics: undefined }>({
  _functionalDiagnostics: [async ({ browser, context, page, baseURL }, use, testInfo): Promise<void> => {
    if (process.env.COGLATAS_FUNCTIONAL_EVIDENCE !== '1' && process.env.COGLATAS_FUNCTIONAL_DIAGNOSTICS !== '1') { await use(undefined); return; }
    const observer = createBrowserObservation(baseURL ?? 'http://127.0.0.1:5080');
    const stopObservation = observeContext(context, observer);
    const captureState = { failureCaptured: false };
    failureSnapshots.set(page, async () => {
      if (captureState.failureCaptured) { return; }
      captureState.failureCaptured = true;
      stopObservation();
      const projection = await captureProjection(page, 'failed-step');
      Object.assign(observer.data, projection);
    });
    await use(undefined);
    failureSnapshots.delete(page);
    stopObservation();
    if (!captureState.failureCaptured) {
      const projection = await captureProjection(page, 'after-test-cleanup');
      Object.assign(observer.data, projection);
    }
    if (['failed', 'timedOut', 'interrupted'].includes(testInfo.status ?? '')) {
      const snapshot = await captureSnapshot(browser, observer.data, testInfo);
      Object.assign(observer.data, snapshot);
    }
    if (process.env.COGLATAS_FUNCTIONAL_DIAGNOSTICS === '1' && ['failed', 'timedOut', 'interrupted'].includes(testInfo.status ?? '')) {
      // This object is allowlisted; never include assertion text, URLs, bodies or headers.
      console.info('Licensed Functional browser observation:', JSON.stringify(validateBrowserObservation(observer.data)));
    }
    await testInfo.attach(OBSERVATION_ATTACHMENT, { body: Buffer.from(JSON.stringify(validateBrowserObservation(observer.data))), contentType: 'application/json' });
  }, { auto: true }],
});
