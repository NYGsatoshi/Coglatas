import assert from 'node:assert/strict';
import test from 'node:test';
import { randomUUID } from 'node:crypto';
import { mkdtemp, readFile, writeFile, unlink, rmdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { readPilotConfiguration, pilotOrigin, validateCheckpoint, validateRestartRecord, validateExerciseReceipt, validatePhaseReceipt, pilotMessageUtcTicks, isRedactedDenial, sanitizedReceipt, assertBoundOrigin, scopeReadPaths } from './student-council-pilot-helpers.mjs';

const scope = () => Object.fromEntries(['workspaceId', 'channelId', 'directId', 'directMessageId', 'rootMessageId', 'fileId'].map((key) => [key, randomUUID()]));
function fixture() {
  return { version: 1, syntheticOnly: true, existingIdsVerified: true, inventoryReference: 'pilot/synthetic-inventory-01', allowed: { ...scope(), fileSha256: 'a'.repeat(64), fileSizeBytes: 42 }, deniedSameTenant: scope(), deniedCrossTenant: scope(), revocation: { ...scope(), userId: randomUUID() }, memberUserId: randomUUID(), operatorUserId: randomUUID() };
}
function environment(overrides = {}) {
  const env = { ENABLED: '1', SYNTHETIC_FIXTURE: '1', SCOPE: 'core', PHASE: 'exercise', URL: 'https://portal.example.com', COMMIT: 'a'.repeat(40) };
  for (const name of ['MEMBER', 'OPERATOR', 'REVOKEE']) { env[`${name}_EMAIL`] = `${name.toLowerCase()}@example.test`; env[`${name}_PASSWORD`] = 'synthetic-password-for-unit-test'; }
  return { ...Object.fromEntries(Object.entries(env).map(([key, value]) => ['COGLATAS_STUDENT_PILOT_' + key, value])), ...overrides };
}
function checkpoint(config) { return { version: 2, scope: config.scope, commit: config.commit, binding: config.binding, phase: 'exercise', groupDm: config.scope === 'full' ? 'OWNED_CHECKS_PASSED' : 'NOT_EVALUATED', group: config.scope === 'full' ? groupProof() : undefined, completedAtUtc: new Date(Date.now() - 60000).toISOString(), runId: randomUUID(), rootMessageId: randomUUID(), replyMessageId: randomUUID(), directMessageId: randomUUID() }; }
function fullFixture() { return { ...fixture(), version: 2, groups: Object.fromEntries(['deniedSameTenant', 'deniedCrossTenant'].map(section => [section, { conversationId: randomUUID(), messageId: randomUUID(), body: `Synthetic protected ${section} Group body` }])) }; }
function groupProof() { return { conversationId: randomUUID(), firstMessageId: randomUUID(), secondMessageId: randomUUID(), unreadUiVerified: true, removedReaddedLeftVerified: true, creatorRetained: true }; }
function restart(cp) { return { version: 1, commit: cp.commit, checkpointRunId: cp.runId, operatorAttested: true, beforeContainerId: 'b'.repeat(64), afterContainerId: 'b'.repeat(64), beforeStartedAtUtc: new Date(Date.now() - 120000).toISOString(), afterStartedAtUtc: new Date(Date.now() - 30000).toISOString() }; }

test('core execution can only issue partial qualification and no restart or recovery credit', () => {
  const config = readPilotConfiguration(environment(), fixture());
  const receipt = sanitizedReceipt(config, 'exercise', undefined, randomUUID());
  assert.equal(receipt.qualification, 'PILOT_PARTIAL');
  assert.equal(receipt.fullPilotGo, false);
  assert.equal(receipt.groupDm, 'NOT_EVALUATED');
  assert.equal(receipt.publicGo, false);
  assert.equal(receipt.restartPersistence, 'NOT_EVALUATED');
  assert.equal(receipt.backupRestore, 'NOT_EVALUATED');
  assert.equal(JSON.stringify(receipt).includes(config.accounts.member.password), false);
});

test('full scope requires complete version 2 known-body Group controls and cannot issue a receipt without executed proof', () => {
  const env = environment({ COGLATAS_STUDENT_PILOT_SCOPE: 'full' });
  assert.throws(() => readPilotConfiguration(env, fixture()), /PILOT_NO_GO.*version 2/u);
  const f = fullFixture(), config = readPilotConfiguration(env, f), proof = groupProof(), cp = checkpoint(config);
  assert.throws(() => sanitizedReceipt(config, 'exercise'));
  for (const key of Object.keys(proof)) {
    const missing = { ...proof }; delete missing[key];
    assert.throws(() => sanitizedReceipt(config, 'exercise', missing, cp.runId));
  }
  const receipt = sanitizedReceipt(config, 'exercise', proof, cp.runId);
  assert.equal(receipt.groupDm, 'OWNED_CHECKS_PASSED');
  assert.equal(receipt.fullPilotGo, false);
  assert.equal(receipt.publicGo, false);
  assert.ok(receipt.missingMatrixControls.includes('expired-cookie'));
  assert.ok(receipt.missingMatrixControls.includes('all-prohibited-file-mutations'));
  validatePhaseReceipt(receipt, config);
  for (const mutate of [f => delete f.groups, f => delete f.groups.deniedSameTenant.messageId,
    f => f.groups.deniedCrossTenant.body = '', f => f.groups.deniedSameTenant.conversationId = f.allowed.workspaceId,
    f => f.groups.deniedCrossTenant.messageId = f.groups.deniedSameTenant.messageId]) {
    const invalid = fullFixture(); mutate(invalid); assert.throws(() => readPilotConfiguration(env, invalid));
  }
  assert.equal(validateCheckpoint(cp, config), cp);
  validateExerciseReceipt(receipt, cp);
  for (const delta of [{ scope: 'core' }, { binding: 'wrong' }, { publicGo: true }, { fullPilotGo: true }, { groupDm: 'NOT_EVALUATED' }]) {
    assert.throws(() => validatePhaseReceipt({ ...receipt, ...delta }, config));
    assert.throws(() => validateExerciseReceipt({ ...receipt, ...delta }, cp));
  }
  assert.notEqual(config.binding, readPilotConfiguration(environment(), f).binding);
  assert.throws(() => validateCheckpoint({ ...cp, group: { ...proof, creatorRetained: false } }, config));
  assert.throws(() => validateExerciseReceipt({ ...receipt, runId: randomUUID() }, cp));
  assert.throws(() => validatePhaseReceipt({ ...receipt, groupChecks: {} }, config));
  assert.throws(() => validateExerciseReceipt({ ...receipt, groupChecks: {} }, cp));
});

test('requires every marker, exact commit, phase, synthetic distinct account and existing fixture ID', () => {
  for (const key of Object.keys(environment())) assert.throws(() => readPilotConfiguration({ ...environment(), [key]: '' }, fixture()));
  for (const overrides of [ { COGLATAS_STUDENT_PILOT_PHASE: 'skip' }, { COGLATAS_STUDENT_PILOT_COMMIT: 'main' }, { COGLATAS_STUDENT_PILOT_MEMBER_EMAIL: 'person@school.edu' }, { COGLATAS_STUDENT_PILOT_OPERATOR_EMAIL: 'member@example.test' } ]) assert.throws(() => readPilotConfiguration(environment(overrides), fixture()));
  for (const mutate of [ (f) => f.syntheticOnly = false, (f) => f.existingIdsVerified = false, (f) => f.allowed.channelId = 'random', (f) => f.deniedSameTenant.workspaceId = f.allowed.workspaceId, (f) => f.allowed.fileSha256 = '', (f) => f.allowed.fileSizeBytes = 1048577 ]) {
    const value = fixture(); mutate(value); assert.throws(() => readPilotConfiguration(environment(), value));
  }
});

test('Group cursor ordering retains sub-millisecond server precision without GUID or Date millisecond inference', () => {
  assert.ok(pilotMessageUtcTicks('2026-10-20T01:02:03.1234567Z') > pilotMessageUtcTicks('2026-10-20T01:02:03.123456Z'));
  assert.equal(pilotMessageUtcTicks('2026-10-20T01:02:03Z'), pilotMessageUtcTicks('2026-10-20T01:02:03.0000000+00:00'));
  for (const invalid of [undefined, 'yesterday', '2026-02-30T01:02:03Z', '2026-10-20T01:02:03.12345678Z', '2026-10-20T01:02:03+09:00']) assert.throws(() => pilotMessageUtcTicks(invalid));
});

test('rejects IP literals, local/reserved names and non-root public origins without exposing values', () => {
  for (const url of ['http://portal.example.com', 'https://localhost', 'https://192.168.0.1', 'https://[::1]', 'https://[::ffff:127.0.0.1]', 'https://[2606:4700:4700::1111]', 'https://host.internal', 'https://pilot.example.test', 'https://secret:password@portal.example.com', 'https://portal.example.com/path', 'https://portal.example.com?secret=token']) {
    assert.throws(() => pilotOrigin(url), (error) => !error.message.includes(url));
  }
});

test('rejects redirected origins before credentials or relative API requests can follow them', () => {
  const origin = 'https://portal.example.com';
  assert.doesNotThrow(() => assertBoundOrigin(`${origin}/app/login?returnUrl=workspaces`, origin));
  for (const destination of ['https://other.example.com/app/login', 'https://portal.example.com.attacker.invalid/app/login', 'http://portal.example.com/app/login', 'https://user:secret@portal.example.com/app/login', '/app/login']) {
    assert.throws(() => assertBoundOrigin(destination, origin), (error) => !error.message.includes(destination) && !error.message.includes('secret'));
  }
});

test('requires known Direct bodies and includes the exact revocation Direct message read', () => {
  const f = fixture();
  for (const section of ['allowed', 'deniedSameTenant', 'deniedCrossTenant', 'revocation']) {
    const paths = scopeReadPaths(f[section]);
    assert.ok(paths.includes(`/api/conversations/${f[section].directId}/messages`));
    assert.ok(paths.includes(`/api/conversations/${f[section].directId}/messages?anchorMessageId=${f[section].directMessageId}`));
    const missing = structuredClone(f);
    delete missing[section].directMessageId;
    assert.throws(() => readPilotConfiguration(environment(), missing), /directMessageId/u);
  }
  const missingDirect = structuredClone(f);
  delete missingDirect.revocation.directId;
  assert.throws(() => readPilotConfiguration(environment(), missingDirect), /revocation\.directId/u);
  assert.equal(isRedactedDenial({ status: 200, text: 'persisted synthetic Direct body' }), false);
});

test('checkpoint is bound to exact origin, fixture, commit and bounded exercise time', () => {
  const f = fixture(), config = readPilotConfiguration(environment(), f), cp = checkpoint(config);
  assert.equal(validateCheckpoint(cp, config), cp);
  for (const overrides of [{ version: 1 }, { scope: 'full' }, { binding: 'bad' }, { commit: 'c'.repeat(40) }, { phase: 'verify-restart' }, { groupDm: 'GO' }, { rootMessageId: 'bad' }, { completedAtUtc: new Date(Date.now() - 86400001).toISOString() }, { completedAtUtc: 'yesterday' }]) assert.throws(() => validateCheckpoint({ ...cp, ...overrides }, config));
  assert.throws(() => validateCheckpoint(cp, readPilotConfiguration(environment({ COGLATAS_STUDENT_PILOT_URL: 'https://other.example.com' }), f)));
});

test('restart must attest observed IDs, same run and a UTC start after exercise', () => {
  const config = readPilotConfiguration(environment(), fixture()), cp = checkpoint(config), record = restart(cp);
  assert.equal(validateRestartRecord(record, cp), record); // docker restart retains the container ID
  for (const overrides of [{ operatorAttested: false }, { checkpointRunId: randomUUID() }, { commit: 'd'.repeat(40) }, { afterContainerId: 'unknown' }, { afterStartedAtUtc: record.beforeStartedAtUtc }, { afterStartedAtUtc: new Date(Date.now() + 60000).toISOString() }]) assert.throws(() => validateRestartRecord({ ...record, ...overrides }, cp));
});

test('restart cannot consume a checkpoint from a failed exercise without its successful receipt', () => {
  const config = readPilotConfiguration(environment(), fixture()), cp = checkpoint(config);
  const receipt = sanitizedReceipt(config, 'exercise', undefined, cp.runId);
  assert.doesNotThrow(() => validateExerciseReceipt(receipt, cp));
  for (const value of [undefined, {}, { ...receipt, phase: 'verify-restart' }, { ...receipt, qualification: 'PILOT_NO_GO' }, { ...receipt, completedAtUtc: new Date(Date.parse(cp.completedAtUtc) - 1000).toISOString() }]) assert.throws(() => validateExerciseReceipt(value, cp));
});

test('negative matrix rejects false passes, leaks and provider errors', () => {
  const id = randomUUID();
  for (const status of [400, 403, 404]) assert.equal(isRedactedDenial({ status, text: '{"error":"Not found."}' }, [id]), true);
  for (const status of [200, 201, 302, 401, 429, 500, 503]) assert.equal(isRedactedDenial({ status, text: '{}' }, [id]), false);
  for (const text of [id, 'System.Exception', 'postgresql', 'StorageKey', 'Set-Cookie:', 'password=oops']) assert.equal(isRedactedDenial({ status: 400, text }, [id]), false);
});

test('runner validation contacts no target and rejects selection/artifact bypass arguments', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'coglatas-pilot-gate-unit-'));
  const inventory = join(directory, 'fixture.json');
  const f = fixture();
  await writeFile(inventory, JSON.stringify(f), { mode: 0o600 });
  const runner = fileURLToPath(new URL('./student-council-pilot-run.mjs', import.meta.url));
  const env = { ...process.env, ...environment(), COGLATAS_STUDENT_PILOT_FIXTURE: inventory, COGLATAS_STUDENT_PILOT_EVIDENCE_DIR: directory };
  try {
    const valid = spawnSync(process.execPath, [runner, '--validate-only'], { env, encoding: 'utf8', timeout: 10000 });
    assert.equal(valid.status, 0);
    assert.match(valid.stdout, /No browser, deployment or release qualification/u);
    assert.equal(valid.stdout.includes(env.COGLATAS_STUDENT_PILOT_MEMBER_PASSWORD), false);
    assert.equal(valid.stdout.includes(f.allowed.workspaceId), false);
    for (const args of [['--grep=none'], ['--reporter=html'], ['--config=playwright.config.ts'], ['--validate-only', '--list']]) {
      const rejected = spawnSync(process.execPath, [runner, ...args], { env, encoding: 'utf8', timeout: 10000 });
      assert.equal(rejected.status, 1);
      assert.match(rejected.stderr, /cannot be overridden/u);
      assert.equal(rejected.stderr.includes(env.COGLATAS_STUDENT_PILOT_MEMBER_PASSWORD), false);
    }
    for (const name of ['exercise-receipt.json', 'checkpoint.json']) {
      const path = join(directory, name), original = 'Synthetic previous outcome; preserve exactly.\n';
      await writeFile(path, original, { flag: 'wx' });
      try {
        const refused = spawnSync(process.execPath, [runner], { env, encoding: 'utf8', timeout: 10000 });
        assert.equal(refused.status, 1);
        assert.match(refused.stderr, /output already exists/u);
        assert.equal(await readFile(path, 'utf8'), original);
      } finally { await unlink(path); }
    }
    await writeFile(inventory, JSON.stringify(fullFixture()));
    const full = spawnSync(process.execPath, [runner, '--validate-only'], {
      env: { ...env, COGLATAS_STUDENT_PILOT_SCOPE: 'full' }, encoding: 'utf8', timeout: 10000
    });
    assert.equal(full.status, 0);
    assert.match(full.stdout, /No browser, deployment or release qualification/u);
  } finally {
    await unlink(inventory);
    await rmdir(directory);
  }
});
