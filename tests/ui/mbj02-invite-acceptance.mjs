import { mkdir, writeFile } from 'node:fs/promises';
import { request } from '@playwright/test';

const baseURL = requiredEnv('PLAYWRIGHT_BASE_URL');
const adminEmail = requiredSyntheticEmail('COGLATAS_MBJ02_ADMIN_EMAIL');
const adminDisplayName = process.env.COGLATAS_MBJ02_ADMIN_DISPLAY_NAME?.trim() || 'MBJ02 System Admin';
const adminPassword = requiredEnv('COGLATAS_MBJ02_ADMIN_PASSWORD');
const inviteeEmail = requiredSyntheticEmail('COGLATAS_MBJ02_INVITEE_EMAIL');
const inviteeDisplayName = process.env.COGLATAS_MBJ02_INVITEE_DISPLAY_NAME?.trim() || 'MBJ02 Invited User';
const inviteePassword = requiredEnv('COGLATAS_MBJ02_INVITEE_PASSWORD');
const revokedEmail = requiredSyntheticEmail('COGLATAS_MBJ02_REVOKED_EMAIL');
const expiredEmail = requiredSyntheticEmail('COGLATAS_MBJ02_EXPIRED_EMAIL');
const mismatchTargetEmail = requiredSyntheticEmail('COGLATAS_MBJ02_MISMATCH_TARGET_EMAIL');
const mismatchOtherEmail = requiredSyntheticEmail('COGLATAS_MBJ02_MISMATCH_OTHER_EMAIL');
const crossTenantEmail = requiredSyntheticEmail('COGLATAS_MBJ02_CROSS_TENANT_EMAIL');
const crossTenantToken = requiredEnv('COGLATAS_MBJ02_CROSS_TENANT_TOKEN');
const crossTenantWorkspaceId = requiredEnv('COGLATAS_MBJ02_CROSS_TENANT_WORKSPACE_ID');
const workspaceRoleMember = 3;

const evidence = {
  journey: 'MBJ-02',
  baseURL,
  syntheticEmails: {
    administrator: adminEmail,
    acceptedInvitee: inviteeEmail,
    revokedInvitee: revokedEmail,
    expiredInvitee: expiredEmail,
    mismatchTarget: mismatchTargetEmail,
    mismatchOther: mismatchOtherEmail,
    crossTenantInvitee: crossTenantEmail
  },
  steps: [],
  secretMaterialRecorded: false
};

