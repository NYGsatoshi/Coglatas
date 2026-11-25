import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';
import { AnswerValue, AqApi, PublicSurvey, Submission, SurveyField } from './aq-api';
import { AQ_PUBLIC_COPY } from './aq-public-copy';
import { createCryptographicUuid } from '../../core/security/client-id';

type SubmissionState = 'ready' | 'sending' | 'pending' | 'expired' | 'accepted' | 'rejected';
interface Attempt { key: string; body: Submission; startedAt: number; outcomeUnknown: boolean }
const REPLAY_DURATION_MS = 24 * 60 * 60 * 1000;

@Component({
  templateUrl: './aq-public-survey.html',
  styleUrl: './aq-page.css',
})
export class AqPublicSurvey {
  protected readonly copy = AQ_PUBLIC_COPY;
  protected readonly survey = signal<PublicSurvey | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly answers = signal<ReadonlyMap<string, AnswerValue>>(new Map());
  protected readonly displayName = signal('');
  protected readonly consent = signal(false);
  protected readonly state = signal<SubmissionState>('ready');
  protected readonly marked = signal(false);
  protected readonly invalidFields = signal<ReadonlySet<string>>(new Set());
  protected readonly answeredQuestions = computed(() => {
    const values = this.answers();
    return (this.survey()?.fields ?? []).filter(field => this.hasAnswer(values.get(field.fieldKey))).length;
  });
  private readonly outcome = viewChild<ElementRef<HTMLElement>>('outcome');
  private readonly api = inject(AqApi);
  private readonly destroyRef = inject(DestroyRef);
  private code = '';
  private generation = 0;
  private attempt?: Attempt;

  constructor() {
    inject(ActivatedRoute).paramMap.pipe(
      map(params => params.get('code') ?? ''),
      distinctUntilChanged(),
      takeUntilDestroyed(),
    ).subscribe(code => {
      this.code = code;
      this.attempt = undefined;
      this.answers.set(new Map());
      this.displayName.set('');
      this.consent.set(false);
      this.invalidFields.set(new Set());
      this.state.set('ready');
      this.load();
    });
  }

  protected load(): void {
    const generation = ++this.generation;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.survey.set(null);
    this.api.form(this.code).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: survey => {
        if (generation !== this.generation) return;
        this.survey.set(survey);
        this.marked.set(this.readMarker(survey.localMarkerKey));
        this.loading.set(false);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.loading.set(false);
        this.loadFailed.set(true);
      },
    });
  }

  protected setAnswer(key: string, value: AnswerValue): void {
    this.answers.update(previous => new Map(previous).set(key, value));
    this.invalidFields.update(previous => {
      const updated = new Set(previous);
      updated.delete(key);
      return updated;
    });
  }

  protected chosen(key: string, option: string): boolean {
    const current = this.answers().get(key);
    return Array.isArray(current) && current.includes(option);
  }

  protected selectChoice(key: string, option: string, selected: boolean): void {
    const current = this.answers().get(key);
    const options = new Set(Array.isArray(current) ? current : []);
    if (selected) options.add(option);
    else options.delete(option);
    this.setAnswer(key, [...options]);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const survey = this.survey();
    if (!survey || survey.receptionState !== 'open' || this.marked() ||
        (this.state() !== 'ready' && this.state() !== 'rejected')) return;

    const values = this.answers();
    const invalid = survey.fields.filter(field => !this.validField(field, values.get(field.fieldKey)));
    this.invalidFields.set(new Set(invalid.map(field => field.fieldKey)));
    const name = survey.nameMode === 'anonymous' ? '' : this.displayName().trim();
    if (invalid.length || (survey.nameMode === 'requiredName' && !name)) return;

    const body: Submission = {
      versionRef: survey.versionRef,
      schemaDigest: survey.schemaDigest,
      answers: survey.fields.flatMap(field => {
        const value = values.get(field.fieldKey);
        return this.hasAnswer(value) ? [{ fieldKey: field.fieldKey, value }] : [];
      }),
      ...(name ? { name } : {}),
      ...(survey.nameMode !== 'anonymous' && survey.accountOptInAvailable && this.consent() ? { accountOptIn: true } : {}),
    };
    let key: string;
    try {
      key = createCryptographicUuid();
    } catch {
      this.showOutcome('rejected');
      return;
    }
    this.attempt = { body, key, startedAt: Date.now(), outcomeUnknown: false };
    this.deliver();
  }

  protected retry(): void {
    if (this.state() === 'pending') this.deliver();
  }

  private deliver(): void {
    const attempt = this.attempt;
    if (!attempt) return;
    if (Date.now() - attempt.startedAt >= REPLAY_DURATION_MS) {
      this.attempt = undefined;
      this.showOutcome('expired');
      return;
    }
    const generation = this.generation;
    this.state.set('sending');
    this.api.submit(this.code, attempt.body, attempt.key).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (generation !== this.generation) return;
        const marker = this.survey()?.localMarkerKey;
        if (marker) this.writeMarker(marker);
        this.attempt = undefined;
        this.answers.set(new Map());
        this.displayName.set('');
        this.consent.set(false);
        this.showOutcome('accepted');
      },
      error: (error: unknown) => {
        if (generation !== this.generation) return;
        if (!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500 ||
            error.error?.error?.code === 'ConcurrentModification' ||
            (attempt.outcomeUnknown && [401, 403, 404, 429].includes(error.status))) {
          attempt.outcomeUnknown = true;
          this.showOutcome('pending');
          return;
        }
        this.attempt = undefined;
        this.showOutcome('rejected');
      },
    });
  }

  private showOutcome(state: SubmissionState): void {
    this.state.set(state);
    setTimeout(() => this.outcome()?.nativeElement.focus());
  }

  private hasAnswer(value: AnswerValue | undefined): value is AnswerValue {
    if (typeof value === 'string') return value.trim().length > 0;
    if (Array.isArray(value)) return value.length > 0;
    if (typeof value === 'number') return Number.isFinite(value);
    return typeof value === 'boolean';
  }

  private validField(field: SurveyField, value: AnswerValue | undefined): boolean {
    if (!this.hasAnswer(value)) return !field.required;
    const constraints = field.constraints;
    if (Array.isArray(value)) {
      return value.length >= (constraints?.minChoices ?? 0) &&
        value.length <= (constraints?.maxChoices ?? Number.MAX_SAFE_INTEGER);
    }
    return true;
  }

  private readMarker(key: string): boolean {
    try {
      return localStorage.getItem('coglatas.aq.submitted.' + key) === '1';
    } catch {
      return false;
    }
  }

  private writeMarker(key: string): void {
    try {
      localStorage.setItem('coglatas.aq.submitted.' + key, '1');
    } catch {
      // Confirmation remains authoritative when browser storage is unavailable.
    }
  }
}
