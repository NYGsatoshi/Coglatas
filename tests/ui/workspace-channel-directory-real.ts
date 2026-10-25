import { randomUUID } from 'node:crypto';
import { expect, type Page, type TestInfo } from '@playwright/test';

/**
 * Browser-only, synthetic-P0, bounded diagnostic. Exports no channel names,
 * user/workspace IDs, URLs, input text, event data or credentials.
 */
export function installChannelDialogObservation(): void {
  type Kind = 'initial' | 'structure' | 'click';
  type ObservationEvent = { readonly atMs: number; readonly kind: Kind;
    readonly directoryPresent: boolean; readonly createPresent: boolean;
    readonly createEnabled: boolean; readonly dialogPresent: boolean };
  const limit = 48;
  const events: ObservationEvent[] = [];
  let truncated = 0;
  let previous: string | undefined;
  const origin = performance.now();
  const record = (kind: Kind): void => {
    const directory = document.querySelector('[data-testid="workspace-channel-directory"]');
    const create = document.querySelector<HTMLButtonElement>('[data-testid="channel-create-open"]');
    const dialog = document.querySelector('[data-testid="channel-create-dialog"]');
    const snapshot = { directoryPresent: !!directory, createPresent: !!create,
      createEnabled: !!create && !create.disabled, dialogPresent: !!dialog };
    const signature = JSON.stringify(snapshot);
    if (kind === 'structure' && signature === previous) return;
    previous = signature;
    events.push({ atMs: Math.max(0, Math.floor(performance.now() - origin)), kind, ...snapshot });
    if (events.length > limit) { events.shift(); truncated = Math.min(truncated + 1, 1_000_000); }
  };
  document.addEventListener('click', (event) => {
    if (event.target instanceof Element &&
        event.target.closest('[data-testid="channel-create-open"]')) record('click');
  }, true);
  const start = (): void => {
    record('initial');
    const root = document.documentElement;
    if (root) {
      new MutationObserver(() => record('structure'))
        .observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
    }
  };
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start, { once: true });
  } else { start(); }
  Object.defineProperty(window, '__coglatasChannelDialogObservationV1', {
    value: () => ({ schemaVersion: 1, events: [...events], eventsTruncated: truncated }),
    configurable: false
  });
}


/** Reject any unrecognized field or private value before publishing diagnostics. */
export function serializeChannelDialogObservation(raw: unknown): string | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const source = raw as Record<string, unknown>;
  const exactKeys = (value: Record<string, unknown>, keys: readonly string[]): boolean => {
    const actual = Object.keys(value).sort();
    const expected = [...keys].sort();
    return actual.length === expected.length && actual.every((name, index) => name === expected[index]);
  };
  if (!exactKeys(source, ['schemaVersion', 'events', 'eventsTruncated']) ||
      source.schemaVersion !== 1 || !Array.isArray(source.events) ||
      source.events.length < 1 || source.events.length > 48 ||
      typeof source.eventsTruncated !== 'number' ||
      !Number.isSafeInteger(source.eventsTruncated) ||
      source.eventsTruncated < 0 || source.eventsTruncated > 1_000_000) return null;

  const normalized = [];
  for (const item of source.events) {
    if (!item || typeof item !== 'object' || Array.isArray(item)) return null;
    const event = item as Record<string, unknown>;
    if (!exactKeys(event, ['atMs', 'kind', 'directoryPresent', 'createPresent',
      'createEnabled', 'dialogPresent']) ||
        typeof event.atMs !== 'number' || !Number.isSafeInteger(event.atMs) ||
        event.atMs < 0 || event.atMs > 300_000 ||
        typeof event.kind !== 'string' ||
        !['initial', 'structure', 'click'].includes(event.kind) ||
        ['directoryPresent', 'createPresent', 'createEnabled', 'dialogPresent']
          .some((key) => typeof event[key] !== 'boolean')) return null;
    normalized.push({
      atMs: event.atMs, kind: event.kind, directoryPresent: event.directoryPresent,
      createPresent: event.createPresent, createEnabled: event.createEnabled,
      dialogPresent: event.dialogPresent
    });
  }
  return JSON.stringify({ schemaVersion: 1, events: normalized,
    eventsTruncated: source.eventsTruncated });
}

