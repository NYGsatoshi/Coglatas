import { AfterViewChecked, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import {
  LucideEllipsis,
  LucideBookmarkPlus,
  LucideFlag,
  LucideMessageSquare,
  LucidePencil,
  LucideTrash
} from '@lucide/angular';

import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import { MessagingMessageActionState, MessagingMessageViewModel, MessagingRouteKind } from '../messaging.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-message-item',
  standalone: true,
  imports: [CoglatasDialogComponent, LucideBookmarkPlus, LucideEllipsis, LucideFlag, LucideMessageSquare, LucidePencil, LucideTrash],
  template: `
    <article
      #messageArticle
      class="message"
      [attr.data-testid]="message.deliveryState === 'confirmed' ? 'confirmed-message' : 'pending-message'"
      [attr.id]="messageElementId"
      [class.message--own]="message.isOwnMessage"
      [class.message--dm]="routeKind === 'dm'"
      [class.message--focus-target]="focusTarget"
      [attr.tabindex]="focusTarget ? -1 : null"
    >
      <header class="message__meta">
        <span class="message__author">{{ message.authorLabel }}</span>
        <span class="message__role">{{ message.authorRoleLabel }}</span>
        <span class="message__time">{{ message.sentAtLabel }}</span>
        @if (message.editedAt) {
          <span class="message__edited" data-testid="message-edited-marker">{{ i18n.translate('messaging.actions.edited') }}</span>
        }
      </header>

      @if (!message.isDeleted) {
        @if (isEditing) {
          <form class="message__edit-form" (submit)="saveEdit($event)">
            <label [attr.for]="editInputId">{{ i18n.translate('messaging.actions.editMessage') }}</label>
            <textarea
              #editInput
              [id]="editInputId"
              [value]="messageAction.draft"
              [disabled]="isBusy"
              [attr.data-testid]="'message-edit-input-' + message.id"
              (input)="changeEditDraft($event)"
            ></textarea>
            @if (messageAction.error) {
              <p class="message__action-error" role="alert">{{ messageAction.error }}</p>
            }
            <div class="message__edit-actions">
              <button type="button" [disabled]="isBusy" (click)="cancelEdit()">{{ i18n.translate('common.cancel') }}</button>
              <button type="submit" [disabled]="isBusy" [attr.data-testid]="'save-message-edit-' + message.id">
                {{ i18n.translate(isBusy ? 'messaging.actions.saving' : 'messaging.actions.saveChanges') }}
              </button>
            </div>
          </form>
        } @else {
          <p class="message__body" data-testid="message-body">{{ message.body }}</p>
        }
      } @else {
        <p class="message__tombstone" data-testid="message-tombstone">{{ i18n.translate('messaging.deleted') }}</p>
      }

      @if (canShowActions || canShowThreadEntry) {
          <div class="message__actions" role="group" [attr.aria-label]="i18n.translate('messaging.actions.groupAria', { name: message.authorLabel })">
            @if (canShowThreadEntry) {
              <button
                type="button"
                class="message__thread-entry"
                [attr.id]="threadButtonId"
                [attr.data-testid]="'open-message-thread-' + message.id"
                [attr.aria-label]="threadEntryAriaLabel"
                (click)="openThread.emit({ messageId: message.id, triggerElementId: threadButtonId })"
              >
                <svg lucideMessageSquare aria-hidden="true"></svg>
                <span aria-hidden="true">&#x21B3;</span>
                <span>{{ threadEntryLabel }}</span>
              </button>
            }
            @if (canShowActions) {
              <button
                type="button"
                class="message__action-button message__action-button--primary"
                [attr.data-testid]="'save-message-for-later-' + message.id"
                [attr.aria-label]="i18n.translate('messaging.actions.saveAria', { name: message.authorLabel })"
                [disabled]="isBusy || hasActiveAction"
                (click)="saveForLater()"
              >
                <svg lucideBookmarkPlus aria-hidden="true"></svg>
                <span>{{ i18n.translate('messaging.actions.saveLater') }}</span>
              </button>
              <div class="message__overflow">
                <button
                  type="button"
                  class="message__action-button message__more-button"
                  [attr.id]="moreButtonId"
                  [attr.data-testid]="'message-more-actions-' + message.id"
                  [attr.aria-label]="i18n.translate('messaging.actions.moreAria', { name: message.authorLabel })"
                  [attr.aria-expanded]="overflowOpen"
                  [attr.aria-controls]="overflowPanelId"
                  [disabled]="isBusy || hasActiveAction"
                  (click)="toggleOverflow()"
                >
                  <svg lucideEllipsis aria-hidden="true"></svg>
                  <span>{{ i18n.translate('messaging.actions.more') }}</span>
                </button>
                @if (overflowOpen && !hasActiveAction) {
                  <div class="message__overflow-panel" [attr.id]="overflowPanelId" data-testid="message-action-overflow">
                    @if (canEditMessage) {
                      <button
                        type="button"
                        class="message__action-button"
                        [attr.id]="editButtonId"
                        [attr.data-testid]="'edit-message-' + message.id"
                        [attr.aria-label]="i18n.translate('messaging.actions.editAria', { name: message.authorLabel })"
                        (click)="openEdit()"
                      >
                        <svg lucidePencil aria-hidden="true"></svg>
                        <span>{{ i18n.translate('messaging.actions.edit') }}</span>
                      </button>
                    }
                    @if (message.isOwnMessage) {
                      <button
                        type="button"
                        class="message__action-button message__action-button--danger"
                        [attr.data-testid]="'delete-message-' + message.id"
                        [attr.aria-label]="i18n.translate('messaging.actions.deleteAria', { name: message.authorLabel })"
                        (click)="openDelete()"
                      >
                        <svg lucideTrash aria-hidden="true"></svg>
                        <span>{{ i18n.translate('messaging.actions.delete') }}</span>
                      </button>
                    }
                    <button
                      type="button"
                      class="message__action-button"
                      [attr.data-testid]="'report-message-' + message.id"
                      [attr.aria-label]="i18n.translate('messaging.actions.reportAria', { name: message.authorLabel })"
                      (click)="openReport()"
                    >
                      <svg lucideFlag aria-hidden="true"></svg>
                      <span>{{ i18n.translate('messaging.actions.report') }}</span>
                    </button>
                  </div>
                }
              </div>
            }
          </div>
      }

      @if (message.readState?.ownReadLabel) {
        <p class="message__read" data-testid="own-read-marker">{{ message.readState?.ownReadLabel }}</p>
      }
      @if (message.readState?.otherReadSummaryLabel) {
        <p class="message__read" data-testid="other-read-summary">{{ message.readState?.otherReadSummaryLabel }}</p>
      }
      @if (canViewOthersPreciseReadTimestamps && message.readState?.otherReadPreciseTimestampLabel) {
        <p class="message__read" data-testid="other-read-precise">
          {{ message.readState?.otherReadPreciseTimestampLabel }}
        </p>
      }
    </article>

    <app-coglatas-dialog
      [open]="isDeleteConfirmation"
      [title]="i18n.translate('messaging.actions.deleteTitle')"
      [description]="i18n.translate('messaging.actions.deleteHelp')"
      [confirmLabel]="i18n.translate('messaging.actions.deleteConfirm')"
      [busy]="isBusy"
      [destructive]="true"
      [focusReturnFallbackId]="moreButtonId"
      (confirm)="confirmDelete.emit(message.id)"
      (cancel)="cancelAction.emit()"
    >
      @if (messageAction.error) {
        <p class="message__action-error" role="alert">{{ messageAction.error }}</p>
      }
    </app-coglatas-dialog>

    <app-coglatas-dialog
      [open]="isReportConfirmation"
      [title]="i18n.translate('messaging.actions.reportTitle')"
      [description]="i18n.translate('messaging.actions.reportDescription')"
      [confirmLabel]="i18n.translate('messaging.actions.reportConfirm')"
      [busy]="isBusy"
      [focusReturnFallbackId]="moreButtonId"
      (confirm)="confirmReport.emit({ messageId: message.id, reasonCode: 'reported' })"
      (cancel)="cancelAction.emit()"
    >
      <p>{{ i18n.translate('messaging.actions.reportHelp') }}</p>
      @if (messageAction.error) {
        <p class="message__action-error" role="alert">{{ messageAction.error }}</p>
      }
    </app-coglatas-dialog>
  `,
  styleUrl: './message-item.component.scss',
})
export class MessageItemComponent implements AfterViewChecked {
  readonly i18n = injectMessagingI18n();
  @ViewChild('messageArticle') private messageArticle?: ElementRef<HTMLElement>;
  @ViewChild('editInput') private editInput?: ElementRef<HTMLTextAreaElement>;
  @Input() routeKind: MessagingRouteKind = 'channel';
  @Input({ required: true }) message!: MessagingMessageViewModel;
  @Input({ required: true }) messageAction!: MessagingMessageActionState;
  @Input() canEditOwnMessages = false;
  @Input() canViewOthersPreciseReadTimestamps = false;
  @Input() canOpenThreads = false;
  @Input() focusTarget = false;

