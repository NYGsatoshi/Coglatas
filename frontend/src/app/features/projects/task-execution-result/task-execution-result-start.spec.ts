import { HttpStatusCode, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TaskExecutionResultComponent } from './task-execution-result.component';

const TASK_ID = 'task-464';

describe('TaskExecutionResultComponent run launcher', () => {
  let fixture: ComponentFixture<TaskExecutionResultComponent>;
  let http: HttpTestingController;

  const lifecycle = {
    expectCancelled(request: Readonly<Pick<TestRequest, 'cancelled' | 'flush'>>): void {
      expect(request.cancelled).toBe(true);
      expect(() => { request.flush({ id: 'stale-run', status: 'Succeeded' }); }).toThrow();
    },
    expectCompleted(): void {
      fixture.detectChanges();
      expect(fixture.componentInstance.starting()).toBe(false);
      expect(fixture.componentInstance.startFeedback()).toContain('Execution completed');
      expect(lifecycle.nativeElement().querySelector('[data-testid="task-execution-result-status"]')?.textContent).toContain('Succeeded');
      expect(lifecycle.nativeElement().textContent).toContain('browser-smoke-task.txt');
    },
    expectPendingSingleFlight(start: Readonly<Pick<TestRequest, 'cancelled'>>): void {
      expect(start.cancelled).toBe(false);
      expect(fixture.componentInstance.starting()).toBe(true);
      fixture.componentInstance.startExecution();
      http.expectNone(`/api/tasks/${TASK_ID}/execution-runs`);
    },
    loadDurableResult(): void {
      http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(succeededResult());
    },
    nativeElement(): HTMLElement {
      const native: unknown = fixture.nativeElement;
      if (!(native instanceof HTMLElement)) { throw new Error('Expected a native component element.'); }
      return native;
    },
    refreshProjection(): TestRequest {
      fixture.componentRef.setInput('loadExistingResult', true);
      fixture.detectChanges();
      return http.expectOne(`/api/tasks/${TASK_ID}/execution-result`);
    },
    startPending(): TestRequest {
      fixture.componentInstance.startExecution();
      const request = http.expectOne(`/api/tasks/${TASK_ID}/execution-runs`);
      expect(request.request.headers.get('Idempotency-Key')).toMatch(/^task-execution-ui-[A-Za-z0-9-]+$/u);
      return request;
    },
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskExecutionResultComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(TaskExecutionResultComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('taskId', TASK_ID);
    fixture.componentRef.setInput('allowExecutionStart', true);
    fixture.componentRef.setInput('loadExistingResult', false);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('posts an empty command with a fresh Idempotency-Key and then loads the durable result', () => {
    http.expectNone(`/api/tasks/${TASK_ID}/execution-result`);

    const button = (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="task-execution-start"]');
    expect(button).not.toBeNull();
    button?.click();

    const start = http.expectOne(`/api/tasks/${TASK_ID}/execution-runs`);
    expect(start.request.method).toBe('POST');
    expect(start.request.withCredentials).toBe(true);
    expect(start.request.body).toEqual({});
    expect(start.request.headers.get('Idempotency-Key')).toMatch(/^task-execution-ui-[A-Za-z0-9-]+$/u);

    const serializedBody = JSON.stringify(start.request.body);
    for (const forbidden of ['candidateIds', 'fileIds', 'fsPath', 'materializedSources', 'evidence', 'sources']) {
      expect(serializedBody).not.toContain(forbidden);
    }

    start.flush({ id: 'run-464', status: 'Succeeded' });

    const result = http.expectOne(`/api/tasks/${TASK_ID}/execution-result`);
    expect(result.request.method).toBe('GET');
    expect(result.request.withCredentials).toBe(true);
    result.flush(succeededResult());
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-execution-start-feedback"]')?.textContent)
      .toContain('Execution completed');
    expect(native.querySelector('[data-testid="task-execution-result-status"]')?.textContent)
      .toContain('Succeeded');
    expect(native.querySelector('[data-testid="task-execution-report-body"]')?.textContent)
      .toContain('browser-smoke-task.txt');
  });

  it('redacts unauthorized execution failures instead of rendering backend details', () => {
    const button = (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="task-execution-start"]');
    button?.click();

    http.expectOne(`/api/tasks/${TASK_ID}/execution-runs`).flush(
      { error: { message: '/srv/private/project/browser-smoke-task.txt' } },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Task execution is unavailable in the current session.');
    expect(text).not.toContain('/srv/private');
    expect(text).not.toContain('browser-smoke-task.txt');
  });

  describe('independent command and projection lifecycles', () => {
    it('preserves a pending execution POST when the same Task gains a latest-run projection', () => {
      const command = lifecycle.startPending(), key = command.request.headers.get('Idempotency-Key');
      lifecycle.refreshProjection().flush(succeededResult());
      lifecycle.expectPendingSingleFlight(command);
      expect(command.request.headers.get('Idempotency-Key')).toBe(key);
      command.flush({ id: 'run-464', status: 'Succeeded' });
      lifecycle.loadDurableResult();
      lifecycle.expectCompleted();
    });

    it('cancels the old execution POST when the exact Task identity changes', () => {
      const start = lifecycle.startPending();
      fixture.componentRef.setInput('taskId', 'task-other');
      fixture.detectChanges();
      lifecycle.expectCancelled(start);
      expect(fixture.componentInstance.starting()).toBe(false);
      expect(fixture.componentInstance.result()).toBeNull();
      http.expectNone(`/api/tasks/${TASK_ID}/execution-result`);
      http.expectNone('/api/tasks/task-other/execution-runs');
    });

    it('loads a later durable projection after command completion without another POST', () => {
      lifecycle.startPending().flush({ id: 'run-464', status: 'Succeeded' });
      lifecycle.loadDurableResult();
      lifecycle.refreshProjection().flush(succeededResult());
      http.expectNone(`/api/tasks/${TASK_ID}/execution-runs`);
      lifecycle.expectCompleted();
    });

    it('replaces a pending latest read on command completion without losing the command response', () => {
      const command = lifecycle.startPending(), projection = lifecycle.refreshProjection();
      command.flush({ id: 'run-464', status: 'Succeeded' });
      lifecycle.expectCancelled(projection);
      lifecycle.loadDurableResult();
      expect(fixture.componentInstance.result()?.runId).toBe('run-464');
      lifecycle.expectCompleted();
      http.expectNone(`/api/tasks/${TASK_ID}/execution-runs`);
    });
  });

  describe('command cleanup and failure boundaries', () => {
    it('keeps a pending command single-flight across manual result retry and presentation reset', () => {
      const command = lifecycle.startPending(), projection = lifecycle.refreshProjection();
      fixture.componentRef.setInput('loadExistingResult', false);
      fixture.detectChanges();
      lifecycle.expectCancelled(projection);
      lifecycle.expectPendingSingleFlight(command);
      fixture.componentInstance.retry();
      lifecycle.loadDurableResult();
      lifecycle.expectPendingSingleFlight(command);
      command.flush({ id: 'run-464', status: 'Succeeded' });
      lifecycle.loadDurableResult();
    });

    it('cancels both the pending command and latest read when the component is destroyed', () => {
      const command = lifecycle.startPending(), projection = lifecycle.refreshProjection();
      fixture.destroy();
      lifecycle.expectCancelled(command);
      lifecycle.expectCancelled(projection);
      expect(fixture.componentInstance.startFeedback()).toBeNull();
    });

    it('reports a real POST failure after a same-Task projection refresh', () => {
      const start = lifecycle.startPending();
      lifecycle.refreshProjection().flush(succeededResult());
      start.flush({}, { status: HttpStatusCode.ServiceUnavailable, statusText: 'Service Unavailable' });
      fixture.detectChanges();
      expect(fixture.componentInstance.starting()).toBe(false);
      expect(fixture.componentInstance.startError()).toBe('Task execution could not be started. Try again.');
      expect(fixture.componentInstance.startFeedback()).toBeNull();
      http.expectNone(`/api/tasks/${TASK_ID}/execution-runs`);
    });

    it('rejects a pending projection when the execution POST loses current authority', () => {
      const command = lifecycle.startPending(), projection = lifecycle.refreshProjection();
      command.flush({}, { status: HttpStatusCode.Forbidden, statusText: 'Forbidden' });
      lifecycle.expectCancelled(projection);
      expect(fixture.componentInstance.result()).toBeNull();
      expect(fixture.componentInstance.noResult()).toBe(true);
      expect(fixture.componentInstance.startError()).toBe('Task execution is unavailable in the current session.');
    });
  });

});

function succeededResult(): Record<string, unknown> {
  return {
    runId: 'run-464',
    status: 'Succeeded',
    failureCode: null,
    requestedAtUtc: '2026-08-31T00:00:00Z',
    queuedAtUtc: '2026-08-31T00:00:01Z',
    startedAtUtc: '2026-08-31T00:00:02Z',
    finishedAtUtc: '2026-08-31T00:00:03Z',
    report: {
      id: 'result-464',
      schemaVersion: 1,
      title: 'Project Files Analysis Report',
      bodyMarkdown: '# Project Files Analysis Report\n\n- browser-smoke-task.txt',
      contentSha256: 'b'.repeat(64),
      completedAtUtc: '2026-08-31T00:00:03Z',
    },
  };
}
