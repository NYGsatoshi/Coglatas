import { DatePipe } from '@angular/common';
import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { EMPTY, catchError, distinctUntilChanged, filter, forkJoin, fromEvent, interval, map, merge, Observable } from 'rxjs';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { createCryptographicUuid, createLocalOpaqueId } from '../../core/security/client-id';
import { AnswerValue, AqApi, Page, aqSegment } from './aq-api';

type Relation = 'own' | 'assigned' | 'granted' | 'managed';
interface ActionCapabilities {
  report: boolean;
  invite: boolean;
  privateNote: boolean;
  share: boolean;
  exportAggregate: boolean;
  exportDetail: boolean;
}
interface Workspace {
  presentation: { id: string; eventId: string; title: string };
  relations: Relation[];
  capabilities?: ActionCapabilities;
}
interface Invitation {
  id: string;
  presentationLabel: string;
  direction: 'issued' | 'received';
  status: 'pending' | 'active' | 'declined' | 'revoked' | 'expired';
  versionNo: number;
  expiresAt?: string;
}
interface FeedbackAnswer { id: string; fieldKey: string; label?: string; visibility: 'visible' | 'hidden'; value?: AnswerValue; visibilityVersionNo?: number }
interface FeedbackRow { id: string; presentationId: string; submittedAt: string; answers: FeedbackAnswer[] }
interface QuestionSummary {
  fieldKey: string;
  label?: string;
  denominator: number;
  mean?: number;
  distribution?: { optionKey: string; responses: number }[];
}
interface Note { body: string }
interface Recipient { recipientRef: string; label: string }
interface ExportRequest { exportRequestId: string }
interface ExportStatus { state: 'queued' | 'building' | 'ready' | 'failed' | 'expired' }
interface Summary { responses: number; questions: QuestionSummary[] }

const relationLabels: Record<Relation, string> = {
  own: '自分の発表',
  assigned: '担当の発表',
  granted: '招待された発表',
  managed: '運営中の発表',
};
const invitationLabels: Record<Invitation['status'], string> = {
  pending: '承認・同意待ち', active: '有効', declined: '辞退済み', revoked: '取り消し済み', expired: '期限切れ',
};

