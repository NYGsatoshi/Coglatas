import {
  CoglatasGanttCalendar,
  CoglatasGanttDependency,
  CoglatasGanttDependencyType,
  CoglatasGanttItem,
  CoglatasGanttItemKind,
  CoglatasGanttPermissions,
  CoglatasGanttPriority,
  CoglatasGanttStageCategory,
  CoglatasGanttWarning,
  CoglatasGanttWarningSeverity
} from '../../shared/ui/contracts/coglatas-complex-adapter.contracts';
import {
  ProjectGanttCommandResponseDto,
  ProjectGanttSnapshotDto
} from './projects.api';

export interface ProjectGanttSnapshot {
  readonly projectId: string;
  readonly projectTitle: string;
  readonly projectVersion: number;
  readonly workflowVersion: number;
  readonly calendarVersion: number | null;
  readonly calendar: CoglatasGanttCalendar;
  readonly scheduledItems: readonly CoglatasGanttItem[];
  readonly unscheduledItems: readonly CoglatasGanttItem[];
  readonly milestones: readonly CoglatasGanttItem[];
  readonly dependencies: readonly CoglatasGanttDependency[];
  readonly warnings: readonly CoglatasGanttWarning[];
  readonly permissions: CoglatasGanttPermissions;
  readonly maximumItems: number;
  readonly totalItems: number;
}

export interface ProjectGanttCommandResult {
  readonly taskId: string;
  readonly kind: CoglatasGanttItemKind;
  readonly plannedStartDate: string | null;
  readonly plannedEndDate: string | null;
  readonly milestoneDate: string | null;
  readonly progressPercent: number;
  readonly version: number;
  readonly warnings: readonly CoglatasGanttWarning[];
}

type ItemPlacement = 'scheduled' | 'unscheduled' | 'milestone' | 'command';

export function mapProjectGanttSnapshot(dto: ProjectGanttSnapshotDto): ProjectGanttSnapshot {
  const maximumItems = positiveInteger(read(dto, 'maximumItems'), 'maximumItems');
  const totalItems = nonNegativeInteger(read(dto, 'totalItems'), 'totalItems');
  const permissions = mapPermissions(read(dto, 'permissions'), 'permissions');
  const scheduledItems = requiredArray(read(dto, 'scheduledItems'), 'scheduledItems')
    .map((item, index) => mapItem(item, 'scheduled', `scheduledItems[${index}]`));
  const unscheduledItems = requiredArray(read(dto, 'unscheduledItems'), 'unscheduledItems')
    .map((item, index) => mapItem(item, 'unscheduled', `unscheduledItems[${index}]`));
  const milestones = requiredArray(read(dto, 'milestones'), 'milestones')
    .map((item, index) => mapItem(item, 'milestone', `milestones[${index}]`));
  const allItems = [...scheduledItems, ...unscheduledItems, ...milestones];

  validateBoundedItems(allItems.length, maximumItems, totalItems);
  const itemById = indexItems(allItems);
  validateHierarchy(itemById);
  validateMilestoneReferences(allItems, milestones);
  validateItemPermissions(allItems, itemById, permissions);

  const dependencies = requiredArray(read(dto, 'dependencies'), 'dependencies')
    .map((dependency, index) => mapDependency(dependency, `dependencies[${index}]`));
  validateDependencies(dependencies, itemById, maximumItems);

  return {
    projectId: requiredText(read(dto, 'projectId'), 'projectId'),
    projectTitle: requiredText(read(dto, 'projectTitle'), 'projectTitle'),
    projectVersion: positiveInteger(read(dto, 'projectVersion'), 'projectVersion'),
    workflowVersion: positiveInteger(read(dto, 'workflowVersion'), 'workflowVersion'),
    calendarVersion: nullablePositiveInteger(read(dto, 'calendarVersion'), 'calendarVersion'),
    calendar: mapCalendar(read(dto, 'calendar')),
    scheduledItems,
    unscheduledItems,
    milestones,
    dependencies,
    warnings: requiredArray(read(dto, 'warnings'), 'warnings')
      .map((warning, index) => mapWarning(warning, `warnings[${index}]`)),
    permissions,
    maximumItems,
    totalItems
  };
}

