import { TestBed } from '@angular/core/testing';

import {
  CoglatasGanttContract,
  CoglatasGanttItem
} from '../../contracts/coglatas-complex-adapter.contracts';
import { CoglatasGanttComponent } from './coglatas-adapter-shells.components';

describe('CoglatasGanttComponent vendor eligibility', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CoglatasGanttComponent] }).compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('keeps the desktop visual timeline eligible for an empty canonical snapshot', () => {
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'desktop';
    fixture.componentInstance.state = 'empty';
    fixture.componentInstance.contract = ganttContract();

    expect(fixture.componentInstance.vendorEligible).toBe(true);
  });

  it('keeps the desktop visual timeline eligible when every Task is unscheduled', () => {
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'desktop';
    fixture.componentInstance.state = 'ready';
    fixture.componentInstance.contract = ganttContract([unscheduledTask()]);

    expect(fixture.componentInstance.vendorEligible).toBe(true);
  });

  it('does not load the visual timeline for narrow presentation', () => {
    const fixture = TestBed.createComponent(CoglatasGanttComponent);
    fixture.componentInstance.presentation = 'narrow';
    fixture.componentInstance.state = 'ready';
    fixture.componentInstance.contract = ganttContract([unscheduledTask()]);

    expect(fixture.componentInstance.vendorEligible).toBe(false);
  });
});

function ganttContract(unscheduledItems: readonly CoglatasGanttItem[] = []): CoglatasGanttContract<object> {
  return {
    ariaLabel: 'Canonical schedule',
    presentation: 'desktop',
    state: 'ready',
    tasks: [],
    taskIdentity: () => '',
    taskLabel: () => '',
    milestones: [],
    timezone: 'Asia/Tokyo',
    readOnly: true,
    scheduledItems: [],
    unscheduledItems,
    canonicalMilestones: [],
    dependencies: [],
    warnings: [],
    permissions: {
      canEditSchedule: false,
      canEditProgress: false,
      canManageDependencies: false,
      canClearSchedule: false,
      canOpen: true
    }
  };
}

function unscheduledTask(): CoglatasGanttItem {
  return {
    taskId: 'task-unscheduled',
    kind: 'task',
    parentTaskId: null,
    milestoneId: null,
    title: 'Unscheduled Task',
    plannedStartDate: null,
    plannedEndDate: null,
    milestoneDate: null,
    progressPercent: 0,
    progressIsDerived: false,
    workflowStageId: 'stage-todo',
    workflowStageName: 'Todo',
    stageCategory: 'todo',
    priority: 'medium',
    isBlocked: false,
    primaryAssignee: null,
    version: 1,
    scheduleEditPermissions: {
      canEditSchedule: false,
      canEditProgress: false,
      canManageDependencies: false,
      canClearSchedule: false,
      canOpen: true
    },
    warnings: []
  };
}
