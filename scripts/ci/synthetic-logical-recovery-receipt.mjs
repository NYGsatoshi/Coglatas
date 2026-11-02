import { execFileSync } from 'node:child_process';
import { appendFileSync, closeSync, constants, fstatSync, lstatSync, mkdirSync, openSync, readSync, realpathSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = realpathSync(resolve(dirname(fileURLToPath(import.meta.url)), '../..'));
const PREFIX = 'artifacts/synthetic-logical-recovery/';
const MAX_RECEIPT_BYTES = 64 * 1024;
const sha = value => typeof value === 'string' && /^[a-f0-9]{40}$/.test(value);
const digest = value => typeof value === 'string' && /^[a-f0-9]{64}$/.test(value);
const boolean = value => typeof value === 'boolean';
const count = value => Number.isSafeInteger(value) && value >= 0;
const oneOf = (...values) => value => values.includes(value);
const nullable = check => value => value === null || check(value);
const fixed = value => oneOf(value);
const object = (value, fields, required = Object.keys(fields)) => value !== null && typeof value === 'object' &&
  !Array.isArray(value) && required.every(key => Object.hasOwn(value, key)) &&
  Object.entries(value).every(([key, child]) => Object.hasOwn(fields, key) && fields[key](child));
const fields = (names, check) => Object.fromEntries(names.map(name => [name, check]));
const components = ['schema', 'rows', 'catalog', 'sequences', 'uploads', 'keys'];
const catalog = ['relations', 'constraints', 'indexes', 'triggers', 'functions', 'policies', 'schemas', 'defaults', 'roles', 'memberships', 'migrations'];
const comparison = value => object(value, {
  components: child => object(child, fields(components, boolean)),
  catalog: child => object(child, fields(catalog, boolean)),
});
const seed = value => object(value, { runtimeEnvironment: fixed('Test'), appMode: fixed('SaaS'),
  ...fields(['fixture', 'tenancy', 'uiShell', 'administrator', 'browser', 'demo'], fixed(false)) });
const image = value => typeof value === 'string' && /^sha256:[a-f0-9]{64}$/.test(value);
const stages = ['source', 'build', 'migration-tool-restore', 'migration-project-restore', 'migration-bundle-build',
  'source-seed', 'checkpoint', 'restore-list-plan', 'restore-target-prepare', 'restore-postgres', 'restore-version',
  'restore-upload-extract', 'restore-key-extract', 'restore-checkpoint', 'startup', 'target-probes', 'fault', 'rollback'];

function require(condition) {
  if (!condition) throw new Error('Synthetic logical recovery evidence rejected.');
}

/** Accept only the existing writer's closed metadata, including genuine failure receipts. */
export function validateRecoveryReceiptData(value) {
  const schema = {
    schemaVersion: fixed(1), kind: fixed('SYNTHETIC_LOGICAL_RECOVERY_MECHANISM'),
    runId: child => typeof child === 'string' && /^[1-9][0-9]{0,19}$/.test(child), attempt: fixed(1), sourceSha: sha,
    receiptNonce: child => typeof child === 'string' && /^[a-f0-9]{12}$/.test(child), admittedSourceTree: sha,
    githubJob: child => typeof child === 'string' && /^[A-Za-z_][A-Za-z0-9_-]{0,99}$/.test(child),
    jobIdReconciliation: fixed('REQUIRES_PROVIDER_METADATA'), trustedAttestation: fixed('UNVERIFIED'),
    status: oneOf('PASS', 'ERROR'), testedCombinedTree: nullable(sha),
    specificationBaseline: fixed('850a5d9376db11e75235cf55e03ebed4de9c0510'), firstAttemptOnly: fixed(true),
    runtimeMode: fixed('Test/SaaS'), publicPorts: fixed(false), restoredTargetMigrations: fixed(false),
    postgresVersionNum: nullable(count), appImage: nullable(image), postgresImage: nullable(image),
    restoreCalls: child => object(child, fields(['target', 'fault'], oneOf(0, 1))),
    helperImage: fixed('node@sha256:e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413'),
    migrationSdkImage: fixed('mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:afd0ccff9d306812a69ef0f47458376b6acb544ae866cfc4b8d28ff0eef35ea7'),
    strictCheckpoint: oneOf('PASS', 'NOT_EVALUATED'),
    startupDelta: child => child === 'NOT_EVALUATED' || object(child, {
      equalToCheckpoint: boolean, changedTables: count, schemaUnchanged: boolean }),
    api: child => child === 'NOT_EVALUATED' || object(child, {
      phase: fixed('verify'), status: fixed('PASS'), actualActors: fixed(3), currentCookieIdentities: fixed(2),
      fileByteChecks: fixed(2), crossTenantDenials: fixed(2), revokedSessionDenied: fixed(true),
      freshLoginLogout: fixed(true), deploymentHttps: fixed('NOT_EVALUATED') }),
    ...fields(['faultSourcePreserved', 'originalSourceRollback', 'cleanup', 'corruptTargetQuarantined'], boolean),
    rolesAndRlsQualification: fixed('NOT_EVALUATED'), productionHttps: fixed('NOT_EVALUATED'),
    technicalReleaseReady: fixed(false), studentCouncilPilot: fixed('RELEASE_NO_GO'), privateEvidenceUploaded: fixed(false),
    seedFlags: child => object(child, { initialSourceFixture: fixed(true), sourceCheckpoint: seed, target: seed, fault: seed }, []),
    ...fields(['migrationBundleSha256', 'databaseDumpSha256', 'checkpointDigest'], digest),
    restoreList: child => object(child, { entryCount: count, foreignKeyCount: count, originalSha256: digest, plannedSha256: digest }),
    strictCheckpointComparison: comparison, startupCheckpointComparison: comparison,
    corruptArchiveRejectedByPgRestore: fixed(true), componentDigests: child => object(child, fields(components, digest)),
    tableCount: count, failedStage: oneOf(...stages),
    failureCode: child => typeof child === 'string' && /^[A-Z_]{1,64}$/.test(child),
    commandExitCode: child => count(child) && child <= 255, cleanupError: fixed('OWNED_CLEANUP_FAILED'),
  };
  const optional = new Set(['migrationBundleSha256', 'databaseDumpSha256', 'checkpointDigest', 'restoreList',
    'strictCheckpointComparison', 'startupCheckpointComparison', 'corruptArchiveRejectedByPgRestore',
    'componentDigests', 'tableCount', 'failedStage', 'failureCode', 'commandExitCode', 'cleanupError']);
  require(object(value, schema, Object.keys(schema).filter(key => !optional.has(key))));
  require(value.testedCombinedTree === value.admittedSourceTree || (value.status === 'ERROR' && value.testedCombinedTree === null));
  if (value.status === 'PASS') validatePassedReceipt(value);
  return value;
}

function validatePassedReceipt(value) {
  require(['migrationBundleSha256', 'databaseDumpSha256', 'checkpointDigest', 'restoreList', 'componentDigests', 'tableCount']
    .every(key => Object.hasOwn(value, key)));
  require(value.restoreCalls.target === 1 && value.restoreCalls.fault === 1 && value.strictCheckpoint === 'PASS' &&
    value.startupDelta.equalToCheckpoint === true && value.startupDelta.changedTables === 0 && value.startupDelta.schemaUnchanged === true);
  require(value.api.status === 'PASS' && value.faultSourcePreserved && value.originalSourceRollback && value.cleanup &&
    value.corruptTargetQuarantined && value.corruptArchiveRejectedByPgRestore === true);
  for (const checkpoint of ['strictCheckpointComparison', 'startupCheckpointComparison']) {
    require(value[checkpoint] && ['components', 'catalog'].every(group => Object.values(value[checkpoint][group]).every(child => child === true)));
  }
  require(!Object.hasOwn(value, 'failedStage') && !Object.hasOwn(value, 'failureCode') && !Object.hasOwn(value, 'cleanupError'));
}

function directory(path, privateOnly = false) {
  const info = lstatSync(path);
  require(info.isDirectory() && !info.isSymbolicLink() && realpathSync(path) === path &&
    info.uid === process.getuid() && (info.mode & (privateOnly ? 0o077 : 0o022)) === 0);
}

function output(env, values) {
  const path = env.GITHUB_OUTPUT;
  require(isAbsolute(path ?? '') && realpathSync(path) === path);
  const before = lstatSync(path);
  require(before.isFile() && !before.isSymbolicLink() && before.uid === process.getuid());
  const fd = openSync(path, constants.O_WRONLY | constants.O_APPEND | constants.O_NOFOLLOW);
  try {
    const opened = fstatSync(fd);
    require(opened.isFile() && opened.dev === before.dev && opened.ino === before.ino);
    appendFileSync(fd, values);
  } finally { closeSync(fd); }
}

function identityMatches(receipt, identity, env) {
  require(receipt.runId === identity.runId && receipt.attempt === identity.attempt && receipt.sourceSha === identity.sourceSha &&
    receipt.githubJob === env.GITHUB_JOB && receipt.admittedSourceTree === env.COGLATAS_RECOVERY_SOURCE_TREE);
}

function git(root, args) {
  return execFileSync('/usr/bin/git', ['-C', root, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

/** Called only after the original hostedContract admits the workflow identity. No Docker or dependency setup occurs here. */
export function prepareRecoveryWorkflow(identity, env = process.env, { root = ROOT } = {}) {
  require(env.COGLATAS_RECOVERY_REUSE_MAIN_ARTIFACTS === 'false' && identity.attempt === 1 &&
    identity.runId === env.GITHUB_RUN_ID && identity.sourceSha === env.GITHUB_SHA);
  require(realpathSync(env.GITHUB_WORKSPACE ?? '') === root && git(root, ['rev-parse', 'HEAD']) === identity.sourceSha &&
    !git(root, ['status', '--porcelain', '--untracked-files=all']));
  const tree = git(root, ['rev-parse', 'HEAD^{tree}']); require(sha(tree));
  output(env, 'admitted=true\nsource_tree=' + tree + '\n');
  return tree;
}

/** The original nonce-owned writer remains exclusive; existing matching files are never selected. */
export function writeRecoveryReceipt(receipt, identity, env = process.env, { root = ROOT } = {}) {
  const text = JSON.stringify(receipt, null, 2) + '\n';
  validateRecoveryReceiptData(JSON.parse(text)); identityMatches(receipt, identity, env);
  const artifacts = join(root, 'artifacts');
  try { mkdirSync(artifacts, { mode: 0o700 }); } catch (error) { if (error.code !== 'EEXIST') throw error; }
  directory(artifacts);
  const parent = join(artifacts, 'synthetic-logical-recovery');
  try { mkdirSync(parent, { mode: 0o700 }); } catch (error) { if (error.code !== 'EEXIST') throw error; }
  directory(parent);
  const folder = join(parent, receipt.runId + '-1-' + receipt.receiptNonce);
  mkdirSync(folder, { mode: 0o700 }); directory(folder, true);
  const path = join(folder, 'receipt.json');
  writeFileSync(path, text, { flag: 'wx', mode: 0o600 });
  const selected = relative(root, path).replaceAll('\\', '/');
  output(env, 'receipt_path=' + selected + '\n');
  return selected;
}

function readReceipt(path) {
  const before = lstatSync(path);
  require(before.isFile() && !before.isSymbolicLink() && before.nlink === 1 && before.uid === process.getuid() &&
    (before.mode & 0o077) === 0 && before.size > 0 && before.size <= MAX_RECEIPT_BYTES);
  const fd = openSync(path, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  try {
    const opened = fstatSync(fd);
    require(opened.isFile() && opened.dev === before.dev && opened.ino === before.ino && opened.size === before.size);
    const bytes = Buffer.alloc(before.size + 1); let size = 0;
    while (size < bytes.length) { const read = readSync(fd, bytes, size, bytes.length - size, null); if (!read) break; size += read; }
    const after = fstatSync(fd);
    require(size === before.size && after.size === before.size && after.mtimeMs === before.mtimeMs && after.ctimeMs === before.ctimeMs);
    const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes.subarray(0, size));
    const receipt = JSON.parse(text);
    require(text === JSON.stringify(receipt, null, 2) + '\n');
    return validateRecoveryReceiptData(receipt);
  } finally { closeSync(fd); }
}

/** Validate only the current runner-emitted path; never enumerate a checkout glob or manufacture a success verdict. */
export function validateRecoveryReceipt(identity, env = process.env, { root = ROOT } = {}) {
  require(env.COGLATAS_RECOVERY_REUSE_MAIN_ARTIFACTS === 'false' && identity.attempt === 1 &&
    /^[1-9][0-9]{0,19}$/.test(identity.runId ?? '') && sha(identity.sourceSha));
  const selected = env.COGLATAS_RECOVERY_RECEIPT_PATH;
  const expected = new RegExp('^' + PREFIX + identity.runId + '-1-([a-f0-9]{12})/receipt\\.json$');
  const match = typeof selected === 'string' && expected.exec(selected); require(match);
  directory(join(root, 'artifacts')); directory(join(root, 'artifacts', 'synthetic-logical-recovery'));
  directory(join(root, dirname(selected)), true);
  const receipt = readReceipt(join(root, selected)); identityMatches(receipt, identity, env);
  require(receipt.receiptNonce === match[1] && sha(env.COGLATAS_RECOVERY_SOURCE_TREE) &&
    git(root, ['rev-parse', identity.sourceSha + '^{tree}']) === env.COGLATAS_RECOVERY_SOURCE_TREE);
  if (receipt.status === 'PASS') require(git(root, ['rev-parse', 'HEAD']) === identity.sourceSha && !git(root, ['status', '--porcelain', '--untracked-files=all']));
  output(env, 'receipt_path=' + selected + '\n');
  return selected;
}
