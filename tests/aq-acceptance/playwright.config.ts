import { defineConfig, devices } from '@playwright/test';

const rawOrigin = process.env.COGLATAS_AQ_SYNTHETIC_ORIGIN;

if (!rawOrigin) {
  throw new Error('Set COGLATAS_AQ_SYNTHETIC_ORIGIN to the isolated candidate backend.');
}

const origin = new URL(rawOrigin);
if (
  origin.protocol !== 'http:' ||
  origin.hostname !== '127.0.0.1' ||
  !origin.port ||
  origin.username ||
  origin.password ||
  origin.href !== `${origin.origin}/`
) {
  throw new Error('AQ browser verification requires an explicit loopback HTTP origin.');
}

export default defineConfig({
  testDir: '.',
  testMatch: process.env.COGLATAS_AQ_BROWSER_SUITE === 'protected'
    ? 'protected-feedback.spec.ts' : 'public-survey.spec.ts',
  timeout: 45_000,
  workers: 1,
  forbidOnly: true,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: origin.origin,
    locale: 'ja-JP',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
  projects: [
    { name: 'chromium-mobile-viewport', use: { ...devices['Pixel 5'] } },
    { name: 'webkit-mobile-viewport', use: { ...devices['iPhone 13'] } },
  ],
});
