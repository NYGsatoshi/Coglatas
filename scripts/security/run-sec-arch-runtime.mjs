import { spawn } from 'node:child_process';
import { randomUUID, createHash } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, resolve, relative, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { platform, arch } from 'node:os';
import { runtimeArguments, preparePrivateDirectory, postgresStorageArguments, verifyPostgresStorage } from './sec-arch-runtime-options.mjs';
import { captureRuntimeAssemblyBinding, unverifiedRuntimeAssemblyBinding } from './sec-arch-runtime-assemblies.mjs';

// Local qualification owns its PostgreSQL environment; it cannot accept an external connection string.
const image = 'postgres@sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873';
const pythonImage = 'python@sha256:2d9aefe2fef018a7eb2c13064c89c71929800fd2e5dccdbf52ea5da5bb8d929a';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const reusedHttpMethods = [
  'TaskDetailHttpContractUsesCanonicalRoutesSafeErrorsAndBoundedAggregate',
  'TaskActivityHttpContractIsIndependentBoundedStableAndFailClosed',
  'RevokedTaskCommentAuthorReceivesSafeForbiddenForCanonicalUpdateAndDelete',
  'CommunicationBodiesStayParticipantScopedAndDeniedResponsesAreGeneric',
  'PrivateWorkspaceSharingReauthorizesApiReadsAndDoesNotLeakProtectedSharingMetadata',
  'FileMetadataAndDeniedResponsesDoNotExposeStorageIdentifiers',
  'WorkspaceFileDeleteCapabilityAndDirectMutationRemainOwnerScoped',
  'MessageThreadAuthorityRequiresReadPostAndCreateThreadWithoutLeakingSummary',
  'ProjectCreateOptionsFailClosedAfterMembershipOrWorkspaceDeactivation',
  'CanonicalTaskCreateRoutesResolveThroughTheInProcessHostAndPreserveSafeTenantBoundaries',
];
const reusedHttpTheoryMethods = [
  'AdminInvitesDenyTenantOwnersAndRestrictedMembersWithoutDisclosingInvites',
  'AdminInvitesPreserveAuthorizedEmailProjectionAndTenantIsolation',
];
const runtimeFilter = ['FullyQualifiedName~Coglatas.Tests.SecurityArchitecture',
  'FullyQualifiedName~Coglatas.Tests.PostgreSql.OutboxReplayPostgreSqlTests',
  ...[...reusedHttpMethods, ...reusedHttpTheoryMethods].map(method => 'FullyQualifiedName=Coglatas.Tests.Tenancy.HttpTenantIsolationTests.' + method)].join('|');
let options;
try { options = runtimeArguments(process.argv.slice(2)); }
catch { console.error('Fixture CLI arguments are invalid'); process.exit(2); }
if (options.development || options.candidateSha === undefined) {
  console.error('Runtime fixture requires --candidate-sha and a clean checkout'); process.exit(2);
}
const reportPath = resolve(options.report === 'artifacts/sec-arch/kafka.json' ? 'artifacts/sec-arch/runtime.json' : options.report);
const outputRelative = relative(resolve(root, 'artifacts/sec-arch'), reportPath);
if (outputRelative.startsWith('..') || isAbsolute(outputRelative) || !outputRelative.endsWith('.json')) {
  console.error('Fixture report must be a JSON artifact inside artifacts/sec-arch'); process.exit(2);
}
const nonce = randomUUID().replaceAll('-', '');
const container = 'coglatas-sec-arch-runtime-' + nonce;
const network = 'coglatas-sec-arch-runtime-' + nonce;
const password = randomUUID();
const trxPath = `artifacts/sec-arch/runtime-${nonce}.trx`;
let created = false;
let networkCreated = false;
let outcome = 'ERROR';
let observation = null;
let environment = null;
let assemblyBinding = unverifiedRuntimeAssemblyBinding();
let candidateVerified = false;
let cleanupVerified = true;
let stage = 'candidate validation';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');

