import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, Input, OnChanges, OnDestroy, SimpleChanges, computed, inject, signal, viewChild } from '@angular/core';
import { forkJoin, Subscription } from 'rxjs';

import { normalizeApiError } from '../../../core/api/api-error.adapter';
import { RealtimeFacade } from '../../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../../core/realtime/realtime.models';
import { COGLATAS_PROJECTS_MOCK } from '../projects.facade';
import { TaskExecutionResultComponent } from '../task-execution-result/task-execution-result.component';

type ScopeOrigin = 'ProjectDefault' | 'TaskOverride';
type ScopeEditorMode = 'inherit' | 'override';
type RunStatus = 'Accepted' | 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Stopped' | 'Redirected';
type MajorState = RunStatus;
export type SourceState = 'Allow' | 'Prioritize' | 'Exclude';
export type SourceKind = 'Web' | 'WebSite' | 'ProjectFile' | 'ConnectedApp';

type EditorTarget = 'project' | 'task';

interface SourceRule {
  readonly kind: SourceKind;
  readonly sourceId: string;
  readonly state: SourceState;
}

interface SourcePolicyV2 {
  readonly schemaVersion: 2;
  readonly web: SourceState;
  readonly webSite: SourceState;
  readonly projectFile: SourceState;
  readonly connectedApp: SourceState;
  readonly items: readonly SourceRule[];
}

interface SourcePolicy {
  readonly webEnabled: boolean;
  readonly projectFilesEnabled: boolean;
  readonly policyV2: SourcePolicyV2;
}

interface SourceInventoryItem {
  readonly kind: SourceKind;
  readonly sourceId: string;
  readonly label: string;
}

interface ProjectExecutionScope {
  readonly policy: SourcePolicy;
  readonly version: number;
  readonly canManage: boolean;
}

interface LatestExecutionRun {
  readonly status: RunStatus;
  readonly majorState: MajorState;
  readonly snapshotScopeOrigin: ScopeOrigin;
  readonly snapshotWebEnabled: boolean;
  readonly snapshotProjectFilesEnabled: boolean;
  readonly snapshotPolicyV2: SourcePolicyV2 | null;
}

interface TaskExecutionScope {
  readonly effectivePolicy: SourcePolicy;
  readonly origin: ScopeOrigin;
  readonly projectDefaultVersion: number;
  readonly taskOverrideVersion: number | null;
  readonly taskOverridePolicy: SourcePolicy | null;
  readonly canManage: boolean;
  readonly latestRun: LatestExecutionRun | null;
  readonly sourceInventory: readonly SourceInventoryItem[];
}

interface ScopePanelData {
  readonly projectId: string;
  readonly taskId: string;
  readonly project: ProjectExecutionScope;
  readonly task: TaskExecutionScope;
}