@Component({
  imports: [RouterLink, DatePipe],
  styleUrl: './aq-page.css',
  template: `
    <section lang="ja" class="aq-page">
      <h1>発表のフィードバック</h1>
      <a routerLink="/aq/feedback/inbox">レビュー受信箱</a>
      @if (loading()) { <p role="status">読み込み中…</p> }
      @if (failed()) { <p role="alert">一覧を読み込めませんでした。</p> }
      <button type="button" (click)="load()">更新</button>
      @for (category of relations; track category) {
        <h2>{{ relationLabels[category] }}</h2>
        <ul class="aq-list">
          @for (item of related(category); track item.presentation.id) {
            <li>
              <a [routerLink]="['/aq/feedback', item.presentation.id]">{{ item.presentation.title }}</a>
              @if (category === 'managed') {
                <p><a [routerLink]="['/aq/manage', item.presentation.eventId]">発表イベントの運営</a></p>
              }
            </li>
          } @empty { <li>該当する発表はありません。</li> }
        </ul>
      }
      @if (more()) { <button type="button" [disabled]="loading()" (click)="load(more())">発表をさらに表示</button> }
      <h2>閲覧招待</h2>
      @if (invitationFailure()) { <p role="alert">閲覧招待の操作を完了できませんでした。</p> }
      <ul class="aq-list">
        @for (invitation of invitations(); track invitation.id) {
          <li>
            <p>{{ invitation.presentationLabel }} · {{ invitation.direction === 'received' ? '受け取った招待' : '送った招待' }} · {{ invitationLabels[invitation.status] }}</p>
            @if (invitation.expiresAt) {
              <p><time [attr.datetime]="invitation.expiresAt">{{ invitation.expiresAt | date:'medium' }}</time></p>
            }
            <div class="aq-actions">
              @if (invitation.direction === 'received' && invitation.status === 'pending') {
                <button type="button" [disabled]="invitationBusy()" (click)="act(invitation, 'accept')">閲覧に同意する</button>
                <button type="button" [disabled]="invitationBusy()" (click)="act(invitation, 'decline')">辞退</button>
              }
              @if (invitation.status === 'active' || (invitation.direction === 'issued' && invitation.status === 'pending')) {
                <button type="button" [disabled]="invitationBusy()" (click)="act(invitation, 'revoke')">招待を取り消す</button>
              }
            </div>
          </li>
        } @empty { <li>現在、表示できる招待はありません。</li> }
      </ul>
      @if (invitationMore()) {
        <button type="button" [disabled]="invitationBusy()" (click)="loadInvitations(invitationMore())">招待をさらに表示</button>
      }
    </section>
  `,
})
export class AqFeedbackHome {
  private readonly api = inject(AqApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly relationLabels = relationLabels;
  protected readonly invitationLabels = invitationLabels;
  protected readonly relations: Relation[] = ['own', 'assigned', 'granted', 'managed'];
  protected readonly workspaces = signal<Workspace[]>([]);
  protected readonly invitations = signal<Invitation[]>([]);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly invitationFailure = signal(false);
  protected readonly invitationBusy = signal(false);
  protected readonly more = signal('');
  protected readonly invitationMore = signal('');
  private generation = 0;
  private invitationGeneration = 0;

  constructor() {
    bindFeedbackRefresh(() => {
      this.workspaces.set([]);
      this.invitations.set([]);
      this.load();
    });
  }

  protected related(relation: Relation): Workspace[] {
    return this.workspaces().filter(item => item.relations.includes(relation));
  }

  protected load(cursor?: string): void {
    const generation = ++this.generation;
    this.loading.set(true);
    this.failed.set(false);
    this.api.read<Page<Workspace>>('me/presentation-feedback', cursor ? { cursor } : {})
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: page => {
          if (generation !== this.generation) return;
          this.workspaces.set(cursor ? [...this.workspaces(), ...page.items] : page.items);
          this.more.set(page.nextCursor ?? '');
          this.loading.set(false);
        },
        error: () => {
          if (generation !== this.generation) return;
          this.workspaces.set([]);
          this.more.set('');
          this.failed.set(true);
          this.loading.set(false);
        },
      });
    if (!cursor) this.loadInvitations();
  }

  protected loadInvitations(cursor?: string): void {
    const generation = ++this.invitationGeneration;
    this.invitationBusy.set(true);
    this.api.read<Page<Invitation>>('me/feedback-invitations', cursor ? { cursor } : {})
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: page => {
          if (generation !== this.invitationGeneration) return;
          this.invitations.set(cursor ? [...this.invitations(), ...page.items] : page.items);
          this.invitationMore.set(page.nextCursor ?? '');
          this.invitationBusy.set(false);
        },
        error: () => {
          if (generation !== this.invitationGeneration) return;
          this.invitations.set([]);
          this.invitationMore.set('');
          this.invitationBusy.set(false);
        },
      });
  }

  protected act(invitation: Invitation, action: 'accept' | 'decline' | 'revoke'): void {
    if (this.invitationBusy()) return;
    const expectedVersionNo = invitation.versionNo;
    const body = action === 'accept' ? { expectedVersionNo, consent: true }
      : action === 'revoke' ? { expectedVersionNo, reason: '閲覧招待の終了' } : { expectedVersionNo };
    this.invitationBusy.set(true);
    this.invitationFailure.set(false);
    this.api.command('POST', 'feedback-invitations/' + aqSegment(invitation.id) + '/' + action, body)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => this.load(),
        error: () => {
          this.invitationFailure.set(true);
          this.loadInvitations();
        },
      });
  }
}

