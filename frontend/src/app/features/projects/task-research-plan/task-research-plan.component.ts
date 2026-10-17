import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';

import type { PROJECT_EXECUTION_ENGLISH_MESSAGES } from '../../../core/i18n/messages/project-execution.messages';
import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';
import { normalizeApiError } from '../../../core/api/api-error.adapter';

type ExecutionMessageKey = keyof typeof PROJECT_EXECUTION_ENGLISH_MESSAGES;

type StepStatus = 'Planned' | 'Ready' | 'Blocked' | 'Deferred';

interface PlanStep {
  readonly id: string;
  readonly position: number;
  readonly title: string;
  readonly objective: string;
  readonly scopeSummary: string;
  readonly status: StepStatus;
}

interface PlanRevision {
  readonly id: string;
  readonly number: number;
  readonly createdAtUtc: string;
  readonly steps: readonly PlanStep[];
}

interface ResearchPlan {
  readonly planId: string | null;
  readonly version: number;
  readonly currentRevision: PlanRevision | null;
  readonly canManage: boolean;
}

interface EditableStep {
  readonly baseStepId: string | null;
  readonly title: string;
  readonly objective: string;
  readonly scopeSummary: string;
  readonly status: StepStatus;
}

interface ProposedStep extends EditableStep {
  readonly position: number;
}

interface StepDiff {
  readonly kinds: readonly string[];
  readonly baseStepId: string | null;
  readonly beforePosition: number | null;
  readonly afterPosition: number | null;
  readonly before: PlanStep | null;
  readonly after: ProposedStep | null;
  readonly changedFields: readonly string[];
}

interface ImpactItem {
  readonly kind: string;
  readonly message: string;
  readonly stepPosition: number | null;
}

interface ImpactSummary {
  readonly beforeStepCount: number;
  readonly afterStepCount: number;
  readonly executionStepCountChanged: boolean;
  readonly executionOrderChanged: boolean;
  readonly sourceScopeGuidanceChanged: boolean;
  readonly deliverableAlignmentReviewRequired: boolean;
  readonly items: readonly ImpactItem[];
}

interface PlanPreview {
  readonly baseVersion: number;
  readonly baseRevisionId: string | null;
  readonly baseRevisionNumber: number | null;
  readonly fingerprint: string;
  readonly proposedSteps: readonly ProposedStep[];
  readonly changes: readonly StepDiff[];
  readonly impact: ImpactSummary;
}

const EMPTY_STEP: EditableStep = Object.freeze({
  baseStepId: null,
  title: '',
  objective: '',
  scopeSummary: '',
  status: 'Planned'
});

/**
 * Editor for the current Task-owned Research Plan revision. Reordering uses
 * explicit move buttons so it remains available to keyboard and touch users.
 * Issue #366 requires an authoritative server diff review before a changed
 * draft can be saved as the next immutable revision.
 */

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-research-plan',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './task-research-plan.component.html',
  styleUrl: './task-research-plan.component.scss',
})
export class TaskResearchPlanComponent implements OnChanges, OnDestroy {
  readonly i18n = injectProjectsI18n();
  @Input({ required: true }) taskId = '';
  @Output() readonly dirtyChange = new EventEmitter<boolean>();

  private readonly http = inject(HttpClient, { optional: true });
  private loadRequest: Subscription | null = null;
  private previewRequest: Subscription | null = null;
  private saveRequest: Subscription | null = null;
  private readonly generation = signal(0);
  private readonly state = signal<ResearchPlan | null>(null);
  private readonly savedRevisionNumber = signal<number | null>(null);
  readonly loading = signal(false);
  readonly previewing = signal(false);
  readonly saving = signal(false);
  private readonly errorKey = signal<ExecutionMessageKey | null>(null);
  readonly error = computed(() => {
    const key = this.errorKey();
    return key ? this.i18n.translate(key) : null;
  });
  private readonly feedbackKey = signal<ExecutionMessageKey | null>(null);
  readonly feedback = computed(() => {
    const key = this.feedbackKey();
    return key ? this.i18n.translate(key, { revision: this.i18n.formatNumber(this.savedRevisionNumber() ?? 0) }) : null;
  });
  readonly draft = signal<EditableStep[]>([]);
  readonly loadedDraft = signal<EditableStep[]>([]);
  readonly preview = signal<PlanPreview | null>(null);
  readonly plan = this.state.asReadonly();
  readonly canManage = computed(() => this.state()?.canManage ?? false);
  readonly dirty = computed(() => JSON.stringify(this.draft()) !== JSON.stringify(this.loadedDraft()));
  readonly reviewedChanges = computed(() => this.preview()?.changes.length ?? 0);
  readonly canSaveReviewed = computed(() =>
    this.dirty() &&
    !this.previewing() &&
    !this.saving() &&
    this.preview() !== null &&
    this.reviewedChanges() > 0 &&
    this.preview()!.baseVersion === this.state()?.version);
  readonly statuses: readonly StepStatus[] = ['Planned', 'Ready', 'Blocked', 'Deferred'];

