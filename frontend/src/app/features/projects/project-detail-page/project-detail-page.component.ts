import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';
import { FrontendFeatureFlagsService } from '../../../core/feature-flags/frontend-feature-flags.service';
import { PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES } from '../../../core/i18n/messages/projectide.messages';
import { projectPriorityText, projectUiText, ProjectUiCopy } from '../projects.i18n';
import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, map } from 'rxjs';

import { AppEmptyStateComponent } from '../../../shared/empty-state/app-empty-state/app-empty-state.component';
import { AppErrorBannerComponent } from '../../../shared/error/app-error-banner/app-error-banner.component';
import { AppInlineLoadingComponent } from '../../../shared/loading/app-inline-loading/app-inline-loading.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { CoglatasGanttComponent, CoglatasKanbanComponent } from '../../../shared/ui/adapters/syncfusion/coglatas-adapter-shells.components';
import {
  CoglatasAdapterState,
  CoglatasGanttContract,
  CoglatasGanttEditIntent,
  CoglatasGanttItem,
  CoglatasKanbanContract,
  CoglatasKanbanMoveRequest
} from '../../../shared/ui/contracts/coglatas-complex-adapter.contracts';
import { ProjectGanttSnapshot } from '../project-gantt.models';
import {
  ProjectKanbanCard,
  ProjectKanbanColumn,
  ProjectKanbanSnapshot,
  ProjectKanbanSwimlane
} from '../project-kanban.models';
import {
  ProjectDetailFacade,
  ProjectDetailTab,
  ProjectKanbanStatus,
  ProjectScheduleStatus
} from '../project-detail.facade';
import { TaskGridRow } from '../projects.types';
import { TaskTableComponent } from '../task-table/task-table.component';
import { ProjectArtifactsPanelComponent } from '../../artifacts/project-artifacts-panel/project-artifacts-panel.component';