  @Output() readonly startEdit = new EventEmitter<string>();
  @Output() readonly editDraftChange = new EventEmitter<{ readonly messageId: string; readonly draft: string }>();
  @Output() readonly saveEditRequested = new EventEmitter<string>();
  @Output() readonly cancelAction = new EventEmitter<void>();
  @Output() readonly requestDelete = new EventEmitter<string>();
  @Output() readonly confirmDelete = new EventEmitter<string>();
  @Output() readonly requestReport = new EventEmitter<string>();
  @Output() readonly saveForLaterRequested = new EventEmitter<string>();
  @Output() readonly confirmReport = new EventEmitter<{ readonly messageId: string; readonly reasonCode: string }>();
  @Output() readonly openThread = new EventEmitter<{
    readonly messageId: string;
    readonly triggerElementId: string;
  }>();

  overflowOpen = false;
  private focusedEditorKey: string | null = null;
  private focusedTargetId: string | null = null;

  get messageElementId(): string {
    return `message-${this.safeId}`;
  }

  get editButtonId(): string {
    return `edit-message-${this.safeId}`;
  }

  get moreButtonId(): string {
    return `message-more-actions-${this.safeId}`;
  }

  get editInputId(): string {
    return `edit-message-input-${this.safeId}`;
  }

  get overflowPanelId(): string {
    return `message-action-overflow-${this.safeId}`;
  }

