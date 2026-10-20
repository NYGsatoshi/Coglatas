import { createHash } from 'node:crypto';
import { isIP } from 'node:net';
import { isUuid, publicHttpsOrigin } from './public-https-golden-path-helpers.mjs';

const prefix = 'COGLATAS_STUDENT_PILOT_';
const scopeKeys = ['workspaceId', 'channelId', 'directId', 'directMessageId', 'rootMessageId', 'fileId'];

// Execution evidence never grants deployment or real-user admission authority.
export function readPilotConfiguration(environment, fixture) {
  for (const key of ['ENABLED', 'SYNTHETIC_FIXTURE']) {
    if (environment[prefix + key] !== '1') fail(`${prefix}${key}=1 is required.`);
  }
  const scope = required(environment, 'SCOPE');
  if (!['core', 'full'].includes(scope)) fail('Pilot SCOPE must be core or full.');
  const phase = required(environment, 'PHASE');
  if (!['exercise', 'verify-restart'].includes(phase)) fail('Pilot PHASE must be exercise or verify-restart.');
  const commit = required(environment, 'COMMIT');
  if (!/^[a-f0-9]{40}$/u.test(commit)) fail('Pilot COMMIT must be the exact reviewed 40-character commit.');
  const baseURL = pilotOrigin(required(environment, 'URL'));
  const accounts = Object.fromEntries(['member', 'operator', 'revokee'].map((name) => {
    const key = name.toUpperCase();
    const email = required(environment, `${key}_EMAIL`);
    if (!/^[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@example\.test$/iu.test(email)) fail(`Pilot ${key}_EMAIL must be a synthetic @example.test account.`);
    return [name, Object.freeze({ email, password: required(environment, `${key}_PASSWORD`) })];
  }));
  if (new Set(Object.values(accounts).map((a) => a.email.toLowerCase())).size !== 3) fail('Pilot accounts must be distinct.');
  validateFixture(fixture);
  if (scope === 'full') validateFullFixture(fixture);
  const binding = createHash('sha256').update(JSON.stringify({ baseURL, commit, fixture, scope })).digest('hex');
  return Object.freeze({ baseURL, commit, phase, scope, accounts: Object.freeze(accounts), fixture, binding });
}

export function pilotOrigin(value) {
  // Reuse the #481 contract without changing it; tighten reserved/private forms
  // here, including bracketed and IPv4-mapped IPv6 hosts.
  const origin = publicHttpsOrigin(value);
  const hostname = new URL(origin).hostname.toLowerCase().replace(/^\[|\]$/gu, '');
  if (isIP(hostname) || !hostname.includes('.') || /\.(?:test|invalid|localhost|local|internal|home|lan|example)$/u.test(hostname)) {
    fail('Pilot URL must be a public DNS HTTPS origin.');
  }
  return origin;
}

export function validateFixture(fixture) {
  if (!fixture || ![1, 2].includes(fixture.version) || fixture.syntheticOnly !== true || fixture.existingIdsVerified !== true) fail('Pilot fixture must declare version 1 or 2, syntheticOnly and existingIdsVerified.');
  if (typeof fixture.inventoryReference !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._/-]{2,119}$/u.test(fixture.inventoryReference)) fail('Pilot fixture requires a sanitized inventoryReference.');
  const ids = [];
  for (const section of ['allowed', 'deniedSameTenant', 'deniedCrossTenant']) {
    for (const key of scopeKeys) {
      const value = fixture[section]?.[key];
      if (!isUuid(value)) fail(`Pilot fixture ${section}.${key} must be an existing synthetic UUID.`);
      ids.push(value.toLowerCase());
    }
  }
  for (const key of [...scopeKeys, 'userId']) {
    const value = fixture.revocation?.[key];
    if (!isUuid(value)) fail(`Pilot fixture revocation.${key} must be an existing synthetic UUID.`);
    ids.push(value.toLowerCase());
  }
  for (const key of ['memberUserId', 'operatorUserId']) {
    if (!isUuid(fixture[key])) fail(`Pilot fixture ${key} must be an existing synthetic UUID.`);
    ids.push(fixture[key].toLowerCase());
  }
  if (new Set(ids).size !== ids.length) fail('Pilot fixture resource and account identities must be distinct.');
  if (!/^[a-f0-9]{64}$/u.test(fixture.allowed.fileSha256 ?? '')) fail('Pilot fixture requires the expected synthetic File SHA-256.');
  if (!Number.isInteger(fixture.allowed.fileSizeBytes) || fixture.allowed.fileSizeBytes < 1 || fixture.allowed.fileSizeBytes > 1048576) fail('Pilot synthetic File must be between 1 byte and 1 MiB.');
}

