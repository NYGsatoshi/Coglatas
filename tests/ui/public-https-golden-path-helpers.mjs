import { isIP } from 'node:net';

const publicSmokeMarker = 'COGLATAS_PUBLIC_HTTPS_SMOKE';
const syntheticFixtureMarker = 'COGLATAS_PUBLIC_SMOKE_SYNTHETIC_FIXTURE';
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

const fixtureIdKeys = Object.freeze([
  'COGLATAS_PUBLIC_SMOKE_WORKSPACE_ID',
  'COGLATAS_PUBLIC_SMOKE_PROJECT_ID',
  'COGLATAS_PUBLIC_SMOKE_TASK_ID',
  'COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_WORKSPACE_ID',
  'COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_PROJECT_ID',
  'COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_TASK_ID',
  'COGLATAS_PUBLIC_SMOKE_REVOKED_FILE_ID'
]);

export function readPublicHttpsSmokeConfiguration(environment) {
  if (environment[publicSmokeMarker] !== '1') {
    throw new Error(`${publicSmokeMarker}=1 is required for the public HTTPS Golden Path.`);
  }

  if (environment[syntheticFixtureMarker] !== '1') {
    throw new Error(`${syntheticFixtureMarker}=1 is required to acknowledge the dedicated synthetic fixture.`);
  }

  const baseURL = publicHttpsOrigin(required(environment, 'COGLATAS_PUBLIC_SMOKE_URL'));
  const email = required(environment, 'COGLATAS_PUBLIC_SMOKE_EMAIL');
  const password = required(environment, 'COGLATAS_PUBLIC_SMOKE_PASSWORD');

  if (!email.toLowerCase().endsWith('@example.test')) {
    throw new Error('COGLATAS_PUBLIC_SMOKE_EMAIL must use the dedicated synthetic @example.test account.');
  }

  const ids = Object.fromEntries(
    fixtureIdKeys.map((key) => [key, requiredUuid(environment, key)])
  );

  return Object.freeze({
    baseURL,
    email,
    password,
    workspaceId: ids.COGLATAS_PUBLIC_SMOKE_WORKSPACE_ID,
    projectId: ids.COGLATAS_PUBLIC_SMOKE_PROJECT_ID,
    taskId: ids.COGLATAS_PUBLIC_SMOKE_TASK_ID,
    unauthorizedWorkspaceId: ids.COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_WORKSPACE_ID,
    unauthorizedProjectId: ids.COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_PROJECT_ID,
    unauthorizedTaskId: ids.COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_TASK_ID,
    revokedFileId: ids.COGLATAS_PUBLIC_SMOKE_REVOKED_FILE_ID
  });
}

export function publicHttpsOrigin(value) {
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error('COGLATAS_PUBLIC_SMOKE_URL must be an absolute public HTTPS URL.');
  }

  if (url.protocol !== 'https:') {
    throw new Error('COGLATAS_PUBLIC_SMOKE_URL must use HTTPS.');
  }

  if (
    url.username ||
    url.password ||
    url.search ||
    url.hash ||
    url.pathname !== '/' ||
    url.port
  ) {
    throw new Error('COGLATAS_PUBLIC_SMOKE_URL must be a root HTTPS origin without credentials, query, fragment, or a non-standard port.');
  }

  const hostname = url.hostname.toLowerCase();
  if (isLocalHostname(hostname)) {
    throw new Error('COGLATAS_PUBLIC_SMOKE_URL must not target localhost, a loopback address, or a private network address.');
  }

  return url.origin;
}

export function isUuid(value) {
  return typeof value === 'string' && uuidPattern.test(value);
}

function required(environment, key) {
  const value = environment[key];
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${key} must be configured as a protected deployment-gate value.`);
  }
  return value.trim();
}

function requiredUuid(environment, key) {
  const value = required(environment, key);
  if (!isUuid(value)) {
    throw new Error(`${key} must be a UUID for the dedicated synthetic fixture.`);
  }
  return value;
}

function isLocalHostname(hostname) {
  if (hostname === 'localhost' || hostname.endsWith('.localhost')) {
    return true;
  }

  const kind = isIP(hostname);
  if (kind === 4) {
    const octets = hostname.split('.').map(Number);
    return (
      octets[0] === 0 ||
      octets[0] === 10 ||
      octets[0] === 127 ||
      (octets[0] === 169 && octets[1] === 254) ||
      (octets[0] === 172 && octets[1] >= 16 && octets[1] <= 31) ||
      (octets[0] === 192 && octets[1] === 168)
    );
  }

  if (kind === 6) {
    return hostname === '::1' || hostname.startsWith('fe80:') || hostname.startsWith('fc') || hostname.startsWith('fd');
  }

  return false;
}
