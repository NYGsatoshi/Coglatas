import { HttpClient, HttpParams } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  Input,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { Observable, Subscription, forkJoin } from 'rxjs';

interface WorkspaceSearchApiResponse {
  readonly items?: unknown;
}

interface WorkspaceSearchApiItem {
  readonly type?: unknown;
  readonly id?: unknown;
  readonly title?: unknown;
  readonly workspaceId?: unknown;
  readonly createdAt?: unknown;
}

interface WorkspaceSearchResult {
  readonly key: string;
  readonly kind: 'project' | 'file';
  readonly kindLabel: 'Research / Project' | 'File';
  readonly id: string;
  readonly title: string;
  readonly route: string;
  readonly createdAt: string;
}

type WorkspaceSearchStatus = 'idle' | 'invalid' | 'loading' | 'ready' | 'empty' | 'error';

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const MAX_RENDERED_RESULTS = 8;
const TYPE_PAGE_SIZE = 8;

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-search',
  standalone: true,
  template: `
    <section class="workspace-search" aria-label="Search current Workspace">
      <form
        class="workspace-search__form"
        role="search"
        aria-label="Search current Workspace"
        [attr.aria-busy]="status() === 'loading'"
        (submit)="submitSearch($event)"
        (keydown.escape)="dismissResults()"
      >
        <label class="workspace-search__label" for="workspace-search-input">Workspace search</label>
        <div class="workspace-search__control">
          <input
            #searchInput
            id="workspace-search-input"
            data-testid="workspace-search-input"
            type="search"
            autocomplete="off"
            maxlength="120"
            placeholder="Search files, Research, Projects"
            aria-keyshortcuts="Control+K Meta+K"
            aria-describedby="workspace-search-scope workspace-search-status"
            [attr.aria-controls]="status() === 'ready' ? 'workspace-search-results' : null"
            [disabled]="!workspaceId"
            [value]="query()"
            (input)="updateQuery($event)"
          />
          <button
            type="submit"
            data-testid="workspace-search-submit"
            [disabled]="!workspaceId || status() === 'loading'"
          >
            {{ status() === 'loading' ? 'Searching' : 'Search' }}
          </button>
        </div>

        <p id="workspace-search-scope" class="workspace-search__scope">
          @if (workspaceId) {
            Current Workspace{{ workspaceLabel ? ': ' + workspaceLabel : '' }} · Files and Research / Projects
          } @else {
            Select a Workspace to search
          }
        </p>

        <div
          id="workspace-search-status"
          class="workspace-search__status"
          data-testid="workspace-search-status"
          aria-live="polite"
        >
          @switch (status()) {
            @case ('invalid') { Enter at least 2 characters. }
            @case ('loading') { Searching the current Workspace. }
            @case ('empty') { No matching Files or Research / Projects were found. }
            @case ('error') { Search is unavailable. Try again. }
            @case ('ready') { {{ results().length }} authorized results shown. }
          }
        </div>

        @if (status() === 'ready') {
          <ul id="workspace-search-results" class="workspace-search__results" data-testid="workspace-search-results">
            @for (result of results(); track result.key) {
              <li>
                <button
                  type="button"
                  class="workspace-search__result"
                  (click)="openResult(result)"
                >
                  <span class="workspace-search__kind">{{ result.kindLabel }}</span>
                  <span class="workspace-search__title">{{ result.title }}</span>
                </button>
              </li>
            }
          </ul>
        }
      </form>
    </section>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .workspace-search {
      position: relative;
      min-width: 0;
    }

    .workspace-search__form {
      display: grid;
      gap: 4px;
      min-width: 0;
    }

    .workspace-search__label,
    .workspace-search__scope,
    .workspace-search__status {
      margin: 0;
      color: var(--coglatas-color-text-muted);
      font-size: 0.75rem;
      font-weight: 700;
    }

    .workspace-search__control {
      display: grid;
      grid-template-columns: minmax(9rem, 1fr) auto;
      gap: 6px;
      min-width: 0;
    }

    .workspace-search__control input,
    .workspace-search__control button {
      min-height: var(--shell-touch-target, 44px);
      border: 1px solid var(--coglatas-color-border-default);
      border-radius: var(--shell-radius-md, 6px);
      background: var(--coglatas-color-bg-control);
      color: var(--coglatas-color-text-primary);
    }

    .workspace-search__control input {
      min-width: 0;
      padding: 0 var(--shell-space-3, 12px);
    }

    .workspace-search__control button {
      border-color: var(--coglatas-color-border-strong);
      background: var(--coglatas-color-bg-elevated);
      color: var(--coglatas-color-action-primary);
      font-weight: 700;
      cursor: pointer;
      padding: 0 var(--shell-space-3, 12px);
    }

    .workspace-search__control input:focus-visible,
    .workspace-search__control button:focus-visible,
    .workspace-search__result:focus-visible {
      outline: var(--coglatas-focus-outline);
      outline-offset: var(--coglatas-focus-offset);
    }

    .workspace-search__control input:disabled,
    .workspace-search__control button:disabled {
      cursor: default;
      opacity: 0.68;
    }

    .workspace-search__status:empty {
      display: none;
    }

    .workspace-search__results {
      position: absolute;
      z-index: 20;
      inset-inline: 0;
      top: calc(100% + 4px);
      display: grid;
      gap: 2px;
      max-height: min(24rem, 55vh);
      margin: 0;
      padding: 6px;
      overflow-y: auto;
      list-style: none;
      border: 1px solid var(--coglatas-color-border-strong);
      border-radius: var(--shell-radius-md, 6px);
      background: var(--coglatas-color-bg-elevated);
      box-shadow: 0 8px 24px rgb(0 0 0 / 20%);
    }

    .workspace-search__result {
      display: grid;
      grid-template-columns: minmax(7rem, auto) minmax(0, 1fr);
      align-items: center;
      gap: 8px;
      width: 100%;
      min-height: var(--shell-touch-target, 44px);
      border: 0;
      border-radius: var(--shell-radius-md, 6px);
      background: transparent;
      color: var(--coglatas-color-text-primary);
      cursor: pointer;
      padding: 8px;
      text-align: left;
    }

    .workspace-search__result:hover {
      background: var(--coglatas-color-bg-selected);
    }

    .workspace-search__kind {
      color: var(--coglatas-color-text-muted);
      font-size: 0.75rem;
      font-weight: 700;
    }

    .workspace-search__title {
      min-width: 0;
      overflow-wrap: anywhere;
      font-weight: 700;
    }

    @media (max-width: 420px) {
      .workspace-search__control {
        grid-template-columns: minmax(0, 1fr);
      }

      .workspace-search__result {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class WorkspaceSearchComponent implements OnChanges, OnDestroy {
  @Input() workspaceId: string | null = null;
  @Input() workspaceLabel = '';
  @ViewChild('searchInput') private searchInput?: ElementRef<HTMLInputElement>;

  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private request: Subscription | null = null;
  private requestGeneration = 0;

  readonly query = signal('');
  readonly status = signal<WorkspaceSearchStatus>('idle');
  readonly results = signal<readonly WorkspaceSearchResult[]>([]);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['workspaceId']) {
      this.resetForWorkspaceBoundary();
    }
  }

  ngOnDestroy(): void {
    this.cancelRequest();
  }

  @HostListener('document:keydown', ['$event'])
  focusFromShortcut(event: KeyboardEvent): void {
    if (
      !this.workspaceId ||
      event.altKey ||
      event.shiftKey ||
      !(event.ctrlKey || event.metaKey) ||
      event.key.toLowerCase() !== 'k'
    ) {
      return;
    }

    event.preventDefault();
    this.searchInput?.nativeElement.focus();
  }

  updateQuery(event: Event): void {
    const value = event.target instanceof HTMLInputElement ? event.target.value : '';
    this.query.set(value);
    if (this.status() !== 'idle') {
      this.cancelRequest();
      this.results.set([]);
      this.status.set('idle');
    }
  }

  submitSearch(event: Event): void {
    event.preventDefault();
    const workspaceId = this.workspaceId;
    const query = this.query().trim();
    if (!workspaceId) {
      this.resetForWorkspaceBoundary();
      return;
    }

    if (query.length < 2) {
      this.cancelRequest();
      this.results.set([]);
      this.status.set('invalid');
      return;
    }

    const generation = ++this.requestGeneration;
    this.request?.unsubscribe();
    this.request = null;
    this.results.set([]);
    this.status.set('loading');

    const projects = this.searchType('Project', query, workspaceId);
    const files = this.searchType('File', query, workspaceId);
    const request = forkJoin({ projects, files }).subscribe({
      next: ({ projects: projectResponse, files: fileResponse }) => {
        if (!this.isCurrent(generation, workspaceId, query)) {
          return;
        }

        const combined = [
          ...this.parseResponse(projectResponse, 'project', workspaceId),
          ...this.parseResponse(fileResponse, 'file', workspaceId),
        ];
        const deduplicated = new Map(combined.map((item) => [item.key, item]));
        const results = [...deduplicated.values()]
          .sort((left, right) => {
            const byDate = Date.parse(right.createdAt) - Date.parse(left.createdAt);
            return Number.isFinite(byDate) && byDate !== 0
              ? byDate
              : left.title.localeCompare(right.title);
          })
          .slice(0, MAX_RENDERED_RESULTS);

        this.results.set(results);
        this.status.set(results.length > 0 ? 'ready' : 'empty');
      },
      error: () => {
        if (this.isCurrent(generation, workspaceId, query)) {
          this.results.set([]);
          this.status.set('error');
        }
      },
    });
    this.request = request;
    request.add(() => {
      if (this.request === request) {
        this.request = null;
      }
    });
  }

  dismissResults(): void {
    this.cancelRequest();
    this.results.set([]);
    this.status.set('idle');
  }

  openResult(result: WorkspaceSearchResult): void {
    this.dismissResults();
    void this.router.navigateByUrl(result.route);
  }

  private searchType(type: 'Project' | 'File', query: string, workspaceId: string): Observable<unknown> {
    const params = new HttpParams()
      .set('q', query)
      .set('type', type)
      .set('workspaceId', workspaceId)
      .set('page', '1')
      .set('pageSize', String(TYPE_PAGE_SIZE));
    return this.http.get<unknown>('/api/search', { params, withCredentials: true });
  }

  private parseResponse(
    response: unknown,
    kind: WorkspaceSearchResult['kind'],
    workspaceId: string,
  ): WorkspaceSearchResult[] {
    if (!this.isRecord(response)) {
      return [];
    }

    const api = response as WorkspaceSearchApiResponse;
    if (!Array.isArray(api.items)) {
      return [];
    }

    const expectedType = kind === 'project' ? 7 : 13;
    const expectedTypeName = kind === 'project' ? 'Project' : 'File';
    const kindLabel = kind === 'project' ? 'Research / Project' : 'File';
    const results: WorkspaceSearchResult[] = [];

    for (const raw of api.items) {
      if (!this.isRecord(raw)) {
        continue;
      }

      const item = raw as WorkspaceSearchApiItem;
      const typeMatches = item.type === expectedType || item.type === expectedTypeName;
      const id = typeof item.id === 'string' ? item.id : '';
      const title = typeof item.title === 'string' ? item.title.trim() : '';
      const resultWorkspaceId = typeof item.workspaceId === 'string' ? item.workspaceId : '';
      const createdAt = typeof item.createdAt === 'string' ? item.createdAt : '';
      if (
        !typeMatches ||
        !UUID_PATTERN.test(id) ||
        !title ||
        resultWorkspaceId.toLowerCase() !== workspaceId.toLowerCase()
      ) {
        continue;
      }

      results.push({
        key: `${kind}:${id.toLowerCase()}`,
        kind,
        kindLabel,
        id,
        title,
        route: kind === 'project' ? `/projects/${id}` : `/workspaces/${workspaceId}/files`,
        createdAt,
      });
    }

    return results;
  }

  private resetForWorkspaceBoundary(): void {
    this.cancelRequest();
    this.query.set('');
    this.results.set([]);
    this.status.set('idle');
  }

  private cancelRequest(): void {
    this.requestGeneration++;
    this.request?.unsubscribe();
    this.request = null;
  }

  private isCurrent(generation: number, workspaceId: string, query: string): boolean {
    return (
      generation === this.requestGeneration &&
      this.workspaceId === workspaceId &&
      this.query().trim() === query
    );
  }

  private isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
  }
}
