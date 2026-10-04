import { TestBed } from '@angular/core/testing';

import { FrontendFeatureFlagsService } from './frontend-feature-flags.service';

describe('FrontendFeatureFlagsService', () => {
  it('defaults the v0.4 design system to enabled and supports rollback', () => {
    TestBed.configureTestingModule({});
    const flags = TestBed.inject(FrontendFeatureFlagsService);
    expect(flags.designSystemV04Enabled()).toBe(true);
    expect(document.documentElement.dataset['coglatasDesignSystem']).toBe('v04');
    flags.setForTesting({ 'frontend.designSystemV04': false });
    expect(flags.designSystemV04Enabled()).toBe(false);
    expect(document.documentElement.dataset['coglatasDesignSystem']).toBe('legacy');
  });

  it('keeps Syncfusion implementations disabled until their independent rollout flags are enabled', () => {
    TestBed.configureTestingModule({});
    const flags = TestBed.inject(FrontendFeatureFlagsService);

    expect(flags.syncfusionGridEnabled()).toBe(false);
    expect(flags.syncfusionUploaderEnabled()).toBe(false);

    flags.setForTesting({ 'frontend.syncfusionGrid': true });

    expect(flags.syncfusionGridEnabled()).toBe(true);
    expect(flags.syncfusionUploaderEnabled()).toBe(false);
  });

  it('uses tasks.kanbanV1 only as a reversible presentation rollout', () => {
    TestBed.configureTestingModule({});
    const flags = TestBed.inject(FrontendFeatureFlagsService);

    expect(flags.kanbanV1Enabled()).toBe(true);
    flags.setForTesting({ 'tasks.kanbanV1': false });
    expect(flags.kanbanV1Enabled()).toBe(false);
  });

  it('uses tasks.ganttV1 only as a reversible Schedule-tab presentation rollout', () => {
    TestBed.configureTestingModule({});
    const flags = TestBed.inject(FrontendFeatureFlagsService);

    expect(flags.ganttV1Enabled()).toBe(true);
    flags.setForTesting({ 'tasks.ganttV1': false });
    expect(flags.ganttV1Enabled()).toBe(false);
  });
});
