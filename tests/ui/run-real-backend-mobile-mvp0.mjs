import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { buildPlaywrightGrep } from '../../scripts/ci/build-playwright-grep.mjs';
import { verifyPlaywrightRequiredTests } from '../../scripts/ci/verify-playwright-required-tests.mjs';
import { validateMobileMvp0Actors, mobileMvp0FailureLocation } from './mobile-mvp0-profile.mjs';

const directory = 'artifacts/mobile-mvp0';
const junitPath = 'test-results/playwright-results.xml';
const manifest = new URL('../../scripts/ci/real-backend-mobile-mvp0-required-tests.txt', import.meta.url);
const realIo = {
  environment: process.env, args: process.argv,
  cwdMatchesSource: () => realpathSync(process.cwd()) === realpathSync(fileURLToPath(new URL('../..', import.meta.url))),
  execFileSync, existsSync, mkdirSync, readFileSync, writeFileSync,
  buildPlaywrightGrep, verifyPlaywrightRequiredTests,
  runCompose: async () => {
    const { runRealBackendSmokeCompose } = await import('./run-real-backend-smoke-compose.mjs');
    return runRealBackendSmokeCompose();
  },
  info: console.log, error: console.error,
};

/** Default entry point uses real IO; isolated tests exercise this identical admission and execution path. */
export async function runMobileMvp0(io = realIo) {
  const env = io.environment;
  let ownsEvidence = false, scenarioStarted = false;
  let receipt = { status: 'NOT_RUN', sourceSha: null, treeSha: null,
    runId: env.GITHUB_RUN_ID ?? null, attempt: Number(env.GITHUB_RUN_ATTEMPT ?? 1),
    retries: 0, requiredCases: 1, verifiedActors: 0, imageDigest: null };
  let exitCode = 1;
  try {
    if (['COGLATAS_REAL_BACKEND_UI07_READONLY_ROSTER', 'COGLATAS_REAL_BACKEND_MOBILE',
      'COGLATAS_FCI04_ONLY', 'COGLATAS_ISSUE_683_EVIDENCE'].some(key => env[key] === '1') ||
        env.COGLATAS_FCI04_GATES || env.COGLATAS_FUNCTIONAL_FILES_GATE || !io.cwdMatchesSource()) {
      throw new Error('Conflicting selection or a different source directory is not admitted.');
    }
    if ([junitPath, ...['source-sha', 'profile.json', 'execution.json'].map(name => `${directory}/${name}`)]
      .some(path => io.existsSync(path))) {
      throw new Error('Existing receipts are preserved; a fresh execution workspace is required.');
    }
    io.mkdirSync(directory, { recursive: true });
    ownsEvidence = true;
    const git = (...args) => io.execFileSync('git', args, { encoding: 'utf8' }).trim();
    const sha = git('rev-parse', 'HEAD'), tree = git('rev-parse', 'HEAD^{tree}');
    if (io.args.length !== 2 || !/^[0-9a-f]{40}$/.test(sha) || !/^[0-9a-f]{40}$/.test(tree) ||
        env.TARGET_SHA !== sha || receipt.attempt !== 1 ||
        git('status', '--porcelain', '--untracked-files=all') || env.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1') {
      throw new Error('Exact clean first-attempt source and a dedicated source build are required.');
    }
    receipt = { ...receipt, sourceSha: sha, treeSha: tree, status: 'FAIL' };
    io.writeFileSync(`${directory}/source-sha`, sha + '\n', { flag: 'wx' });
    for (const key of ['DEBUG', 'PWDEBUG', 'NODE_OPTIONS', 'PLAYWRIGHT_JSON_OUTPUT_NAME', 'PLAYWRIGHT_JUNIT_OUTPUT_NAME']) delete env[key];
    env.COGLATAS_REAL_BACKEND_SMOKE_GREP = await io.buildPlaywrightGrep(manifest, {
      verifyPath: new URL('./real-backend-smoke.spec.ts', import.meta.url),
    });
    env.COGLATAS_REAL_BACKEND_SMOKE_SCOPE = 'independent MVP0 Mobile single case';
    env.COGLATAS_REAL_BACKEND_P0_SETUP = '1';
    env.COGLATAS_REAL_BACKEND_MVP0_MOBILE = '1';
    env.COGLATAS_FCI04_GATES = '';
    env.COGLATAS_FCI04_ONLY = '0';
    env.COGLATAS_FUNCTIONAL_FILES_GATE = '';
    env.COGLATAS_APP_IMAGE = `coglatas-mobile-mvp0:${sha}`;
    scenarioStarted = true;
    const actualExitCode = await io.runCompose();
    if (actualExitCode !== 0) throw new Error('The actual Compose execution did not succeed.');
    const junit = await io.verifyPlaywrightRequiredTests(manifest, new URL('../../test-results/playwright-results.xml', import.meta.url));
    if (junit.requiredCount !== 1 || junit.discoveredCaseCount !== 1) throw new Error('Exactly one mandatory Mobile case is required.');
    const profiles = JSON.parse(io.readFileSync(`${directory}/profile.json`, 'utf8'));
    const verifiedActors = validateMobileMvp0Actors(profiles);
    const imageDigest = io.execFileSync('docker', ['image', 'inspect', '--format', '{{.Id}}', env.COGLATAS_APP_IMAGE], { encoding: 'utf8' }).trim();
    if (!/^sha256:[0-9a-f]{64}$/.test(imageDigest)) throw new Error('The source-built image identity is required.');
    receipt = { ...receipt, status: 'PASS', verifiedActors, imageDigest, junitCases: junit.discoveredCaseCount };
    exitCode = 0;
    io.info('Independent MVP0 Mobile acceptance: one unchanged scenario, four narrow touch actors, attempt 1, retry 0.');
  } catch {
    io.error('Independent MVP0 Mobile did not qualify; inspect closed execution metadata and source-bound job steps.');
    if (ownsEvidence && scenarioStarted) {
      try {
        const failureLocation = mobileMvp0FailureLocation(io.readFileSync(junitPath, 'utf8'));
        if (failureLocation) { receipt.failureLocation = failureLocation; io.error(JSON.stringify({ failureLocation })); }
      } catch { /* No raw XML, assertion, message body or cookie is published. */ }
    }
  } finally {
    if (ownsEvidence) io.writeFileSync(`${directory}/execution.json`, JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
  }
  return exitCode;
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  process.exitCode = await runMobileMvp0();
}