@Component({
  imports: [RouterLink, DatePipe],
  styleUrl: './aq-page.css',
  template: `
    <section lang="ja" class="aq-page">
      <a routerLink="/aq/feedback">発表一覧へ</a>
      <h1>発表のフィードバック</h1>
      <button type="button" (click)="load()">更新</button>
      @if (loading()) { <p role="status">読み込み中…</p> }
      @if (failed()) { <p role="alert">フィードバックを表示できません。</p> }
      @if (summary(); as totals) {
        <h2>集計</h2>
        <p>回答数 {{ totals.responses }}</p>
        <ul class="aq-list">
          @for (question of totals.questions; track question.fieldKey) {
            <li>
              <strong>{{ question.label ?? question.fieldKey }}</strong> · 有効回答 {{ question.denominator }}
              @if (question.mean !== undefined) { · 平均 {{ question.mean }} }
              @for (option of question.distribution ?? []; track option.optionKey) {
                <p>{{ option.optionKey }} · {{ option.responses }}件</p>
              }
            </li>
          }
        </ul>
        <h2>個別の回答</h2>
        <ul class="aq-list">
          @for (response of rows(); track response.id) {
            <li>
              <time [attr.datetime]="response.submittedAt">{{ response.submittedAt | date:'medium' }}</time>
              @for (answer of response.answers; track answer.id) {
                @if (answer.visibility === 'visible') {
                  <p><strong>{{ answer.label ?? answer.fieldKey }}</strong> {{ display(answer.value) }}</p>
                  @if (mayReport()) {
                    <button type="button" [disabled]="actionBusy()" (click)="reportAnswer.set(answer.id)">この回答を通報</button>
                  }
                } @else if (answer.visibilityVersionNo) {
                  <p>{{ answer.label ?? answer.fieldKey }} · 非表示</p>
                  <button type="button" [disabled]="actionBusy()" (click)="readHidden(answer)">監査記録付きで原文を確認</button>
                }
                @if (answer.visibilityVersionNo) {
                  <button type="button" [disabled]="actionBusy()" (click)="moderating.set(answer)">
                    {{ answer.visibility === 'visible' ? '非表示にする' : '表示を再開する' }}
                  </button>
                }
              }
            </li>
          } @empty { <li>表示できる回答はありません。</li> }
        </ul>
        @if (more()) { <button type="button" [disabled]="loading()" (click)="load(more())">さらに表示</button> }
        @if (reportAnswer()) {
          <form class="aq-panel" (submit)="sendReport($event)">
            <label>通報の理由 <textarea required [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></label>
            <div class="aq-actions">
              <button type="submit" [disabled]="actionBusy()">通報する</button>
              <button type="button" (click)="reportAnswer.set('')">キャンセル</button>
            </div>
          </form>
        }
        @if (hiddenBody()) {
          <p class="aq-panel">{{ hiddenBody() }}</p>
          <button type="button" (click)="hiddenBody.set('')">原文を閉じる</button>
        }
        @if (moderating(); as answer) {
          <form class="aq-panel" (submit)="moderate($event, answer)">
            <label>表示設定の変更理由 <textarea required [value]="moderationReason()" (input)="moderationReason.set($any($event.target).value)"></textarea></label>
            <div class="aq-actions">
              <button type="submit" [disabled]="actionBusy()">変更を確定する</button>
              <button type="button" (click)="moderating.set(null)">キャンセル</button>
            </div>
          </form>
        }
        @if (actionMessage()) { <p role="status">{{ actionMessage() }}</p> }
        @if (actionFailed()) { <p role="alert">操作を完了できませんでした。</p> }
        @if (capabilities()?.invite) {
          <h2>閲覧招待</h2>
          @if (recipients().length) {
            <label>招待する参加者
              <select (change)="recipient.set($any($event.target).value)">
                <option value="">選択してください</option>
                @for (entry of recipients(); track entry.recipientRef) {
                  <option [value]="entry.recipientRef">{{ entry.label }}</option>
                }
              </select>
            </label>
            <button type="button" [disabled]="!recipient() || actionBusy()" (click)="invite()">承認待ちの招待を送る</button>
          } @else { <p>招待できる参加者は表示されていません。</p> }
        }
        @if (capabilities()?.privateNote || capabilities()?.share) {
          <h2>レビュー</h2>
          <form (submit)="writeNote($event)">
            <label>コメント <textarea required [value]="noteBody()" (input)="noteBody.set($any($event.target).value)"></textarea></label>
            <label>公開範囲
              <select (change)="noteKind.set($any($event.target).value)">
                @if (capabilities()?.privateNote) { <option value="private-notes">自分の非公開メモ</option> }
                @if (capabilities()?.share) { <option value="shared-feedback">発表者に共有する講評</option> }
              </select>
            </label>
            <button type="submit" [disabled]="actionBusy()">保存</button>
          </form>
          @for (note of privateNotes(); track $index) { <p class="aq-panel">{{ note.body }}</p> }
        }
        @if (capabilities()?.exportAggregate || capabilities()?.exportDetail) {
          <h2>CSV出力</h2>
          @if (capabilities()?.exportAggregate) {
            <button type="button" [disabled]="actionBusy()" (click)="requestCsv('aggregate')">集計CSVを準備</button>
          }
          @if (capabilities()?.exportDetail) {
            <button type="button" [disabled]="actionBusy()" (click)="requestCsv('detail')">詳細CSVを準備</button>
          }
          @if (exportId()) {
            <button type="button" [disabled]="actionBusy()" (click)="receiveCsv()">出力状況を確認・保存</button>
          }
        }
        @if (shared().length) {
          <h2>共有された講評</h2>
          @for (item of shared(); track $index) {
            <p class="aq-panel">{{ item.body }}</p>
          }
        }
      }
    </section>
  `,
})
export class AqFeedbackDetail {
  private readonly api = inject(AqApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly summary = signal<Summary | null>(null);
  protected readonly shared = signal<Note[]>([]);
  protected readonly rows = signal<FeedbackRow[]>([]);
  protected readonly more = signal('');
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly capabilities = signal<ActionCapabilities | undefined>(undefined);
  protected readonly recipients = signal<Recipient[]>([]);
  protected readonly recipient = signal('');
  protected readonly privateNotes = signal<Note[]>([]);
  protected readonly reportAnswer = signal('');
  protected readonly moderating = signal<FeedbackAnswer | null>(null);
  protected readonly moderationReason = signal('');
  protected readonly hiddenBody = signal('');
  protected readonly reason = signal('');
  protected readonly noteBody = signal('');
  protected readonly noteKind = signal('private-notes');
  protected readonly actionBusy = signal(false);
  protected readonly actionFailed = signal(false);
  protected readonly actionMessage = signal('');
  protected readonly exportId = signal('');
  private generation = 0;
  private id = '';

  constructor() {
    inject(ActivatedRoute).paramMap.pipe(
      map(params => params.get('presentationId') ?? ''),
      distinctUntilChanged(),
      takeUntilDestroyed(),
    ).subscribe(id => {
      this.id = id;
      this.exportId.set('');
      this.load();
    });
    bindFeedbackRefresh(() => this.load(), () => this.id);
  }

  protected display(value: AnswerValue | undefined): string {
    if (value === undefined) return '';
    if (Array.isArray(value)) return value.join('、');
    if (typeof value === 'boolean') return value ? 'はい' : 'いいえ';
    return String(value);
  }

  protected load(cursor?: string): void {
    if (!this.id) return;
    const generation = ++this.generation;
    const path = 'presentations/' + aqSegment(this.id);
    const rows = this.api.read<Page<FeedbackRow>>(path + '/feedback', cursor ? { cursor } : {});
    this.loading.set(true);
    this.failed.set(false);
    if (!cursor) {
      this.clear();
      this.api.read<Page<Note>>(path + '/shared-feedback').pipe(
        catchError(() => EMPTY), takeUntilDestroyed(this.destroyRef),
      ).subscribe(page => {
        if (generation === this.generation && !this.failed()) this.shared.set(page.items);
      });
    }
    forkJoin({ rows, summary: this.api.read<Summary>(path + '/feedback-summary') })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: data => {
          if (generation !== this.generation) return;
          this.rows.set(cursor ? [...this.rows(), ...data.rows.items] : data.rows.items);
          this.summary.set(data.summary);
          this.more.set(data.rows.nextCursor ?? '');
          this.loading.set(false);
          if (!cursor) this.loadActions(generation);
        },
        error: () => {
          if (generation !== this.generation) return;
          this.clear();
          this.loading.set(false);
          this.failed.set(true);
        },
      });
  }

  protected mayReport(): boolean {
    return this.capabilities()?.report === true;
  }

  protected sendReport(event: Event): void {
    event.preventDefault();
    const reason = this.reason().trim();
    if (!reason || !this.reportAnswer()) return;
    this.perform(this.api.command('POST', 'feedback/answers/' + aqSegment(this.reportAnswer()) + '/reports', { reason }),
      '通報を受け付けました。', () => {
        this.reportAnswer.set('');
        this.reason.set('');
      });
  }

  protected moderate(event: Event, answer: FeedbackAnswer): void {
    event.preventDefault();
    const reason = this.moderationReason().trim();
    if (!answer.visibilityVersionNo || !reason) return;
    this.perform(this.api.command('PATCH', 'feedback/answers/' + aqSegment(answer.id) + '/visibility', {
      expectedVersionNo: answer.visibilityVersionNo,
      action: answer.visibility === 'visible' ? 'hide' : 'restore',
      reason,
    }), '回答の表示設定を更新しました。', () => this.load());
  }

  protected readHidden(answer: FeedbackAnswer): void {
    this.perform(this.api.read<{ body: string }>('feedback/answers/' + aqSegment(answer.id) + '/moderation-detail'),
      '非表示の原文を確認しています。', value => this.hiddenBody.set(value.body));
  }

  protected invite(): void {
    if (!this.recipient()) return;
    this.perform(this.api.command('POST', this.presentationPath() + '/feedback-invitations', { recipientRef: this.recipient() }),
      '承認待ちの招待を送信しました。', () => this.recipient.set(''));
  }

  protected writeNote(event: Event): void {
    event.preventDefault();
    const body = this.noteBody().trim();
    if (!body) return;
    this.perform(this.api.command('POST', this.presentationPath() + '/' + this.noteKind(), { body }),
      'コメントを保存しました。', () => {
        this.noteBody.set('');
        this.load();
      });
  }

  protected requestCsv(mode: 'aggregate' | 'detail'): void {
    const columns = mode === 'aggregate' ? ['questionLabel', 'choiceCount'] : ['questionLabel', 'answerValue'];
    this.perform(this.api.command<ExportRequest>('POST', this.presentationPath() + '/feedback-export', {
      mode, columns,
    }, createCryptographicUuid()), 'CSV出力を要求しました。', result => this.exportId.set(result.exportRequestId));
  }

  protected receiveCsv(): void {
    const path = 'presentation-feedback-exports/' + aqSegment(this.exportId());
    this.perform(this.api.read<ExportStatus>(path), 'CSVの出力状態を確認しました。', result => {
      if (result.state !== 'ready') {
        this.actionMessage.set(result.state === 'queued' || result.state === 'building'
          ? 'CSVを準備中です。' : 'このCSVを保存できません。');
        return;
      }
      this.perform(this.api.command<{ grantId: string }>('POST', path + '/download-grants', {}), '保存の準備ができました。', grant => {
        this.perform(this.api.download(path + '/download-grants/' + aqSegment(grant.grantId) + '/content'),
          'CSVを保存しました。', blob => {
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = 'presentation-feedback.csv';
            link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
          });
      });
    });
  }

  private presentationPath(): string {
    return 'presentations/' + aqSegment(this.id);
  }

  private perform<T>(operation: Observable<T>, message: string, done: (data: T) => void): void {
    if (this.actionBusy()) return;
    const generation = this.generation;
    this.actionBusy.set(true);
    this.actionFailed.set(false);
    operation.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => {
        if (generation !== this.generation) return;
        this.actionBusy.set(false);
        this.actionMessage.set(message);
        done(data);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.actionBusy.set(false);
        this.actionFailed.set(true);
      },
    });
  }

  private loadActions(generation: number): void {
    this.api.read<Page<Workspace>>('me/presentation-feedback', { pageSize: '100' }).pipe(
      catchError(() => EMPTY), takeUntilDestroyed(this.destroyRef),
    ).subscribe(page => {
      if (this.generation !== generation || this.failed()) return;
      const capabilities = page.items.find(item => item.presentation.id === this.id)?.capabilities;
      this.capabilities.set(capabilities);
      this.noteKind.set(capabilities?.privateNote ? 'private-notes' : 'shared-feedback');
      if (capabilities?.invite) {
        this.api.read<Page<Recipient>>(this.presentationPath() + '/feedback-invitation-recipients')
          .pipe(catchError(() => EMPTY), takeUntilDestroyed(this.destroyRef)).subscribe(result => {
            if (this.generation === generation && !this.failed()) this.recipients.set(result.items);
          });
      }
      if (capabilities?.privateNote) {
        this.api.read<Page<Note>>(this.presentationPath() + '/private-notes')
          .pipe(catchError(() => EMPTY), takeUntilDestroyed(this.destroyRef)).subscribe(result => {
            if (this.generation === generation && !this.failed()) this.privateNotes.set(result.items);
          });
      }
    });
  }

  private clear(): void {
    this.capabilities.set(undefined);
    this.recipients.set([]);
    this.privateNotes.set([]);
    this.recipient.set('');
    this.reportAnswer.set('');
    this.reason.set('');
    this.moderating.set(null);
    this.moderationReason.set('');
    this.hiddenBody.set('');
    this.noteBody.set('');
    this.actionBusy.set(false);
    this.rows.set([]);
    this.summary.set(null);
    this.shared.set([]);
    this.more.set('');
  }
}

