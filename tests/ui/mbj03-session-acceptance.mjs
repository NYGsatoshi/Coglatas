import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { request } from '@playwright/test';

const phase = process.argv[2];
if (!['phase1', 'phase2', 'phase3'].includes(phase)) {
  throw new Error('MBJ-03 probe requires phase1, phase2, or phase3.');
}

const baseURL = requiredEnv('PLAYWRIGHT_BASE_URL');
const adminEmail = requiredSyntheticEmail('COGLATAS_MBJ03_ADMIN_EMAIL');
const adminDisplayName = process.env.COGLATAS_MBJ03_ADMIN_DISPLAY_NAME?.trim() || 'MBJ03 System Admin';
const adminPassword = requiredEnv('COGLATAS_MBJ03_ADMIN_PASSWORD');
const subjectEmail = requiredSyntheticEmail('COGLATAS_MBJ03_SUBJECT_EMAIL');
const subjectDisplayName = process.env.COGLATAS_MBJ03_SUBJECT_DISPLAY_NAME?.trim() || 'MBJ03 Session Subject';
const oldPassword = requiredEnv('COGLATAS_MBJ03_OLD_PASSWORD');
const newPassword = requiredEnv('COGLATAS_MBJ03_NEW_PASSWORD');

if (oldPassword === newPassword) {
  throw new Error('MBJ-03 old and new passwords must differ.');
}

const privateDir = 'test-results/.mbj03-private';
const phase1EvidencePath = 'test-results/mbj03-phase1-runtime.json';
const adminStatePath = path.join(privateDir, 'admin.json');
const subjectAStatePath = path.join(privateDir, 'subject-a.json');
const subjectCStatePath = path.join(privateDir, 'subject-c.json');
const subjectDStatePath = path.join(privateDir, 'subject-d.json');

if (phase === 'phase1') {
  await runPhase1();
} else if (phase === 'phase2') {
  await runPhase2();
} else {
  await runPhase3();
}

