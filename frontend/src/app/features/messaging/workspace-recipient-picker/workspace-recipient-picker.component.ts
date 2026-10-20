import { ChangeDetectionStrategy, Component, DestroyRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideX } from '@lucide/angular';
import { Subscription } from 'rxjs';
import { injectMessagingI18n } from '../../../core/i18n/providers/messaging-i18n';
import { GroupWorkspaceMemberDto, MessagingApi } from '../messaging.api';

export interface WorkspaceRecipientOption {
  readonly userId: string;
  readonly displayName: string;
}

@Component({
  selector: 'app-workspace-recipient-picker',
  standalone: true,
  imports: [FormsModule, LucideX],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="workspace-recipients" [attr.aria-busy]="status() === 'loading'">
      <p class="workspace-recipients__help">{{ i18n.translate('messaging.group.workspaceOnly') }}</p>
      <label>
        <span>{{ i18n.translate('messaging.group.findMember') }}</span>
        <input type="search" [ngModel]="query()" (ngModelChange)="query.set($event)"
          [disabled]="disabled || status() !== 'ready'" autocomplete="off" data-testid="group-recipient-search" />
      </label>
      @if (selected().length) {
        <ul class="workspace-recipients__selected" [attr.aria-label]="i18n.translate('messaging.recipient.selected')">
          @for (person of selected(); track person.userId) {
            <li><span>{{ person.displayName }}</span><button type="button" [disabled]="disabled"
              [attr.aria-label]="i18n.translate('messaging.recipient.remove', { name: person.displayName })"
              (click)="toggle(person)"><svg lucideX aria-hidden="true"></svg></button></li>
          }
        </ul>
      }
      @if (status() === 'loading') {
        <p role="status">{{ i18n.translate('messaging.recipient.loading') }}</p>
      } @else if (status() === 'error') {
        <p role="alert">{{ i18n.translate('messaging.group.members.unavailable') }}</p>
        <button type="button" [disabled]="disabled" (click)="loadMembers()">{{ i18n.translate('common.retry') }}</button>
      } @else if (matches().length === 0) {
        <p role="status">{{ i18n.translate('messaging.recipient.empty') }}</p>
      } @else {
        <div class="workspace-recipients__options" role="group" tabindex="0" [attr.aria-label]="i18n.translate('messaging.recipient.options')">
          @for (person of matches(); track person.userId) {
            <button type="button" [attr.aria-pressed]="isSelected(person.userId)" [disabled]="disabled"
              data-testid="group-recipient-option" (click)="toggle(person)">{{ person.displayName }}</button>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './workspace-recipient-picker.component.scss'
})
export class WorkspaceRecipientPickerComponent implements OnChanges {
  readonly i18n = injectMessagingI18n();
  private readonly api = inject(MessagingApi);
  @Input({ required: true }) workspaceId = '';
  @Input() excludedUserIds: readonly string[] = [];
  @Input() disabled = false;
  @Input() multiple = true;
  @Input() resetKey = 0;
  @Output() recipientsChanged = new EventEmitter<readonly WorkspaceRecipientOption[]>();
  readonly query = signal('');
  readonly selected = signal<readonly WorkspaceRecipientOption[]>([]);
  readonly status = signal<'loading' | 'ready' | 'error'>('loading');
  private readonly members = signal<readonly WorkspaceRecipientOption[]>([]);
  private readonly excluded = signal<readonly string[]>([]);
  readonly matches = computed(() => {
    const query = this.query().trim().toLocaleLowerCase();
    return this.members().filter((person) => !this.excluded().includes(person.userId) &&
      (!query || person.displayName.toLocaleLowerCase().includes(query)));
  });
  private request?: Subscription;
  private generation = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.generation++; this.request?.unsubscribe(); });
  }

  ngOnChanges(changes: SimpleChanges): void {
    this.excluded.set(this.excludedUserIds);
    if (changes['workspaceId'] || changes['resetKey']) { this.loadMembers(); }
    const allowed = this.selected().filter((person) => !this.excludedUserIds.includes(person.userId));
    if (allowed.length !== this.selected().length) { this.selected.set(allowed); this.recipientsChanged.emit(allowed); }
  }

  loadMembers(): void {
    this.request?.unsubscribe();
    const generation = ++this.generation;
    this.members.set([]);
    this.selected.set([]);
    this.query.set('');
    this.recipientsChanged.emit([]);
    this.status.set('loading');
    if (!this.workspaceId) { this.status.set('error'); return; }
    this.request = this.api.listGroupWorkspaceMembers(this.workspaceId).subscribe({
      next: (response) => {
        if (generation !== this.generation) { return; }
        if (!Array.isArray(response)) { this.status.set('error'); return; }
        this.members.set(mapActiveWorkspaceRecipients(response));
        this.status.set('ready');
      },
      error: () => {
        if (generation !== this.generation) { return; }
        this.members.set([]); this.selected.set([]); this.recipientsChanged.emit([]); this.status.set('error');
      }
    });
  }

  isSelected(userId: string): boolean { return this.selected().some((person) => person.userId === userId); }

  toggle(person: WorkspaceRecipientOption): void {
    if (this.disabled || this.status() !== 'ready' ||
        (!this.isSelected(person.userId) && !this.matches().some((candidate) => candidate.userId === person.userId))) { return; }
    const selected = this.isSelected(person.userId) ? this.selected().filter((candidate) => candidate.userId !== person.userId)
      : this.multiple ? [...this.selected(), person] : [person];
    this.selected.set(selected);
    this.recipientsChanged.emit(selected);
  }
}

export function mapActiveWorkspaceRecipients(members: readonly GroupWorkspaceMemberDto[]): readonly WorkspaceRecipientOption[] {
  const seen = new Set<string>();
  return members.flatMap((member) => {
    if ((member.status !== 'Active' && member.status !== 1) || typeof member.userId !== 'string' || !member.userId.trim() ||
        typeof member.displayName !== 'string' || !member.displayName.trim() || seen.has(member.userId)) { return []; }
    seen.add(member.userId);
    return [{ userId: member.userId, displayName: member.displayName }];
  });
}
