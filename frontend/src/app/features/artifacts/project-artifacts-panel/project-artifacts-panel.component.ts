import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, Input, OnChanges, OnDestroy, SimpleChanges, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';

import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';

type ArtifactStatus = 'Draft' | 'Submitted' | 'Reviewed' | 'Approved' | 'Archived' | 'Unknown';
type ListStatus = 'loading' | 'ready' | 'empty' | 'unavailable' | 'error';

interface ProjectArtifact {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly updatedAt: string;
  readonly status: ArtifactStatus;
}

const DISPLAY_LIMIT = 20;
const ARTIFACT_STATUSES = ['Draft', 'Submitted', 'Reviewed', 'Approved', 'Archived'] as const;

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-project-artifacts-panel',
  standalone: true,
  imports: [RouterLink],
  template: `
    <section class="project-artifacts" data-testid="project-artifacts-panel"
      [attr.aria-label]="i18n.translate('projects.artifacts.title')">
      <header>
        <div>
          <h3>{{ i18n.translate('projects.artifacts.title') }}</h3>
          <p>{{ i18n.translate('projects.artifacts.description') }}</p>
        </div>
        <button type="button" (click)="refresh()" [disabled]="status() === 'loading'"
          data-testid="project-artifacts-refresh">
          {{ i18n.translate('projects.artifacts.refresh') }}
        </button>
      </header>
      @if (status() === 'loading') {
        <p role="status">{{ i18n.translate('projects.artifacts.loading') }}</p>
      } @else if (status() === 'unavailable') {
        <p role="status" data-testid="project-artifacts-unavailable">{{ i18n.translate('projects.artifacts.denied') }}</p>
      } @else if (status() === 'error') {
        <p role="alert" data-testid="project-artifacts-error">{{ i18n.translate('projects.artifacts.error') }}</p>
      } @else if (status() === 'empty') {
        <p role="status" data-testid="project-artifacts-empty">{{ i18n.translate('projects.artifacts.empty') }}</p>
      } @else {
        <ul data-testid="project-artifacts-list">
          @for (artifact of visibleArtifacts(); track artifact.id) {
            <li>
              <a [routerLink]="['/artifacts', artifact.id]"
                [attr.aria-label]="i18n.translate('projects.artifacts.open') + ': ' + artifact.title">
                {{ artifact.title }}
              </a>
              @if (artifact.description) { <p>{{ artifact.description }}</p> }
              <span>{{ statusLabel(artifact.status) }}</span>
              @if (artifact.updatedAt) {
                <span>{{ i18n.translate('projects.artifacts.updated') }}:
                  {{ i18n.formatDateTime(artifact.updatedAt, { dateStyle: 'medium', timeStyle: 'short' }) }}</span>
              }
            </li>
          }
        </ul>
        @if (truncated()) {
          <p role="status" data-testid="project-artifacts-bounded">
            {{ i18n.translate('projects.artifacts.limit', { shown: i18n.formatNumber(DISPLAY_LIMIT), total: i18n.formatNumber(artifacts().length) }) }}
          </p>
        }
      }
    </section>
  `,
  styles: [`
    .project-artifacts { display: grid; gap: var(--coglatas-space-3); min-width: 0; padding: var(--coglatas-space-4); background: var(--coglatas-color-bg-surface); color: var(--coglatas-color-text-primary); border: 1px solid var(--coglatas-color-border-default); border-radius: var(--coglatas-radius-lg); }
    header { display: flex; flex-wrap: wrap; align-items: start; justify-content: space-between; gap: var(--coglatas-space-3); }
    header > div { display: grid; gap: var(--coglatas-space-1); }
    h3, p { margin: 0; overflow-wrap: anywhere; }
    h3 { font-size: 1rem; }
    p, li span { color: var(--coglatas-color-text-secondary); }
    header button { min-height: 44px; background: var(--coglatas-color-bg-card); border: 1px solid var(--coglatas-color-border-default); border-radius: var(--coglatas-radius-md); color: var(--coglatas-color-text-primary); }
    ul { display: grid; gap: var(--coglatas-space-2); padding: 0; margin: 0; list-style: none; }
    li { display: grid; gap: var(--coglatas-space-1); min-width: 0; padding: var(--coglatas-space-3); border: 1px solid var(--coglatas-color-border-default); border-radius: var(--coglatas-radius-md); }
    li a { overflow-wrap: anywhere; color: var(--coglatas-color-action-primary); font-weight: 700; }
    li span { font-size: .875rem; }
    button:focus-visible, a:focus-visible { outline: 2px solid var(--coglatas-color-focus); outline-offset: 2px; }
  `],
})
export class ProjectArtifactsPanelComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) projectId = '';

  readonly i18n = injectProjectsI18n();
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private generation = 0;
  private request: Subscription | null = null;

  readonly DISPLAY_LIMIT = DISPLAY_LIMIT;
  readonly status = signal<ListStatus>('loading');
  readonly artifacts = signal<readonly ProjectArtifact[]>([]);
  readonly visibleArtifacts = computed(() => this.artifacts().slice(0, DISPLAY_LIMIT));
  readonly truncated = computed(() => this.artifacts().length > DISPLAY_LIMIT);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['projectId']) {
      this.refresh();
    }
  }

  refresh(): void {
    this.request?.unsubscribe();
    this.request = null;
    const generation = ++this.generation;
    const projectId = this.projectId;
    this.artifacts.set([]);
    this.status.set(projectId ? 'loading' : 'unavailable');
    if (!projectId) {
      return;
    }

    this.request = this.http.get<unknown>(
      `/api/projects/${encodeURIComponent(projectId)}/artifacts`,
      { withCredentials: true },
    ).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        if (generation !== this.generation || projectId !== this.projectId) {
          return;
        }
        const mapped = mapProjectArtifacts(response, projectId);
        if (!mapped) {
          this.status.set('unavailable');
          return;
        }
        this.artifacts.set(mapped);
        this.status.set(mapped.length === 0 ? 'empty' : 'ready');
      },
      error: (error: { status?: number }) => {
        if (generation !== this.generation || projectId !== this.projectId) {
          return;
        }
        this.status.set([400, 401, 403, 404].includes(error.status ?? 0) ? 'unavailable' : 'error');
      },
    });
  }

  statusLabel(status: ArtifactStatus): string {
    const key = ({
      Draft: 'projects.artifacts.statusDraft',
      Submitted: 'projects.artifacts.statusSubmitted',
      Reviewed: 'projects.artifacts.statusReviewed',
      Approved: 'projects.artifacts.statusApproved',
      Archived: 'projects.artifacts.statusArchived',
      Unknown: 'projects.artifacts.statusUnknown',
    } as const)[status];
    return this.i18n.translate(key);
  }

  ngOnDestroy(): void {
    ++this.generation;
    this.request?.unsubscribe();
    this.artifacts.set([]);
  }
}

