import { channelHttpStatus, channelScopeId, readChannelDirectory, readChannelItem, validChannelName } from './workspace-channels.api';
import { WORKSPACE_CHANNELS_EN, WORKSPACE_CHANNELS_JA } from './workspace-channels.messages';

const workspaceId = '10000000-0000-4000-8000-000000000001';
const otherWorkspaceId = '10000000-0000-4000-8000-000000000002';
const id = '20000000-0000-4000-8000-000000000001';
const item = { id, workspaceId, name: 'Council events', visibility: 'PublicWithinScope',
  isMember: false, canJoin: true, canManage: false, isDefault: false };
const directory = () => ({ workspaceId, canCreate: false, items: [item], page: 1, pageSize: 50, totalCount: 1 });

describe('Workspace channel directory wire boundary', () => {
  it('projects metadata only and does not infer Message access from public discovery', () => {
    const projected = readChannelItem({ ...item, body: 'Must not be retained', unreadCount: 19, members: ['Not a directory field'] }, workspaceId);
    expect(projected).toEqual(item);
    expect(projected.isMember).toBe(false);
    expect(projected).not.toHaveProperty('body');
    expect(projected).not.toHaveProperty('unreadCount');
    expect(projected).not.toHaveProperty('members');
  });

  it('rejects a directory or a row from another Workspace', () => {
    expect(() => readChannelDirectory({ ...directory(), workspaceId: otherWorkspaceId }, workspaceId, 1)).toThrow();
    expect(() => readChannelDirectory({ ...directory(), items: [{ ...item, workspaceId: otherWorkspaceId }] }, workspaceId, 1)).toThrow();
  });

  it('rejects forged capability representations and invalid default-channel participation', () => {
    for (const patch of [
      { canCreate: 'true' }, { page: 2 }, { pageSize: 100 }, { totalCount: -1 }, { totalCount: 1.2 }, { totalCount: 0 }
    ]) expect(() => readChannelDirectory({ ...directory(), ...patch }, workspaceId, 1)).toThrow();
    for (const patch of [
      { isMember: 'false' }, { canJoin: 1 }, { canManage: true }, { visibility: 0 },
      { visibility: 'Private' }, { isDefault: true }, { id: '../another-scope' }
    ]) expect(() => readChannelItem({ ...item, ...patch }, workspaceId)).toThrow();
  });

  it('keeps existing private participation without granting discoverability or management', () => {
    expect(readChannelItem({ ...item, visibility: 'Private', isMember: true, canJoin: false }, workspaceId))
      .toMatchObject({ visibility: 'Private', isMember: true, canJoin: false, canManage: false });
  });

  it('rejects duplicate identities and overfull pages', () => {
    expect(() => readChannelDirectory({ ...directory(), items: [item, item], totalCount: 2 }, workspaceId, 1)).toThrow();
    expect(() => readChannelDirectory({ ...directory(), items: Array(51).fill(item), totalCount: 51 }, workspaceId, 1)).toThrow();
    expect(readChannelDirectory({ ...directory(), items: [], totalCount: 0 }, workspaceId, 1).items).toEqual([]);
  });

  it('accepts Unicode names but reserves general and rejects control characters and excessive length', () => {
    expect(validChannelName(' 生徒会・行事運営 ')).toBe(true);
    for (const name of ['', '  ', 'GENERAL', ' general ', 'a\u0000b', 'a\u0085b', 'x'.repeat(201)]) {
      expect(validChannelName(name)).toBe(false);
    }
    expect(validChannelName('x'.repeat(200))).toBe(true);
  });

  it('rejects empty and path-shaped scope identifiers', () => {
    for (const value of [null, undefined, '', {}, '../workspaces', '00000000-0000-0000-0000-000000000000']) {
      expect(channelScopeId(value)).toBeNull();
    }
    expect(channelScopeId('AB000000-0000-4000-8000-000000000001')).toBe('ab000000-0000-4000-8000-000000000001');
  });

  it('recognizes both production-normalized and raw HTTP error status', () => {
    expect(channelHttpStatus({ httpStatus: 403, status: 503 })).toBe(403);
    expect(channelHttpStatus({ status: 401 })).toBe(401);
    expect(channelHttpStatus({ httpStatus: '403' })).toBe(0);
    expect(channelHttpStatus(null)).toBe(0);
  });

  it('keeps the English and Japanese channel catalog complete', () => {
    expect(Object.keys(WORKSPACE_CHANNELS_JA).sort()).toEqual(Object.keys(WORKSPACE_CHANNELS_EN).sort());
    expect(Object.values(WORKSPACE_CHANNELS_JA).every((value) => value.trim().length > 0)).toBe(true);
  });
});
