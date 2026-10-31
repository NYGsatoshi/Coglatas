import { spawnSync } from 'node:child_process';
import { strict as assert } from 'node:assert';
import { realpathSync } from 'node:fs';
import { isAbsolute, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

// Explicitly invoked inside the existing isolated Compose browser container.
// The original runner still owns selection, readiness, assertions and retries.
test('the original real-backend runner succeeds under native coverage', () => {
  const root = realpathSync(fileURLToPath(new URL('../..', import.meta.url)));
  assert.equal(realpathSync(process.cwd()), root, 'use the checked-out source root');
  assert.equal(process.env.COGLATAS_REAL_BACKEND_SMOKE, '1');
  assert.equal(process.env.PLAYWRIGHT_BASE_URL, 'http://127.0.0.1:8080');
  assert.match(process.env.TARGET_SHA ?? '', /^[0-9a-f]{40}$/);
  // node:test substitutes its own temporary directory and copies raw reports
  // to the caller's requested directory after collection has completed.
  assert.ok(isAbsolute(process.env.NODE_V8_COVERAGE ?? ''), 'native raw collection must be enabled');

  const suite = process.env.COGLATAS_NATIVE_COVERAGE_SUITE;
  assert.ok(suite === 'mobile-mvp0' || suite === 'p0', 'closed original suite selection');
  const args = suite === 'mobile-mvp0'
    ? ['--config=playwright.mobile-mvp0-real.config.ts', '--project=real-backend-mobile-mvp0-320',
      '--retries=0', '--workers=1'] : [];
  if (suite === 'mobile-mvp0') assert.equal(process.env.COGLATAS_REAL_BACKEND_MVP0_MOBILE, '1');
  else assert.notEqual(process.env.COGLATAS_REAL_BACKEND_MVP0_MOBILE, '1');
  console.log(`Original-runner native coverage runtime: ${process.version}.`);

  const result = spawnSync(process.execPath,
    [resolve(root, 'tests/ui/run-real-backend-playwright.mjs'), ...args], {
      cwd: root, env: process.env, stdio: 'inherit',
    });
  assert.equal(result.error, undefined, 'the original child must start');
  assert.equal(result.signal, null, 'a signaled child cannot qualify');
  assert.equal(result.status, 0, 'the original runner must pass');
});
