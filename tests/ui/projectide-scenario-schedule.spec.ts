import { expect, test, type Page } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

async function openSchedule(page: Page, locale: 'en' | 'ja') {
  await page.addInitScript(language => {
    localStorage.setItem('coglatas.locale', language);
    window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
  }, locale);
  await page.goto('/app/projectide/preview');
  await page.getByRole('button', { name: locale === 'en' ? 'Open Scenario schedule example' : 'シナリオのスケジュール例を開く', exact: true }).click();
}

for (const locale of ['en', 'ja'] as const) {
  const pick = async (page: Page, key: string) => {
    await page.getByTestId('ide-context').selectOption(key);
    await page.getByRole('button', { name: locale === 'en' ? 'Load result fixture' : '結果Fixtureを読み込む', exact: true }).click();
    await page.getByRole('button', { name: locale === 'en' ? 'Scenario' : 'シナリオ', exact: true }).click();
    const comparison = page.getByRole('button', { name: locale === 'en' ? 'Load Scenario comparison' : 'シナリオの比較を読み込む', exact: true });
    await comparison.focus();
    await comparison.press('Enter');
  };

  test('shows schedule evidence beside the existing Scenario layers and withdraws it at Draft transition in ' + locale, async ({ page }) => {
    await openSchedule(page, locale);
    await pick(page, 'tests-simulation-root-base');
    const schedule = page.getByTestId('ide-scenario-schedule');
    await expect(page.getByTestId('ide-scenario-assumptions')).toContainText('TaskDuration');
    await expect(page.getByTestId('ide-scenario-changes')).toContainText('/payload/duration');
    await expect(page.getByTestId('ide-scenario-results')).toBeVisible();
    await expect(page.getByTestId('ide-scenario-schedule-delta')).toHaveText('2');
    const baseline = page.getByTestId('ide-scenario-schedule-stage-0');
    await expect(baseline.getByRole('row').filter({ hasText: '00000007-' }).locator('td').last()).toHaveText('5');
    await expectNoAccessibilityViolations(page, 'app-projectide-workbench');
    await page.getByRole('button', { name: locale === 'en' ? 'Load Scenario snapshot' : 'シナリオのスナップショットを読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-context')).toHaveValue('tests-simulation-root-hypothesis');
    await expect(schedule).toHaveCount(0);
    await page.getByRole('button', { name: locale === 'en' ? 'Load prepared Draft' : '準備済みDraftを読み込む', exact: true }).click();
    await expect(page.getByTestId('ide-context')).toHaveValue('tests-simulation-root-draft');
    await expect(schedule).toHaveCount(0);
  });

  test('shows incomplete schedule evidence without a zero delta in ' + locale, async ({ page }) => {
    await openSchedule(page, locale);
    await pick(page, 'tests-simulation-bounded-base');
    const schedule = page.getByTestId('ide-scenario-schedule');
    await expect(schedule.getByRole('status')).toContainText(locale === 'en' ? 'A recording is incomplete' : '実行記録が不完全');
    await expect(schedule.getByTestId('ide-scenario-schedule-delta')).toHaveCount(0);
    await expect(schedule.getByRole('table')).toHaveCount(0);
  });
}