const SOURCE_KINDS: readonly SourceKind[] = ['Web', 'WebSite', 'ProjectFile', 'ConnectedApp'];
const SOURCE_STATES: readonly SourceState[] = ['Allow', 'Prioritize', 'Exclude'];
const EMPTY_POLICY_V2: SourcePolicyV2 = Object.freeze({
  schemaVersion: 2,
  web: 'Exclude',
  webSite: 'Exclude',
  projectFile: 'Exclude',
  connectedApp: 'Exclude',
  items: [],
});
const MOCK_POLICY: SourcePolicy = Object.freeze({
  webEnabled: false,
  projectFilesEnabled: false,
  policyV2: EMPTY_POLICY_V2,
});
let componentSequence = 0;

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-execution-scope',
  standalone: true,
  imports: [TaskExecutionResultComponent],
  templateUrl: './task-execution-scope.component.html',
  styleUrl: './task-execution-scope.component.scss',
})
export class TaskExecutionScopeComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) projectId = '';
  @Input({ required: true }) taskId = '';

  private readonly http = inject(HttpClient, { optional: true });
  private readonly realtime = inject(RealtimeFacade, { optional: true });
  private readonly scenario = inject(COGLATAS_PROJECTS_MOCK, { optional: true });
  private readonly owner = `task-execution-scope-${++componentSequence}`;
  private readonly feedbackElement = viewChild<ElementRef<HTMLElement>>('scopeFeedback');
  private readonly detailsElement = viewChild<ElementRef<HTMLElement>>('scopeDetails');
  private readonly state = signal<ScopePanelData | null>(null);
  private readonly scopeGeneration = signal(0);
  readonly activeRead = signal(false);
  private readRequest: Subscription | null = null;
  private mutationRequest: Subscription | null = null;
  private realtimeEvents: Subscription | null = null;
  private unregisterProtectedStateClearer: (() => void) | null = null;

  readonly loadError = signal<string | null>(null);
  readonly mutationError = signal<string | null>(null);
  readonly feedback = signal<string | null>(null);
  readonly saving = signal<'project' | 'task' | null>(null);

  readonly projectWebEnabled = signal(false);
  readonly projectFilesEnabled = signal(false);
  readonly overrideWebEnabled = signal(false);
  readonly overrideProjectFilesEnabled = signal(false);

  readonly projectWebState = signal<SourceState>('Exclude');
  readonly projectWebSiteState = signal<SourceState>('Exclude');
  readonly projectFileState = signal<SourceState>('Exclude');
  readonly projectConnectedAppState = signal<SourceState>('Exclude');
  readonly overrideWebState = signal<SourceState>('Exclude');
  readonly overrideWebSiteState = signal<SourceState>('Exclude');
  readonly overrideFileState = signal<SourceState>('Exclude');
  readonly overrideConnectedAppState = signal<SourceState>('Exclude');
  readonly projectItemRules = signal<readonly SourceRule[]>([]);
  readonly overrideItemRules = signal<readonly SourceRule[]>([]);
  readonly taskEditorMode = signal<ScopeEditorMode>('inherit');

  readonly scope = this.state.asReadonly();
  readonly canManageProject = computed(() => this.state()?.project.canManage ?? false);
  readonly canManageTask = computed(() => this.state()?.task.canManage ?? false);
  readonly canManageAnything = computed(() => this.canManageProject() || this.canManageTask());
  readonly sourceKinds = SOURCE_KINDS;
  readonly sourceStates = SOURCE_STATES;
  readonly detailsId = `${this.owner}-details`;
  readonly eligibleSourceKindCount = computed(() => {
    const policy = this.state()?.task.effectivePolicy.policyV2;
    return policy ? SOURCE_KINDS.filter((kind) => kindHasEligibleSource(policy, kind)).length : 0;
  });
  readonly allowedSourceKindCount = this.eligibleSourceKindCount;

  readonly inventory = computed(() => this.state()?.task.sourceInventory ?? []);
  readonly projectRuleRows = computed(() => ruleRows(this.projectItemRules(), this.inventory()));
  readonly overrideRuleRows = computed(() => ruleRows(this.overrideItemRules(), this.inventory()));

  constructor() {
    this.unregisterProtectedStateClearer = this.realtime?.registerProtectedStateClearer(
      this.owner,
      () => this.clearProtectedState(),
    ) ?? null;
    this.realtimeEvents = this.realtime?.durableEvents$.subscribe((event) => this.handleRealtimeEvent(event)) ?? null;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['projectId'] || changes['taskId']) {this.loadScope();}
  }

  ngOnDestroy(): void {
    this.scopeGeneration.update((generation) => generation + 1);
    this.readRequest?.unsubscribe();
    this.mutationRequest?.unsubscribe();
    this.realtimeEvents?.unsubscribe();
    this.unregisterProtectedStateClearer?.();
  }

  retry(): void { this.loadScope(); }

  focusScopeDetails(): void {
    const details = this.detailsElement()?.nativeElement;
    if (!details) {return;}
    details.focus({ preventScroll: true });
    details.scrollIntoView?.({ block: 'nearest' });
  }

  setTaskEditorMode(mode: ScopeEditorMode): void { this.taskEditorMode.set(mode); }

  setProjectWebEnabled(event: Event): void {
    const enabled = checkboxValue(event);
    this.projectWebEnabled.set(enabled);
    this.projectWebState.set(enabled ? 'Allow' : 'Exclude');
  }

  setProjectFilesEnabled(event: Event): void {
    const enabled = checkboxValue(event);
    this.projectFilesEnabled.set(enabled);
    this.projectFileState.set(enabled ? 'Allow' : 'Exclude');
  }

  setOverrideWebEnabled(event: Event): void {
    const enabled = checkboxValue(event);
    this.overrideWebEnabled.set(enabled);
    this.overrideWebState.set(enabled ? 'Allow' : 'Exclude');
  }

  setOverrideProjectFilesEnabled(event: Event): void {
    const enabled = checkboxValue(event);
    this.overrideProjectFilesEnabled.set(enabled);
    this.overrideFileState.set(enabled ? 'Allow' : 'Exclude');
  }

  setKindState(target: EditorTarget, kind: SourceKind, event: Event): void {
    const state = selectState(event);
    const signalForState = this.stateSignal(target, kind);
    signalForState.set(state);
    if (kind === 'Web') {
      (target === 'project' ? this.projectWebEnabled : this.overrideWebEnabled).set(state !== 'Exclude');
    }
    if (kind === 'ProjectFile') {
      const rules = target === 'project' ? this.projectItemRules() : this.overrideItemRules();
      const enabled = state !== 'Exclude' || rules.some((rule) => rule.kind === 'ProjectFile' && rule.state !== 'Exclude');
      (target === 'project' ? this.projectFilesEnabled : this.overrideProjectFilesEnabled).set(enabled);
    }
  }

  setItemState(target: EditorTarget, kind: SourceKind, sourceId: string, event: Event): void {
    const nextState = selectState(event);
    const rulesSignal = target === 'project' ? this.projectItemRules : this.overrideItemRules;
    const rules = rulesSignal();
    const without = rules.filter((rule) => !(rule.kind === kind && rule.sourceId === sourceId));
    rulesSignal.set([...without, { kind, sourceId, state: nextState }]);
    this.reconcileLegacyFileFlag(target);
  }

  removeItemRule(target: EditorTarget, kind: SourceKind, sourceId: string): void {
    const rulesSignal = target === 'project' ? this.projectItemRules : this.overrideItemRules;
    rulesSignal.set(rulesSignal().filter((rule) => !(rule.kind === kind && rule.sourceId === sourceId)));
    this.reconcileLegacyFileFlag(target);
  }

  addSiteRule(target: EditorTarget, value: string): void {
    const sourceId = canonicalSiteId(value);
    if (!sourceId) {
      this.mutationError.set('Enter a hostname such as docs.example.com.');
      return;
    }
    const rulesSignal = target === 'project' ? this.projectItemRules : this.overrideItemRules;
    if (rulesSignal().some((rule) => rule.kind === 'WebSite' && rule.sourceId === sourceId)) {return;}
    rulesSignal.set([...rulesSignal(), { kind: 'WebSite', sourceId, state: 'Allow' }]);
    this.mutationError.set(null);
  }

  itemState(target: EditorTarget, kind: SourceKind, sourceId: string): SourceState {
    const rules = target === 'project' ? this.projectItemRules() : this.overrideItemRules();
    return rules.find((rule) => rule.kind === kind && rule.sourceId === sourceId)?.state ??
      this.stateSignal(target, kind)();
  }

  kindState(target: EditorTarget, kind: SourceKind): SourceState { return this.stateSignal(target, kind)(); }
  policyState(policy: SourcePolicy, kind: SourceKind): SourceState { return policyDefault(policy.policyV2, kind); }

  stateMeaning(state: SourceState): string {
    switch (state) {
      case 'Allow': return 'Eligible for the next run.';
      case 'Prioritize': return 'Eligible and preferred before ordinary Allow sources.';
      case 'Exclude': return 'Not eligible and never materialized.';
    }
  }

  saveProjectDefault(): void {
    const current = this.state();
    const http = this.http;
    if (!current || !current.project.canManage || !http || typeof http.put !== 'function' || this.saving()) {return;}

    const projectId = this.normalizedProjectId();
    const taskId = this.normalizedTaskId();
    if (!projectId || !taskId) {return;}

    const policyV2 = this.buildEditorPolicy('project');
    const generation = this.scopeGeneration();
    this.mutationRequest?.unsubscribe();
    this.saving.set('project');
    this.mutationError.set(null);
    this.feedback.set(null);
    this.mutationRequest = http.put<unknown>(
      `/api/projects/${encodeURIComponent(projectId)}/execution-scope`,
      {
        webEnabled: legacyWebEnabled(policyV2),
        projectFilesEnabled: legacyProjectFilesEnabled(policyV2),
        expectedVersion: current.project.version,
        policyV2,
      },
      { withCredentials: true },
    ).subscribe({
      next: (response) => {
        try { mapProjectExecutionScope(response); }
        catch (error: unknown) { this.completeMutationFailure(error, generation, projectId, taskId); return; }
        if (!this.isCurrent(generation, projectId, taskId)) {return;}
        this.mutationRequest = null;
        this.saving.set(null);
        this.feedback.set('Project default source policy saved. The Active Scope Summary was refreshed from the server.');
        this.focusFeedbackSoon();
        this.loadScope(true);
      },
      error: (error: unknown) => this.completeMutationFailure(error, generation, projectId, taskId),
    });
  }

  saveTaskScope(): void {
    const current = this.state();
    const http = this.http;
    if (!current || !current.task.canManage || !http || this.saving()) {return;}

    const projectId = this.normalizedProjectId();
    const taskId = this.normalizedTaskId();
    if (!projectId || !taskId) {return;}

    const generation = this.scopeGeneration();
    const mode = this.taskEditorMode();
    const policyV2 = this.buildEditorPolicy('task');
    const request = mode === 'inherit'
      ? http.delete<unknown>(
        `/api/tasks/${encodeURIComponent(taskId)}/execution-scope-override`,
        { body: { expectedVersion: current.task.taskOverrideVersion ?? 0 }, withCredentials: true },
      )
      : http.put<unknown>(
        `/api/tasks/${encodeURIComponent(taskId)}/execution-scope-override`,
        {
          webEnabled: legacyWebEnabled(policyV2),
          projectFilesEnabled: legacyProjectFilesEnabled(policyV2),
          expectedVersion: current.task.taskOverrideVersion ?? 0,
          policyV2,
        },
        { withCredentials: true },
      );

    this.mutationRequest?.unsubscribe();
    this.saving.set('task');
    this.mutationError.set(null);
    this.feedback.set(null);
    this.mutationRequest = request.subscribe({
      next: (response) => {
        try { mapTaskExecutionScope(response); }
        catch (error: unknown) { this.completeMutationFailure(error, generation, projectId, taskId); return; }
        if (!this.isCurrent(generation, projectId, taskId)) {return;}
        this.mutationRequest = null;
        this.saving.set(null);
        this.feedback.set(mode === 'inherit'
          ? 'This Task now inherits the Project default source policy.'
          : 'Task source policy saved as a complete Task override.');
        this.focusFeedbackSoon();
        this.loadScope(true);
      },
      error: (error: unknown) => this.completeMutationFailure(error, generation, projectId, taskId),
    });
  }

  sourceEligibility(enabled: boolean): string { return enabled ? 'Enabled' : 'Disabled'; }
  scopeOriginLabel(origin: ScopeOrigin): string { return origin === 'TaskOverride' ? 'Task override' : 'Project default'; }
  majorStateLabel(state: MajorState): string { return state; }

  runStatusLabel(status: RunStatus): string {
    switch (status) {
      case 'Accepted': return 'Execution request was durably accepted.';
      case 'Queued': return 'Execution is queued for server materialization.';
      case 'Running': return 'Execution is materializing authorized sources.';
      case 'Succeeded': return 'Execution succeeded.';
      case 'Failed': return 'Execution failed.';
      case 'Stopped': return 'Execution was deliberately stopped.';
      case 'Redirected': return 'Execution was closed for direction correction.';
    }
  }

  private buildEditorPolicy(target: EditorTarget): SourcePolicyV2 {
    const web = this.compatibilityState(target, 'Web');
    const projectFile = this.compatibilityState(target, 'ProjectFile');
    const items = [...(target === 'project' ? this.projectItemRules() : this.overrideItemRules())]
      .sort((a, b) => `${a.kind}:${a.sourceId}`.localeCompare(`${b.kind}:${b.sourceId}`));
    return {
      schemaVersion: 2,
      web,
      webSite: this.stateSignal(target, 'WebSite')(),
      projectFile,
      connectedApp: this.stateSignal(target, 'ConnectedApp')(),
      items,
    };
  }

  private compatibilityState(target: EditorTarget, kind: 'Web' | 'ProjectFile'): SourceState {
    const state = this.stateSignal(target, kind)();
    const enabled = kind === 'Web'
      ? (target === 'project' ? this.projectWebEnabled() : this.overrideWebEnabled())
      : (target === 'project' ? this.projectFilesEnabled() : this.overrideProjectFilesEnabled());
    if ((state !== 'Exclude') === enabled) {return state;}
    return enabled ? 'Allow' : 'Exclude';
  }

  private reconcileLegacyFileFlag(target: EditorTarget): void {
    const rules = target === 'project' ? this.projectItemRules() : this.overrideItemRules();
    const state = this.stateSignal(target, 'ProjectFile')();
    const enabled = state !== 'Exclude' || rules.some((rule) => rule.kind === 'ProjectFile' && rule.state !== 'Exclude');
    (target === 'project' ? this.projectFilesEnabled : this.overrideProjectFilesEnabled).set(enabled);
  }

  private stateSignal(target: EditorTarget, kind: SourceKind) {
    if (target === 'project') {
      switch (kind) {
        case 'Web': return this.projectWebState;
        case 'WebSite': return this.projectWebSiteState;
        case 'ProjectFile': return this.projectFileState;
        case 'ConnectedApp': return this.projectConnectedAppState;
      }
    }
    switch (kind) {
      case 'Web': return this.overrideWebState;
      case 'WebSite': return this.overrideWebSiteState;
      case 'ProjectFile': return this.overrideFileState;
      case 'ConnectedApp': return this.overrideConnectedAppState;
    }
  }

  private loadScope(preserveFeedback = false): void {
    const projectId = this.normalizedProjectId();
    const taskId = this.normalizedTaskId();
    this.scopeGeneration.update((generation) => generation + 1);
    const generation = this.scopeGeneration();
    this.readRequest?.unsubscribe();
    this.mutationRequest?.unsubscribe();
    this.mutationRequest = null;
    this.saving.set(null);
    if (!preserveFeedback) {this.feedback.set(null);}
    this.mutationError.set(null);
    this.loadError.set(null);
    const current = this.state();
    if (current?.projectId !== projectId || current.taskId !== taskId) {this.state.set(null);}
    if (!projectId || !taskId) { this.activeRead.set(false); return; }

    if (this.scenario) {
      this.applyScope(
        { policy: MOCK_POLICY, version: 1, canManage: false },
        {
          effectivePolicy: MOCK_POLICY,
          origin: 'ProjectDefault',
          projectDefaultVersion: 1,
          taskOverrideVersion: null,
          taskOverridePolicy: null,
          canManage: false,
          latestRun: null,
          sourceInventory: [],
        },
      );
      this.activeRead.set(false);
      return;
    }

    const http = this.http;
    if (!http || typeof http.get !== 'function') {
      this.activeRead.set(false);
      this.loadError.set('Source-scope settings are unavailable in this view.');
      return;
    }

    this.activeRead.set(true);
    this.readRequest = forkJoin({
      project: http.get<unknown>(`/api/projects/${encodeURIComponent(projectId)}/execution-scope`, { withCredentials: true }),
      task: http.get<unknown>(`/api/tasks/${encodeURIComponent(taskId)}/execution-scope`, { withCredentials: true }),
    }).subscribe({
      next: (response) => {
        try {
          const project = mapProjectExecutionScope(response.project);
          const task = mapTaskExecutionScope(response.task);
          if (!this.isCurrent(generation, projectId, taskId)) {return;}
          this.applyScope(project, task);
          this.activeRead.set(false);
          this.readRequest = null;
        } catch (error: unknown) {
          this.completeLoadFailure(error, generation, projectId, taskId);
        }
      },
      error: (error: unknown) => this.completeLoadFailure(error, generation, projectId, taskId),
    });
  }

  private applyScope(project: ProjectExecutionScope, task: TaskExecutionScope): void {
    this.state.set({ projectId: this.normalizedProjectId(), taskId: this.normalizedTaskId(), project, task });
    this.applyEditorPolicy('project', project.policy.policyV2);
    this.taskEditorMode.set(task.origin === 'TaskOverride' ? 'override' : 'inherit');
    this.applyEditorPolicy('task', (task.taskOverridePolicy ?? task.effectivePolicy).policyV2);
  }

  private applyEditorPolicy(target: EditorTarget, policy: SourcePolicyV2): void {
    this.stateSignal(target, 'Web').set(policy.web);
    this.stateSignal(target, 'WebSite').set(policy.webSite);
    this.stateSignal(target, 'ProjectFile').set(policy.projectFile);
    this.stateSignal(target, 'ConnectedApp').set(policy.connectedApp);
    const rulesSignal = target === 'project' ? this.projectItemRules : this.overrideItemRules;
    rulesSignal.set([...policy.items]);
    (target === 'project' ? this.projectWebEnabled : this.overrideWebEnabled).set(legacyWebEnabled(policy));
    (target === 'project' ? this.projectFilesEnabled : this.overrideProjectFilesEnabled).set(legacyProjectFilesEnabled(policy));
  }

  private completeLoadFailure(error: unknown, generation: number, projectId: string, taskId: string): void {
    if (!this.isCurrent(generation, projectId, taskId)) {return;}
    this.readRequest = null;
    this.activeRead.set(false);
    const normalized = normalizeApiError(error);
    if (normalized.httpStatus === 401 || normalized.httpStatus === 403 || normalized.httpStatus === 404) {
      this.clearProtectedState();
      return;
    }
    this.loadError.set('Source-scope settings could not be loaded. Try again.');
  }

  private completeMutationFailure(error: unknown, generation: number, projectId: string, taskId: string): void {
    if (!this.isCurrent(generation, projectId, taskId)) {return;}
    this.mutationRequest = null;
    this.saving.set(null);
    const normalized = normalizeApiError(error);
    if (normalized.httpStatus === 401 || normalized.httpStatus === 403 || normalized.httpStatus === 404) {
      this.clearProtectedState();
      return;
    }
    this.mutationError.set(normalized.httpStatus === 409
      ? 'The source policy changed before it could be saved. Reload it and try again.'
      : 'The source-policy update could not be completed. Reload it and try again.');
  }

  private clearProtectedState(): void {
    this.scopeGeneration.update((generation) => generation + 1);
    this.readRequest?.unsubscribe();
    this.mutationRequest?.unsubscribe();
    this.readRequest = null;
    this.mutationRequest = null;
    this.state.set(null);
    this.activeRead.set(false);
    this.saving.set(null);
    this.feedback.set(null);
    this.mutationError.set(null);
    this.loadError.set('Source-scope settings are unavailable in the current session.');
    this.projectItemRules.set([]);
    this.overrideItemRules.set([]);
  }

  private isCurrent(generation: number, projectId: string, taskId: string): boolean {
    return generation === this.scopeGeneration() && projectId === this.normalizedProjectId() && taskId === this.normalizedTaskId();
  }
  private normalizedProjectId(): string { return this.projectId.trim(); }
  private normalizedTaskId(): string { return this.taskId.trim(); }
  private focusFeedbackSoon(): void { setTimeout(() => this.feedbackElement()?.nativeElement.focus()); }

  private handleRealtimeEvent(event: DurableRealtimeEvent): void {
    if (event.eventType === 'Security.AuthorizationStateChanged.v1') { this.clearProtectedState(); return; }
    const projectId = this.normalizedProjectId();
    const taskId = this.normalizedTaskId();
    if (!projectId || !taskId || this.saving()) {return;}
    if (event.eventType === 'Projects.ProjectChanged.v1' && event.aggregateId === projectId) { this.loadScope(); return; }
    if (['Projects.TaskChanged.v1', 'Projects.TaskAssignmentChanged.v1', 'Projects.TaskWorkflowChanged.v1', 'Projects.TaskCommentChanged.v1']
      .includes(event.eventType) && event.aggregateId === taskId) {this.loadScope();}
  }
}

