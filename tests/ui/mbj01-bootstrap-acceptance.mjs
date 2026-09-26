import { mkdir, writeFile } from 'node:fs/promises';
import { request } from '@playwright/test';

const phase = process.argv[2] ?? 'initial';
const baseURL = process.env.PLAYWRIGHT_BASE_URL?.trim();
const email = process.env.COGLATAS_MBJ01_BOOTSTRAP_EMAIL?.trim();
const displayName = process.env.COGLATAS_MBJ01_BOOTSTRAP_DISPLAY_NAME?.trim() || 'MBJ01 Bootstrap Admin';
const password = process.env.COGLATAS_MBJ01_BOOTSTRAP_PASSWORD;

if (!['initial', 'restart'].includes(phase)) {
  throw new Error(`Unknown MBJ-01 acceptance phase '${phase}'.`);
}
if (!baseURL) {
  throw new Error('PLAYWRIGHT_BASE_URL is required for MBJ-01 bootstrap acceptance.');
}
if (!email || !email.toLowerCase().endsWith('@example.test')) {
  throw new Error('COGLATAS_MBJ01_BOOTSTRAP_EMAIL must be a synthetic @example.test address.');
}
if (!password) {
  throw new Error('COGLATAS_MBJ01_BOOTSTRAP_PASSWORD must be supplied at runtime.');
}

const evidence = {
  journey: 'MBJ-01',
  phase,
  baseURL,
  email,
  displayName,
  steps: []
};

const api = await request.newContext({
  baseURL,
  extraHTTPHeaders: {
    'X-Tenant-Slug': 'default'
  }
});

try {
  const ready = await api.get('/health/ready');
  record('health-ready', 'GET', '/health/ready', ready.status());
  requireStatus(ready, 200, 'health readiness');

  const csrfResponse = await api.get('/api/security/csrf-token');
  record('csrf-token', 'GET', '/api/security/csrf-token', csrfResponse.status(), '[redacted]');
  requireStatus(csrfResponse, 200, 'CSRF token');
  const csrf = await readJson(csrfResponse, 'CSRF token response');
  const csrfToken = requiredString(csrf, 'token', 'CSRF token response');
  const csrfHeaderName = requiredString(csrf, 'headerName', 'CSRF token response');

  const loginResponse = await api.post('/api/auth/login', {
    data: { email, password },
    headers: { [csrfHeaderName]: csrfToken }
  });
  record('bootstrap-admin-login', 'POST', '/api/auth/login', loginResponse.status(), '[credential body redacted]');
  requireStatus(loginResponse, 200, 'bootstrap administrator login');
  const login = await readJson(loginResponse, 'bootstrap administrator login response');

  const userId = requiredString(login, 'userId', 'bootstrap administrator login response');
  requireEqual(login.email, email, 'login email');
  requireEqual(login.displayName, displayName, 'login display name');
  requireArray(login.workspaces, 'login workspaces');
  requireEqual(login.workspaces.length, 1, 'bootstrap administrator Workspace count');
  const loginWorkspace = login.workspaces[0];
  const workspaceId = requiredString(loginWorkspace, 'id', 'bootstrap administrator Workspace');
  requireEqual(loginWorkspace.name, 'Default Workspace', 'bootstrap administrator Workspace name');
  if (!login.currentWorkspace || login.currentWorkspace.id !== workspaceId) {
    throw new Error('Bootstrap administrator currentWorkspace does not match the persisted default Workspace.');
  }

  const meResponse = await api.get('/api/auth/me');
  record('auth-me', 'GET', '/api/auth/me', meResponse.status());
  requireStatus(meResponse, 200, 'authenticated current-user response');
  const me = await readJson(meResponse, 'authenticated current-user response');
  requireEqual(me.userId, userId, 'current-user id');
  requireEqual(me.email, email, 'current-user email');
  requireEqual(me.displayName, displayName, 'current-user display name');
  requireArray(me.workspaces, 'current-user workspaces');
  if (!me.workspaces.some((workspace) => workspace?.id === workspaceId && workspace?.name === 'Default Workspace')) {
    throw new Error('Current-user response does not contain the persisted default Workspace membership.');
  }

  const workspacesResponse = await api.get('/api/workspaces');
  record('workspace-list', 'GET', '/api/workspaces', workspacesResponse.status());
  requireStatus(workspacesResponse, 200, 'authorized Workspace list');
  const workspaces = await readJson(workspacesResponse, 'authorized Workspace list');
  requireArray(workspaces, 'authorized Workspace list');
  requireEqual(workspaces.length, 1, 'authorized Workspace count');
  if (!workspaces.some((workspace) => workspace?.id === workspaceId && workspace?.name === 'Default Workspace')) {
    throw new Error('Authorized Workspace list does not contain the bootstrap default Workspace.');
  }

  evidence.userId = userId;
  evidence.workspaceId = workspaceId;
  evidence.workspaceCount = workspaces.length;
  evidence.loginSucceeded = true;
  evidence.passwordMaterialRecorded = false;

  await mkdir('test-results', { recursive: true });
  await writeFile(
    `test-results/mbj01-bootstrap-${phase}.json`,
    `${JSON.stringify(evidence, null, 2)}\n`,
    'utf8'
  );
  console.log(
    `MBJ-01 ${phase} runtime probe passed: bootstrap administrator authenticated with one persisted default Workspace.`
  );
} finally {
  await api.dispose();
}

function record(name, method, path, status, bodyPreview) {
  evidence.steps.push({
    name,
    method,
    path,
    status,
    ...(bodyPreview ? { bodyPreview } : {})
  });
}

function requireStatus(response, expected, label) {
  if (response.status() !== expected) {
    throw new Error(`${label} returned HTTP ${response.status()}, expected ${expected}.`);
  }
}

function requireEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label} mismatch: got ${JSON.stringify(actual)}, expected ${JSON.stringify(expected)}.`);
  }
}

function requireArray(value, label) {
  if (!Array.isArray(value)) {
    throw new Error(`${label} is not an array.`);
  }
}

function requiredString(value, property, label) {
  const result = value && typeof value === 'object' ? value[property] : undefined;
  if (typeof result !== 'string' || result.length === 0) {
    throw new Error(`${label} is missing required string property '${property}'.`);
  }
  return result;
}

async function readJson(response, label) {
  try {
    return await response.json();
  } catch {
    throw new Error(`${label} is not valid JSON.`);
  }
}
