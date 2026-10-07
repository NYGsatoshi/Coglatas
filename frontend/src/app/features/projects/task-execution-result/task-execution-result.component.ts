import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, Input, OnChanges, OnDestroy, SimpleChanges, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { normalizeApiError } from '../../../core/api/api-error.adapter';
import { createCryptographicUuid } from '../../../core/security/client-id';

type RunStatus = 'Accepted' | 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Stopped' | 'Redirected';
type InterventionAction = 'stop' | 'correct';

interface ExecutionRunAcceptance {
  readonly id: string;
  readonly status: RunStatus;
}

interface ExecutionReport {
  readonly id: string;
  readonly schemaVersion: number;
  readonly title: string;
  readonly bodyMarkdown: string;
  readonly contentSha256: string;
  readonly completedAtUtc: string;
}

interface ExecutionResultProjection {
  readonly runId: string;
  readonly status: RunStatus;
  readonly failureCode: string | null;
  readonly requestedAtUtc: string;
  readonly queuedAtUtc: string | null;
  readonly startedAtUtc: string | null;
  readonly finishedAtUtc: string | null;
  readonly report: ExecutionReport | null;
}

interface InterventionRunProjection {
  readonly id: string;
  readonly status: RunStatus;
}

interface InterventionResponse {
  readonly action: 'Stop' | 'CorrectDirection';
  readonly closedRun: InterventionRunProjection;
  readonly resumedRun: InterventionRunProjection | null;
  readonly resumePoint: 'None' | 'NewRunFromLatestTaskState';
  readonly editableSurfaces: readonly string[];
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-execution-result',
  standalone: true,
  templateUrl: './task-execution-result.component.html',
  styleUrl: './task-execution-result.component.scss',
})
export class TaskExecutionResultComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) taskId = '';
  @Input() allowExecutionStart = false;
  @Input() loadExistingResult = true;
  @Input() interventionCanManage = false;

  private readonly http = inject(HttpClient, { optional: true });
  private latestResultRequest: Subscription | null = null;
  private executionCommandRequest: Subscription | null = null;
  private interventionRequest: Subscription | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private commandGeneration = 0;
  private resultGeneration = 0;

  readonly result = signal<ExecutionResultProjection | null>(null);
  readonly loading = signal(false);
  readonly noResult = signal(false);
  readonly error = signal<string | null>(null);
  readonly starting = signal(false);
  readonly startError = signal<string | null>(null);
  readonly startFeedback = signal<string | null>(null);
  readonly canManageInterventions = signal(false);
  readonly intervening = signal<InterventionAction | null>(null);
  readonly interventionError = signal<string | null>(null);
  readonly interventionFeedback = signal<string | null>(null);
  readonly stopConfirmation = signal(false);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['taskId']) {
      this.resetCommandContext();
    }
    if (changes['taskId'] || changes['interventionCanManage']) {
      this.canManageInterventions.set(this.interventionCanManage);
      if (!this.interventionCanManage) {this.stopConfirmation.set(false);}
    }
    if (changes['taskId'] || changes['loadExistingResult']) {
      if (this.loadExistingResult) {
        this.reload();
      } else {
        this.resetWithoutLoad();
      }
    }
  }

  ngOnDestroy(): void {
    this.commandGeneration++;
    this.cancelPending();
  }

  retry(): void {
    this.reload();
  }

  startExecution(): void {
    const taskId = this.taskId.trim();
    const http = this.http;
    if (!this.allowExecutionStart || !taskId || !http || typeof http.post !== 'function' || this.starting() || this.intervening()) {
      return;
    }

    this.commandGeneration++;
    const generation = this.commandGeneration;
    this.cancelResultRequests();
    this.result.set(null);
    this.noResult.set(false);
    this.error.set(null);
    this.startError.set(null);
    this.startFeedback.set(null);
    this.interventionError.set(null);
    this.interventionFeedback.set(null);
    this.stopConfirmation.set(false);
    this.starting.set(true);

    this.executionCommandRequest = http.post<unknown>(
      `/api/tasks/${encodeURIComponent(taskId)}/execution-runs`,
      {},
      {
        headers: { 'Idempotency-Key': createExecutionIdempotencyKey() },
        withCredentials: true,
      },
    ).subscribe({
      next: (response) => {
        if (!this.isCurrentCommand(generation, taskId)) {
          return;
        }

        let accepted: ExecutionRunAcceptance;
        try {
          accepted = mapExecutionRunAcceptance(response);
        } catch {
          this.executionCommandRequest = null;
          this.starting.set(false);
          this.startError.set('The execution acceptance response was invalid.');
          return;
        }

        this.executionCommandRequest = null;
        this.starting.set(false);
        this.startFeedback.set(startFeedbackMessage(accepted.status));
        this.reload();
      },
      error: (error: unknown) => {
        if (!this.isCurrentCommand(generation, taskId)) {
          return;
        }

        this.executionCommandRequest = null;
        this.starting.set(false);
        const normalized = normalizeApiError(error);
        if (normalized.httpStatus === 401 || normalized.httpStatus === 403 || normalized.httpStatus === 404) {
          this.cancelResultRequests();
          this.result.set(null);
          this.noResult.set(true);
          this.startError.set('Task execution is unavailable in the current session.');
          return;
        }

        this.startError.set(
          normalized.httpStatus === 409
            ? 'The execution request could not be reconciled. Start a new request.'
            : 'Task execution could not be started. Try again.',
        );
      },
    });
  }

  canIntervene(status: RunStatus): boolean {
    return this.canManageInterventions() && isIntervenableStatus(status) && !this.starting() && this.intervening() === null;
  }

  interventionUnavailableReason(status: RunStatus): string {
    if (!this.canManageInterventions()) {return 'You do not have permission to intervene in this Task.';}
    if (status === 'Succeeded') {return 'This execution already completed successfully.';}
    if (status === 'Failed') {return 'This execution already ended in a failure state.';}
    if (status === 'Stopped') {return 'This execution was already stopped.';}
    if (status === 'Redirected') {return 'This execution was already redirected to a successor Run.';}
    return '';
  }

  requestStopConfirmation(): void {
    const current = this.result();
    if (!current || !this.canIntervene(current.status)) {return;}
    this.interventionError.set(null);
    this.stopConfirmation.set(true);
  }

  cancelStopConfirmation(): void {
    if (this.intervening() !== 'stop') {this.stopConfirmation.set(false);}
  }

  stopExecution(): void {
    const current = this.result();
    if (!current || !this.canIntervene(current.status) || !this.stopConfirmation()) {return;}
    this.submitIntervention('stop', current.runId);
  }

  correctDirection(): void {
    const current = this.result();
    if (!current || !this.canIntervene(current.status)) {return;}
    this.submitIntervention('correct', current.runId);
  }

  statusMessage(status: RunStatus): string {
    switch (status) {
      case 'Accepted': return 'The execution request was durably accepted.';
      case 'Queued': return 'The execution is queued for server processing.';
      case 'Running': return 'Authorized Project files are being analyzed.';
      case 'Succeeded': return 'The durable execution report is ready.';
      case 'Failed': return 'The execution ended with a safe failure state.';
      case 'Stopped': return 'The execution was deliberately stopped and no successor Run was started.';
      case 'Redirected': return 'This immutable Run was closed by a direction correction.';
    }
  }

  private submitIntervention(action: InterventionAction, runId: string): void {
    const taskId = this.taskId.trim();
    const http = this.http;
    if (!taskId || !runId || !http || typeof http.post !== 'function' || this.intervening()) {return;}

    this.commandGeneration++;
    const generation = this.commandGeneration;
    this.cancelResultRequests();
    this.interventionError.set(null);
    this.interventionFeedback.set(null);
    this.intervening.set(action);

    const command = action === 'stop' ? 'stop' : 'correct-direction';
    this.interventionRequest = http.post<unknown>(
      `/api/tasks/${encodeURIComponent(taskId)}/execution-runs/${encodeURIComponent(runId)}/${command}`,
      {},
      { withCredentials: true },
    ).subscribe({
      next: (response) => {
        if (!this.isCurrentCommand(generation, taskId)) {return;}

        let intervention: InterventionResponse;
        try {
          intervention = mapInterventionResponse(response);
        } catch {
          this.interventionRequest = null;
          this.intervening.set(null);
          this.interventionError.set('The intervention response was invalid. Reload the execution state.');
          return;
        }

        this.interventionRequest = null;
        this.intervening.set(null);
        this.stopConfirmation.set(false);
        this.interventionFeedback.set(action === 'stop'
          ? 'Task execution stopped. No successor Run was started.'
          : `Direction corrected. Resume point: ${resumePointLabel(intervention.resumePoint)}.`);
        this.result.set(null);
        this.noResult.set(false);
        this.reload();
      },
      error: (error: unknown) => {
        if (!this.isCurrentCommand(generation, taskId)) {return;}
        this.interventionRequest = null;
        this.intervening.set(null);
        const normalized = normalizeApiError(error);
        if (normalized.httpStatus === 401 || normalized.httpStatus === 403 || normalized.httpStatus === 404) {
          this.canManageInterventions.set(false);
          this.stopConfirmation.set(false);
          this.interventionError.set('Execution intervention is unavailable in the current session.');
          return;
        }
        this.interventionError.set(normalized.httpStatus === 409
          ? 'The execution changed before the intervention was saved. The latest state has been reloaded.'
          : 'The execution intervention could not be completed. Try again.');
        this.result.set(null);
        this.reload();
      },
    });
  }

  private reload(): void {
    this.cancelResultRequests();
    this.result.set(null);
    this.noResult.set(false);
    this.error.set(null);
    this.startError.set(null);
    this.load(this.resultGeneration);
  }

  private resetWithoutLoad(): void {
    this.cancelResultRequests();
    this.result.set(null);
    this.noResult.set(false);
    this.error.set(null);
    this.startError.set(null);
    this.startFeedback.set(null);
    this.interventionError.set(null);
    this.interventionFeedback.set(null);
    this.stopConfirmation.set(false);
  }

  private load(generation: number): void {
    const taskId = this.taskId.trim();
    const http = this.http;
    if (!taskId || !http || typeof http.get !== 'function') {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.latestResultRequest = http.get<unknown>(
      `/api/tasks/${encodeURIComponent(taskId)}/execution-result`,
      { withCredentials: true },
    ).subscribe({
      next: (response) => {
        if (generation !== this.resultGeneration || taskId !== this.taskId.trim()) {
          return;
        }

        try {
          const result = mapExecutionResult(response);
          this.result.set(result);
          this.noResult.set(false);
          this.error.set(null);
          this.loading.set(false);
          this.latestResultRequest = null;
          if (isIntervenableStatus(result.status)) {
            this.pollTimer = setTimeout(() => this.load(generation), 1500);
          }
        } catch {
          this.loading.set(false);
          this.latestResultRequest = null;
          this.error.set('The execution result response was invalid.');
        }
      },
      error: (error: unknown) => {
        if (generation !== this.resultGeneration || taskId !== this.taskId.trim()) {
          return;
        }

        this.loading.set(false);
        this.latestResultRequest = null;
        const normalized = normalizeApiError(error);
        if (normalized.httpStatus === 401 || normalized.httpStatus === 403 || normalized.httpStatus === 404) {
          this.result.set(null);
          this.error.set(null);
          this.noResult.set(true);
          return;
        }

        this.error.set('The execution result could not be loaded. Try again.');
      },
    });
  }

  private cancelResultRequests(): void {
    // Projection refreshes invalidate reads and polling, never an active command.
    this.resultGeneration++;
    this.latestResultRequest?.unsubscribe();
    this.latestResultRequest = null;
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
    this.loading.set(false);
  }

  private resetCommandContext(): void {
    this.commandGeneration++;
    this.cancelPending();
    this.startFeedback.set(null);
    this.interventionError.set(null);
    this.interventionFeedback.set(null);
    this.stopConfirmation.set(false);
  }

  private cancelPending(): void {
    this.cancelResultRequests();
    this.executionCommandRequest?.unsubscribe();
    this.executionCommandRequest = null;
    this.starting.set(false);
    this.interventionRequest?.unsubscribe();
    this.interventionRequest = null;
    this.intervening.set(null);
  }

  private isCurrentCommand(generation: number, taskId: string): boolean {
    return generation === this.commandGeneration && taskId === this.taskId.trim();
  }
}

