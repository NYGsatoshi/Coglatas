import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { buildPlaywrightGrep } from '../../scripts/ci/build-playwright-grep.mjs';
import { verifyPlaywrightRequiredTests } from '../../scripts/ci/verify-playwright-required-tests.mjs';
import { validateMobileMvp0Actors } from './mobile-mvp0-profile.mjs';

const directory = 'artifacts/mobile-mvp0';
const manifest = new URL('../../scripts/ci/real-backend-mobile-mvp0-required-tests.txt', import.meta.url);
let receipt = { status: 'NOT_RUN', sourceSha: null, treeSha: null,
  runId: process.env.GITHUB_RUN_ID ?? null, attempt: Number(process.env.GITHUB_RUN_ATTEMPT ?? 1),
  retries: 0, requiredCases: 1, verifiedActors: 0, imageDigest: null };
try {
  mkdirSync(directory, { recursive: true });
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
  const sha = git('rev-parse', 'HEAD');
  const tree = git('rev-parse', 'HEAD^{tree}');
  if (process.argv.length !== 2 || !/^[0-9a-f]{40}$/.test(sha) || !/^[0-9a-f]{40}$/.test(tree) ||
      process.env.TARGET_SHA !== sha || receipt.attempt !== 1 ||
      git('status', '--porcelain', '--untracked-files=no') ||
      process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1') {
    throw new Error('Exact clean first-attempt source and a dedicated source build are required.');
  }
  receipt = { ...receipt, sourceSha: sha, treeSha: tree, status: 'FAIL' };
  writeFileSync(`${directory}/source-sha`, sha + '\n');
  for (const key of ['DEBUG', 'PWDEBUG', 'NODE_OPTIONS', 'PLAYWRIGHT_JSON_OUTPUT_NAME', 'PLAYWRIGHT_JUNIT_OUTPUT_NAME']) {
    delete process.env[key];
  }
  process.env.COGLATAS_REAL_BACKEND_SMOKE_GREP = await buildPlaywrightGrep(manifest, {
    verifyPath: new URL('./real-backend-smoke.spec.ts', import.meta.url),
  });
  process.env.COGLATAS_REAL_BACKEND_SMOKE_SCOPE = 'independent MVP0 Mobile single case';
  process.env.COGLATAS_REAL_BACKEND_P0_SETUP = '1';
  process.env.COGLATAS_REAL_BACKEND_MVP0_MOBILE = '1';
  process.env.COGLATAS_FCI04_GATES = '';
  process.env.COGLATAS_FCI04_ONLY = '0';
  process.env.COGLATAS_FUNCTIONAL_FILES_GATE = '';
  process.env.COGLATAS_APP_IMAGE = `coglatas-mobile-mvp0:${sha}`;
  await import('./run-real-backend-smoke-compose.mjs');
  if (process.exitCode) throw new Error('The unchanged real-backend scenario did not pass.');
  const junit = await verifyPlaywrightRequiredTests(manifest, new URL('../../test-results/playwright-results.xml', import.meta.url));
  if (junit.requiredCount !== 1 || junit.discoveredCaseCount !== 1) throw new Error('Exactly one mandatory Mobile case is required.');
  const profiles = JSON.parse(readFileSync(`${directory}/profile.json`, 'utf8'));
  const verifiedActors = validateMobileMvp0Actors(profiles);
  const imageDigest = execFileSync('docker', ['image', 'inspect', '--format', '{{.Id}}', process.env.COGLATAS_APP_IMAGE], { encoding: 'utf8' }).trim();
  if (!/^sha256:[0-9a-f]{64}$/.test(imageDigest)) throw new Error('The source-built image identity is required.');
  receipt = { ...receipt, status: 'PASS', verifiedActors, imageDigest, junitCases: junit.discoveredCaseCount };
  console.log('Independent MVP0 Mobile acceptance: one unchanged scenario, four narrow touch actors, attempt 1, retry 0.');
} catch {
  process.exitCode = 1;
  console.error('Independent MVP0 Mobile did not qualify; inspect sanitized execution metadata and source-bound job steps.');
} finally {
  writeFileSync(`${directory}/execution.json`, JSON.stringify(receipt, null, 2) + '\n');
}
