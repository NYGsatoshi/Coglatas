import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { staticPlaywrightShardArgs } from './static-playwright-shard.mjs';

const eligible = Object.freeze({
  CI: 'true',
  PLAYWRIGHT_SKIP_BUILD: '1',
  COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD: 'true',
  COGLATAS_STATIC_PLAYWRIGHT_WORKERS: '2',
  COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: '1',
});

test('only exact two-shard identities can be emitted for prebuilt full static suite', () => {
  assert.throws(() => staticPlaywrightShardArgs(eligible), /not permitted/);
  assert.throws(() => staticPlaywrightShardArgs({ COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: 'true' }), /not permitted/);
  assert.deepEqual(staticPlaywrightShardArgs({ COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: '0' }), []);
  for (const shard of ['1/2', '2/2']) {
    const env = { ...eligible, COGLATAS_STATIC_PLAYWRIGHT_SHARD: shard };
    assert.deepEqual(staticPlaywrightShardArgs(env), ['--shard=' + shard]);
    assert.equal(env.COGLATAS_STATIC_PLAYWRIGHT_SHARD, shard);
  }
});

test('missing authorization, invalid shard counts and unsafe modes fail closed', () => {
  const enabled = { ...eligible, COGLATAS_STATIC_PLAYWRIGHT_SHARD: '1/2' };
  for (const shard of ['0/2', '3/2', '1/1', '2/3', '1/3', '1/02', '1/2 ', 'true', '-1/2']) {
    assert.throws(() => staticPlaywrightShardArgs({ ...enabled, COGLATAS_STATIC_PLAYWRIGHT_SHARD: shard }), /not permitted/);
  }
  for (const key of ['CI', 'PLAYWRIGHT_SKIP_BUILD', 'COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD',
    'COGLATAS_STATIC_PLAYWRIGHT_WORKERS', 'COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH']) {
    for (const value of ['', '0', 'false', undefined]) {
      assert.throws(() => staticPlaywrightShardArgs({ ...enabled, [key]: value }), /not permitted/);
    }
  }
  for (const key of ['COGLATAS_COMPAT_CRITICAL', 'COGLATAS_REAL_BACKEND_SMOKE', 'COGLATAS_PUBLIC_HTTPS_SMOKE']) {
    assert.throws(() => staticPlaywrightShardArgs({ ...enabled, [key]: '1' }), /not permitted/);
  }
  assert.throws(() => staticPlaywrightShardArgs({ ...enabled, PLAYWRIGHT_BASE_URL: 'https://example.test' }), /not permitted/);
  assert.throws(() => staticPlaywrightShardArgs(enabled, ['--grep', 'partial']), /not permitted/);
});

test('full default suite never emits a shard even in protected hosted frontend', () => {
  const fullRun = { ...eligible, COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: '0' };
  assert.deepEqual(staticPlaywrightShardArgs(fullRun), []);
  assert.deepEqual(staticPlaywrightShardArgs({}, []), []);
  assert.deepEqual(staticPlaywrightShardArgs(undefined, []), []);
});

test('required gate cannot accidentally opt into half coverage or duplicate artifacts', () => {
  const parallel = readFileSync('scripts/ci/run-frontend-parallel.sh', 'utf8');
  const config = readFileSync('playwright.config.ts', 'utf8');
  const runner = readFileSync('tests/ui/run-angular-playwright.mjs', 'utf8');
  const compose = readFileSync('infra/compose/dev/playwright.yml', 'utf8');
  const workflow = readFileSync('.github/workflows/ci.yml', 'utf8');

  assert.match(parallel, /COGLATAS_STATIC_PLAYWRIGHT_SHARD:-/);
  assert.match(parallel, /COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH:-0/);
  assert.match(parallel, /Protected frontend-test must never execute a partial Playwright shard/);
  assert.match(config, /fullyParallel: true/);
  assert.match(config, /name: "chromium-desktop"/);
  assert.match(config, /name: "chromium-mobile"/);
  assert.match(runner, /staticPlaywrightShardArgs\(process\.env, playwrightArgs\)/);
  assert.match(compose, /COGLATAS_STATIC_PLAYWRIGHT_SHARD: "\$\{COGLATAS_STATIC_PLAYWRIGHT_SHARD:-\}"/);
  const jobHeadings = [...workflow.matchAll(/^  ([a-z0-9-]+):[ \t]*$/gm)];
  const index = jobHeadings.findIndex((match) => match[1] === 'frontend-test');
  assert.ok(index >= 0, 'required frontend-test job must exist');
  const frontendJob = workflow.slice(
    jobHeadings[index].index,
    jobHeadings[index + 1]?.index ?? workflow.length,
  );
  assert.match(frontendJob, /^    name: frontend-test$/m);
  assert.match(frontendJob, /run: bash scripts\/ci\/run-frontend-parallel\.sh/);
  assert.doesNotMatch(workflow, /COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH:\s*['"]?1/);
  assert.doesNotMatch(workflow, /name: frontend-playwright-shard/);
  // Consumer download references are expected; only the immutable Producer may publish.
  const producer = workflow.split('  frontend-build:', 2)[1]?.split('  frontend-test:', 1)[0];
  assert.ok(producer, 'frontend-build must own the immutable Angular artifact');
  assert.equal((producer.match(/name: frontend-build-artifacts/g) ?? []).length, 1);
  assert.equal((producer.match(/name: Publish frontend build artifacts/g) ?? []).length, 1);
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
