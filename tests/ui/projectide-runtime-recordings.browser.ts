import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';

test('looks up a server-issued Compiler recording through the development Runtime', async ({ page }) => {
  const { email, password, projectId, runId, receiptDigest } = fixtureConfiguration();

  await page.goto('/app/login');
  await expect(page.getByTestId('login-page')).toBeVisible();
  await page.getByTestId('login-email').fill(email);
  await page.getByTestId('login-password').fill(password);
  const [login] = await Promise.all([
    page.waitForResponse(response =>
      response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/auth/login'),
    page.getByTestId('login-submit').click(),
  ]);
  expect(login.status(), 'actual development host login').toBe(200);
  await expect(page).toHaveURL(/\/app\/workspaces$/);

  await page.goto(`/app/projects/${projectId}`);
  await page.getByRole('link', { name: 'ProjectIDE development Runtime' }).click();
  await expect(page).toHaveURL(url =>
    url.pathname === '/app/projectide/preview' && url.searchParams.get('runtimeProjectId') === projectId);
  await expect(page.getByRole('heading', { name: 'ProjectIDE development Runtime', level: 1 })).toBeVisible();

  const fulfilled = await lookUp(page, projectId, runId);
  expect(fulfilled.status()).toBe(200);
  expect(fulfilled.headers()['cache-control']).toContain('no-store');
  await expect(page.getByRole('status').filter({ hasText: 'Compiler recording loaded.' })).toBeVisible();
  await expect(page.locator('code').filter({ hasText: receiptDigest })).toBeVisible();

  const unavailable = await lookUp(page, projectId, randomUUID());
  expect(unavailable.status()).toBe(404);
  expect(unavailable.headers()['cache-control']).toContain('no-store');
  expect((await unavailable.body()).length).toBe(0);
  await expect(page.getByRole('status').filter({ hasText: 'The Compiler recording is unavailable.' })).toBeVisible();
  await expect(page.locator('code').filter({ hasText: receiptDigest })).toHaveCount(0);
});

async function lookUp(page: Page, projectId: string, runId: string) {
  await page.getByRole('textbox', { name: 'Compiler run ID' }).fill(runId);
  const path = `/api/projectide/runtime/projects/${projectId}/compiler-recordings/${runId}`;
  const [response] = await Promise.all([
    page.waitForResponse(candidate => candidate.request().method() === 'GET' && new URL(candidate.url()).pathname === path),
    page.getByRole('button', { name: 'Look up recording' }).click(),
  ]);
  return response;
}

function fixtureConfiguration() {
  const baseUrl = process.env.PLAYWRIGHT_BASE_URL ?? '';
  const email = process.env.COGLATAS_BROWSER_SMOKE_EMAIL ?? '';
  const password = process.env.COGLATAS_BROWSER_SMOKE_PASSWORD ?? '';
  const projectId = (process.env.COGLATAS_PROJECTIDE_RUNTIME_PROJECT_ID ?? '').toLowerCase();
  const runId = (process.env.COGLATAS_PROJECTIDE_RUNTIME_RUN_ID ?? '').toLowerCase();
  const receiptDigest = process.env.COGLATAS_PROJECTIDE_RUNTIME_RECEIPT_DIGEST ?? '';
  const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/;

  if (!baseUrl || !email.endsWith('@example.test') || !password ||
      !uuid.test(projectId) || !uuid.test(runId) || !/^[0-9a-f]{64}$/.test(receiptDigest)) {
    throw new Error('Set the actual development host URL and synthetic account, project, run and receipt fixture identifiers.');
  }

  const origin = new URL(baseUrl);
  if (origin.port === '4173' || origin.username || origin.password) {
    throw new Error('The Runtime browser qualification requires the development application origin.');
  }

  return { email, password, projectId, runId, receiptDigest };
}
