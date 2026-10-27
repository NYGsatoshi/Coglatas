import { execFileSync, spawnSync } from 'node:child_process';
import { accessSync, constants, lstatSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const phases = new Set(['OUTPUT_PREPARATION', 'INVOCATION_BINDING', 'SOURCE_BINDING', 'HARNESS_PREPARATION', 'HARNESS_EXECUTION']);
const filesystemCodes = new Set(['EACCES', 'EPERM', 'ENOENT', 'ENOTDIR', 'ELOOP', 'ENOSPC', 'EROFS', 'EIO']);
const defaultIo = { lstatSync, accessSync, uid: () => process.getuid() };
const flag = value => typeof value === 'boolean' ? value : null;

function osCode(error) {
  try { return typeof error?.code === 'string' && filesystemCodes.has(error.code) ? error.code : null; }
  catch { return null; }
}

/** Stat only the two fixed setup paths; never read targets, names, files or data. */
export function observeMobileOutput(io = defaultIo) {
  const unavailable = state => ({ state, directory: null, symlink: null, ownedByRunner: null,
    writableByRunner: null, searchableByRunner: null, privateToRunner: null });
  const inspect = path => {
    try {
      const info = io.lstatSync(path);
      const directory = !!info.isDirectory(), symlink = !!info.isSymbolicLink();
      const value = { state: 'OBSERVED', directory, symlink,
        ownedByRunner: Number.isSafeInteger(info.uid) && Number.isSafeInteger(io.uid()) ? info.uid === io.uid() : null,
        writableByRunner: null, searchableByRunner: null,
        privateToRunner: Number.isSafeInteger(info.mode) ? (info.mode & 0o077) === 0 : null };
      if (directory && !symlink) {
        for (const [field, permission] of [['writableByRunner', constants.W_OK], ['searchableByRunner', constants.X_OK]]) {
          try { io.accessSync(path, permission); value[field] = true; }
          catch (error) { value[field] = ['EACCES', 'EPERM'].includes(osCode(error)) ? false : null; }
        }
      }
      return value;
    } catch (error) { return unavailable(osCode(error) === 'ENOENT' ? 'MISSING' : 'UNAVAILABLE'); }
  };
  const artifactRoot = inspect('artifacts');
  // Do not traverse a symlink or an unverified ancestor to inspect the child.
  const mobileOutput = artifactRoot.state === 'OBSERVED' && artifactRoot.directory && !artifactRoot.symlink
    ? inspect('artifacts/mobile') : unavailable('NOT_INSPECTED');
  return { artifactRoot, mobileOutput };
}

/** Closed setup metadata only; no exception text, UID, path, argument or credential.
 * A spawned harness process does not establish browser or owner execution.
 */
export function mobileSetupDiagnostic(state, io = defaultIo) {
  return { schemaVersion: 1, scope: 'SYNTHETIC_MOBILE_SETUP_DIAGNOSTIC',
    phase: phases.has(state.phase) ? state.phase : 'UNKNOWN', osCode: osCode(state.error),
    sourceClaim: typeof state.sourceClaim === 'string' && /^[0-9a-f]{40}$/.test(state.sourceClaim) ? state.sourceClaim : null,
    runId: typeof state.runId === 'string' && /^[1-9][0-9]{0,19}$/.test(state.runId) ? state.runId : null,
    runAttempt: typeof state.runAttempt === 'string' && /^[1-9][0-9]{0,8}$/.test(state.runAttempt) ? state.runAttempt : null,
    invocationValid: flag(state.invocationValid), sourceMatched: flag(state.sourceMatched),
    sourceClean: flag(state.sourceClean), harnessStartAttempted: state.harnessStartAttempted === true,
    harnessProcessStarted: state.harnessProcessStarted === true,
    harnessExitCode: Number.isInteger(state.harnessExitCode) && state.harnessExitCode >= 0 && state.harnessExitCode <= 255
      ? state.harnessExitCode : null,
    filesystem: observeMobileOutput(io), functionalAcceptance: 'NOT_EVALUATED', publicGo: false };
}

function main() {
  const state = { phase: 'OUTPUT_PREPARATION', sourceClaim: process.env.TARGET_SHA,
    runId: process.env.GITHUB_RUN_ID, runAttempt: process.env.GITHUB_RUN_ATTEMPT,
    invocationValid: null, sourceMatched: null, sourceClean: null, harnessStartAttempted: false, harnessProcessStarted: false, harnessExitCode: null };
  const emit = () => console.info('MOBILE_SETUP_DIAGNOSTIC ' + JSON.stringify(mobileSetupDiagnostic(state)));
  emit();
  try {
    mkdirSync('artifacts/mobile', { recursive: true });
    for (const name of ['owners.json', 'diagnostics.json', 'image-before.json', 'image-continuity.json', 'source-sha']) {
      rmSync('artifacts/mobile/' + name, { force: true });
    }
    state.phase = 'INVOCATION_BINDING';
    state.invocationValid = process.argv.length === 2;
    if (!state.invocationValid) { throw new Error('Mobile acceptance does not accept selection overrides.'); }
    state.phase = 'SOURCE_BINDING';
    const sha = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
    state.sourceMatched = /^[0-9a-f]{40}$/.test(sha) && process.env.TARGET_SHA === sha;
    if (!state.sourceMatched) { throw new Error('Mobile acceptance requires an exact clean candidate.'); }
    state.sourceClean = !execFileSync('git', ['status', '--porcelain', '--untracked-files=no'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
    if (!state.sourceClean) { throw new Error('Mobile acceptance requires an exact clean candidate.'); }
    writeFileSync('artifacts/mobile/source-sha', sha + '\n');
    state.phase = 'HARNESS_PREPARATION';
    const environment = { ...process.env };
    for (const key of ['DEBUG', 'PWDEBUG', 'NODE_OPTIONS', 'PLAYWRIGHT_JSON_OUTPUT_NAME', 'PLAYWRIGHT_JUNIT_OUTPUT_NAME']) {
      delete environment[key];
    }
    state.phase = 'HARNESS_EXECUTION';
    state.harnessStartAttempted = true;
    const result = spawnSync('bash', ['scripts/ci/functional-compose-harness.sh'], { stdio: 'inherit', env: {
      ...environment, COGLATAS_REAL_BACKEND_MOBILE: '1',
      COGLATAS_APP_IMAGE: process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1'
        ? process.env.COGLATAS_APP_IMAGE : 'coglatas-mobile-acceptance:' + sha,
      FUNCTIONAL_COMPOSE_FILES: 'infra/compose/test/real-backend-smoke.yml,infra/compose/test/mobile-acceptance.yml',
    } });
    state.harnessProcessStarted = Number.isInteger(result.pid) && result.pid > 0;
    state.error = result.error;
    state.harnessExitCode = result.status ?? 1;
    emit();
    return state.harnessExitCode;
  } catch (error) {
    state.error = error;
    console.error('Mobile acceptance setup failed; verify the clean source, runtime and protected build environment.');
    emit();
    return 1;
  }
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) process.exitCode = main();
