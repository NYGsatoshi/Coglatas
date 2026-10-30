import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { createReadStream, closeSync, lstatSync, mkdirSync, mkdtempSync, openSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

export const criteria = Object.freeze(['fileSharingFalse', 'sameTenant', 'knownAResourceDeniedToB', 'currentMeIsB',
  'sameDocument', 'oldSocketsClosed', 'freshSocketCreated', 'noOldInitialBodyInB', 'bFreshBodyRendered',
  'bFreshWireOnReplacement', 'bPersistence', 'aPositiveBodyRendered', 'aPositiveWire', 'aPrivateWireExcluded',
  'aPrivateUiExcluded', 'passiveObservationLive', 'noDocumentNavigation']);
const frozenScenario = 'b8eaf7577da1503db15ac85f57331090b40c981d932727f648dd2216907ba942';
const sha = /^[a-f0-9]{40}$/;
const digest = /^[a-f0-9]{64}$/;
const imageId = /^sha256:[a-f0-9]{64}$/;
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function keys(value, allowed) {
  assert.ok(value && typeof value === 'object' && !Array.isArray(value));
  assert.ok(Object.keys(value).every(key => allowed.includes(key)));
}
export function validateAdmission(input) {
  assert.ok(sha.test(input.source) && sha.test(input.head) && sha.test(input.sourceTree));
  assert.equal(input.source, input.checkout);
  assert.equal(input.source, input.artifactSource);
  assert.equal(input.source, input.dotnetSource);
  assert.equal(input.source, input.frontendSource);
  assert.equal(input.sourceTree, input.headTree, 'Producer and reviewed head trees differ');
  assert.match(input.runId, /^[1-9][0-9]*$/);
  assert.match(input.attempt, /^[1-9][0-9]*$/);
  if (input.event === 'pull_request') {
    assert.equal(input.artifactHead, input.head);
    assert.deepEqual(input.parents?.slice(1), [input.head]);
    assert.equal(input.imageName, `coglatas-pr-functional:${input.source}`);
  } else {
    assert.equal(input.event, 'push');
    assert.equal(input.ref, 'refs/heads/main');
    assert.equal(input.head, input.source);
    assert.equal(input.imageName, `coglatas-main-runtime:${input.source}`);
  }
  assert.match(input.producerImage, imageId);
  assert.match(input.engineImage, imageId);
}
export function validateJourney(report, source, started, now = Date.now()) {
  keys(report, ['schemaVersion', 'scope', 'source', 'startedAtUtc', 'completedAtUtc', 'qualification', 'release',
    'observations', 'failedCriteria', 'runtime', 'proofDigests', 'failure']);
  assert.equal(report.schemaVersion, 1);
  assert.equal(report.scope, 'ISOLATED_REAL_BROWSER_SAME_CONTEXT_ACTOR_REPLACEMENT');
  assert.deepEqual(report.source, { frontend: source, backend: source });
  assert.equal(report.release, 'RELEASE_NO_GO');
  assert.equal(report.qualification, 'BOUNDED_PASS');
  assert.deepEqual(report.failedCriteria, []);
  assert.equal(report.failure, undefined);
  const begin = Date.parse(report.startedAtUtc), end = Date.parse(report.completedAtUtc);
  assert.ok(Number.isFinite(begin) && begin >= started - 2000 && end >= begin && end <= now + 2000);
  assert.ok(end - begin < 240000, 'Frozen watchdog budget exceeded');
  keys(report.observations, [...criteria, 'historyLengthBefore', 'historyDeltaToLogin', 'passiveObservationMilliseconds']);
  for (const key of criteria) assert.equal(report.observations[key], true, key);
  assert.equal(report.observations.passiveObservationMilliseconds, 15000);
  assert.ok(Number.isInteger(report.observations.historyLengthBefore) && report.observations.historyLengthBefore > 1);
  assert.ok(Number.isInteger(report.observations.historyDeltaToLogin) && report.observations.historyDeltaToLogin < 0);
  keys(report.runtime, ['node', 'playwright', 'browser']);
  assert.match(report.runtime.node, /^v[0-9]+\.[0-9]+\.[0-9]+$/);
  assert.match(report.runtime.playwright, /^[0-9]+\.[0-9]+\.[0-9]+$/);
  assert.match(report.runtime.browser, /^[0-9]+(?:\.[0-9]+){1,3}$/);
  keys(report.proofDigests, ['aBody', 'bBody', 'tenant', 'aUser', 'bUser']);
  for (const key of ['aBody', 'bBody', 'tenant', 'aUser', 'bUser']) assert.match(report.proofDigests[key], digest);
  // Publish fresh finite facts only. Private diagnostics, identifiers and body hashes never leave control.
  return { criteria: Object.fromEntries(criteria.map(key => [key, true])), passiveObservationMilliseconds: 15000,
    historyDeltaToLogin: report.observations.historyDeltaToLogin, runtime: report.runtime };
}
export function validateCompletion({ exitCode, signal, journey, image, admission, expectedHashes, started, now }) {
  assert.equal(signal, null);
  assert.equal(exitCode, 0, 'Harness includes successful cleanup in its exit status');
  keys(image, ['schemaVersion', 'scope', 'sourceSha', 'toolSourceClaim', 'harnessSha256', 'helperSha256',
    'verifierSha256', 'expectedProducerImageId', 'engineImageId', 'bindingSha256', 'beforeProbeVerified',
    'afterProbeVerified', 'suiteExitCode']);
  assert.equal(image.schemaVersion, 1);
  assert.equal(image.scope, 'within-this-functional-tool-invocation');
  assert.equal(image.sourceSha, admission.source);
  assert.equal(image.toolSourceClaim, admission.source);
  assert.equal(image.expectedProducerImageId, admission.producerImage);
  assert.equal(image.engineImageId, admission.engineImage);
  assert.equal(image.beforeProbeVerified, true);
  assert.equal(image.afterProbeVerified, true);
  assert.equal(image.suiteExitCode, 0);
  for (const key of ['harnessSha256', 'helperSha256', 'verifierSha256']) assert.equal(image[key], expectedHashes[key]);
  assert.ok(image.bindingSha256 === '' || digest.test(image.bindingSha256));
  if (admission.producerImage !== admission.engineImage) assert.match(image.bindingSha256, digest);
  return validateJourney(journey, admission.source, started, now);
}
function command(program, args) {
  const result = spawnSync(program, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 65536 });
  assert.equal(result.status, 0, 'Fixed source admission command failed');
  return result.stdout.trim();
}
function boundedFile(file, maximum = 65536) {
  const stat = lstatSync(file);
  assert.ok(stat.isFile() && !stat.isSymbolicLink() && stat.nlink === 1 && stat.size > 0 && stat.size <= maximum);
  return readFileSync(file, 'utf8');
}
function privateFile(file, maximum) {
  const stat = lstatSync(file);
  assert.equal(stat.uid, process.getuid());
  assert.equal(stat.mode & 0o777, 0o600);
  return boundedFile(file, maximum);
}
function stamp(file) {
  const value = boundedFile(file, 41);
  assert.match(value, /^[a-f0-9]{40}\n?$/);
  return value.trim();
}
async function fileHash(file) {
  const result = createHash('sha256');
  for await (const chunk of createReadStream(file)) result.update(chunk);
  return result.digest('hex');
}
async function run() {
  let phase = 'ADMISSION';
  const publicFile = resolve('artifacts/actor-replacement/receipt.json');
  // This path contains only the closed receipt. All child output stays in runner-temp.
  mkdirSync(resolve('artifacts/actor-replacement'), { recursive: true });
  let receipt = { schemaVersion: 1, qualification: 'INCOMPLETE', phase, release: 'RELEASE_NO_GO' };
  let fd;
  try {
    assert.equal(process.platform, 'linux');
    assert.equal(process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE, '1');
    assert.equal(hash(readFileSync('tests/ui/actor-replacement-real.mjs')), frozenScenario);
    const root = resolve(process.env.ACTOR_ARTIFACT_ROOT || '');
    assert.ok(root.startsWith(resolve(process.env.RUNNER_TEMP) + '/'));
    const source = process.env.GITHUB_SHA;
    const head = process.env.GITHUB_EVENT_NAME === 'pull_request' ? process.env.PR_HEAD_SHA : source;
    assert.match(source, sha);
    assert.match(head, sha);
    const admission = {
      event: process.env.GITHUB_EVENT_NAME, ref: process.env.GITHUB_REF, source, head,
      checkout: command('/usr/bin/git', ['rev-parse', 'HEAD']),
      sourceTree: command('/usr/bin/git', ['rev-parse', `${source}^{tree}`]),
      headTree: command('/usr/bin/git', ['rev-parse', `${head}^{tree}`]),
      parents: command('/usr/bin/git', ['show', '-s', '--format=%P', 'HEAD']).split(' '),
      artifactSource: stamp(join(root, 'source-sha')),
      artifactHead: process.env.GITHUB_EVENT_NAME === 'pull_request' ? stamp(join(root, 'pr-head-sha')) : undefined,
      dotnetSource: stamp('artifacts/ci/dotnet-build-sha'),
      frontendSource: command('/usr/bin/tar', ['-xOf', join(root, 'frontend-build-artifacts.tar'), 'artifacts/ci/frontend-build-sha']),
      imageName: boundedFile(join(root, 'runtime-image-name'), 256).trim(),
      producerImage: process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID,
      engineImage: process.env.COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID,
      runId: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT
    };
    validateAdmission(admission);
    assert.equal(command('/usr/bin/git', ['status', '--porcelain', '--untracked-files=no']), '');
    const control = mkdtempSync(join(resolve(process.env.RUNNER_TEMP), 'actor-control-'));
    for (const name of ['home', 'tmp', 'diagnostics']) mkdirSync(join(control, name), { mode: 0o700 });
    const password = `Actor!${randomBytes(24).toString('hex')}`;
    const ownerPassword = `Owner!${randomBytes(24).toString('hex')}`;
    for (const value of [password, `${password}:recipient`, `${password}:operator`, ownerPassword]) console.log(`::add-mask::${value}`);
    const childEnv = { ...process.env, ACTOR_UID: String(process.getuid()), ACTOR_GID: String(process.getgid()),
      ACTOR_CONTROL_DIR: control, TARGET_SHA: source, COGLATAS_BROWSER_SMOKE_EMAIL: 'e2e-user@example.test',
      COGLATAS_BROWSER_SMOKE_PASSWORD: password, COGLATAS_SEED_ADMIN_PASSWORD: ownerPassword,
      FUNCTIONAL_COMPOSE_PROJECT_NAME: `coglatas-actor-${admission.runId}-${admission.attempt}`,
      FUNCTIONAL_COMPOSE_FILES: 'infra/compose/test/real-backend-smoke.yml,infra/compose/test/actor-replacement.yml',
      COGLATAS_FUNCTIONAL_TOOL_SHA: source, FUNCTIONAL_DIAGNOSTIC_DIR: join(control, 'diagnostics'), SYNCFUSION_LICENSE: 'artifact-reuse-no-build' };
    for (const key of ['COGLATAS_REAL_BACKEND_MOBILE', 'COGLATAS_FUNCTIONAL_DOMAIN', 'COGLATAS_REAL_BACKEND_P0_SETUP']) delete childEnv[key];
    const hashes = Object.fromEntries([['harnessSha256', 'functional-compose-harness.sh'], ['helperSha256', 'acceptance-image-reuse.sh'],
      ['verifierSha256', 'runtime_image_identity.py']].map(([key, file]) => [key, hash(readFileSync(`scripts/ci/${file}`))]));
    phase = 'HARNESS';
    fd = openSync(join(control, 'harness.private.log'), 'wx', 0o600);
    const started = Date.now();
    const child = spawn('/usr/bin/bash', ['scripts/ci/functional-compose-harness.sh'], { env: childEnv, stdio: ['ignore', fd, fd] });
    let interrupted = false;
    const interrupt = () => { interrupted = true; child.kill('SIGINT'); };
    const terminate = () => { interrupted = true; child.kill('SIGTERM'); };
    process.once('SIGINT', interrupt); process.once('SIGTERM', terminate);
    let result;
    try {
      result = await new Promise((resolveChild, reject) => {
        child.once('error', reject);
        child.once('close', (exitCode, signal) => resolveChild({ exitCode, signal }));
      });
    } finally {
      process.removeListener('SIGINT', interrupt); process.removeListener('SIGTERM', terminate);
      closeSync(fd); fd = undefined;
    }
    phase = 'RECEIPT';
    assert.equal(interrupted, false);
    const log = privateFile(join(control, 'harness.private.log'), 16 * 1024 * 1024);
    const records = log.split(/\r?\n/).filter(line => line.startsWith('FUNCTIONAL_IMAGE_REUSE '));
    assert.equal(records.length, 1);
    const image = JSON.parse(records[0].slice('FUNCTIONAL_IMAGE_REUSE '.length));
    const journey = JSON.parse(privateFile(join(control, 'result.json'), 65536));
    const finite = validateCompletion({ ...result, journey, image, admission, expectedHashes: hashes, started });
    receipt = { schemaVersion: 1, qualification: 'BOUNDED_PASS', scope: 'SAME_DOCUMENT_ACTOR_REPLACEMENT',
      sourceSha: source, reviewedHeadSha: head, sourceTree: admission.sourceTree, producerHeadTreeEqual: true,
      runId: admission.runId, attempt: admission.attempt, journeys: 1, criteriaPassed: 17, ...finite,
      producerImageId: admission.producerImage, engineImageId: admission.engineImage,
      imageBeforeAfterVerified: true, cleanupSucceeded: true, frozenScenarioSha256: frozenScenario,
      imageArchiveSha256: await fileHash(join(root, 'runtime-image.tar.gz')),
      release: 'RELEASE_NO_GO' };
    process.exitCode = 0;
  } catch {
    receipt = { schemaVersion: 1, qualification: 'INCOMPLETE', phase, release: 'RELEASE_NO_GO' };
    process.exitCode = 1;
  } finally {
    if (fd !== undefined) closeSync(fd);
    writeFileSync(publicFile, JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  }
  console.log(`Actor replacement: ${receipt.qualification}; release remains RELEASE_NO_GO.`);
}
if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) await run();
