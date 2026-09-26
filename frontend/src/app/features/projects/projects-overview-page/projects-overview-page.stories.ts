import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';
import { provideRouter } from '@angular/router';

import { COGLATAS_PROJECTS_MOCK } from '../projects.facade';
import { PROJECTS_SCENARIOS } from '../projects.mock';
import { ProjectsOverviewPageComponent } from './projects-overview-page.component';

const meta: Meta<ProjectsOverviewPageComponent> = {
  title: 'Features/Projects/ProjectsOverviewPage',
  component: ProjectsOverviewPageComponent,
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

type Story = StoryObj<ProjectsOverviewPageComponent>;

const withScenario = (scenario: keyof typeof PROJECTS_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [provideRouter([]), { provide: COGLATAS_PROJECTS_MOCK, useValue: PROJECTS_SCENARIOS[scenario] }]
    })
  ]
});

export const ProjectsDefault: Story = {};

export const ProjectsLoading: Story = withScenario('loading');

export const ProjectsEmpty: Story = withScenario('empty');

export const ProjectsPermissionDenied: Story = withScenario('permissionDenied');

export const LongTaskTitles: Story = withScenario('longTaskTitles');

export const Mobile: Story = {
  ...withScenario('mobile'),
  parameters: {
    viewport: { defaultViewport: 'mobile1' }
  }
};
