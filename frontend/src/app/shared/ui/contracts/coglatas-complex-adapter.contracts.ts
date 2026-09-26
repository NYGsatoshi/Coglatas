export type CoglatasComplexAdapterName =
  | 'data-grid'
  | 'dialog'
  | 'file-uploader'
  | 'date-time-picker'
  | 'kanban'
  | 'gantt'
  | 'tree-grid'
  | 'scheduler';

export type CoglatasAdapterPresentation = 'desktop' | 'narrow';
export type CoglatasAdapterState = 'ready' | 'loading' | 'empty' | 'error' | 'permission-denied' | 'conflict' | 'degraded' | 'rollback';

export interface CoglatasAdapterShellContract {
  readonly ariaLabel: string;
  readonly presentation: CoglatasAdapterPresentation;
  readonly state: CoglatasAdapterState;
}

export interface CoglatasDataGridColumn<TRow> {
  readonly id: string;
  readonly label: string;
  readonly value: (row: TRow) => string;
}

export interface CoglatasDataGridContract<TRow> extends CoglatasAdapterShellContract {
  readonly rows: readonly TRow[];
  readonly columns: readonly CoglatasDataGridColumn<TRow>[];
  readonly rowIdentity: (row: TRow) => string;
  readonly page: number;
  readonly pageSize: number;
}

export interface CoglatasDialogContract extends CoglatasAdapterShellContract {
  readonly title: string;
  readonly description?: string;
  readonly confirmLabel?: string;
  readonly cancelLabel?: string;
  readonly closeOnEscape: boolean;
  readonly destructive: boolean;
  readonly busy?: boolean;
}

export interface CoglatasFileUploadItem {
  readonly clientRequestId: string;
  readonly fileName: string;
  readonly state: 'pending' | 'uploading' | 'succeeded' | 'failed' | 'cancelled';
}

export interface CoglatasFileUploaderContract extends CoglatasAdapterShellContract {
  readonly files: readonly CoglatasFileUploadItem[];
  readonly multiple: boolean;
  /** Client policy is intentionally absent: the backend owns file validation. */
  readonly disabled?: boolean;
}

export interface CoglatasDateTimePickerContract extends CoglatasAdapterShellContract {
  readonly value: string | null;
  readonly timezone: string;
  readonly readOnly: boolean;
}

export interface CoglatasKanbanContract<TItem> extends CoglatasAdapterShellContract {
  readonly items: readonly TItem[];
  readonly itemIdentity: (item: TItem) => string;
  readonly columns: readonly CoglatasKanbanColumn[];
  readonly itemTitle: (item: TItem) => string;
  readonly itemStatus: (item: TItem) => string;
  readonly itemOrder: (item: TItem) => number;
  readonly itemDescription?: (item: TItem) => string;
  readonly itemMetadata?: (item: TItem) => readonly string[];
  readonly itemKindLabel?: (item: TItem) => string;
  /** Optional presentation grouping; it never rewrites item ownership or state. */
  readonly itemSwimlane?: (item: TItem) => { readonly key: string; readonly label: string };
  readonly canOpenItem: (item: TItem) => boolean;
  readonly canMoveItem: (item: TItem) => boolean;
  /** Command proposals are enabled only when the backend exposes permission. */
  readonly canRequestTransition: (item: TItem, targetStatus: string) => boolean;
  readonly busyItemId?: string | null;
  readonly focusItemId?: string | null;
  readonly feedback?: string | null;
}

export interface CoglatasKanbanColumn {
  readonly id: string;
  readonly label: string;
  readonly category: string;
  readonly cardCount: number;
  readonly wipWarningLimit: number | null;
  readonly hasWipWarning: boolean;
  readonly requiresReason?: boolean;
}

export interface CoglatasKanbanMoveRequest<TItem> {
  readonly item: TItem;
  readonly targetStatus: string;
  /** A null before/after pair is the canonical end-of-Stage intent. */
  readonly targetBeforeItemId: string | null;
  readonly targetAfterItemId: string | null;
  readonly reason: string | null;
  readonly source: 'drag' | 'keyboard';
}

/** Calendar dates are ISO `yyyy-MM-dd` values and are never browser-local timestamps. */
export type CoglatasGanttDateOnly = string;
export type CoglatasGanttItemKind = 'task' | 'milestone';
export type CoglatasGanttStageCategory = 'backlog' | 'todo' | 'inProgress' | 'review' | 'done' | 'cancelled';
export type CoglatasGanttPriority = 'low' | 'medium' | 'high' | 'critical';
export type CoglatasGanttDependencyType = 'finishToStart' | 'startToStart' | 'finishToFinish' | 'startToFinish';
export type CoglatasGanttWarningSeverity = 'info' | 'warning';
export type CoglatasGanttEditSource = 'pointer' | 'keyboard' | 'form';

export interface CoglatasGanttWarning {
  readonly code: string;
  readonly message: string;
  readonly severity: CoglatasGanttWarningSeverity;
  readonly targetType: string;
  readonly targetId: string | null;
  readonly field: string | null;
  /** Gantt projection warnings are informational; blocking failures use the API error contract. */
  readonly blocking: false;
}