export function mapProjectGanttCommandResponse(dto: ProjectGanttCommandResponseDto): ProjectGanttCommandResult {
  const source = asRecord(dto, 'command');
  const kind = itemKind(source['kind'], 'command.kind');
  const plannedStartDate = nullableDateOnly(source['plannedStartDate'], 'command.plannedStartDate');
  const plannedEndDate = nullableDateOnly(source['plannedEndDate'], 'command.plannedEndDate');
  const milestoneDate = nullableDateOnly(source['milestoneDate'], 'command.milestoneDate');
  const progressPercent = percentage(source['progressPercent'], 'command.progressPercent');
  const warnings = requiredArray(source['warnings'], 'command.warnings')
    .map((warning, index) => mapWarning(warning, `command.warnings[${index}]`));
  validateItemDates(
    { kind, plannedStartDate, plannedEndDate, milestoneDate, warnings },
    'command',
    'command'
  );
  if (kind === 'milestone' && progressPercent !== 0 && progressPercent !== 100) {
    throw invalid('command.progressPercent', 'must be 0 or 100 for a Milestone');
  }

  return {
    taskId: requiredText(source['taskId'], 'command.taskId'),
    kind,
    plannedStartDate,
    plannedEndDate,
    milestoneDate,
    progressPercent,
    version: positiveInteger(source['version'], 'command.version'),
    warnings
  };
}

function mapCalendar(value: unknown): CoglatasGanttCalendar {
  const dto = asRecord(value, 'calendar');
  const workingDays = requiredArray(dto['workingDays'], 'calendar.workingDays')
    .map((day, index) => requiredText(day, `calendar.workingDays[${index}]`));
  const normalizedWorkingDays = new Set(workingDays.map((day) => day.toLowerCase()));
  if (normalizedWorkingDays.size !== workingDays.length) {
    throw invalid('calendar.workingDays', 'contains duplicate days');
  }

  return {
    timeZone: requiredText(dto['timeZone'], 'calendar.timeZone'),
    workingDays,
    holidaysAvailable: requiredBoolean(dto['holidaysAvailable'], 'calendar.holidaysAvailable'),
    limitations: requiredArray(dto['limitations'], 'calendar.limitations')
      .map((limitation, index) => requiredText(limitation, `calendar.limitations[${index}]`))
  };
}

function mapItem(value: unknown, placement: ItemPlacement, field: string): CoglatasGanttItem {
  const dto = asRecord(value, field);
  const kind = itemKind(dto['kind'], `${field}.kind`);
  const plannedStartDate = nullableDateOnly(dto['plannedStartDate'], `${field}.plannedStartDate`);
  const plannedEndDate = nullableDateOnly(dto['plannedEndDate'], `${field}.plannedEndDate`);
  const milestoneDate = nullableDateOnly(dto['milestoneDate'], `${field}.milestoneDate`);
  const progressPercent = percentage(dto['progressPercent'], `${field}.progressPercent`);
  const warnings = requiredArray(dto['warnings'], `${field}.warnings`)
    .map((warning, index) => mapWarning(warning, `${field}.warnings[${index}]`));
  const scheduleEditPermissions = mapPermissions(dto['scheduleEditPermissions'], `${field}.scheduleEditPermissions`);

  validateItemDates(
    { kind, plannedStartDate, plannedEndDate, milestoneDate, warnings },
    placement,
    field
  );

  if (kind === 'milestone' && progressPercent !== 0 && progressPercent !== 100) {
    throw invalid(`${field}.progressPercent`, 'must be 0 or 100 for a Milestone');
  }

  const stageCategory = itemStageCategory(dto['stageCategory'], `${field}.stageCategory`);
  if (stageCategory === 'done' && progressPercent !== 100) {
    throw invalid(`${field}.progressPercent`, 'must be 100 for a Done WorkItem');
  }

  return {
    taskId: requiredText(dto['taskId'], `${field}.taskId`),
    kind,
    parentTaskId: nullableText(dto['parentTaskId'], `${field}.parentTaskId`),
    milestoneId: nullableText(dto['milestoneId'], `${field}.milestoneId`),
    title: requiredText(dto['title'], `${field}.title`),
    plannedStartDate,
    plannedEndDate,
    milestoneDate,
    progressPercent,
    progressIsDerived: requiredBoolean(dto['progressIsDerived'], `${field}.progressIsDerived`),
    workflowStageId: nullableText(dto['workflowStageId'], `${field}.workflowStageId`),
    workflowStageName: nullableText(dto['workflowStageName'], `${field}.workflowStageName`),
    stageCategory,
    priority: itemPriority(dto['priority'], `${field}.priority`),
    isBlocked: requiredBoolean(dto['isBlocked'], `${field}.isBlocked`),
    primaryAssignee: mapAssignee(dto['primaryAssignee'], `${field}.primaryAssignee`),
    version: positiveInteger(dto['version'], `${field}.version`),
    scheduleEditPermissions,
    warnings
  };
}

