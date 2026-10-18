import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { ConversationSettingsPanelComponent } from '../conversation-settings-panel/conversation-settings-panel.component';
import { MessageComposerComponent } from '../message-composer/message-composer.component';
import { MessageNavigationStateService } from '../message-navigation-state.service';
import { MessageTimelineComponent } from '../message-timeline/message-timeline.component';
import { MessagingFacade } from '../messaging.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { ThreadWorkspaceComponent } from '../thread-workspace/thread-workspace.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-dm-page',
  standalone: true,
  imports: [
    ConversationSettingsPanelComponent,
    MessageComposerComponent,
    MessageTimelineComponent,
    RouterLink,
    ThreadWorkspaceComponent,
  ],
  templateUrl: './dm-page.component.html',
  styleUrl: './dm-page.component.scss',
})
export class DmPageComponent {
  readonly i18n = injectMessagingI18n();
  readonly facade = inject(MessagingFacade);
  readonly realtime = inject(RealtimeFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly navigationState = inject(MessageNavigationStateService);
  readonly page = this.facade.page;
  readonly focusMessageId = signal<string | null>(null);
  readonly threadRootMessageId = signal<string | null>(null);
  private openedFollowUpThreadKey: string | null = null;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((paramMap) => {
      this.navigationState.resetDetailScroll();
      const focusMessageId = this.route.snapshot?.queryParamMap.get('focusMessageId') ?? null;
      this.focusMessageId.set(focusMessageId);
      this.threadRootMessageId.set(this.route.snapshot?.queryParamMap.get('threadRootMessageId') ?? null);
      this.openedFollowUpThreadKey = null;
      this.facade.loadConversation(paramMap.get('conversationId'), 'dm', null, focusMessageId);
    });

    effect(() => {
      const focusMessageId = this.focusMessageId();
      const threadRootMessageId = this.threadRootMessageId();
      const page = this.page();
      const key = focusMessageId && threadRootMessageId ? `${focusMessageId}:${threadRootMessageId}` : null;
      if (!key || key === this.openedFollowUpThreadKey || !page.messages.some((message) => message.id === threadRootMessageId)) {
        return;
      }
      this.openedFollowUpThreadKey = key;
      this.facade.openThread(
        threadRootMessageId!,
        `message-${safeElementId(threadRootMessageId!)}`,
        focusMessageId!
      );
    });
  }

  readonly canReadBody = computed(
    () =>
      this.page().conversation.kind === 'dm' &&
      this.page().conversation.viewerIsParticipant &&
      !this.page().conversation.viewerWasRemoved &&
      this.page().conversation.capabilities.includes('readBody'),
  );

  readonly canPost = computed(
    () => this.canReadBody() && this.page().conversation.capabilities.includes('postMessage'),
  );

  readonly canCreateThread = computed(() =>
    this.canPost() && this.page().conversation.capabilities.includes('createThread'),
  );

  readonly threadOpen = computed(() => this.facade.thread().status !== 'closed');

  readonly showComposer = computed(
    () =>
      this.page().conversation.viewerIsParticipant && !this.page().conversation.viewerWasRemoved,
  );

  readonly composerDisabledReason = computed(() => {
    const page = this.page();
    if (!page.conversation.viewerIsParticipant) {
      return this.i18n.translate('messaging.composer.noDmParticipation');
    }
    return page.conversation.composerDisabledReason ?? '';
  });
}

function safeElementId(value: string): string {
  return value.replace(/[^a-zA-Z0-9_-]/g, '-');
}
