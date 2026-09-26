import { type Meta, type StoryObj } from '@storybook/angular';

import type { CoglatasGanttContract } from '../../contracts/coglatas-complex-adapter.contracts';
import { CoglatasDataGridComponent, CoglatasDialogComponent, CoglatasFileUploaderComponent, CoglatasGanttComponent } from './coglatas-adapter-shells.components';

const dataGridContract = {
  ariaLabel: 'Member list',
  columns: [],
  page: 1,
  pageSize: 25,
  presentation: 'desktop' as const,
  rowIdentity: (row: object) => JSON.stringify(row),
  rows: [],
  state: 'ready' as const
};
const dialogContract = { ariaLabel: 'Confirm action', closeOnEscape: true, destructive: false, presentation: 'desktop' as const, state: 'ready' as const, title: 'Confirm' };
const uploaderContract = { ariaLabel: 'Upload files', files: [], multiple: true, presentation: 'desktop' as const, state: 'ready' as const };
const ganttPermissions = { canEditSchedule: true, canEditProgress: true, canManageDependencies: true, canClearSchedule: true, canOpen: true };
const ganttContract: CoglatasGanttContract<object> = {
  ariaLabel: 'Canonical Project schedule',
  presentation: 'narrow',
  state: 'ready',
  tasks: [],
  taskIdentity: () => '',
  taskLabel: () => '',
  milestones: [],
  timezone: 'Asia/Tokyo',
  readOnly: false,
  calendar: {
    timeZone: 'Asia/Tokyo',
    workingDays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
    holidaysAvailable: false,
    limitations: ['Holiday details are not available.']
  },
  scheduledItems: [{
    taskId: 'task-story',
    kind: 'task',
    parentTaskId: null,
    milestoneId: null,
    title: 'Prepare accessible release',
    plannedStartDate: '2026-08-03',
    plannedEndDate: '2026-08-07',
    milestoneDate: null,
    progressPercent: 45,
    progressIsDerived: false,
    workflowStageId: 'stage-progress',
    workflowStageName: 'In progress',
    stageCategory: 'inProgress',
    priority: 'high',
    isBlocked: true,
    primaryAssignee: { userId: 'user-story', displayName: 'Taylor' },
    version: 4,
    scheduleEditPermissions: ganttPermissions,
    warnings: [{
      code: 'DEPENDENCY_VIOLATION',
      message: 'The manual dates do not satisfy the dependency.',
      severity: 'warning',
      targetType: 'task',
      targetId: 'task-story',
      field: 'plannedStartDate',
      blocking: false
    }]
  }],
  unscheduledItems: [],
  canonicalMilestones: [],
  dependencies: [],
  warnings: [],
  permissions: ganttPermissions,
  feedback: 'Manual scheduling is available.'
};

const meta: Meta = {
  title: 'Shared/UI adapters/Complex fallback shells',
  render: () => ({
    moduleMetadata: { imports: [CoglatasDataGridComponent, CoglatasDialogComponent, CoglatasFileUploaderComponent, CoglatasGanttComponent] },
    props: { dataGridContract, dialogContract, uploaderContract, ganttContract },
    template: `
      <main style="display:grid;gap:var(--coglatas-space-3);max-width:720px;padding:var(--coglatas-space-4)">
        <coglatas-data-grid [contract]="dataGridContract" />
        <coglatas-dialog [contract]="dialogContract" state="conflict" />
        <coglatas-file-uploader [contract]="uploaderContract" presentation="narrow" state="loading" />
        <coglatas-gantt [contract]="ganttContract" presentation="narrow" />
      </main>
    `
  })
};

export default meta;
type Story = StoryObj;

export const DarkCompact: Story = {};
export const LightComfortable: Story = {
  decorators: [(story) => {
    document.documentElement.dataset['coglatasTheme'] = 'light';
    document.documentElement.dataset['coglatasDensity'] = 'comfortable';
    return story();
  }]
};
