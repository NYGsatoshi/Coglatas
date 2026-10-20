import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { ConversationListComponent } from '../conversation-list/conversation-list.component';
import { ConversationSettingsPanelComponent } from '../conversation-settings-panel/conversation-settings-panel.component';
import { ConversationReadActionComponent } from '../conversation-read-action/conversation-read-action.component';
import { MessageGlobalSettingsService } from '../message-global-settings.service';
import { MessageComposerComponent } from '../message-composer/message-composer.component';
import { MessageNavigationStateService } from '../message-navigation-state.service';
import { MessageTimelineComponent } from '../message-timeline/message-timeline.component';
import { MessagingFacade } from '../messaging.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { ThreadWorkspaceComponent } from '../thread-workspace/thread-workspace.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-channel-messaging-page',
  standalone: true,
  imports: [
    ConversationListComponent,
    ConversationSettingsPanelComponent,
    ConversationReadActionComponent,
    MessageComposerComponent,
    MessageTimelineComponent,
    NgTemplateOutlet,
    RouterLink,
    ThreadWorkspaceComponent,
  ],
  templateUrl: './channel-messaging-page.component.html',
  styleUrl: './channel-messaging-page.component.scss',
})
export class ChannelMessagingPageComponent {
  readonly i18n = injectMessagingI18n();
  readonly facade = inject(MessagingFacade);
  readonly realtime = inject(RealtimeFacade);
  readonly globalSettings = inject(MessageGlobalSettingsService);
  private readonly route = inject(ActivatedRoute);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly navigationState = inject(MessageNavigationStateService);
  private readonly routeContext = signal<{
    readonly conversationId: string | null;
    readonly workspaceId: string | null;
    readonly focusMessageId: string | null;
    readonly threadRootMessageId: string | null;
  }>({ conversationId: null, workspaceId: null, focusMessageId: null, threadRootMessageId: null });
  private hasObservedActiveWorkspace = false;
  private lastLoadKey: string | null = null;
  private openedFollowUpThreadKey: string | null = null;
  readonly page = this.facade.page;
  readonly channelConversations = computed(() => {
    const workspaceId = this.activeWorkspace.activeWorkspace()?.id;
    const routeWorkspaceId = this.routeContext().workspaceId;
    if (!workspaceId || (routeWorkspaceId && routeWorkspaceId !== workspaceId)) {
      return [];
    }
    return this.page().conversations.filter((conversation) =>
      conversation.kind === 'channel' && conversation.workspaceId === workspaceId);
  });
  readonly focusMessageId = computed(() => this.routeContext().focusMessageId);
  readonly channelParticipants = computed(() => {
    const page = this.page();
    const conversation = page.conversation;
    const workspaceId = this.activeWorkspace.activeWorkspace()?.id;
    const context = this.routeContext();
    if (!workspaceId || (context.workspaceId && context.workspaceId !== workspaceId) ||
        conversation.workspaceId !== workspaceId || conversation.id !== context.conversationId ||
        conversation.kind !== 'channel' || conversation.isGroup ||
        (page.status !== 'ready' && page.status !== 'empty') || !this.canReadBody()) {
      return [];
    }
    return conversation.participants ?? [];
  });
  readonly participantRoleLabels = {
    admin: 'messaging.participants.admin',
    member: 'messaging.participants.member',
    readOnly: 'messaging.participants.readOnly',
  } as const;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((paramMap) => {
      this.navigationState.resetDetailScroll();
      this.routeContext.set({
        conversationId: paramMap.get('conversationId'),
        workspaceId: paramMap.get('workspaceId'),
        focusMessageId: this.route.snapshot?.queryParamMap.get('focusMessageId') ?? null,
        threadRootMessageId: this.route.snapshot?.queryParamMap.get('threadRootMessageId') ?? null,
      });
      this.openedFollowUpThreadKey = null;
      this.loadForCommittedScope();
    });

    effect(() => {
      this.routeContext();
      this.activeWorkspace.activeWorkspace();
      this.loadForCommittedScope();
    });

    effect(() => {
      const context = this.routeContext();
      const page = this.page();
      const key = context.focusMessageId && context.threadRootMessageId
        ? `${context.focusMessageId}:${context.threadRootMessageId}`
        : null;
      if (
        !key ||
        key === this.openedFollowUpThreadKey ||
        !page.messages.some((message) => message.id === context.threadRootMessageId)
      ) {
        return;
      }
      this.openedFollowUpThreadKey = key;
      this.facade.openThread(
        context.threadRootMessageId!,
        `message-${safeElementId(context.threadRootMessageId!)}`,
        context.focusMessageId!
      );
    });
  }

  private loadForCommittedScope(): void {
    const context = this.routeContext();
    const activeWorkspaceId = this.activeWorkspace.activeWorkspace()?.id ?? null;
    if (activeWorkspaceId) {
      this.hasObservedActiveWorkspace = true;
    }

    // During B -> A navigation, Angular may construct the A component before
    // NavigationEnd commits A. Do not start A requests under B. A first deep
    // link from null is safe because the guard already validated the route
    // and null -> A is deliberately non-destructive.
    if (
      (activeWorkspaceId && context.workspaceId && activeWorkspaceId !== context.workspaceId) ||
      (!activeWorkspaceId && this.hasObservedActiveWorkspace)
    ) {
      return;
    }

    const expectedWorkspaceId = context.workspaceId ?? activeWorkspaceId;
    const loadKey = `${context.conversationId ?? ''}:${expectedWorkspaceId ?? ''}:${context.focusMessageId ?? ''}`;
    if (loadKey === this.lastLoadKey) {
      return;
    }
    this.lastLoadKey = loadKey;
    this.facade.loadConversation(
      context.conversationId,
      'channel',
      expectedWorkspaceId,
      context.focusMessageId,
    );
  }

  readonly canReadBody = computed(
    () =>
      this.page().conversation.viewerIsParticipant &&
      !this.page().conversation.viewerWasRemoved &&
      this.page().conversation.capabilities.includes('readBody'),
  );

  readonly canPost = computed(
    () =>
      this.canReadBody() &&
      this.page().conversation.capabilities.includes('postMessage') &&
      this.page().status !== 'sessionExpired',
  );

  readonly canCreateThread = computed(() =>
    this.canPost() && this.page().conversation.capabilities.includes('createThread'),
  );

  readonly threadOpen = computed(() => this.facade.thread().status !== 'closed');

  readonly composerDisabledReason = computed(() => {
    const page = this.page();
    if (page.conversation.viewerWasRemoved) {
      return this.i18n.translate('messaging.composer.removed');
    }
    if (
      !page.conversation.viewerIsParticipant ||
      !page.conversation.capabilities.includes('readBody')
    ) {
      return this.i18n.translate('messaging.composer.noParticipation');
    }
    return page.conversation.composerDisabledReason ?? '';
  });

  readonly showComposer = computed(
    () =>
      this.page().conversation.viewerIsParticipant && !this.page().conversation.viewerWasRemoved,
  );

  readonly canViewOthersPreciseReadTimestamps = computed(() =>
    this.page().conversation.capabilities.includes('viewOthersPreciseReadTimestamps'),
  );
}

function safeElementId(value: string): string {
  return value.replace(/[^a-zA-Z0-9_-]/g, '-');
}