function mapProjectExecutionScope(value: unknown): ProjectExecutionScope {
  const record = requiredRecord(value, 'Project execution scope');
  return {
    policy: mapSourcePolicy(record['policy'], 'Project execution scope policy'),
    version: requiredVersion(record['version'], 'Project execution scope version'),
    canManage: requiredBoolean(record['canManage'], 'Project execution scope permission'),
  };
}

function mapTaskExecutionScope(value: unknown): TaskExecutionScope {
  const record = requiredRecord(value, 'Task execution scope');
  const origin = requiredOrigin(record['origin'], 'Task execution scope origin');
  const overridePolicy = nullableSourcePolicy(record['taskOverridePolicy'], 'Task override policy');
  const overrideVersion = nullableVersion(record['taskOverrideVersion'], 'Task override version');
  if ((origin === 'TaskOverride') !== (overridePolicy !== null) || (origin === 'TaskOverride') !== (overrideVersion !== null))
    {throw new Error('Task execution scope response has an inconsistent override state.');}

  return {
    effectivePolicy: mapSourcePolicy(record['effectivePolicy'], 'Task effective source policy'),
    origin,
    projectDefaultVersion: requiredVersion(record['projectDefaultVersion'], 'Project default version'),
    taskOverrideVersion: overrideVersion,
    taskOverridePolicy: overridePolicy,
    canManage: requiredBoolean(record['canManage'], 'Task execution scope permission'),
    latestRun: nullableLatestRun(record['latestRun']),
    sourceInventory: mapInventory(record['sourceInventory']),
  };
}

