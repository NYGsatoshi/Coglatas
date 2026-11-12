import { defineConfig } from '@playwright/test';
import base from './playwright.functional.config';
import { privatePlaywrightOutput } from './tests/ui/helpers/private-playwright-output.mjs';
import { mobileDeviceOptions, mobileProject } from './tests/functional/mobile/profile.mjs';

export default defineConfig({
  ...base,
  testMatch: ['**/messaging/collaboration-owner-journey.spec.ts', '**/files/files-fast-journey.spec.ts', '**/mobile/channel.browser.ts'],
  grep: /FUNC-(?:MSG-001|NOTIF-001|FILE-002|MOBILE-001)\b/,
  workers: 1, retries: 0, fullyParallel: false, forbidOnly: true,
  // Root-owned browser output stays inside the disposable runner container.
  // Host harness diagnostics must not share Playwright's cleaned output directory.
  outputDir: privatePlaywrightOutput('mobile'),
  reporter: [['./tests/functional/mobile/reporter.mjs']],
  projects: [{ name: mobileProject, use: { browserName: 'chromium', ...mobileDeviceOptions(mobileProject) } }],
});