@Component({ selector: 'app-project-detail-page', standalone: true, imports: [RouterLink, AppEmptyStateComponent, AppErrorBannerComponent, AppInlineLoadingComponent, AppPermissionDeniedComponent, CoglatasKanbanComponent, CoglatasGanttComponent, TaskTableComponent, ProjectArtifactsPanelComponent], templateUrl: './project-detail-page.component.html', styleUrl: './project-detail-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ProjectDetailPageComponent implements OnDestroy {
  readonly projectIdeFlags = inject(FrontendFeatureFlagsService);
  readonly i18n = injectProjectsI18n();
  readonly t = (text: ProjectUiCopy, parameters: Readonly<Record<string, string | number>> = {}): string => projectUiText(this.i18n, text, parameters);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly facade = inject(ProjectDetailFacade);
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly destroyRef = inject(DestroyRef);
  readonly page = computed(() => this.facade.view());
  readonly tab = signal<ProjectDetailTab>('overview');
  readonly artifactsOpen = signal(false);
  readonly schedulePresentation = signal<'desktop' | 'narrow'>('desktop');
  readonly configOpen = signal(false);
  readonly configColumns = signal<readonly ProjectKanbanColumn[]>([]);
  readonly configSwimlane = signal<ProjectKanbanSwimlane>('none');
  private readonly activationAnnouncement = viewChild<ElementRef<HTMLElement>>('activationAnnouncement');
  private readonly pageFocus = viewChild<ElementRef<HTMLElement>>('pageFocus');
  private previousActivationStatus = 'idle';
  private activationCompletionInterrupted = false;
  private activationInterruptionFallbackFocused = false;
  private readonly pendingTaskCreateProjectId = signal<string | null>(null);
  private readonly taskCreateNavigationInterrupted = signal(false);
  private readonly taskCreateNavigationInFlight = signal(false);
  private readonly operationalTabs: readonly { id: ProjectDetailTab; label: ProjectUiCopy }[] = [{ id: 'overview', label: 'Overview' }, { id: 'tasks', label: 'Tasks' }, { id: 'list', label: 'List' }, { id: 'schedule', label: 'Schedule' }, { id: 'workload', label: 'Workload' }, { id: 'members', label: 'Members' }];
  readonly tabs = computed<readonly { id: ProjectDetailTab; label: ProjectUiCopy }[]>(() =>
    this.page().project?.isOperational === true
      ? this.operationalTabs
      : this.operationalTabs.slice(0, 1));
  readonly canActivate = computed(() => {
    const vm = this.page();
    return vm.project?.canActivate === true &&
      Number.isSafeInteger(vm.project.versionNo) &&
      (vm.project.versionNo ?? 0) > 0 &&
      ['idle', 'failure', 'conflict'].includes(vm.activation.status);
  });
  readonly activationBusy = computed(() =>
    ['submitting', 'reconciling'].includes(this.page().activation.status));
  readonly swimlanes: readonly { value: ProjectKanbanSwimlane; label: ProjectUiCopy }[] = [
    { value: 'none', label: 'None' },
    { value: 'primaryAssignee', label: 'Primary assignee' },
    { value: 'targetGroup', label: 'Target group' },
    { value: 'priority', label: 'Priority' },
    { value: 'parentTask', label: 'Parent task' }
  ];

  constructor() {
    this.i18n.registerMessages(PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES);
    this.route.paramMap.pipe(
      map((params) => params.get('projectId')?.trim() ?? ''),
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((projectId) => {
      this.tab.set('overview');
      this.artifactsOpen.set(false);
      this.configOpen.set(false);
      this.configColumns.set([]);
      this.configSwimlane.set('none');
      this.previousActivationStatus = 'idle';
      this.activationCompletionInterrupted = false;
      this.activationInterruptionFallbackFocused = false;
      this.pendingTaskCreateProjectId.set(null);
      this.taskCreateNavigationInterrupted.set(false);
      if (projectId) {this.facade.load(projectId);}
      else {this.facade.release();}
    });
    this.breakpoints.observe('(max-width: 40rem)')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => this.schedulePresentation.set(result.matches ? 'narrow' : 'desktop'));
    effect(() => {
      const view = this.page();
      const status = view.activation.status;
      const completed = ['success', 'failure', 'conflict', 'uncertain', 'permissionDenied'].includes(status);
      const wasBusy = this.previousActivationStatus === 'submitting' ||
        this.previousActivationStatus === 'reconciling';
      if (
        status === 'idle' &&
        view.status !== 'ready' &&
        (wasBusy || this.previousActivationStatus === 'success')
      ) {
        this.activationCompletionInterrupted = true;
        this.activationInterruptionFallbackFocused = false;
      }
      const outerTerminal = view.status === 'permissionDenied' || view.status === 'error';
      const focusInterruptedFallback = this.activationCompletionInterrupted &&
        outerTerminal &&
        !this.activationInterruptionFallbackFocused;
      const focusCompletion = completed &&
        (wasBusy || this.activationCompletionInterrupted);
      this.previousActivationStatus = status;
      if (focusCompletion || focusInterruptedFallback) {
        if (focusInterruptedFallback)
          {this.activationInterruptionFallbackFocused = true;}
        if (completed) {
          this.activationCompletionInterrupted = false;
          this.activationInterruptionFallbackFocused = false;
        }
        this.tab.set('overview');
        queueMicrotask(() => {
          const focusTarget = this.activationAnnouncement()?.nativeElement ??
            this.pageFocus()?.nativeElement;
          focusTarget?.focus();
        });
      }
    });
    effect(() => {
      const pendingProjectId = this.pendingTaskCreateProjectId();
      if (!pendingProjectId) {
        return;
      }

      const view = this.page();
      const project = view.project;
      if (!project) {
        if (view.status === 'loading') {
          this.taskCreateNavigationInterrupted.set(true);
        } else {
          this.clearPendingTaskCreateNavigation();
        }
        return;
      }

      if (
        project.id !== pendingProjectId ||
        project.isOperational !== true ||
        project.canCreateTask !== true
      ) {
        // A current authoritative Project that no longer matches the pending
        // scope or permission is a denial, not a transient reconnect. Do not
        // resurrect the old click if authorization later changes again.
        this.clearPendingTaskCreateNavigation();
        return;
      }

      if (!this.taskCreateNavigationInterrupted() || this.taskCreateNavigationInFlight()) {
        return;
      }

      this.taskCreateNavigationInterrupted.set(false);
      void this.navigateToTaskCreate(pendingProjectId);
    });
  }

  projectStatusLabel(status: string): string {
    const labels: Record<string, ProjectUiCopy> = {planning: 'Draft', active: 'Active', review: 'Review', atRisk: 'Needs attention', complete: 'Completed', suspended: 'Suspended', archived: 'Archived'};
    return labels[status] ? this.t(labels[status]) : '';
  }
  visibilityLabel(visibility: string | undefined): string {
    const labels: Record<string, ProjectUiCopy> = {workspaceVisible: 'Workspace visible', membersOnly: 'Members only', restricted: 'Restricted'};
    return this.t(labels[visibility ?? ''] ?? 'Visibility unavailable');
  }
  ngOnDestroy(): void { this.facade.release(); }
  openCreateTask(): void {
    const project = this.page().project;
    if (project?.isOperational === true && project.canCreateTask === true) {
      this.pendingTaskCreateProjectId.set(project.id);
      this.taskCreateNavigationInterrupted.set(false);
      void this.navigateToTaskCreate(project.id);
    }
  }
  private clearPendingTaskCreateNavigation(): void {
    this.pendingTaskCreateProjectId.set(null);
    this.taskCreateNavigationInterrupted.set(false);
  }
  private async navigateToTaskCreate(projectId: string): Promise<void> {
    if (this.taskCreateNavigationInFlight()) {
      return;
    }

    this.taskCreateNavigationInFlight.set(true);
    try {
      const navigated = await this.router.navigate(['/projects', projectId, 'tasks', 'new']);
      if (navigated && this.pendingTaskCreateProjectId() === projectId) {
        this.pendingTaskCreateProjectId.set(null);
        this.taskCreateNavigationInterrupted.set(false);
      }
    } catch {
      // A concurrent authorization refresh can supersede navigation. The
      // pending opaque Project id is retried only if the live Project scope is
      // actually interrupted and later reauthorized by the server.
    } finally {
      this.taskCreateNavigationInFlight.set(false);
    }
  }
  openTask(row: TaskGridRow): void { void this.router.navigate(['/projects', row.projectId, 'tasks', row.id]); }
  openKanbanItem(item: object, projectId: string): void {
    const card = item as ProjectKanbanCard;
    if (card.canOpen) {void this.router.navigate(['/projects', projectId, 'tasks', card.taskId]);}
  }
  requestKanbanMove(event: CoglatasKanbanMoveRequest<object>): void {
    this.facade.moveTask(event as CoglatasKanbanMoveRequest<ProjectKanbanCard>);
  }
  setKanbanInteractionActive(active: boolean): void { this.facade.setKanbanInteractionActive(active); }
  retryKanban(): void { this.facade.retryKanban(); }
  retryTaskList(): void { this.facade.retryTaskList(); }
  retrySchedule(): void { this.facade.retrySchedule(); }
  activateProject(): void { this.facade.activate(); }
  retryPreservedScheduleIntent(): void { this.facade.retryPreservedScheduleIntent(); }
  clearPreservedScheduleIntent(): void { this.facade.clearPreservedScheduleIntent(); }
  requestGanttEdit(intent: CoglatasGanttEditIntent): void { this.facade.applyGanttEdit(intent); }
  setGanttInteractionActive(active: boolean): void { this.facade.setScheduleInteractionActive(active); }
  reportGanttFailure(): void { this.facade.reportGanttAdapterFailure(); }
  openGanttItem(item: CoglatasGanttItem, projectId: string): void {
    if (item.kind === 'task' && item.scheduleEditPermissions.canOpen)
      {void this.router.navigate(['/projects', projectId, 'tasks', item.taskId]);}
  }
  setSwimlane(value: string): void { this.facade.setKanbanSwimlane(value as ProjectKanbanSwimlane); }
  setIncludeOlderCompleted(include: boolean): void { this.facade.setIncludeOlderCompleted(include); }

  openConfiguration(snapshot: ProjectKanbanSnapshot): void {
    this.configColumns.set(snapshot.columns.map((column) => ({ ...column })));
    this.configSwimlane.set(snapshot.defaultSwimlane);
    this.configOpen.set(true);
    this.facade.setKanbanInteractionActive(true);
  }
  cancelConfiguration(): void {
    this.configOpen.set(false);
    this.facade.setKanbanInteractionActive(false);
  }
  moveConfigColumn(index: number, offset: -1 | 1): void {
    const columns = [...this.configColumns()];
    const target = index + offset;
    if (target < 0 || target >= columns.length) {return;}
    [columns[index], columns[target]] = [columns[target], columns[index]];
    this.configColumns.set(columns);
  }
  setWipLimit(stageId: string, value: string): void {
    const parsed = value.trim() ? Number(value) : null;
    this.configColumns.update((columns) => columns.map((column) =>
      column.workflowStageId === stageId
        ? { ...column, wipWarningLimit: parsed !== null && Number.isInteger(parsed) && parsed > 0 ? parsed : null }
        : column));
  }
  saveConfiguration(): void {
    this.facade.updateKanbanConfig(this.configSwimlane(), this.configColumns());
    this.configOpen.set(false);
    this.facade.setKanbanInteractionActive(false);
  }

  kanban(snapshot: ProjectKanbanSnapshot, status: ProjectKanbanStatus, feedback: string | null, busyTaskId: string | null, focusTaskId: string | null): CoglatasKanbanContract<ProjectKanbanCard> {
    return {
      ariaLabel: this.t('Canonical Project Task Kanban'),
      presentation: 'desktop',
      state: this.kanbanState(status),
      items: snapshot.cards,
      itemIdentity: (card) => card.taskId,
      itemTitle: (card) => card.summary,
      itemDescription: (card) => this.t('Primary assignee: {{assignee}}. Target group: {{group}}.', {assignee: card.primaryAssigneeLabel, group: card.targetGroupLabel}),
      itemMetadata: (card) => [
        this.t('Priority: {{priority}}', {priority: this.kanbanPriorityLabel(card.priority)}),
        this.t(card.isBlocked ? 'Blocked' : 'Not blocked'),
        card.isParentSummary ? this.t('Derived progress: {{count}}%', {count: this.i18n.formatNumber(card.progressPercent)}) : this.t('Progress: {{count}}%', {count: this.i18n.formatNumber(card.progressPercent)}),
        card.isParentSummary
          ? this.t('Derived dates: {{start}} to {{end}}', {start: card.plannedStartDate ?? this.t('No start date'), end: card.plannedEndDate ?? this.t('No end date')})
          : card.parentSummary ? this.t('Parent: {{title}}', {title: card.parentSummary}) : this.t('Leaf task'),
        card.isParentSummary ? this.t('{{done}} of {{total}} child tasks complete', {done: this.i18n.formatNumber(card.completedChildCount), total: this.i18n.formatNumber(card.childCount)}) : this.t('Actionable card')
      ],
      itemKindLabel: (card) => this.t(card.isParentSummary ? 'Parent summary task' : 'Actionable leaf task'),
      itemStatus: (card) => card.workflowStageId,
      itemOrder: (card) => card.boardOrder,
      itemSwimlane: snapshot.selectedSwimlane === 'none'
        ? undefined
        : (card) => ({ key: card.swimlaneKey, label: card.swimlaneLabel }),
      canOpenItem: (card) => card.canOpen,
      canMoveItem: (card) => card.canMove,
      columns: snapshot.columns.map((column) => ({
        id: column.workflowStageId,
        label: column.displayName,
        category: column.category,
        cardCount: column.currentAuthorizedCardCount,
        wipWarningLimit: column.wipWarningLimit,
        hasWipWarning: column.hasWipWarning,
        requiresReason: column.category === 'cancelled'
      })),
      canRequestTransition: (card, target) => card.allowedTargetWorkflowStageIds.includes(target),
      busyItemId: busyTaskId,
      focusItemId: focusTaskId,
      feedback
    };
  }

  private kanbanPriorityLabel(priority: ProjectKanbanCard['priority']): string {
    const values: Readonly<Record<string, 'low' | 'medium' | 'high' | 'critical' | undefined>> = {
      Low: 'low', Medium: 'medium', High: 'high', Critical: 'critical'
    };
    const value = values[priority];
    return value ? projectPriorityText(this.i18n, value) : priority;
  }

  kanbanState(status: ProjectKanbanStatus): CoglatasAdapterState {
    return status === 'permissionDenied' ? 'permission-denied' :
      status === 'notFound' ? 'error' :
      status === 'disabled' ? 'empty' :
      status;
  }

  gantt(snapshot: ProjectGanttSnapshot, status: ProjectScheduleStatus): CoglatasGanttContract<CoglatasGanttItem> {
    const schedule = this.page().schedule;
    const compatibilityTasks = [...snapshot.scheduledItems, ...snapshot.unscheduledItems]
      .filter((item) => item.kind === 'task');
    const readOnly = !schedule.canonicalEnabled ||
      !(
        snapshot.permissions.canEditSchedule ||
        snapshot.permissions.canEditProgress ||
        snapshot.permissions.canManageDependencies ||
        snapshot.permissions.canClearSchedule
      );
    return {
      ariaLabel: this.t('Canonical Project schedule'),
      presentation: this.schedulePresentation(),
      state: this.ganttState(status),
      tasks: compatibilityTasks,
      taskIdentity: (task) => task.taskId,
      taskLabel: (task) => this.i18n.translate('projects.copy.compatibilityTaskScheduleSummary', {
        title: task.title,
        start: task.plannedStartDate == null ? this.t('No planned start') : this.i18n.formatDateOnly(task.plannedStartDate),
        end: task.plannedEndDate == null ? this.t('No planned end') : this.i18n.formatDateOnly(task.plannedEndDate),
        stage: task.workflowStageName ?? this.t('No Stage'),
        priority: projectPriorityText(this.i18n, task.priority),
        blocked: this.t(task.isBlocked ? 'Blocked' : 'Not blocked')
      }),
      milestones: snapshot.milestones.map((milestone) => ({
        id: milestone.taskId,
        title: milestone.title,
        dueDate: milestone.milestoneDate,
        status: milestone.workflowStageName ?? milestone.stageCategory
      })),
      timezone: snapshot.calendar.timeZone,
      readOnly,
      calendar: schedule.canonicalEnabled ? snapshot.calendar : undefined,
      scheduledItems: schedule.canonicalEnabled ? snapshot.scheduledItems : undefined,
      unscheduledItems: schedule.canonicalEnabled ? snapshot.unscheduledItems : undefined,
      canonicalMilestones: schedule.canonicalEnabled ? snapshot.milestones : undefined,
      dependencies: schedule.canonicalEnabled ? snapshot.dependencies : undefined,
      warnings: schedule.canonicalEnabled ? snapshot.warnings : undefined,
      permissions: schedule.canonicalEnabled ? snapshot.permissions : undefined,
      busyItemId: schedule.busyItemId,
      focusItemId: schedule.focusItemId,
      feedback: schedule.feedback
    };
  }

  ganttState(status: ProjectScheduleStatus): CoglatasAdapterState {
    return status === 'permissionDenied' ? 'permission-denied' : status;
  }
}
