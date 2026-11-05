// Linux-CI-only regression; the native Playwright helper list is cross-platform.
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

test('native Playwright helper list remains portable and shell-independent', () => {
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  const portable = readFileSync('tests/ui/static-playwright-shard.node-test.mjs', 'utf8');
  assert.match(runner, /'tests\/ui\/static-playwright-shard\.node-test\.mjs'/);
  assert.doesNotMatch(runner, /static-playwright-protected-launcher\.node-test\.mjs/);
  assert.doesNotMatch(portable, /node:child_process|spawnSync\(|'bash'/);
});

test('protected full frontend launcher rejects partial sharding before any worker starts', () => {
  const directory = mkdtempSync(join(tmpdir(), 'coglatas-shard-guard-'));
  try {
    for (const changes of [
      { COGLATAS_STATIC_PLAYWRIGHT_SHARD: '1/2' },
      { COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: '1' },
    ]) {
      const logDir = join(directory, 'no-worker-logs');
      const result = spawnSync('bash', ['scripts/ci/run-frontend-parallel.sh'], {
        cwd: process.cwd(),
        encoding: 'utf8',
        timeout: 10000,
        env: {
          ...process.env,
          FRONTEND_BUILD: 'false', FRONTEND_UNIT: 'false',
          FRONTEND_ARCHITECTURE: 'false', FRONTEND_PLAYWRIGHT: 'false',
          FRONTEND_STORYBOOK: 'false', FRONTEND_LICENSE_GUARD: 'false',
          COGLATAS_STATIC_PLAYWRIGHT_SHARD: '',
          COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: '0',
          CI_PARALLEL_LOG_DIR: logDir,
          ...changes,
        },
      });
      assert.equal(result.status, 1, 'partial shard request must fail before frontend work');
      assert.match(result.stderr, /Protected frontend-test must never execute a partial Playwright shard/);
      assert.equal(existsSync(logDir), false, 'refusal must happen before any lane starts');
    }
  } finally {
    rmSync(directory, { force: true, recursive: true });
  }
});