  get threadButtonId(): string {
    return `open-message-thread-${this.safeId}`;
  }

  get canShowThreadEntry(): boolean {
    return this.canOpenThreads &&
      this.message.deliveryState === 'confirmed' &&
      !this.message.threadRootMessageId &&
      (!this.message.isDeleted || (this.message.thread?.replyCount ?? 0) > 0);
  }

  get threadEntryLabel(): string {
    const count = this.message.thread?.replyCount ?? 0;
    return count > 0
      ? this.i18n.translate(count === 1 ? 'messaging.thread.replyCountOne' : 'messaging.thread.replyCount', { count: this.i18n.formatNumber(count) })
      : this.i18n.translate('messaging.thread.replyInThread');
  }

  get threadEntryAriaLabel(): string {
    return this.i18n.translate('messaging.thread.entryAria', { action: this.threadEntryLabel, name: this.message.authorLabel });
  }

  get isEditing(): boolean {
    return this.messageAction.messageId === this.message.id && this.messageAction.mode === 'editing';
  }

  get isDeleteConfirmation(): boolean {
    return this.messageAction.messageId === this.message.id && this.messageAction.mode === 'confirmDelete';
  }

  get isReportConfirmation(): boolean {
    return this.messageAction.messageId === this.message.id && this.messageAction.mode === 'confirmReport';
  }

  get isBusy(): boolean {
    return this.messageAction.messageId === this.message.id && this.messageAction.pending !== null;
  }

  get hasActiveAction(): boolean {
    return this.messageAction.mode !== 'idle';
  }

  get canShowActions(): boolean {
    return this.message.deliveryState === 'confirmed' && !this.message.isDeleted && !this.isEditing;
  }

  get canEditMessage(): boolean {
    return this.message.isOwnMessage && this.canEditOwnMessages;
  }

  toggleOverflow(): void {
    this.overflowOpen = !this.overflowOpen;
  }

  openEdit(): void {
    this.overflowOpen = false;
    this.startEdit.emit(this.message.id);
  }

  changeEditDraft(event: Event): void {
    this.editDraftChange.emit({
      messageId: this.message.id,
      draft: (event.target as HTMLTextAreaElement).value
    });
  }

  saveEdit(event: SubmitEvent): void {
    event.preventDefault();
    this.saveEditRequested.emit(this.message.id);
  }

  cancelEdit(): void {
    this.cancelAction.emit();
    queueMicrotask(() => document.getElementById(this.moreButtonId)?.focus());
  }

  openDelete(): void {
    this.overflowOpen = false;
    this.requestDelete.emit(this.message.id);
  }

  openReport(): void {
    this.overflowOpen = false;
    this.requestReport.emit(this.message.id);
  }

  saveForLater(): void {
    this.overflowOpen = false;
    this.saveForLaterRequested.emit(this.message.id);
  }

  private get safeId(): string {
    return this.message.id.replace(/[^a-zA-Z0-9_-]/g, '-');
  }

  ngAfterViewChecked(): void {
    const targetId = this.focusTarget ? this.message.id : null;
    if (targetId && targetId !== this.focusedTargetId) {
      this.focusedTargetId = targetId;
      queueMicrotask(() => this.messageArticle?.nativeElement.focus());
    } else if (!targetId) {
      this.focusedTargetId = null;
    }
    const editorKey = this.isEditing && !this.isBusy ? `${this.message.id}:${this.messageAction.mode}` : null;
    if (!editorKey || editorKey === this.focusedEditorKey) {
      if (!editorKey) {
        this.focusedEditorKey = null;
      }
      return;
    }
    this.focusedEditorKey = editorKey;
    queueMicrotask(() => this.editInput?.nativeElement.focus());
  }
}
