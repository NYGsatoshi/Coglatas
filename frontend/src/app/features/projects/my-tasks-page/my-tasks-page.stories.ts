import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { signal } from '@angular/core';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import { EMPTY } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { COGLATAS_MY_TASKS_MOCK } from '../my-tasks.facade';
import { PROJECTS_SCENARIOS } from '../projects.mock';
import { ProjectsScenario } from '../projects.types';
import { COGLATAS_WORK_VIEW_PREFERENCE_STORAGE, WorkViewPreferenceStorage } from '../work-view-preference.service';
import { MyTasksPageComponent } from './my-tasks-page.component';

// Fixtures must not inherit a real session boundary, transport, or saved filters.
const storyRealtime: Pick<RealtimeFacade,
  'durableEvents$' | 'connectionState' | 'registerProtectedStateClearer' | 'registerCatchUp'> = {
  durableEvents$: EMPTY,
  connectionState: signal<'Degraded'>('Degraded').asReadonly(),
  registerProtectedStateClearer: () => () => undefined,
  registerCatchUp: () => () => undefined
};

function storyFilterStorage(): WorkViewPreferenceStorage {
  const values = new Map<string, string>();
  return {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => { values.set(key, value); },
    removeItem: (key) => { values.delete(key); }
  };
}

function storyScenario(scenario: ProjectsScenario): ProjectsScenario {
  return { ...scenario, myTasks: (scenario.myTasks ?? scenario.tasks).filter((task) => task.authorized) };
}

const meta: Meta<MyTasksPageComponent> = {
  title: 'Features/Projects/MyTasksPage',
  component: MyTasksPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([], withDisabledInitialNavigation()),
        { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
        { provide: RealtimeFacade, useValue: storyRealtime },
        { provide: COGLATAS_WORK_VIEW_PREFERENCE_STORAGE, useFactory: storyFilterStorage },
        { provide: COGLATAS_MY_TASKS_MOCK, useValue: storyScenario(PROJECTS_SCENARIOS.default) }
      ]
    })
  ]
};

export default meta;

type Story = StoryObj<MyTasksPageComponent>;

const withScenario = (scenario: keyof typeof PROJECTS_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_MY_TASKS_MOCK, useValue: storyScenario(PROJECTS_SCENARIOS[scenario]) }]
    })
  ]
});

export const TasksDefault: Story = {};

export const TasksManyRowsBoundedPage: Story = withScenario('manyRowsBoundedPage');
