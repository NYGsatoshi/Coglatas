import { pbkdf2Sync, randomBytes, randomUUID } from 'node:crypto';

export const rosterMemberCount = 43;
export const rosterComposeFile = 'infra/compose/test/real-backend-smoke.yml';
const shaPattern = /^[a-f0-9]{40}$/;
const guidPattern = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;

// This adapter boundary is a test-harness receipt, never product authority.
// The caller must independently observe freshness, labels and source binding.
export function assertRosterOwnership({ sourceSha, projectName, ownership }) {
  const valid = shaPattern.test(sourceSha ?? '') &&
    /^coglatas-ui07-roster-[a-z0-9][a-z0-9-]{0,40}$/.test(projectName ?? '') &&
    ownership?.projectName === projectName && ownership.composeFile === rosterComposeFile &&
    ownership.preexistingContainers === 0 && ownership.preexistingVolumes === 0 &&
    ownership.preexistingNetworks === 0 && ownership.createdByCurrentRun === true &&
    ownership.postgresProjectLabel === projectName && ownership.appProjectLabel === projectName &&
    ownership.migrationExitCode === 0 && ownership.appHealthy === true &&
    ownership.imageSourceSha === sourceSha && ownership.sourceBindingVerified === true &&
    ownership.database === 'coglatas_smoke';
  if (!valid) throw new Error('ReadOnly roster fixture ownership/source admission failed.');
}

function passwordHash(password) {
  const salt = randomBytes(16);
  return `PBKDF2-SHA256$100000$${salt.toString('base64')}$${pbkdf2Sync(password, salt, 100000, 32, 'sha256').toString('base64')}`;
}

function literal(value) { return `'${value.replaceAll("'", "''")}'`; }

function authoritySnapshot(excludedIds) {
  return `SELECT jsonb_build_object(
    'users', (SELECT jsonb_agg(jsonb_build_array("Id", "SystemRole", "Status", "DeletedAt") ORDER BY "Id") FROM users WHERE NOT ("Id" = ANY(${excludedIds}))),
    'tenant', (SELECT jsonb_agg(to_jsonb(t) ORDER BY "Id") FROM tenant_users t WHERE NOT ("UserId" = ANY(${excludedIds}))),
    'workspace', (SELECT jsonb_agg(to_jsonb(w) ORDER BY "Id") FROM workspace_members w WHERE NOT ("UserId" = ANY(${excludedIds}))),
    'conversation', (SELECT jsonb_agg(to_jsonb(c) ORDER BY "Id") FROM conversation_members c WHERE NOT ("UserId" = ANY(${excludedIds}))))`;
}

