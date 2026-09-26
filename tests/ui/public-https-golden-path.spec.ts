import { createHash, randomUUID } from 'node:crypto';
import { expect, request, test, type BrowserContext, type Page } from '@playwright/test';

import {
  isUuid,
  readPublicHttpsSmokeConfiguration
} from './public-https-golden-path-helpers.mjs';

const configuration = readPublicHttpsSmokeConfiguration(process.env);
const safeFailureMarkers = [
  'exception',
  'stack trace',
  'stacktrace',
  'system.',
  'connectionstring',
  'postgresql',
  'password=',
  'set-cookie:'
];

test.describe('Public HTTPS production Golden Path', () => {
  test.setTimeout(180_000);

  test('uses the public proxy route for the durable Task result and security boundaries', async ({ page, context }) => {
    await assertPublicHttpRedirect(configuration.baseURL);

    const loginPageResponse = await navigate(page, '/app/login');
    await expect(page.getByTestId('login-page')).toBeVisible();
    expect(isHttpsUrl(page.url())).toBe(true);
    expect(hasHsts(loginPageResponse?.headers() ?? {})).toBe(true);
    assertBrowserSecurityHeaders(loginPageResponse?.headers() ?? {});

    await assertInvalidLoginIsDenied(page);
    await loginThroughBrowser(page);
    assertSecureSessionCookies(await context.cookies());

    await assertAuthorizedPath(page, `/api/workspaces/${configuration.workspaceId}`);
    await navigate(page, `/app/workspaces/${configuration.workspaceId}/projects`);
    await expect(page.getByTestId('app-shell')).toBeVisible();

    await assertAuthorizedPath(page, `/api/projects/${configuration.projectId}`);
    await navigate(page, `/app/projects/${configuration.projectId}`);
    await expect(page.getByTestId('project-detail-page')).toBeVisible();

    await assertAuthorizedPath(page, `/api/tasks/${configuration.taskId}`);
    await navigate(page, `/app/projects/${configuration.projectId}/tasks/${configuration.taskId}`);
    await expect(page.getByTestId('task-detail-page')).toBeVisible();

    await assertMissingCsrfIsDenied(page);
    await assertMalformedRequestIsRedacted(page);
    await assertCurrentAuthorizationDenials(page);

    const runId = await requestExecutionFromTaskDetail(page);
    const firstResult = await waitForDurableResult(page, runId);
    assertDurableProjectFileResult(firstResult, runId);
    const firstFingerprint = fingerprint(firstResult);

    await expect(page.getByTestId('task-execution-result-status')).toBeVisible();
    await expect(page.getByTestId('task-execution-report')).toBeVisible();

    await reload(page);
    await expect(page.getByTestId('task-detail-page')).toBeVisible();
    const afterReload = await getDurableResult(page, runId);
    assertDurableProjectFileResult(afterReload, runId);
    expect(fingerprint(afterReload) === firstFingerprint).toBe(true);

    const logout = await csrfRequest(page, 'POST', '/api/auth/logout');
    expect(logout.status === 200).toBe(true);

    await assertProtectedResultDenied(page, runId);
    await context.clearCookies();
    await assertProtectedResultDenied(page, runId);

    await loginThroughBrowser(page);
    const afterRelogin = await getDurableResult(page, runId);
    assertDurableProjectFileResult(afterRelogin, runId);
    expect(fingerprint(afterRelogin) === firstFingerprint).toBe(true);
  });
});

async function assertPublicHttpRedirect(baseURL: string): Promise<void> {
  const httpURL = new URL(baseURL);
  httpURL.protocol = 'http:';
  const api = await request.newContext();

  try {
    const response = await api.get(httpURL.toString(), { maxRedirects: 0 });
    const location = response.headers()['location'];
    const redirectsToHttps =
      [301, 302, 307, 308].includes(response.status()) &&
      typeof location === 'string' &&
      isHttpsUrl(new URL(location, httpURL).toString());

    expect(redirectsToHttps).toBe(true);
  } finally {
    await api.dispose();
  }
}

async function assertInvalidLoginIsDenied(page: Page): Promise<void> {
  const result = await csrfRequest(page, 'POST', '/api/auth/login', {
    email: `invalid-public-gate-${randomUUID()}@example.test`,
    password: 'not-a-valid-password'
  });

  expect([400, 401, 403].includes(result.status)).toBe(true);
  expect(isRedactedFailure(result.text, [])).toBe(true);
}