function mapProjectArtifacts(response: unknown, projectId: string): readonly ProjectArtifact[] | null {
  if (!Array.isArray(response)) {
    return null;
  }
  const mapped: ProjectArtifact[] = [];
  for (const entry of response) {
    if (!entry || typeof entry !== 'object' || Array.isArray(entry)) {
      return null;
    }
    const record = entry as Record<string, unknown>;
    if (record['projectId'] !== projectId ||
        typeof record['id'] !== 'string' || !record['id'].trim() ||
        typeof record['title'] !== 'string' || !record['title'].trim()) {
      // Reject the entire response: never display another Project's metadata,
      // even if the first items happened to be valid.
      return null;
    }
    mapped.push({
      id: record['id'],
      title: record['title'],
      description: typeof record['description'] === 'string' ? record['description'] : '',
      updatedAt: typeof record['updatedAt'] === 'string' ? record['updatedAt']
        : typeof record['createdAt'] === 'string' ? record['createdAt'] : '',
      status: artifactStatus(record['status']),
    });
  }
  return mapped;
}

function artifactStatus(value: unknown): ArtifactStatus {
  if (typeof value === 'number' && Number.isInteger(value)) {
    return ARTIFACT_STATUSES[value] ?? 'Unknown';
  }
  if (typeof value === 'string' && ARTIFACT_STATUSES.some((status) => status === value)) {
    return value as ArtifactStatus;
  }
  return 'Unknown';
}
