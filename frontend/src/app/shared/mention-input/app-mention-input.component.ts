import { I18nService, TranslationKey } from '../../core/i18n/i18n.service';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, EventEmitter, Input, OnChanges, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, debounceTime, switchMap } from 'rxjs/operators';
import { Observable, of, Subject } from 'rxjs';

import { TaskMentionCandidateDto } from '../../features/projects/projects.api';

interface MentionToken { readonly userId: string; readonly displayName: string; readonly start: number; readonly end: number; }
interface MentionRange { readonly start: number; readonly end: number; readonly selectionStart: number; readonly selectionEnd: number; readonly taskId: string; readonly generation: number; }
type SearchResult = { readonly kind: 'ready'; readonly candidates: readonly TaskMentionCandidateDto[]; readonly range: MentionRange } | { readonly kind: 'error'; readonly status: number; readonly range: MentionRange };

let mentionInputInstance = 0;

/** Friendly display text is reconciled to canonical @{GUID} tokens at explicit token spans. */
@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-mention-input', standalone: true,
  template: `<label [for]="textareaId">{{ i18n.translate('mention.comment') }}</label>
  <textarea #editor [id]="textareaId" [value]="displayValue()" [attr.maxlength]="maxLength ?? null" (input)="onInput($event)" (keydown)="onKeydown($event)" (click)="onCaretChange($event)" (keyup)="onCaretChange($event)"
    [attr.aria-describedby]="statusId" aria-autocomplete="list" [attr.aria-expanded]="candidates().length > 0"
    [attr.aria-controls]="listboxId" [attr.aria-activedescendant]="activeOptionId()"></textarea>
  @if (candidates().length) { <ul [id]="listboxId" role="listbox" [attr.aria-label]="i18n.translate('mention.candidates')">
    @for (candidate of candidates(); track candidate.userId; let i = $index) { <li [id]="optionId(i)" role="option" [attr.aria-selected]="i === selectedIndex()"><button type="button" (click)="select(candidate)">@{{ candidate.displayName }}</button></li> }
  </ul> }
  <p [id]="statusId" role="status" aria-live="polite">{{ status() }}</p>`,
  styles: [':host{display:grid;gap:.5rem}textarea{min-height:5rem}ul{margin:0;padding:0;list-style:none}button{width:100%;text-align:left}'],
})
export class AppMentionInputComponent implements OnChanges {
  readonly i18n = inject(I18nService);
  @Input({ required: true }) taskId = '';
  @Input() value = '';
  @Input() inputId?: string;
  @Input() maxLength: number | null = 12000;
  @Input() knownMentions: readonly { userId: string; displayName: string }[] = [];
  @Output() valueChange = new EventEmitter<string>();
  @ViewChild('editor') private editor?: ElementRef<HTMLTextAreaElement>;
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly query = new Subject<MentionRange | null>();
  private readonly instance = ++mentionInputInstance;
  private tokens: readonly MentionToken[] = [];
  private lastCanonical = '';
  private lastTaskId = '';
  private taskGeneration = 0;
  private queryRange: MentionRange | null = null;
  readonly candidates = signal<readonly { userId: string; displayName: string }[]>([]);
  readonly selectedIndex = signal(0);
  private readonly statusState = signal<{ key: TranslationKey; parameters?: Readonly<Record<string, string | number>> } | null>(null);
  readonly status = computed(() => { const status = this.statusState(); return status ? this.i18n.translate(status.key, status.parameters) : ''; });
  readonly displayValue = signal('');

  get textareaId(): string { return this.inputId || `mention-input-${this.instance}`; }
  get listboxId(): string { return `${this.textareaId}-candidates`; }
  get statusId(): string { return `${this.textareaId}-status`; }

  constructor() {
    this.query.pipe(debounceTime(200), switchMap(range => this.search(range)), takeUntilDestroyed(this.destroyRef)).subscribe(result => {
      if (!result || !this.isRangeCurrent(result.range)) {return;}
      if (result.kind === 'error') {
        this.candidates.set([]);
        this.statusState.set({ key: result.status === 403 ? 'mention.denied' : result.status === 429 ? 'mention.rateLimited' : 'mention.error' });
        return;
      }
      const candidates = result.candidates.map(candidate => ({ userId: typeof candidate.userId === 'string' ? candidate.userId : '', displayName: typeof candidate.displayName === 'string' ? candidate.displayName : '' })).filter(candidate => candidate.userId && candidate.displayName);
      this.candidates.set(candidates); this.selectedIndex.set(0);
      this.statusState.set(candidates.length ? { key: 'mention.count', parameters: { count: candidates.length } } : { key: 'mention.empty' });
    });
  }

  ngOnChanges(): void {
    if (this.value === this.lastCanonical && this.taskId === this.lastTaskId) {return;}
    this.taskGeneration++;
    this.closeCandidates('');
    const names = new Map(this.knownMentions.map(mention => [mention.userId, mention.displayName]));
    const tokens: MentionToken[] = [];
    const pattern = /@\{(?<id>[0-9a-fA-F-]{36})\}/g;
    let display = ''; let canonicalCursor = 0; let match: RegExpExecArray | null;
    while ((match = pattern.exec(this.value)) !== null) {
      display += this.value.slice(canonicalCursor, match.index);
      const userId = match.groups?.['id'] ?? ''; const displayName = names.get(userId) ?? 'Mention'; const text = `@${displayName}`;
      tokens.push({ userId, displayName, start: display.length, end: display.length + text.length });
      display += text; canonicalCursor = match.index + match[0].length;
    }
    this.tokens = tokens; this.displayValue.set(display + this.value.slice(canonicalCursor)); this.lastCanonical = this.value; this.lastTaskId = this.taskId;
  }