const admin = await newApiContext();
try {
  const ready = await admin.get('/health/ready');
  record('health-ready', 'GET', '/health/ready', ready.status());
  requireStatus(ready, 200, 'health readiness');

  const adminCsrf = await getCsrf(admin, 'admin-login-csrf');
  const loginResponse = await admin.post('/api/auth/login', {
    data: { email: adminEmail, password: adminPassword },
    headers: { [adminCsrf.headerName]: adminCsrf.token }
  });
  record('administrator-login', 'POST', '/api/auth/login', loginResponse.status(), '[credential body redacted]');
  requireStatus(loginResponse, 200, 'administrator login');
  const adminLogin = await readJson(loginResponse, 'administrator login response');
  const adminUserId = requiredString(adminLogin, 'userId', 'administrator login response');
  requireEqual(adminLogin.email, adminEmail, 'administrator login email');
  requireEqual(adminLogin.displayName, adminDisplayName, 'administrator display name');
  requireArray(adminLogin.workspaces, 'administrator workspaces');
  if (adminLogin.workspaces.length !== 1) {
    throw new Error(`MBJ-02 requires exactly one bootstrap Workspace; got ${adminLogin.workspaces.length}.`);
  }
  const workspaceId = requiredString(adminLogin.workspaces[0], 'id', 'administrator Workspace');

  const createCsrf = await getCsrf(admin, 'admin-create-csrf');

  const acceptedInvite = await createInvite(admin, createCsrf, {
    workspaceId,
    email: inviteeEmail,
    role: workspaceRoleMember,
    expiresAt: null
  }, 'accepted-invite-create');
  const acceptedInviteId = requiredString(acceptedInvite, 'id', 'accepted invite response');
  const acceptedToken = extractInviteToken(acceptedInvite, 'accepted invite response');
  requireEqual(acceptedInvite.email, inviteeEmail, 'accepted invite email');
  requireEqual(acceptedInvite.workspaceId, workspaceId, 'accepted invite Workspace');
  requireEqual(acceptedInvite.role, 'Member', 'accepted invite role');

  const invited = await newApiContext();
  try {
    const validate = await invited.get(`/api/invites/validate?token=${encodeURIComponent(acceptedToken)}`);
    record('anonymous-validate', 'GET', '/api/invites/validate?token=[redacted]', validate.status());
    requireStatus(validate, 200, 'anonymous invite validation');
    const validation = await readJson(validate, 'anonymous invite validation response');
    requireEqual(validation.valid, true, 'invite validation valid flag');
    requireEqual(validation.email, inviteeEmail, 'invite validation email');
    requireEqual(validation.role, 'Member', 'invite validation role');

    const inviteeCsrf = await getCsrf(invited, 'invitee-accept-csrf');
    const accept = await invited.post('/api/invites/accept', {
      data: { token: acceptedToken, displayName: inviteeDisplayName, password: inviteePassword },
      headers: { [inviteeCsrf.headerName]: inviteeCsrf.token }
    });
    record('anonymous-accept', 'POST', '/api/invites/accept', accept.status(), '[invite token and password redacted]');
    requireStatus(accept, 200, 'anonymous invite acceptance');
    const accepted = await readJson(accept, 'anonymous invite acceptance response');
    const inviteeUserId = requiredString(accepted, 'userId', 'anonymous invite acceptance response');
    requireEqual(accepted.email, inviteeEmail, 'accepted user email');
    requireEqual(accepted.displayName, inviteeDisplayName, 'accepted user display name');
    requireArray(accepted.workspaces, 'accepted user workspaces');
    if (!accepted.workspaces.some((workspace) => workspace?.id === workspaceId)) {
      throw new Error('Accepted user response does not contain the invited Workspace.');
    }
    if (!accepted.currentWorkspace || accepted.currentWorkspace.id !== workspaceId) {
      throw new Error('Accepted user currentWorkspace does not match the invited Workspace.');
    }

    const me = await invited.get('/api/auth/me');
    record('accepted-session-me', 'GET', '/api/auth/me', me.status());
    requireStatus(me, 200, 'accepted session current-user lookup');
    const currentUser = await readJson(me, 'accepted session current-user response');
    requireEqual(currentUser.userId, inviteeUserId, 'accepted session user id');
    requireEqual(currentUser.email, inviteeEmail, 'accepted session email');

    evidence.accepted = {
      inviteId: acceptedInviteId,
      userId: inviteeUserId,
      workspaceId
    };
  } finally {
    await invited.dispose();
  }

  const reuse = await newApiContext();
  try {
    const reuseCsrf = await getCsrf(reuse, 'reuse-csrf');
    const reused = await reuse.post('/api/invites/accept', {
      data: { token: acceptedToken, displayName: 'Reuse Attempt', password: inviteePassword },
      headers: { [reuseCsrf.headerName]: reuseCsrf.token }
    });
    record('used-invite-rejected', 'POST', '/api/invites/accept', reused.status(), '[invite token and password redacted]');
    requireStatus(reused, 404, 'used invite reuse rejection');
  } finally {
    await reuse.dispose();
  }

  const loginAfterAccept = await newApiContext();
  try {
    const loginCsrf = await getCsrf(loginAfterAccept, 'post-accept-login-csrf');
    const login = await loginAfterAccept.post('/api/auth/login', {
      data: { email: inviteeEmail, password: inviteePassword },
      headers: { [loginCsrf.headerName]: loginCsrf.token }
    });
    record('post-accept-login', 'POST', '/api/auth/login', login.status(), '[credential body redacted]');
    requireStatus(login, 200, 'post-accept login');
    const loginBody = await readJson(login, 'post-accept login response');
    requireEqual(loginBody.userId, evidence.accepted.userId, 'post-accept login user id');
    requireEqual(loginBody.email, inviteeEmail, 'post-accept login email');

    const postLoginMe = await loginAfterAccept.get('/api/auth/me');
    record('post-accept-login-me', 'GET', '/api/auth/me', postLoginMe.status());
    requireStatus(postLoginMe, 200, 'post-accept login current-user lookup');
    const postLoginCurrentUser = await readJson(postLoginMe, 'post-accept login current-user response');
    requireEqual(postLoginCurrentUser.userId, evidence.accepted.userId, 'post-accept login current-user id');
  } finally {
    await loginAfterAccept.dispose();
  }

  const revokedInvite = await createInvite(admin, createCsrf, {
    workspaceId,
    email: revokedEmail,
    role: workspaceRoleMember,
    expiresAt: null
  }, 'revoked-invite-create');
  const revokedInviteId = requiredString(revokedInvite, 'id', 'revoked invite response');
  const revokedToken = extractInviteToken(revokedInvite, 'revoked invite response');
  const revoke = await admin.post(`/api/admin/invites/${revokedInviteId}/revoke`, {
    headers: { [createCsrf.headerName]: createCsrf.token }
  });
  record('administrator-revoke', 'POST', '/api/admin/invites/{id}/revoke', revoke.status());
  requireStatus(revoke, 200, 'administrator invite revoke');

  const revokedAnonymous = await newApiContext();
  try {
    const validateRevoked = await revokedAnonymous.get(`/api/invites/validate?token=${encodeURIComponent(revokedToken)}`);
    record('revoked-validate-rejected', 'GET', '/api/invites/validate?token=[redacted]', validateRevoked.status());
    requireStatus(validateRevoked, 404, 'revoked invite validation rejection');
    const revokedCsrf = await getCsrf(revokedAnonymous, 'revoked-accept-csrf');
    const acceptRevoked = await revokedAnonymous.post('/api/invites/accept', {
      data: { token: revokedToken, displayName: 'Revoked User', password: inviteePassword },
      headers: { [revokedCsrf.headerName]: revokedCsrf.token }
    });
    record('revoked-accept-rejected', 'POST', '/api/invites/accept', acceptRevoked.status(), '[invite token and password redacted]');
    requireStatus(acceptRevoked, 404, 'revoked invite acceptance rejection');
  } finally {
    await revokedAnonymous.dispose();
  }
  evidence.revokedInviteId = revokedInviteId;

  const expiresAt = new Date(Date.now() + 5000).toISOString();
  const expiredInvite = await createInvite(admin, createCsrf, {
    workspaceId,
    email: expiredEmail,
    role: workspaceRoleMember,
    expiresAt
  }, 'expired-invite-create');
  const expiredInviteId = requiredString(expiredInvite, 'id', 'expired invite response');
  const expiredToken = extractInviteToken(expiredInvite, 'expired invite response');

  const expiredAnonymous = await newApiContext();
  try {
    const deadline = Date.now() + 15000;
    let finalValidationStatus = 0;
    while (Date.now() < deadline) {
      const response = await expiredAnonymous.get(`/api/invites/validate?token=${encodeURIComponent(expiredToken)}`);
      finalValidationStatus = response.status();
      if (finalValidationStatus === 404) {
        break;
      }
      if (finalValidationStatus !== 200) {
        throw new Error(`expired invite validation returned unexpected HTTP ${finalValidationStatus}.`);
      }
      await delay(500);
    }
    record('expired-validate-rejected', 'GET', '/api/invites/validate?token=[redacted]', finalValidationStatus);
    if (finalValidationStatus !== 404) {
      throw new Error('Invite did not transition to expired state within the bounded acceptance window.');
    }

    const expiredCsrf = await getCsrf(expiredAnonymous, 'expired-accept-csrf');
    const acceptExpired = await expiredAnonymous.post('/api/invites/accept', {
      data: { token: expiredToken, displayName: 'Expired User', password: inviteePassword },
      headers: { [expiredCsrf.headerName]: expiredCsrf.token }
    });
    record('expired-accept-rejected', 'POST', '/api/invites/accept', acceptExpired.status(), '[invite token and password redacted]');
    requireStatus(acceptExpired, 404, 'expired invite acceptance rejection');
  } finally {
    await expiredAnonymous.dispose();
  }
  evidence.expiredInviteId = expiredInviteId;

  const mismatchInvite = await createInvite(admin, createCsrf, {
    workspaceId,
    email: mismatchTargetEmail,
    role: workspaceRoleMember,
    expiresAt: null
  }, 'mismatch-invite-create');
  const mismatchInviteId = requiredString(mismatchInvite, 'id', 'mismatch invite response');
  const mismatchToken = extractInviteToken(mismatchInvite, 'mismatch invite response');
  const mismatchAnonymous = await newApiContext();
  try {
    const mismatchCsrf = await getCsrf(mismatchAnonymous, 'mismatch-register-csrf');
    const mismatch = await mismatchAnonymous.post('/api/auth/register-by-invite', {
      data: {
        inviteToken: mismatchToken,
        displayName: 'Mismatch Attempt',
        email: mismatchOtherEmail,
        password: inviteePassword
      },
      headers: { [mismatchCsrf.headerName]: mismatchCsrf.token }
    });
    record('mismatched-email-rejected', 'POST', '/api/auth/register-by-invite', mismatch.status(), '[invite token, email and password body redacted]');
    requireStatus(mismatch, 404, 'mismatched email invite registration rejection');

    const stillValid = await mismatchAnonymous.get(`/api/invites/validate?token=${encodeURIComponent(mismatchToken)}`);
    record('mismatch-invite-remains-unused', 'GET', '/api/invites/validate?token=[redacted]', stillValid.status());
    requireStatus(stillValid, 200, 'mismatched email invite must remain unused');
  } finally {
    await mismatchAnonymous.dispose();
  }
  evidence.mismatchInviteId = mismatchInviteId;

  const foreignWorkspaceAttempt = await admin.post('/api/admin/invites', {
    data: {
      workspaceId: crossTenantWorkspaceId,
      email: 'mbj02-cross-admin-attempt@example.test',
      role: workspaceRoleMember,
      expiresAt: null
    },
    headers: { [createCsrf.headerName]: createCsrf.token }
  });
  record('cross-tenant-admin-create-rejected', 'POST', '/api/admin/invites', foreignWorkspaceAttempt.status());
  requireStatus(foreignWorkspaceAttempt, 400, 'cross-tenant Workspace invite creation rejection');

  const crossTenantAnonymous = await newApiContext();
  try {
    const crossValidate = await crossTenantAnonymous.get(`/api/invites/validate?token=${encodeURIComponent(crossTenantToken)}`);
    record('cross-tenant-validate-rejected', 'GET', '/api/invites/validate?token=[redacted]', crossValidate.status());
    requireStatus(crossValidate, 404, 'cross-tenant invite validation rejection');

    const crossCsrf = await getCsrf(crossTenantAnonymous, 'cross-tenant-accept-csrf');
    const crossAccept = await crossTenantAnonymous.post('/api/invites/accept', {
      data: { token: crossTenantToken, displayName: 'Cross Tenant Attempt', password: inviteePassword },
      headers: { [crossCsrf.headerName]: crossCsrf.token }
    });
    record('cross-tenant-accept-rejected', 'POST', '/api/invites/accept', crossAccept.status(), '[invite token and password redacted]');
    requireStatus(crossAccept, 404, 'cross-tenant invite acceptance rejection');
  } finally {
    await crossTenantAnonymous.dispose();
  }

  evidence.administratorUserId = adminUserId;
  evidence.crossTenantWorkspaceId = crossTenantWorkspaceId;

  await mkdir('test-results', { recursive: true });
  await writeFile(
    'test-results/mbj02-invite-runtime.json',
    `${JSON.stringify(evidence, null, 2)}\n`,
    'utf8'
  );
  console.log('MBJ-02 runtime acceptance passed: Admin create, anonymous validate/accept, login, and all required negative paths were verified.');
} finally {
  await admin.dispose();
}

