import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, distinctUntilChanged, forkJoin, map, Observable, of } from 'rxjs';
import { AqApi, ManageableEventCandidate, Page, SurveyField, aqSegment } from './aq-api';
import { createCryptographicUuid } from '../../core/security/client-id';

interface EventSettings {
  nameMode: 'anonymous' | 'optionalName' | 'requiredName';
  accountBindingAllowed: boolean;
  publicFields: string[];
  programTargets: { presentationId: string; targetId: string }[];
  invitationPolicy?: { enabled: boolean; decisionWindowSeconds: number; maximumActiveSeconds: number };
  receptionStopped: boolean;
  resultsStopped: boolean;
  programPublished: boolean;
  timeZone: string;
}
interface EventManagement { settings: EventSettings; versionNo: number; programCodeState: string }
interface Presentation {
  presentation: { id: string; title: string };
  versionNo: number;
}
interface Campaign { id: string; formDefinitionId: string; enabled: boolean; published: boolean; versionNo: number }
interface Target { id: string; presentationId: string; versionNo: number; settings: { enabled: boolean; resultsStopped: boolean } }
interface FormSummary { id: string; title: string; versionNo: number }
interface QuestionVersion { id: string; versionNo: number; state: string }
interface Approval {
  invitation: { id: string; versionNo: number };
  presentationLabel: string;
  inviterLabel: string;
  recipientLabel: string;
}
interface Preview {
  previewRef: string;
  diagnostics: { row: number; code: string }[];
  proposedAssignments: { row: number; presentationId: string; role: 'presenter' | 'reviewer'; personRef?: string; pendingRosterRef?: string }[];
  expectedSourceVersions?: { sourceRef: string; versionNo: number }[];
}

