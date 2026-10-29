import { expect, type Page, test, type TestInfo } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';
import { attachPenpotEvidence } from './penpot-attachment.mjs';
import {
  channelId, channelTitle, conversationId, conversationTitle, fileName,
  installLocalizedWorkflowApi, projectId, projectTitle, reportVersionId, taskId, taskTitle, workspaceId,
} from './fixtures/core-workflow-api';

const viewports = [
  { width: 1536, height: 1024 },
  { width: 1366, height: 768 },
  { width: 768, height: 1024 },
  { width: 390, height: 844 },
];

test.describe('Penpot revision 339 supported Web projections', () => {
  test.describe.configure({ timeout: 90_000 });
  for (const viewport of viewports) {
    test(`contains supported workflows in both themes at ${viewport.width}x${viewport.height}`, async ({ page }, testInfo) => {
      await page.setViewportSize(viewport);
      const errors: string[] = [];
      const commands: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      page.on('request', (request) => {
        const pathname = new URL(request.url()).pathname;
        if (pathname.startsWith('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && !pathname.endsWith('/read')) {
          commands.push(`${request.method()} ${pathname}`);
        }
      });
      await installLocalizedWorkflowApi(page);
      await page.addInitScript(() => localStorage.setItem('coglatas.locale', 'ja'));
      await page.goto('/app/login');
      for (const theme of ['dark', 'light'] as const) {
        await page.evaluate((selectedTheme) => localStorage.setItem('coglatas.ui.theme.v1', selectedTheme), theme);

        await page.goto('/app/login');
        await expect(page.locator('input[type="email"]')).toBeVisible();
        await expect(page.locator('html')).toHaveAttribute('data-coglatas-theme', theme);
        const themeToggle = page.getByTestId('theme-toggle');
        await expect(themeToggle).toHaveAccessibleName(theme === 'dark' ? 'ライト' : 'ダーク');
        await expect(themeToggle).toHaveAttribute('aria-description',
          theme === 'dark' ? 'ライトモードに切り替え' : 'ダークモードに切り替え');
        await capture(page, testInfo, `${theme}-login`);
        await expectNoAccessibilityViolations(page);

        await page.goto('/app/workspaces');
        await expect(page.getByTestId('workspace-dashboard')).toBeVisible();
        await capture(page, testInfo, `${theme}-workspaces`);

        await page.goto(`/app/projects/${projectId}`);
        await expect(page.getByRole('heading', { name: projectTitle, exact: true })).toBeVisible();
        await capture(page, testInfo, `${theme}-project-overview`);
        await page.getByRole('tab', { name: '一覧', exact: true }).click();
        await expect(page.getByText(taskTitle, { exact: true }).filter({ visible: true }).first()).toBeVisible();
        await expect(page.getByText('Configured stage ユーザー', { exact: true }).filter({ visible: true }).first()).toBeVisible();
        await capture(page, testInfo, `${theme}-task-list`);
        await page.getByRole('tab', { name: 'タスク', exact: true }).click();
        await expect(page.locator(`[data-kanban-card-id="${taskId}"]`).filter({ visible: true }).first()).toContainText(taskTitle);
        await capture(page, testInfo, `${theme}-board`);

        await page.goto('/app/tasks');
        await expect(page.getByTestId('my-tasks-results')).toContainText('Configured stage ユーザー');
        await capture(page, testInfo, `${theme}-my-tasks`);

        await page.goto(`/app/dm/${conversationId}`);
        await expect(page.getByRole('heading', { name: conversationTitle, exact: true })).toBeVisible();
        await expect(page.getByTestId('message-draft')).toBeVisible();
        await expect(page.getByTestId('message-body').filter({ hasText: 'Own supplied response' })).toBeVisible();
        await capture(page, testInfo, `${theme}-dm`);

        await page.goto(`/app/workspaces/${workspaceId}/channels/${channelId}`);
        await expect(page.getByRole('heading', { name: channelTitle, exact: true })).toBeVisible();
        await expect(page.getByTestId('message-draft')).toBeVisible();
        await capture(page, testInfo, `${theme}-channel`);
        await page.getByTestId('open-message-thread-localization-message').click();
        await expect(page.getByTestId('thread-root-message')).toContainText('Supplied message 本文');
        await expect(page.getByTestId('thread-replies')).toContainText('Supplied thread reply 返答');
        await capture(page, testInfo, `${theme}-thread`);

        await page.goto('/app/files');
        await expect(page.getByText(fileName, { exact: true }).filter({ visible: true }).first()).toBeVisible();
        await capture(page, testInfo, `${theme}-files`);
        await expectNoAccessibilityViolations(page, 'main');
        await page.getByText(fileName, { exact: true }).filter({ visible: true }).first().click();
        await expect(page.getByTestId('files-preview-pane')).toContainText(fileName);
        await capture(page, testInfo, `${theme}-file-inspector`);

        await page.goto(`/app/projects/${projectId}/reports/${reportVersionId}`);
        const reader = page.locator('app-report-reader-page');
        await expect(reader.getByRole('heading', { name: 'User-authored report 調査結果', exact: true })).toBeVisible();
        await expect(reader).toContainText('Supplied report prose 原文');
        await expect(reader.locator('.dialog-backdrop')).toBeHidden();
        await expect(reader.locator('.refine-action')).toBeHidden();
        await expect(reader.locator('#evidence-inspector')).toBeHidden();
        await expect(page.getByRole('main')).toHaveCount(1);
        await expect(reader.locator('main')).toHaveCount(0);
        await capture(page, testInfo, `${theme}-report`);
        await reader.getByRole('button', { name: /Supplied citation/ }).click();
        const evidence = reader.locator('#evidence-inspector');
        await expect(evidence).toBeVisible();
        await expect(evidence).toContainText('Supplied evidence passage 原文');
        await expect(evidence.locator('.claim-refine')).toBeHidden();
        await capture(page, testInfo, `${theme}-report-evidence`);
        await expectNoAccessibilityViolations(page, 'main');
        await evidence.locator('.close').click();
        await expect(evidence).toBeHidden();
        await expect(reader.getByRole('button', { name: /Supplied citation/ })).toBeFocused();
      }
      expect(errors).toEqual([]);
      expect(commands).toEqual([]);
    });
  }
});

async function capture(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await expect.poll(() => page.evaluate(() => document.fonts.status)).toBe('loaded');
  if (await page.getByTestId('app-shell').count() > 0 && (page.viewportSize()?.width ?? 0) > 860) {
    await expect(page.locator('app-feature-menu-icon svg').filter({ visible: true }).first()).toBeVisible();
  }
  const layout = await page.evaluate(() => {
    const main = document.getElementById('app-shell-main-content');
    const style = getComputedStyle(document.body);
    return {
      documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      mainOverflow: main ? main.scrollWidth - main.clientWidth : 0,
      theme: document.documentElement.dataset['coglatasTheme'],
      fontFamily: style.fontFamily,
      fontSize: style.fontSize,
      backgroundColor: style.backgroundColor,
      color: style.color,
      main: main ? { x: main.getBoundingClientRect().x, width: main.getBoundingClientRect().width } : null,
    };
  });
  expect(layout.documentOverflow, name).toBeLessThanOrEqual(1);
  expect(layout.mainOverflow, name).toBeLessThanOrEqual(1);
  const fileName = `${testInfo.project.name}-${page.viewportSize()?.width}-${name}`;
  await attachPenpotEvidence(page, testInfo, fileName, layout);
}
