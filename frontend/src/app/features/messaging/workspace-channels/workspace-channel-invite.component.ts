import { A11yModule } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, DestroyRef, Input, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { I18nService } from '../../../core/i18n/i18n.service';
import { WorkspaceRecipientOption, WorkspaceRecipientPickerComponent } from '../workspace-recipient-picker/workspace-recipient-picker.component';
import { channelHttpStatus, channelScopeId, WorkspaceChannelItem, WorkspaceChannelsApi } from './workspace-channels.api';
import { WorkspaceChannelsFacade } from './workspace-channels.facade';

const EN = {
  add: 'Add a Workspace member', title: 'Add channel member', submit: 'Add member', close: 'Close',
  pending: 'Waiting for the server…', done: 'The server confirmed membership. No message was sent.',
  error: 'Membership could not be confirmed. Check current channel members before retrying.',
  help: 'Only a current Workspace member can be added. This grants channel history access; it does not send an email invitation.'
};
const JA: Record<keyof typeof EN, string> = {
  add: 'ワークスペースのメンバーを追加', title: 'チャンネルのメンバーを追加', submit: 'メンバーを追加', close: '閉じる',
  pending: 'サーバーの応答を待っています…', done: 'サーバーで参加状態を確認しました。メッセージは送信していません。',
  error: '参加状態を確認できませんでした。再試行する前に現在のチャンネル参加者を確認してください。',
  help: '追加できるのは現在のワークスペースのメンバーのみです。追加するとチャンネルの履歴を閲覧できます。メールでの招待は送信しません。'
};

@Component({
  selector: 'app-workspace-channel-invite',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [A11yModule, WorkspaceRecipientPickerComponent],
  template: `
    <button type="button" [disabled]="!current() || !!facade.pending()" (click)="open()" data-testid="channel-invite-open">{{ text().add }}</button>
    @if (opened() && current()) {
      <div class="channels__overlay">
        <section class="channels__dialog" role="dialog" aria-modal="true" [attr.aria-labelledby]="'channel-invite-' + channel.id"
          cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown.escape)="escape($event)" data-testid="channel-invite-dialog">
          <header><h2 [id]="'channel-invite-' + channel.id">{{ text().title }} — {{ channel.name }}</h2></header>
          <div class="channels__dialog-body">
            <p>{{ text().help }}</p>
            <app-workspace-recipient-picker [workspaceId]="channel.workspaceId" [multiple]="false"
              [disabled]="pending()" [resetKey]="resetKey()" (recipientsChanged)="select($event)" />
            @if (result() === 'done') { <p role="status">{{ text().done }}</p> }
            @if (result() === 'error') { <p role="alert">{{ text().error }}</p> }
          </div>
          <footer class="channels__actions">
            <button type="button" [disabled]="pending()" (click)="close()">{{ text().close }}</button>
            <button type="button" class="channels__primary" [disabled]="pending() || !selected() || result() === 'done'"
              (click)="submit()" data-testid="channel-invite-submit">{{ pending() ? text().pending : text().submit }}</button>
          </footer>
        </section>
      </div>
    }
  `,
  styleUrl: './workspace-channels-page.component.scss'
})
export class WorkspaceChannelInviteComponent {
  @Input({ required: true }) channel!: WorkspaceChannelItem;
  readonly facade = inject(WorkspaceChannelsFacade);
  private readonly api = inject(WorkspaceChannelsApi);
  private readonly i18n = inject(I18nService);
  private readonly initialScope = this.facade.scope()?.key;
  private request?: Subscription;
  private destroyed = false;
  readonly text = computed(() => this.i18n.locale() === 'ja' ? JA : EN);
  readonly opened = signal(false);
  readonly pending = signal(false);
  readonly selected = signal<string | null>(null);
  readonly resetKey = signal(0);
  readonly result = signal<'done' | 'error' | null>(null);

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.request?.unsubscribe(); });
  }

  current(): boolean {
    return !this.destroyed && this.facade.scope()?.key === this.initialScope && this.facade.status() === 'ready' &&
      this.facade.directory()?.items.some((item) => item.id === this.channel?.id &&
        item.workspaceId === this.channel?.workspaceId && item.canManage) === true;
  }

  open(): void {
    if (!this.current() || this.facade.pending()) return;
    this.selected.set(null); this.result.set(null); this.resetKey.update((value) => value + 1); this.opened.set(true);
  }

  close(): void { if (!this.pending()) this.opened.set(false); }

  escape(event: KeyboardEvent): void {
    if (!event.isComposing && event.keyCode !== 229) this.close();
  }

  select(people: readonly WorkspaceRecipientOption[]): void {
    this.selected.set(people.length === 1 ? channelScopeId(people[0].userId) : null);
    this.result.set(null);
  }

  submit(): void {
    const recipient = this.selected();
    if (!this.current() || !recipient || this.pending() || this.result() === 'done') return;
    this.pending.set(true); this.result.set(null);
    this.request = this.api.invite(this.channel.workspaceId, this.channel.id, recipient).subscribe({
      next: () => {
        if (!this.current()) return;
        this.pending.set(false); this.selected.set(null); this.result.set('done');
      },
      error: (error: unknown) => {
        if (!this.current()) return;
        this.pending.set(false);
        if ([400, 401, 403, 404].includes(channelHttpStatus(error))) {
          this.opened.set(false); this.selected.set(null); this.facade.notice.set('denied'); void this.facade.refresh();
        } else { this.result.set('error'); }
      }
    });
  }
}