export function validateFullFixture(fixture) {
  if (fixture.version !== 2) fail('PILOT_NO_GO: full scope requires the complete version 2 Group fixture.');
  const ids = [];
  for (const section of ['deniedSameTenant', 'deniedCrossTenant']) {
    const group = fixture.groups?.[section];
    for (const key of ['conversationId', 'messageId']) {
      if (!isUuid(group?.[key])) fail('Pilot full fixture requires known existing denied Group and Message identities.');
      ids.push(group[key].toLowerCase());
    }
    if (typeof group.body !== 'string' || group.body.length < 16 || group.body.length > 1000 || !group.body.startsWith('Synthetic ')) fail('Pilot full fixture requires the known synthetic protected Group body.');
  }
  const existing = [fixture.memberUserId, fixture.operatorUserId, fixture.revocation.userId,
    ...['allowed', 'deniedSameTenant', 'deniedCrossTenant', 'revocation'].flatMap(section => scopeKeys.map(key => fixture[section][key]))];
  if (new Set([...existing, ...ids].map(value => value.toLowerCase())).size !== existing.length + ids.length) fail('Pilot full Group fixture identities must be distinct from all core fixtures.');
}

export function validateCheckpoint(value, configuration) {
  if (value?.version !== 2 || value.scope !== configuration.scope || value.binding !== configuration.binding || value.commit !== configuration.commit || value.phase !== 'exercise' || value.groupDm !== (configuration.scope === 'full' ? 'OWNED_CHECKS_PASSED' : 'NOT_EVALUATED')) fail('Checkpoint does not match the exact pilot fixture, scope, origin and commit.');
  for (const key of ['rootMessageId', 'replyMessageId', 'directMessageId', 'runId']) {
    if (!isUuid(value[key])) fail('Checkpoint contains an invalid synthetic message identity.');
  }
  if (configuration.scope === 'full') validateGroupCheckpoint(value.group);
  if (!isUtc(value.completedAtUtc) || Date.now() - Date.parse(value.completedAtUtc) > 86400000 || Date.parse(value.completedAtUtc) > Date.now()) fail('Checkpoint must be an exercise completed within the previous 24 hours.');
  return value;
}

export function validateGroupCheckpoint(group) {
  if (!group || !['conversationId', 'firstMessageId', 'secondMessageId'].every(key => isUuid(group[key])) ||
      new Set([group.conversationId, group.firstMessageId, group.secondMessageId]).size !== 3 ||
      group.unreadUiVerified !== true || group.removedReaddedLeftVerified !== true || group.creatorRetained !== true) fail('Checkpoint requires completed Group persistence, unread and lifecycle checks.');
  return group;
}

export function pilotMessageUtcTicks(value) {
  const match = typeof value === 'string' && /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/u.exec(value);
  const milliseconds = match ? Date.parse(match[1] + 'Z') : NaN;
  if (!match || !Number.isFinite(milliseconds) || !new Date(milliseconds).toISOString().startsWith(match[1])) fail('Pilot requires a valid full-precision UTC Message timestamp.');
  return BigInt(milliseconds) * 10000n + BigInt((match[2] ?? '').padEnd(7, '0'));
}