function nullableLatestRun(value: unknown): LatestExecutionRun | null {
  if (value === null || value === undefined) {return null;}
  const record = requiredRecord(value, 'Latest execution run');
  const status = requiredRunStatus(record['status']);
  return {
    status,
    majorState: record['majorState'] == null ? majorStateFromRunStatus(status) : requiredMajorState(record['majorState']),
    snapshotScopeOrigin: requiredOrigin(record['snapshotScopeOrigin'], 'Latest execution run source origin'),
    snapshotWebEnabled: requiredBoolean(record['snapshotWebEnabled'], 'Latest execution run Web policy'),
    snapshotProjectFilesEnabled: requiredBoolean(record['snapshotProjectFilesEnabled'], 'Latest execution run file policy'),
    snapshotPolicyV2: record['snapshotPolicyV2'] == null ? null : mapPolicyV2(record['snapshotPolicyV2'], 'Latest execution run source policy'),
  };
}

function mapSourcePolicy(value: unknown, label: string): SourcePolicy {
  const record = requiredRecord(value, label);
  const webEnabled = requiredBoolean(record['webEnabled'], `${label} Web setting`);
  const projectFilesEnabled = requiredBoolean(record['projectFilesEnabled'], `${label} Project-files setting`);
  const policyV2 = record['policyV2'] == null
    ? legacyPolicy(webEnabled, projectFilesEnabled)
    : mapPolicyV2(record['policyV2'], `${label} V2`);
  if (legacyWebEnabled(policyV2) !== webEnabled || legacyProjectFilesEnabled(policyV2) !== projectFilesEnabled)
    {throw new Error(`${label} compatibility projection is inconsistent.`);}
  return { webEnabled, projectFilesEnabled, policyV2 };
}