function validateItemDates(
  item: Pick<CoglatasGanttItem, 'kind' | 'plannedStartDate' | 'plannedEndDate' | 'milestoneDate' | 'warnings'>,
  placement: ItemPlacement,
  field: string
): void {
  if (item.plannedStartDate && item.plannedEndDate && item.plannedEndDate < item.plannedStartDate) {
    throw invalid(field, 'contains a planned end date before its planned start date');
  }

  if (item.kind === 'milestone') {
    if (item.milestoneDate === null && !hasWarning(item.warnings, 'MILESTONE_DATE_REQUIRED')) {
      throw invalid(`${field}.milestoneDate`, 'is required unless the canonical missing-date warning is present');
    }
    if (
      item.milestoneDate !== null &&
      ((item.plannedStartDate !== null && item.plannedStartDate !== item.milestoneDate) ||
        (item.plannedEndDate !== null && item.plannedEndDate !== item.milestoneDate))
    ) {
      throw invalid(field, 'must represent a Milestone as zero-duration');
    }
    if (placement === 'scheduled' || placement === 'unscheduled') {
      throw invalid(`${field}.kind`, 'must be Task outside the milestones collection');
    }
    return;
  }

  if (item.milestoneDate !== null) {
    throw invalid(`${field}.milestoneDate`, 'must be null for a Task');
  }
  if (placement === 'milestone') {
    throw invalid(`${field}.kind`, 'must be Milestone in the milestones collection');
  }
  if (placement === 'scheduled' && item.plannedStartDate === null && item.plannedEndDate === null) {
    throw invalid(field, 'must contain at least one planned date in scheduledItems');
  }
  if (placement === 'unscheduled' && (item.plannedStartDate !== null || item.plannedEndDate !== null)) {
    throw invalid(field, 'must not contain planned dates in unscheduledItems');
  }
  if (placement === 'unscheduled' && !hasWarning(item.warnings, 'UNSCHEDULED')) {
    throw invalid(`${field}.warnings`, 'must include UNSCHEDULED');
  }
}

function mapDependency(value: unknown, field: string): CoglatasGanttDependency {
  const dto = asRecord(value, field);
  const type = dependencyType(dto['type'], `${field}.type`);
  const warnings = requiredArray(dto['warnings'], `${field}.warnings`)
    .map((warning, index) => mapWarning(warning, `${field}.warnings[${index}]`));
  const editable = requiredBoolean(dto['editable'], `${field}.editable`);
  const predecessorTaskId = requiredText(dto['predecessorTaskId'], `${field}.predecessorTaskId`);
  const successorTaskId = requiredText(dto['successorTaskId'], `${field}.successorTaskId`);

  if (predecessorTaskId === successorTaskId) {
    throw invalid(field, 'contains a self dependency');
  }
  if (type !== 'finishToStart' && editable) {
    throw invalid(`${field}.editable`, 'must be false for a legacy non-FS dependency');
  }
  if (type !== 'finishToStart' && !hasWarning(warnings, 'LEGACY_DEPENDENCY_TYPE')) {
    throw invalid(`${field}.warnings`, 'must include LEGACY_DEPENDENCY_TYPE for a non-FS dependency');
  }

  return {
    dependencyId: requiredText(dto['dependencyId'], `${field}.dependencyId`),
    predecessorTaskId,
    successorTaskId,
    type,
    editable,
    version: positiveInteger(dto['version'], `${field}.version`),
    warnings
  };
}

