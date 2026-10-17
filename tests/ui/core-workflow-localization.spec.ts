import { expect, type Page, test } from '@playwright/test';

import { expectNoAccessibilityViolations } from './a11y';

import { conversationId, conversationTitle, draft, fileName, installLocalizedWorkflowApi, projectTitle, taskTitle } from './fixtures/core-workflow-api';

test('changes language through preferences across core workflows without translating supplied content or sending a draft', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: testInfo.project.name === 'chromium-mobile' ? 320 : 1366, height: 844 });
  const errors: string[] = [];
  const mutations: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname;
    if (path.startsWith('/api/') && !['GET', 'HEAD', 'OPTIONS'].includes(request.method()) && !path.endsWith('/read')) {
      mutations.push(`${request.method()} ${path}`);
    }
  });
  await installLocalizedWorkflowApi(page);
  await page.goto('/app/account');

  for (const locale of ['ja', 'en'] as const) {
    await navigate(page, 'account', '/app/account');
    await page.getByTestId('display-language').selectOption(locale);
    await expect(page.locator('html')).toHaveAttribute('lang', locale);
    await expect(page.getByTestId('language-preferences')).toContainText(locale === 'ja' ? '表示言語' : 'Display language');
    expect(await page.evaluate(() => localStorage.getItem('coglatas.locale'))).toBe(locale);

    await navigate(page, 'projects', '/app/projects');
    await expect(page.getByRole('heading', { name: locale === 'ja' ? 'プロジェクト' : 'Projects', exact: true })).toBeVisible();
    await expect(page.getByText(projectTitle, { exact: true }).first()).toBeVisible();
    await expectContained(page);

    await navigate(page, 'my-tasks', '/app/tasks');
    await expect(page.getByRole('heading', { name: locale === 'ja' ? 'マイタスク' : 'My tasks', exact: true })).toBeVisible();
    await expect(page.getByTestId('my-tasks-search')).toHaveAccessibleName(locale === 'ja' ? '検索' : 'Search');
    await expect(page.getByText(taskTitle, { exact: true }).first()).toBeVisible();
    await expect(page.getByTestId('my-tasks-results')).toContainText('Configured stage ユーザー');
    await expectContained(page);

    await navigate(page, 'messages', '/app/messages');
    const conversation = page.getByTestId('conversation-list-item').filter({ hasText: conversationTitle });
    await conversation.click();
    await expect(page).toHaveURL(new RegExp(`/app/dm/${conversationId}$`));
    await expect(page.getByTestId('send-message')).toHaveAccessibleName(locale === 'ja' ? '送信' : 'Send');
    await expect(page.getByText('Supplied message 本文', { exact: true })).toBeVisible();
    const composer = page.getByTestId('message-draft');
    if (locale === 'ja') {
      await composer.fill(draft);
    } else {
      await expect(composer).toHaveValue(draft);
    }
    await expectContained(page);

    await navigate(page, 'files', '/app/files');
    await expect(page.getByRole('heading', { name: locale === 'ja' ? 'ファイル' : 'Files', exact: true })).toBeVisible();
    await expect(page.getByText(fileName, { exact: true }).filter({ visible: true }).first()).toBeVisible();
    await expectContained(page);
    await expectNoAccessibilityViolations(page, 'main');
  }

  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  expect(await page.evaluate(() => localStorage.getItem('coglatas.locale'))).toBe('en');
  expect(mutations).toEqual([]);
  expect(errors).toEqual([]);
});

async function navigate(page: Page, id: string, path: string): Promise<void> {
  const toggle = page.getByTestId('mobile-nav-toggle');
  if ((page.viewportSize()?.width ?? 1366) < 768) {
    await expect(toggle).toBeVisible();
    const navigation = page.getByTestId('mobile-navigation');
    if (await navigation.getAttribute('aria-hidden') === 'true') {
      await toggle.click();
    }
    await navigation.getByTestId(`nav-${id}`).click();
  } else {
    await page.getByTestId(`nav-${id}`).filter({ visible: true }).first().click();
  }
  await expect(page).toHaveURL(new RegExp(`${path}$`));
}

async function expectContained(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => ({
    document: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    main: (() => {
      const main = document.getElementById('app-shell-main-content');
      return main ? main.scrollWidth - main.clientWidth : 0;
    })()
  }));
  expect(overflow.document).toBeLessThanOrEqual(1);
  expect(overflow.main).toBeLessThanOrEqual(1);
}