export function validateRestartRecord(value, checkpoint) {
  if (!value || value.version !== 1 || value.commit !== checkpoint.commit || value.checkpointRunId !== checkpoint.runId || value.operatorAttested !== true) fail('Restart record must attest the same exact commit and checkpoint.');
  for (const key of ['beforeContainerId', 'afterContainerId']) {
    if (!/^[a-f0-9]{64}$/u.test(value[key] ?? '')) fail('Restart record requires Docker container IDs.');
  }
  for (const key of ['beforeStartedAtUtc', 'afterStartedAtUtc']) {
    if (!isUtc(value[key])) fail('Restart record requires UTC start times.');
  }
  if (Date.parse(value.afterStartedAtUtc) <= Date.parse(checkpoint.completedAtUtc) || Date.parse(value.afterStartedAtUtc) <= Date.parse(value.beforeStartedAtUtc) || Date.parse(value.afterStartedAtUtc) > Date.now()) fail('Restart must occur after the exercise and before verification.');
  return value;
}

export function validateExerciseReceipt(value, checkpoint) {
  if (value?.version !== 2 || value.runId !== checkpoint.runId || value.phase !== 'exercise' || value.binding !== checkpoint.binding || value.scope !== checkpoint.scope || value.commit !== checkpoint.commit || value.qualification !== 'PILOT_PARTIAL' || value.groupDm !== checkpoint.groupDm || value.fullPilotGo !== false || value.publicGo !== false || !isUtc(value.completedAtUtc) || Date.parse(value.completedAtUtc) < Date.parse(checkpoint.completedAtUtc)) fail('Restart verification requires a successful exercise receipt for the same scope, run, commit and checkpoint time.');
  if (checkpoint.scope === 'full' && !['unreadUiVerified', 'removedReaddedLeftVerified', 'creatorRetained'].every(key => value.groupChecks?.[key] === true)) fail('Restart full receipt lacks completed owned Group checks.');
}

export function validatePhaseReceipt(value, configuration) {
  if (value?.version !== 2 || value.binding !== configuration.binding || value.scope !== configuration.scope ||
      value.commit !== configuration.commit || value.phase !== configuration.phase || value.qualification !== 'PILOT_PARTIAL' ||
      value.fullPilotGo !== false || value.publicGo !== false || value.groupDm !== (configuration.scope === 'full' ? 'OWNED_CHECKS_PASSED' : 'NOT_EVALUATED') ||
      !isUuid(value.runId) || !isUtc(value.completedAtUtc)) fail('Pilot execution did not issue a matching scoped partial receipt.');
  if (configuration.scope === 'full' && !['unreadUiVerified', 'removedReaddedLeftVerified', 'creatorRetained'].every(key => value.groupChecks?.[key] === true)) fail('Pilot full receipt requires the completed owned Group checks.');
}

export function isRedactedDenial(result, protectedIds = []) {
  const body = String(result.text ?? '').toLowerCase();
  return [400, 403, 404].includes(result.status) &&
    ['exception', 'stacktrace', 'stack trace', 'system.', 'password=', 'connectionstring', 'postgresql', 'set-cookie:', 'storagekey'].every((marker) => !body.includes(marker)) &&
    protectedIds.every((id) => !body.includes(id.toLowerCase()));
}

export function isGroupContractDenial(result, protectedValues, operation) {
  if (result.status !== 400 || !isRedactedDenial(result, protectedValues)) return false;
  const errors = { read: 'Conversation not found.', send: 'You are not allowed to send messages.', thread: 'Message thread not found.' };
  if (!Object.hasOwn(errors, operation)) return false;
  try {
    const value = JSON.parse(result.text);
    return value !== null && typeof value === 'object' && Object.keys(value).length === 1 && value.error === errors[operation];
  } catch { return false; }
}

export function assertBoundOrigin(value, expectedOrigin) {
  let url;
  try { url = new URL(value); } catch { fail('Pilot navigation or response has an invalid origin.'); }
  if (url.protocol !== 'https:' || url.origin !== expectedOrigin || url.username || url.password) fail('Pilot navigation or response crossed the configured HTTPS origin.');
}

export function scopeReadPaths(scope) {
  // The Direct body boundary must be exercised separately from its detail DTO.
  // Never replace or filter the dedicated revocation Direct fixture.
  return [
    `/api/workspaces/${scope.workspaceId}`,
    `/api/conversations/${scope.channelId}`,
    `/api/conversations/${scope.channelId}/messages`,
    `/api/conversations/${scope.directId}/messages`,
    `/api/conversations/${scope.directId}`,
    `/api/conversations/${scope.directId}/messages?anchorMessageId=${scope.directMessageId}`,
    `/api/messages/${scope.rootMessageId}/thread`,
    `/api/files/${scope.fileId}`
  ];
}

