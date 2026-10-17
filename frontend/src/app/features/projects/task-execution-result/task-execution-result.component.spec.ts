import { I18nService } from '../../../core/i18n/i18n.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { TaskExecutionResultComponent } from './task-execution-result.component';

const TASK_ID = 'task-463';
const RUN_ID = 'run-463';

// Existing behavior assertions intentionally use the English display locale.
beforeEach(() => { localStorage.setItem('coglatas.locale', 'en'); });
afterEach(() => { localStorage.removeItem('coglatas.locale'); });

describe('TaskExecutionResultComponent', () => {
  let fixture: ComponentFixture<TaskExecutionResultComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskExecutionResultComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(TaskExecutionResultComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('taskId', TASK_ID);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('switches result state and date labels without refetching or translating the saved server report', () => {
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(succeededResult('Succeeded / server-owned report / 本文'));
    fixture.detectChanges();
    const i18n = TestBed.inject(I18nService);
    const native = fixture.nativeElement as HTMLElement;
    const result = fixture.componentInstance.result();
    const report = native.querySelector('[data-testid="task-execution-report"]')!;

    i18n.setLocale('ja');
    fixture.detectChanges();
    expect(native.querySelector('[data-testid="task-execution-result-status"]')?.textContent).toBe('成功');
    expect(report.textContent).toContain('Project Files Analysis Report');
    expect(report.textContent).toContain('Succeeded / server-owned report / 本文');
    expect(report.textContent).toContain(i18n.formatDateTime('2026-08-30T22:00:03Z', { dateStyle: 'medium', timeStyle: 'short' }));
    expect(fixture.componentInstance.result()).toBe(result);
    expect(fixture.componentInstance.result()?.status).toBe('Succeeded');
    expect(http.match(() => true)).toHaveLength(0);

    i18n.setLocale('en');
    fixture.detectChanges();
    expect(native.querySelector('[data-testid="task-execution-result-status"]')?.textContent).toBe('Succeeded');
    expect(native.querySelector('[data-testid="task-execution-report"]')).toBe(report);
    expect(report.textContent).toContain('Succeeded / server-owned report / 本文');
    expect(http.match(() => true)).toHaveLength(0);
  });

  it('loads and renders the durable server report without interpreting Markdown as HTML', () => {
    const request = http.expectOne(`/api/tasks/${TASK_ID}/execution-result`);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush(succeededResult('# Report\n\n<img src=x onerror=alert(1)>'));
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-execution-result-status"]')?.textContent).toContain('Succeeded');
    expect(native.querySelector('[data-testid="task-execution-report-body"]')?.textContent).toContain('<img src=x');
    expect(native.querySelector('[data-testid="task-execution-report"] img')).toBeNull();
    expect(native.textContent).toContain('Report SHA-256');
  });

  it('polls non-terminal state and stops after a terminal response', () => {
    vi.useFakeTimers();

    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(runningResult());
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Authorized Project files are being analyzed.');

    vi.advanceTimersByTime(1500);
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(succeededResult('# Complete'));
    fixture.detectChanges();
    vi.advanceTimersByTime(5000);

    http.expectNone(`/api/tasks/${TASK_ID}/execution-result`);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('durable execution report is ready');
  });

  it('uses a redacted empty state for unauthorized or missing results', () => {
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(
      { error: { message: 'secret/path/report.md' } },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No authorized execution result is available.');
    expect(text).not.toContain('secret/path');
  });

  it('rejects a succeeded projection that has no durable report', () => {
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush({
      ...succeededResult('# Missing'),
      report: null,
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('execution result response was invalid');
  });

  it('keeps direction correction and destructive stop as separate controls', () => {
    setInterventionPermission(true);
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(runningResult());
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-execution-correct-direction"]')).not.toBeNull();
    expect(native.querySelector('[data-testid="task-execution-stop"]')).not.toBeNull();
    expect(native.querySelector('[data-testid="task-execution-correction-help"]')?.textContent).toContain('Task brief');
    expect(native.querySelector('[data-testid="task-execution-correction-help"]')?.textContent).toContain('Research plan');
    expect(native.querySelector('[data-testid="task-execution-correction-help"]')?.textContent).toContain('Active source scope');
    expect(native.querySelector('[data-testid="task-execution-correction-help"]')?.textContent).toContain('new Run from the latest saved Task state');
  });

  it('requires explicit confirmation before sending a destructive stop command', () => {
    setInterventionPermission(true);
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(runningResult());
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    (native.querySelector('[data-testid="task-execution-stop"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(native.querySelector('[data-testid="task-execution-stop-confirmation"]')).not.toBeNull();
    http.expectNone(`/api/tasks/${TASK_ID}/execution-runs/${RUN_ID}/stop`);

    (native.querySelector('[data-testid="task-execution-stop-confirm"]') as HTMLButtonElement).click();
    const stop = http.expectOne(`/api/tasks/${TASK_ID}/execution-runs/${RUN_ID}/stop`);
    expect(stop.request.method).toBe('POST');
    expect(stop.request.withCredentials).toBe(true);
    stop.flush({
      action: 'Stop',
      closedRun: { id: RUN_ID, status: 'Stopped' },
      resumedRun: null,
      resumePoint: 'None',
      editableSurfaces: [],
    });

    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(terminalResult('Stopped'));
    fixture.detectChanges();

    expect(native.querySelector('[data-testid="task-execution-result-status"]')?.textContent).toContain('Stopped');
    expect(native.querySelector('[data-testid="task-execution-intervention-feedback"]')?.textContent).toContain('No successor Run was started');
  });

  it('redirects to a successor run and displays the truthful resume point', () => {
    setInterventionPermission(true);
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(runningResult());
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    (native.querySelector('[data-testid="task-execution-correct-direction"]') as HTMLButtonElement).click();

    const redirect = http.expectOne(`/api/tasks/${TASK_ID}/execution-runs/${RUN_ID}/correct-direction`);
    expect(redirect.request.method).toBe('POST');
    redirect.flush({
      action: 'CorrectDirection',
      closedRun: { id: RUN_ID, status: 'Redirected' },
      resumedRun: { id: 'run-464', status: 'Accepted' },
      resumePoint: 'NewRunFromLatestTaskState',
      editableSurfaces: ['Task brief', 'Research plan', 'Active source scope'],
    });

    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(succeededResult('# Redirected run complete', 'run-464'));
    fixture.detectChanges();

    expect(native.querySelector('[data-testid="task-execution-intervention-feedback"]')?.textContent)
      .toContain('new Run from the latest saved Task state');
    expect(native.querySelector('[data-testid="task-execution-result-status"]')?.textContent).toContain('Succeeded');
  });

  it('does not render intervention controls for users without management permission', () => {
    http.expectOne(`/api/tasks/${TASK_ID}/execution-result`).flush(runningResult());
    fixture.detectChanges();

    const native = fixture.nativeElement as HTMLElement;
    expect(native.querySelector('[data-testid="task-execution-interventions"]')).toBeNull();
    expect(native.querySelector('[data-testid="task-execution-stop"]')).toBeNull();
    expect(native.querySelector('[data-testid="task-execution-correct-direction"]')).toBeNull();
  });

  function setInterventionPermission(canManage: boolean): void {
    fixture.componentRef.setInput('interventionCanManage', canManage);
    fixture.detectChanges();
  }
});

function runningResult(): Record<string, unknown> {
  return {
    runId: RUN_ID,
    status: 'Running',
    failureCode: null,
    requestedAtUtc: '2026-08-30T22:00:00Z',
    queuedAtUtc: '2026-08-30T22:00:01Z',
    startedAtUtc: '2026-08-30T22:00:02Z',
    finishedAtUtc: null,
    report: null,
  };
}

function terminalResult(status: 'Stopped' | 'Redirected'): Record<string, unknown> {
  return {
    runId: RUN_ID,
    status,
    failureCode: null,
    requestedAtUtc: '2026-08-30T22:00:00Z',
    queuedAtUtc: '2026-08-30T22:00:01Z',
    startedAtUtc: '2026-08-30T22:00:02Z',
    finishedAtUtc: '2026-08-30T22:00:03Z',
    report: null,
  };
}

function succeededResult(bodyMarkdown: string, runId = RUN_ID): Record<string, unknown> {
  return {
    runId,
    status: 'Succeeded',
    failureCode: null,
    requestedAtUtc: '2026-08-30T22:00:00Z',
    queuedAtUtc: '2026-08-30T22:00:01Z',
    startedAtUtc: '2026-08-30T22:00:02Z',
    finishedAtUtc: '2026-08-30T22:00:03Z',
    report: {
      id: 'result-463',
      schemaVersion: 1,
      title: 'Project Files Analysis Report',
      bodyMarkdown,
      contentSha256: 'a'.repeat(64),
      completedAtUtc: '2026-08-30T22:00:03Z',
    },
  };
}
