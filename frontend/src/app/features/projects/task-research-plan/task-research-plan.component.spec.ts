import { I18nService } from '../../../core/i18n/i18n.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TaskResearchPlanComponent } from './task-research-plan.component';

const TASK_ID = 'task-366';

// Existing behavior assertions intentionally use the English display locale.
beforeEach(() => { localStorage.setItem('coglatas.locale', 'en'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('TaskResearchPlanComponent', () => {
  let fixture: ComponentFixture<TaskResearchPlanComponent>;
  let component: TaskResearchPlanComponent;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskResearchPlanComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(TaskResearchPlanComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('taskId', TASK_ID);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('switches plan labels without losing edits, focus, canonical status or reviewed server impact text', async () => {
    flushPlan(http, planResponse());
    component.updateText(0, 'title', 'Edited / 下書き');
    fixture.detectChanges();
    const i18n = TestBed.inject(I18nService);
    const native = fixture.nativeElement as HTMLElement;
    const input = native.querySelector<HTMLInputElement>('input')!;
    const select = native.querySelector<HTMLSelectElement>('select')!;
    select.value = 'Ready';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const draft = component.draft();
    const dirty = vi.spyOn(component.dirtyChange, 'emit');
    input.focus();
    input.setSelectionRange(1, 4);
    await Promise.resolve();
    dirty.mockClear();

    i18n.setLocale('ja');
    fixture.detectChanges();
    await Promise.resolve();
    expect(native.querySelector('h2')?.textContent).toBe('調査計画');
    expect(select.value).toBe('Ready');
    expect(select.selectedOptions[0]?.textContent?.trim()).toBe('準備完了');
    expect(native.querySelector('input')).toBe(input);
    expect(input.value).toBe('Edited / 下書き');
    expect(input.selectionStart).toBe(1);
    expect(document.activeElement).toBe(input);
    expect(component.draft()).toBe(draft);
    expect(component.dirty()).toBe(true);
    expect(dirty).not.toHaveBeenCalled();
    expect(http.match(() => true)).toHaveLength(0);

    component.reviewChanges();
    const review = http.expectOne(`/api/tasks/${TASK_ID}/research-plan/preview`);
    expect(review.request.body.steps[0].status).toBe('Ready');
    expect(review.request.body.steps[0].title).toBe('Edited / 下書き');
    review.flush(previewResponse());
    fixture.detectChanges();
    const reviewed = component.preview();
    expect(native.textContent).toContain('変更 + 並べ替え');
    expect(native.textContent).toContain('An existing step moves from position 2 to 1.');
    expect(component.canSaveReviewed()).toBe(true);

    i18n.setLocale('en');
    fixture.detectChanges();
    expect(native.querySelector('h2')?.textContent).toBe('Research Plan');
    expect(component.preview()).toBe(reviewed);
    expect(component.canSaveReviewed()).toBe(true);
    expect(input.value).toBe('Edited / 下書き');
    expect(select.value).toBe('Ready');
    expect(native.textContent).toContain('An existing step moves from position 2 to 1.');
    expect(http.match(() => true)).toHaveLength(0);
  });

  it('keeps a localized safe load failure across language changes without displaying server diagnostics', () => {
    http.expectOne(`/api/tasks/${TASK_ID}/research-plan`).flush(
      { message: 'private-server-file-path' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    const i18n = TestBed.inject(I18nService);
    const native = fixture.nativeElement as HTMLElement;
    expect(native.textContent).toContain('The Research Plan could not be loaded.');
    expect(native.textContent).not.toContain('private-server-file-path');
    i18n.setLocale('ja');
    fixture.detectChanges();
    expect(native.textContent).toContain('調査計画を読み込めませんでした。');
    expect(native.textContent).not.toContain('private-server-file-path');
    expect(http.match(() => true)).toHaveLength(0);
  });

  it('reviews authoritative added/modified/reordered diff and impact before saving the exact draft', () => {
    flushPlan(http, planResponse());
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-research-plan-revision"]')?.textContent).toContain('revision 3');
    expect((native.querySelector('input') as HTMLInputElement | null)?.value).toBe('Collect evidence');
    expect(native.querySelector('[aria-label="Move Step 2 up"]')).not.toBeNull();

    component.moveStep(1, -1);
    component.addStep();
    component.updateText(2, 'title', 'Publish review');
    component.updateStatus(2, 'Ready');
    component.reviewChanges();

    const review = http.expectOne(`/api/tasks/${TASK_ID}/research-plan/preview`);
    expect(review.request.method).toBe('POST');
    expect(review.request.withCredentials).toBe(true);
    expect(review.request.body).toEqual({
      expectedVersion: 7,
      steps: [
        { title: 'Review findings', objective: 'Review source facts.', scopeSummary: 'Task scope', status: 'Ready', baseStepId: 'step-2' },
        { title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned', baseStepId: 'step-1' },
        { title: 'Publish review', objective: '', scopeSummary: '', status: 'Ready', baseStepId: null }
      ]
    });
    review.flush(previewResponse());
    fixture.detectChanges();

    const reviewedNative = fixture.nativeElement as HTMLElement;
    expect(reviewedNative.querySelector('[data-testid="task-research-plan-diff"]')).not.toBeNull();
    expect(reviewedNative.textContent).toContain('Modified + Reordered');
    expect(reviewedNative.textContent).toContain('Source-scope guidance changes for Step 1.');
    expect(reviewedNative.textContent).toContain('Plan coverage changed. Review the Task deliverable before saving');
    expect(component.canSaveReviewed()).toBe(true);

    component.save();
    const save = http.expectOne(`/api/tasks/${TASK_ID}/research-plan`);
    expect(save.request.method).toBe('PUT');
    expect(save.request.withCredentials).toBe(true);
    expect(save.request.body).toEqual({
      expectedVersion: 7,
      steps: [
        { title: 'Review findings', objective: 'Review source facts.', scopeSummary: 'Task scope', status: 'Ready', baseStepId: 'step-2' },
        { title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned', baseStepId: 'step-1' },
        { title: 'Publish review', objective: '', scopeSummary: '', status: 'Ready', baseStepId: null }
      ],
      previewFingerprint: 'a'.repeat(64)
    });
    save.flush(planResponse({ version: 8, number: 4, steps: [
      { id: 'step-3', position: 1, title: 'Review findings', objective: 'Review source facts.', scopeSummary: 'Task scope', status: 'Ready' },
      { id: 'step-4', position: 2, title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned' },
      { id: 'step-5', position: 3, title: 'Publish review', objective: '', scopeSummary: '', status: 'Ready' }
    ] }));
    fixture.detectChanges();

    expect(component.dirty()).toBe(false);
    expect(component.preview()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Research Plan revision 4 saved from the reviewed diff.');
  });

  it('invalidates a reviewed diff as soon as the draft changes again', () => {
    flushPlan(http, planResponse());
    component.updateText(0, 'objective', 'Updated objective');
    component.reviewChanges();
    http.expectOne(`/api/tasks/${TASK_ID}/research-plan/preview`).flush(previewResponse({
      changes: [modifiedChange()]
    }));
    fixture.detectChanges();

    expect(component.preview()).not.toBeNull();
    expect(component.canSaveReviewed()).toBe(true);

    component.updateText(0, 'objective', 'Changed after review');
    fixture.detectChanges();

    expect(component.preview()).toBeNull();
    expect(component.canSaveReviewed()).toBe(false);
  });

  it('shows a persisted plan to a viewer without mutation controls', () => {
    flushPlan(http, planResponse({ canManage: false }));
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.textContent).toContain('Current saved revision 3');
    expect(native.querySelector('input')).toBeNull();
    expect(native.querySelector('textarea')).toBeNull();
    expect(native.textContent).not.toContain('Save reviewed Plan');
    expect(native.textContent).not.toContain('Review changes');
  });

  it('reloads the authoritative revision after a stale reviewed save conflict', () => {
    flushPlan(http, planResponse());
    component.updateText(0, 'objective', 'Updated objective');
    component.reviewChanges();
    http.expectOne(`/api/tasks/${TASK_ID}/research-plan/preview`).flush(previewResponse({
      changes: [modifiedChange()]
    }));
    component.save();

    http.expectOne(`/api/tasks/${TASK_ID}/research-plan`).flush({
      error: { code: 'RESEARCH_PLAN_STALE_VERSION', message: 'Changed elsewhere.' }
    }, { status: 409, statusText: 'Conflict' });
    flushPlan(http, planResponse({ version: 8, number: 4 }));
    fixture.detectChanges();

    expect(component.plan()?.version).toBe(8);
    expect(component.dirty()).toBe(false);
    expect(component.preview()).toBeNull();
  });
});

function flushPlan(http: HttpTestingController, response: Record<string, unknown>): void {
  const request = http.expectOne(`/api/tasks/${TASK_ID}/research-plan`);
  expect(request.request.withCredentials).toBe(true);
  request.flush(response);
}

function planResponse(overrides: { readonly version?: number; readonly number?: number; readonly canManage?: boolean; readonly steps?: readonly Record<string, unknown>[] } = {}): Record<string, unknown> {
  return {
    planId: 'plan-366',
    version: overrides.version ?? 7,
    canManage: overrides.canManage ?? true,
    currentRevision: {
      id: 'revision-3',
      number: overrides.number ?? 3,
      createdAtUtc: '2026-09-01T14:35:00Z',
      createdByUserId: 'user-366',
      steps: overrides.steps ?? [
        { id: 'step-1', position: 1, title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned' },
        { id: 'step-2', position: 2, title: 'Review findings', objective: 'Review source facts.', scopeSummary: 'Task scope', status: 'Ready' }
      ]
    }
  };
}

function previewResponse(overrides: { readonly changes?: readonly Record<string, unknown>[] } = {}): Record<string, unknown> {
  return {
    baseVersion: 7,
    baseRevisionId: 'revision-3',
    baseRevisionNumber: 3,
    fingerprint: 'a'.repeat(64),
    proposedSteps: [
      { baseStepId: 'step-2', position: 1, title: 'Review findings', objective: 'Review source facts.', scopeSummary: 'Task scope', status: 'Ready' },
      { baseStepId: 'step-1', position: 2, title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned' },
      { baseStepId: null, position: 3, title: 'Publish review', objective: '', scopeSummary: '', status: 'Ready' }
    ],
    changes: overrides.changes ?? [
      {
        kinds: ['Modified', 'Reordered'],
        baseStepId: 'step-2',
        beforePosition: 2,
        afterPosition: 1,
        before: { id: 'step-2', position: 2, title: 'Review findings', objective: 'Review source facts.', scopeSummary: 'Task scope', status: 'Ready' },
        after: { baseStepId: 'step-2', position: 1, title: 'Review findings', objective: 'Review source facts.', scopeSummary: 'Task scope', status: 'Ready' },
        changedFields: ['scopeSummary']
      },
      {
        kinds: ['Reordered'],
        baseStepId: 'step-1',
        beforePosition: 1,
        afterPosition: 2,
        before: { id: 'step-1', position: 1, title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned' },
        after: { baseStepId: 'step-1', position: 2, title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned' },
        changedFields: []
      },
      {
        kinds: ['Added'],
        baseStepId: null,
        beforePosition: null,
        afterPosition: 3,
        before: null,
        after: { baseStepId: null, position: 3, title: 'Publish review', objective: '', scopeSummary: '', status: 'Ready' },
        changedFields: []
      }
    ],
    impact: {
      beforeStepCount: 2,
      afterStepCount: 3,
      executionStepCountChanged: true,
      executionOrderChanged: true,
      sourceScopeGuidanceChanged: true,
      deliverableAlignmentReviewRequired: true,
      items: [
        { kind: 'ExecutionOrderChanged', message: 'An existing step moves from position 2 to 1.', stepPosition: 1, baseStepId: 'step-2' },
        { kind: 'SourceScopeGuidanceChanged', message: 'Source-scope guidance changes for Step 1. Effective source access is unchanged by this plan edit and remains governed by the Task execution source policy.', stepPosition: 1, baseStepId: 'step-2' },
        { kind: 'ExecutionStepAdded', message: 'Step 3 will be added to the saved execution plan.', stepPosition: 3, baseStepId: null },
        { kind: 'DeliverableAlignmentReviewRequired', message: 'Plan coverage changed. Review the Task deliverable before saving; Research Plan edits do not mutate the Task deliverable contract.', stepPosition: null, baseStepId: null }
      ]
    }
  };
}

function modifiedChange(): Record<string, unknown> {
  return {
    kinds: ['Modified'],
    baseStepId: 'step-1',
    beforePosition: 1,
    afterPosition: 1,
    before: { id: 'step-1', position: 1, title: 'Collect evidence', objective: 'Gather approved sources.', scopeSummary: 'Project files', status: 'Planned' },
    after: { baseStepId: 'step-1', position: 1, title: 'Collect evidence', objective: 'Updated objective', scopeSummary: 'Project files', status: 'Planned' },
    changedFields: ['objective']
  };
}
