import { applicationConfig, type Meta, type StoryObj } from '@storybook/angular';

import { COGLATAS_EXPORT_DIAGNOSTICS_MOCK } from '../admin.facade';
import { EXPORT_DIAGNOSTICS_SCENARIOS } from '../admin.mock';
import { ExportDiagnosticsPageComponent } from './export-diagnostics-page.component';

const meta: Meta<ExportDiagnosticsPageComponent> = {
  title: 'Features/Admin/ExportDiagnosticsPage',
  component: ExportDiagnosticsPageComponent,
  parameters: {
    layout: 'fullscreen'
  },
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_EXPORT_DIAGNOSTICS_MOCK, useValue: EXPORT_DIAGNOSTICS_SCENARIOS.default }]
    })
  ]
};

export default meta;

type Story = StoryObj<ExportDiagnosticsPageComponent>;

const withScenario = (scenario: keyof typeof EXPORT_DIAGNOSTICS_SCENARIOS): Story => ({
  decorators: [
    applicationConfig({
      providers: [{ provide: COGLATAS_EXPORT_DIAGNOSTICS_MOCK, useValue: EXPORT_DIAGNOSTICS_SCENARIOS[scenario] }]
    })
  ]
});

export const ExportDiagnosticsDefault: Story = {};

export const ExportAllowed: Story = withScenario('allowed');

export const ExportNotAllowed: Story = withScenario('notAllowed');

export const ExportJobPending: Story = withScenario('pending');

export const ExportJobFailed: Story = withScenario('failed');
