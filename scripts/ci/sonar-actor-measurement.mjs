import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { appendFileSync, closeSync, createReadStream, existsSync, lstatSync, mkdirSync, mkdtempSync, openSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, posix, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { validateCompletion } from '../../tests/ui/run-real-backend-actor-replacement.mjs';
import { validateNodeCoverage } from './verify-sonar-node-coverage.mjs';

export const profile = 'SONAR_ACTOR_MEASUREMENT_ONLY';
export const actorFile = 'tests/ui/actor-replacement-real.mjs';
export const frozenActor = 'b8eaf7577da1503db15ac85f57331090b40c981d932727f648dd2216907ba942';
export const measurementBranch = 'ci/sonar-actor-measurement';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const sha = /^[a-f0-9]{40}$/;
const digest = /^sha256:[a-f0-9]{64}$/;
const tools = ['scripts/ci/sonar-actor-measurement.mjs', 'scripts/ci/package-sonar-actor-measurement.sh',
  'scripts/ci/sonar-actor-frontend-output.mjs', 'frontend/angular.json', '.github/workflows/main-build-artifacts.yml', 'global.json', 'package.json',
  'scripts/ci/sonar_actor_image_identity.py', 'scripts/ci/runtime_image_identity.py',
  'scripts/ci/acceptance-image-reuse.sh', 'scripts/ci/functional-compose-harness.sh',
  'scripts/ci/verify-sonar-node-coverage.mjs', 'tests/ui/run-real-backend-actor-replacement.mjs', actorFile,
  'infra/docker/runtime-prebuilt.Dockerfile', 'infra/compose/test/real-backend-smoke.yml',
  'infra/compose/test/actor-replacement.yml', 'infra/compose/test/sonar-actor-measurement.yml'];

/** Actual push identity is preserved. Router PR metadata is analysis metadata only. */
export function validateMeasurementAdmission(value) {
  const repository = 'NYGsatoshi/Coglatas';
  assert.equal(value.repository, repository);
  assert.equal(value.owner, repository.split('/')[0]);
  assert.equal(value.actor, value.owner);
  assert.equal(value.triggeringActor, value.owner);
  assert.equal(value.eventName, 'push');
  assert.match(value.source, sha); assert.match(value.tree, sha);
  assert.equal(value.checkout, value.source); assert.equal(value.checkoutTree, value.tree);
  assert.match(value.runId, /^[1-9][0-9]*$/); assert.match(value.attempt, /^[1-9][0-9]*$/);
  assert.equal(value.event.repository.full_name, value.repository);
  assert.equal(value.event.sender.login, value.owner);
  assert.equal(value.event.after, value.source); assert.equal(value.event.ref, value.ref);
  assert.equal(value.event.deleted, false);
  if (value.ref === 'refs/heads/main') {
    assert.equal(value.analysisKind, 'main');
    assert.equal(value.prNumber, ''); assert.equal(value.prBranch, ''); assert.equal(value.prBase, '');
  } else {
    assert.equal(value.ref, `refs/heads/${measurementBranch}`);
    assert.equal(value.analysisKind, 'pull_request');
    assert.match(value.prNumber, /^[1-9][0-9]*$/);
    assert.equal(value.prBranch, measurementBranch); assert.equal(value.prBase, 'main');
  }
  assert.equal(value.imageName, `coglatas-sonar-actor-measurement:${value.source}`);
}

/** Validate complete original native rows. No record selection or hit rewriting. */
export function validateActorCoverage(text, code, root = resolve('.')) {
  assert.equal(hash(Buffer.from(code)), frozenActor);
  assert.ok(Buffer.byteLength(text) <= 1024 * 1024);
  const rows = text.trimEnd().split(/\r?\n/);
  assert.equal(rows.filter(row => row === `SF:${actorFile}`).length, 1);
  assert.equal(rows.filter(row => row.startsWith('SF:')).length, 1);
  assert.equal(rows.filter(row => row === 'TN:').length, 1);
  assert.equal(rows.filter(row => row === 'end_of_record').length, 1);
  assert.equal(rows.at(-1), 'end_of_record');
  const checked = validateNodeCoverage(text, { root, tracked: new Set([actorFile]), read: () => code });
  assert.equal(checked.sourceFiles, 1);
  const integer = value => { assert.match(value, /^(0|[1-9][0-9]*)$/); assert.ok(Number.isSafeInteger(Number(value))); return Number(value); };
  const functions = [], calls = [], branches = [], lines = [];
  const totals = new Map();
  for (const row of rows) {
    let match;
    if ((match = /^FN:(\d+),([^\r\n]+)$/.exec(row))) functions.push([integer(match[1]), match[2]]);
    else if ((match = /^FNDA:(\d+),([^\r\n]+)$/.exec(row))) calls.push([integer(match[1]), match[2]]);
    else if ((match = /^BRDA:(\d+),(\d+),(\d+),(\d+|-)$/.exec(row))) branches.push([match.slice(1, 4).join(','), match[4] === '-' ? 0 : integer(match[4])]);
    else if ((match = /^DA:(\d+),(\d+)$/.exec(row))) lines.push([integer(match[1]), integer(match[2])]);
    else if ((match = /^(FNF|FNH|BRF|BRH|LF|LH):(\d+)$/.exec(row))) { assert.ok(!totals.has(match[1])); totals.set(match[1], integer(match[2])); }
    else assert.ok(row === 'TN:' || row === `SF:${actorFile}` || row === 'end_of_record');
  }
  const compare = (left, right) => {
    if (left === right) return 0;
    return left < right ? -1 : 1;
  };
  assert.deepEqual(functions.map(row => row[1]).sort(compare), calls.map(row => row[1]).sort(compare));
  assert.equal(new Set(functions.map(row => JSON.stringify(row))).size, functions.length);
  assert.equal(new Set(branches.map(row => row[0])).size, branches.length);
  assert.equal(new Set(lines.map(row => row[0])).size, lines.length);
  const lineCount = code.split('\n').length - Number(code.endsWith('\n'));
  assert.deepEqual(lines.map(row => row[0]), Array.from({ length: lineCount }, (_, index) => index + 1));
  assert.ok(functions.length > 0 && branches.length > 0 && lines.length > 0);
  assert.equal(calls.filter(row => row[1] === 'run' && row[0] > 0).length, 1);
  for (const [key, expected] of [['FNF', functions.length], ['FNH', calls.filter(row => row[0] > 0).length],
    ['BRF', branches.length], ['BRH', branches.filter(row => row[1] > 0).length], ['LF', lines.length], ['LH', lines.filter(row => row[1] > 0).length]]) assert.equal(totals.get(key), expected);
  return { sourceFiles: 1, functions: functions.length, functionsHit: totals.get('FNH'),
    branches: branches.length, branchesHit: totals.get('BRH'), lines: lines.length, linesHit: totals.get('LH') };
}

/** Closed diagnostics only; never serialize private reporter lines or numbers from them. */
export function nativeSummaryObservation(text) {
  const rows = text.split(/\r?\n/);
  const expectedRows = Object.fromEntries([['tests', 1], ['suites', 0], ['pass', 1], ['fail', 0], ['cancelled', 0], ['skipped', 0], ['todo', 0]]
    .map(([name, count]) => [name, Math.min(2, rows.filter(row => row === `ℹ ${name} ${count}`).length)]));
  return { expectedRows, ansiEscapePresent: text.includes('\u001b[') };
}

export function validateNativeSummary(text) {
  assert.ok(Buffer.byteLength(text) <= 1024 * 1024);
  const rows = text.split(/\r?\n/);
  // Native spec coverage renders the top-level tests directory using this exact blank-column row.
  const directoryRows = rows.flatMap((row, index) => /^ℹ tests +\| +\| +\| +\| *$/u.test(row) ? [index] : []);
  assert.ok(directoryRows.length <= 1);
  const directory = directoryRows[0] ?? -1;
  const coverageStart = rows.indexOf('ℹ start of coverage report');
  if (directory !== -1) {
    const coverageEnd = rows.indexOf('ℹ end of coverage report');
    assert.ok(coverageStart > 0 && directory > coverageStart && coverageEnd > directory);
    assert.equal(rows.filter(row => row === 'ℹ start of coverage report').length, 1);
    assert.equal(rows.filter(row => row === 'ℹ end of coverage report').length, 1);
  }
  for (const [name, count] of [['tests', 1], ['suites', 0], ['pass', 1], ['fail', 0], ['cancelled', 0], ['skipped', 0], ['todo', 0]]) {
    const records = text.split(/\r?\n/).filter(row => row === `ℹ ${name} ${count}`);
    assert.equal(records.length, 1);
    assert.equal(rows.filter((row, index) => index !== directory && row.startsWith(`ℹ ${name} `)).length, 1);
    if (directory !== -1) assert.ok(rows.indexOf(`ℹ ${name} ${count}`) < coverageStart);
  }
}

function command(program, args, options = {}) {
  assert.ok(isAbsolute(program));
  const result = spawnSync(program, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 65536, ...options });
  assert.equal(result.status, 0); assert.equal(result.signal, null);
  return (result.stdout ?? '').trim();
}
export function trustedExecutorPaths(node, dotnetRoot) {
  // Roots observed in completed hosted Ubuntu job124375574849; no patch version is guessed.
  assert.match(node, /^\/opt\/hostedtoolcache\/node\/24\.[0-9]+\.[0-9]+\/x64\/bin\/node$/);
  assert.equal(dotnetRoot, '/usr/share/dotnet');
  return { node, npm: posix.join(posix.dirname(node), 'npm'), npmCli: posix.resolve(posix.dirname(node), '../lib/node_modules/npm/bin/npm-cli.js'),
    dotnet: '/usr/share/dotnet/dotnet', docker: '/usr/bin/docker', python: '/usr/bin/python3',
    git: '/usr/bin/git', bash: '/usr/bin/bash' };
}
async function executorProvenance(paths) {
  const result = {};
  for (const [name, path] of Object.entries(paths)) {
    const canonical = realpathSync(path);
    if (name === 'python') assert.match(canonical, /^\/usr\/bin\/python3\.[0-9]+$/);
    else if (name === 'npm') assert.equal(canonical, paths.npmCli);
    else assert.equal(canonical, path);
    const before = lstatSync(canonical);
    assert.ok(before.isFile() && !before.isSymbolicLink());
    const value = createHash('sha256');
    for await (const chunk of createReadStream(canonical)) value.update(chunk);
    const after = lstatSync(canonical);
    assert.equal(after.dev, before.dev); assert.equal(after.ino, before.ino);
    assert.equal(after.size, before.size); assert.equal(after.mtimeMs, before.mtimeMs);
    result[name] = { path, realpath: canonical, sha256: value.digest('hex') };
  }
  return result;
}
async function qualifyExecutors(env) {
  const paths = trustedExecutorPaths(process.execPath, process.env.DOTNET_ROOT);
  const provenance = await executorProvenance(paths);
  assert.ok(paths.node.includes(`/${process.version.slice(1)}/`));
  const npmVersion = JSON.parse(readFileSync(resolve(dirname(paths.npmCli), '../package.json'), 'utf8')).version;
  assert.equal(`npm@${npmVersion}`, JSON.parse(readFileSync('package.json', 'utf8')).packageManager);
  const versions = { node: process.version, npm: command(paths.node, [paths.npmCli, '--version'], { env }),
    dotnet: command(paths.dotnet, ['--version'], { env }), python: command(paths.python, ['--version'], { env }).replace(/^Python /, ''),
    docker: JSON.parse(command(paths.docker, ['version', '--format', '{{json .Client.Version}}'], { env })) };
  assert.equal(versions.npm, npmVersion);
  assert.equal(versions.dotnet, JSON.parse(readFileSync('global.json', 'utf8')).sdk.version);
  for (const version of Object.values(versions)) assert.match(version, /^v?[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]{1,32})?$/);
  return { paths, provenance, versions };
}
function ownedFile(file, maximum, privateMode = false, started = 0) {
  const stat = lstatSync(file);
  assert.ok(stat.isFile() && !stat.isSymbolicLink() && stat.nlink === 1 && stat.size > 0 && stat.size <= maximum);
  assert.equal(stat.uid, process.getuid());
  assert.ok(stat.mtimeMs >= started - 2000);
  if (privateMode) assert.equal(stat.mode & 0o777, 0o600);
  return readFileSync(file, 'utf8');
}
function sourceGuard(source) {
  assert.equal(command('/usr/bin/git', ['rev-parse', 'HEAD']), source);
  assert.equal(command('/usr/bin/git', ['status', '--porcelain', '--untracked-files=no']), '');
  for (const file of tools) assert.equal(hash(readFileSync(file)), hash(spawnSync('/usr/bin/git', ['cat-file', 'blob', `${source}:${file}`], { maxBuffer: 1024 * 1024 }).stdout));
  assert.equal(hash(readFileSync(actorFile)), frozenActor);
}
export function childCompletionObservation(value) {
  let signal = 'OTHER';
  if (value.signal === null) signal = 'NONE';
  else if (['SIGINT', 'SIGTERM', 'SIGKILL'].includes(value.signal)) signal = value.signal;
  return { exitCode: Number.isSafeInteger(value.exitCode) && value.exitCode >= 0 && value.exitCode <= 255 ? value.exitCode : null, signal };
}