  constructor() {
    effect(() => this.dirtyChange.emit(this.dirty()));
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['taskId']) {this.load();}
  }

  ngOnDestroy(): void {
    this.generation.update(value => value + 1);
    this.loadRequest?.unsubscribe();
    this.previewRequest?.unsubscribe();
    this.saveRequest?.unsubscribe();
  }

  retry(): void { this.load(); }

  addStep(): void {
    if (!this.canManage() || this.busy()) {return;}
    this.invalidateReview();
    this.draft.update(steps => [...steps, { ...EMPTY_STEP }]);
  }

  removeStep(index: number): void {
    if (!this.canManage() || this.busy()) {return;}
    this.invalidateReview();
    this.draft.update(steps => steps.filter((_, candidate) => candidate !== index));
  }

  moveStep(index: number, direction: -1 | 1): void {
    if (!this.canManage() || this.busy()) {return;}
    const next = index + direction;
    this.draft.update(steps => {
      if (next < 0 || next >= steps.length) {return steps;}
      this.invalidateReview();
      const reordered = [...steps];
      [reordered[index], reordered[next]] = [reordered[next], reordered[index]];
      return reordered;
    });
  }

  updateText(index: number, field: 'title' | 'objective' | 'scopeSummary', value: string): void {
    if (!this.canManage() || this.busy()) {return;}
    this.invalidateReview();
    this.draft.update(steps => steps.map((step, candidate) => candidate === index ? { ...step, [field]: value } : step));
  }

  updateStatus(index: number, value: string): void {
    if (!this.canManage() || this.busy() || !this.statuses.includes(value as StepStatus)) {return;}
    this.invalidateReview();
    this.draft.update(steps => steps.map((step, candidate) => candidate === index ? { ...step, status: value as StepStatus } : step));
  }

  discard(): void {
    if (this.busy()) {return;}
    this.preview.set(null);
    this.draft.set(copySteps(this.loadedDraft()));
    this.feedbackKey.set('projectExecution.plan.discarded');
    this.errorKey.set(null);
  }

  reviewChanges(): void {
    const http = this.http;
    const current = this.state();
    if (!http || !current || !current.canManage || this.busy() || !this.dirty() || !this.normalizedTaskId()) {return;}

    this.previewing.set(true);
    this.preview.set(null);
    this.errorKey.set(null);
    this.feedbackKey.set(null);
    const requestGeneration = this.generation();
    this.previewRequest?.unsubscribe();
    this.previewRequest = http.post<unknown>(
      `/api/tasks/${encodeURIComponent(this.normalizedTaskId())}/research-plan/preview`,
      {
        expectedVersion: current.version,
        steps: this.requestSteps()
      },
      { withCredentials: true }).subscribe({
      next: response => {
        if (requestGeneration !== this.generation()) {return;}
        const mapped = mapPreview(response);
        if (mapped?.baseVersion !== current.version) {
          this.errorKey.set('projectExecution.plan.invalidPreview');
          return;
        }
        this.preview.set(mapped);
        this.feedbackKey.set(mapped.changes.length
          ? 'projectExecution.plan.reviewHint'
          : 'projectExecution.plan.sameDraft');
      },
      error: error => {
        if (requestGeneration !== this.generation()) {return;}
        const apiError = normalizeApiError(error);
        this.errorKey.set('projectExecution.plan.reviewFailed');
        this.previewing.set(false);
        if (apiError.httpStatus === 409) {this.load();}
      },
      complete: () => {
        if (requestGeneration === this.generation()) {this.previewing.set(false);}
      }
    });
  }

  save(): void {
    const http = this.http;
    const current = this.state();
    const reviewed = this.preview();
    if (!http || !current || !current.canManage || !reviewed || !this.canSaveReviewed() || !this.normalizedTaskId()) {return;}

    this.saving.set(true);
    this.errorKey.set(null);
    this.feedbackKey.set(null);
    const requestGeneration = this.generation();
    this.saveRequest?.unsubscribe();
    this.saveRequest = http.put<unknown>(
      `/api/tasks/${encodeURIComponent(this.normalizedTaskId())}/research-plan`,
      {
        expectedVersion: current.version,
        steps: this.requestSteps(),
        previewFingerprint: reviewed.fingerprint
      },
      { withCredentials: true }).subscribe({
      next: response => {
        if (requestGeneration !== this.generation()) {return;}
        const plan = mapResearchPlan(response);
        if (!plan) {
          this.errorKey.set('projectExecution.plan.invalidSaved');
          return;
        }
        this.applyPlan(plan);
        this.savedRevisionNumber.set(plan.currentRevision?.number ?? null);
        this.feedbackKey.set('projectExecution.plan.saved');
      },
      error: error => {
        if (requestGeneration !== this.generation()) {return;}
        const apiError = normalizeApiError(error);
        this.errorKey.set('projectExecution.plan.saveFailed');
        this.saving.set(false);
        this.preview.set(null);
        if (apiError.httpStatus === 409) {this.load();}
      },
      complete: () => {
        if (requestGeneration === this.generation()) {this.saving.set(false);}
      }
    });
  }

  private load(): void {
    const http = this.http;
    const taskId = this.normalizedTaskId();
    this.generation.update(value => value + 1);
    const requestGeneration = this.generation();
    this.loadRequest?.unsubscribe();
    this.previewRequest?.unsubscribe();
    this.saveRequest?.unsubscribe();
    this.previewing.set(false);
    this.saving.set(false);
    this.preview.set(null);
    this.errorKey.set(null);
    this.feedbackKey.set(null);
    if (!http || !taskId) {
      this.loading.set(false);
      this.state.set(null);
      this.draft.set([]);
      this.loadedDraft.set([]);
      return;
    }

    this.loading.set(true);
    this.loadRequest = http.get<unknown>(
      `/api/tasks/${encodeURIComponent(taskId)}/research-plan`,
      { withCredentials: true }).subscribe({
      next: response => {
        if (requestGeneration !== this.generation()) {return;}
        const plan = mapResearchPlan(response);
        if (!plan) {
          this.errorKey.set('projectExecution.plan.invalid');
          return;
        }
        this.applyPlan(plan);
      },
      error: error => {
        if (requestGeneration !== this.generation()) {return;}
        this.state.set(null);
        this.draft.set([]);
        this.loadedDraft.set([]);
        this.errorKey.set('projectExecution.plan.loadFailed');
        this.loading.set(false);
      },
      complete: () => {
        if (requestGeneration === this.generation()) {this.loading.set(false);}
      }
    });
  }

  private applyPlan(plan: ResearchPlan): void {
    const steps = (plan.currentRevision?.steps ?? []).map(step => ({
      baseStepId: step.id,
      title: step.title,
      objective: step.objective,
      scopeSummary: step.scopeSummary,
      status: step.status
    }));
    this.state.set(plan);
    this.preview.set(null);
    this.draft.set(copySteps(steps));
    this.loadedDraft.set(copySteps(steps));
  }

  private invalidateReview(): void {
    this.preview.set(null);
    this.feedbackKey.set(null);
    this.errorKey.set(null);
  }

  stepStatusLabel(status: StepStatus): string { return this.i18n.translate(`projectExecution.plan.status.${status}`); }

  changeKindLabel(kind: string): string {
    switch (kind) {
      case 'Added': return this.i18n.translate('projectExecution.plan.change.Added');
      case 'Removed': return this.i18n.translate('projectExecution.plan.change.Removed');
      case 'Modified': return this.i18n.translate('projectExecution.plan.change.Modified');
      case 'Reordered': return this.i18n.translate('projectExecution.plan.change.Reordered');
      default: return kind;
    }
  }

  fieldLabel(field: string): string {
    switch (field) {
      case 'title': return this.i18n.translate('projectExecution.plan.field.title');
      case 'objective': return this.i18n.translate('projectExecution.plan.field.objective');
      case 'scopeSummary': return this.i18n.translate('projectExecution.plan.field.scopeSummary');
      case 'status': return this.i18n.translate('projectExecution.plan.field.status');
      case 'position': return this.i18n.translate('projectExecution.plan.field.position');
      default: return field;
    }
  }

  changeKindsLabel(kinds: readonly string[]): string { return kinds.map(kind => this.changeKindLabel(kind)).join(' + '); }
  changedFieldsLabel(fields: readonly string[]): string { return fields.map(field => this.fieldLabel(field)).join(this.i18n.locale() === 'ja' ? '、' : ', '); }

  private requestSteps(): readonly Record<string, unknown>[] {
    return this.draft().map(step => ({
      title: step.title,
      objective: step.objective,
      scopeSummary: step.scopeSummary,
      status: step.status,
      baseStepId: step.baseStepId
    }));
  }

  private busy(): boolean { return this.previewing() || this.saving(); }
  private normalizedTaskId(): string { return this.taskId.trim(); }
}