async function loginThroughBrowser(page: Page): Promise<void> {
  await navigate(page, '/app/login');
  await expect(page.getByTestId('login-page')).toBeVisible();

  const loginResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/auth/login'
  );

  await page.getByTestId('login-email').fill(configuration.email);
  await page.getByTestId('login-password').fill(configuration.password);
  await page.getByTestId('login-submit').click();

  const loginResponse = await loginResponsePromise;
  expect(loginResponse.status() === 200).toBe(true);
  await expect(page).toHaveURL(/\/app\/workspaces$/);
  await expect(page.getByTestId('app-shell')).toBeVisible();
}

function assertSecureSessionCookies(cookies: Awaited<ReturnType<BrowserContext['cookies']>>): void {
  const secureAuthCookie = cookies.some((cookie) =>
    cookie.name === '.Coglatas.Auth' &&
    cookie.secure &&
    cookie.httpOnly &&
    cookie.sameSite === 'Lax'
  );
  const secureCsrfCookie = cookies.some((cookie) =>
    cookie.name === '.Coglatas.Csrf' &&
    cookie.secure &&
    cookie.httpOnly &&
    cookie.sameSite === 'Lax'
  );

  expect(secureAuthCookie).toBe(true);
  expect(secureCsrfCookie).toBe(true);
}

async function assertAuthorizedPath(page: Page, path: string): Promise<void> {
  const result = await browserFetch(page, path);
  expect(result.status === 200).toBe(true);
  expect(result.headers['cache-control']).toContain('no-store');
}

async function assertMissingCsrfIsDenied(page: Page): Promise<void> {
  const result = await browserFetch(page, `/api/tasks/${configuration.taskId}/execution-runs`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': `public-https-no-csrf-${randomUUID()}`
    },
    body: '{}'
  });

  assertDeniedAndRedacted(result, [configuration.taskId]);
}

async function assertMalformedRequestIsRedacted(page: Page): Promise<void> {
  const result = await csrfRequest(
    page,
    'POST',
    `/api/tasks/${configuration.taskId}/execution-runs`,
    '{',
    {
      'Content-Type': 'application/json',
      'Idempotency-Key': `public-https-malformed-${randomUUID()}`
    },
    true
  );

  expect([400, 415].includes(result.status)).toBe(true);
  expect(isRedactedFailure(result.text, [configuration.taskId])).toBe(true);
}

async function assertCurrentAuthorizationDenials(page: Page): Promise<void> {
  const deniedPaths = [
    [`/api/workspaces/${configuration.unauthorizedWorkspaceId}`, configuration.unauthorizedWorkspaceId],
    [`/api/projects/${configuration.unauthorizedProjectId}`, configuration.unauthorizedProjectId],
    [`/api/tasks/${configuration.unauthorizedTaskId}`, configuration.unauthorizedTaskId],
    [`/api/files/${configuration.revokedFileId}`, configuration.revokedFileId]
  ] as const;

  for (const [path, protectedId] of deniedPaths) {
    assertDeniedAndRedacted(await browserFetch(page, path), [protectedId]);
  }
}

async function requestExecutionFromTaskDetail(page: Page): Promise<string> {
  const startButton = page.getByTestId('task-execution-start');
  await expect(startButton).toBeVisible();
  await expect(startButton).toBeEnabled();

  const startResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST' &&
    new URL(response.url()).pathname === `/api/tasks/${configuration.taskId}/execution-runs`
  );
  await startButton.click();

  const startResponse = await startResponsePromise;
  expect(startResponse.status() === 201).toBe(true);
  const payload = asRecord(parseJson(await startResponse.text()));
  const runId = typeof payload?.id === 'string' ? payload.id : '';
  expect(isUuid(runId)).toBe(true);

  return runId;
}

async function waitForDurableResult(page: Page, runId: string): Promise<unknown> {
  const deadline = Date.now() + 90_000;

  while (Date.now() < deadline) {
    const result = await browserFetch(
      page,
      `/api/tasks/${configuration.taskId}/execution-runs/${runId}/result`
    );
    if (result.status === 200) {
      return parseJson(result.text);
    }

    await delay(750);
  }

  throw new Error('The durable Task result was not available through the public HTTPS endpoint before the gate deadline.');
}

async function getDurableResult(page: Page, runId: string): Promise<unknown> {
  const result = await browserFetch(page, `/api/tasks/${configuration.taskId}/execution-runs/${runId}/result`);
  expect(result.status === 200).toBe(true);
  return parseJson(result.text);
}

function assertDurableProjectFileResult(value: unknown, expectedRunId: string): void {
  const result = asRecord(value);
  const report = asRecord(result?.report);
  const body = typeof report?.bodyMarkdown === 'string' ? report.bodyMarkdown : '';
  const valid =
    result?.runId === expectedRunId &&
    result?.status === 'Succeeded' &&
    report?.title === 'Project Files Analysis Report' &&
    /Authorized sources consumed:\s*[1-9]\d*/iu.test(body);

  expect(valid).toBe(true);
}