export interface CoglatasGanttPermissions {
  readonly canEditSchedule: boolean;
  readonly canEditProgress: boolean;
  readonly canManageDependencies: boolean;
  readonly canClearSchedule: boolean;
  readonly canOpen: boolean;
}

export interface CoglatasGanttAssignee {
  readonly userId: string;
  readonly displayName: string;
}

export interface CoglatasGanttCalendar {
  readonly timeZone: string;
  readonly workingDays: readonly string[];
  readonly holidaysAvailable: boolean;
  readonly limitations: readonly string[];
}

export interface CoglatasGanttItem {
  readonly taskId: string;
  readonly kind: CoglatasGanttItemKind;
  readonly parentTaskId: string | null;
  readonly milestoneId: string | null;
  readonly title: string;
  readonly plannedStartDate: CoglatasGanttDateOnly | null;
  readonly plannedEndDate: CoglatasGanttDateOnly | null;
  readonly milestoneDate: CoglatasGanttDateOnly | null;
  readonly progressPercent: number;
  readonly progressIsDerived: boolean;
  readonly workflowStageId: string | null;
  readonly workflowStageName: string | null;
  readonly stageCategory: CoglatasGanttStageCategory;
  readonly priority: CoglatasGanttPriority;
  readonly isBlocked: boolean;
  readonly primaryAssignee: CoglatasGanttAssignee | null;
  readonly version: number;
  readonly scheduleEditPermissions: CoglatasGanttPermissions;
  readonly warnings: readonly CoglatasGanttWarning[];
}

export interface CoglatasGanttDependency {
  readonly dependencyId: string;
  readonly predecessorTaskId: string;
  readonly successorTaskId: string;
  readonly type: CoglatasGanttDependencyType;
  readonly editable: boolean;
  readonly version: number;
  readonly warnings: readonly CoglatasGanttWarning[];
}

export interface CoglatasGanttScheduleEditIntent {
  readonly kind: 'schedule';
  readonly taskId: string;
  readonly plannedStartDate: CoglatasGanttDateOnly | null;
  readonly plannedEndDate: CoglatasGanttDateOnly | null;
  readonly milestoneDate: CoglatasGanttDateOnly | null;
  readonly expectedVersion: number;
  readonly source: CoglatasGanttEditSource;
}

export interface CoglatasGanttProgressEditIntent {
  readonly kind: 'progress';
  readonly taskId: string;
  readonly progressPercent: number;
  readonly expectedVersion: number;
  readonly source: CoglatasGanttEditSource;
}

export interface CoglatasGanttAddDependencyEditIntent {
  readonly kind: 'addDependency';
  readonly predecessorTaskId: string;
  readonly successorTaskId: string;
  readonly type: 'finishToStart';
  readonly expectedVersion: number;
  readonly source: CoglatasGanttEditSource;
}

export interface CoglatasGanttRemoveDependencyEditIntent {
  readonly kind: 'removeDependency';
  readonly dependencyId: string;
  readonly successorTaskId: string;
  readonly expectedVersion: number;
  readonly source: CoglatasGanttEditSource;
}

export type CoglatasGanttEditIntent =
  | CoglatasGanttScheduleEditIntent
  | CoglatasGanttProgressEditIntent
  | CoglatasGanttAddDependencyEditIntent
  | CoglatasGanttRemoveDependencyEditIntent;

export interface CoglatasGanttEditResult {
  readonly item: CoglatasGanttItem | null;
  readonly dependency: CoglatasGanttDependency | null;
  readonly removedDependencyId: string | null;
  readonly version: number;
  readonly warnings: readonly CoglatasGanttWarning[];
}

export interface CoglatasGanttContract<TTask = CoglatasGanttItem> extends CoglatasAdapterShellContract {
  /**
   * Compatibility projection used until the existing read-only Schedule tab is
   * switched to the canonical collections below.
   */
  readonly tasks: readonly TTask[];
  readonly taskIdentity: (task: TTask) => string;
  readonly taskLabel: (task: TTask) => string;
  readonly milestones: readonly CoglatasGanttMilestone[];
  readonly timezone: string;
  readonly readOnly: boolean;
  readonly calendar?: CoglatasGanttCalendar;
  readonly scheduledItems?: readonly CoglatasGanttItem[];
  readonly unscheduledItems?: readonly CoglatasGanttItem[];
  readonly canonicalMilestones?: readonly CoglatasGanttItem[];
  readonly dependencies?: readonly CoglatasGanttDependency[];
  readonly warnings?: readonly CoglatasGanttWarning[];
  readonly permissions?: CoglatasGanttPermissions;
  readonly busyItemId?: string | null;
  readonly focusItemId?: string | null;
  readonly feedback?: string | null;
  readonly requestEdit?: (intent: CoglatasGanttEditIntent) => void;
}

export interface CoglatasGanttMilestone {
  readonly id: string;
  readonly title: string;
  readonly dueDate: string | null;
  readonly status: string;
}

export interface CoglatasTreeGridContract<TItem> extends CoglatasAdapterShellContract {
  readonly items: readonly TItem[];
  readonly itemIdentity: (item: TItem) => string;
}

export interface CoglatasSchedulerContract<TItem> extends CoglatasAdapterShellContract {
  readonly items: readonly TItem[];
  readonly timezone: string;
}
