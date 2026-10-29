import assert from 'node:assert/strict';
import test from 'node:test';

import { staticPlaywrightWorkerArgs } from './static-playwright-workers.mjs';

const hostedStatic = Object.freeze({
  CI: 'true',
  PLAYWRIGHT_SKIP_BUILD: '1',
  COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD: 'true',
  COGLATAS_STATIC_PLAYWRIGHT_WORKERS: '2',
});

test('exact prebuilt CI static suite uses only a bounded two-worker override', () => {
  const argv = staticPlaywrightWorkerArgs(hostedStatic, []);
  assert.deepEqual(argv, ['--workers=2']);
  assert.deepEqual(hostedStatic, {
    CI: 'true',
    PLAYWRIGHT_SKIP_BUILD: '1',
    COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD: 'true',
    COGLATAS_STATIC_PLAYWRIGHT_WORKERS: '2',
  });
  assert.ok(argv.every((arg) => arg.startsWith('--workers=')));
});

test('default and unknown worker values fail closed to project-wide single worker', () => {
  for (const value of [undefined, '', '0', '1', '3', '4', '100', 'true', '2 ', '-2']) {
    assert.deepEqual(
      staticPlaywrightWorkerArgs({ ...hostedStatic, COGLATAS_STATIC_PLAYWRIGHT_WORKERS: value }, []),
      [],
      `worker setting ${String(value)} must not override Playwright default`,
    );
  }
});

test('the source revision and hosted prebuilt requirement cannot be bypassed', () => {
  for (const key of ['CI', 'PLAYWRIGHT_SKIP_BUILD', 'COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD']) {
    for (const value of [undefined, 'false', '0', 'yes', '']) {
      assert.deepEqual(
        staticPlaywrightWorkerArgs({ ...hostedStatic, [key]: value }, []),
        [],
        `${key}=${String(value)} must not activate a second worker`,
      );
    }
  }
});

test('real backend, compatibility, public HTTPS and remote static URL remain single worker', () => {
  for (const key of [
    'COGLATAS_REAL_BACKEND_SMOKE',
    'COGLATAS_COMPAT_CRITICAL',
    'COGLATAS_PUBLIC_HTTPS_SMOKE',
  ]) {
    assert.deepEqual(staticPlaywrightWorkerArgs({ ...hostedStatic, [key]: '1' }, []), [], key);
  }
  for (const url of ['http://127.0.0.1:4173', 'https://example.test', 'http://coglatas-backend:8080']) {
    assert.deepEqual(
      staticPlaywrightWorkerArgs({ ...hostedStatic, PLAYWRIGHT_BASE_URL: url }, []), [],
      'configured external or custom server must not activate a second worker',
    );
  }
});

test('focused, snapshot-update and explicit CLI arguments stay single worker', () => {
  for (const args of [
    ['--grep', 'focus'],
    ['--project=chromium-desktop'],
    ['--update-snapshots'],
    ['--workers=4'],
    ['--retries=0'],
    ['--shard=1/2'],
  ]) {
    const original = [...args];
    assert.deepEqual(staticPlaywrightWorkerArgs(hostedStatic, args), []);
    assert.deepEqual(args, original, 'the helper must not mutate explicit CLI arguments');
  }
});

test('invalid invocation is fail closed and never mutates the host environment', () => {
  assert.deepEqual(staticPlaywrightWorkerArgs({}, []), []);
  assert.deepEqual(staticPlaywrightWorkerArgs(hostedStatic, null), []);
  assert.deepEqual(staticPlaywrightWorkerArgs(null, []), []);
  assert.deepEqual(staticPlaywrightWorkerArgs(hostedStatic, '--grep'), []);
});