function mapPolicyV2(value: unknown, label: string): SourcePolicyV2 {
  const record = requiredRecord(value, label);
  if (record['schemaVersion'] !== 2) {throw new Error(`${label} schema version is invalid.`);}
  const rawItems = record['items'];
  if (!Array.isArray(rawItems) || rawItems.length > 256) {throw new Error(`${label} item rules are invalid.`);}
  const items = rawItems.map((item, index): SourceRule => {
    const rule = requiredRecord(item, `${label} item ${index}`);
    const sourceId = rule['sourceId'];
    if (typeof sourceId !== 'string' || sourceId.length < 1 || sourceId.length > 256) {throw new Error(`${label} source id is invalid.`);}
    return { kind: requiredSourceKind(rule['kind']), sourceId, state: requiredSourceState(rule['state']) };
  });
  return {
    schemaVersion: 2,
    web: requiredSourceState(record['web']),
    webSite: requiredSourceState(record['webSite']),
    projectFile: requiredSourceState(record['projectFile']),
    connectedApp: requiredSourceState(record['connectedApp']),
    items,
  };
}

function mapInventory(value: unknown): readonly SourceInventoryItem[] {
  if (value === null || value === undefined) {return [];}
  if (!Array.isArray(value) || value.length > 512) {throw new Error('Source inventory response is invalid.');}
  return value.map((item, index) => {
    const record = requiredRecord(item, `Source inventory ${index}`);
    const sourceId = record['sourceId'];
    const label = record['label'];
    if (typeof sourceId !== 'string' || typeof label !== 'string') {throw new Error('Source inventory response is invalid.');}
    return { kind: requiredSourceKind(record['kind']), sourceId, label };
  });
}

