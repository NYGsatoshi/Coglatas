import { I18nService, TranslationKey } from '../../../core/i18n/i18n.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { normalizeApiError } from '../../../core/api/api-error.adapter';
import { WorkspacesFacade } from '../workspaces.facade';
import {
  QuickResearchCreateResponseError,
  WorkspaceResearchQuickCreateService,
  createQuickResearchIdempotencyKey,
} from '../workspace-research-quick-create.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-workspace-research-quick-create-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './workspace-research-quick-create-page.component.html',
  styleUrl: './workspace-research-quick-create-page.component.scss',
})
export class WorkspaceResearchQuickCreatePageComponent {
  readonly i18n = inject(I18nService);
  @ViewChild('titleInput') private titleInput?: ElementRef<HTMLInputElement>;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly workspaces = inject(WorkspacesFacade);
  private readonly quickCreate = inject(WorkspaceResearchQuickCreateService);

  readonly workspaceId = this.route.snapshot.paramMap.get('workspaceId') ?? '';
  readonly title = signal('');
  readonly submitting = signal(false);
  readonly navigating = signal(false);
  readonly createdProjectId = signal<string | null>(null);
  private readonly titleErrorKey = signal<TranslationKey | null>(null);
  readonly titleError = computed(() => { const key = this.titleErrorKey(); return key ? this.i18n.translate(key) : null; });
  private readonly errorState = signal<{ key: TranslationKey } | { message: string } | null>(null);
  readonly errorMessage = computed(() => { const state = this.errorState(); return state ? 'key' in state ? this.i18n.translate(state.key) : state.message : null; });
  private requestIdentity: string | null = null;
  private requestTitle: string | null = null;

  readonly workspace = computed(
    () =>
      this.workspaces.dashboard().workspaces.find((item) => item.id === this.workspaceId) ?? null,
  );
  readonly canCreateResearch = computed(
    () => this.workspace()?.capabilities.includes('createProject') === true,
  );
  readonly busy = computed(() => this.submitting() || this.navigating());

  updateTitle(value: string): void {
    if (this.busy() || this.createdProjectId() !== null) {
      return;
    }

    this.title.set(value);
    this.titleErrorKey.set(null);
    this.errorState.set(null);
    const normalized = value.trim();
    if (this.requestTitle !== null && normalized !== this.requestTitle) {
      this.requestIdentity = null;
      this.requestTitle = null;
    }
  }

  submit(): void {
    if (this.busy()) {
      return;
    }

    const committedProjectId = this.createdProjectId();
    if (committedProjectId !== null) {
      void this.navigateToCreatedProject(committedProjectId);
      return;
    }

    this.titleErrorKey.set(null);
    if (!this.canCreateResearch()) {
      this.errorState.set({ key: 'research.denied' });
      return;
    }

    const normalizedTitle = this.title().trim();
    if (!normalizedTitle) {
      this.showTitleError('research.nameRequired');
      return;
    }
    if (normalizedTitle.length > 200) {
      this.showTitleError('research.nameLength');
      return;
    }

    if (this.requestTitle !== normalizedTitle || this.requestIdentity === null) {
      this.requestTitle = normalizedTitle;
      this.requestIdentity = createQuickResearchIdempotencyKey();
    }

    const requestIdentity = this.requestIdentity;
    this.submitting.set(true);
    this.errorState.set(null);
    this.quickCreate
      .createResearch(this.workspaceId, normalizedTitle, requestIdentity)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: (projectId) => {
          // A verified canonical HTTP 201 has committed this command. From
          // here, recovery is navigation-only and must never post a new key.
          this.requestIdentity = null;
          this.requestTitle = null;
          this.createdProjectId.set(projectId);
          void this.navigateToCreatedProject(projectId);
        },
        error: (error: unknown) => {
          const normalized = normalizeApiError(error);
          if (
            error instanceof QuickResearchCreateResponseError ||
            (normalized.httpStatus >= 200 && normalized.httpStatus < 300)
          ) {
            this.errorState.set({ key: 'research.uncertain' });
            return;
          }

          this.errorState.set({ message: normalized.message });
        },
      });
  }

  private showTitleError(key: TranslationKey): void {
    this.titleErrorKey.set(key);
    this.errorState.set(null);
    queueMicrotask(() => this.titleInput?.nativeElement.focus());
  }

  private async navigateToCreatedProject(projectId: string): Promise<void> {
    if (this.navigating()) {
      return;
    }

    this.navigating.set(true);
    this.errorState.set(null);
    try {
      const navigated = await this.router.navigate(['/projects', projectId]);
      if (!navigated) {
        this.setCommittedNavigationError();
      }
    } catch {
      this.setCommittedNavigationError();
    } finally {
      this.navigating.set(false);
    }
  }

  private setCommittedNavigationError(): void {
    this.errorState.set({ key: 'research.committed' });
  }
}