async function assertProtectedResultDenied(page: Page, runId: string): Promise<void> {
  const result = await browserFetch(page, `/api/tasks/${configuration.taskId}/execution-runs/${runId}/result`);
  expect(result.status === 401).toBe(true);
  expect(isRedactedFailure(result.text, [configuration.taskId, runId])).toBe(true);
}

function assertDeniedAndRedacted(result: BrowserFetchResult, protectedValues: readonly string[]): void {
  expect([400, 403, 404].includes(result.status)).toBe(true);
  expect(isRedactedFailure(result.text, protectedValues)).toBe(true);
}

function isRedactedFailure(text: string, protectedValues: readonly string[]): boolean {
  const lower = text.toLowerCase();
  return (
    safeFailureMarkers.every((marker) => !lower.includes(marker)) &&
    protectedValues.every((value) => !lower.includes(value.toLowerCase()))
  );
}

async function browserFetch(
  page: Page,
  path: string,
  init: { method?: string; headers?: Record<string, string>; body?: string } = {}
): Promise<BrowserFetchResult> {
  return page.evaluate(async ({ path, init }) => {
    const headers: Record<string, string> = {},
      response = await fetch(path, { credentials: 'include', ...init });
    response.headers.forEach((value, key) => {
      headers[key] = value;
    });
    return { status: response.status, text: await response.text(), headers };
  }, { path, init });
}

async function csrfRequest(
  page: Page,
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE',
  path: string,
  body?: unknown,
  additionalHeaders: Readonly<Record<string, string>> = {},
  rawBody = false
): Promise<BrowserFetchResult> {
  return page.evaluate(async ({ method, path, body, additionalHeaders, rawBody }) => {
    const csrfResponse = await fetch('/api/security/csrf-token', { credentials: 'include' });
    const csrf = await csrfResponse.json() as { token?: string; headerName?: string };
    const headers: Record<string, string> = { ...additionalHeaders };
    if (csrf.token && csrf.headerName) {
      headers[csrf.headerName] = csrf.token;
    }
    const response = await fetch(path, {
        method,
        credentials: 'include',
        headers,
        ...(body === undefined ? {} : { body: rawBody ? String(body) : JSON.stringify(body) })
      }),
      responseHeaders: Record<string, string> = {};
    response.headers.forEach((value, key) => {
      responseHeaders[key] = value;
    });
    return { status: response.status, text: await response.text(), headers: responseHeaders };
  }, { method, path, body, additionalHeaders, rawBody });
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function fingerprint(value: unknown): string {
  return createHash('sha256').update(JSON.stringify(value)).digest('hex');
}

function hasHsts(headers: Record<string, string>): boolean {
  const value = headers['strict-transport-security'] ?? '';
  return /max-age=\d+/iu.test(value);
}

// eslint-disable-next-line func-style -- Keep the new SEC-13 assertion helper hoisted with the existing Playwright helpers.
function assertBrowserSecurityHeaders(headers: Readonly<Record<string, string>>): void {
  expect(headers['x-content-type-options']).toBe('nosniff');
  expect(headers['x-frame-options']).toBe('DENY');
  expect(headers['referrer-policy']).toBe('strict-origin-when-cross-origin');
  expect(['camera=()', 'microphone=()'].every((directive) =>
    headers['permissions-policy'].includes(directive)
  )).toBe(true);

  const csp = headers['content-security-policy'] ?? '';
  expect(["default-src 'self'", "script-src 'self'", "frame-ancestors 'none'"].every((directive) =>
    csp.includes(directive)
  )).toBe(true);
  expect(csp).not.toContain("script-src 'self' 'unsafe-inline'");
  expect(csp).not.toContain("'unsafe-eval'");
  expect(csp).not.toContain('*');
}

function isHttpsUrl(value: string): boolean {
  try {
    return new URL(value).protocol === 'https:';
  } catch {
    return false;
  }
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

async function navigate(page: Page, path: string) {
  try {
    return await page.goto(path);
  } catch {
    // Do not let Playwright include a fixture UUID in a failed navigation error.
    throw new Error('Browser navigation through a required public application route failed.');
  }
}

async function reload(page: Page) {
  try {
    return await page.reload();
  } catch {
    // The Task URL includes fixture identifiers and must not be reflected in CI.
    throw new Error('Browser reload through the public application route failed.');
  }
}

interface BrowserFetchResult {
  status: number;
  text: string;
  headers: Record<string, string>;
}