async function command(executable, args, env = process.env, timeout = 60000) {
  return await new Promise((resolveResult, reject) => {
    const child = spawn(executable, args, { cwd: root, env, windowsHide: true,
      detached: process.platform !== 'win32', stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === 'win32') spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'],
        { windowsHide: true, stdio: 'ignore' });
      else { try { process.kill(-child.pid, 'SIGKILL'); } catch { /* Already exited. */ } }
    }, timeout);
    const collect = bytes => { if (output.length < 1024 * 1024) output += bytes.toString(); };
    child.stdout.on('data', collect);
    child.stderr.on('data', collect);
    child.on('error', () => { clearTimeout(timer); reject(new Error('Fixture executable unavailable')); });
    child.on('close', code => { clearTimeout(timer); resolveResult({ code, output, timedOut }); });
  });
}
const docker = args => command('docker', args);
const requireSuccess = result => {
  if (result.code !== 0 || result.timedOut) throw new Error('Fixture command failed');
  return result.output.trim();
};
async function cleanCandidate() {
  return requireSuccess(await command('git', ['rev-parse', 'HEAD'])) === options.candidateSha &&
    requireSuccess(await command('git', ['status', '--porcelain'])) === '' &&
    resolve(requireSuccess(await command('git', ['rev-parse', '--show-toplevel']))) === root;
}