async function newApiContext() {
  return request.newContext({
    baseURL,
    extraHTTPHeaders: {
      'X-Tenant-Slug': 'default'
    }
  });
}

async function getCsrf(api, name) {
  const response = await api.get('/api/security/csrf-token');
  record(name, 'GET', '/api/security/csrf-token', response.status(), '[CSRF token redacted]');
  requireStatus(response, 200, `${name} response`);
  const body = await readJson(response, `${name} response`);
  return {
    token: requiredString(body, 'token', `${name} response`),
    headerName: requiredString(body, 'headerName', `${name} response`)
  };
}

async function createInvite(api, csrf, data, stepName) {
  const response = await api.post('/api/admin/invites', {
    data,
    headers: { [csrf.headerName]: csrf.token }
  });
  record(stepName, 'POST', '/api/admin/invites', response.status(), '[invite token redacted]');
  if (response.status() !== 200) {
    const detail = await safeFailureDetail(response);
    throw new Error(`${stepName} returned HTTP ${response.status()}, expected 200${detail ? `: ${detail}` : '.'}`);
  }
  return readJson(response, `${stepName} response`);
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

function requiredEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`${name} is required for MBJ-02 invite acceptance.`);
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

async function safeFailureDetail(response) {
  let text;
  try {
    text = await response.text();
  } catch {
    return '';
  }

  for (const secret of [adminPassword, inviteePassword, crossTenantToken]) {
    if (secret) {
      text = text.split(secret).join('[REDACTED]');
    }
  }
  return text.trim().slice(0, 1000);
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}