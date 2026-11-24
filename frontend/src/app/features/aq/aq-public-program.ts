import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';
import { AqApi, Program } from './aq-api';
import { AQ_PUBLIC_COPY } from './aq-public-copy';

@Component({
  imports: [RouterLink],
  template: `
    <main lang="ja" class="aq-page">
      <h1>{{ program()?.title ?? copy.program }}</h1>
      @if (loading()) { <p role="status">{{ copy.loading }}</p> }
      @if (failed()) {
        <p role="alert">{{ copy.programError }}</p>
        <button type="button" (click)="load()">{{ copy.retryLoad }}</button>
      }
      @if (program(); as list) {
        <ul class="aq-list">
          @for (entry of list.presentations; track entry.surveyEntryRef) {
            <li>
              <a [routerLink]="['/aq/surveys', entry.surveyEntryRef]">{{ entry.title ?? copy.programEntry }}</a>
              @if (entry.startAt) { <p><time [attr.datetime]="entry.startAt">{{ displayTime(entry.startAt, list.timeZone) }}</time></p> }
              @if (entry.venue) { <p>{{ entry.venue }}</p> }
              @if (entry.presenterDisplayNames?.length) { <p>{{ entry.presenterDisplayNames?.join('、') }}</p> }
            </li>
          } @empty { <li>{{ copy.programEmpty }}</li> }
        </ul>
        @if (list.nextCursor) {
          <button type="button" [disabled]="loading()" (click)="load(list.nextCursor)">{{ copy.more }}</button>
        }
      }
    </main>
  `,
  styleUrl: './aq-page.css',
})
export class AqPublicProgram {
  protected readonly copy = AQ_PUBLIC_COPY;
  protected readonly program = signal<Program | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  private readonly api = inject(AqApi);
  private readonly destroyRef = inject(DestroyRef);
  private code = '';
  private request = 0;

  constructor() {
    inject(ActivatedRoute).paramMap.pipe(
      map(params => params.get('code') ?? ''),
      distinctUntilChanged(),
      takeUntilDestroyed(),
    ).subscribe(code => {
      this.code = code;
      this.program.set(null);
      this.load();
    });
  }

  protected displayTime(value: string, timeZone?: string): string {
    if (!timeZone) return value;
    try {
      return new Intl.DateTimeFormat('ja', {
        dateStyle: 'short', timeStyle: 'short', timeZone,
      }).format(new Date(value)) + ' (' + timeZone + ')';
    } catch {
      return value;
    }
  }

  protected load(cursor?: string): void {
    const request = ++this.request;
    this.loading.set(true);
    this.failed.set(false);
    this.api.program(this.code, cursor).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: page => {
        if (request !== this.request) return;
        const previous = cursor ? this.program()?.presentations ?? [] : [];
        this.program.set({ ...page, presentations: [...previous, ...page.presentations] });
        this.loading.set(false);
      },
      error: () => {
        if (request !== this.request) return;
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }
}
