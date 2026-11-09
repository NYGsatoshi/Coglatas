import { defineConfig } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { mobileMvp0Device } from './mobile-mvp0-profile.mjs';

// Measure the existing controlled-DOM regressions without a Product server.
export default defineConfig({
  testDir: fileURLToPath(new URL('.', import.meta.url)),
  testMatch: /group-entry-workspace\.spec\.ts$/,
  timeout: 30_000,
  expect: { timeout: 5_000 },
  retries: 0,
  workers: 1,
  reporter: [['list']],
  outputDir: fileURLToPath(new URL('../../artifacts/sonar-group-entry', import.meta.url)),
  use: {
    browserName: 'chromium',
    channel: process.platform === 'win32' ? 'msedge' : undefined,
    headless: true,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
  projects: [
    { name: 'group-entry-desktop' },
    { name: 'group-entry-320-touch', use: mobileMvp0Device },
  ],
});
