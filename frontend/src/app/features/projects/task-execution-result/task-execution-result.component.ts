import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  inject,
  signal,
  computed,
} from '@angular/core';
import { Subscription } from 'rxjs';

import type { PROJECT_EXECUTION_ENGLISH_MESSAGES } from '../../../core/i18n/messages/project-execution.messages';
import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';
import { normalizeApiError } from '../../../core/api/api-error.adapter';
import { createCryptographicUuid } from '../../../core/security/client-id';

type ExecutionMessageKey = keyof typeof PROJECT_EXECUTION_ENGLISH_MESSAGES;

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
  readonly i18n = injectProjectsI18n();
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

  private readonly resumePointKey = signal<ExecutionMessageKey>('projectExecution.result.resumeNone');
  readonly result = signal<ExecutionResultProjection | null>(null);
  readonly loading = signal(false);
  readonly noResult = signal(false);
  private readonly errorKey = signal<ExecutionMessageKey | null>(null);
  readonly error = computed(() => {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : null;
  });
  readonly starting = signal(false);
  private readonly startErrorKey = signal<ExecutionMessageKey | null>(null);
  readonly startError = computed(() => {
    const key = this.startErrorKey();
    return key ? this.i18n.translate(key) : null;
  });
  private readonly startFeedbackKey = signal<ExecutionMessageKey | null>(null);
  readonly startFeedback = computed(() => {
    const key = this.startFeedbackKey();
    return key ? this.i18n.translate(key) : null;
  });
  readonly canManageInterventions = signal(false);
  readonly intervening = signal<InterventionAction | null>(null);
  private readonly interventionErrorKey = signal<ExecutionMessageKey | null>(null);
  readonly interventionError = computed(() => {
    const key = this.interventionErrorKey();
    return key ? this.i18n.translate(key) : null;
  });
  private readonly interventionFeedbackKey = signal<ExecutionMessageKey | null>(null);
  readonly interventionFeedback = computed(() => {
    const key = this.interventionFeedbackKey();
    return key ? this.i18n.translate(key, { point: this.i18n.translate(this.resumePointKey()) }) : null;
  });
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
    this.errorKey.set(null);
    this.startErrorKey.set(null);
    this.startFeedbackKey.set(null);
    this.interventionErrorKey.set(null);
    this.interventionFeedbackKey.set(null);
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
          this.startErrorKey.set('projectExecution.result.invalidAcceptance');
          return;
        }

        this.executionCommandRequest = null;
        this.starting.set(false);
        this.startFeedbackKey.set(startFeedbackMessage(accepted.status));
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
          this.startErrorKey.set('projectExecution.result.session');
          return;
        }

        this.startErrorKey.set(
          normalized.httpStatus === 409
            ? 'projectExecution.result.startConflict'
            : 'projectExecution.result.startFailed',
        );
      },
    });
  }

  canIntervene(status: RunStatus): boolean {
    return this.canManageInterventions() && isIntervenableStatus(status) && !this.starting() && this.intervening() === null;
  }

  interventionUnavailableReason(status: RunStatus): string {
    if (!this.canManageInterventions()) {return this.i18n.translate('projectExecution.result.noPermission');}
    if (status === 'Succeeded') {return this.i18n.translate('projectExecution.result.alreadySucceeded');}
    if (status === 'Failed') {return this.i18n.translate('projectExecution.result.alreadyFailed');}
    if (status === 'Stopped') {return this.i18n.translate('projectExecution.result.alreadyStopped');}
    if (status === 'Redirected') {return this.i18n.translate('projectExecution.result.alreadyRedirected');}
    return '';
  }

  requestStopConfirmation(): void {
    const current = this.result();
    if (!current || !this.canIntervene(current.status)) {return;}
    this.interventionErrorKey.set(null);
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

  runStatusLabel(status: RunStatus): string { return this.i18n.translate(`projectExecution.run.${status}`); }

  statusMessage(status: RunStatus): string {
    switch (status) {
      case 'Accepted': return this.i18n.translate('projectExecution.result.status.Accepted');
      case 'Queued': return this.i18n.translate('projectExecution.result.status.Queued');
      case 'Running': return this.i18n.translate('projectExecution.result.status.Running');
      case 'Succeeded': return this.i18n.translate('projectExecution.result.status.Succeeded');
      case 'Failed': return this.i18n.translate('projectExecution.result.status.Failed');
      case 'Stopped': return this.i18n.translate('projectExecution.result.status.Stopped');
      case 'Redirected': return this.i18n.translate('projectExecution.result.status.Redirected');
    }
  }

  private submitIntervention(action: InterventionAction, runId: string): void {
    const taskId = this.taskId.trim();
    const http = this.http;
    if (!taskId || !runId || !http || typeof http.post !== 'function' || this.intervening()) {return;}

    this.commandGeneration++;
    const generation = this.commandGeneration;
    this.cancelResultRequests();
    this.interventionErrorKey.set(null);
    this.interventionFeedbackKey.set(null);
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
          this.interventionErrorKey.set('projectExecution.result.invalidIntervention');
          return;
        }

        this.interventionRequest = null;
        this.intervening.set(null);
        this.stopConfirmation.set(false);
        this.resumePointKey.set(intervention.resumePoint === 'NewRunFromLatestTaskState' ? 'projectExecution.result.resumeLatest' : 'projectExecution.result.resumeNone');
        this.interventionFeedbackKey.set(action === 'stop'
          ? 'projectExecution.result.stopped'
          : 'projectExecution.result.corrected');
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
          this.interventionErrorKey.set('projectExecution.result.interventionSession');
          return;
        }
        this.interventionErrorKey.set(normalized.httpStatus === 409
          ? 'projectExecution.result.interventionConflict'
          : 'projectExecution.result.interventionFailed');
        this.result.set(null);
        this.reload();
      },
    });
  }

  private reload(): void {
    this.cancelResultRequests();
    this.result.set(null);
    this.noResult.set(false);
    this.errorKey.set(null);
    this.startErrorKey.set(null);
    this.load(this.resultGeneration);
  }

  private resetWithoutLoad(): void {
    this.cancelResultRequests();
    this.result.set(null);
    this.noResult.set(false);
    this.errorKey.set(null);
    this.startErrorKey.set(null);
    this.startFeedbackKey.set(null);
    this.interventionErrorKey.set(null);
    this.interventionFeedbackKey.set(null);
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
          this.errorKey.set(null);
          this.loading.set(false);
          this.latestResultRequest = null;
          if (isIntervenableStatus(result.status)) {
            this.pollTimer = setTimeout(() => this.load(generation), 1500);
          }
        } catch {
          this.loading.set(false);
          this.latestResultRequest = null;
          this.errorKey.set('projectExecution.result.invalidResult');
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
          this.errorKey.set(null);
          this.noResult.set(true);
          return;
        }

        this.errorKey.set('projectExecution.result.loadFailed');
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
    this.startFeedbackKey.set(null);
    this.interventionErrorKey.set(null);
    this.interventionFeedbackKey.set(null);
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

function startFeedbackMessage(status: RunStatus): ExecutionMessageKey {
  switch (status) {
    case 'Accepted': return 'projectExecution.result.start.Accepted';
    case 'Queued': return 'projectExecution.result.start.Queued';
    case 'Running': return 'projectExecution.result.start.Running';
    case 'Succeeded': return 'projectExecution.result.start.Succeeded';
    case 'Failed': return 'projectExecution.result.start.Failed';
    case 'Stopped': return 'projectExecution.result.start.Stopped';
    case 'Redirected': return 'projectExecution.result.start.Redirected';
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