async function runPhase1() {
  const evidence = newEvidence('phase1');
  await mkdir(privateDir, { recursive: true });

  const admin = await newApiContext();
  const subjectA = await newApiContext();
  const subjectB = await newApiContext();
  const oldPasswordAttempt = await newApiContext();
  const subjectC = await newApiContext();

  try {
    await expectReady(admin, evidence);

    const adminLogin = await login(admin, adminEmail, adminPassword, evidence, 'administrator-login');
    requireEqual(adminLogin.email, adminEmail, 'administrator login email');
    requireEqual(adminLogin.displayName, adminDisplayName, 'administrator display name');
    requireArray(adminLogin.workspaces, 'administrator workspaces');
    if (adminLogin.workspaces.length !== 1) {
      throw new Error(`MBJ-03 requires exactly one bootstrap Workspace; got ${adminLogin.workspaces.length}.`);
    }
    const workspaceId = requiredString(adminLogin.workspaces[0], 'id', 'administrator Workspace');

    const inviteCsrf = await getCsrf(admin, evidence, 'administrator-invite-csrf');
    const inviteResponse = await admin.post('/api/admin/invites', {
      data: { workspaceId, email: subjectEmail, role: 3, expiresAt: null },
      headers: { [inviteCsrf.headerName]: inviteCsrf.token }
    });
    record(evidence, 'administrator-create-subject-invite', 'POST', '/api/admin/invites', inviteResponse.status(), '[invite token redacted]');
    requireStatus(inviteResponse, 200, 'administrator subject invite creation');
    const invite = await readJson(inviteResponse, 'administrator subject invite response');
    const inviteToken = extractInviteToken(invite, 'administrator subject invite response');

    const acceptCsrf = await getCsrf(subjectA, evidence, 'subject-accept-csrf');
    const acceptedResponse = await subjectA.post('/api/invites/accept', {
      data: { token: inviteToken, displayName: subjectDisplayName, password: oldPassword },
      headers: { [acceptCsrf.headerName]: acceptCsrf.token }
    });
    record(evidence, 'subject-accept-invite', 'POST', '/api/invites/accept', acceptedResponse.status(), '[invite token and password redacted]');
    requireStatus(acceptedResponse, 200, 'subject invite acceptance');
    const accepted = await readJson(acceptedResponse, 'subject invite acceptance response');
    const subjectUserId = requiredString(accepted, 'userId', 'subject invite acceptance response');
    requireEqual(accepted.email, subjectEmail, 'accepted subject email');

    const secondLogin = await login(subjectB, subjectEmail, oldPassword, evidence, 'subject-second-session-login');
    requireEqual(secondLogin.userId, subjectUserId, 'second session user id');

    const missingCsrf = await subjectA.post('/api/auth/change-password', {
      data: { currentPassword: oldPassword, newPassword }
    });
    record(evidence, 'change-password-missing-csrf-rejected', 'POST', '/api/auth/change-password', missingCsrf.status(), '[password body redacted]');
    requireStatus(missingCsrf, 403, 'missing-CSRF password change');

    const passwordCsrf = await getCsrf(subjectA, evidence, 'change-password-csrf');
    const passwordChange = await subjectA.post('/api/auth/change-password', {
      data: { currentPassword: oldPassword, newPassword },
      headers: { [passwordCsrf.headerName]: passwordCsrf.token }
    });
    record(evidence, 'password-change', 'POST', '/api/auth/change-password', passwordChange.status(), '[password body redacted]');
    requireStatus(passwordChange, 200, 'password change');

    await expectMe(subjectA, 200, evidence, 'password-change-current-session-remains-valid', subjectUserId);
    await expectMe(subjectB, 401, evidence, 'password-change-other-session-revoked');

    await login(oldPasswordAttempt, subjectEmail, oldPassword, evidence, 'old-password-login-rejected', 401);

    const newPasswordLogin = await login(subjectC, subjectEmail, newPassword, evidence, 'new-password-login');
    requireEqual(newPasswordLogin.userId, subjectUserId, 'new-password login user id');
    await expectMe(subjectC, 200, evidence, 'new-password-session-me', subjectUserId);

    await expectHubNegotiate(subjectA, 200, evidence, 'current-session-hub-negotiate');
    await expectHubNegotiate(subjectC, 200, evidence, 'new-session-hub-negotiate');

    await admin.storageState({ path: adminStatePath });
    await subjectA.storageState({ path: subjectAStatePath });
    await subjectC.storageState({ path: subjectCStatePath });

    evidence.subjectUserId = subjectUserId;
    evidence.workspaceId = workspaceId;
    evidence.privateSessionStatePersistedForRestart = true;
    await writeEvidence(phase1EvidencePath, evidence);
  } finally {
    await Promise.all([
      admin.dispose(),
      subjectA.dispose(),
      subjectB.dispose(),
      oldPasswordAttempt.dispose(),
      subjectC.dispose()
    ]);
  }
}

