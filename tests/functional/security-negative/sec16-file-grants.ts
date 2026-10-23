import { randomUUID } from 'node:crypto';
import { expect, type APIRequestContext, type APIResponse } from '@playwright/test';
import { csrfAwareRequest } from '../helpers/csrf';
import { assertSafeDenial } from '../helpers/safe-denial';
import { assertSafeResponse } from '../helpers/safe-response';

interface Sec16Actors {
  readonly alphaMemberApi: APIRequestContext;
  readonly alphaOwnerApi: APIRequestContext;
  readonly betaOwnerApi: APIRequestContext;
  readonly alphaFileId: string;
}

const seededBytes = Buffer.from('SEC02_ALPHA_FILE_CANARY_DO_NOT_LEAK\n', 'utf8');
const privateMarkers = [
  'SEC02_ALPHA_FILE_CANARY_DO_NOT_LEAK',
  'sec02-alpha-private.txt',
  'SEC-02 Alpha Private Project Canary'
] as const;

async function requestGrant(api: APIRequestContext, fileId: string): Promise<APIResponse> {
  return csrfAwareRequest(api, 'POST', `/api/files/${fileId}/download-grants`,
    { data: { purpose: 'sec16-adversarial-p0' } });
}

async function consume(api: APIRequestContext, id: string, token: string): Promise<APIResponse> {
  return csrfAwareRequest(api, 'POST', `/api/file-download-grants/${id}/download`,
    { data: { token } });
}

async function deny(response: APIResponse, label: string, secret?: string): Promise<void> {
  await assertSafeDenial(response, {
    label, expectedStatus: 400, maxBodyBytes: 2048,
    forbiddenMarkers: secret ? [...privateMarkers, secret] : privateMarkers,
  });
  expect(response.headers()['location'], label + ' cannot redirect to storage').toBeUndefined();
  expect(response.headers()['content-disposition'], label + ' cannot reveal filenames').toBeUndefined();
}

async function positiveControl(response: APIResponse, label: string): Promise<void> {
  await assertSafeResponse(response, { label, expectedStatus: 200 });
  // Never put raw synthetic file bytes in diagnostic evidence.
  expect((await response.body()).equals(seededBytes), label + ' must read the seeded bytes').toBe(true);
  expect(response.headers()['cache-control'] ?? '').toContain('no-store');
}

/**
 * Tests a current synthetic File download grant at the HTTP boundary.
 * The retained token and IDs stay in memory and cannot reach attached evidence.
 */
export async function createSec16GrantJourney({
  alphaMemberApi, alphaOwnerApi, betaOwnerApi, alphaFileId,
}: Sec16Actors): Promise<{
  readonly before: Record<string, boolean>;
  readonly afterRevocation: () => Promise<Record<string, boolean>>;
}> {
  const issuance = await requestGrant(alphaMemberApi, alphaFileId);
  await assertSafeResponse(issuance, { label: 'SEC-16 authorized issuance', expectedStatus: 200 });
  const payload: unknown = await issuance.json();
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
    throw new Error('SEC-16 grant response was not an object.');
  }
  const data = payload as Record<string, unknown>;
  const id = data['fileDownloadGrantId'];
  const token = data['token'];
  expect(typeof id === 'string' && /^[0-9a-f-]{36}$/iu.test(id)).toBe(true);
  expect(typeof token === 'string' && token.length > 0).toBe(true);
  expect(data['fileObjectId']).toBe(alphaFileId);
  if (typeof id !== 'string' || typeof token !== 'string') {
    throw new Error('SEC-16 grant lacked an in-memory identity or token.');
  }

  await deny(await consume(alphaOwnerApi, id, token), 'SEC-16 same-Tenant actor transfer', token);
  await deny(await consume(betaOwnerApi, id, token), 'SEC-16 foreign-Tenant actor transfer', token);
  await deny(await consume(alphaMemberApi, id, randomUUID()), 'SEC-16 invalid token', token);
  await deny(await consume(alphaMemberApi, randomUUID(), token), 'SEC-16 wrong grant identity', token);
  await deny(await requestGrant(betaOwnerApi, alphaFileId), 'SEC-16 foreign scope grant issuance');
  // A live successful download makes false-green denial via missing resources impossible.
  await positiveControl(await consume(alphaMemberApi, id, token), 'SEC-16 authorized grant control');

  return {
    before: {
      positiveControl: true, actorTransferDenied: true, foreignTransferDenied: true,
      invalidTokenDenied: true, wrongGrantDenied: true, foreignIssueDenied: true,
    },
    afterRevocation: async () => {
      // FCI-07 has now removed this actor's Alpha Workspace membership while
      // the same authenticated cookie jar remains alive.
      await assertSafeResponse(await alphaMemberApi.get('/api/auth/me'), {
        label: 'SEC-16 same authenticated Tenant session', expectedStatus: 200,
      });
      await deny(await consume(alphaMemberApi, id, token), 'SEC-16 stale grant after revoke', token);
      await deny(await requestGrant(alphaMemberApi, alphaFileId), 'SEC-16 fresh grant after revoke');
      await deny(await alphaMemberApi.get(`/api/files/${alphaFileId}/download`),
        'SEC-16 direct download after revoke');
      await positiveControl(await alphaOwnerApi.get(`/api/files/${alphaFileId}/download`),
        'SEC-16 unaffected owner positive control');
      return {
        sameSessionAuthenticated: true, staleGrantDenied: true,
        freshGrantDenied: true, directDownloadDenied: true, positiveControl: true,
      };
    },
  };
}