function copySteps(steps: readonly EditableStep[]): EditableStep[] {
  return steps.map(step => ({ ...step }));
}

function mapResearchPlan(value: unknown): ResearchPlan | null {
  if (!isRecord(value) || typeof value['version'] !== 'number' || typeof value['canManage'] !== 'boolean') {return null;}
  const revision = mapRevision(value['currentRevision']);
  if (value['currentRevision'] !== null && revision === null) {return null;}
  return {
    planId: typeof value['planId'] === 'string' ? value['planId'] : null,
    version: value['version'],
    currentRevision: revision,
    canManage: value['canManage']
  };
}

function mapRevision(value: unknown): PlanRevision | null {
  if (value === null) {return null;}
  if (!isRecord(value) || typeof value['id'] !== 'string' || typeof value['number'] !== 'number' || typeof value['createdAtUtc'] !== 'string' || !Array.isArray(value['steps'])) {return null;}
  const steps = value['steps'].map(mapStep);
  return steps.every((step): step is PlanStep => step !== null)
    ? { id: value['id'], number: value['number'], createdAtUtc: value['createdAtUtc'], steps }
    : null;
}

function mapStep(value: unknown): PlanStep | null {
  if (!isRecord(value) || typeof value['id'] !== 'string' || typeof value['position'] !== 'number' || typeof value['title'] !== 'string' || typeof value['objective'] !== 'string' || typeof value['scopeSummary'] !== 'string' || !isStepStatus(value['status'])) {return null;}
  return { id: value['id'], position: value['position'], title: value['title'], objective: value['objective'], scopeSummary: value['scopeSummary'], status: value['status'] };
}