async function runPhase2() {
  const phase1Evidence = JSON.parse(await readFile(phase1EvidencePath, 'utf8'));
  const subjectUserId = requiredString(phase1Evidence, 'subjectUserId', 'phase1 evidence');
  const evidence = newEvidence('phase2');

  const admin = await newApiContext(adminStatePath);
  const subjectA = await newApiContext(subjectAStatePath);
  const subjectC = await newApiContext(subjectCStatePath);
  const suspendedLoginAttempt = await newApiContext();
  const subjectD = await newApiContext();

  try {
    await expectReady(admin, evidence);
    await expectMe(admin, 200, evidence, 'administrator-cookie-survives-restart');
    await expectMe(subjectA, 200, evidence, 'password-change-current-session-survives-restart', subjectUserId);
    await expectMe(subjectC, 200, evidence, 'new-password-session-survives-restart', subjectUserId);
    await expectHubNegotiate(subjectC, 200, evidence, 'post-restart-hub-negotiate');

    const logoutCsrf = await getCsrf(subjectA, evidence, 'logout-csrf');
    const logout = await subjectA.post('/api/auth/logout', {
      data: {},
      headers: { [logoutCsrf.headerName]: logoutCsrf.token }
    });
    record(evidence, 'logout', 'POST', '/api/auth/logout', logout.status());
    requireStatus(logout, 200, 'logout');
    await expectMe(subjectA, 401, evidence, 'logout-revokes-current-session');

    const suspendCsrf = await getCsrf(admin, evidence, 'administrator-suspend-csrf');
    const suspend = await admin.post(`/api/admin/users/${subjectUserId}/suspend`, {
      data: {},
      headers: { [suspendCsrf.headerName]: suspendCsrf.token }
    });
    record(evidence, 'administrator-suspend-user', 'POST', '/api/admin/users/{id}/suspend', suspend.status());
    requireStatus(suspend, 200, 'administrator suspend user');

    await expectMe(subjectC, 401, evidence, 'suspension-revokes-existing-session');
    await expectHubNegotiate(subjectC, 401, evidence, 'suspended-session-hub-rejected');
    await login(suspendedLoginAttempt, subjectEmail, newPassword, evidence, 'suspended-user-login-rejected', 401);

    const activateCsrf = await getCsrf(admin, evidence, 'administrator-activate-csrf');
    const activate = await admin.post(`/api/admin/users/${subjectUserId}/activate`, {
      data: {},
      headers: { [activateCsrf.headerName]: activateCsrf.token }
    });
    record(evidence, 'administrator-activate-user', 'POST', '/api/admin/users/{id}/activate', activate.status());
    requireStatus(activate, 200, 'administrator activate user');

    const reactivatedLogin = await login(subjectD, subjectEmail, newPassword, evidence, 'reactivated-user-login');
    requireEqual(reactivatedLogin.userId, subjectUserId, 'reactivated login user id');
    await expectMe(subjectD, 200, evidence, 'reactivated-session-me', subjectUserId);
    await expectHubNegotiate(subjectD, 200, evidence, 'reactivated-session-hub-negotiate');
    await subjectD.storageState({ path: subjectDStatePath });

    evidence.subjectUserId = subjectUserId;
    evidence.reactivatedSessionStatePersistedForExpiryProbe = true;
    await writeEvidence('test-results/mbj03-phase2-runtime.json', evidence);
  } finally {
    await Promise.all([
      admin.dispose(),
      subjectA.dispose(),
      subjectC.dispose(),
      suspendedLoginAttempt.dispose(),
      subjectD.dispose()
    ]);
  }
}

async function runPhase3() {
  const phase1Evidence = JSON.parse(await readFile(phase1EvidencePath, 'utf8'));
  const subjectUserId = requiredString(phase1Evidence, 'subjectUserId', 'phase1 evidence');
  const evidence = newEvidence('phase3');

  const expiredSession = await newApiContext(subjectDStatePath);
  const freshSession = await newApiContext();

  try {
    await expectMe(expiredSession, 401, evidence, 'database-expired-session-rejected');
    await expectHubNegotiate(expiredSession, 401, evidence, 'expired-session-hub-rejected');

    const loginBody = await login(freshSession, subjectEmail, newPassword, evidence, 'fresh-login-after-expiry');
    requireEqual(loginBody.userId, subjectUserId, 'fresh login after expiry user id');
    await expectMe(freshSession, 200, evidence, 'fresh-session-after-expiry-me', subjectUserId);
    await expectHubNegotiate(freshSession, 200, evidence, 'fresh-session-after-expiry-hub-negotiate');

    evidence.subjectUserId = subjectUserId;
    await writeEvidence('test-results/mbj03-phase3-runtime.json', evidence);
  } finally {
    await Promise.all([expiredSession.dispose(), freshSession.dispose()]);
  }
}