function mapWarning(value: unknown, field: string): CoglatasGanttWarning {
  const dto = asRecord(value, field);
  const blocking = requiredBoolean(dto['blocking'], `${field}.blocking`);
  if (blocking) {
    throw invalid(`${field}.blocking`, 'must be false; blocking failures use the API error envelope');
  }

  return {
    code: requiredText(dto['code'], `${field}.code`),
    message: requiredText(dto['message'], `${field}.message`),
    severity: warningSeverity(dto['severity'], `${field}.severity`),
    targetType: requiredText(dto['targetType'], `${field}.targetType`),
    targetId: nullableText(dto['targetId'], `${field}.targetId`),
    field: nullableText(dto['field'], `${field}.field`),
    blocking: false
  };
}

function mapPermissions(value: unknown, field: string): CoglatasGanttPermissions {
  const dto = asRecord(value, field);
  return {
    canEditSchedule: requiredBoolean(dto['canEditSchedule'], `${field}.canEditSchedule`),
    canEditProgress: requiredBoolean(dto['canEditProgress'], `${field}.canEditProgress`),
    canManageDependencies: requiredBoolean(dto['canManageDependencies'], `${field}.canManageDependencies`),
    canClearSchedule: requiredBoolean(dto['canClearSchedule'], `${field}.canClearSchedule`),
    canOpen: requiredBoolean(dto['canOpen'], `${field}.canOpen`)
  };
}

function mapAssignee(value: unknown, field: string): CoglatasGanttItem['primaryAssignee'] {
  if (value === null) {
    return null;
  }
  const dto = asRecord(value, field);
  return {
    userId: requiredText(dto['userId'], `${field}.userId`),
    displayName: requiredText(dto['displayName'], `${field}.displayName`)
  };
}

function validateBoundedItems(returnedItems: number, maximumItems: number, totalItems: number): void {
  if (returnedItems > maximumItems) {
    throw invalid('maximumItems', 'is smaller than the returned item count');
  }
  if (totalItems !== returnedItems) {
    throw invalid('totalItems', 'must equal the returned item count; truncated Gantt snapshots are not accepted');
  }
}

function indexItems(items: readonly CoglatasGanttItem[]): ReadonlyMap<string, CoglatasGanttItem> {
  const result = new Map<string, CoglatasGanttItem>();
  for (const item of items) {
    if (result.has(item.taskId)) {
      throw invalid('items', `contains duplicate taskId ${item.taskId}`);
    }
    result.set(item.taskId, item);
  }
  return result;
}

function validateHierarchy(itemById: ReadonlyMap<string, CoglatasGanttItem>): void {
  for (const item of itemById.values()) {
    if (!item.parentTaskId) {
      continue;
    }
    const parent = itemById.get(item.parentTaskId);
    if (!parent) {
      throw invalid(`item ${item.taskId}.parentTaskId`, 'references an item outside the authorized snapshot');
    }
    if (parent.kind !== 'task') {
      throw invalid(`item ${item.taskId}.parentTaskId`, 'must reference a Task');
    }
  }

  for (const item of itemById.values()) {
    const visited = new Set<string>([item.taskId]);
    let parentId = item.parentTaskId;
    while (parentId) {
      if (visited.has(parentId)) {
        throw invalid('hierarchy', 'contains a parent cycle');
      }
      visited.add(parentId);
      parentId = itemById.get(parentId)?.parentTaskId ?? null;
    }
  }

  for (const item of itemById.values()) {
    if (item.parentTaskId && itemById.get(item.parentTaskId)?.parentTaskId) {
      throw invalid(`item ${item.taskId}.parentTaskId`, 'exceeds the canonical root-and-child hierarchy depth');
    }
  }
}

function validateMilestoneReferences(items: readonly CoglatasGanttItem[], milestones: readonly CoglatasGanttItem[]): void {
  const milestoneIds = new Set<string>();
  for (const milestone of milestones) {
    milestoneIds.add(milestone.taskId);
    if (milestone.milestoneId) {
      milestoneIds.add(milestone.milestoneId);
    }
  }

  for (const item of items) {
    if (item.kind === 'task' && item.milestoneId && !milestoneIds.has(item.milestoneId)) {
      throw invalid(`item ${item.taskId}.milestoneId`, 'references a Milestone outside the authorized snapshot');
    }
  }
}

