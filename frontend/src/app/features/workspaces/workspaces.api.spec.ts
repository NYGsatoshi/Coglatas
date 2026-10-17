import {
  canonicalizeWorkspaceCreateInput,
  mapWorkspaceDashboardItem,
  mapWorkspaceDashboardResponse,
  mapWorkspaceCreateSuccess,
  mapWorkspacePageCapabilities,
  WorkspaceDashboardListItemDto,
} from './workspaces.api';

const dashboardItem = (
  overrides: Partial<WorkspaceDashboardListItemDto> = {},
): WorkspaceDashboardListItemDto => ({
  id: 'workspace-1',
  name: 'Canonical Workspace',
  description: null,
  icon: null,
  status: 0,
  createdAt: '2026-08-22T00:00:00Z',
  updatedAt: '2026-08-23T00:00:00Z',
  currentUserRole: 'Member',
  accessSource: 'WorkspaceMembership',
  canOpenWorkspace: true,
  canOpenMembers: true,
  canOpenProjects: true,
  canCreateProject: false,
  canAddFiles: false,
  unreadAnnouncementCount: 0,
  unreadConversationCount: 0,
  inProgressProjectCount: 0,
  runningProjectCount: 0,
  needsReviewProjectCount: 0,
  ...overrides,
});

describe('Workspace dashboard API mapper', () => {
  it('retains valid raw instants for reactive locale formatting without parsing legacy display labels', () => {
    const card = mapWorkspaceDashboardItem(dashboardItem());
    expect(card.lastUpdatedUtc).toBe('2026-08-23T00:00:00Z');
    const fallback = mapWorkspaceDashboardItem(dashboardItem({ updatedAt: 'not an instant' }));
    expect(fallback.lastUpdatedUtc).toBe('2026-08-22T00:00:00Z');
    const unavailable = mapWorkspaceDashboardItem(dashboardItem({ updatedAt: null, createdAt: null }));
    expect(unavailable.lastUpdatedUtc).toBeNull();
    expect(unavailable.availability.lastUpdated).toBe(false);
  });

  it.each([
    ['Owner', '管理者'],
    ['Admin', '管理者'],
    ['Adviser', '先生'],
    ['Member', 'メンバー'],
    ['ReadOnly', '閲覧のみ'],
  ] as const)('maps the canonical %s role without inventing another role', (role, label) => {
    const card = mapWorkspaceDashboardItem(dashboardItem({ currentUserRole: role }));

    expect(card.currentUserRole).toBe(role);
    expect(card.accessSource).toBe('WorkspaceMembership');
    expect(card.roleLabel).toBe(label);
  });

  it('presents SystemAdmin access explicitly with no Workspace membership role', () => {
    const card = mapWorkspaceDashboardItem(
      dashboardItem({ currentUserRole: null, accessSource: 'SystemAdmin' }),
    );

    expect(card.currentUserRole).toBeNull();
    expect(card.accessSource).toBe('SystemAdmin');
    expect(card.roleLabel).toBe('システム管理者アクセス');
  });

  it('preserves authoritative zero and non-zero counts', () => {
    const zero = mapWorkspaceDashboardItem(dashboardItem());
    const nonZero = mapWorkspaceDashboardItem(
      dashboardItem({
        unreadAnnouncementCount: 7,
        unreadConversationCount: 4,
        inProgressProjectCount: 9,
        runningProjectCount: 6,
        needsReviewProjectCount: 3,
      }),
    );

    expect(zero.unreadAnnouncementCount).toBe(0);
    expect(zero.unreadConversationCount).toBe(0);
    expect(zero.activeProjectCount).toBe(0);
    expect(zero.runningProjectCount).toBe(0);
    expect(zero.needsReviewProjectCount).toBe(0);
    expect(zero.availability).toMatchObject({
      unreadAnnouncements: true,
      unreadConversations: true,
      activeProjects: true,
      runningProjects: true,
      needsReviewProjects: true,
    });
    expect(nonZero.unreadAnnouncementCount).toBe(7);
    expect(nonZero.unreadConversationCount).toBe(4);
    expect(nonZero.activeProjectCount).toBe(9);
    expect(nonZero.runningProjectCount).toBe(6);
    expect(nonZero.needsReviewProjectCount).toBe(3);
  });

  it('maps each backend card capability boolean independently', () => {
    const allRead = mapWorkspaceDashboardItem(dashboardItem());
    const membersDenied = mapWorkspaceDashboardItem(dashboardItem({ canOpenMembers: false }));
    const projectsOnly = mapWorkspaceDashboardItem(
      dashboardItem({
        canOpenWorkspace: false,
        canOpenMembers: false,
        canOpenProjects: true,
      }),
    );
    const quickCreate = mapWorkspaceDashboardItem(
      dashboardItem({
        canOpenProjectCreate: true,
        canCreateProject: true,
        canAddFiles: true,
      }),
    );

    expect(allRead.capabilities).toEqual(['openWorkspace', 'openMembers', 'openProjects']);
    expect(membersDenied.capabilities).toEqual(['openWorkspace', 'openProjects']);
    expect(projectsOnly.capabilities).toEqual(['openProjects']);
    expect(quickCreate.capabilities).toEqual([
      'openWorkspace',
      'openMembers',
      'openProjects',
      'openProjectCreate',
      'createProject',
      'addFiles',
    ]);
  });

  it('maps the server-owned External aggregate without inventing protected details', () => {
    const manager = mapWorkspaceDashboardItem(dashboardItem({
      hasExternalShares: true,
      externalShareCount: 2,
      canInspectSharing: true,
      canManageSharing: true,
      memberPreview: [
        { userId: 'member-1', displayName: 'Alice' },
        { userId: 'member-2', displayName: 'Bob' },
      ],
    }));
    const ordinaryViewer = mapWorkspaceDashboardItem(dashboardItem({
      hasExternalShares: true,
      externalShareCount: 2,
    }));

    expect(manager.hasExternalShares).toBe(true);
    expect(manager.externalShareCount).toBe(2);
    expect(manager.memberPreview).toEqual([
      { id: 'member-1', displayName: 'Alice' },
      { id: 'member-2', displayName: 'Bob' },
    ]);
    expect(manager.capabilities).toEqual(expect.arrayContaining(['inspectSharing', 'manageSharing']));
    expect(ordinaryViewer.hasExternalShares).toBe(true);
    expect(ordinaryViewer.externalShareCount).toBeNull();
    expect(ordinaryViewer.capabilities).not.toContain('manageSharing');
  });

  it('fails Quick Create mutation affordances closed when capability fields are absent', () => {
    const card = mapWorkspaceDashboardItem(
      dashboardItem({ canOpenProjectCreate: undefined, canCreateProject: undefined, canAddFiles: undefined }),
    );

    expect(card.capabilities).toEqual(['openWorkspace', 'openMembers', 'openProjects']);
    expect(card.capabilities).not.toContain('createProject');
    expect(card.capabilities).not.toContain('openProjectCreate');
    expect(card.capabilities).not.toContain('addFiles');
  });

  it('keeps full Project setup independent from the ungrouped Quick Create authority', () => {
    const card = mapWorkspaceDashboardItem(
      dashboardItem({
        canOpenProjectCreate: true,
        canCreateProject: false,
      }),
    );

    expect(card.capabilities).toContain('openProjectCreate');
    expect(card.capabilities).not.toContain('createProject');
  });

  it('keeps unavailable values distinct instead of fabricating numeric zero or Member', () => {
    const card = mapWorkspaceDashboardItem(
      dashboardItem({
        currentUserRole: undefined,
        accessSource: undefined,
        unreadAnnouncementCount: undefined,
        unreadConversationCount: undefined,
        inProgressProjectCount: undefined,
        runningProjectCount: undefined,
        needsReviewProjectCount: undefined,
      }),
    );

    expect(card.currentUserRole).toBeNull();
    expect(card.accessSource).toBeNull();
    expect(card.roleLabel).toBe('役割情報なし');
    expect(card.unreadAnnouncementCount).toBeNull();
    expect(card.unreadConversationCount).toBeNull();
    expect(card.activeProjectCount).toBeNull();
    expect(card.runningProjectCount).toBeNull();
    expect(card.needsReviewProjectCount).toBeNull();
    expect(card.availability).toMatchObject({
      unreadAnnouncements: false,
      unreadConversations: false,
      activeProjects: false,
      runningProjects: false,
      needsReviewProjects: false,
    });
  });

  it('derives the compatibility total only when both additive state counts are authoritative', () => {
    const splitOnly = mapWorkspaceDashboardItem(
      dashboardItem({
        inProgressProjectCount: undefined,
        runningProjectCount: 2,
        needsReviewProjectCount: 1,
      }),
    );
    const incompleteSplit = mapWorkspaceDashboardItem(
      dashboardItem({
        inProgressProjectCount: undefined,
        runningProjectCount: 2,
        needsReviewProjectCount: undefined,
      }),
    );

    expect(splitOnly.activeProjectCount).toBe(3);
    expect(incompleteSplit.activeProjectCount).toBeNull();
    expect(incompleteSplit.needsReviewProjectCount).toBeNull();
  });

  it('maps page create capability only from the enveloped backend value', () => {
    expect(
      mapWorkspacePageCapabilities({
        requestId: 'request-1',
        data: { canCreate: true },
        warnings: [],
      }),
    ).toEqual(['createWorkspace']);
    expect(
      mapWorkspacePageCapabilities({
        requestId: 'request-2',
        data: { canCreate: false },
        warnings: [],
      }),
    ).toEqual([]);
    expect(mapWorkspacePageCapabilities({ data: { canCreate: true } })).toEqual([]);
    expect(
      mapWorkspacePageCapabilities({ requestId: 'request-3', data: { canCreate: true } }),
    ).toEqual([]);
    expect(mapWorkspacePageCapabilities({ data: null })).toEqual([]);
    expect(mapWorkspacePageCapabilities(null)).toEqual([]);
  });

  it('rejects a non-array dashboard response instead of treating it as no access', () => {
    expect(() => mapWorkspaceDashboardResponse({ items: [] })).toThrow(
      'Workspace dashboard response must be an array.',
    );
  });

  it('canonicalizes the create body without adding client-owned identifiers', () => {
    expect(
      canonicalizeWorkspaceCreateInput({
        name: '  Research Team  ',
        description: '   ',
        icon: '  🔬  ',
      }),
    ).toEqual({
      name: 'Research Team',
      description: null,
      icon: '🔬',
    });
  });

  it('strictly maps the canonical Workspace create envelope', () => {
    const response = mapWorkspaceCreateSuccess({
      requestId: 'request-create-1',
      data: {
        id: '11111111-1111-4111-8111-111111111111',
        name: 'Research Team',
        description: null,
        icon: '🔬',
        status: 0,
        createdByUserId: '22222222-2222-4222-8222-222222222222',
        createdAt: '2026-08-24T01:02:03.456Z',
        updatedAt: null,
      },
      warnings: [],
    });

    expect(response).toEqual({
      requestId: 'request-create-1',
      data: {
        id: '11111111-1111-4111-8111-111111111111',
        name: 'Research Team',
        description: null,
        icon: '🔬',
        status: 0,
        createdByUserId: '22222222-2222-4222-8222-222222222222',
        createdAt: '2026-08-24T01:02:03.456Z',
        updatedAt: null,
      },
      warnings: [],
    });
  });

  it.each([
    ['missing data', { requestId: 'request-create-1', warnings: [] }],
    [
      'non-UUID resource id',
      {
        requestId: 'request-create-1',
        data: {
          id: 'workspace-1',
          name: 'Research Team',
          description: null,
          icon: null,
          status: 0,
          createdByUserId: '22222222-2222-4222-8222-222222222222',
          createdAt: '2026-08-24T01:02:03Z',
          updatedAt: null,
        },
        warnings: [],
      },
    ],
    [
      'non-active status',
      {
        requestId: 'request-create-1',
        data: {
          id: '11111111-1111-4111-8111-111111111111',
          name: 'Research Team',
          description: null,
          icon: null,
          status: 1,
          createdByUserId: '22222222-2222-4222-8222-222222222222',
          createdAt: '2026-08-24T01:02:03Z',
          updatedAt: null,
        },
        warnings: [],
      },
    ],
    [
      'missing warnings',
      {
        requestId: 'request-create-1',
        data: {
          id: '11111111-1111-4111-8111-111111111111',
          name: 'Research Team',
          description: null,
          icon: null,
          status: 0,
          createdByUserId: '22222222-2222-4222-8222-222222222222',
          createdAt: '2026-08-24T01:02:03Z',
          updatedAt: null,
        },
      },
    ],
  ])('rejects a successful-looking create response with %s', (_caseName, response) => {
    expect(() => mapWorkspaceCreateSuccess(response)).toThrow();
  });
});
