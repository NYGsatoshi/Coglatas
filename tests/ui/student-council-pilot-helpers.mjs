import { createHash } from 'node:crypto';
import { isIP } from 'node:net';
import { isUuid, publicHttpsOrigin } from './public-https-golden-path-helpers.mjs';

const prefix = 'COGLATAS_STUDENT_PILOT_';
const scopeKeys = ['workspaceId', 'channelId', 'directId', 'directMessageId', 'rootMessageId', 'fileId'];

// This is an execution contract, not permission to activate ConversationType.Group.
export function readPilotConfiguration(environment, fixture) {
  for (const key of ['ENABLED', 'SYNTHETIC_FIXTURE']) {
    if (environment[prefix + key] !== '1') fail(`${prefix}${key}=1 is required.`);
  }
  const scope = required(environment, 'SCOPE');
  if (!['core', 'full'].includes(scope)) fail('Pilot SCOPE must be core or full.');
  if (scope === 'full') fail('PILOT_NO_GO: GroupDM is unsupported; a full pilot cannot qualify. Use core only for an explicitly partial result.');
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
  const binding = createHash('sha256').update(JSON.stringify({ baseURL, commit, fixture })).digest('hex');
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
  if (!fixture || fixture.version !== 1 || fixture.syntheticOnly !== true || fixture.existingIdsVerified !== true) fail('Pilot fixture must declare version 1, syntheticOnly and existingIdsVerified.');
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

export function validateCheckpoint(value, configuration) {
  if (value?.version !== 1 || value.binding !== configuration.binding || value.commit !== configuration.commit || value.groupDm !== 'NO_GO' || value.phase !== 'exercise') fail('Checkpoint does not match the exact pilot fixture, origin and commit.');
  for (const key of ['rootMessageId', 'replyMessageId', 'directMessageId', 'runId']) {
    if (!isUuid(value[key])) fail('Checkpoint contains an invalid synthetic message identity.');
  }
  if (!isUtc(value.completedAtUtc) || Date.now() - Date.parse(value.completedAtUtc) > 86400000 || Date.parse(value.completedAtUtc) > Date.now()) fail('Checkpoint must be an exercise completed within the previous 24 hours.');
  return value;
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
  if (value?.version !== 1 || value.phase !== 'exercise' || value.commit !== checkpoint.commit || value.qualification !== 'PILOT_PARTIAL' || value.groupDm !== 'NO_GO' || value.fullPilotGo !== false || !isUtc(value.completedAtUtc) || Date.parse(value.completedAtUtc) < Date.parse(checkpoint.completedAtUtc)) fail('Restart verification requires a successful exercise receipt for the same commit and checkpoint time.');
}

export function isRedactedDenial(result, protectedIds = []) {
  const body = String(result.text ?? '').toLowerCase();
  return [400, 403, 404].includes(result.status) &&
    ['exception', 'stacktrace', 'stack trace', 'system.', 'password=', 'connectionstring', 'postgresql', 'set-cookie:', 'storagekey'].every((marker) => !body.includes(marker)) &&
    protectedIds.every((id) => !body.includes(id.toLowerCase()));
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

export function sanitizedReceipt(configuration, phase) {
  return {
    version: 1, commit: configuration.commit, commitSource: 'OPERATOR_SUPPLIED', phase, completedAtUtc: new Date().toISOString(),
    qualification: 'PILOT_PARTIAL', groupDm: 'NO_GO', fullPilotGo: false,
    restartPersistence: phase === 'verify-restart' ? 'VERIFIED_AFTER_OPERATOR_ATTESTED_RESTART' : 'NOT_EVALUATED',
    crossTenantFixtureExistence: 'OPERATOR_INVENTORY_ATTESTED',
    backupRestore: 'NOT_EVALUATED', targetDeployment: 'NOT_EVALUATED'
  };
}

function isUtc(value) { return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?Z$/u.test(value) && Number.isFinite(Date.parse(value)); }
function required(environment, key) {
  const value = environment[prefix + key];
  if (typeof value !== 'string' || value.trim().length === 0) fail(`${prefix}${key} must be configured.`);
  return value.trim();
}
function fail(message) { throw new Error(message); }
