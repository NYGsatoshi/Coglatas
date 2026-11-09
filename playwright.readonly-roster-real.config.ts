import { defineConfig } from '@playwright/test';

const privateOutputDir = process.env.COGLATAS_UI07_ROSTER_PRIVATE_OUTPUT_DIR;
if (!/^artifacts\/roster-private\/[a-f0-9]{32}$/.test(privateOutputDir ?? '')) {
  throw new Error('ReadOnly roster requires its closed private diagnostic directory.');
}

export default defineConfig({
  testDir: './tests/ui',
  testMatch: 'readonly-roster-real.browser.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  reporter: [['line']],
  outputDir: privateOutputDir,
  use: {
    browserName: 'chromium', baseURL: process.env.PLAYWRIGHT_BASE_URL,
    locale: 'en-US', timezoneId: 'UTC', colorScheme: 'dark', reducedMotion: 'no-preference',
    trace: 'off', screenshot: 'off', video: 'off',
  },
  projects: [
    { name: 'readonly-roster-desktop', use: { viewport: { width: 1536, height: 600 } } },
    { name: 'readonly-roster-mobile', use: { viewport: { width: 320, height: 420 }, isMobile: true, hasTouch: true } },
  ],
});
