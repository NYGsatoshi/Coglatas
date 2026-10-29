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

const securityShadowThree = Object.freeze({
  ...hostedStatic,
  COGLATAS_STATIC_PLAYWRIGHT_WORKERS: '3',
  COGLATAS_STATIC_PLAYWRIGHT_SHADOW3: '1',
  GITHUB_EVENT_NAME: 'pull_request',
  GITHUB_BASE_REF: 'SecurityTestImplimation',
});

test('exact SecurityTestImplimation PR may trial three workers on the hosted prebuilt static suite', () => {
  assert.deepEqual(staticPlaywrightWorkerArgs(securityShadowThree, []), ['--workers=3']);
  assert.deepEqual(staticPlaywrightWorkerArgs(hostedStatic, []), ['--workers=2']);
  assert.equal(Object.isFrozen(securityShadowThree), true);
});

test('three workers require every independent event, branch and opt-in control', () => {
  const invalid = [
    ['COGLATAS_STATIC_PLAYWRIGHT_SHADOW3', [undefined, '', '0', 'true', '01', '2']],
    ['GITHUB_EVENT_NAME', [undefined, '', 'push', 'workflow_dispatch', 'pull_request_target']],
    ['GITHUB_BASE_REF', [undefined, '', 'main', 'SecurityTestImplementation',
                         'securitytestimplimation', 'SecurityTestImplimation/other']],
    ['COGLATAS_STATIC_PLAYWRIGHT_WORKERS', [undefined, '', '4', '30', '03', '3 ']],
  ];
  for (const [key, values] of invalid) {
    for (const value of values) {
      assert.deepEqual(
        staticPlaywrightWorkerArgs({ ...securityShadowThree, [key]: value }, []),
        [],
        `invalid three-worker gate ${key}=${String(value)} must fail closed`,
      );
    }
  }
});

test('three workers cannot bypass real backend, custom server, explicit CLI or hosted build gates', () => {
  for (const key of ['CI', 'PLAYWRIGHT_SKIP_BUILD', 'COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD']) {
    for (const value of [undefined, 'false', '0', '']) {
      assert.deepEqual(staticPlaywrightWorkerArgs({ ...securityShadowThree, [key]: value }, []), []);
    }
  }
  for (const key of ['COGLATAS_REAL_BACKEND_SMOKE', 'COGLATAS_COMPAT_CRITICAL',
                     'COGLATAS_PUBLIC_HTTPS_SMOKE']) {
    assert.deepEqual(staticPlaywrightWorkerArgs({ ...securityShadowThree, [key]: '1' }, []), []);
  }
  assert.deepEqual(
    staticPlaywrightWorkerArgs({ ...securityShadowThree, PLAYWRIGHT_BASE_URL: 'http://127.0.0.1:4173' }, []),
    [],
  );
  for (const args of [['--grep', 'focus'], ['--workers=3'], ['--update-snapshots'], ['--shard=1/2']]) {
    assert.deepEqual(staticPlaywrightWorkerArgs(securityShadowThree, args), []);
  }
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

test('co-running hosted Vitest has an explicit bounded worker budget and keeps isolation', async () => {
  const { readFile } = await import('node:fs/promises');
  const [launcher, config, compose] = await Promise.all([
    readFile('scripts/ci/run-frontend-parallel.sh', 'utf8'),
    readFile('frontend/vitest.config.ts', 'utf8'),
    readFile('infra/compose/dev/playwright.yml', 'utf8'),
  ]);
  assert.match(launcher, /run_unit\(\) \{[\s\S]*GITHUB_ACTIONS:-[\s\S]*FRONTEND_PLAYWRIGHT:-[\s\S]*COGLATAS_FRONTEND_VITEST_MAX_WORKERS=2 npm --prefix frontend test/u);
  assert.match(config, /maxWorkers: isCi && process\.env\['COGLATAS_FRONTEND_VITEST_MAX_WORKERS'\] === '2' \? 2 : undefined/u);
  assert.match(config, /isolate: true/u);
  assert.doesNotMatch(config, /isolate: false/u);
  assert.match(launcher, /GITHUB_EVENT_NAME:-[^\\n]*pull_request/u);
  assert.match(launcher, /GITHUB_BASE_REF:-[^\\n]*SecurityTestImplimation/u);
  assert.match(launcher, /COGLATAS_STATIC_PLAYWRIGHT_WORKERS=3 COGLATAS_STATIC_PLAYWRIGHT_SHADOW3=1/u);
  assert.match(launcher, /COGLATAS_STATIC_PLAYWRIGHT_WORKERS=2 npm run test:ui:angular:docker/u);
  for (const name of ['COGLATAS_STATIC_PLAYWRIGHT_SHADOW3', 'GITHUB_EVENT_NAME', 'GITHUB_BASE_REF']) {
    assert.match(compose, new RegExp(`^\\s*${name}:\\s`, 'm'));
  }
});