function mapProposedStep(value: unknown): ProposedStep | null {
  if (!isRecord(value) || typeof value['position'] !== 'number' || typeof value['title'] !== 'string' || typeof value['objective'] !== 'string' || typeof value['scopeSummary'] !== 'string' || !isStepStatus(value['status'])) {return null;}
  const rawBaseStepId = value['baseStepId'];
  if (rawBaseStepId !== null && typeof rawBaseStepId !== 'string') {return null;}
  const baseStepId: string | null = typeof rawBaseStepId === 'string' ? rawBaseStepId : null;
  return { baseStepId, position: value['position'], title: value['title'], objective: value['objective'], scopeSummary: value['scopeSummary'], status: value['status'] };
}

function mapDiff(value: unknown): StepDiff | null {
  if (!isRecord(value) || !Array.isArray(value['kinds']) || !value['kinds'].every(kind => typeof kind === 'string') || !Array.isArray(value['changedFields']) || !value['changedFields'].every(field => typeof field === 'string')) {return null;}
  const rawBaseStepId = value['baseStepId'];
  const rawBeforePosition = value['beforePosition'];
  const rawAfterPosition = value['afterPosition'];
  if (rawBaseStepId !== null && typeof rawBaseStepId !== 'string') {return null;}
  if (rawBeforePosition !== null && typeof rawBeforePosition !== 'number') {return null;}
  if (rawAfterPosition !== null && typeof rawAfterPosition !== 'number') {return null;}
  const baseStepId: string | null = typeof rawBaseStepId === 'string' ? rawBaseStepId : null;
  const beforePosition: number | null = typeof rawBeforePosition === 'number' ? rawBeforePosition : null;
  const afterPosition: number | null = typeof rawAfterPosition === 'number' ? rawAfterPosition : null;
  const before = value['before'] === null ? null : mapStep(value['before']);
  const after = value['after'] === null ? null : mapProposedStep(value['after']);
  if (value['before'] !== null && before === null) {return null;}
  if (value['after'] !== null && after === null) {return null;}
  return { kinds: value['kinds'], baseStepId, beforePosition, afterPosition, before, after, changedFields: value['changedFields'] };
}

