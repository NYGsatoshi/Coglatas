#!/usr/bin/env bash
set -euo pipefail

node --input-type=module <<'NODE'
import { mkdir, writeFile } from 'node:fs/promises';
import { request } from '@playwright/test';

const baseURL = requiredEnv('PLAYWRIGHT_BASE_URL');
const adminEmail = requiredSyntheticEmail('COGLATAS_MBJ02_ADMIN_EMAIL');
const adminPassword = requiredEnv('COGLATAS_MBJ02_ADMIN_PASSWORD');
const memberEmail = requiredSyntheticEmail('COGLATAS_MBJ02_INVITEE_EMAIL');
const memberPassword = requiredEnv('COGLATAS_MBJ02_INVITEE_PASSWORD');
const memberDisplayName = process.env.COGLATAS_MBJ02_INVITEE_DISPLAY_NAME?.trim() || 'MVP-A AuthZ Member';
const memberRole = 3;
const evidence = {
  journey: 'MVP-A AuthZ boundary',
  baseURL,
  steps: [],
  secretMaterialRecorded: false
};

function requiredEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required for MVP-A AuthZ boundary acceptance.`);
  return value;
}

function requiredSyntheticEmail(name) {
  const value = requiredEnv(name);
  if (!value.toLowerCase().endsWith('@example.test')) {
    throw new Error(`${name} must use the synthetic @example.test domain.`);
  }
  return value;
}

function record(name, method, path, response) {
  evidence.steps.push({ name, method, path, status: response.status() });
}

function requireStatus(response, expected, label) {
  if (response.status() !== expected) {
    throw new Error(`${label} returned HTTP ${response.status()}, expected ${expected}.`);
  }
}

async function readJson(response, label) {
  try {
    return await response.json();
  } catch {
    throw new Error(`${label} is not valid JSON.`);
  }
}

function requiredString(value, property, label) {
  const result = value && typeof value === 'object' ? value[property] : undefined;
  if (typeof result !== 'string' || result.length === 0) {
    throw new Error(`${label} is missing required string property '${property}'.`);
  }
  return result;
}

function newApiContext() {
  return request.newContext({
    baseURL,
    extraHTTPHeaders: { 'X-Tenant-Slug': 'default' }
  });
}

async function getCsrf(api, name) {
  const response = await api.get('/api/security/csrf-token');
  record(name, 'GET', '/api/security/csrf-token', response);
  requireStatus(response, 200, `${name} response`);
  const body = await readJson(response, `${name} response`);
  return {
    token: requiredString(body, 'token', `${name} response`),
    headerName: requiredString(body, 'headerName', `${name} response`)
  };
}

function extractInviteToken(invite) {
  const inviteUrl = requiredString(invite, 'inviteUrl', 'invite response');
  const token = new URL(inviteUrl, baseURL).searchParams.get('token');
  if (!token) throw new Error('Invite response URL does not contain a token.');
  return token;
}

async function verifyAnonymous() {
  const api = await newApiContext();
  try {
    const ready = await api.get('/health/ready');
    record('health-ready', 'GET', '/health/ready', ready);
    requireStatus(ready, 200, 'health readiness');

    const me = await api.get('/api/auth/me');
    record('anonymous-protected-me', 'GET', '/api/auth/me', me);
    requireStatus(me, 401, 'anonymous protected current-user lookup');

    const admin = await api.get('/api/admin/invites');
    record('anonymous-admin-denied', 'GET', '/api/admin/invites', admin);
    requireStatus(admin, 401, 'anonymous admin endpoint denial');
  } finally {
    await api.dispose();
  }
}

async function createMemberInvite() {
  const admin = await newApiContext();
  try {
    const loginCsrf = await getCsrf(admin, 'administrator-login-csrf');
    const login = await admin.post('/api/auth/login', {
      data: { email: adminEmail, password: adminPassword },
      headers: { [loginCsrf.headerName]: loginCsrf.token }
    });
    record('administrator-login', 'POST', '/api/auth/login', login);
    requireStatus(login, 200, 'administrator login');
    const loginBody = await readJson(login, 'administrator login response');
    if (!Array.isArray(loginBody.workspaces) || loginBody.workspaces.length === 0) {
      throw new Error('Administrator login response does not expose a seeded Workspace.');
    }
    const workspaceId = requiredString(loginBody.workspaces[0], 'id', 'administrator Workspace');

    const adminList = await admin.get('/api/admin/invites');
    record('administrator-admin-allowed', 'GET', '/api/admin/invites', adminList);
    requireStatus(adminList, 200, 'administrator admin endpoint access');

    const missingCsrf = await admin.post('/api/admin/invites', {
      data: {
        workspaceId,
        email: 'mvpa-authz-csrf-probe@example.test',
        role: memberRole,
        expiresAt: null
      }
    });
    record('administrator-missing-csrf-denied', 'POST', '/api/admin/invites', missingCsrf);
    requireStatus(missingCsrf, 403, 'administrator mutation without CSRF');

    const createCsrf = await getCsrf(admin, 'administrator-create-member-csrf');
    const createInvite = await admin.post('/api/admin/invites', {
      data: { workspaceId, email: memberEmail, role: memberRole, expiresAt: null },
      headers: { [createCsrf.headerName]: createCsrf.token }
    });
    record('administrator-create-member-invite', 'POST', '/api/admin/invites', createInvite);
    requireStatus(createInvite, 200, 'administrator member invite creation');
    const inviteBody = await readJson(createInvite, 'administrator member invite response');
    return { workspaceId, inviteToken: extractInviteToken(inviteBody) };
  } finally {
    await admin.dispose();
  }
}

async function verifyMember(workspaceId, inviteToken) {
  const member = await newApiContext();
  try {
    const acceptCsrf = await getCsrf(member, 'member-accept-csrf');
    const accept = await member.post('/api/invites/accept', {
      data: { token: inviteToken, displayName: memberDisplayName, password: memberPassword },
      headers: { [acceptCsrf.headerName]: acceptCsrf.token }
    });
    record('member-accept-invite', 'POST', '/api/invites/accept', accept);
    requireStatus(accept, 200, 'member invite acceptance');

    const me = await member.get('/api/auth/me');
    record('member-session-active', 'GET', '/api/auth/me', me);
    requireStatus(me, 200, 'member current-user lookup');

    const adminRead = await member.get('/api/admin/invites');
    record('member-admin-read-denied', 'GET', '/api/admin/invites', adminRead);
    requireStatus(adminRead, 403, 'non-admin admin endpoint read denial');

    const mutationCsrf = await getCsrf(member, 'member-admin-mutation-csrf');
    const adminMutation = await member.post('/api/admin/invites', {
      data: {
        workspaceId,
        email: 'mvpa-authz-non-admin-probe@example.test',
        role: memberRole,
        expiresAt: null
      },
      headers: { [mutationCsrf.headerName]: mutationCsrf.token }
    });
    record('member-admin-mutation-denied', 'POST', '/api/admin/invites', adminMutation);
    requireStatus(adminMutation, 403, 'non-admin admin endpoint mutation denial');

    const logoutCsrf = await getCsrf(member, 'member-logout-csrf');
    const logout = await member.post('/api/auth/logout', {
      data: {},
      headers: { [logoutCsrf.headerName]: logoutCsrf.token }
    });
    record('member-logout', 'POST', '/api/auth/logout', logout);
    requireStatus(logout, 200, 'member logout');

    const afterLogout = await member.get('/api/auth/me');
    record('revoked-session-denied', 'GET', '/api/auth/me', afterLogout);
    requireStatus(afterLogout, 401, 'logged-out session denial');
  } finally {
    await member.dispose();
  }
}

await verifyAnonymous();
const { workspaceId, inviteToken } = await createMemberInvite();
await verifyMember(workspaceId, inviteToken);
await mkdir('test-results', { recursive: true });
await writeFile(
  'test-results/mvp-a-authz-boundary.json',
  `${JSON.stringify(evidence, null, 2)}\n`,
  'utf8'
);
console.log(
  'MVP-A AuthZ boundary acceptance passed: anonymous 401, admin allow, non-admin 403, CSRF denial, and logout invalidation were verified against the real backend.'
);
NODE