function fixtureSql(plan) {
  const rows = literal(JSON.stringify(plan.users));
  const workspace = literal(plan.workspaceId), conversation = literal(plan.conversationId);
  const owner = literal(plan.users[0].id), reader = literal(plan.users[1].id);
  const ids = `ARRAY[${plan.users.map(user => `${literal(user.id)}::uuid`).join(',')}]`;
  const snapshot = authoritySnapshot(ids);
  return `BEGIN;
DO $roster$
DECLARE
  fixture jsonb := ${rows}::jsonb;
  tenant uuid;
  entry jsonb;
  before_authority jsonb;
  after_authority jsonb;
BEGIN
  IF current_database() <> 'coglatas_smoke' OR (SELECT count(*) FROM users) > 2000 THEN
    RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Roster fixture database admission failed';
  END IF;
  IF (SELECT count(*) FROM tenants WHERE "Slug" = 'default' AND "Status" = 'Active') <> 1 THEN
    RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Roster fixture Tenant admission failed';
  END IF;
  SELECT "Id" INTO STRICT tenant FROM tenants WHERE "Slug" = 'default' AND "Status" = 'Active';
  IF jsonb_array_length(fixture) <> 43 OR
     (SELECT count(DISTINCT value->>'id') FROM jsonb_array_elements(fixture)) <> 43 OR
     EXISTS (SELECT 1 FROM users WHERE "Id" = ANY(${ids})) OR
     EXISTS (SELECT 1 FROM workspaces WHERE "Id" = ${workspace}::uuid) OR
     EXISTS (SELECT 1 FROM conversations WHERE "Id" = ${conversation}::uuid) THEN
    RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Roster fixture identity collision';
  END IF;
  ${snapshot} INTO before_authority;
  FOR entry IN SELECT value FROM jsonb_array_elements(fixture) LOOP
    INSERT INTO users ("Id", "DisplayName", "Email", "NormalizedEmail", "PasswordHash", "SystemRole", "Status", "FailedLoginAttempts", "CreatedAt")
    VALUES ((entry->>'id')::uuid, entry->>'name', entry->>'email', upper(entry->>'email'), entry->>'hash', 'User', 'Active', 0, now());
    INSERT INTO tenant_users ("Id", "TenantId", "UserId", "Role", "Status", "JoinedAt", "CreatedAt")
    VALUES ((entry->>'tenantMemberId')::uuid, tenant, (entry->>'id')::uuid, 'Member', 'Active', now(), now());
  END LOOP;
  INSERT INTO workspaces ("Id", "TenantId", "Name", "Slug", "Status", "CreatedByUserId", "DefaultTaskDeadlineDigestLocalTime", "TaskNotificationSettingsVersion", "CreatedAt")
  VALUES (${workspace}::uuid, tenant, 'Synthetic ReadOnly roster', ${literal(plan.slug)}, 'Active', ${owner}::uuid, TIME '08:00:00', 1, now());
  INSERT INTO conversations ("Id", "TenantId", "WorkspaceId", "Type", "Title", "IsArchived", "IsLocked", "CreatedByUserId", "CreatedAt")
  VALUES (${conversation}::uuid, tenant, ${workspace}::uuid, 'Group', 'Synthetic ReadOnly roster', false, false, ${owner}::uuid, now());
  FOR entry IN SELECT value FROM jsonb_array_elements(fixture) LOOP
    INSERT INTO workspace_members ("Id", "TenantId", "WorkspaceId", "UserId", "Role", "Status", "JoinedAt", "TaskNotificationPreferenceVersion", "CreatedAt")
    VALUES ((entry->>'workspaceMemberId')::uuid, tenant, ${workspace}::uuid, (entry->>'id')::uuid,
      CASE WHEN entry->>'id' = ${owner} THEN 'Owner' ELSE 'ReadOnly' END, 'Active', now(), 1, now());
    INSERT INTO conversation_members ("Id", "TenantId", "ConversationId", "UserId", "Role", "CanRead", "CanPost", "CanManageMembers", "CanCreateThread", "JoinedAt", "IsMuted", "IsArchived", "IsLater", "CreatedAt")
    VALUES ((entry->>'conversationMemberId')::uuid, tenant, ${conversation}::uuid, (entry->>'id')::uuid,
      CASE WHEN entry->>'id' = ${owner} THEN 'Admin' ELSE 'ReadOnly' END, true,
      entry->>'id' = ${owner}, entry->>'id' = ${owner}, false, now(), false, false, false, now());
  END LOOP;
  ${snapshot} INTO after_authority;
  IF before_authority IS DISTINCT FROM after_authority THEN
    RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Existing authority changed';
  END IF;
END $roster$;
SELECT json_build_object('kind', 'UI07_READONLY_ROSTER',
  'workspaceId', ${workspace}, 'conversationId', ${conversation}, 'ownerId', ${owner}, 'readerId', ${reader},
  'memberCount', (SELECT count(*) FROM conversation_members WHERE "ConversationId" = ${conversation}::uuid),
  'workspaceMemberCount', (SELECT count(*) FROM workspace_members WHERE "WorkspaceId" = ${workspace}::uuid AND "Status" = 'Active'),
  'tenantMemberCount', (SELECT count(*) FROM tenant_users WHERE "UserId" = ANY(${ids}) AND "Status" = 'Active'),
  'readOnlyCount', (SELECT count(*) FROM conversation_members WHERE "ConversationId" = ${conversation}::uuid AND "Role" = 'ReadOnly' AND "CanRead" AND NOT "CanPost" AND NOT "CanManageMembers" AND NOT "CanCreateThread"),
  'existingAuthorityUnchanged', true);
COMMIT;
`;
}

