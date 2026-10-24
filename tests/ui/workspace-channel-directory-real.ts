import { randomUUID } from 'node:crypto';
import { expect, type Page } from '@playwright/test';

/**
 * Runs only inside the guarded real ASP.NET Core/PostgreSQL browser smoke.
 * No API interception, mock response or synthetic client-side participant is used.
 */
export async function exerciseNamedWorkspaceChannel(
  page: Page, workspaceId: string, settleAuthorization: () => Promise<void>
): Promise<{ readonly channelId: string; readonly name: string; readonly persisted: boolean }> {
  const directoryEndpoint = `/api/workspaces/${workspaceId}/chat-channels`;
  const name = `Synthetic council channel ${randomUUID().slice(0, 8)}`;

  await page.goto('/app/teams');
  const directory = page.getByTestId('workspace-channel-directory');
  await expect(directory).toBeVisible();
  await expect(page.getByTestId('workspace-switcher')).toHaveValue(workspaceId);
  await expect(directory).toContainText('general');
  // Workspace creation can invalidate SignalR authorization after the
  // directory's first HTTP read. Wait for its authoritative realtime catch-up
  // before opening a local-only dialog that may be cleared by that boundary.
  // Do not retry the mutation, suppress a denial, or substitute a mock response.
  await expect(page.getByTestId('realtime-connection-state')).toContainText(
    'Realtime updates connected.', { timeout: 30_000 }
  );
  const createAction = directory.getByTestId('channel-create-open');
  await expect(createAction).toBeEnabled();
  await createAction.click();

  const dialog = page.getByTestId('channel-create-dialog');
  await expect(dialog).toBeVisible();
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