function isIntervenableStatus(status: RunStatus): boolean {
  return status === 'Accepted' || status === 'Queued' || status === 'Running';
}

function mapExecutionRunAcceptance(value: unknown): ExecutionRunAcceptance {
  const record = requiredRecord(value, 'Task execution acceptance');
  return {
    id: requiredString(record['id'], 'Run identity'),
    status: requiredStatus(record['status']),
  };
}

function startFeedbackMessage(status: RunStatus): string {
  switch (status) {
    case 'Accepted': return 'Execution request accepted. The durable result will refresh from the server.';
    case 'Queued': return 'Execution queued. The durable result will refresh from the server.';
    case 'Running': return 'Execution started. The durable result will refresh from the server.';
    case 'Succeeded': return 'Execution completed. The durable report is loading from the server.';
    case 'Failed': return 'Execution completed with a bounded server failure state.';
    case 'Stopped': return 'Execution was stopped before the start response completed.';
    case 'Redirected': return 'Execution was redirected before the start response completed.';
  }
}

function createExecutionIdempotencyKey(): string {
  return `task-execution-ui-${createCryptographicUuid()}`;
}

function mapExecutionResult(value: unknown): ExecutionResultProjection {
  const record = requiredRecord(value, 'Task execution result');
  const status = requiredStatus(record['status']);
  const report = nullableReport(record['report']);
  if (status === 'Succeeded' && report === null) {
    throw new Error('A succeeded execution requires a durable report.');
  }
  if (status !== 'Succeeded' && report !== null) {
    throw new Error('A non-succeeded execution cannot expose a report.');
  }

  return {
    runId: requiredString(record['runId'], 'Run identity'),
    status,
    failureCode: nullableString(record['failureCode'], 'Failure code', 100),
    requestedAtUtc: requiredString(record['requestedAtUtc'], 'Requested timestamp'),
    queuedAtUtc: nullableString(record['queuedAtUtc'], 'Queued timestamp'),
    startedAtUtc: nullableString(record['startedAtUtc'], 'Started timestamp'),
    finishedAtUtc: nullableString(record['finishedAtUtc'], 'Finished timestamp'),
    report,
  };
}

