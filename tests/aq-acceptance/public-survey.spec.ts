import { expect, test, type Response } from '@playwright/test';

const publicApi = '/api/v1/public/surveys/';
const submitText = '送信';
const successText = '視聴者向けフォームの回答を受け付けました';

function requiredFixtureSetting(key: string): string {
  const value = process.env[key];
  if (!value) {
    throw new Error(`Missing AQ synthetic fixture setting ${key}.`);
  }
  return value;
}

function matchesResponse(response: Response, method: string, path: string): boolean {
  return response.request().method() === method && new URL(response.url()).pathname === path;
}

type SurveySchemaIdentity = { readonly versionRef: string; readonly schemaDigest: string };

async function readSchemaIdentity(response: Response): Promise<SurveySchemaIdentity> {
  expect(response.status()).toBe(200);
  const payload = (await response.json()) as { data?: Partial<SurveySchemaIdentity> };
  if (typeof payload.data?.versionRef !== 'string' || typeof payload.data.schemaDigest !== 'string') {
    throw new Error('The public form did not return its composition identity.');
  }
  expect(payload.data.versionRef.length).toBeGreaterThan(0);
  expect(payload.data.schemaDigest).toMatch(/^[a-f0-9]{64}$/u);
  return { versionRef: payload.data.versionRef, schemaDigest: payload.data.schemaDigest };
}

test('guest program and direct survey resolve to the same form and commit an answer', async ({ browser, page }) => {
  const program = encodeURIComponent(requiredFixtureSetting('COGLATAS_AQ_PROGRAM_CODE'));
  const target = encodeURIComponent(requiredFixtureSetting('COGLATAS_AQ_SURVEY_CODE'));
  const presentationTitle = requiredFixtureSetting('COGLATAS_AQ_PRESENTATION_TITLE');
  const questionLabel = requiredFixtureSetting('COGLATAS_AQ_GUEST_QUESTION_LABEL');
  const targetPath = publicApi + target;

  await page.goto(`/app/aq/events/${program}`);
  const listingSurvey = page.waitForResponse(response => matchesResponse(response, 'GET', targetPath));
  await page.getByRole('link', { name: presentationTitle, exact: true }).click();
  const programIdentity = await readSchemaIdentity(await listingSurvey);

  const context = await browser.newContext({ baseURL: new URL(page.url()).origin });
  try {
    const direct = await context.newPage();
    const directSurvey = direct.waitForResponse(response => matchesResponse(response, 'GET', targetPath));
    await direct.goto(`/app/aq/surveys/${target}`);
    expect(await readSchemaIdentity(await directSurvey)).toEqual(programIdentity);
    await expect(direct.getByRole('textbox', { name: questionLabel, exact: true })).toBeVisible();
  } finally {
    await context.close();
  }

  const answer = page.getByRole('textbox', { name: questionLabel, exact: true });
  await expect(answer).toBeVisible();
  await answer.focus();
  await expect(answer).toBeFocused();
  await page.keyboard.insertText('Synthetic audience response');
  const submission = page.waitForResponse(response => matchesResponse(response, 'POST', targetPath + '/submissions'));
  await page.getByRole('button', { name: submitText, exact: true }).press('Enter');
  expect((await submission).status()).toBe(201);
  await expect(page.getByText(successText, { exact: true })).toBeVisible();
});