function mapImpact(value: unknown): ImpactSummary | null {
  if (!isRecord(value) || typeof value['beforeStepCount'] !== 'number' || typeof value['afterStepCount'] !== 'number' || typeof value['executionStepCountChanged'] !== 'boolean' || typeof value['executionOrderChanged'] !== 'boolean' || typeof value['sourceScopeGuidanceChanged'] !== 'boolean' || typeof value['deliverableAlignmentReviewRequired'] !== 'boolean' || !Array.isArray(value['items'])) {return null;}
  const items = value['items'].map(item => {
    if (!isRecord(item) || typeof item['kind'] !== 'string' || typeof item['message'] !== 'string') {return null;}
    const rawStepPosition = item['stepPosition'];
    if (rawStepPosition !== null && typeof rawStepPosition !== 'number') {return null;}
    const stepPosition: number | null = typeof rawStepPosition === 'number' ? rawStepPosition : null;
    return { kind: item['kind'], message: item['message'], stepPosition } satisfies ImpactItem;
  });
  if (!items.every((item): item is ImpactItem => item !== null)) {return null;}
  return {
    beforeStepCount: value['beforeStepCount'],
    afterStepCount: value['afterStepCount'],
    executionStepCountChanged: value['executionStepCountChanged'],
    executionOrderChanged: value['executionOrderChanged'],
    sourceScopeGuidanceChanged: value['sourceScopeGuidanceChanged'],
    deliverableAlignmentReviewRequired: value['deliverableAlignmentReviewRequired'],
    items
  };
}

function mapPreview(value: unknown): PlanPreview | null {
  if (!isRecord(value) || typeof value['baseVersion'] !== 'number' || typeof value['fingerprint'] !== 'string' || !Array.isArray(value['proposedSteps']) || !Array.isArray(value['changes'])) {return null;}
  const rawBaseRevisionId = value['baseRevisionId'];
  const rawBaseRevisionNumber = value['baseRevisionNumber'];
  if (rawBaseRevisionId !== null && typeof rawBaseRevisionId !== 'string') {return null;}
  if (rawBaseRevisionNumber !== null && typeof rawBaseRevisionNumber !== 'number') {return null;}
  const baseRevisionId: string | null = typeof rawBaseRevisionId === 'string' ? rawBaseRevisionId : null;
  const baseRevisionNumber: number | null = typeof rawBaseRevisionNumber === 'number' ? rawBaseRevisionNumber : null;
  const proposedSteps = value['proposedSteps'].map(mapProposedStep);
  const changes = value['changes'].map(mapDiff);
  const impact = mapImpact(value['impact']);
  if (!proposedSteps.every((step): step is ProposedStep => step !== null) || !changes.every((change): change is StepDiff => change !== null) || !impact) {return null;}
  return {
    baseVersion: value['baseVersion'],
    baseRevisionId,
    baseRevisionNumber,
    fingerprint: value['fingerprint'],
    proposedSteps,
    changes,
    impact
  };
}

function isStepStatus(value: unknown): value is StepStatus {
  return value === 'Planned' || value === 'Ready' || value === 'Blocked' || value === 'Deferred';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}