/** Optional diagnostic must never change the assertion's outcome. */
async function attachChannelDialogObservation(page: Page, testInfo: TestInfo | undefined): Promise<void> {
  if (!testInfo) return;
  try {
    const snapshot = await page.evaluate(() => {
      const read = (window as Window & {
        __coglatasChannelDialogObservationV1?: () => unknown
      }).__coglatasChannelDialogObservationV1;
      return typeof read === 'function' ? read() : null;
    });
    const safe = serializeChannelDialogObservation(snapshot);
    if (safe !== null) {
      await testInfo.attach('channel-dialog-structural-v1.json', {
        body: safe, contentType: 'application/json'
      });
    }
  } catch {
    // Never convert a legitimate browser assertion failure into a diagnostic error.
  }
}

/**
 * Runs only inside the guarded real ASP.NET Core/PostgreSQL browser smoke.
 * No API interception, mock response or synthetic client-side participant is used.
 */
export async function exerciseNamedWorkspaceChannel(
  page: Page, workspaceId: string, settleAuthorization: () => Promise<void>,
  diagnosticTestInfo?: TestInfo
): Promise<{ readonly channelId: string; readonly name: string; readonly persisted: boolean }> {
  const directoryEndpoint = `/api/workspaces/${workspaceId}/chat-channels`;
  const name = `Synthetic council channel ${randomUUID().slice(0, 8)}`;

  // Register before navigation; never delay the guarded create click to install a probe.
  if (diagnosticTestInfo) await page.addInitScript(installChannelDialogObservation);
  await page.goto('/app/teams');
  const directory = page.getByTestId('workspace-channel-directory');
  await expect(directory).toBeVisible();
  await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
  await expect(directory).toContainText('general');
  const createAction = directory.getByTestId('channel-create-open');
  await expect(createAction).toBeEnabled();
  await createAction.click();

  const dialog = page.getByTestId('channel-create-dialog');
  try {
    await expect(dialog).toBeVisible();
  } finally {
    // Capture the failing first attempt before outer test cleanup; also retain a
    // bounded positive control for genuine first-attempt successes.
    await attachChannelDialogObservation(page, diagnosticTestInfo);
  }
  await expect(dialog).toHaveAttribute('role', 'dialog');
  await dialog.getByTestId('channel-create-name').fill(name);
  await dialog.getByTestId('channel-create-visibility').selectOption('Private');

  const [created] = await Promise.all([
    page.waitForResponse((response) =>
      new URL(response.url()).pathname === directoryEndpoint && response.request().method() === 'POST'),
    dialog.getByTestId('channel-create-submit').click()
  ]);
  expect(created.status(), 'real Workspace channel creation').toBe(200);
  const commandHeaders = await created.request().allHeaders();
  expect(commandHeaders['x-csrf-token'], 'real Angular CSRF interceptor').toBeTruthy();
  expect(commandHeaders['idempotency-key'], 'idempotent Workspace channel create key')
    .toMatch(/^[0-9a-f-]{36}$/iu);
  expect(created.request().postDataJSON()).toEqual({ name, visibility: 'Private' });

  const payload = await created.json() as Record<string, unknown>;
  const channelId = payload['id'];
  expect(channelId).toMatch(/^[0-9a-f-]{36}$/iu);
  expect(payload).toMatchObject({
    workspaceId, name, visibility: 'Private',
    isMember: true, canJoin: false, canManage: true, isDefault: false
  });

  await expect(dialog).toHaveCount(0);
  await expect(directory.getByTestId('channel-confirmed-open'))
    .toHaveAttribute('href', `/app/workspaces/${workspaceId}/channels/${channelId}`);

  await settleAuthorization();
  await page.reload();
  const restored = page.getByTestId('workspace-channel-directory');
  await expect(restored).toBeVisible();
  const row = restored.locator(`[data-channel-id="${channelId}"]`);
  await expect(row).toContainText(name);
  await expect(row.getByTestId('channel-directory-open'))
    .toHaveAttribute('href', `/app/workspaces/${workspaceId}/channels/${channelId}`);

  await page.goto(`/app/workspaces/${workspaceId}/channels/${channelId}`);
  await expect(page.getByTestId('channel-messaging-page')).toBeVisible();
  return { channelId: String(channelId), name, persisted: true };
}
