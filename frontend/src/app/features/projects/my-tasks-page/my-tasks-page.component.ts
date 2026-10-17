import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';
import { projectUiText, projectPriorityText, ProjectUiCopy } from '../projects.i18n';
import { ChangeDetectionStrategy, Component, ElementRef, ViewChild, computed, inject } from '@angular/core';
import { Router } from '@angular/router';

import { AppDataGridActionEvent } from '../../../shared/grid/app-data-grid/app-data-grid.types';
import { AppEmptyStateComponent } from '../../../shared/empty-state/app-empty-state/app-empty-state.component';
import { AppErrorBannerComponent } from '../../../shared/error/app-error-banner/app-error-banner.component';
import { AppInlineLoadingComponent } from '../../../shared/loading/app-inline-loading/app-inline-loading.component';
import { AppPermissionDeniedComponent } from '../../../shared/permission/app-permission-denied/app-permission-denied.component';
import { FrontendFeatureFlagsService } from '../../../core/feature-flags/frontend-feature-flags.service';
import { MyTasksBuiltinFilter, MyTasksFacade } from '../my-tasks.facade';
import {
  MyTasksBlockedFilter,
  MyTasksPriorityFilter,
  MyTasksScope,
  MyTasksStageCategoryFilter,
  MyTasksTab,
  MyTasksUrgencyGroup,
  MyTasksViewModel,
  TaskGridRow
} from '../projects.types';
import { TaskTableComponent } from '../task-table/task-table.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-my-tasks-page', standalone: true,
  imports: [AppEmptyStateComponent, AppErrorBannerComponent, AppInlineLoadingComponent, AppPermissionDeniedComponent, TaskTableComponent],
  templateUrl: './my-tasks-page.component.html', styleUrl: './my-tasks-page.component.scss',
})
export class MyTasksPageComponent {
  readonly i18n = injectProjectsI18n();
  readonly t = (text: ProjectUiCopy, parameters: Readonly<Record<string, string | number>> = {}): string => projectUiText(this.i18n, text, parameters);
  readonly priorityLabel = (priority: 'low' | 'medium' | 'high' | 'urgent' | 'critical'): string => projectPriorityText(this.i18n, priority);
  @ViewChild('savedFilterNameInput') private savedFilterNameInput?: ElementRef<HTMLInputElement>;
  private readonly facade = inject(MyTasksFacade);
  private readonly router = inject(Router);
  private readonly flags = inject(FrontendFeatureFlagsService);
  readonly page = computed(() => this.facade.getMyTasks());
  readonly designSystemV04Enabled = this.flags.designSystemV04Enabled;
  readonly myTasksV1Enabled = this.flags.myTasksV1Enabled;
  readonly tabs: readonly { readonly id: MyTasksTab; readonly label: ProjectUiCopy }[] = [
    { id: 'assigned', label: 'Assigned to Me' }, { id: 'participating', label: 'Participating' }, { id: 'reviews', label: 'Reviews' },
    { id: 'created', label: 'Created by Me' }, { id: 'watching', label: 'Watching' }, { id: 'teamQueue', label: 'Team Queue' }, { id: 'completed', label: 'Completed' }
  ];
  readonly groups: readonly { readonly id: MyTasksUrgencyGroup; readonly label: ProjectUiCopy }[] = [
    { id: 'overdue', label: 'Overdue' }, { id: 'today', label: 'Today' }, { id: 'next7Days', label: 'Next 7 Days' }, { id: 'later', label: 'Later' }, { id: 'noDeadline', label: 'No Deadline' }
  ];
  savedFilterName = '';

