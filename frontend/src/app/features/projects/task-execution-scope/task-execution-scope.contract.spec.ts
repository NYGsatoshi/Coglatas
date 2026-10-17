import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TaskExecutionScopeComponent } from './task-execution-scope.component';

const PROJECT_ID = '11111111-1111-1111-1111-111111111111';
const TASK_ID = '22222222-2222-2222-2222-222222222222';

// Existing behavior assertions intentionally use the English display locale.
beforeEach(() => { localStorage.setItem('coglatas.locale', 'en'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('TaskExecutionScopeComponent canonical source-policy presentation', () => {
  let fixture: ComponentFixture<TaskExecutionScopeComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskExecutionScopeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(TaskExecutionScopeComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('projectId', PROJECT_ID);
    fixture.componentRef.setInput('taskId', TASK_ID);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('keeps the effective next-run V2 policy visible and explains Allow, Prioritize, and Exclude', () => {
    http.expectOne(`/api/projects/${PROJECT_ID}/execution-scope`).flush({
      policy: {
        webEnabled: false,
        projectFilesEnabled: false,
        policyV2: {
          schemaVersion: 2,
          web: 'Exclude',
          webSite: 'Exclude',
          projectFile: 'Exclude',
          connectedApp: 'Exclude',
          items: [],
        },
      },
      version: 6,
      canManage: true,
    });
    http.expectOne(`/api/tasks/${TASK_ID}/execution-scope`).flush({
      effectivePolicy: {
        webEnabled: true,
        projectFilesEnabled: false,
        policyV2: {
          schemaVersion: 2,
          web: 'Allow',
          webSite: 'Exclude',
          projectFile: 'Exclude',
          connectedApp: 'Exclude',
          items: [
            { kind: 'WebSite', sourceId: 'site:docs.example.com', state: 'Prioritize' },
          ],
        },
      },
      origin: 'TaskOverride',
      projectDefaultVersion: 6,
      taskOverrideVersion: 3,
      taskOverridePolicy: {
        webEnabled: true,
        projectFilesEnabled: false,
        policyV2: {
          schemaVersion: 2,
          web: 'Allow',
          webSite: 'Exclude',
          projectFile: 'Exclude',
          connectedApp: 'Exclude',
          items: [
            { kind: 'WebSite', sourceId: 'site:docs.example.com', state: 'Prioritize' },
          ],
        },
      },
      canManage: true,
      latestRun: {
        id: '33333333-3333-3333-3333-333333333333',
        status: 'Accepted',
        requestedAtUtc: '2026-08-29T00:00:00Z',
        snapshotSchemaVersion: 3,
        snapshotScopeOrigin: 'ProjectDefault',
        snapshotProjectScopeVersion: 5,
        snapshotTaskOverrideVersion: null,
        snapshotWebEnabled: false,
        snapshotProjectFilesEnabled: true,
        snapshotPolicyV2: {
          schemaVersion: 2,
          web: 'Exclude',
          webSite: 'Exclude',
          projectFile: 'Prioritize',
          connectedApp: 'Exclude',
          items: [],
        },
      },
      sourceInventory: [],
      changesApplyTo: 'nextRun',
    });
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-context-summary-count"]')?.textContent).toContain('2 of 4 source kinds eligible');
    expect(native.querySelector('[data-testid="task-context-summary-origin"]')?.textContent).toContain('Task override');
    expect(native.querySelector('[data-testid="task-context-summary-web"]')?.textContent).toContain('Web: Allow');
    expect(native.querySelector('[data-testid="task-context-summary-files"]')?.textContent).toContain('Project files: Exclude');
    expect(native.querySelector('[data-testid="task-execution-scope-origin"]')?.textContent).toContain('Task override');
    expect(native.querySelector('[data-testid="task-execution-scope-web"]')?.textContent).toBe('Enabled');
    expect(native.querySelector('[data-testid="task-execution-scope-web-policy"]')?.textContent).toContain('Allow');
    expect(native.querySelector('[data-testid="task-execution-scope-files"]')?.textContent).toBe('Disabled');
    expect(native.querySelector('[data-testid="task-execution-scope-files-policy"]')?.textContent).toContain('Exclude');
    expect(native.querySelector('[data-testid="task-execution-scope-sites"]')?.textContent).toContain('Exclude');
    expect(native.querySelector('[data-testid="task-execution-scope-apps"]')?.textContent).toContain('Exclude');

    const terms = native.querySelector('[data-testid="task-execution-scope-terms"]')?.textContent ?? '';
    expect(terms).toContain('Allow');
    expect(terms).toContain('Eligible for the next Run.');
    expect(terms).toContain('Prioritize');
    expect(terms).toContain('Eligible and preferred before ordinary Allow sources.');
    expect(terms).toContain('Exclude');
    expect(terms).toContain('Not eligible and never materialized by the runtime.');
    expect(native.querySelector('[data-testid="task-execution-scope-rule-limit"]')?.textContent)
      .toContain('A specific source rule overrides its source-kind default.');

    const itemRules = native.querySelector('[data-testid="task-execution-item-rules"]')?.textContent ?? '';
    expect(itemRules).toContain('WebSite');
    expect(itemRules).toContain('site:docs.example.com');
    expect(itemRules).toContain('Prioritize');

    expect(native.querySelector('[data-testid="task-execution-scope-future-only"]')?.textContent)
      .toContain('immutable resolved policy snapshot');
    expect(native.querySelector('[data-testid="task-execution-runtime-contract"]')?.textContent)
      .toContain('Execution provider: First-party Project Files V1');
    expect(native.querySelector('.task-execution-scope__editor-link')?.getAttribute('href')).toBe('#task-execution-scope-editor');

    const snapshot = native.querySelector('[data-testid="task-execution-snapshot"]')?.textContent ?? '';
    expect(native.querySelector('[data-testid="task-execution-major-state"]')?.textContent).toContain('Accepted');
    expect(snapshot).toContain('Project default');
    expect(snapshot).toContain('Web at request');
    expect(snapshot).toContain('Exclude');
    expect(snapshot).toContain('Project files at request');
    expect(snapshot).toContain('Prioritize');
    expect(snapshot).toContain('Execution request was durably accepted.');
  });

  it('keeps legacy compatibility responses fail-closed without exposing hidden source inventory', () => {
    http.expectOne(`/api/projects/${PROJECT_ID}/execution-scope`).flush({
      policy: { webEnabled: false, projectFilesEnabled: false },
      version: 0,
      canManage: false,
    });
    http.expectOne(`/api/tasks/${TASK_ID}/execution-scope`).flush({
      effectivePolicy: { webEnabled: false, projectFilesEnabled: false },
      origin: 'ProjectDefault',
      projectDefaultVersion: 0,
      taskOverrideVersion: null,
      taskOverridePolicy: null,
      canManage: false,
      latestRun: null,
      changesApplyTo: 'nextRun',
    });
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    const text = native.textContent ?? '';
    expect(text).toContain('Specific sites');
    expect(text).toContain('Connected apps');
    expect(text).not.toContain('private.example');
    expect(text).not.toContain('file-123');
    expect(text).not.toContain('3 sources');
    expect(native.querySelector('[data-testid="task-context-summary-count"]')?.textContent).toContain('0 of 4 source kinds eligible');
    expect(native.querySelector('[data-testid="task-context-summary"]')?.textContent).toContain('never a hidden inventory count');
    expect(native.querySelector('[data-testid="task-execution-scope-sites"]')?.textContent).toContain('Exclude');
    expect(native.querySelector('[data-testid="task-execution-scope-apps"]')?.textContent).toContain('Exclude');
    expect(native.querySelector('.task-execution-scope__editor-link')).toBeNull();
  });
});