function validateItemPermissions(
  items: readonly CoglatasGanttItem[],
  itemById: ReadonlyMap<string, CoglatasGanttItem>,
  snapshotPermissions: CoglatasGanttPermissions
): void {
  const parentIds = new Set(
    items.flatMap((item) => item.parentTaskId ? [item.parentTaskId] : [])
  );

  for (const item of items) {
    const permissions = item.scheduleEditPermissions;
    if (
      (permissions.canEditSchedule && !snapshotPermissions.canEditSchedule) ||
      (permissions.canEditProgress && !snapshotPermissions.canEditProgress) ||
      (permissions.canManageDependencies && !snapshotPermissions.canManageDependencies) ||
      (permissions.canClearSchedule && !snapshotPermissions.canClearSchedule) ||
      (permissions.canOpen && !snapshotPermissions.canOpen)
    ) {
      throw invalid(`item ${item.taskId}.scheduleEditPermissions`, 'exceeds snapshot permissions');
    }

    if (parentIds.has(item.taskId)) {
      if (!item.progressIsDerived) {
        throw invalid(`item ${item.taskId}.progressIsDerived`, 'must be true for a parent Task');
      }
      if (permissions.canEditSchedule || permissions.canEditProgress || permissions.canClearSchedule) {
        throw invalid(`item ${item.taskId}.scheduleEditPermissions`, 'must keep derived parent schedule and progress read-only');
      }
      if (!hasWarning(item.warnings, 'PARENT_DERIVED')) {
        throw invalid(`item ${item.taskId}.warnings`, 'must include PARENT_DERIVED');
      }
    }

    if (item.kind === 'milestone' && permissions.canClearSchedule) {
      throw invalid(`item ${item.taskId}.scheduleEditPermissions.canClearSchedule`, 'must be false for a Milestone');
    }

    if (item.parentTaskId && !itemById.has(item.parentTaskId)) {
      throw invalid(`item ${item.taskId}.parentTaskId`, 'references an unavailable parent');
    }
  }
}

function validateDependencies(
  dependencies: readonly CoglatasGanttDependency[],
  itemById: ReadonlyMap<string, CoglatasGanttItem>,
  maximumItems: number
): void {
  const maximumEdges = maximumItems <= 1 ? 0 : maximumItems * (maximumItems - 1);
  if (!Number.isSafeInteger(maximumEdges) || dependencies.length > maximumEdges) {
    throw invalid('dependencies', 'exceeds the bounded item graph');
  }

  const dependencyIds = new Set<string>();
  const edges = new Set<string>();
  const successors = new Map<string, string[]>();
  const indegree = new Map<string, number>();

  for (const dependency of dependencies) {
    if (dependencyIds.has(dependency.dependencyId)) {
      throw invalid('dependencies', `contains duplicate dependencyId ${dependency.dependencyId}`);
    }
    dependencyIds.add(dependency.dependencyId);

    const predecessor = itemById.get(dependency.predecessorTaskId);
    const successor = itemById.get(dependency.successorTaskId);
    if (!predecessor || !successor || predecessor.kind !== 'task' || successor.kind !== 'task') {
      throw invalid(`dependency ${dependency.dependencyId}`, 'references a Task outside the authorized Project snapshot');
    }

    const edge = `${dependency.predecessorTaskId}\u0000${dependency.successorTaskId}`;
    if (edges.has(edge)) {
      throw invalid('dependencies', 'contains a duplicate predecessor/successor edge');
    }
    edges.add(edge);
    if (dependency.type === 'finishToStart') {
      successors.set(dependency.predecessorTaskId, [...(successors.get(dependency.predecessorTaskId) ?? []), dependency.successorTaskId]);
      indegree.set(dependency.predecessorTaskId, indegree.get(dependency.predecessorTaskId) ?? 0);
      indegree.set(dependency.successorTaskId, (indegree.get(dependency.successorTaskId) ?? 0) + 1);
    }
  }

  const queue = [...indegree.entries()].filter(([, count]) => count === 0).map(([taskId]) => taskId);
  let visited = 0;
  while (queue.length > 0) {
    const taskId = queue.shift();
    if (!taskId) {
      continue;
    }
    visited++;
    for (const successor of successors.get(taskId) ?? []) {
      const remaining = (indegree.get(successor) ?? 0) - 1;
      indegree.set(successor, remaining);
      if (remaining === 0) {
        queue.push(successor);
      }
    }
  }
  if (visited !== indegree.size) {
    throw invalid('dependencies', 'contains a cycle');
  }
}

