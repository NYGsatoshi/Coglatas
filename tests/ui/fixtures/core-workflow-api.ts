import { type Page } from '@playwright/test';

export const workspaceId = 'static-workspace-1';
export const projectId = '72100000-0000-4000-8000-000000000001';
export const taskId = '72100000-0000-4000-8000-000000000002';
export const conversationId = '72100000-0000-4000-8000-000000000003';
export const fileId = '72100000-0000-4000-8000-000000000004';
export const projectTitle = 'User-authored project 計画';
export const taskTitle = 'User-authored task 調査';
export const conversationTitle = 'User-authored DM 相談';
export const fileName = 'User-authored file 資料.zip';
export const draft = 'Unsent draft 下書き α';

export const channelId = '72100000-0000-4000-8000-000000000005';
export const reportVersionId = '72100000-0000-4000-8000-000000000006';
export const channelTitle = 'User-authored channel 研究';

export async function installLocalizedWorkflowApi(page: Page): Promise<void> {
  const conversation = {
    id: conversationId, workspaceId, type: 'DirectMessage', title: conversationTitle,
    unreadCount: 0, hasMention: false, isMuted: false, isArchived: false, isLocked: false,
    createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z'
  };
  const project = {
    id: projectId, workspaceId, title: projectTitle, status: 'Active', visibility: 'WorkspaceVisible',
    activationState: 'Activated', createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z'
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const scopedConversationId = path.split('/')[3];
    const json = (body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/security/csrf-token') { return json({ token: 'localization-csrf', headerName: 'X-CSRF-Token' }); }
    if (path === '/api/projects') { return json({ items: [project], totalCount: 1, page: 1, pageSize: 50 }); }
    if (path === `/api/projects/${projectId}`) { return json(project); }
    if (path === `/api/projects/${projectId}/tasks`) {
      return json({ items: [{ id: taskId, projectId, title: taskTitle, status: 'InProgress',
        workflowStageId: 'stage-progress', workflowStageName: 'Configured stage ユーザー', stageCategory: 'InProgress',
        priority: 'High', progressPercent: 67, plannedStartDate: '2026-10-12', plannedEndDate: '2026-10-23',
        isBlocked: false, hasArtifact: false, createdAt: '2026-10-12T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z',
        uiPermissions: { canEdit: false, canAssign: false, canChangeStatus: false, canDelete: false, allowedTransitions: [], rowVersion: '1' }, version: 1 }], totalCount: 1, page: 1, pageSize: 50, hasMore: false });
    }
    if (path === `/api/projects/${projectId}/kanban`) { return json(kanbanSnapshot()); }
    if (path === `/api/projects/${projectId}/gantt`) { return json(ganttSnapshot()); }
    if (path === `/api/projects/${projectId}/workload` || path === `/api/projects/${projectId}/members`) { return json([]); }
    if (path === `/api/projects/${projectId}/artifact-versions/${reportVersionId}/report`) {
      return json({ projectId, taskId: new URL(request.url()).searchParams.has('taskId') ? taskId : null,
        artifactId: '72100000-0000-4000-8000-000000000007', artifactVersionId: reportVersionId, versionNumber: 4,
        canRefine: true, title: 'User-authored report 調査結果', sections: [{
          id: '72100000-0000-4000-8000-000000000008', logicalSectionId: '72100000-0000-4000-8000-000000000009', ordinal: 1, heading: 'User-authored findings 結論',
          runs: [{ kind: 'text', text: 'Supplied report prose 原文 stays unchanged across themes and language.', citation: null },
            { kind: 'citation', text: 'Supplied citation 引用', citation: {
              id: '72100000-0000-4000-8000-000000000010', ordinal: 1,
              claimId: '72100000-0000-4000-8000-000000000011', logicalClaimId: '72100000-0000-4000-8000-000000000012',
              evidence: [{ id: '72100000-0000-4000-8000-000000000013', ordinal: 1, sourceKind: 'ProjectFile',
                sourceTitle: 'User-authored evidence 資料', passage: 'Supplied evidence passage 原文', location: null, traceEventId: null }]
            } }]
        }] });
    }
    if (path === '/api/me/tasks/counts') {
      return json({ views: [{ view: 'Assigned', count: 1 }], timeGroups: [{ timeGroup: 'Today', count: 1 }] });
    }
    if (path === '/api/me/tasks') {
      return json({
        items: [{ taskId, tenantId: 'mock-tenant', workspaceId, workspaceTitle: 'Static Workspace', projectId,
          projectTitle, title: taskTitle, workflowStageName: 'Configured stage ユーザー', stageCategory: 'InProgress',
          priority: 'High', isBlocked: false, progressPercent: 67, timeGroup: 'Today', isOverdue: false,
          version: 1, labels: [], checklistCompletedCount: 0, checklistTotalCount: 0,
          quickEditPermissions: { canClaim: false, canChangeStage: false }, warnings: [] }],
        totalCount: 1, page: 1, pageSize: 50
      });
    }
    if (path === '/api/conversations') { return json({ items: [conversation, { ...conversation, id: channelId, type: 'WorkspaceChannel', title: channelTitle }], counts: { all: 2, unread: 0, mentions: 0, later: 0 } }); }
    if ([`/api/conversations/${conversationId}`, `/api/conversations/${channelId}`].includes(path)) {
      return json({ ...conversation, ...(path.endsWith(channelId) ? { id: channelId, type: 'WorkspaceChannel', title: channelTitle } : {}), members: [
        { userId: 'mock-user-a', displayName: 'Mock User A', canRead: true, canPost: true },
        { userId: 'mock-user-b', displayName: 'User-authored name 利用者', canRead: true, canPost: true }
      ] });
    }
    if ([`/api/conversations/${conversationId}/state`, `/api/conversations/${channelId}/state`].includes(path)) {
      return json({ conversationId: scopedConversationId, userId: 'mock-user-a', isMuted: false, isLater: false, unreadCount: 0 });
    }
    if ([`/api/conversations/${conversationId}/messages`, `/api/conversations/${channelId}/messages`].includes(path) && request.method() === 'GET') {
      return json({ items: [{ id: 'localization-message', workspaceId, conversationId: scopedConversationId, authorUserId: 'mock-user-b',
        authorDisplayName: 'User-authored name 利用者', body: 'Supplied message 本文',
        createdAt: '2026-10-16T10:00:00Z', version: 1 }, { id: 'localization-own-message', workspaceId, conversationId: scopedConversationId, authorUserId: 'mock-user-a', authorDisplayName: 'Mock User A', body: 'Own supplied response 返信 is preserved.', createdAt: '2026-10-16T10:03:00Z', version: 1 }] });
    }
    if ([`/api/conversations/${conversationId}/read`, `/api/conversations/${channelId}/read`].includes(path) && request.method() === 'POST') { return json({}); }
    if (path === '/api/messages/localization-message/thread' && request.method() === 'GET') {
      const root = { id: 'localization-message', workspaceId, conversationId: channelId,
        authorUserId: 'mock-user-b', authorDisplayName: 'User-authored name 利用者', body: 'Supplied message 本文',
        attachments: [], createdAt: '2026-10-16T10:00:00Z', isDeleted: false, version: 1 };
      return json({ rootMessage: root,
        replies: [{ ...root, id: 'localization-thread-reply', body: 'Supplied thread reply 返答', threadRootMessageId: root.id }],
        summary: { threadRootMessageId: root.id, replyCount: 1, latestReplyAt: root.createdAt,
          participantDisplayNames: ['User-authored name 利用者'] }, hasMore: false, maximumReplies: 100 });
    }
    if (path === '/api/file-folders') { return json({ workspaceId, rootVersion: 1, folders: [] }); }
    if (path === '/api/files') {
      return json({ items: [{ id: fileId, fileObjectId: fileId, workspaceId, originalFileName: fileName,
        contentType: 'application/zip', sizeBytes: 2048, status: 'Active', scanStatus: 'Clean',
        uploadedByUserId: 'mock-user-b', uploadedByDisplayName: 'User-authored name 利用者', canDelete: false, accessState: 'Workspace',
        createdAt: '2026-10-16T10:00:00Z', updatedAt: '2026-10-16T10:00:00Z' }],
        totalCount: 1, page: 1, pageSize: 50, hasMore: false });
    }
    return route.fallback();
  });
}