export function sanitizedReceipt(configuration, phase, group, runId) {
  if (!isUuid(runId)) fail('Pilot receipt requires the actual checkpoint run identity.');
  if (configuration.scope === 'full') validateGroupCheckpoint(group);
  return {
    version: 2, runId, binding: configuration.binding, scope: configuration.scope, commit: configuration.commit, commitSource: 'OPERATOR_SUPPLIED', phase, completedAtUtc: new Date().toISOString(),
    qualification: 'PILOT_PARTIAL', groupDm: configuration.scope === 'full' ? 'OWNED_CHECKS_PASSED' : 'NOT_EVALUATED', fullPilotGo: false, publicGo: false,
    ...(configuration.scope === 'full' ? { groupChecks: { unreadUiVerified: group.unreadUiVerified, removedReaddedLeftVerified: group.removedReaddedLeftVerified, creatorRetained: group.creatorRetained } } : {}),
    restartPersistence: phase === 'verify-restart' ? 'VERIFIED_AFTER_OPERATOR_ATTESTED_RESTART' : 'NOT_EVALUATED',
    crossTenantFixtureExistence: 'OPERATOR_INVENTORY_ATTESTED',
    backupRestore: 'NOT_EVALUATED', targetDeployment: 'NOT_EVALUATED',
    flowCoverage: {
      '01-public-https': 'CHECKED', '02-login': 'CHECKED',
      '03-workspace-select': configuration.scope === 'full' ? 'UI_CHECKED' : 'API_SCOPE_ONLY',
      '04-teams-channel': configuration.scope === 'full' ? 'DIRECTORY_AND_TIMELINE_CHECKED' : 'TIMELINE_ONLY',
      '05-channel-send': 'EXERCISE_WRITE_AND_DURABLE_READ_CHECKED',
      '06-second-user-receive': configuration.scope === 'full' ? 'EXERCISE_UI_RECEIVE_NO_TRANSPORT_CAUSAL_CLAIM' : 'NOT_EVALUATED',
      '07-thread-reply': 'EXERCISE_WRITE_AND_DURABLE_READ_CHECKED', '08-direct-send': 'EXERCISE_WRITE_AND_DURABLE_READ_CHECKED',
      '09-group': configuration.scope === 'full' ? 'EXERCISE_LIFECYCLE_AND_PERSISTENCE_CHECKED' : 'NOT_EVALUATED',
      '10-unread': configuration.scope === 'full' ? 'GROUP_ONLY_DM_CHANNEL_NOT_EVALUATED' : 'NOT_EVALUATED',
      '11-files-read-download': 'CHECKED', '12-reload-persistence': 'CHECKED', '13-logout': 'CHECKED',
      '14-unauthenticated-denial': 'CHECKED', '15-nonparticipant-denial': 'CORE_CHECKED_GROUP_ADDED_IN_FULL',
      '16-revocation': 'EXERCISE_WORKSPACE_CORE_GROUP_REMOVE_LEAVE_ADDED_IN_FULL',
      '17-restart': phase === 'verify-restart' ? 'OPERATOR_ATTESTED_RESTART_PERSISTENCE_CHECKED' : 'NOT_EVALUATED'
    },
    missingMatrixControls: [...(configuration.scope === 'core' ? ['group-positive-unread-lifecycle', 'second-actor-channel-ui-receive'] : []),
      'dm-channel-unread', 'mentions', 'expired-cookie', 'delayed-response', 'reconnect-stale-state', 'workspace-revoked-group-stale-ui', 'all-prohibited-file-mutations', 'target-image-provenance', 'backup-restore-drill', 'physical-ime', 'school-approval-and-operations']
  };
}

function isUtc(value) { return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?Z$/u.test(value) && Number.isFinite(Date.parse(value)); }
function required(environment, key) {
  const value = environment[prefix + key];
  if (typeof value !== 'string' || value.trim().length === 0) fail(`${prefix}${key} must be configured.`);
  return value.trim();
}
function fail(message) { throw new Error(message); }