async function invoke(program, args, env, fd, observeCompletion) {
  assert.ok(isAbsolute(program));
  const child = spawn(program, args, { env, stdio: ['ignore', fd, fd] });
  let interrupted = false;
  const interrupt = () => { interrupted = true; child.kill('SIGINT'); };
  const terminate = () => { interrupted = true; child.kill('SIGTERM'); };
  process.once('SIGINT', interrupt); process.once('SIGTERM', terminate);
  try {
    const result = await new Promise((done, reject) => { child.once('error', reject); child.once('close', (exitCode, signal) => done({ exitCode, signal })); });
    if (observeCompletion) observeCompletion({ exitCode: result.exitCode, signal: result.signal });
    assert.equal(interrupted, false);
    assert.equal(result.signal, null); assert.equal(result.exitCode, 0);
    return result;
  } finally { process.removeListener('SIGINT', interrupt); process.removeListener('SIGTERM', terminate); }
}
async function run() {
  let phase = 'ADMISSION', fd, outputOwned = false, nativeSummary, buildCleanChild, validatedLcov;
  const output = resolve('artifacts/sonar-actor');
  let receipt = { schemaVersion: 1, profile, canonicalRuntime: false, deployable: false, qualification: 'INCOMPLETE', phase, release: 'RELEASE_NO_GO' };
  try {
    assert.equal(process.argv.length, 2); assert.equal(process.platform, 'linux');
    assert.ok(!existsSync(output) && !lstatSync('artifacts').isSymbolicLink());
    mkdirSync(output, { mode: 0o700 }); outputOwned = true;
    assert.ok(!existsSync('.sonarqube'));
    assert.ok(!process.env.SONAR_TOKEN && !process.env.SYNCFUSION_LICENSE);
    const source = process.env.GITHUB_SHA, imageName = `coglatas-sonar-actor-measurement:${source}`;
    const tree = command('/usr/bin/git', ['rev-parse', 'HEAD^{tree}']);
    const admission = { repository: process.env.GITHUB_REPOSITORY, owner: process.env.GITHUB_REPOSITORY_OWNER,
      actor: process.env.GITHUB_ACTOR, triggeringActor: process.env.GITHUB_TRIGGERING_ACTOR,
      eventName: process.env.GITHUB_EVENT_NAME, ref: process.env.GITHUB_REF,
      event: JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')), source, tree,
      checkout: command('/usr/bin/git', ['rev-parse', 'HEAD']), checkoutTree: tree,
      runId: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT,
      analysisKind: process.env.SONAR_ANALYSIS_KIND, prNumber: process.env.SONAR_PR_NUMBER || '',
      prBranch: process.env.SONAR_PR_BRANCH || '', prBase: process.env.SONAR_PR_BASE || '', imageName };
    validateMeasurementAdmission(admission); sourceGuard(source);
    const control = mkdtempSync(join(resolve(process.env.RUNNER_TEMP), 'sonar-actor-'));
    for (const name of ['home', 'tmp', 'diagnostics', 'runtime']) mkdirSync(join(control, name), { mode: 0o700 });
    const runtime = join(control, 'runtime'), binding = join(runtime, 'measurement-binding.json');
    const password = `Actor!${randomBytes(24).toString('hex')}`, ownerPassword = `Owner!${randomBytes(24).toString('hex')}`;
    for (const value of [password, `${password}:recipient`, `${password}:operator`, ownerPassword]) console.log(`::add-mask::${value}`);
    // Allowlist host process environment; no scanner/license/remote-daemon credentials enter build or Compose.
    const env = Object.fromEntries(['PATH', 'HOME', 'LANG', 'DOTNET_ROOT', 'NUGET_PACKAGES', 'GITHUB_WORKSPACE'].filter(key => process.env[key]).map(key => [key, process.env[key]]));
    Object.assign(env, { CI: 'true', RUNNER_TEMP: resolve(process.env.RUNNER_TEMP), GITHUB_SHA: source, GITHUB_RUN_ID: admission.runId, GITHUB_RUN_ATTEMPT: admission.attempt,
      TMPDIR: join(control, 'tmp'), DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' });
    phase = 'TOOLS';
    const executors = await qualifyExecutors(env);
    env.PATH = [dirname(executors.paths.node), dirname(executors.paths.dotnet), '/usr/bin', '/bin'].join(':');
    phase = 'BUILD'; fd = openSync(join(control, 'build.private.log'), 'wx', 0o600);
    await invoke('/usr/bin/bash', ['scripts/ci/package-sonar-actor-measurement.sh', runtime], env, fd);
    closeSync(fd); fd = undefined;
    sourceGuard(source);
    const observedImage = ownedFile(join(runtime, 'runtime-image-id'), 80).trim();
    const engineImage = command(executors.paths.docker, ['image', 'inspect', '--format', '{{.Id}}', imageName], { env });
    assert.match(observedImage, digest); assert.match(engineImage, digest);
    const stamp = resolve('artifacts/ci/dotnet-build-sha');
    const identityArgs = ['--archive', join(runtime, 'runtime-image.tar.gz'), '--source-sha', source, '--image-name', imageName,
      '--engine-id', engineImage, '--binding', binding, '--dotnet-stamp', stamp];
    command(executors.paths.python, ['-B', 'scripts/ci/sonar_actor_image_identity.py', 'record', ...identityArgs], { env });
    const proof = JSON.parse(ownedFile(binding, 65536));
    const configImage = proof.archiveConfigImageId;
    assert.match(configImage, digest);
    assert.equal(proof.producerObservedImageId, observedImage); assert.equal(proof.loadedEngineImageId, engineImage);
    Object.assign(env, { ACTOR_UID: String(process.getuid()), ACTOR_GID: String(process.getgid()), ACTOR_CONTROL_DIR: control,
      TARGET_SHA: source, COGLATAS_BROWSER_SMOKE_EMAIL: 'e2e-user@example.test', COGLATAS_BROWSER_SMOKE_PASSWORD: password,
      COGLATAS_SEED_ADMIN_PASSWORD: ownerPassword, SYNCFUSION_LICENSE: 'artifact-reuse-no-build',
      FUNCTIONAL_COMPOSE_PROJECT_NAME: `coglatas-sonar-actor-${admission.runId}-${admission.attempt}`,
      FUNCTIONAL_COMPOSE_FILES: 'infra/compose/test/real-backend-smoke.yml,infra/compose/test/actor-replacement.yml,infra/compose/test/sonar-actor-measurement.yml',
      COGLATAS_FUNCTIONAL_TOOL_SHA: source, FUNCTIONAL_DIAGNOSTIC_DIR: join(control, 'diagnostics'),
      COGLATAS_RUNTIME_MEASUREMENT_PROFILE: 'sonar-actor-v1', COGLATAS_APP_IMAGE: imageName,
      COGLATAS_REUSE_PREBUILT_APP_IMAGE: '1', COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID: configImage,
      COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID: engineImage, COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA: source,
      COGLATAS_REUSE_PREBUILT_APP_IMAGE_ARCHIVE: join(runtime, 'runtime-image.tar.gz'),
      COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING: binding, COGLATAS_REUSE_PREBUILT_DOTNET_STAMP: stamp,
      COGLATAS_REUSE_PREBUILT_DOTNET_BUILD: 'true' });
    const hashes = Object.fromEntries([['harnessSha256', 'functional-compose-harness.sh'], ['helperSha256', 'acceptance-image-reuse.sh'],
      ['verifierSha256', 'runtime_image_identity.py']].map(([key, file]) => [key, hash(readFileSync(`scripts/ci/${file}`))]));
    phase = 'HARNESS'; fd = openSync(join(control, 'harness.private.log'), 'wx', 0o600);
    const started = Date.now();
    const completion = await invoke('/usr/bin/bash', ['scripts/ci/functional-compose-harness.sh'], env, fd);
    closeSync(fd); fd = undefined;
    phase = 'VALIDATION_SOURCE'; sourceGuard(source);
    phase = 'VALIDATION_IMAGE_BINDING';
    command(executors.paths.python, ['-B', 'scripts/ci/sonar_actor_image_identity.py', 'verify', ...identityArgs, '--config-id', configImage], { env });
    phase = 'VALIDATION_IMAGE_RECEIPT';
    const records = ownedFile(join(control, 'harness.private.log'), 16 * 1024 * 1024, true, started).split(/\r?\n/).filter(row => row.startsWith('FUNCTIONAL_IMAGE_REUSE '));
    assert.equal(records.length, 1);
    phase = 'VALIDATION_JOURNEY_FILE';
    const journey = JSON.parse(ownedFile(join(control, 'result.json'), 65536, true, started));
    phase = 'VALIDATION_JOURNEY';
    const finite = validateCompletion({ ...completion, journey, image: JSON.parse(records[0].slice(23)),
      admission: { source, producerImage: configImage, engineImage }, expectedHashes: hashes, started });
    phase = 'VALIDATION_NATIVE_SUMMARY_FILE';
    const summary = ownedFile(join(control, 'native.spec.private.log'), 1024 * 1024, false, started);
    nativeSummary = nativeSummaryObservation(summary);
    phase = 'VALIDATION_NATIVE_SUMMARY_PARSE';
    validateNativeSummary(summary);
    phase = 'VALIDATION_NATIVE_LCOV';
    const lcov = ownedFile(join(control, 'actor-native.lcov'), 1024 * 1024, false, started);
    const coverage = validateActorCoverage(lcov, readFileSync(actorFile, 'utf8'));
    phase = 'VALIDATED_LCOV_WRITE';
    writeFileSync(join(output, 'lcov.info'), lcov, { flag: 'wx', mode: 0o600 });
    validatedLcov = { sourceSha: source, scenarioSha256: frozenActor, lcovSha256: hash(Buffer.from(lcov)), coverage };
    phase = 'PRE_ANALYSIS_CLEAN_FILE'; fd = openSync(join(control, 'clean.private.log'), 'wx', 0o600);
    // A pre-analysis build must not make the existing begin/build/end interval incremental.
    phase = 'PRE_ANALYSIS_CLEAN_EXECUTION';
    await invoke(executors.paths.dotnet, ['clean', 'Coglatas.slnx', '--configuration', 'Release', '--verbosity', 'quiet'], env, fd,
      value => { buildCleanChild = childCompletionObservation(value); });
    closeSync(fd); fd = undefined;
    phase = 'POST_CLEAN_SOURCE'; sourceGuard(source);
    phase = 'POST_CLEAN_EXECUTORS';
    assert.deepEqual(await executorProvenance(executors.paths), executors.provenance);
    phase = 'FINAL_RECEIPT';
    receipt = { schemaVersion: 1, profile, canonicalRuntime: false, deployable: false, qualification: 'BOUNDED_MEASUREMENT_PASS',
      eventName: 'push', ref: admission.ref, sourceSha: source, sourceTree: tree, runId: admission.runId, attempt: admission.attempt,
      analysisKind: admission.analysisKind, analysisPrNumber: admission.prNumber || null, imageName, producerObservedImageId: observedImage,
      archiveConfigImageId: configImage, loadedEngineImageId: engineImage, archiveSha256: proof.archiveSha256, imageBindingSha256: hash(readFileSync(binding)),
      toolSha256: Object.fromEntries(tools.map(file => [file, hash(readFileSync(file))])), buildTools: executors.versions,
      executableProvenance: executors.provenance,
      ...finite, implicitFileTestsPassed: 1, criteriaPassed: 17, coverage, lcovSha256: hash(Buffer.from(lcov)),
      imageBeforeAfterVerified: true, cleanupSucceeded: true, release: 'RELEASE_NO_GO' };
    process.exitCode = 0;
  } catch {
    receipt = { schemaVersion: 1, profile, canonicalRuntime: false, deployable: false, qualification: 'INCOMPLETE', phase,
      ...(nativeSummary ? { nativeSummary } : {}), ...(buildCleanChild ? { buildCleanChild } : {}),
      ...(validatedLcov ? { validatedLcov } : {}), release: 'RELEASE_NO_GO' };
    process.exitCode = 1;
  }
  finally {
    if (fd !== undefined) closeSync(fd);
    if (outputOwned) {
      writeFileSync(join(output, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
      appendFileSync(process.env.GITHUB_OUTPUT, 'receipt_ready=true\n');
    }
  }
  console.log(`Actor measurement: ${receipt.qualification}; noncanonical, nondeployable; RELEASE_NO_GO.`);
}
if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) await run();