export function bindFeedbackRefresh(refresh: () => void, presentationId?: () => string): void {
  const realtime = inject(RealtimeFacade);
  const destroy = inject(DestroyRef);
  const key = createLocalOpaqueId('aq');
  destroy.onDestroy(realtime.registerProtectedStateClearer(key, refresh));
  destroy.onDestroy(realtime.registerCatchUp(key, refresh));
  merge(fromEvent(document, 'visibilitychange'), interval(30_000)).pipe(
    filter(() => document.visibilityState === 'visible'),
    takeUntilDestroyed(destroy),
  ).subscribe(refresh);
  realtime.durableEvents$.pipe(
    filter(event => event.eventType === 'Feedback.PresentationInvalidated.v1' &&
      (!presentationId || presentationId() === event.aggregateId)),
    takeUntilDestroyed(destroy),
  ).subscribe(refresh);
  effect(() => {
    if (realtime.initialSynchronizationSettled()) refresh();
  });
}

interface InboxEntry {
  submission: FeedbackRow;
  readerState: { isRead: boolean; observedRevision: number };
  reportStatus?: 'none' | 'open' | 'resolved';
}
interface InboxPage extends Page<InboxEntry> { unreadCount: number; unresolvedReportCount?: number }

@Component({
  imports: [RouterLink, DatePipe],
  styleUrl: './aq-page.css',
  template: `
    <section lang="ja" class="aq-page">
      <a routerLink="/aq/feedback">発表一覧へ</a>
      <h1>レビュー受信箱</h1>
      <div class="aq-actions">
        <label>既読状態
          <select (change)="readFilter.set($any($event.target).value); load()">
            <option value="">すべて</option>
            <option value="unread">未読</option>
            <option value="read">既読</option>
          </select>
        </label>
        <label>通報
          <select (change)="reportFilter.set($any($event.target).value); load()">
            <option value="">すべて</option>
            <option value="open">未解決</option>
            <option value="resolved">解決済み</option>
          </select>
        </label>
        <button type="button" [disabled]="loading()" (click)="load()">更新</button>
      </div>
      @if (loading()) { <p role="status">読み込み中…</p> }
      @if (failed()) { <p role="alert">受信箱の操作を完了できませんでした。</p> }
      @if (inbox(); as data) {
        <p>未読 {{ data.unreadCount }}件 @if (data.unresolvedReportCount !== undefined) { · 未解決の通報 {{ data.unresolvedReportCount }}件 }</p>
        <ul class="aq-list">
          @for (entry of data.items; track entry.submission.id) {
            <li>
              <a [routerLink]="['/aq/feedback', entry.submission.presentationId]">発表のフィードバック</a>
              <p><time [attr.datetime]="entry.submission.submittedAt">{{ entry.submission.submittedAt | date:'medium' }}</time> · {{ entry.readerState.isRead ? '既読' : '未読' }}</p>
              @for (answer of entry.submission.answers; track answer.id) {
                @if (answer.visibility === 'visible') { <p>{{ answer.value }}</p> }
              }
              <button type="button" [disabled]="busy()" (click)="toggle(entry)">{{ entry.readerState.isRead ? '未読にする' : '既読にする' }}</button>
            </li>
          } @empty { <li>表示できる回答はありません。</li> }
        </ul>
        @if (data.nextCursor) {
          <button type="button" [disabled]="loading()" (click)="load(data.nextCursor)">さらに表示</button>
        }
      }
    </section>
  `,
})
export class AqFeedbackInbox {
  private readonly api = inject(AqApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly inbox = signal<InboxPage | null>(null);
  protected readonly readFilter = signal('');
  protected readonly reportFilter = signal('');
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  private generation = 0;

  constructor() {
    bindFeedbackRefresh(() => this.load());
  }

  protected load(cursor?: string): void {
    const generation = ++this.generation;
    this.loading.set(true);
    this.failed.set(false);
    if (!cursor) this.inbox.set(null);
    const params: Record<string, string> = {};
    if (cursor) params['cursor'] = cursor;
    if (this.readFilter()) params['isRead'] = this.readFilter() === 'read' ? 'true' : 'false';
    if (this.reportFilter()) params['reportStatus'] = this.reportFilter();
    this.api.read<InboxPage>('feedback/inbox', params).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: data => {
        if (generation !== this.generation) return;
        this.inbox.set(cursor ? { ...data, items: [...this.inbox()?.items ?? [], ...data.items] } : data);
        this.loading.set(false);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.inbox.set(null);
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  protected toggle(entry: InboxEntry): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.command('PATCH', 'feedback/submissions/' + aqSegment(entry.submission.id) + '/read-state', {
      isRead: !entry.readerState.isRead,
      observedRevision: entry.readerState.observedRevision,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: () => {
        this.busy.set(false);
        this.inbox.set(null);
        this.failed.set(true);
      },
    });
  }
}