async function newApiContext(storageState) {
  return request.newContext({
    baseURL,
    storageState,
    extraHTTPHeaders: {
      'X-Tenant-Slug': 'default'
    }
  });
}

async function expectReady(api, evidence) {
  const response = await api.get('/health/ready');
  record(evidence, 'health-ready', 'GET', '/health/ready', response.status());
  requireStatus(response, 200, 'health readiness');
}

async function login(api, email, password, evidence, name, expectedStatus = 200) {
  const csrf = await getCsrf(api, evidence, `${name}-csrf`);
  const response = await api.post('/api/auth/login', {
    data: { email, password },
    headers: { [csrf.headerName]: csrf.token }
  });
  record(evidence, name, 'POST', '/api/auth/login', response.status(), '[credential body redacted]');
  requireStatus(response, expectedStatus, name);
  return expectedStatus === 200 ? readJson(response, `${name} response`) : null;
}

async function expectMe(api, expectedStatus, evidence, name, expectedUserId) {
  const response = await api.get('/api/auth/me');
  record(evidence, name, 'GET', '/api/auth/me', response.status());
  requireStatus(response, expectedStatus, name);
  if (expectedStatus === 200) {
    const body = await readJson(response, `${name} response`);
    if (expectedUserId) {
      requireEqual(body.userId, expectedUserId, `${name} user id`);
    }
    return body;
  }
  return null;
}

async function expectHubNegotiate(api, expectedStatus, evidence, name) {
  const csrf = await getCsrf(api, evidence, `${name}-csrf`);
  const response = await api.post('/hubs/app/negotiate?negotiateVersion=1', {
    headers: { [csrf.headerName]: csrf.token }
  });
  record(evidence, name, 'POST', '/hubs/app/negotiate?negotiateVersion=1', response.status());
  requireStatus(response, expectedStatus, name);
}

async function getCsrf(api, evidence, name) {
  const response = await api.get('/api/security/csrf-token');
  record(evidence, name, 'GET', '/api/security/csrf-token', response.status(), '[CSRF token redacted]');
  requireStatus(response, 200, `${name} response`);
  const body = await readJson(response, `${name} response`);
  return {
    token: requiredString(body, 'token', `${name} response`),
    headerName: requiredString(body, 'headerName', `${name} response`)
  };
}

function extractInviteToken(invite, label) {
  const inviteUrl = requiredString(invite, 'inviteUrl', label);
  let token;
  try {
    token = new URL(inviteUrl, baseURL).searchParams.get('token');
  } catch {
    throw new Error(`${label} has an invalid inviteUrl.`);
  }
  if (!token) {
    throw new Error(`${label} inviteUrl does not contain a token.`);
  }
  return token;
}

function newEvidence(phaseName) {
  return {
    journey: 'MBJ-03',
    phase: phaseName,
    baseURL,
    syntheticEmails: {
      administrator: adminEmail,
      subject: subjectEmail
    },
    steps: [],
    secretMaterialRecorded: false
  };
}

function record(evidence, name, method, requestPath, status, bodyPreview) {
  evidence.steps.push({
    name,
    method,
    path: requestPath,
    status,
    ...(bodyPreview ? { bodyPreview } : {})
  });
}

async function writeEvidence(filePath, evidence) {
  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(filePath, `${JSON.stringify(evidence, null, 2)}\n`, 'utf8');
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

function requiredEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`${name} is required for MBJ-03 session lifecycle acceptance.`);
  }
  return value;
}

function requiredSyntheticEmail(name) {
  const value = requiredEnv(name);
  if (!value.toLowerCase().endsWith('@example.test')) {
    throw new Error(`${name} must use the synthetic @example.test domain.`);
  }
  return value;
}

async function readJson(response, label) {
  try {
    return await response.json();
  } catch {
    throw new Error(`${label} is not valid JSON.`);
  }
}
