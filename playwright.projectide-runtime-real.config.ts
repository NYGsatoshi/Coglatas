import { defineConfig } from '@playwright/test';
import base from './playwright.config';

export default defineConfig({
  ...base,
  testMatch: 'projectide-runtime-recordings.browser.ts',
  webServer: undefined,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: { ...base.use, trace: 'off', screenshot: 'off', video: 'off' },
});
