import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, forkJoin, map, Observable } from 'rxjs';
import { AqApi, Page, aqSegment } from './aq-api';
import { createCryptographicUuid } from '../../core/security/client-id';

interface EventSettings {
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
interface Campaign { id: string; enabled: boolean; published: boolean; versionNo: number }
interface Target { id: string; presentationId: string; versionNo: number; settings: { enabled: boolean; resultsStopped: boolean } }
interface Approval {
  invitation: { id: string; versionNo: number };
  presentationLabel: string;
  inviterLabel: string;
  recipientLabel: string;
}
interface Preview {
  previewRef: string;
  diagnostics: { row: number; code: string }[];
  proposedAssignments: { row: number; role: 'presenter' | 'reviewer'; personRef?: string; pendingRosterRef?: string }[];
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
      data: this.api.read<EventManagement>(root + '/feedback-settings'),
      presentations: this.api.read<Page<Presentation>>(root + '/presentations'),
      campaigns: this.api.read<Page<Campaign>>(root + '/survey-campaigns'),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        if (generation !== this.generation) return;
        this.data.set(result.data);
        this.presentations.set(result.presentations.items);
        this.campaigns.set(result.campaigns.items);
        this.busy.set(false);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.busy.set(false);
        this.failed.set(true);
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

  protected setEventSetting(name: keyof Pick<EventSettings, 'receptionStopped' | 'resultsStopped' | 'programPublished'>, checked: boolean): void {
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
    const generation = ++this.targetGeneration;
    this.campaignId.set(id);
    this.targetId.set('');
    this.targets.set([]);
    if (!id) return;
    this.api.read<Page<Target>>('survey-campaigns/' + aqSegment(id) + '/targets')
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: page => {
          if (generation === this.targetGeneration) this.targets.set(page.items);
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

  protected presentationTitle(id: string): string {
    return this.presentations().find(row => row.presentation.id === id)?.presentation.title ?? '発表';
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
