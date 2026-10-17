import { I18nService } from '../../../core/i18n/i18n.service';
import { projectUiText, ProjectUiCopy } from '../projects.i18n';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged, map } from 'rxjs';

import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';
import { AppEmptyStateComponent } from '../../../shared/empty-state/app-empty-state/app-empty-state.component';
import { AppErrorBannerComponent } from '../../../shared/error/app-error-banner/app-error-banner.component';
import { AppInlineLoadingComponent } from '../../../shared/loading/app-inline-loading/app-inline-loading.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { ContinueWorkingPanelComponent } from '../../../shared/continue-working/continue-working-panel.component';
import { WorkspacesFacade } from '../../workspaces/workspaces.facade';
import { ProjectCreateInput } from '../project-create.api';
import { ProjectCreateDialogComponent } from '../project-create-dialog/project-create-dialog.component';
import { ProjectCreateFacade } from '../project-create.facade';
import { ProjectsFacade } from '../projects.facade';
import { ProjectSummaryPanelComponent } from '../project-summary-panel/project-summary-panel.component';
import { FrontendFeatureFlagsService } from '../../../core/feature-flags/frontend-feature-flags.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-projects-overview-page',
  standalone: true,
  imports: [
    RouterLink,
    AppEmptyStateComponent,
    AppErrorBannerComponent,
    AppInlineLoadingComponent,
    AppPermissionDeniedComponent,
    ContinueWorkingPanelComponent,
    ProjectCreateDialogComponent,
    ProjectSummaryPanelComponent,
  ],
  templateUrl: './projects-overview-page.component.html',
  styleUrl: './projects-overview-page.component.scss',
})
export class ProjectsOverviewPageComponent {
  readonly i18n = inject(I18nService);
  readonly t = (text: ProjectUiCopy, parameters: Readonly<Record<string, string | number>> = {}): string => projectUiText(this.i18n, text, parameters);
  private readonly facade = inject(ProjectsFacade);
  private readonly flags = inject(FrontendFeatureFlagsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly workspaces = inject(WorkspacesFacade);
  private readonly projectCreate = inject(ProjectCreateFacade);
  private readonly routeWorkspaceId = toSignal(
    this.route.paramMap.pipe(
      map((params) => params.get('workspaceId')),
      distinctUntilChanged(),
    ),
    { initialValue: this.route.snapshot.paramMap.get('workspaceId') },
  );
  private readonly createQueryRequested = toSignal(
    this.route.queryParamMap.pipe(
      map((params) => params.get('create') === '1'),
      distinctUntilChanged(),
    ),
    { initialValue: this.route.snapshot.queryParamMap.get('create') === '1' },
  );
  private readonly workspaceScopeId = computed(
    () => this.routeWorkspaceId() ?? this.activeWorkspace.activeWorkspace()?.id ?? null,
  );
  private observedWorkspaceScopeId = this.workspaceScopeId();
  private workspaceScopeRecheckPending = false;
  private handledCreateQuery = false;

  readonly page = computed(() => this.facade.getProjectsOverview());
  readonly designSystemV04Enabled = this.flags.designSystemV04Enabled;
  readonly createDialogOpen = signal(false);
  readonly createOptions = this.projectCreate.options;
  readonly createState = this.projectCreate.createState;
  private readonly scopedWorkspace = computed(() => {
    // A valid Workspace route owns scope. The global Projects route uses only
    // the canonical active Workspace selected by the shell.
    const workspaceId = this.workspaceScopeId();
    if (!workspaceId) {
      return null;
    }

    return this.workspaces.dashboard().workspaces.find((item) => item.id === workspaceId);
  });
  readonly createWorkspace = computed(() => {
    const workspace = this.scopedWorkspace();
    if (!workspace?.capabilities.includes('openProjectCreate')) {
      return null;
    }

    return { id: workspace.id, name: workspace.displayName };
  });
  readonly continueWorkingWorkspaceId = computed(() => this.scopedWorkspace()?.id ?? null);
  readonly canCreateResearch = computed(() =>
    this.scopedWorkspace()?.capabilities.includes('openProjectCreate') === true,
  );
  readonly canBrowseFiles = computed(() =>
    // Workspace File inventory uses the same server CanViewWorkspace policy
    // as this dashboard-owned openWorkspace projection. addFiles is a
    // separate mutation bit and is intentionally not consulted here.
    this.scopedWorkspace()?.capabilities.includes('openWorkspace') === true,
  );

  constructor() {
    effect(() => {
      const nextWorkspaceScopeId = this.workspaceScopeId();
      const dashboard = this.workspaces.dashboard();
      const createState = this.createState();
      const committedNavigationInProgress =
        createState.status === 'committedPendingNavigation' ||
        (createState.status === 'submitting' && Boolean(createState.createdProjectId));

      if (nextWorkspaceScopeId !== null && nextWorkspaceScopeId !== this.observedWorkspaceScopeId) {
        // Non-null scope changes are real route/selection transitions, even
        // when the dashboard happens to be refreshing at the same time. The
        // canonical Workspace boundary already clears ProjectCreateFacade;
        // this page only owns destruction of its local dialog/form.
        this.workspaceScopeRecheckPending = false;
        this.observedWorkspaceScopeId = nextWorkspaceScopeId;
        this.createDialogOpen.set(false);
        return;
      }

      if (dashboard.status === 'loading' || dashboard.status === 'error') {
        // Authorization rechecks deliberately hide ActiveWorkspace while the
        // server-owned Workspace list is refreshed. A Workspace-scoped route
        // still exposes its route ID in this interval, so dashboard state—not
        // only a null ActiveWorkspace—marks the transient gap. Protected
        // options are cleared separately; keeping the dialog mounted retains
        // only the user's local, non-authoritative form values.
        this.workspaceScopeRecheckPending = this.observedWorkspaceScopeId !== null;
        return;
      }

      if (!nextWorkspaceScopeId) {
        this.workspaceScopeRecheckPending = false;
        const hadWorkspaceScope = this.observedWorkspaceScopeId !== null;
        this.observedWorkspaceScopeId = null;
        if (!committedNavigationInProgress && (hadWorkspaceScope || this.createDialogOpen())) {
          this.createDialogOpen.set(false);
        }
        return;
      }

      const authoritativeWorkspace =
        dashboard.status === 'ready'
          ? dashboard.workspaces.find((workspace) => workspace.id === nextWorkspaceScopeId)
          : null;
      if (!authoritativeWorkspace?.capabilities.includes('openProjectCreate')) {
        // The Workspace list is terminal and either the route was revoked or
        // the actor no longer has the server-projected full-create affordance.
        // Preserve only an opaque, already-committed navigation recovery.
        this.workspaceScopeRecheckPending = false;
        if (!committedNavigationInProgress) {
          this.createDialogOpen.set(false);
        }
        return;
      }

      if (this.workspaceScopeRecheckPending) {
        this.workspaceScopeRecheckPending = false;
        if (this.createDialogOpen() && !committedNavigationInProgress) {
          void this.projectCreate.loadOptions(nextWorkspaceScopeId);
        }
      }
    });

    effect(() => {
      const createRequested = this.createQueryRequested();
      if (!createRequested) {
        this.handledCreateQuery = false;
        return;
      }
      if (this.handledCreateQuery) {
        return;
      }

      const workspaceId = this.workspaceScopeId();
      const dashboard = this.workspaces.dashboard();
      if (!workspaceId || dashboard.status === 'loading') {
        return;
      }

      // A ready dashboard row is the authoritative presentation grant. A
      // ready list without the row is also an authoritative denial. Consume
      // the marker either way so reload/back cannot loop the command surface.
      if (dashboard.status !== 'ready') {
        return;
      }

      this.handledCreateQuery = true;
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { create: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
      if (this.createWorkspace()) {
        this.openCreateDialog();
      }
    });
  }

  retry(): void {
    this.facade.retryProjects();
  }

  openCreateDialog(): void {
    const workspace = this.createWorkspace();
    if (!workspace) {
      return;
    }

    this.projectCreate.resetCreatePresentation();
    this.createDialogOpen.set(true);
    void this.projectCreate.loadOptions(workspace.id);
  }

  resumeCreatedProject(): void {
    if (this.createState().status === 'committedPendingNavigation') {
      this.createDialogOpen.set(true);
    }
  }

  cancelCreateDialog(): void {
    this.createDialogOpen.set(false);
    this.projectCreate.resetCreatePresentation();
  }

  retryCreateOptions(): void {
    const workspace = this.createWorkspace();
    if (workspace) {
      void this.projectCreate.loadOptions(workspace.id);
    }
  }

  async submitProject(input: ProjectCreateInput): Promise<void> {
    const workspace = this.createWorkspace();
    if (!workspace) {
      return;
    }

    if (await this.projectCreate.createProject(workspace.id, input)) {
      this.createDialogOpen.set(false);
    }
  }

  async retryCreatedProjectNavigation(): Promise<void> {
    if (await this.projectCreate.retryCreatedProjectNavigation()) {
      this.createDialogOpen.set(false);
    }
  }
}