export async function prepareReadOnlyRosterFixture(options) {
  assertRosterOwnership(options);
  if (typeof options.executeSql !== 'function') throw new Error('ReadOnly roster SQL adapter is required.');
  const runId = randomUUID();
  const credentials = [];
  const users = Array.from({ length: rosterMemberCount }, (_, index) => {
    const password = `Synthetic1!${randomBytes(32).toString('hex')}`;
    const user = { id: randomUUID(), tenantMemberId: randomUUID(), workspaceMemberId: randomUUID(),
      conversationMemberId: randomUUID(), name: `Synthetic roster participant ${String(index + 1).padStart(2, '0')}`,
      email: `ui07-roster-${runId}-${index}@example.test`, hash: passwordHash(password) };
    if (index < 2) credentials.push({ id: user.id, email: user.email, password });
    return user;
  });
  const plan = { users, workspaceId: randomUUID(), conversationId: randomUUID(), slug: `ui07-roster-${runId}` };
  let result;
  try { result = await options.executeSql(fixtureSql(plan)); }
  catch { throw new Error('ReadOnly roster fixture execution failed; SQLSTATE=UNAVAILABLE.'); }
  if (result?.exitCode !== 0) {
    const state = /^\s*(?:psql:[^\n]*:\s*)?ERROR:\s*([0-9A-Z]{5})\s*$/m.exec(String(result?.stderr ?? ''))?.[1] ?? 'UNAVAILABLE';
    throw new Error(`ReadOnly roster fixture execution failed; SQLSTATE=${state}.`);
  }
  let observed;
  try { observed = JSON.parse(result.stdout.trim()); }
  catch { throw new Error('ReadOnly roster fixture receipt is unavailable.'); }
  const expected = { kind: 'UI07_READONLY_ROSTER', workspaceId: plan.workspaceId,
    conversationId: plan.conversationId, ownerId: users[0].id, readerId: users[1].id,
    memberCount: 43, workspaceMemberCount: 43, tenantMemberCount: 43,
    readOnlyCount: 42, existingAuthorityUnchanged: true };
  if (!observed || Object.keys(observed).length !== Object.keys(expected).length ||
      Object.entries(expected).some(([key, value]) => observed[key] !== value) ||
      ![observed.workspaceId, observed.conversationId, observed.ownerId, observed.readerId].every(id => guidPattern.test(id))) {
    throw new Error('ReadOnly roster fixture receipt did not reconcile.');
  }
  const environment = { COGLATAS_UI07_ROSTER_SOURCE_SHA: options.sourceSha,
    COGLATAS_UI07_ROSTER_WORKSPACE_ID: plan.workspaceId, COGLATAS_UI07_ROSTER_CONVERSATION_ID: plan.conversationId,
    COGLATAS_UI07_ROSTER_MEMBER_COUNT: '43' };
  for (const [index, actor] of ['OWNER', 'READER'].entries()) {
    for (const field of ['id', 'email', 'password']) environment[`COGLATAS_UI07_ROSTER_${actor}_${field.toUpperCase()}`] = credentials[index][field];
  }
  return { environment, receipt: { ...expected, sourceSha: options.sourceSha, projectName: options.projectName,
    scope: 'DEFENSIVE_PERSISTED_ROLE_FIXTURE_NOT_ROLE_UPDATE_API_PROOF' } };
}