function mapInterventionResponse(value: unknown): InterventionResponse {
  const record = requiredRecord(value, 'Task execution intervention');
  const action = record['action'];
  if (action !== 'Stop' && action !== 'CorrectDirection') {throw new Error('Intervention action is invalid.');}
  const closedRun = mapInterventionRun(record['closedRun'], 'Closed Run');
  const resumedRun = record['resumedRun'] == null ? null : mapInterventionRun(record['resumedRun'], 'Resumed Run');
  const resumePoint = record['resumePoint'];
  if (resumePoint !== 'None' && resumePoint !== 'NewRunFromLatestTaskState') {throw new Error('Resume point is invalid.');}
  if (action === 'Stop' && (closedRun.status !== 'Stopped' || resumedRun !== null || resumePoint !== 'None')) {
    throw new Error('Stop intervention response is inconsistent.');
  }
  if (action === 'CorrectDirection' && (closedRun.status !== 'Redirected' || resumedRun === null || resumePoint !== 'NewRunFromLatestTaskState')) {
    throw new Error('Direction correction response is inconsistent.');
  }
  return {
    action,
    closedRun,
    resumedRun,
    resumePoint,
    editableSurfaces: requiredStringArray(record['editableSurfaces'], 'Editable surfaces'),
  };
}

function mapInterventionRun(value: unknown, label: string): InterventionRunProjection {
  const record = requiredRecord(value, label);
  return {
    id: requiredString(record['id'], `${label} identity`),
    status: requiredStatus(record['status']),
  };
}

