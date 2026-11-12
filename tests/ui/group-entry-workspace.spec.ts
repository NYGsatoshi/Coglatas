import { expect, test } from '@playwright/test';
import { readGroupRestorationWorkspace } from './group-dm-real';

// Controlled DOM synchronization regressions; these are not actual-backend evidence.
test.describe('Group fixture Workspace restoration precondition', () => {
  test.describe.configure({ retries: 0 });
  const workspaceId = 'f922f81c-a79e-4900-8d56-2c09b7efa5cb';

  test('waits for the allowed loading projection before capturing the selected scope', async ({ page }) => {
    await page.setContent(`<label for="scope">Workspace</label>
      <select id="scope" data-testid="workspace-switcher" disabled><option value="">Select Workspace</option></select>
      <button id="restore">Restore authorized projection</button>
      <script>document.querySelector('#restore').onclick=()=>{
        const select=document.querySelector('#scope');
        select.add(new Option('Owned synthetic Workspace','${workspaceId}'));
        select.disabled=false;select.value='${workspaceId}';
      };</script>`);
    let completed = false;
    const result = readGroupRestorationWorkspace(page).then(
      value => { completed = true; return { value }; },
      error => { completed = true; return { error }; },
    );
    await expect(page.getByTestId('workspace-switcher')).toHaveValue('');
    expect(completed, 'no restoration intent may be captured during loading').toBe(false);
    await page.getByRole('button', { name: 'Restore authorized projection' }).click();
    expect(await result).toEqual({ value: workspaceId });
  });

  test('rejects a disabled stale GUID instead of treating it as authorized readiness', async ({ page }) => {
    await page.setContent(`<select data-testid="workspace-switcher" disabled>
      <option value="${workspaceId}" selected>Stale synthetic scope</option></select>`);
    await expect(readGroupRestorationWorkspace(page)).rejects.toThrow();
    await expect(page.getByTestId('workspace-switcher')).toBeDisabled();
  });

  test('rejects a completed selection-required state without inventing a scope', async ({ page }) => {
    await page.setContent('<select data-testid="workspace-switcher"><option value="">Select Workspace</option></select>');
    await expect(readGroupRestorationWorkspace(page)).rejects.toThrow();
    await expect(page.getByTestId('workspace-switcher')).toHaveValue('');
  });
});
