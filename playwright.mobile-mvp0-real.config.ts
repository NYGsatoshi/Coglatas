import { defineConfig } from '@playwright/test';
import base from './playwright.config';
import { mobileMvp0Device, mobileMvp0Project, mobileMvp0Title } from './tests/ui/mobile-mvp0-profile.mjs';

export default defineConfig({
  ...base,
  testMatch: ['real-backend-smoke.spec.ts'],
  grep: new RegExp(mobileMvp0Title + '$'),
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  // Raw assertion messages may contain protected synthetic message bodies.
  // Keep JUnit local to verification; publish only the closed metadata receipts.
  reporter: [['junit', { outputFile: 'test-results/playwright-results.xml' }]],
  use: { ...base.use, trace: 'off', video: 'off', screenshot: 'off' },
  projects: [{ name: mobileMvp0Project, use: { browserName: 'chromium', ...mobileMvp0Device } }],
});
