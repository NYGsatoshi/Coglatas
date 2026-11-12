import assert from 'node:assert/strict';
import { pbkdf2Sync } from 'node:crypto';
import test from 'node:test';
import { assertRosterOwnership, prepareReadOnlyRosterFixture, rosterComposeFile } from './readonly-roster-fixture.mjs';

const sourceSha = 'a'.repeat(40), projectName = 'coglatas-ui07-roster-test-123';
function options(executeSql) {
  return { sourceSha, projectName, executeSql, ownership: { projectName, composeFile: rosterComposeFile,
    preexistingContainers: 0, preexistingVolumes: 0, preexistingNetworks: 0, createdByCurrentRun: true,
    postgresProjectLabel: projectName, appProjectLabel: projectName, migrationExitCode: 0,
    appHealthy: true, imageSourceSha: sourceSha, sourceBindingVerified: true, database: 'coglatas_smoke' } };
}
function observeSql(sql) {
  const users = JSON.parse(/fixture jsonb := '([^']+)'::jsonb/.exec(sql)[1]);
  const value = name => new RegExp(`'${name}', '([a-f0-9-]+)'`).exec(sql)[1];
  return { users, receipt: { kind: 'UI07_READONLY_ROSTER', workspaceId: value('workspaceId'),
    conversationId: value('conversationId'), ownerId: value('ownerId'), readerId: value('readerId'),
    memberCount: 43, workspaceMemberCount: 43, tenantMemberCount: 43, readOnlyCount: 42,
    existingAuthorityUnchanged: true } };
}

// Node's test runner owns completion and reports callback failures.
void test('ownership, source, freshness and database failures prevent SQL execution', async () => {
  let calls = 0;
  const mutations = [
    x => { x.sourceSha = 'not-a-commit'; }, x => { x.projectName = 'production'; },
    x => { x.ownership.composeFile = 'deploy/sakura/compose.yml'; },
    x => { x.ownership.preexistingContainers = 1; }, x => { x.ownership.preexistingVolumes = 1; },
    x => { x.ownership.preexistingNetworks = 1; }, x => { x.ownership.createdByCurrentRun = false; },
    x => { x.ownership.postgresProjectLabel = 'another-project'; },
    x => { x.ownership.appProjectLabel = 'another-project'; },
    x => { x.ownership.migrationExitCode = 1; }, x => { x.ownership.appHealthy = false; },
    x => { x.ownership.imageSourceSha = 'b'.repeat(40); },
    x => { x.ownership.sourceBindingVerified = false; }, x => { x.ownership.database = 'production'; },
  ];
  for (const mutation of mutations) {
    const input = options(async () => { calls++; });
    mutation(input);
    await assert.rejects(prepareReadOnlyRosterFixture(input), /admission failed/);
  }
  assert.equal(calls, 0);
  assert.doesNotThrow(() => assertRosterOwnership(options(() => {})));
});

void test('the bounded plan creates unique unprivileged actors and reconciles an auth-free receipt', async () => {
  let observed, sql;
  const result = await prepareReadOnlyRosterFixture(options(async input => {
    sql = input; observed = observeSql(input);
    assert.equal(observed.users.length, 43);
    assert.equal(new Set(observed.users.flatMap(x => [x.id, x.tenantMemberId, x.workspaceMemberId, x.conversationMemberId])).size, 172);
    assert.equal(new Set(observed.users.map(x => x.email)).size, 43);
    assert.ok(observed.users.every(x => /^ui07-roster-[a-f0-9-]+-\d+@example\.test$/.test(x.email)));
    assert.equal(/current_database\(\) <> 'coglatas_smoke'/.test(input), true, 'database admission is explicit');
    assert.equal(/before_authority IS DISTINCT FROM after_authority/.test(input), true, 'existing authority is reconciled');
    assert.equal((input.match(/^BEGIN;/gm) ?? []).length, 1);
    assert.equal((input.match(/^COMMIT;/gm) ?? []).length, 1);
    assert.equal(/\b(?:UPDATE|DELETE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|DISABLE)\b/i.test(input), false, 'no existing mutation or schema/privilege changes');
    assert.equal(/'Member', 'Active'/.test(input), true, 'ordinary active Tenant membership');
    assert.equal(/THEN 'Owner' ELSE 'ReadOnly'/.test(input), true, 'bounded Workspace roles');
    assert.equal(/THEN 'Admin' ELSE 'ReadOnly'/.test(input), true, 'bounded Conversation roles');
    return { exitCode: 0, stdout: JSON.stringify(observed.receipt), stderr: '' };
  }));
  assert.equal(result.environment.COGLATAS_UI07_ROSTER_MEMBER_COUNT, '43');
  const receipt = JSON.stringify(result.receipt);
  for (const [index, actor] of ['OWNER', 'READER'].entries()) {
    const password = result.environment[`COGLATAS_UI07_ROSTER_${actor}_PASSWORD`];
    const hash = observed.users[index].hash;
    const [, iterations, salt, key] = hash.split('$');
    assert.equal(pbkdf2Sync(password, Buffer.from(salt, 'base64'), Number(iterations), 32, 'sha256').toString('base64') === key, true, 'generated credential matches the existing hash format');
    assert.equal(sql.includes(password), false);
    assert.equal(receipt.includes(password), false);
    assert.equal(receipt.includes(hash), false);
  }
  assert.equal(result.receipt.existingAuthorityUnchanged, true);
  assert.equal(result.receipt.sourceSha, sourceSha);
  assert.equal(/password|hash|email/i.test(receipt), false, 'receipt contains no credential fields');
});

void test('SQL failure exposes only SQLSTATE and never the supplied diagnostic body', async () => {
  await assert.rejects(prepareReadOnlyRosterFixture(options(async sql => ({ exitCode: 1, stdout: '',
    stderr: `ERROR: 23505\nDETAIL: secret-password-canary\n${sql}` }))), error => {
    assert.equal(error.message === 'ReadOnly roster fixture execution failed; SQLSTATE=23505.', true, 'only the SQLSTATE is exposed');
    return true;
  });
  await assert.rejects(prepareReadOnlyRosterFixture(options(async () => { throw new Error('secret-password-canary'); })),
    { message: 'ReadOnly roster fixture execution failed; SQLSTATE=UNAVAILABLE.' });
});

void test('missing evidence, incorrect cardinality and unexpected credential fields cannot qualify', async () => {
  for (const mutation of [() => '', receipt => ({ ...receipt, memberCount: 3 }),
    receipt => ({ ...receipt, readOnlyCount: 41 }), receipt => ({ ...receipt, existingAuthorityUnchanged: false }),
    receipt => ({ ...receipt, password: 'secret-password-canary' })]) {
    await assert.rejects(prepareReadOnlyRosterFixture(options(async sql => {
      const value = mutation(observeSql(sql).receipt);
      return { exitCode: 0, stdout: typeof value === 'string' ? value : JSON.stringify(value), stderr: '' };
    })), /receipt/);
  }
});
