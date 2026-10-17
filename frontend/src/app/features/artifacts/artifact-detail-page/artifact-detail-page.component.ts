import { HttpClient } from '@angular/common/http';
import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';

interface ArtifactDetailDto {
  readonly id?: unknown;
  readonly projectId?: unknown;
  readonly title?: unknown;
  readonly description?: unknown;
  readonly artifactType?: unknown;
  readonly status?: unknown;
  readonly updatedAt?: unknown;
  readonly createdAt?: unknown;
}

interface ArtifactDetailViewModel {
  readonly status: 'loading' | 'ready' | 'unavailable' | 'error';
  readonly title: string;
  readonly description: string;
  readonly artifactType: string;
  readonly artifactStatus: string;
  readonly updatedAt: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-artifact-detail-page',
  standalone: true,
  template: `
    <main class="artifact-detail" data-testid="artifact-detail-page">
      @let vm = viewModel();
      @if (vm.status === 'loading') {
        <p role="status">{{ i18n.translate('artifact.loading') }}</p>
      } @else if (vm.status === 'ready') {
        <p class="artifact-detail__eyebrow">{{ i18n.translate('artifact.title') }}</p>
        <h1>{{ vm.title || i18n.translate('artifact.title') }}</h1>
        @if (vm.description) {
          <p>{{ vm.description }}</p>
        }
        <dl>
          <dt>{{ i18n.translate('artifact.type') }}</dt>
          <dd>{{ artifactTypeLabel(vm.artifactType) }}</dd>
          <dt>{{ i18n.translate('artifact.status') }}</dt>
          <dd>{{ artifactStatusLabel(vm.artifactStatus) }}</dd>
          <dt>{{ i18n.translate('artifact.updated') }}</dt>
          <dd>{{ i18n.formatDateTime(vm.updatedAt, { dateStyle: 'medium', timeStyle: 'short' }) || i18n.translate('artifact.unknown') }}</dd>
        </dl>
      } @else if (vm.status === 'unavailable') {
        <h1>{{ i18n.translate('artifact.unavailable') }}</h1>
        <p>{{ i18n.translate('artifact.denied') }}</p>
      } @else {
        <h1>{{ i18n.translate('artifact.unavailable') }}</h1>
        <p>{{ i18n.translate('artifact.loadError') }}</p>
      }
    </main>
  `,
  styles: [`
    .artifact-detail { display: grid; gap: 16px; max-width: 56rem; padding: 18px; border: 1px solid var(--coglatas-color-border-default); border-radius: 8px; background: var(--coglatas-color-bg-card); color: var(--coglatas-color-text-secondary); }
    .artifact-detail__eyebrow { color: var(--coglatas-color-text-primary); font-size: 13px; font-weight: 700; }
    h1, p, dl { margin: 0; overflow-wrap: anywhere; }
    h1 { color: var(--coglatas-color-text-primary); font-size: 15px; font-weight: 700; line-height: 1.4; }
    dl { display: grid; grid-template-columns: minmax(0, 110px) minmax(0, 1fr); gap: 12px 16px; padding-top: 16px; border-top: 1px solid var(--coglatas-color-border-default); }
    dt { font-size: 11px; }
    dd { margin: 0; color: var(--coglatas-color-text-primary); font-size: 12px; font-weight: 500; overflow-wrap: anywhere; }
  `],
})
export class ArtifactDetailPageComponent {
  readonly i18n = injectFilesI18n();
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  readonly viewModel = signal<ArtifactDetailViewModel>(emptyViewModel('loading'));

  artifactTypeLabel(value: string): string {
    const keys = ['artifact.type.document', 'artifact.type.image', 'artifact.type.video', 'artifact.type.code',
      'artifact.type.presentation', 'artifact.type.spreadsheet', 'artifact.type.archive', 'artifact.type.other'] as const;
    const names = ['Document', 'Image', 'Video', 'Code', 'Presentation', 'Spreadsheet', 'Archive', 'Other'];
    const index = names.indexOf(value);
    const key = keys[index >= 0 ? index : /^\d$/.test(value) ? Number(value) : -1];
    return key ? this.i18n.translate(key) : value || this.i18n.translate('artifact.unknown');
  }

  artifactStatusLabel(value: string): string {
    const keys = ['artifact.status.draft', 'artifact.status.submitted', 'artifact.status.reviewed',
      'artifact.status.approved', 'artifact.status.archived'] as const;
    const names = ['Draft', 'Submitted', 'Reviewed', 'Approved', 'Archived'];
    const index = names.indexOf(value);
    const key = keys[index >= 0 ? index : /^\d$/.test(value) ? Number(value) : -1];
    return key ? this.i18n.translate(key) : value || this.i18n.translate('artifact.unknown');
  }

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const artifactId = params.get('artifactId');
      if (!artifactId) {
        this.viewModel.set(emptyViewModel('unavailable'));
        return;
      }

      this.loadArtifact(artifactId);
    });
  }

  private loadArtifact(artifactId: string): void {
    this.viewModel.set(emptyViewModel('loading'));
    this.http.get<ArtifactDetailDto>(`/api/artifacts/${encodeURIComponent(artifactId)}`, {
      withCredentials: true
    }).subscribe({
      next: (artifact) => {
        const id = stringValue(artifact.id);
        if (!id || id !== artifactId) {
          this.viewModel.set(emptyViewModel('unavailable'));
          return;
        }

        const updatedAt = stringValue(artifact.updatedAt) ?? stringValue(artifact.createdAt) ?? '';
        this.viewModel.set({
          status: 'ready',
          title: stringValue(artifact.title) ?? '',
          description: stringValue(artifact.description) ?? '',
          artifactType: displayValue(artifact.artifactType),
          artifactStatus: displayValue(artifact.status),
          updatedAt
        });
      },
      error: (error: { status?: number }) => {
        this.viewModel.set(emptyViewModel(
          error.status === 401 || error.status === 403 || error.status === 404
            ? 'unavailable'
            : 'error'
        ));
      }
    });
  }
}

function emptyViewModel(status: ArtifactDetailViewModel['status']): ArtifactDetailViewModel {
  return {
    status,
    title: '',
    description: '',
    artifactType: '',
    artifactStatus: '',
    updatedAt: ''
  };
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function displayValue(value: unknown): string {
  if (typeof value === 'string' && value.length > 0) {
    return value;
  }
  if (typeof value === 'number' && Number.isFinite(value)) {
    return String(value);
  }
  return '';
}
