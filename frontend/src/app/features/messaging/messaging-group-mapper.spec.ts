import { mapConversationListItem, mapConversationPage, routeKindFromApi } from './messaging.mapper';

describe('Group conversation presentation boundary', () => {
  for (const type of ['Group', 7] as const) {
    it(`routes the backend Group type ${type} through DM`, () => {
      const row = mapConversationListItem({ id: 'group-a', workspaceId: 'workspace-a', type });
      expect(row).toMatchObject({ kind: 'dm', isGroup: true, route: '/dm/group-a', title: 'Group DM' });
      expect(routeKindFromApi(type)).toBe('dm');
    });

    it(`keeps Group ${type} read-only while the backend rejects Group sends`, () => {
      const page = mapConversationPage({
        id: 'group-a', workspaceId: 'workspace-a', type,
        members: [
          { userId: 'me', displayName: 'Current user', canRead: true, canPost: true, canCreateThread: true },
          { userId: 'other', displayName: 'Other participant', canRead: true },
        ],
      }, [], { currentUserId: 'me', currentTenantId: 'tenant-a', fallbackRouteKind: 'channel' });
      expect(page.routeKind).toBe('dm');
      expect(page.conversation.title).toBe('Group DM');
      expect(page.conversation.capabilities).toEqual(['readBody', 'viewOwnReadMarker']);
      expect(page.conversation.composerDisabledReasonKey).toBe('messaging.group.readOnly');
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

  it('keeps Project and Workspace channel types in Teams', () => {
    for (const type of ['ProjectChannel', 1, 'WorkspaceChannel', 10]) {
      expect(mapConversationListItem({ id: 'channel-a', workspaceId: 'workspace-a', type }))
        .toMatchObject({ kind: 'channel', isGroup: false, route: '/workspaces/workspace-a/channels/channel-a' });
    }
  });
});
