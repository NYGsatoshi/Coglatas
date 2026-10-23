import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';

try {
  mkdirSync('artifacts/mobile', { recursive: true });
  for (const name of ['owners.json', 'image-before.json', 'image-continuity.json', 'source-sha']) {
    rmSync(`artifacts/mobile/${name}`, { force: true });
  }
  if (process.argv.length !== 2) { throw new Error('Mobile acceptance does not accept selection overrides.'); }
  const sha = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  if (!/^[0-9a-f]{40}$/.test(sha) || process.env.TARGET_SHA !== sha ||
      execFileSync('git', ['status', '--porcelain', '--untracked-files=no'], { encoding: 'utf8' }).trim()) {
    throw new Error('Mobile acceptance requires an exact clean candidate.');
  }
  writeFileSync('artifacts/mobile/source-sha', sha + '\n');
  const environment = { ...process.env };
  for (const key of ['DEBUG', 'PWDEBUG', 'NODE_OPTIONS', 'PLAYWRIGHT_JSON_OUTPUT_NAME', 'PLAYWRIGHT_JUNIT_OUTPUT_NAME']) {
    delete environment[key];
  }
  const result = spawnSync('bash', ['scripts/ci/functional-compose-harness.sh'], { stdio: 'inherit', env: {
    ...environment, COGLATAS_REAL_BACKEND_MOBILE: '1',
    COGLATAS_APP_IMAGE: process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1'
      ? process.env.COGLATAS_APP_IMAGE : `coglatas-mobile-acceptance:${sha}`,
    FUNCTIONAL_COMPOSE_FILES: 'infra/compose/test/real-backend-smoke.yml,infra/compose/test/mobile-acceptance.yml',
  } });
  process.exitCode = result.status ?? 1;
} catch {
  console.error('Mobile acceptance setup failed; verify the clean source, runtime and protected build environment.');
  process.exitCode = 1;
}
