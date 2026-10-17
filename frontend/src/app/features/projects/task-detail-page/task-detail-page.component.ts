import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';
import { projectUiText, projectPriorityText, ProjectUiCopy } from '../projects.i18n';
import { ChangeDetectionStrategy, Component, OnDestroy, Signal, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';

import { AppEmptyStateComponent } from '../../../shared/empty-state/app-empty-state/app-empty-state.component';
import { AppInlineLoadingComponent } from '../../../shared/loading/app-inline-loading/app-inline-loading.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { ProjectsFacade } from '../projects.facade';
import { TASK_LABEL_DESCRIPTION_MAX_LENGTH, TASK_LABEL_NAME_MAX_LENGTH, TaskActivityLogType, TaskDetailSection, TaskDetailSectionState, TaskEditorSaveRequest, TaskStageCategory, TaskStatus } from '../projects.types';
import { TaskDependenciesReadonlyComponent } from '../task-dependencies-readonly/task-dependencies-readonly.component';
import { TaskEditorComponent } from '../task-editor/task-editor.component';
import { TaskExecutionScopeComponent } from '../task-execution-scope/task-execution-scope.component';
import { TaskResearchPlanComponent } from '../task-research-plan/task-research-plan.component';
import { TaskStatusBadgeComponent } from '../task-status-badge/task-status-badge.component';
import { AppMentionInputComponent } from '../../../shared/mention-input/app-mention-input.component';
import { FilesFacade } from '../../files/files.facade';
import { AttachmentPickerDialogComponent } from '../../files/attachment-picker-dialog/attachment-picker-dialog.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-detail-page',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule,
    AppEmptyStateComponent,
    AppInlineLoadingComponent,
    AppPermissionDeniedComponent,
    TaskDependenciesReadonlyComponent,
    TaskEditorComponent,
    TaskExecutionScopeComponent,
    TaskResearchPlanComponent,
    TaskStatusBadgeComponent,
    AppMentionInputComponent,
    AttachmentPickerDialogComponent
  ],
  templateUrl: './task-detail-page.component.html',
  styleUrl: './task-detail-page.component.scss',
})
export class TaskDetailPageComponent implements OnDestroy {
  readonly i18n = injectProjectsI18n();
  readonly t = (text: ProjectUiCopy, parameters: Readonly<Record<string, string | number>> = {}): string => projectUiText(this.i18n, text, parameters);
  readonly priorityLabel = (priority: 'low' | 'medium' | 'high' | 'urgent' | 'critical'): string => projectPriorityText(this.i18n, priority);
  private readonly facade = inject(ProjectsFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly files = inject(FilesFacade);
  private routeSubscription: Subscription | null = null;
  private readonly projectId = signal<string | undefined>(undefined);
  private readonly taskId = signal<string | undefined>(undefined);
  private readonly activityRequested = signal(false);

  readonly page = computed(() =>
    this.facade.getTaskDetail(
      this.projectId(),
      this.taskId()
    )
  );
  readonly mutationState = computed(() => this.facade.getTaskMutationState());
  readonly conflictReloadState = computed(() => this.facade.getTaskConflictReloadState());
  private readonly sectionStateSignals: Record<TaskDetailSection, Signal<TaskDetailSectionState>> = {
    detail: computed(() => this.facade.getDetailSectionState('detail')),
    activity: computed(() => this.facade.getDetailSectionState('activity')),
    subtasks: computed(() => this.facade.getDetailSectionState('subtasks')),
    checklist: computed(() => this.facade.getDetailSectionState('checklist')),
    comments: computed(() => this.facade.getDetailSectionState('comments')),
    labels: computed(() => this.facade.getDetailSectionState('labels')),
    watch: computed(() => this.facade.getDetailSectionState('watch')),
    files: computed(() => this.facade.getDetailSectionState('files'))
  };
  readonly sectionState = (section: TaskDetailSection) => this.sectionStateSignals[section];
  readonly filePickerState = this.files.pickerStateForTask;
  readonly checklistText = signal('');
  readonly commentBody = signal('');
  readonly commentImportant = signal(false);
  readonly editingCommentId = signal<string | null>(null);
  readonly editingCommentBody = signal('');
  readonly editingCommentImportant = signal(false);
  private readonly originalCommentBody = signal('');
  private readonly originalCommentImportant = signal(false);
  readonly editingChecklistId = signal<string | null>(null);
  readonly editingChecklistText = signal('');
  private readonly originalChecklistText = signal('');
  readonly selectedLabelId = signal('');
  readonly newLabelName = signal('');
  readonly editingLabelId = signal<string | null>(null);
  readonly editingLabelName = signal('');
  readonly editingLabelDescription = signal('');
  readonly editingLabelSortKey = signal('');
  private readonly originalLabelName = signal('');
  private readonly originalLabelDescription = signal('');
  private readonly originalLabelSortKey = signal('');
  readonly selectedAttachmentId = signal('');
  readonly fileDownloadMessage = signal('');
  readonly subtaskTitle = signal('');
  readonly taskEditorDirty = signal(false);
  readonly researchPlanDirty = signal(false);
  readonly labelNameMaxLength = TASK_LABEL_NAME_MAX_LENGTH;
  readonly labelDescriptionMaxLength = TASK_LABEL_DESCRIPTION_MAX_LENGTH;
  /** One local source of truth for realtime protection; focus alone never makes this true. */
  readonly detailEditing = computed(() =>
    this.taskEditorDirty() ||
    this.researchPlanDirty() ||
    this.subtaskTitle().trim().length > 0 ||
    this.checklistText().trim().length > 0 ||
    (this.editingChecklistId() !== null && this.editingChecklistText().trim() !== this.originalChecklistText().trim()) ||
    this.commentBody().trim().length > 0 ||
    (this.editingCommentId() !== null && (this.editingCommentBody().trim() !== this.originalCommentBody().trim() || this.editingCommentImportant() !== this.originalCommentImportant())) ||
    this.selectedLabelId().length > 0 || this.newLabelName().trim().length > 0 ||
    (this.editingLabelId() !== null && (this.editingLabelName().trim() !== this.originalLabelName().trim() || this.editingLabelDescription().trim() !== this.originalLabelDescription().trim() || this.editingLabelSortKey() !== this.originalLabelSortKey())) ||
    this.selectedAttachmentId().length > 0 ||
    Object.values(this.sectionStateSignals).some(state => state().status === 'submitting')
  );
  private lastRouteKey: string | null = null;

  constructor() {
    effect(() => this.facade.setDetailEditing(this.detailEditing()));
    // Test/story doubles from before the Task-body/subresource split may not expose
    // this additive facade method yet.
    effect(() => (this.facade as unknown as { setTaskBodyEditing?: (editing: boolean) => void }).setTaskBodyEditing?.(this.taskEditorDirty()));
    // Authorization loss intentionally clears all protected Task projections and
    // invalidates in-flight Activity responses. Preserve only the local fact that
    // this mounted route requested Activity, then re-read page one after the same
    // Task is authoritatively rehydrated under the new authorization generation.
    effect(() => {
      const requested = this.activityRequested();
      const taskId = this.taskId();
      const vm = this.page();
      const activityState = this.sectionState('activity')().status;
      if (!requested || !taskId || !vm.task || !vm.detail || vm.task.id !== taskId || activityState !== 'idle') {return;}
      this.facade.loadActivity(taskId);
    });
    this.routeSubscription = this.route.paramMap.subscribe((params) => {
      const projectId = params.get('projectId') ?? undefined;
      const taskId = params.get('taskId') ?? undefined;
      const routeKey = `${projectId ?? ''}/${taskId ?? ''}`;
      if (this.lastRouteKey !== null && this.lastRouteKey !== routeKey) {this.resetLocalTaskDraftState();}
      this.lastRouteKey = routeKey;
      this.projectId.set(projectId);
      this.taskId.set(taskId);
      this.facade.ensureTaskDetail(projectId, taskId);
    });
  }

  ngOnDestroy(): void {
    this.resetLocalTaskDraftState();
    this.files.cancelAttachmentDownloads();
    this.routeSubscription?.unsubscribe();
    this.facade.releaseTaskDetail();
  }

  saveTask(request: TaskEditorSaveRequest): void {
    const vm = this.page();
    if (!vm.task) {
      return;
    }

    this.facade.saveTask(vm.task.id, vm.task.projectId, request);
  }

  resetMutationState(): void {
    this.facade.clearTaskMutationState();
  }

  reloadTaskAfterConflict(): void {
    const taskId = this.taskId();
    if (taskId) {this.facade.reloadTaskAfterConflict(taskId);}
  }

  retry(): void { const taskId = this.taskId(); if (taskId) {this.facade.retryTaskDetail(taskId);} }
  retrySection(section: TaskDetailSection): void { const taskId = this.taskId(); if (taskId) {this.facade.retrySection(taskId, section);} }
  /**
   * Use the bubbling interaction path rather than relying on the native
   * non-bubbling `toggle` event to reach Angular's component listener.
   */
  loadActivity(): void {
    const taskId = this.taskId();
    if (!taskId) {return;}
    this.activityRequested.set(true);
    this.facade.loadActivity(taskId);
  }
  loadMore(section: 'activity' | 'subtasks' | 'comments' | 'files'): void { const taskId = this.taskId(); if (!taskId) {return;} if (section === 'activity') {this.facade.loadMoreActivity(taskId);} else if (section === 'subtasks') {this.facade.loadMoreSubtasks(taskId);} else if (section === 'comments') {this.facade.loadMoreComments(taskId);} else {this.facade.loadMoreFiles(taskId);} }
  phaseStateLabel(category: TaskStageCategory | undefined, status: TaskStatus, isBlocked: boolean | undefined): string {
    if (isBlocked) {return this.t('Blocked');}
    switch (category) {
      case 'backlog':
      case 'todo': return this.t('Waiting');
      case 'inProgress': return this.t('Running');
      case 'review': return this.t('Needs review');
      case 'done': return this.t('Completed');
      case 'cancelled': return this.t('Cancelled');
      default: return status === 'inProgress' ? this.t('Running') : status === 'review' ? this.t('Needs review') : status === 'done' ? this.t('Completed') : status === 'cancelled' ? this.t('Cancelled') : status === 'blocked' ? this.t('Blocked') : this.t('Waiting');
    }
  }
  activityTypeLabel(type: TaskActivityLogType): string {
    switch (type) {
      case 'statusUpdate': return this.t('Status update');
      case 'decision': return this.t('Decision');
      case 'issue': return this.t('Needs attention');
      case 'note': return this.t('Activity');
      default: return this.t('Recorded activity');
    }
  }
  activityHasConfirmedEmptyState(): boolean { return this.sectionState('activity')().status === 'empty'; }
  createSubtask(): void { const vm = this.page(); const title = this.subtaskTitle().trim(); if (vm.task && vm.detail?.permissions.canCreateSubtask && title.length <= 300) { this.facade.createSubtask(vm.task.id, title, () => this.subtaskTitle.set('')); } }
  createChecklist(): void { const vm = this.page(); const text = this.checklistText().trim(); if (vm.task && text && text.length <= 1000 && vm.detail?.permissions.canCreateChecklistItem) { this.facade.createChecklist(vm.task.id, text, () => this.checklistText.set('')); } }
  toggleChecklist(item: { id: string; text: string; isCompleted: boolean; version: string }): void { const vm = this.page(); if (vm.task) {this.facade.updateChecklist(vm.task.id, item.id, item.text, !item.isCompleted, item.version);} }
  editChecklist(item: { id: string; text: string }): void { this.editingChecklistId.set(item.id); this.editingChecklistText.set(item.text); this.originalChecklistText.set(item.text); }
  cancelEditChecklist(): void { this.editingChecklistId.set(null); this.editingChecklistText.set(''); this.originalChecklistText.set(''); }
  saveChecklist(item: { id: string; isCompleted: boolean; version: string }): void { const vm = this.page(); const text = this.editingChecklistText().trim(); if (vm.task && text && text.length <= 1000) {this.facade.updateChecklist(vm.task.id, item.id, text, item.isCompleted, item.version, () => this.cancelEditChecklist());} }
  deleteChecklist(item: { id: string; version: string }): void { const vm = this.page(); if (vm.task) {this.facade.deleteChecklist(vm.task.id, item.id, item.version);} }
  moveChecklist(itemId: string, direction: -1 | 1): void { const vm = this.page(); if (!vm.task || !vm.detail) {return;} const ids = vm.detail.checklist.map(item => item.id); const from = ids.indexOf(itemId); const to = from + direction; if (from < 0 || to < 0 || to >= ids.length) {return;} [ids[from], ids[to]] = [ids[to], ids[from]]; this.facade.reorderChecklist(vm.task.id, ids, vm.detail.taskVersion); }
  postComment(): void { const vm = this.page(); const body = this.commentBody().trim(); if (vm.task && body && body.length <= 12000 && vm.detail?.permissions.canCreateComment) { this.facade.createComment(vm.task.id, body, this.commentImportant(), () => { this.commentBody.set(''); this.commentImportant.set(false); }); } }
  editComment(comment: { id: string; body: string | null; isImportant: boolean }): void { if (comment.body !== null) { this.editingCommentId.set(comment.id); this.editingCommentBody.set(comment.body); this.editingCommentImportant.set(comment.isImportant); this.originalCommentBody.set(comment.body); this.originalCommentImportant.set(comment.isImportant); } }
  cancelEditComment(): void { this.editingCommentId.set(null); this.editingCommentBody.set(''); this.editingCommentImportant.set(false); this.originalCommentBody.set(''); this.originalCommentImportant.set(false); }
  saveComment(comment: { id: string; version: string }): void { const vm = this.page(); const body = this.editingCommentBody().trim(); if (vm.task && body && body.length <= 12000) {this.facade.updateComment(vm.task.id, comment.id, body, this.editingCommentImportant(), comment.version, () => this.cancelEditComment());} }
  toggleImportant(comment: { id: string; body: string | null; isImportant: boolean; version: string }): void { const vm = this.page(); if (vm.task && comment.body !== null) {this.facade.updateComment(vm.task.id, comment.id, comment.body, !comment.isImportant, comment.version);} }
  deleteComment(comment: { id: string; version: string }): void { const vm = this.page(); if (vm.task) {this.facade.deleteComment(vm.task.id, comment.id, comment.version);} }
  toggleWatch(): void { const vm = this.page(); if (vm.task && vm.detail?.permissions.canChangeWatch) {this.facade.setWatch(vm.task.id, !vm.detail.watchState.isWatching, vm.detail.watchState.version);} }
  removeLabel(labelId: string): void { const vm = this.page(); if (vm.task && vm.detail) {this.facade.removeLabel(vm.task.id, labelId, vm.detail.taskVersion);} }
  applyLabel(): void { const vm = this.page(); const labelId = this.selectedLabelId(); if (vm.task && labelId && vm.detail?.permissions.canApplyLabels) {this.facade.applyLabel(vm.task.id, labelId, vm.detail.taskVersion, () => this.selectedLabelId.set(''));} }
  refreshLabelDefinitions(): void { const vm = this.page(); if (vm.task) {this.facade.loadProjectLabelDefinitions(vm.task.projectId, true);} }
  createLabel(): void { const vm = this.page(); if (vm.task && this.newLabelName().trim().length > 0 && this.newLabelName().trim().length <= TASK_LABEL_NAME_MAX_LENGTH && vm.detail?.permissions.canManageLabelDefinitions) {this.facade.createProjectLabel(vm.task.id, vm.task.projectId, this.newLabelName(), () => this.newLabelName.set(''));} }
  editLabel(label: { id: string; name: string; description: string | null; sortKey: string }): void { this.editingLabelId.set(label.id); this.editingLabelName.set(label.name); this.editingLabelDescription.set(label.description ?? ''); this.editingLabelSortKey.set(label.sortKey); this.originalLabelName.set(label.name); this.originalLabelDescription.set(label.description ?? ''); this.originalLabelSortKey.set(label.sortKey); }
  cancelEditLabel(): void { this.editingLabelId.set(null); this.editingLabelName.set(''); this.editingLabelDescription.set(''); this.editingLabelSortKey.set(''); this.originalLabelName.set(''); this.originalLabelDescription.set(''); this.originalLabelSortKey.set(''); }
  saveLabel(label: { id: string; version: string }): void { const vm = this.page(); const sortKey = Number(this.editingLabelSortKey()); if (vm.task && this.editingLabelName().trim() && this.editingLabelName().trim().length <= TASK_LABEL_NAME_MAX_LENGTH && this.editingLabelDescription().trim().length <= TASK_LABEL_DESCRIPTION_MAX_LENGTH && Number.isSafeInteger(sortKey) && sortKey >= 0) {this.facade.updateProjectLabel(vm.task.id, vm.task.projectId, label.id, this.editingLabelName(), this.editingLabelDescription(), this.editingLabelSortKey(), label.version, () => this.cancelEditLabel());} }
  setLabelArchived(label: { id: string; version: string }, archived: boolean): void { const vm = this.page(); if (vm.task) {this.facade.setProjectLabelArchived(vm.task.id, vm.task.projectId, label.id, label.version, archived);} }
  checklistCompletedCount(): number { return this.page().detail?.checklist.filter((item) => item.isCompleted).length ?? 0; }
  isAppliedLabel(): boolean { const detail = this.page().detail; return !!detail?.labels.some((label) => label.id === this.selectedLabelId()); }
  removeFile(associationId: string): void { const vm = this.page(); if (vm.task && vm.detail) {this.facade.removeFile(vm.task.id, associationId, vm.detail.taskVersion);} }
  setSelectedAttachment(id: string | null): void { this.selectedAttachmentId.set(id ?? ''); }
  associateSelectedFile(): void { const vm = this.page(); const attachmentId = this.selectedAttachmentId(); if (vm.task && vm.detail && attachmentId) {this.facade.associateFile(vm.task.id, attachmentId, vm.detail.taskVersion, () => this.selectedAttachmentId.set(''));} }
  loadPickerFiles(): void { const vm = this.page(); if (vm.detail?.permissions.canAssociateFiles && vm.detail.workspaceId) {this.files.loadPickerFilesForWorkspace(vm.detail.workspaceId);} }
  downloadFile(attachmentId: string, fileObjectId: string, fileName: string): void { const taskId = this.taskId(); const projectId = this.projectId(); const workspaceId = this.page().detail?.workspaceId; if (!taskId || !workspaceId) {return;} this.files.downloadAttachment(attachmentId, fileName, { workspaceId, fileObjectId, isCurrent: () => this.taskId() === taskId && this.projectId() === projectId, onState: (_, message) => this.fileDownloadMessage.set(message), onPermissionDenied: () => this.facade.retryTaskDetail(taskId) }); }
  loadMorePickerFiles(): void { this.files.loadMorePickerFiles(); }
  retryPickerFiles(): void { this.files.retryPickerFiles(); }

  taskFileMetadataLabel(file: { readonly contentType: string; readonly sizeBytes: number }): string {
    return this.i18n.translate('files.task.fileMetadata', {
      type: file.contentType
        ? this.i18n.fileContentTypeLabel(file.contentType)
        : this.i18n.translate('files.details.unavailable'),
      size: this.i18n.formatFileSize(file.sizeBytes)
    });
  }

  taskFileScanLabel(scanStatus: string): string {
    return this.i18n.translate('files.task.scan', { status: this.i18n.taskFileScanStatusLabel(scanStatus) });
  }

  taskFileAccessLabel(accessState: string): string {
    return this.i18n.translate('files.task.access', { status: this.i18n.taskFileAccessStateLabel(accessState) });
  }

  taskFileRestrictionLabel(restrictionCode: string | null): string | null {
    return this.i18n.taskFileRestrictionLabel(restrictionCode);
  }

  taskFileSectionMessage(state: TaskDetailSectionState): string {
    if (this.i18n.locale() === 'en') {return state.message ?? this.i18n.translate('files.task.error');}
    if (state.status === 'permissionDenied') {return this.i18n.translate('api.permissionDenied');}
    if (state.status === 'conflict') {return this.i18n.translate('api.conflict');}
    return this.i18n.translate('files.task.error');
  }

  taskFilePickerMessage(): string {
    const state = this.filePickerState();
    if (this.i18n.locale() === 'en') {return state.message ?? this.i18n.translate('files.task.error');}
    return state.status === 'permissionDenied'
      ? this.i18n.translate('api.permissionDenied')
      : this.i18n.translate('files.task.error');
  }

  private resetLocalTaskDraftState(): void {
    this.activityRequested.set(false);
    this.subtaskTitle.set(''); this.checklistText.set(''); this.editingChecklistId.set(null); this.editingChecklistText.set(''); this.originalChecklistText.set(''); this.taskEditorDirty.set(false); this.researchPlanDirty.set(false);
    this.commentBody.set(''); this.commentImportant.set(false); this.cancelEditComment();
    this.selectedLabelId.set(''); this.newLabelName.set(''); this.cancelEditLabel(); this.selectedAttachmentId.set(''); this.fileDownloadMessage.set('');
    this.files.cancelAttachmentDownloads(); this.files.clearPickerFiles(); this.facade.setDetailEditing(false);
  }
}