function kanbanSnapshot(): object {
  return {
    board: { projectId, version: 1, timeZone: 'UTC', defaultSwimlane: 0, selectedSwimlane: 0,
      supportedSwimlanes: [0], supportedFilters: ['includeOlderCompleted'], includesOlderCompleted: false,
      doneWindowDays: 30, totalAuthorizedCardCount: 1, isTruncated: false, uiPermissions: { canConfigure: false }, warnings: [] },
    columns: [
      { workflowStageId: 'stage-progress', displayName: 'Configured stage ユーザー', category: 2, displayOrder: 1000, currentAuthorizedCardCount: 1, hasWipWarning: false, uiPermissions: { canConfigure: false } },
      { workflowStageId: 'stage-done', displayName: 'Completed 完了', category: 4, displayOrder: 2000, currentAuthorizedCardCount: 0, hasWipWarning: false, uiPermissions: { canConfigure: false } }
    ],
    cards: [{ taskId, summary: taskTitle, workflowStageId: 'stage-progress', boardOrder: 1000,
      parentTaskId: null, parentSummary: null, isParentSummary: false, isLeaf: true,
      completedChildCount: 0, childCount: 0, progressPercent: 67, plannedStartDate: '2026-10-12', plannedEndDate: '2026-10-23',
      primaryAssigneeUserId: 'mock-user-b', primaryAssigneeLabel: 'User-authored name 利用者',
      targetGroupId: null, targetGroupLabel: 'Ungrouped', priority: 2, isBlocked: false, version: 1,
      swimlaneKey: 'all', swimlaneLabel: 'All', uiPermissions: { canOpen: true, canMove: false, allowedTargetWorkflowStageIds: [] } }]
  };
}

function ganttSnapshot(): object {
  const permissions = { canEditSchedule: false, canEditProgress: false, canManageDependencies: false, canClearSchedule: false, canOpen: true };
  return {
    projectId, projectTitle, projectVersion: 1, workflowVersion: 1, calendarVersion: null,
    calendar: { timeZone: 'UTC', workingDays: [], holidaysAvailable: false, limitations: [] },
    scheduledItems: [{ taskId, kind: 'Task', parentTaskId: null, milestoneId: null, title: taskTitle,
      plannedStartDate: '2026-10-12', plannedEndDate: '2026-10-23', milestoneDate: null,
      progressPercent: 67, progressIsDerived: false, workflowStageId: 'stage-progress',
      workflowStageName: 'Configured stage ユーザー', stageCategory: 'InProgress', priority: 'High',
      isBlocked: false, primaryAssignee: { userId: 'mock-user-b', displayName: 'User-authored name 利用者' },
      version: 1, scheduleEditPermissions: permissions, warnings: [] }],
    unscheduledItems: [], milestones: [], dependencies: [], warnings: [], permissions, maximumItems: 50, totalItems: 1,
  };
}
