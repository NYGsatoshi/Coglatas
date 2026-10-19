import { defineConfig, devices } from '@playwright/test';

// Separate from #481 and static Angular coverage. The runner fixes the selection.
export default defineConfig({
  testDir: '.',
  testMatch: 'student-council-pilot.browser.ts',
  timeout: 240_000,
  expect: { timeout: 15_000 },
  workers: 1,
  retries: 0,
  fullyParallel: false,
  forbidOnly: true,
  reporter: [['./student-council-pilot-reporter.mjs']],
  use: {
    ...devices['Desktop Chrome'],
    baseURL: process.env.COGLATAS_STUDENT_PILOT_URL,
    locale: 'en-US',
    timezoneId: 'UTC',
    ignoreHTTPSErrors: false,
    trace: 'off', screenshot: 'off', video: 'off',
    serviceWorkers: 'block',
    storageState: { cookies: [], origins: [] }
  },
  projects: [{ name: 'student-council-pilot-chromium' }]
});