function nullableSourcePolicy(value: unknown, label: string): SourcePolicy | null {
  return value == null ? null : mapSourcePolicy(value, label);
}
function legacyPolicy(webEnabled: boolean, projectFilesEnabled: boolean): SourcePolicyV2 {
  return { ...EMPTY_POLICY_V2, web: webEnabled ? 'Allow' : 'Exclude', projectFile: projectFilesEnabled ? 'Allow' : 'Exclude', items: [] };
}
function legacyWebEnabled(policy: SourcePolicyV2): boolean {
  return policy.web !== 'Exclude' || policy.items.some((rule) => rule.kind === 'Web' && rule.state !== 'Exclude');
}
function legacyProjectFilesEnabled(policy: SourcePolicyV2): boolean {
  return policy.projectFile !== 'Exclude' || policy.items.some((rule) => rule.kind === 'ProjectFile' && rule.state !== 'Exclude');
}
function policyDefault(policy: SourcePolicyV2, kind: SourceKind): SourceState {
  switch (kind) {
    case 'Web': return policy.web;
    case 'WebSite': return policy.webSite;
    case 'ProjectFile': return policy.projectFile;
    case 'ConnectedApp': return policy.connectedApp;
  }
}
function kindHasEligibleSource(policy: SourcePolicyV2, kind: SourceKind): boolean {
  return policyDefault(policy, kind) !== 'Exclude'
    || policy.items.some((rule) => rule.kind === kind && rule.state !== 'Exclude');
}
function ruleRows(rules: readonly SourceRule[], inventory: readonly SourceInventoryItem[]) {
  const inventoryMap = new Map<string, SourceInventoryItem>(
    inventory.map((item) => [`${item.kind}:${item.sourceId}`, item]),
  );
  const keys = new Set<string>();
  const rows: Array<SourceInventoryItem & { configured: boolean }> = [];
  for (const item of inventory) {
    const key = `${item.kind}:${item.sourceId}`;
    if (keys.add(key)) {rows.push({ ...item, configured: rules.some((rule) => `${rule.kind}:${rule.sourceId}` === key) });}
  }
  for (const rule of rules) {
    const key = `${rule.kind}:${rule.sourceId}`;
    if (keys.add(key)) {rows.push({ ...(inventoryMap.get(key) ?? { kind: rule.kind, sourceId: rule.sourceId, label: rule.sourceId }), configured: true });}
  }
  return rows.sort((a, b) => `${a.kind}:${a.label}`.localeCompare(`${b.kind}:${b.label}`));
}
function canonicalSiteId(value: string): string | null {
  let host = value.trim().toLowerCase();
  if (!host) {return null;}
  try {
    if (host.includes('://')) {host = new URL(host).hostname.toLowerCase();}
  } catch { return null; }
  host = host.replace(/^site:/, '').replace(/\.$/, '');
  if (!/^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(host)) {return null;}
  return `site:${host}`;
}
function selectState(event: Event): SourceState {
  const value = (event.target as HTMLSelectElement | null)?.value;
  return requiredSourceState(value);
}
function checkboxValue(event: Event): boolean { return (event.target as HTMLInputElement | null)?.checked === true; }
function requiredRecord(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {throw new Error(`${label} response is invalid.`);}
  return value as Record<string, unknown>;
}
function requiredBoolean(value: unknown, label: string): boolean {
  if (typeof value !== 'boolean') {throw new Error(`${label} response is invalid.`);}
  return value;
}
function requiredVersion(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {throw new Error(`${label} response is invalid.`);}
  return value;
}
function nullableVersion(value: unknown, label: string): number | null { return value == null ? null : requiredVersion(value, label); }
function requiredOrigin(value: unknown, label: string): ScopeOrigin {
  if (value === 'ProjectDefault' || value === 'TaskOverride') {return value;}
  throw new Error(`${label} response is invalid.`);
}
function requiredSourceState(value: unknown): SourceState {
  if (value === 'Allow' || value === 'Prioritize' || value === 'Exclude') {return value;}
  throw new Error('Source state response is invalid.');
}
function requiredSourceKind(value: unknown): SourceKind {
  if (value === 'Web' || value === 'WebSite' || value === 'ProjectFile' || value === 'ConnectedApp') {return value;}
  throw new Error('Source kind response is invalid.');
}
function requiredRunStatus(value: unknown): RunStatus {
  if (value === 'Accepted' || value === 'Queued' || value === 'Running' || value === 'Succeeded' || value === 'Failed' || value === 'Stopped' || value === 'Redirected') {return value;}
  throw new Error('Latest execution run status is invalid.');
}
function requiredMajorState(value: unknown): MajorState { return requiredRunStatus(value); }
function majorStateFromRunStatus(status: RunStatus): MajorState { return status; }
