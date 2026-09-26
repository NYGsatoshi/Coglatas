import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { COGLATAS_PROJECTS_MOCK } from '../projects.facade';
import { PROJECTS_PRIMARY_PROJECT_ID, PROJECTS_PRIMARY_TASK_ID, PROJECTS_SCENARIOS } from '../projects.mock';
import { TaskDetailPageComponent } from './task-detail-page.component';

const routeStub = {
  snapshot: {
    paramMap: convertToParamMap({
      projectId: PROJECTS_PRIMARY_PROJECT_ID,
      taskId: PROJECTS_PRIMARY_TASK_ID
    })
  }
};

const meta: Meta<TaskDetailPageComponent> = {
  title: 'Features/Projects/TaskDetailPage',
  component: TaskDetailPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: routeStub },
        { provide: COGLATAS_PROJECTS_MOCK, useValue: PROJECTS_SCENARIOS.default }
      ]
    })
  ]
};

export default meta;

type Story = StoryObj<TaskDetailPageComponent>;

const withScenario = (scenario: keyof typeof PROJECTS_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: routeStub },
        { provide: COGLATAS_PROJECTS_MOCK, useValue: PROJECTS_SCENARIOS[scenario] }
      ]
    })
  ]
});

export const TaskDetailDefault: Story = {};

export const TaskEditorDefault: Story = {};

export const RowVersionConflict: Story = withScenario('rowVersionConflict');

/** Task editor's 409 recovery affordance; it is distinct from ordinary cancel. */
export const TaskSaveConflict: Story = withScenario('taskSaveConflict');

export const TaskConflictReloadLoading: Story = withScenario('taskConflictReloadLoading');

export const TaskConflictReloadError: Story = withScenario('taskConflictReloadError');

/** UI scenario state, not an API DTO: PATCH succeeded but the required reload did not. */
export const TaskSavedButRefreshFailed: Story = withScenario('taskSavedButRefreshFailed');

export const InvalidStateTransition: Story = withScenario('invalidStateTransition');

export const MilestoneReadOnly: Story = withScenario('milestoneReadOnly');

export const DependenciesDisplayOnly: Story = withScenario('dependenciesDisplayOnly');

/** Protected content is omitted when current project/task visibility is revoked. */
export const PermissionRevoked: Story = withScenario('permissionDenied');
