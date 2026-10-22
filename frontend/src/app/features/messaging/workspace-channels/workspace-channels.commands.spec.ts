import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { readChannelDirectory, WorkspaceChannelsApi } from './workspace-channels.api';

const workspaceId = '10000000-0000-4000-8000-000000000001';
const channelId = '20000000-0000-4000-8000-000000000001';
const recipientId = '30000000-0000-4000-8000-000000000001';
const key = '40000000-0000-4000-8000-000000000001';
const endpoint = `/api/workspaces/${workspaceId}/chat-channels`;
const created = { id: channelId, workspaceId, name: 'Council events', visibility: 'Private',
  isMember: true, canJoin: false, canManage: true, isDefault: false };

describe('Workspace channel command response integrity', () => {
  let api: WorkspaceChannelsApi;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(WorkspaceChannelsApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  it('binds the confirmed create to the requested Workspace, name and visibility', () => {
    const next = vi.fn();
    api.create(workspaceId, 'Council events', 'Private', key).subscribe(next);
    const request = http.expectOne(endpoint);
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.headers.get('Idempotency-Key')).toBe(key);
    expect(request.request.body).toEqual({ name: 'Council events', visibility: 'Private' });
    request.flush(created);
    expect(next).toHaveBeenCalledWith(created);
  });

  it.each([
    { name: 'Different name' },
    { visibility: 'PublicWithinScope' },
    { workspaceId: '10000000-0000-4000-8000-000000000002' },
    { name: 'general', visibility: 'PublicWithinScope', isDefault: true, canManage: false },
    { isMember: false, canManage: false }
  ])('does not turn a mismatched create response into success: %j', (patch) => {
    const next = vi.fn();
    const error = vi.fn();
    api.create(workspaceId, 'Council events', 'Private', key).subscribe({ next, error });
    http.expectOne(endpoint).flush({ ...created, ...patch });
    expect(next).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledOnce();
  });

  it.each(['Private', 'ParticipantOnly'])('rejects an unreadable %s row rather than displaying its metadata', (visibility) => {
    expect(() => readChannelDirectory({ workspaceId, canCreate: false, page: 1, pageSize: 50, totalCount: 1,
      items: [{ ...created, visibility, isMember: false, canManage: false }] }, workspaceId, 1)).toThrow();
  });

  it('accepts only the exact joined public channel identity', () => {
    const next = vi.fn();
    const error = vi.fn();
    api.join(workspaceId, channelId).subscribe({ next, error });
    http.expectOne(`${endpoint}/${channelId}/join`).flush(created);
    expect(next).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledOnce();
    api.join(workspaceId, channelId).subscribe(next);
    http.expectOne(`${endpoint}/${channelId}/join`).flush({ ...created, visibility: 'PublicWithinScope', canManage: false });
    expect(next).toHaveBeenCalledOnce();
    expect(next.mock.calls[0][0]).toMatchObject({ id: channelId, isMember: true, visibility: 'PublicWithinScope' });
  });

  it('uses the scoped member endpoint and requires an explicit server confirmation', () => {
    const next = vi.fn();
    const error = vi.fn();
    api.invite(workspaceId, channelId, recipientId).subscribe({ next, error });
    const request = http.expectOne(`${endpoint}/${channelId}/members`);
    expect(request.request.body).toEqual({ userId: recipientId });
    expect(request.request.withCredentials).toBe(true);
    request.flush({ status: 'PENDING' });
    expect(next).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledOnce();
    api.invite(workspaceId, channelId, recipientId).subscribe(next);
    http.expectOne(`${endpoint}/${channelId}/members`).flush({ status: 'OK' });
    expect(next).toHaveBeenCalledOnce();
  });
});
