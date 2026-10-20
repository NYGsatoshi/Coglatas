import { mapConversationListItem, mapConversationPage, routeKindFromApi } from './messaging.mapper';

describe('Group conversation presentation boundary', () => {
  for (const type of ['Group', 7] as const) {
    it(`routes the backend Group type ${type} through DM`, () => {
      const row = mapConversationListItem({ id: 'group-a', workspaceId: 'workspace-a', type });
      expect(row).toMatchObject({ kind: 'dm', isGroup: true, route: '/dm/group-a', title: 'Group DM' });
      expect(routeKindFromApi(type)).toBe('dm');
    });

    it(`uses current Group ${type} participant posting flags while holding threads`, () => {
      const page = mapConversationPage({
        id: 'group-a', workspaceId: 'workspace-a', type,
        members: [
          { userId: 'me', displayName: 'Current user', role: 'Member', canRead: true, canPost: true, canCreateThread: true },
          { userId: 'other', displayName: 'Other participant', canRead: true },
        ],
      }, [], { currentUserId: 'me', currentTenantId: 'tenant-a', fallbackRouteKind: 'channel' });
      expect(page.routeKind).toBe('dm');
      expect(page.conversation.title).toBe('Group DM');
      expect(page.conversation.capabilities).toEqual(['readBody', 'viewOwnReadMarker', 'postMessage', 'leaveGroup']);
      expect(page.conversation.composerDisabledReasonKey).toBeUndefined();
      expect(page.conversation.capabilities).not.toContain('createThread');
    });
  }

  it('does not derive group read authority from the type or member count', () => {
    const page = mapConversationPage({
      id: 'group-a', type: 'Group',
      members: [{ userId: 'me', canRead: true, canPost: true, removedAt: '2026-10-19T00:00:00Z' }],
    }, [], { currentUserId: 'me', currentTenantId: 'tenant-a', fallbackRouteKind: 'dm' });
    expect(page.status).toBe('removedParticipant');
    expect(page.conversation.capabilities).toEqual([]);
  });

  it('preserves the ReadOnly manager flag without granting posting, hierarchy or thread authority', () => {
    const page = mapConversationPage({ id: 'group-a', workspaceId: 'workspace-a', type: 7,
      members: [
        { userId: 'me', displayName: 'Reader manager', role: 2, canRead: true, canPost: true, canManageMembers: true, canCreateThread: true },
        { userId: 'former-admin', displayName: 'Former admin', role: 0, removedAt: '2026-10-19T00:00:00Z' }
      ] }, [], { currentUserId: 'me', currentTenantId: 'tenant-a', fallbackRouteKind: 'dm' });
    expect(page.conversation.capabilities).toContain('manageGroupMembers');
    expect(page.conversation.capabilities).toContain('leaveGroup');
    expect(page.conversation.capabilities).not.toContain('postMessage');
    expect(page.conversation.capabilities).not.toContain('createThread');
    expect(page.conversation.groupMembers).toEqual([
      { userId: 'me', displayName: 'Reader manager', role: 'readOnly', isCurrentUser: true, membership: 'active' },
      { userId: 'former-admin', displayName: 'Former admin', role: 'admin', isCurrentUser: false, membership: 'removed' }
    ]);
  });

  for (const denied of [{ canRead: false }, { leftAt: '2026-10-19T00:00:00Z' }, { removedAt: '2026-10-19T00:00:00Z' }]) {
    it(`clears every Group participant projection for a denied viewer ${JSON.stringify(denied)}`, () => {
      const page = mapConversationPage({ id: 'group-a', workspaceId: 'workspace-a', type: 'Group', members: [
        { userId: 'me', displayName: 'Protected viewer', role: 'Admin', canRead: true, canPost: true, ...denied },
        { userId: 'other', displayName: 'Protected peer', canRead: true }
      ] }, [{ id: 'protected-message', body: 'Protected body' }], { currentUserId: 'me', currentTenantId: 'tenant-a', fallbackRouteKind: 'dm' });
      expect(page.conversation.capabilities).toEqual([]);
      expect(page.conversation.groupMembers).toEqual([]);
      expect(page.conversation.participants).toEqual([]);
      expect(page.conversation.mentionCandidates).toEqual([]);
      expect(page.messages).toEqual([]);
    });
  }

  for (const shape of [{}, { workspaceId: 'workspace-a', projectId: 'project-a' }, { workspaceId: 'workspace-a', parentConversationId: 'parent-a' }]) {
    it(`denies unsupported persisted Group shape ${JSON.stringify(shape)}`, () => {
      const page = mapConversationPage({ id: 'group-a', type: 'Group', ...shape,
        members: [{ userId: 'me', role: 'Admin', canRead: true, canPost: true }] }, [],
      { currentUserId: 'me', currentTenantId: 'tenant-a', fallbackRouteKind: 'dm' });
      expect(page.status).toBe('permissionDenied');
      expect(page.conversation.capabilities).toEqual([]);
    });
  }

  it('keeps Project and Workspace channel types in Teams', () => {
    for (const type of ['ProjectChannel', 1, 'WorkspaceChannel', 10]) {
      expect(mapConversationListItem({ id: 'channel-a', workspaceId: 'workspace-a', type }))
        .toMatchObject({ kind: 'channel', workspaceId: 'workspace-a', isGroup: false, route: '/workspaces/workspace-a/channels/channel-a' });
    }
  });
});