@Component({
  imports: [RouterLink],
  styleUrl: './aq-page.css',
  templateUrl: './aq-management.html',
})
export class AqManagement {
  private readonly api = inject(AqApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly data = signal<EventManagement | null>(null);
  protected readonly presentations = signal<Presentation[]>([]);
  protected readonly campaigns = signal<Campaign[]>([]);
  protected readonly targets = signal<Target[]>([]);
  protected readonly approvals = signal<Approval[]>([]);
  protected readonly preview = signal<Preview | null>(null);
  protected readonly programCode = signal('');
  protected readonly campaignId = signal('');
  protected readonly targetId = signal('');
  protected readonly busy = signal(false);
  protected readonly failed = signal(false);
  protected readonly outcome = signal('');
  protected readonly title = signal('');
  protected readonly venue = signal('');
  protected readonly start = signal('');
  protected readonly end = signal('');
  protected readonly projectId = signal('');
  protected readonly formTitle = signal('');
  protected readonly forms = signal<FormSummary[]>([]);
  protected readonly formId = signal('');
  protected readonly targetPresentation = signal('');
  protected readonly commonVersion = signal('');
  protected readonly versions = signal<QuestionVersion[]>([]);
  protected readonly fields = signal<SurveyField[]>([]);
  protected readonly editingKind = signal<'common' | 'target'>('common');
  protected readonly previousFieldKeys = signal<string[]>([]);
  protected readonly questionLabel = signal('');
  protected readonly questionKind = signal<SurveyField['type']>('text');
  protected readonly options = signal('');
  protected readonly questionRequired = signal(false);
  protected readonly versionReason = signal('');
  protected readonly timeZone = signal(Intl.DateTimeFormat().resolvedOptions().timeZone);
  protected readonly publicEntry = signal('');
  protected readonly invitesEnabled = signal(false);
  protected readonly decisionDays = signal('7');
  protected readonly durationDays = signal('30');
  private id = '';
  private generation = 0;
  private targetGeneration = 0;
  private previewKey = '';

  constructor() {
    inject(ActivatedRoute).paramMap.pipe(
      map(params => params.get('eventId') ?? ''),
      distinctUntilChanged(),
      takeUntilDestroyed(),
    ).subscribe(id => {
      this.id = id;
      ++this.targetGeneration;
      this.projectId.set('');
      this.formId.set('');
      this.versions.set([]);
      this.fields.set([]);
      this.publicEntry.set('');
      this.preview.set(null);
      this.programCode.set('');
      this.campaignId.set('');
      this.targetId.set('');
      this.load();
    });
  }

  protected load(): void {
    const generation = ++this.generation;
    const root = this.eventPath();
    this.busy.set(true);
    this.failed.set(false);
    this.data.set(null);
    this.presentations.set([]);
    this.campaigns.set([]);
    this.approvals.set([]);
    forkJoin({
      data: this.api.read<EventManagement>(root + '/feedback-settings').pipe(
        catchError((error: { httpStatus?: number }) => {
          if (error.httpStatus !== 404) throw error;
          return of(null);
        }),
      ),
      presentations: this.api.read<Page<Presentation>>(root + '/presentations'),
      campaigns: this.api.read<Page<Campaign>>(root + '/survey-campaigns'),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        if (generation !== this.generation) return;
        this.data.set(result.data);
        const invitations = result.data?.settings.invitationPolicy;
        this.invitesEnabled.set(invitations?.enabled ?? false);
        this.decisionDays.set(invitations ? String(invitations.decisionWindowSeconds / 86400) : '7');
        this.durationDays.set(invitations ? String(invitations.maximumActiveSeconds / 86400) : '30');
        this.presentations.set(result.presentations.items);
        this.campaigns.set(result.campaigns.items);
        if (this.campaignId()) this.reloadTargets(this.campaignId());
        this.busy.set(false);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.busy.set(false);
        this.failed.set(true);
      },
    });
    this.api.eventScope(this.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: scope => {
        if (generation === this.generation) this.projectId.set(scope.projectId ?? '');
      },
      error: () => {
        if (generation === this.generation) this.projectId.set('');
      },
    });
    this.api.read<Page<FormSummary>>('forms', { eventId: this.id }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => {
        if (generation === this.generation) this.forms.set(page.items);
      },
      error: () => {
        if (generation === this.generation) this.forms.set([]);
      },
    });
    this.api.read<Page<Approval>>(root + '/feedback-invitations/approval-queue')
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: page => {
          if (generation === this.generation) this.approvals.set(page.items);
        },
        error: () => {
          if (generation === this.generation) this.approvals.set([]);
        },
      });
  }

  protected initializeReception(event: Event): void {
    event.preventDefault();
    const start = new Date(this.start());
    const end = new Date(this.end());
    const zone = this.timeZone().trim();
    if (!Number.isFinite(start.getTime()) || !(start < end) || !zone) {
      this.failed.set(true);
      return;
    }
    this.act(this.api.command('PATCH', this.eventPath() + '/feedback-settings', {
      expectedVersionNo: 1, startAt: start.toISOString(), endAt: end.toISOString(),
      timeZone: zone, publicFields: ['title', 'time', 'venue', 'timeZone'],
    }), 'アンケートの受付を設定しました。', () => {
      this.start.set('');
      this.end.set('');
    });
  }

  protected saveInvitationPolicy(event: Event): void {
    event.preventDefault();
    const current = this.data();
    const decisionWindowSeconds = Number(this.decisionDays()) * 86400;
    const maximumActiveSeconds = Number(this.durationDays()) * 86400;
    if (!current || !Number.isSafeInteger(decisionWindowSeconds) || decisionWindowSeconds <= 0 ||
        !Number.isSafeInteger(maximumActiveSeconds) || maximumActiveSeconds <= 0) {
      this.failed.set(true);
      return;
    }
    this.act(this.api.command('PATCH', this.eventPath() + '/feedback-settings', {
      expectedVersionNo: current.versionNo,
      invitationPolicy: { enabled: this.invitesEnabled(), decisionWindowSeconds, maximumActiveSeconds },
    }), '閲覧招待の設定を更新しました。');
  }

  protected setNameMode(value: EventSettings['nameMode']): void {
    const settings = this.data();
    if (!settings) return;
    this.act(this.api.command('PATCH', this.eventPath() + '/feedback-settings', {
      expectedVersionNo: settings.versionNo, nameMode: value,
      ...(value === 'anonymous' ? { accountBindingAllowed: false } : {}),
    }), '回答者名の設定を更新しました。');
  }

  protected setEventSetting(name: keyof Pick<EventSettings, 'receptionStopped' | 'resultsStopped' | 'programPublished' | 'accountBindingAllowed'>, checked: boolean): void {
    const settings = this.data();
    if (!settings) return;
    this.act(this.api.command('PATCH', this.eventPath() + '/feedback-settings', {
      expectedVersionNo: settings.versionNo,
      [name]: checked,
    }), '設定を更新しました。');
  }

  protected createPresentation(event: Event): void {
    event.preventDefault();
    const title = this.title().trim();
    const venue = this.venue().trim();
    const start = new Date(this.start());
    const end = new Date(this.end());
    if (!title || !Number.isFinite(start.getTime()) || !(start < end)) {
      this.failed.set(true);
      return;
    }
    this.act(this.api.command('POST', this.eventPath() + '/presentations', {
      schedule: { title, startAt: start.toISOString(), endAt: end.toISOString(), ...(venue ? { venue } : {}) },
    }), '発表を追加しました。', () => {
      this.title.set('');
      this.venue.set('');
      this.start.set('');
      this.end.set('');
    });
  }

  protected rotateProgram(): void {
    const current = this.data();
    if (!current) return;
    this.act(this.api.command<{ entryRef: string }>('POST', this.eventPath() + '/program-code/rotate', {
      expectedVersionNo: current.versionNo,
    }), '一覧リンクを発行しました。', result => this.programCode.set(result.entryRef));
  }

  protected selectCampaign(id: string): void {
    this.campaignId.set(id);
    this.targetId.set('');
    this.publicEntry.set('');
    this.targetPresentation.set('');
    this.commonVersion.set('');
    const campaign = this.campaigns().find(item => item.id === id);
    if (campaign) this.selectForm(campaign.formDefinitionId);
    this.reloadTargets(id);
  }

  private reloadTargets(id: string): void {
    const generation = ++this.targetGeneration;
    this.targets.set([]);
    if (!id) return;
    this.api.read<Page<Target>>('survey-campaigns/' + aqSegment(id) + '/targets')
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: page => {
          if (generation !== this.targetGeneration) return;
          this.targets.set(page.items);
          if (!page.items.some(target => target.id === this.targetId())) this.targetId.set('');
        },
        error: () => {
          if (generation === this.targetGeneration) this.failed.set(true);
        },
      });
  }

  protected download(format: 'pdf' | 'png' | 'svg'): void {
    const campaign = this.campaignId();
    const targetId = this.targetId();
    if (!campaign || (format !== 'pdf' && !targetId)) return;
    const params = format === 'pdf' ? { format } : { format, targetId };
    this.act(this.api.download('survey-campaigns/' + aqSegment(campaign) + '/qr-export', params),
      'QR資料を保存しました。', blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'presentation-qr.' + format;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      }, false);
  }

  protected approve(approval: Approval): void {
    this.act(this.api.command('POST', 'feedback-invitations/' + aqSegment(approval.invitation.id) + '/approve', {
      expectedVersionNo: approval.invitation.versionNo,
    }), '閲覧招待を承認しました。');
  }

  protected upload(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const body = new FormData();
    body.append('file', file);
    this.preview.set(null);
    this.act(this.api.command<Preview>('POST', this.eventPath() + '/presentations/import-preview', body),
      'プレビューを作成しました。', result => {
        this.previewKey = createCryptographicUuid();
        this.preview.set(result);
      }, false);
  }

  protected commitImport(): void {
    const preview = this.preview();
    if (!preview?.expectedSourceVersions?.length || preview.diagnostics.length || !preview.proposedAssignments.length) return;
    const confirmedAssignments = preview.proposedAssignments.map(row => row.personRef
      ? { row: row.row, assignment: { role: row.role, personRef: row.personRef } }
      : { row: row.row, pendingPresenter: { pendingRosterRef: row.pendingRosterRef } });
    this.act(this.api.command('POST', this.eventPath() + '/presentations/import-commit', {
      previewRef: preview.previewRef,
      confirmedAssignments,
      expectedSourceVersions: preview.expectedSourceVersions,
    }, this.previewKey), '名簿を取り込みました。', () => this.preview.set(null));
  }

  protected createForm(event: Event): void {
    event.preventDefault();
    const title = this.formTitle().trim();
    const scopeId = this.projectId();
    if (!title || !scopeId) return;
    this.act(this.api.command<{ id: string }>('POST', 'forms', {
      owner: { scopeType: 'project', scopeId }, purpose: 'survey', title,
    }), '共通フォームを追加しました。', created => {
      this.formTitle.set('');
      this.selectForm(created.id);
    });
  }

  protected selectForm(id: string): void {
    this.formId.set(id);
    this.versions.set([]);
    this.fields.set([]);
    this.previousFieldKeys.set([]);
    this.editingKind.set('common');
    if (!id) return;
    this.api.read<Page<QuestionVersion>>('forms/' + aqSegment(id) + '/versions', { pageSize: '100' })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: page => {
          if (this.formId() === id) this.versions.set(page.items);
        },
        error: () => {
          if (this.formId() === id) this.failed.set(true);
        },
      });
  }

  protected loadVersion(id: string): void {
    const form = this.formId();
    if (!form || !id) return;
    this.act(this.api.read<{ fields: SurveyField[] }>('forms/' + aqSegment(form) + '/versions/' + aqSegment(id)),
      '公開版を読み込みました。', version => {
        this.editingKind.set('common');
        this.fields.set(version.fields);
        this.previousFieldKeys.set(version.fields.map(field => field.fieldKey));
      }, false);
  }

  protected addField(event: Event): void {
    event.preventDefault();
    const label = this.questionLabel().trim();
    const type = this.questionKind();
    const options = this.options().split('\n').map(value => value.trim()).filter(Boolean);
    const choice = type === 'singleChoice' || type === 'multipleChoice';
    if (!label || (choice && options.length < 2)) {
      this.failed.set(true);
      return;
    }
    this.fields.update(fields => [...fields, {
      fieldKey: createCryptographicUuid(), label, type, required: this.questionRequired(),
      ...(choice ? { options: options.map(value => ({ optionKey: createCryptographicUuid(), label: value })) } : {}),
      ...(type === 'scale' ? { constraints: { minimum: 1, maximum: 5, step: 1 } } : {}),
    }]);
    this.questionLabel.set('');
    this.options.set('');
  }

  protected renameField(key: string, label: string): void {
    this.fields.update(items => items.map(item => item.fieldKey === key ? { ...item, label } : item));
  }

  protected removeField(key: string): void {
    this.fields.update(items => items.filter(item => item.fieldKey !== key));
  }

  protected editQuestions(kind: 'common' | 'target'): void {
    this.editingKind.set(kind);
    this.fields.set([]);
    this.previousFieldKeys.set([]);
    this.versionReason.set('');
    if (kind !== 'target' || !this.targetId()) return;
    const target = this.targetId();
    const path = 'survey-targets/' + aqSegment(target) + '/question-set-versions';
    this.api.read<Page<QuestionVersion>>(path, { pageSize: '100' }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => {
        if (this.editingKind() !== 'target' || this.targetId() !== target) return;
        const latest = [...page.items].sort((left, right) => right.versionNo - left.versionNo)[0];
        if (!latest) return;
        this.act(this.api.read<{ fields: SurveyField[] }>(path + '/' + aqSegment(latest.id)),
          '発表の設問を読み込みました。', version => {
            if (this.editingKind() === 'target' && this.targetId() === target) {
              this.fields.set(version.fields);
              this.previousFieldKeys.set(version.fields.map(field => field.fieldKey));
            }
          }, false);
      },
      error: () => this.failed.set(true),
    });
  }

  protected publishQuestions(kind: 'common' | 'target'): void {
    if (kind !== this.editingKind()) return;
    const form = this.forms().find(item => item.id === this.formId());
    const target = this.targets().find(item => item.id === this.targetId());
    const version = kind === 'common' ? form?.versionNo : target?.versionNo;
    if (!version || this.fields().some(field => !field.label.trim())) return;
    const path = kind === 'common' ? 'forms/' + aqSegment(form!.id) + '/versions'
      : 'survey-targets/' + aqSegment(target!.id) + '/question-set-versions';
    const currentKeys = new Set(this.fields().map(field => field.fieldKey));
    const compatibility = this.previousFieldKeys().filter(key => currentKeys.has(key))
      .map(fieldKey => ({ previousFieldKey: fieldKey, fieldKey }));
    const reason = this.versionReason().trim();
    this.act(this.api.command('POST', path, {
      expectedVersionNo: version, action: 'publish', fields: this.fields(),
      ...(reason ? { changeReason: reason } : {}),
      ...(reason && compatibility.length ? { compatibility } : {}),
    }), '設問の版を公開しました。', () => {
      if (kind === 'common') this.selectForm(this.formId());
      this.versionReason.set('');
    });
  }

  protected createCampaign(): void {
    if (!this.formId()) return;
    this.act(this.api.command<Campaign>('POST', this.eventPath() + '/survey-campaigns', {
      formDefinitionId: this.formId(), policyRef: this.id,
    }), 'アンケートを作成しました。', result => this.campaignId.set(result.id));
  }

  protected publishCampaign(): void {
    const campaign = this.campaigns().find(item => item.id === this.campaignId());
    if (!campaign) return;
    this.act(this.api.command('PATCH', 'survey-campaigns/' + aqSegment(campaign.id), {
      expectedVersionNo: campaign.versionNo, enabled: true, published: true,
    }), 'アンケートを公開しました。');
  }

  protected createTarget(): void {
    if (!this.campaignId() || !this.targetPresentation() || !this.commonVersion()) return;
    this.act(this.api.command<Target>('POST', 'survey-campaigns/' + aqSegment(this.campaignId()) + '/targets', {
      presentationId: this.targetPresentation(), commonVersionId: this.commonVersion(),
      settings: { enabled: true, resultsStopped: false },
    }), '発表にアンケートを割り当てました。', result => this.targetId.set(result.id));
  }

  protected rotateTarget(): void {
    const target = this.targets().find(item => item.id === this.targetId());
    if (!target) return;
    this.act(this.api.command<{ entryRef: string }>('POST', 'survey-targets/' + aqSegment(target.id) + '/public-code/rotate', {
      expectedVersionNo: target.versionNo,
    }), '回答リンクを発行しました。', result => this.publicEntry.set(result.entryRef));
  }

  protected addProgramTarget(): void {
    const target = this.targets().find(item => item.id === this.targetId());
    const event = this.data();
    if (!target || !event) return;
    const programTargets = [
      ...event.settings.programTargets.filter(entry => entry.presentationId !== target.presentationId),
      { presentationId: target.presentationId, targetId: target.id },
    ];
    this.act(this.api.command('PATCH', this.eventPath() + '/feedback-settings', {
      expectedVersionNo: event.versionNo, programTargets,
    }), '発表一覧に回答リンクを追加しました。');
  }

  protected presentationTitle(id: string, fallback = '発表'): string {
    return this.presentations().find(row => row.presentation.id === id)?.presentation.title ?? fallback;
  }

  private eventPath(): string {
    return 'events/' + aqSegment(this.id);
  }

  private act<T>(action: Observable<T>, message: string, done?: (result: T) => void, reload = true): void {
    if (this.busy()) return;
    const generation = this.generation;
    this.busy.set(true);
    this.failed.set(false);
    this.outcome.set('');
    action.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        if (generation !== this.generation) return;
        this.busy.set(false);
        this.outcome.set(message);
        done?.(result);
        if (reload) this.load();
      },
      error: () => {
        if (generation !== this.generation) return;
        this.busy.set(false);
        this.failed.set(true);
      },
    });
  }
}