function hasWarning(warnings: readonly CoglatasGanttWarning[], code: string): boolean {
  return warnings.some((warning) => warning.code === code);
}

function itemKind(value: unknown, field: string): CoglatasGanttItemKind {
  return enumValue(value, field, { task: 'task', milestone: 'milestone' });
}

function itemStageCategory(value: unknown, field: string): CoglatasGanttStageCategory {
  return enumValue(value, field, {
    backlog: 'backlog',
    todo: 'todo',
    inprogress: 'inProgress',
    review: 'review',
    done: 'done',
    cancelled: 'cancelled'
  });
}

function itemPriority(value: unknown, field: string): CoglatasGanttPriority {
  return enumValue(value, field, {
    low: 'low',
    medium: 'medium',
    high: 'high',
    critical: 'critical'
  });
}

function dependencyType(value: unknown, field: string): CoglatasGanttDependencyType {
  return enumValue(value, field, {
    finishtostart: 'finishToStart',
    starttostart: 'startToStart',
    finishtofinish: 'finishToFinish',
    starttofinish: 'startToFinish'
  });
}

function warningSeverity(value: unknown, field: string): CoglatasGanttWarningSeverity {
  return enumValue(value, field, { info: 'info', warning: 'warning' });
}

function enumValue<T extends string>(value: unknown, field: string, values: Readonly<Record<string, T>>): T {
  if (typeof value !== 'string') {
    throw invalid(field, 'must be a string enum value');
  }
  const key = value.replace(/[^a-zA-Z]/g, '').toLowerCase();
  const mapped = values[key];
  if (!mapped) {
    throw invalid(field, 'contains an unsupported enum value');
  }
  return mapped;
}

function nullableDateOnly(value: unknown, field: string): string | null {
  if (value === null) {
    return null;
  }
  if (typeof value !== 'string' || !isIsoDateOnly(value)) {
    throw invalid(field, 'must be an ISO yyyy-MM-dd calendar date or null');
  }
  return value;
}

function isIsoDateOnly(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) {
    return false;
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1) {
    return false;
  }
  const monthLengths = [31, isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day <= monthLengths[month - 1];
}

function isLeapYear(year: number): boolean {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
}

function percentage(value: unknown, field: string): number {
  const result = nonNegativeInteger(value, field);
  if (result > 100) {
    throw invalid(field, 'must be an integer from 0 to 100');
  }
  return result;
}

function positiveInteger(value: unknown, field: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) {
    throw invalid(field, 'must be a positive safe integer');
  }
  return value;
}

function nullablePositiveInteger(value: unknown, field: string): number | null {
  return value === null ? null : positiveInteger(value, field);
}

function nonNegativeInteger(value: unknown, field: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw invalid(field, 'must be a non-negative safe integer');
  }
  return value;
}

function requiredBoolean(value: unknown, field: string): boolean {
  if (typeof value !== 'boolean') {
    throw invalid(field, 'must be a boolean');
  }
  return value;
}

function requiredText(value: unknown, field: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw invalid(field, 'must be a non-empty string');
  }
  return value;
}

function nullableText(value: unknown, field: string): string | null {
  if (value === null) {
    return null;
  }
  return requiredText(value, field);
}

function requiredArray(value: unknown, field: string): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw invalid(field, 'must be an array');
  }
  return value;
}

function read(value: unknown, property: string): unknown {
  return asRecord(value, 'snapshot')[property];
}

function asRecord(value: unknown, field: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw invalid(field, 'must be an object');
  }
  return value as Record<string, unknown>;
}

function invalid(field: string, reason: string): Error {
  return new Error(`Gantt ${field} ${reason}.`);
}
