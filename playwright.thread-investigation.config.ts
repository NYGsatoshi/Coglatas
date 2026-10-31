import { defineConfig } from '@playwright/test';
import { privatePlaywrightOutput } from './tests/ui/helpers/private-playwright-output.mjs';

const mode = process.env.THREAD_INVESTIGATION_MODE ?? 'matrix';
if (!['matrix', 'default-fixture'].includes(mode)) { throw new Error('Unknown Thread investigation mode.'); }
const baseURL = 'http://127.0.0.1:4187';
export default defineConfig({
  testDir: './tests',
  testMatch: mode === 'matrix' ? '**/diagnostics/thread-denial-matrix.browser.ts' : '**/ui/message-thread-context.spec.ts',
  ...(mode === 'default-fixture' ? { grep: /uses a dedicated 320px thread pane|purges protected tabs, bodies and drafts after an authoritative 403/ } : {}),
  timeout: mode === 'matrix' ? 120_000 : 30_000,
  expect: { timeout: mode === 'matrix' ? 15_000 : 5_000 }, workers: 1, retries: 0,
  maxFailures: 1, fullyParallel: false, forbidOnly: true,
  outputDir: privatePlaywrightOutput('thread'),
  reporter: [['./tests/diagnostics/thread-matrix-reporter.mjs']],
  use: { baseURL, trace: 'off', screenshot: 'off', video: 'off',
    storageState: { cookies: [], origins: [{ origin: baseURL, localStorage: [{ name: 'coglatas.locale', value: 'en' }] }] } },
  projects: [{ name: 'mocked-thread-matrix-320-touch', use: {
    browserName: 'chromium', viewport: { width: 320, height: 800 }, screen: { width: 320, height: 800 },
    deviceScaleFactor: 1, isMobile: true, hasTouch: true,
  } }],
  webServer: { command: 'node tests/ui/serve-static.mjs --host 127.0.0.1 --port 4187',
    url: `${baseURL}/health`, reuseExistingServer: false, timeout: 120_000, stdout: 'ignore', stderr: 'ignore' },
});
