import { chromium, expect, request } from '@playwright/test';
import { isWorkspaceRevocationFrame } from './real-backend-authorization-frame.mjs';

const secondaryWorkspaceName = 'Browser Smoke Workspace Two';
const secondaryAssignedTaskTitle = 'PR04 second workspace assigned';

export async function prepareRealBackendP0State({ baseURL, email, password }) {
  const api = await request.newContext({
    baseURL,
    extraHTTPHeaders: {
      'X-Tenant-Slug': 'default'
    }
  });
  let browser;

  try {
    const anonymousCsrf = await getCsrf(api, 'anonymous CSRF token');
    const loginResponse = await api.post('/api/auth/login', {
      data: { email, password },
      headers: { [anonymousCsrf.headerName]: anonymousCsrf.token }
    });
    if (!loginResponse.ok()) {
      throw new Error(`Real-backend P0 setup login failed with HTTP ${loginResponse.status()}.`);
    }

    const loginBody = await readJson(loginResponse, 'P0 setup login response');
    const userId = requiredString(loginBody, 'userId', 'P0 setup login response');
    let secondaryWorkspace = findWorkspace(loginBody?.workspaces, secondaryWorkspaceName);

    if (!secondaryWorkspace) {
      const workspaceResponse = await api.get('/api/workspaces');
      if (!workspaceResponse.ok()) {
        throw new Error(`Real-backend P0 setup workspace lookup failed with HTTP ${workspaceResponse.status()}.`);
      }
      const workspaces = await readJson(workspaceResponse, 'P0 setup workspace response');
      secondaryWorkspace = findWorkspace(workspaces, secondaryWorkspaceName);
    }

    if (!secondaryWorkspace) {
      throw new Error(`Real-backend P0 setup could not find the synthetic Workspace '${secondaryWorkspaceName}'.`);
    }

    // The HTTP mutation commits before its outbox control frame is delivered.
    // Consume that actual revocation with a temporary authenticated observer,
    // so it cannot cancel the following smoke journey's first command.
    browser = await chromium.launch();
    const observerContext = await browser.newContext({
      baseURL,
      storageState: await api.storageState(),
      extraHTTPHeaders: { 'X-Tenant-Slug': 'default' },
    });
    const observer = await observerContext.newPage();
    const revocation = { started: false, received: false };
    observer.on('websocket', (socket) => {
      if (new URL(socket.url()).pathname !== '/hubs/app') { return; }
      socket.on('framereceived', (frame) => {
        if (revocation.started && isWorkspaceRevocationFrame(frame, userId, secondaryWorkspace.id)) {
          revocation.received = true;
        }
      });
    });
    await observer.goto('/app/tasks');
    const indicator = observer.getByTestId('realtime-connection-state');
    await expect(indicator).toContainText('Realtime updates connected.', { timeout: 30_000 });

    const authorizationRefresh = observer.waitForResponse((response) =>
      response.request().method() === 'GET' &&
      new URL(response.url()).pathname === '/api/auth/status', { timeout: 30_000 });
    // Handle an early setup failure without leaving a rejected response waiter.
    void authorizationRefresh.catch(() => {});
    const authenticatedCsrf = await getCsrf(api, 'authenticated CSRF token');
    const revokePath = `/api/workspaces/${secondaryWorkspace.id}/members/${userId}`;
    revocation.started = true;
    const revokeResponse = await api.delete(revokePath, {
      headers: { [authenticatedCsrf.headerName]: authenticatedCsrf.token }
    });
    if (revokeResponse.status() !== 200) {
      throw new Error(`Real-backend P0 setup membership revoke failed with HTTP ${revokeResponse.status()}.`);
    }
    await expect.poll(() => revocation.received, { timeout: 30_000 }).toBe(true);
    if ((await authorizationRefresh).status() !== 200) {
      throw new Error('Real-backend P0 observer could not refresh current authorization.');
    }
    await expect(indicator).toContainText('Realtime updates connected.', { timeout: 30_000 });

    const myTasksResponse = await api.get('/api/me/tasks?view=assigned&scope=allWorkspaces&page=1&pageSize=100');
    if (!myTasksResponse.ok()) {
      throw new Error(`Real-backend P0 setup My Tasks verification failed with HTTP ${myTasksResponse.status()}.`);
    }

    const myTasks = await readJson(myTasksResponse, 'P0 setup My Tasks response');
    if (myTasks?.availableWorkspaceCount !== 1) {
      throw new Error(
        `Real-backend P0 setup expected exactly one authorized Workspace after revocation; got ${String(myTasks?.availableWorkspaceCount)}.`
      );
    }

    const items = Array.isArray(myTasks?.items) ? myTasks.items : [];
    if (items.some((item) => item && typeof item === 'object' && item.title === secondaryAssignedTaskTitle)) {
      throw new Error('Real-backend P0 setup left the synthetic secondary-Workspace task visible after membership revocation.');
    }

    // Keep setup output non-sensitive: never serialize credentials, CSRF tokens,
    // cookies, Workspace IDs, or user IDs into CI logs.
    console.log('Real-backend P0 fixture prepared: revocation delivered, observer reauthorized, and My Tasks authorization scope verified.');
  } finally {
    await browser?.close();
    await api.dispose();
  }
}

async function getCsrf(api, label) {
  const response = await api.get('/api/security/csrf-token');
  if (!response.ok()) {
    throw new Error(`${label} request failed with HTTP ${response.status()}.`);
  }

  const body = await readJson(response, label);
  const token = requiredString(body, 'token', label);
  const headerName = requiredString(body, 'headerName', label);
  return { token, headerName };
}

async function readJson(response, label) {
  try {
    return await response.json();
  } catch {
    throw new Error(`${label} was not valid JSON.`);
  }
}

function requiredString(value, property, label) {
  const result = value && typeof value === 'object' ? value[property] : undefined;
  if (typeof result !== 'string' || result.length === 0) {
    throw new Error(`${label} is missing required string property '${property}'.`);
  }
  return result;
}

function findWorkspace(value, name) {
  if (!Array.isArray(value)) {
    return null;
  }

  const workspace = value.find((candidate) =>
    candidate &&
    typeof candidate === 'object' &&
    candidate.name === name &&
    typeof candidate.id === 'string' &&
    candidate.id.length > 0
  );

  return workspace ? { id: workspace.id, name: workspace.name } : null;
}