  constructor() { this.facade.load(); }
  retry(): void { this.facade.retry(); }
  refresh(): void { this.facade.refresh(); }
  setTab(tab: MyTasksTab): void { this.facade.setTab(tab); }
  setScope(scope: MyTasksScope): void { this.facade.setScope(scope); }
  setWorkspace(workspaceId: string): void { this.facade.setWorkspace(workspaceId); }
  setProjectFilter(value: string): void { this.facade.setProjectFilter(value); }
  setStageCategoryFilter(value: string): void { this.facade.setStageCategoryFilter(value as MyTasksStageCategoryFilter); }
  setPriorityFilter(value: string): void { this.facade.setPriorityFilter(value as MyTasksPriorityFilter); }
  setBlockedFilter(value: string): void { this.facade.setBlockedFilter(value as MyTasksBlockedFilter); }
  setTimeGroupFilter(value: string): void { this.facade.setTimeGroupFilter(value ? value as MyTasksUrgencyGroup : null); }
  setSearchFilter(value: string): void { this.facade.setSearchFilter(value); }
  applyBuiltinFilter(filter: MyTasksBuiltinFilter): void { this.facade.applyBuiltinFilter(filter); }
  setSavedFilterName(value: string): void { this.savedFilterName = value; }
  saveCurrentFilter(event?: Event): void {
    event?.preventDefault();
    if (!this.facade.saveCurrentFilter(this.savedFilterName)) {return;}
    this.savedFilterName = '';
    queueMicrotask(() => this.savedFilterNameInput?.nativeElement.focus());
  }
  applySavedFilter(filterId: string): void { this.facade.applySavedFilter(filterId); }
  deleteSavedFilter(filterId: string): void {
    if (!this.facade.deleteSavedFilter(filterId)) {return;}
    queueMicrotask(() => this.savedFilterNameInput?.nativeElement.focus());
  }
  clearAllFilters(): void { this.facade.clearAllFilters(); }
  previousPage(): void { this.facade.previousPage(); }
  nextPage(): void { this.facade.nextPage(); }
  setPageSize(value: string): void { this.facade.setPageSize(Number(value)); }
  count(vm: MyTasksViewModel, key: string): number { return vm.counts.find((item) => item.key === key)?.count ?? 0; }
  tasksForGroup(vm: MyTasksViewModel, group: MyTasksUrgencyGroup) { return vm.tasks.filter((task) => task.timeGroup === group); }
  filterConditions(vm: MyTasksViewModel) {
    const stage: Record<string, ProjectUiCopy> = { backlog: 'Backlog', todo: 'Todo', inProgress: 'In progress', review: 'Review', done: 'Done', cancelled: 'Cancelled' };
    const priority: Record<string, ProjectUiCopy> = { low: 'Low', medium: 'Medium', high: 'High', critical: 'Critical' };
    return vm.filterConditions.map((condition) => {
      const value = condition.id === 'relationship' ? this.t(this.tabs.find((tab) => tab.id === vm.selectedTab)!.label)
        : condition.id === 'stage' ? this.t(stage[vm.filters.stageCategory])
        : condition.id === 'priority' ? this.t(priority[vm.filters.priority])
        : condition.id === 'urgency' ? this.t(this.groups.find((group) => group.id === vm.filters.timeGroup)!.label)
        : vm.filters.search.trim();
      const labels: Record<string, string> = {
        relationship: this.t('Relationship: {{value}}', { value }),
        project: this.t('Project filter active'), stage: this.t('Stage: {{value}}', { value }),
        priority: this.t('Priority: {{value}}', { value }), urgency: this.t('Urgency: {{value}}', { value }),
        blocked: this.t(vm.filters.blocked === 'true' ? 'Blocked' : 'Not blocked'),
        search: this.t('Search: {{value}}', { value })
      };
      return { ...condition, label: labels[condition.id] ?? condition.label };
    });
  }
  handleTabKeydown(event: KeyboardEvent, index: number): void {
    const keyTargets: Record<string, number> = {
      ArrowLeft: (index - 1 + this.tabs.length) % this.tabs.length,
      ArrowRight: (index + 1) % this.tabs.length,
      Home: 0,
      End: this.tabs.length - 1
    };
    const targetIndex = keyTargets[event.key];
    if (targetIndex === undefined) {return;}
    event.preventDefault();
    this.setTab(this.tabs[targetIndex].id);
    const tabElements = (event.currentTarget as HTMLElement).parentElement?.querySelectorAll<HTMLElement>('[role="tab"]');
    tabElements?.[targetIndex]?.focus();
  }
  handleTaskAction(event: AppDataGridActionEvent<TaskGridRow>): void { if (event.actionId === 'openDetail') {void this.router.navigate(['/projects', event.row.projectId, 'tasks', event.row.id]);} }
}
