import type { Page } from '@playwright/test';

const workspaceId = 'static-workspace-362';
const conversationId = 'thread-dm-362';
const rootMessageId = 'thread-root-362';
const zeroReplyRootId = 'thread-empty-root-362';
const deletedRootMessageId = 'thread-deleted-root-362';
const currentUserId = 'mock-user-a';
const channelId = 'thread-channel-settings';
const channelWorkspaceId = 'static-workspace-1';

export const threadFixtureIds = Object.freeze({ workspaceId, conversationId, rootMessageId, zeroReplyRootId, deletedRootMessageId, currentUserId, channelId, channelWorkspaceId });

export interface ThreadApiControl {
  reads: string[];
  deniedRoot: string | null;
  holdRoot: string | null;
  release: (() => void) | null;
}
export async function installThreadApi(page: Page, scopedConversationId = conversationId, type = 'DirectMessage',
  rootBodies = { first: 'Pinned parent body', second: 'Root without replies' }): Promise<ThreadApiControl> {
  const control: ThreadApiControl = { reads: [], deniedRoot: null, holdRoot: null, release: null };
  let replies = [threadReply('thread-reply-362', 'Existing thread reply')];
  const scopedWorkspaceId = type === 'WorkspaceChannel' ? channelWorkspaceId : workspaceId;
  const inConversation = (message: Record<string, unknown>) => ({ ...message, workspaceId: scopedWorkspaceId, conversationId: scopedConversationId });

  await page.route('**/api/security/csrf-token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ token: 'csrf-message-thread', headerName: 'X-CSRF-Token' })
    });
  });

  await page.route('**/api/messages/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const threadMatch = path.match(/^\/api\/messages\/([^/]+)\/thread$/);
    const postMatch = path.match(/^\/api\/messages\/([^/]+)\/thread\/messages$/);

    if (threadMatch && request.method() === 'GET') {
      control.reads.push(threadMatch[1]);
      if (control.holdRoot === threadMatch[1]) {
        await new Promise<void>((resolve) => { control.release = resolve; });
        control.release = null;
      }
      if (control.deniedRoot === threadMatch[1]) {
        await route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ error: 'Permission denied.' }) });
        return;
      }
    }

    if (threadMatch && request.method() === 'GET' && threadMatch[1] === rootMessageId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ...threadProjection(replies), rootMessage: inConversation(rootMessage(rootMessageId, rootBodies.first)), replies: replies.map(inConversation) })
      });
      return;
    }

    if (threadMatch && request.method() === 'GET' && threadMatch[1] === deletedRootMessageId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          rootMessage: inConversation(rootMessage(deletedRootMessageId, 'Deleted parent secret', true)),
          replies: [inConversation(threadReply('thread-deleted-root-reply-362', 'Durable reply', undefined, deletedRootMessageId))],
          summary: threadSummary(1, deletedRootMessageId),
          hasMore: false,
          maximumReplies: 100
        })
      });
      return;
    }

    if (threadMatch && request.method() === 'GET' && threadMatch[1] === zeroReplyRootId) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
        rootMessage: inConversation(rootMessage(zeroReplyRootId, rootBodies.second)),
        replies: [], summary: threadSummary(0, zeroReplyRootId), hasMore: false, maximumReplies: 100
      }) });
      return;
    }

    if (postMatch && request.method() === 'POST' && postMatch[1] === rootMessageId) {
      const payload = request.postDataJSON() as { body?: string; clientRequestId?: string };
      const created = threadReply(
        'thread-reply-created-362',
        payload.body ?? '',
        payload.clientRequestId
      );
      replies = [...replies, created];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          message: inConversation(created),
          summary: threadSummary(replies.length)
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.route('**/api/conversations**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const detailMatch = path.match(/^\/api\/conversations\/([^/]+)$/);
    const messagesMatch = path.match(/^\/api\/conversations\/([^/]+)\/messages$/);

    if (path === '/api/conversations' && request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [{
            id: scopedConversationId,
            workspaceId: scopedWorkspaceId,
            type,
            title: 'Thread DM',
            unreadCount: 0,
            createdAt: '2026-08-27T00:00:00Z'
          }]
        })
      });
      return;
    }

    if (messagesMatch && request.method() === 'GET' && messagesMatch[1] === scopedConversationId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            { ...rootMessage(rootMessageId, rootBodies.first), thread: threadSummary(replies.length) },
            rootMessage(zeroReplyRootId, rootBodies.second),
            {
              ...rootMessage(deletedRootMessageId, 'Deleted parent secret', true),
              thread: threadSummary(1, deletedRootMessageId)
            }
          ].map(inConversation)
        })
      });
      return;
    }

    if (detailMatch && request.method() === 'GET' && detailMatch[1] === scopedConversationId) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: scopedConversationId,
          workspaceId: scopedWorkspaceId,
          type,
          title: 'Thread DM',
          isLocked: false,
          isArchived: false,
          createdAt: '2026-08-27T00:00:00Z',
          members: [
            {
              userId: currentUserId,
              displayName: 'Mock User A',
              canRead: true,
              canPost: true,
              canCreateThread: true,
              leftAt: null,
              removedAt: null
            },
            {
              userId: 'mock-user-b',
              displayName: 'Mock User B',
              canRead: true,
              canPost: true,
              canCreateThread: true,
              leftAt: null,
              removedAt: null
            }
          ]
        })
      });
      return;
    }

    if (path === `/api/conversations/${scopedConversationId}/state` && request.method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
        conversationId: scopedConversationId, userId: currentUserId, isMuted: false, isLater: false, unreadCount: 0
      }) });
      return;
    }

    await route.fallback();
  });
  return control;
}

function threadProjection(replies: readonly Record<string, unknown>[]): Record<string, unknown> {
  return {
    rootMessage: rootMessage(rootMessageId, 'Pinned parent body'),
    replies,
    summary: threadSummary(replies.length),
    hasMore: false,
    maximumReplies: 100
  };
}

function threadSummary(replyCount: number, threadRootMessageId = rootMessageId): Record<string, unknown> {
  return {
    threadRootMessageId,
    replyCount,
    latestReplyAt: replyCount > 0 ? '2026-08-27T02:00:00Z' : null,
    participantDisplayNames: replyCount > 1 ? ['Mock User B', 'Mock User A'] : ['Mock User B']
  };
}

function rootMessage(id: string, body: string, isDeleted = false): Record<string, unknown> {
  return {
    id,
    workspaceId,
    conversationId,
    authorUserId: 'mock-user-b',
    authorDisplayName: 'Mock User B',
    body,
    attachments: [],
    createdAt: '2026-08-27T01:00:00Z',
    isDeleted,
    version: 1
  };
}

function threadReply(
  id: string,
  body: string,
  clientRequestId?: string,
  threadRootMessageId = rootMessageId
): Record<string, unknown> {
  return {
    id,
    workspaceId,
    conversationId,
    authorUserId: id.includes('created') ? currentUserId : 'mock-user-b',
    authorDisplayName: id.includes('created') ? 'Mock User A' : 'Mock User B',
    body,
    attachments: [],
    createdAt: id.includes('created') ? '2026-08-27T03:00:00Z' : '2026-08-27T02:00:00Z',
    isDeleted: false,
    version: 1,
    clientRequestId,
    threadRootMessageId
  };
}
