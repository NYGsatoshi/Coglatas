import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../../core/realtime/realtime.models';
import { TaskExecutionScopeComponent } from './task-execution-scope.component';

const PROJECT_ID = 'project-357';
const TASK_ID = 'task-357';

interface ScopeOptions {
  readonly projectWebEnabled?: boolean;
  readonly projectFilesEnabled?: boolean;
  readonly projectVersion?: number;
  readonly projectCanManage?: boolean;
  readonly taskWebEnabled?: boolean;
  readonly taskFilesEnabled?: boolean;
  readonly taskOrigin?: 'ProjectDefault' | 'TaskOverride';
  readonly taskOverrideVersion?: number | null;
  readonly taskCanManage?: boolean;
  readonly latestRun?: Record<string, unknown> | null;
}

describe('TaskExecutionScopeComponent', () => {
  let fixture: ComponentFixture<TaskExecutionScopeComponent>;
  let component: TaskExecutionScopeComponent;
  let http: HttpTestingController;
  let protectedClearer: (() => void) | undefined;
  let realtimeEvents: Subject<DurableRealtimeEvent>;

  beforeEach(async () => {
    realtimeEvents = new Subject<DurableRealtimeEvent>();
    protectedClearer = undefined;
    await TestBed.configureTestingModule({
      imports: [TaskExecutionScopeComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: RealtimeFacade,
          useValue: {
            durableEvents$: realtimeEvents.asObservable(),
            registerProtectedStateClearer: (_owner: string, clearer: () => void) => {
              protectedClearer = clearer;
              return () => { protectedClearer = undefined; };
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TaskExecutionScopeComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('projectId', PROJECT_ID);
    fixture.componentRef.setInput('taskId', TASK_ID);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('preserves the pending execution through a same-Task executionRunChanged scope refresh', () => {
    flushScope(expectScopeReads(http), { projectFilesEnabled: true });
    fixture.detectChanges();
    const native = fixture.nativeElement as HTMLElement;
    native.querySelector<HTMLButtonElement>('[data-testid="task-execution-start"]')?.click();
    const start = http.expectOne(`/api/tasks/${TASK_ID}/execution-runs`);

    realtimeEvents.next({
      ...realtimeEvent('Projects.TaskChanged.v1', TASK_ID),
      payload: { reason: 'executionRunChanged' },
    });
    flushScope(expectScopeReads(http), {
      projectFilesEnabled: true,
      latestRun: {
        status: 'Running',
        snapshotScopeOrigin: 'ProjectDefault',
        snapshotWebEnabled: false,
        snapshotProjectFilesEnabled: true,
      },
    });
    fixture.detectChanges();
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush({
      runId: 'run-1067', status: 'Running', failureCode: null,
      requestedAtUtc: '2026-10-04T00:00:00Z', queuedAtUtc: null,
      startedAtUtc: '2026-10-04T00:00:01Z', finishedAtUtc: null, report: null,
    });
    fixture.detectChanges();

    expect(start.cancelled).toBe(false);
    expect(native.querySelector<HTMLButtonElement>('[data-testid="task-execution-start"]')?.disabled).toBe(true);
    http.expectNone(`/api/tasks/${TASK_ID}/execution-runs`);
    start.flush({ id: 'run-1067', status: 'Stopped' });
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush({
      runId: 'run-1067', status: 'Stopped', failureCode: null,
      requestedAtUtc: '2026-10-04T00:00:00Z', queuedAtUtc: null,
      startedAtUtc: '2026-10-04T00:00:01Z', finishedAtUtc: '2026-10-04T00:00:02Z', report: null,
    });
    fixture.detectChanges();
    expect(native.querySelector('[data-testid="task-execution-result-status"]')?.textContent).toContain('Stopped');
  });

  it('cancels a pending execution when the parent Project identity changes', () => {
    flushScope(expectScopeReads(http), { projectFilesEnabled: true });
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="task-execution-start"]')?.click();
    const start = http.expectOne(`/api/tasks/${TASK_ID}/execution-runs`);

    fixture.componentRef.setInput('projectId', 'project-other');
    fixture.detectChanges();

    expect(start.cancelled).toBe(true);
    expect(() => start.flush({ id: 'stale-run', status: 'Succeeded' })).toThrow();
    http.expectOne('/api/projects/project-other/execution-scope').flush(projectScopeResponse({}));
    http.expectOne(`/api/tasks/${TASK_ID}/execution-scope`).flush(taskScopeResponse({}));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Execution completed');
  });

  it('cancels a pending execution when Workspace or authorization protected state is cleared', () => {
    flushScope(expectScopeReads(http), { projectFilesEnabled: true });
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="task-execution-start"]')?.click();
    const start = http.expectOne(`/api/tasks/${TASK_ID}/execution-runs`);

    expect(protectedClearer).toBeDefined();
    protectedClearer?.();
    fixture.detectChanges();

    expect(start.cancelled).toBe(true);
    expect(() => start.flush({ id: 'stale-run', status: 'Succeeded' })).toThrow();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-task-execution-result')).toBeNull();
    http.expectNone(`/api/tasks/${TASK_ID}/execution-result`);
  });

  it('renders only the authorized effective policy and the first-party runtime contract notice', () => {
    const requests = expectScopeReads(http);
    expect(requests.project.request.withCredentials).toBe(true);
    expect(requests.task.request.withCredentials).toBe(true);
    flushScope(requests);
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-execution-scope-origin"]')?.textContent).toContain('Project default');
    expect(native.querySelector('[data-testid="task-execution-scope-web"]')?.textContent).toContain('Disabled');
    expect(native.querySelector('[data-testid="task-execution-scope-files"]')?.textContent).toContain('Disabled');
    expect(native.querySelector('[data-testid="task-execution-scope-future-only"]')?.textContent).toContain('future Run requests only');
    expect(native.querySelector('[data-testid="task-execution-runtime-contract"]')?.textContent).toContain('Execution provider: First-party Project Files V1');
    expect(native.querySelectorAll('button').length).toBe(1);
    expect(native.querySelector('[data-testid="task-context-summary-count"]')?.textContent).toContain('0 of 4 source kinds eligible');
    expect(native.textContent).not.toContain('Start execution');
  });

  it('summarizes only authorized source kinds and moves keyboard focus to the detailed context', () => {
    flushScope(expectScopeReads(http), {
      taskOrigin: 'TaskOverride',
      taskOverrideVersion: 2,
      taskWebEnabled: true,
      taskFilesEnabled: false,
    });
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    const summary = native.querySelector<HTMLButtonElement>('[data-testid="task-context-summary"]');
    const details = native.querySelector<HTMLElement>('[data-testid="task-context-details"]');
    expect(summary).not.toBeNull();
    expect(summary?.getAttribute('aria-controls')).toBe(details?.id);
    expect(native.querySelector('[data-testid="task-context-summary-count"]')?.textContent).toContain('1 of 4 source kinds eligible');
    expect(native.querySelector('[data-testid="task-context-summary-origin"]')?.textContent).toContain('Task override');
    expect(native.querySelector('[data-testid="task-context-summary-web"]')?.textContent).toContain('Web: Allow');
    expect(native.querySelector('[data-testid="task-context-summary-files"]')?.textContent).toContain('Project files: Exclude');
    expect(summary?.textContent).toContain('never a hidden inventory count');

    summary?.click();

    expect(document.activeElement).toBe(details);
  });

  it('saves the Project default with source policy v2 and refreshes the Task-effective projection', () => {
    flushScope(expectScopeReads(http), {
      projectCanManage: true,
      taskCanManage: true,
      projectVersion: 4,
    });
    fixture.detectChanges();

    component.projectWebEnabled.set(true);
    component.projectFilesEnabled.set(true);
    component.saveProjectDefault();

    const save = http.expectOne(`/api/projects/${PROJECT_ID}/execution-scope`);
    expect(save.request.method).toBe('PUT');
    expect(save.request.withCredentials).toBe(true);
    expect(save.request.body).toEqual({
      webEnabled: true,
      projectFilesEnabled: true,
      expectedVersion: 4,
      policyV2: {
        schemaVersion: 2,
        web: 'Allow',
        webSite: 'Exclude',
        projectFile: 'Allow',
        connectedApp: 'Exclude',
        items: [],
      },
    });
    save.flush({ policy: { webEnabled: true, projectFilesEnabled: true }, version: 5, canManage: true });

    flushScope(expectScopeReads(http), {
      projectWebEnabled: true,
      projectFilesEnabled: true,
      projectVersion: 5,
      taskWebEnabled: true,
      taskFilesEnabled: true,
      projectCanManage: true,
      taskCanManage: true,
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Project default source policy saved.');
    expect(component.scope()?.task.effectivePolicy.webEnabled).toBe(true);
    expect(component.scope()?.task.effectivePolicy.projectFilesEnabled).toBe(true);
  });

  it('renders a locked policy snapshot and derives its major state only from durable run status', () => {
    flushScope(expectScopeReads(http), {
      latestRun: {
        status: 'Failed',
        snapshotScopeOrigin: 'TaskOverride',
        snapshotWebEnabled: true,
        snapshotProjectFilesEnabled: false,
      },
    });
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-execution-snapshot"]')?.textContent).toContain('Task override');
    expect(native.querySelector('[data-testid="task-execution-major-state"]')?.textContent).toContain('Failed');
    expect(native.querySelector('[data-testid="task-execution-snapshot"]')?.textContent).toContain('Execution failed.');
    expect(native.textContent).not.toContain('Start execution');
  });

  it('renders Queued from the server major-state contract in an atomic polite status region', () => {
    flushScope(expectScopeReads(http), {
      latestRun: {
        status: 'Queued',
        majorState: 'Queued',
        snapshotScopeOrigin: 'ProjectDefault',
        snapshotWebEnabled: false,
        snapshotProjectFilesEnabled: true,
      },
    });
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    const status = native.querySelector('[data-testid="task-execution-major-state-status"]');
    expect(status?.getAttribute('role')).toBe('status');
    expect(status?.getAttribute('aria-live')).toBe('polite');
    expect(status?.getAttribute('aria-atomic')).toBe('true');
    expect(native.querySelector('[data-testid="task-execution-major-state"]')?.textContent).toContain('Queued');
    expect(native.querySelector('[data-testid="task-execution-snapshot"]')?.textContent).toContain('Execution is queued for server materialization.');
  });

  it('saves a complete Task override instead of merging it with the Project default', () => {
    flushScope(expectScopeReads(http), {
      projectWebEnabled: true,
      projectCanManage: true,
      taskCanManage: true,
      taskWebEnabled: true,
      taskFilesEnabled: false,
    });
    fixture.detectChanges();

    component.setTaskEditorMode('override');
    component.overrideWebEnabled.set(false);
    component.overrideProjectFilesEnabled.set(true);
    component.saveTaskScope();

    const save = http.expectOne(`/api/tasks/${TASK_ID}/execution-scope-override`);
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual({
      webEnabled: false,
      projectFilesEnabled: true,
      expectedVersion: 0,
      policyV2: {
        schemaVersion: 2,
        web: 'Exclude',
        webSite: 'Exclude',
        projectFile: 'Allow',
        connectedApp: 'Exclude',
        items: [],
      },
    });
    save.flush(taskScopeResponse({
      taskOrigin: 'TaskOverride',
      taskOverrideVersion: 1,
      taskWebEnabled: false,
      taskFilesEnabled: true,
      taskCanManage: true,
    }));

    flushScope(expectScopeReads(http), {
      projectWebEnabled: true,
      projectCanManage: true,
      taskOrigin: 'TaskOverride',
      taskOverrideVersion: 1,
      taskWebEnabled: false,
      taskFilesEnabled: true,
      taskCanManage: true,
    });
    fixture.detectChanges();

    expect(component.scope()?.task.origin).toBe('TaskOverride');
    expect(component.scope()?.task.effectivePolicy.webEnabled).toBe(false);
    expect(component.scope()?.task.effectivePolicy.projectFilesEnabled).toBe(true);
  });

  it('sends Prioritize and canonical site item rules in the complete Task override', () => {
    flushScope(expectScopeReads(http), {
      projectCanManage: true,
      taskCanManage: true,
    });
    fixture.detectChanges();

    component.setTaskEditorMode('override');
    component.overrideFileState.set('Prioritize');
    component.overrideProjectFilesEnabled.set(true);
    component.addSiteRule('task', 'https://Docs.Example.com/path');
    component.saveTaskScope();

    const save = http.expectOne(`/api/tasks/${TASK_ID}/execution-scope-override`);
    expect(save.request.body).toEqual({
      webEnabled: false,
      projectFilesEnabled: true,
      expectedVersion: 0,
      policyV2: {
        schemaVersion: 2,
        web: 'Exclude',
        webSite: 'Exclude',
        projectFile: 'Prioritize',
        connectedApp: 'Exclude',
        items: [{ kind: 'WebSite', sourceId: 'site:docs.example.com', state: 'Allow' }],
      },
    });
    save.flush(taskScopeResponse({
      taskOrigin: 'TaskOverride',
      taskOverrideVersion: 1,
      taskFilesEnabled: true,
      taskCanManage: true,
    }));
    flushScope(expectScopeReads(http), {
      taskOrigin: 'TaskOverride',
      taskOverrideVersion: 1,
      taskFilesEnabled: true,
      taskCanManage: true,
    });
  });

  it('clears an existing Task override using its own optimistic version', () => {
    flushScope(expectScopeReads(http), {
      projectWebEnabled: true,
      projectCanManage: true,
      taskOrigin: 'TaskOverride',
      taskOverrideVersion: 7,
      taskWebEnabled: false,
      taskFilesEnabled: true,
      taskCanManage: true,
    });
    fixture.detectChanges();

    component.setTaskEditorMode('inherit');
    component.saveTaskScope();

    const clear = http.expectOne(`/api/tasks/${TASK_ID}/execution-scope-override`);
    expect(clear.request.method).toBe('DELETE');
    expect(clear.request.withCredentials).toBe(true);
    expect(clear.request.body).toEqual({ expectedVersion: 7 });
    clear.flush(taskScopeResponse({ taskOrigin: 'ProjectDefault', taskCanManage: true, taskWebEnabled: true }));

    flushScope(expectScopeReads(http), {
      projectWebEnabled: true,
      projectCanManage: true,
      taskOrigin: 'ProjectDefault',
      taskCanManage: true,
      taskWebEnabled: true,
    });
    fixture.detectChanges();

    expect(component.scope()?.task.origin).toBe('ProjectDefault');
    expect(component.scope()?.task.taskOverridePolicy).toBeNull();
  });

  it('cancels and removes the protected projection synchronously when Realtime clears the scope', () => {
    flushScope(expectScopeReads(http), { projectCanManage: true, taskCanManage: true });
    fixture.detectChanges();
    expect(component.scope()).not.toBeNull();

    protectedClearer?.();
    fixture.detectChanges();

    expect(component.scope()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('unavailable in the current session');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="task-context-summary"]')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="task-execution-scope-summary"]')).toBeNull();
  });

  it('redacts a failed response instead of rendering server-provided source details', () => {
    const requests = expectScopeReads(http);
    requests.task.flush(
      { error: { message: 'https://private.example/source content must not be shown' } },
      { status: 500, statusText: 'Server Error' },
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Source-scope settings could not be loaded. Try again.');
    expect(text).not.toContain('private.example');
    expect(text).not.toContain('source content must not be shown');
  });

  it('clears a previously authorized projection when an authoritative refresh is denied', () => {
    flushScope(expectScopeReads(http), { projectCanManage: true, taskCanManage: true });
    fixture.detectChanges();
    expect(component.scope()).not.toBeNull();

    realtimeEvents.next(realtimeEvent('Projects.TaskChanged.v1', TASK_ID));
    const refresh = expectScopeReads(http);
    refresh.task.flush(
      { error: { message: 'authorization details must not remain visible' } },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(component.scope()).toBeNull();
    expect(component.canManageAnything()).toBe(false);
    expect(native.querySelector('[data-testid="task-context-summary"]')).toBeNull();
    expect(native.querySelector('[data-testid="task-execution-scope-summary"]')).toBeNull();
    expect(native.textContent).toContain('unavailable in the current session');
    expect(native.textContent).not.toContain('authorization details');
  });

  it('refetches the authoritative projection for a matching Project or Task invalidation without reading event payload data', () => {
    flushScope(expectScopeReads(http), { projectCanManage: true, taskCanManage: true });
    fixture.detectChanges();

    realtimeEvents.next(realtimeEvent('Projects.ProjectChanged.v1', PROJECT_ID));
    expect(component.scope()).not.toBeNull();
    expect(component.activeRead()).toBe(true);
    flushScope(expectScopeReads(http), {
      projectWebEnabled: true,
      projectCanManage: true,
      taskWebEnabled: true,
      taskCanManage: true,
    });
    fixture.detectChanges();
    expect(component.scope()?.task.effectivePolicy.webEnabled).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="task-context-summary-count"]')?.textContent).toContain('1 of 4 source kinds eligible');

    realtimeEvents.next(realtimeEvent('Projects.TaskChanged.v1', TASK_ID));
    flushScope(expectScopeReads(http), {
      projectWebEnabled: true,
      projectFilesEnabled: true,
      projectCanManage: true,
      taskWebEnabled: true,
      taskFilesEnabled: true,
      taskCanManage: true,
    });
    fixture.detectChanges();
    expect(component.scope()?.task.effectivePolicy.projectFilesEnabled).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="task-context-summary-count"]')?.textContent).toContain('2 of 4 source kinds eligible');
  });
});

function expectScopeReads(http: HttpTestingController): { project: TestRequest; task: TestRequest } {
  return {
    project: http.expectOne(`/api/projects/${PROJECT_ID}/execution-scope`),
    task: http.expectOne(`/api/tasks/${TASK_ID}/execution-scope`),
  };
}

function flushScope(
  requests: { project: TestRequest; task: TestRequest },
  options: ScopeOptions = {},
): void {
  requests.project.flush(projectScopeResponse(options));
  requests.task.flush(taskScopeResponse(options));
}

function projectScopeResponse(options: ScopeOptions): Record<string, unknown> {
  return {
    policy: {
      webEnabled: options.projectWebEnabled ?? false,
      projectFilesEnabled: options.projectFilesEnabled ?? false,
    },
    version: options.projectVersion ?? 1,
    canManage: options.projectCanManage ?? false,
  };
}

function taskScopeResponse(options: ScopeOptions): Record<string, unknown> {
  const origin = options.taskOrigin ?? 'ProjectDefault';
  const effectivePolicy = {
    webEnabled: options.taskWebEnabled ?? options.projectWebEnabled ?? false,
    projectFilesEnabled: options.taskFilesEnabled ?? options.projectFilesEnabled ?? false,
  };
  const hasOverride = origin === 'TaskOverride';
  return {
    effectivePolicy,
    origin,
    projectDefaultVersion: options.projectVersion ?? 1,
    taskOverrideVersion: hasOverride ? options.taskOverrideVersion ?? 1 : null,
    taskOverridePolicy: hasOverride ? effectivePolicy : null,
    canManage: options.taskCanManage ?? false,
    latestRun: options.latestRun ?? null,
    changesApplyTo: 'FutureRunsOnly',
  };
}

function realtimeEvent(
  eventType: DurableRealtimeEvent['eventType'],
  aggregateId: string,
): DurableRealtimeEvent {
  return {
    eventId: `event-${aggregateId}`,
    eventType,
    payloadSchemaVersion: 1,
    occurredAt: '2026-08-25T00:00:00.000Z',
    tenantId: 'tenant-357',
    aggregateType: eventType === 'Projects.ProjectChanged.v1' ? 'Project' : 'TaskItem',
    aggregateId,
    aggregateVersion: 2,
    actor: { actorType: 'User', actorId: 'actor-357' },
    correlationId: null,
    causationId: null,
    payload: { ignored: 'payload is never read by this component' },
  };
}