try {
  if (!await cleanCandidate()) throw new Error('Fixture exact clean candidate required');
  let privateDirectory;
  if (options.privateDirectory !== undefined) {
    stage = 'private disclosure boundary';
    // Resolve filesystem aliases and reject another Git checkout before creating exclusive private output.
    privateDirectory = await preparePrivateDirectory(root, options.privateDirectory,
      parent => command('git', ['-C', parent, 'rev-parse', '--show-toplevel']));
  }
  // Do not silently overwrite a retained receipt.
  try { await readFile(reportPath); throw new Error('Fixture report already exists'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  stage = 'isolated PostgreSQL setup';
  // Internal networks cannot publish the host port on the supported Docker Desktop engine.
  // A dedicated bridge publishes only loopback; masquerading and peer communication are disabled.
  requireSuccess(await docker(['network', 'create', '--driver', 'bridge',
    '--opt', 'com.docker.network.bridge.enable_ip_masquerade=false',
    '--opt', 'com.docker.network.bridge.enable_icc=false', '--label', `coglatas.fixture=${nonce}`, network]));
  networkCreated = true;
  requireSuccess(await docker(['run', '--detach', '--name', container, '--network', network,
    '--label', `coglatas.fixture=${nonce}`, '--publish', '127.0.0.1::5432',
    ...postgresStorageArguments(options.postgresStorage),
    '--env', 'POSTGRES_DB=sec_arch_fixture', '--env', 'POSTGRES_USER=sec_arch_migration',
    '--env', `POSTGRES_PASSWORD=${password}`, image]));
  created = true;
  let ready = false;
  for (let attempt = 0; attempt < 30; attempt++) {
    const result = await docker(['exec', container, 'pg_isready', '--username=sec_arch_migration', '--dbname=sec_arch_fixture']);
    if (result.code === 0) { ready = true; break; }
    await new Promise(resolveWait => setTimeout(resolveWait, 500));
  }
  if (!ready) throw new Error('Fixture PostgreSQL readiness failed');
  const port = requireSuccess(await docker(['port', container, '5432/tcp']));
  if (!/^127\.0\.0\.1:\d+$/.test(port)) throw new Error('Fixture loopback binding invalid');
  stage = 'isolated PostgreSQL storage identity';
  const inspection = JSON.parse(requireSuccess(await docker(['inspect', container])))[0];
  const filesystemType = requireSuccess(await docker(['exec', container, 'stat', '-f', '-c', '%T', '/var/lib/postgresql']));
  const mountType = verifyPostgresStorage(options.postgresStorage, inspection, filesystemType);
  const durabilitySettings = requireSuccess(await docker(['exec', container, 'psql', '-U', 'sec_arch_migration',
    '-d', 'sec_arch_fixture', '-Atc', "SELECT current_setting('fsync') || '|' || current_setting('full_page_writes')"]));
  if (durabilitySettings !== 'on|on') throw new Error('Fixture PostgreSQL durability settings differ');
  environment = { platform: platform(), architecture: arch(), postgresImage: image,
    postgresVersion: requireSuccess(await docker(['exec', container, 'psql', '-U', 'sec_arch_migration', '-d', 'sec_arch_fixture', '-Atc', 'SHOW server_version'])),
    dotnetSdk: requireSuccess(await command('dotnet', ['--version'])), fixture: 'SEC02_SYNTHETIC',
    postgresStorage: { mode: options.postgresStorage, mountType, filesystemType,
      maximumBytes: options.postgresStorage === 'tmpfs' ? 2147483648 : null,
      fsync: 'on', fullPageWrites: 'on', crashRecoveryQualified: false } };
  const testEnvironment = { ...process.env,
    POSTGRES_TEST_CONNECTION_STRING: `Host=127.0.0.1;Port=${port.split(':')[1]};Database=sec_arch_fixture;Username=sec_arch_migration;Password=${password}`,
    COGLATAS_TEST_USE_MIGRATED_TEMPLATE: 'true', DOTNET_CLI_UI_LANGUAGE: 'en-US',
    DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' };
  delete testEnvironment.COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY;
  testEnvironment.COGLATAS_SEC_ARCH_CANDIDATE_SHA = options.candidateSha;
  if (privateDirectory !== undefined)
    testEnvironment.COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY = privateDirectory;
  stage = 'exact-candidate build';
  console.log('SEC-ARCH runtime: isolated PostgreSQL ready; compiling exact candidate');
  requireSuccess(await command('dotnet', ['build', 'tests/Coglatas.Tests/Coglatas.Tests.csproj', '--configuration', 'Release'], testEnvironment, 600000));
  if (!await cleanCandidate()) throw new Error('Fixture candidate changed during build');
  await mkdir(resolve(root, 'artifacts/ci'), { recursive: true });
  await writeFile(resolve(root, 'artifacts/ci/dotnet-build-sha'), options.candidateSha + '\n');
  console.log('SEC-ARCH runtime: executing required controls');
  stage = 'runtime execution';
  const tests = await command('dotnet', ['test', 'tests/Coglatas.Tests/Coglatas.Tests.csproj', '--configuration', 'Release',
    '--no-build', '--no-restore', '--filter', runtimeFilter,
    '--logger', `trx;LogFileName=runtime-${nonce}.trx`, '--results-directory', 'artifacts/sec-arch'], testEnvironment, 600000);
  stage = 'execution evidence parsing';
  const observed = requireSuccess(await docker(['run', '--rm', '--network', 'none',
    '--mount', `type=bind,source=${root},target=/repo,readonly`, '--workdir', '/repo', '--env', 'PYTHONDONTWRITEBYTECODE=1',
    pythonImage, 'python', '-c',
    "import sys,json;sys.path.insert(0,'scripts/ci');from sec_arch_evidence import observed_trx;from pathlib import Path;from datetime import datetime,timezone;print(json.dumps(observed_trx(Path(sys.argv[1]).read_bytes(),datetime.now(timezone.utc))))", trxPath]));
  observation = JSON.parse(observed);
  outcome = tests.code === 0 && !tests.timedOut ? observation.outcome : 'FAIL';
  stage = 'six-assembly producer and loaded dependency binding';
  assemblyBinding = await captureRuntimeAssemblyBinding(root);
  candidateVerified = await cleanCandidate();
} catch {
  outcome = 'ERROR';
  console.error(`SEC-ARCH runtime ERROR during ${stage}; raw diagnostics omitted`);
} finally {
  if (created) {
    const label = await docker(['inspect', '--format', '{{index .Config.Labels "coglatas.fixture"}}', container]);
    if (label.code === 0 && label.output.trim() === nonce)
      cleanupVerified &&= (await docker(['rm', '--force', '--volumes', container])).code === 0;
    else cleanupVerified = false;
  }
  if (networkCreated) {
    const label = await docker(['network', 'inspect', '--format', '{{index .Labels "coglatas.fixture"}}', network]);
    if (label.code === 0 && label.output.trim() === nonce)
      cleanupVerified &&= (await docker(['network', 'rm', network])).code === 0;
    else cleanupVerified = false;
  }
  if (!cleanupVerified) outcome = 'ERROR';
  if (!await cleanCandidate()) candidateVerified = false;
  const report = { ...assemblyBinding, verifierId: 'SEC-ARCH-RUNTIME-ISOLATED',
    candidateSha: options.candidateSha, candidateVerified, cleanupVerified,
    environmentFingerprint: environment ? digest(JSON.stringify(environment)) : null,
    environment, executedAtUtc: new Date().toISOString(),
    executionDigest: observation ? digest(await readFile(resolve(root, trxPath))) : null,
    outcome: outcome === 'PASS' && !candidateVerified ? 'UNVERIFIED' : outcome, observation,
    qualification: 'LOCAL_REPRESENTATIVE_ONLY', trustedAttestation: 'UNVERIFIED', ownerApproval: null,
    preAvaloniaVerdict: 'PRE-AVALONIA SEC-ARCH: BLOCKED' };
  await mkdir(dirname(reportPath), { recursive: true });
  await writeFile(reportPath, JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  console.log(`SEC-ARCH runtime: ${report.outcome}; pre-Avalonia BLOCKED`);
  process.exitCode = report.outcome === 'PASS' && cleanupVerified ? 0 : 1;
}