  onInput(event: Event): void {
    const textarea = event.target as HTMLTextAreaElement; const next = textarea.value;
    this.tokens = this.reconcileTokenSpans(this.displayValue(), next); this.displayValue.set(next); this.emitCanonical(); this.openQueryFor(textarea);
  }

  onCaretChange(event: Event): void {
    const textarea = event.target as HTMLTextAreaElement;
    if (this.queryRange && (textarea.selectionStart !== this.queryRange.selectionStart || textarea.selectionEnd !== this.queryRange.selectionEnd)) {this.closeCandidates('mention.caretMoved');}
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.candidates().length) {return;}
    if (event.key === 'ArrowDown') { event.preventDefault(); this.selectedIndex.update(index => Math.min(index + 1, this.candidates().length - 1)); }
    else if (event.key === 'ArrowUp') { event.preventDefault(); this.selectedIndex.update(index => Math.max(index - 1, 0)); }
    else if (event.key === 'Enter') { event.preventDefault(); this.select(this.candidates()[this.selectedIndex()]); }
    else if (event.key === 'Escape') { event.preventDefault(); this.closeCandidates('mention.closed'); }
  }

  select(candidate: { userId: string; displayName: string }): void {
    const range = this.queryRange; const textarea = this.editor?.nativeElement;
    if (!candidate || !range || !textarea || !this.isRangeCurrent(range)) {return;}
    const value = this.displayValue(); const replacement = `@${candidate.displayName}`; const next = `${value.slice(0, range.start)}${replacement}${value.slice(range.end)}`;
    const delta = replacement.length - (range.end - range.start);
    this.tokens = [...this.tokens.filter(token => token.end <= range.start || token.start >= range.end).map(token => token.start >= range.end ? { ...token, start: token.start + delta, end: token.end + delta } : token), { userId: candidate.userId, displayName: candidate.displayName, start: range.start, end: range.start + replacement.length }].sort((a, b) => a.start - b.start);
    this.displayValue.set(next); this.emitCanonical(); this.closeCandidates('mention.selected', { name: candidate.displayName });
    const caret = range.start + replacement.length;
    queueMicrotask(() => { textarea.focus(); textarea.setSelectionRange(caret, caret); });
  }

  activeOptionId(): string | null { return this.candidates().length ? this.optionId(this.selectedIndex()) : null; }
  optionId(index: number): string { return `${this.textareaId}-mention-${index}`; }

  private search(range: MentionRange | null): Observable<SearchResult | null> {
    if (!range) {return of(null);}
    this.statusState.set({ key: 'mention.loading' });
    const query = encodeURIComponent(this.displayValue().slice(range.start + 1, range.end).trim());
    return this.http.get<readonly TaskMentionCandidateDto[]>(`/api/tasks/${range.taskId}/mention-candidates?query=${query}&limit=10`, { withCredentials: true }).pipe(
      switchMap(candidates => of({ kind: 'ready', candidates, range } as const)),
      catchError((error: { status?: number }) => of({ kind: 'error', status: error.status ?? 0, range } as const))
    );
  }

  private openQueryFor(textarea: HTMLTextAreaElement): void {
    const start = textarea.selectionStart; const end = textarea.selectionEnd;
    if (start !== end) { this.closeCandidates(''); return; }
    const match = /@([\p{L}\p{N} .'-]*)$/u.exec(textarea.value.slice(0, end));
    if (match?.index === undefined) { this.closeCandidates(''); return; }
    const range: MentionRange = { start: match.index, end, selectionStart: start, selectionEnd: end, taskId: this.taskId, generation: this.taskGeneration };
    this.queryRange = range; this.query.next(range);
  }

  private reconcileTokenSpans(previous: string, next: string): readonly MentionToken[] {
    let prefix = 0; while (prefix < previous.length && prefix < next.length && previous[prefix] === next[prefix]) {prefix++;}
    let suffix = 0; while (suffix < previous.length - prefix && suffix < next.length - prefix && previous[previous.length - 1 - suffix] === next[next.length - 1 - suffix]) {suffix++;}
    const oldChangedEnd = previous.length - suffix; const shift = next.length - previous.length;
    return this.tokens.flatMap(token => {
      if (token.end <= prefix && next.slice(token.start, token.end) === previous.slice(token.start, token.end)) {return [token];}
      if (token.start >= oldChangedEnd) { const shifted = { ...token, start: token.start + shift, end: token.end + shift }; return next.slice(shifted.start, shifted.end) === previous.slice(token.start, token.end) ? [shifted] : []; }
      return [];
    });
  }

  private emitCanonical(): void { const display = this.displayValue(); let cursor = 0; let canonical = ''; for (const token of this.tokens) { canonical += `${display.slice(cursor, token.start)  }@{${token.userId}}`; cursor = token.end; } canonical += display.slice(cursor); this.lastCanonical = canonical; this.valueChange.emit(canonical); }
  private isRangeCurrent(range: MentionRange): boolean { const textarea = this.editor?.nativeElement; return range.taskId === this.taskId && range.generation === this.taskGeneration && this.queryRange === range && !!textarea && textarea.selectionStart === range.selectionStart && textarea.selectionEnd === range.selectionEnd; }
  private closeCandidates(key: TranslationKey | '', parameters?: Readonly<Record<string, string | number>>): void { this.queryRange = null; this.candidates.set([]); if (key) {this.statusState.set({ key, parameters });} }
}