function resumePointLabel(resumePoint: InterventionResponse['resumePoint']): string {
  return resumePoint === 'NewRunFromLatestTaskState'
    ? 'new Run from the latest saved Task state'
    : 'none';
}

function nullableReport(value: unknown): ExecutionReport | null {
  if (value === null || value === undefined) {
    return null;
  }

  const record = requiredRecord(value, 'Task execution report');
  const schemaVersion = record['schemaVersion'];
  if (schemaVersion !== 1) {
    throw new Error('Task execution report schema is unsupported.');
  }

  const contentSha256 = requiredString(record['contentSha256'], 'Report hash', 64);
  if (!/^[0-9a-f]{64}$/.test(contentSha256)) {
    throw new Error('Task execution report hash is invalid.');
  }

  return {
    id: requiredString(record['id'], 'Report identity'),
    schemaVersion,
    title: requiredString(record['title'], 'Report title', 200),
    bodyMarkdown: requiredString(record['bodyMarkdown'], 'Report body', 20_000),
    contentSha256,
    completedAtUtc: requiredString(record['completedAtUtc'], 'Report completion timestamp'),
  };
}

function requiredRecord(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} is invalid.`);
  }
  return value as Record<string, unknown>;
}

function requiredStatus(value: unknown): RunStatus {
  if (value === 'Accepted' || value === 'Queued' || value === 'Running' || value === 'Succeeded' || value === 'Failed' || value === 'Stopped' || value === 'Redirected') {
    return value;
  }
  throw new Error('Task execution status is invalid.');
}

function requiredStringArray(value: unknown, label: string): readonly string[] {
  if (!Array.isArray(value) || value.length > 10) {throw new Error(`${label} is invalid.`);}
  return value.map((item, index) => requiredString(item, `${label} ${index + 1}`, 100));
}

function requiredString(value: unknown, label: string, maximumLength = 200): string {
  if (typeof value !== 'string' || value.length === 0 || value.length > maximumLength) {
    throw new Error(`${label} is invalid.`);
  }
  return value;
}

function nullableString(value: unknown, label: string, maximumLength = 200): string | null {
  return value === null || value === undefined ? null : requiredString(value, label, maximumLength);
}
