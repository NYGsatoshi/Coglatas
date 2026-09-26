import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { provideRouter } from '@angular/router';

import { COGLATAS_PROJECTS_MOCK } from '../projects.facade';
import { PROJECTS_SCENARIOS } from '../projects.mock';
import { MyTasksPageComponent } from './my-tasks-page.component';

const meta: Meta<MyTasksPageComponent> = {
  title: 'Features/Projects/MyTasksPage',
  component: MyTasksPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [provideRouter([]), { provide: COGLATAS_PROJECTS_MOCK, useValue: PROJECTS_SCENARIOS.default }]
    })
  ]
};

export default meta;

type Story = StoryObj<MyTasksPageComponent>;

const withScenario = (scenario: keyof typeof PROJECTS_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [provideRouter([]), { provide: COGLATAS_PROJECTS_MOCK, useValue: PROJECTS_SCENARIOS[scenario] }]
    })
  ]
});

export const TasksDefault: Story = {};

export const TasksManyRowsBoundedPage: Story = withScenario('manyRowsBoundedPage');