@Component({
  imports: [RouterLink, DatePipe],
  styleUrl: './aq-page.css',
  template: `
    <section class="aq-page" lang="ja">
      <a routerLink="/aq/feedback">発表一覧へ</a>
      <h1>運営する発表イベント</h1>
      @if (busy()) { <p role="status">読み込み中…</p> }
      @if (failed()) {
        <p role="alert">イベント一覧を読み込めませんでした。</p>
        <button type="button" (click)="load(1)">再読み込み</button>
      }
      <ul class="aq-list">
        @for (event of items(); track event.id) {
          <li>
            <a [routerLink]="['/aq/manage', event.id]">{{ event.title }}</a>
            <p><time [attr.datetime]="event.startsAt">{{ event.startsAt | date:'medium' }}</time></p>
          </li>
        } @empty {
          @if (!busy() && !failed()) { <li>このページに対象のイベントはありません。</li> }
        }
      </ul>
      @if (page() > 1) {
        <button type="button" [disabled]="busy()" (click)="load(page() - 1)">前のページ</button>
      }
      @if (next()) {
        <button type="button" [disabled]="busy()" (click)="load(page() + 1)">次のページ</button>
      }
    </section>
  `,
})
export class AqManagementIndex {
  private readonly api = inject(AqApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly items = signal<ManageableEventCandidate[]>([]);
  protected readonly busy = signal(false);
  protected readonly failed = signal(false);
  protected readonly page = signal(1);
  protected readonly next = signal(false);

  constructor() {
    this.load(1);
  }

  protected load(page: number): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.failed.set(false);
    this.api.events(page).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.items.set(result.items.filter(item => item.relatedScope.projectId));
        this.next.set(page * 25 < result.totalCount);
        this.page.set(page);
        this.busy.set(false);
      },
      error: () => {
        this.items.set([]);
        this.failed.set(true);
        this.busy.set(false);
      },
    });
  }
}
