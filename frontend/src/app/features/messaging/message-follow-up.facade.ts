import { injectMessagingI18n } from '../../core/i18n/providers/messaging-i18n';
import { MessagingTextKey } from './messaging.types';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { MessageFollowUpListItemDto, MessagingApi } from './messaging.api';

export type MessageFollowUpStatus = 'loading' | 'ready' | 'empty' | 'error';

export interface MessageFollowUpListItem {
  readonly messageId: string;
  readonly conversationId: string;
  readonly workspaceId: string;
  readonly conversationTitle: string;
  readonly authorDisplayName: string;
  readonly body: string;
  readonly messageCreatedAt: string;
  readonly savedAt: string;
  readonly route: string;
  readonly threadRootMessageId?: string;
}

interface MessageFollowUpState {
  readonly status: MessageFollowUpStatus;
  readonly items: readonly MessageFollowUpListItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly pendingMessageId?: string;
  readonly error?: string;
  readonly errorKey?: MessagingTextKey;
}

const INITIAL_STATE: MessageFollowUpState = {
  status: 'loading',
  items: [],
  page: 1,
  pageSize: 20,
  totalCount: 0
};

@Injectable({ providedIn: 'root' })
export class MessageFollowUpFacade {
  private readonly i18n = injectMessagingI18n();
  private readonly api = inject(MessagingApi);
  private readonly realtime = inject(RealtimeFacade);
  private readonly state = signal<MessageFollowUpState>(INITIAL_STATE);
  private readonly protectedRequests = new Set<Subscription>();
  private requestGeneration = 0;
  readonly view = computed(() => { const state = this.state(); return { ...state, error: state.error && state.errorKey ? this.i18n.translate(state.errorKey) : state.error }; });
  readonly hasNextPage = computed(() => this.state().page * this.state().pageSize < this.state().totalCount);

  constructor() {
    this.realtime.registerProtectedStateClearer?.('message-follow-ups', () => this.clearProtectedState());
  }

  load(page = 1): void {
    const generation = ++this.requestGeneration;
    this.cancelProtectedRequests();
    const pageSize = this.state().pageSize;
    this.state.set({ ...INITIAL_STATE, page, pageSize });
    const request = this.api.listMessageFollowUps(page, pageSize).subscribe({
      next: (response) => {
        if (generation !== this.requestGeneration) {
          return;
        }
        if (!response || !Array.isArray(response.items)) {
          this.failClosed();
          return;
        }
        const responsePage = positiveInteger(response.page);
        const responsePageSize = positiveInteger(response.pageSize);
        const totalCount = nonNegativeInteger(response.totalCount);
        const rawItems = response.items;
        const items = rawItems
          .map(mapFollowUp)
          .filter((item): item is MessageFollowUpListItem => item !== null);
        if (
          !responsePage ||
          !responsePageSize ||
          totalCount === null ||
          responsePage !== page ||
          responsePageSize !== pageSize ||
          items.length !== rawItems.length ||
          items.length > responsePageSize ||
          items.length > totalCount
        ) {
          this.failClosed();
          return;
        }
        this.state.set({
          status: items.length === 0 ? 'empty' : 'ready',
          items,
          page: responsePage,
          pageSize: responsePageSize,
          totalCount
        });
      },
      error: () => {
        if (generation === this.requestGeneration) {
          this.failClosed();
        }
      }
    });
    this.trackProtectedRequest(request);
  }

  remove(messageId: string): void {
    const current = this.state();
    if (current.pendingMessageId || !current.items.some((item) => item.messageId === messageId)) {
      return;
    }
    const generation = ++this.requestGeneration;
    this.cancelProtectedRequests();
    this.state.set({ ...current, pendingMessageId: messageId, error: undefined });
    const request = this.api.removeMessageFollowUp(messageId).subscribe({
      next: (response) => {
        if (generation !== this.requestGeneration) {
          return;
        }
        if (stringValue(response.messageId) !== messageId || response.isSaved !== false) {
          this.failClosed();
          return;
        }
        const remaining = this.state().items.filter((item) => item.messageId !== messageId);
        const targetPage = remaining.length === 0 && current.page > 1 ? current.page - 1 : current.page;
        this.load(targetPage);
      },
      error: () => {
        if (generation === this.requestGeneration) {
          this.failClosed('Saved messages changed or access was revoked. Refresh to try again.', 'messaging.saved.changed');
        }
      }
    });
    this.trackProtectedRequest(request);
  }

  private failClosed(error = 'Saved messages could not be loaded.', errorKey: MessagingTextKey = 'messaging.saved.loadFailed'): void {
    this.state.set({ ...INITIAL_STATE, status: 'error', error, errorKey });
  }

  private clearProtectedState(): void {
    this.requestGeneration += 1;
    this.cancelProtectedRequests();
    this.state.set(INITIAL_STATE);
  }

  private trackProtectedRequest(request: Subscription): void {
    this.protectedRequests.add(request);
    request.add(() => this.protectedRequests.delete(request));
  }

  private cancelProtectedRequests(): void {
    for (const request of [...this.protectedRequests]) {
      request.unsubscribe();
    }
    this.protectedRequests.clear();
  }
}

function mapFollowUp(value: MessageFollowUpListItemDto): MessageFollowUpListItem | null {
  const messageId = stringValue(value.messageId);
  const conversationId = stringValue(value.conversationId);
  const workspaceId = stringValue(value.workspaceId);
  const conversationTitle = stringValue(value.conversationTitle);
  const authorDisplayName = stringValue(value.authorDisplayName);
  const body = textValue(value.body);
  const messageCreatedAt = isoDate(value.messageCreatedAt);
  const savedAt = isoDate(value.savedAt);
  const threadRootMessageId = optionalString(value.threadRootMessageId);
  if (!messageId || !conversationId || !workspaceId || !conversationTitle || !authorDisplayName || body === null || !messageCreatedAt || !savedAt) {
    return null;
  }
  const direct = value.conversationType === 'DirectMessage' || value.conversationType === 0 ||
    value.conversationType === 'Group' || value.conversationType === 7;
  return {
    messageId,
    conversationId,
    workspaceId,
    conversationTitle,
    authorDisplayName,
    body,
    messageCreatedAt,
    savedAt,
    threadRootMessageId: threadRootMessageId ?? undefined,
    route: direct ? `/dm/${conversationId}` : `/workspaces/${workspaceId}/channels/${conversationId}`
  };
}

function stringValue(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function textValue(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function optionalString(value: unknown): string | null {
  return value === null || value === undefined ? null : stringValue(value);
}

function isoDate(value: unknown): string | null {
  const text = stringValue(value);
  return text && !Number.isNaN(Date.parse(text)) ? text : null;
}

function positiveInteger(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null;
}

function nonNegativeInteger(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;
}
