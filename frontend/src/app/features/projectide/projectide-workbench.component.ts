import { SimulationEvent } from './projectide-simulation.reader';
import type { ScopedSourceRegion } from './projectide-scoped-sources';
import { PreparedScenario } from './projectide-scenario';
import { PreparedTestEdit } from './projectide-test-edit';
import { LocalTestAssertion } from './projectide-tests.contracts';
import { DOCUMENT, JsonPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { I18nService, TranslationKey } from '../../core/i18n/i18n.service';
import {
  PROJECTIDE_ENGLISH_MESSAGES,
  PROJECTIDE_JAPANESE_MESSAGES,
} from '../../core/i18n/messages/projectide.messages';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { BoundReference, Diagnostic, HistoryRevisionView, InspectedSymbol, PreparedDraftRename, PreparedDraftRevert, ReferenceCompletion, RequirementTaskSuggestions, SemanticChange, SourceRecord, assessLocalEvidence, contextRevision } from './projectide.contracts';
import { ProjectIdePreviewAdapter } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

@Component({
  selector: 'app-projectide-workbench',
  standalone: true,
  imports: [RouterLink, JsonPipe],
  providers: [ProjectIdeWorkbenchStore, ProjectIdePreviewAdapter],
  templateUrl: './projectide-workbench.component.html',
  styleUrl: './projectide-workbench.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectIdeWorkbenchComponent {
  readonly contextRevision = contextRevision;
  async loadScopedSources(): Promise<void> {
    this.focusGeneration++;
    if (!this.canRead()) { this.store.clear('denied'); return; }
    await this.store.loadScopedSources();
    if (!this.canRead()) this.store.clear('denied');
  }
  openScopedSource(record: SourceRecord): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateScopedSource(record)) this.open('source', true);
  }
  openScopedDiagnostic(region: ScopedSourceRegion, diagnostic: Diagnostic): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateScopedDiagnostic(region, diagnostic)) this.open('source', true);
  }

  readonly store = inject(ProjectIdeWorkbenchStore);
  readonly flags = inject(FrontendFeatureFlagsService);
  readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthSessionFacade);
  private readonly workspace = inject(ActiveWorkspaceFacade);
  private readonly adapter = inject(ProjectIdePreviewAdapter);
  private readonly realtime = inject(RealtimeFacade);
  private readonly document = inject(DOCUMENT);
  readonly panel = signal<'overview' | 'source' | 'problems' | 'inspector' | 'diff' | 'history' | 'tests' | 'scenario'>('overview');
  readonly testPage = signal(0);
  readonly testRows = computed(() => this.store.localTests()?.run.tests.slice(this.testPage() * 32, (this.testPage() + 1) * 32) ?? []);
  readonly diffPage = signal(0);
  readonly historyPage = signal(0);
  readonly historyRows = computed(() => this.store.history()?.revisions.slice(this.historyPage() * 32, (this.historyPage() + 1) * 32) ?? []);
  readonly diffRows = computed(() => this.store.semanticDiff()?.comparison.changes.slice(this.diffPage() * 32, (this.diffPage() + 1) * 32) ?? []);
  readonly severity = signal('');
  readonly kind = signal('');
  readonly search = signal('');
  readonly heading = viewChild<ElementRef<HTMLElement>>('panelHeading');
  private readonly sourceTarget = viewChild<ElementRef<HTMLElement>>('sourceTarget');
  private readonly scopedTargetElement = viewChild<ElementRef<HTMLElement>>('scopedTarget');
  private readonly inspectorButton = viewChild<ElementRef<HTMLButtonElement>>('inspectorButton');
  private focusGeneration = 0;
  readonly tabs = ['overview', 'source', 'problems', 'inspector', 'diff', 'history', 'tests', 'scenario'] as const;
  readonly canRead = computed(
    () =>
      this.flags.projectIdePreviewEnabled() &&
      this.auth.isAuthenticated() &&
      this.auth.currentTenant()?.isAvailable === true &&
      this.auth.session().capabilities.includes('projects:view'),
  );
  readonly diagnostics = computed(() => this.store.result()?.result.diagnostics ?? []);
  readonly kinds = computed(() => [...new Set(this.diagnostics().map((d) => d.ruleId))]);
  readonly filtered = computed(() =>
    this.diagnostics().filter(
      (d) =>
        (!this.severity() || d.severity === this.severity()) &&
        (!this.kind() || d.ruleId === this.kind()) &&
        `${d.code} ${d.ruleId} ${d.diagnosticId} ${d.primaryLocation.entityId ?? ''} ${d.primaryLocation.relationId ?? ''}`
          .toLowerCase()
          .includes(this.search().toLowerCase()),
    ),
  );
  readonly evidenceState = () => {
    const result = this.store.result();
    // The selected case instance belongs to this load generation; a reused key does not.
    return result && result !== this.store.context() ? 'stale' : assessLocalEvidence(result, this.store.hasVerifiedPublication());
  };
  readonly completePass = () => this.evidenceState() === 'complete-local-pass';
  readonly evidenceMessage = () => {
    const keys = {
      'not-loaded': 'ide.evidenceNotLoaded',
      stale: 'ide.evidenceStale',
      'not-executed': 'ide.evidenceNotExecuted',
      'partial-coverage': 'ide.evidencePartial',
      'missing-evidence': 'ide.evidenceMissing',
      'not-passed': 'ide.evidenceNotPassed',
      'complete-local-pass': 'ide.evidenceLocalPass',
    } as const;
    return this.t(keys[this.evidenceState()]);
  };
  readonly records = computed(() => [
    ...(this.store.context()?.declarations ?? []),
    ...(this.store.context()?.relations ?? []),
  ]);
  readonly related = this.store.relatedDiagnostics;
  readonly t = (
    key: keyof typeof PROJECTIDE_ENGLISH_MESSAGES,
    parameters: Record<string, string | number> = {},
  ) => this.i18n.translate(key as TranslationKey, parameters);
  constructor() {
    effect(() => { this.store.localTests(); this.testPage.set(0); });
    this.i18n.registerMessages(PROJECTIDE_ENGLISH_MESSAGES, PROJECTIDE_JAPANESE_MESSAGES);
    const unregister = this.realtime.registerProtectedStateClearer('projectide-preview', () => {
      this.focusGeneration++;
      this.store.clear('denied');
      this.panel.set('overview');
    });
    inject(DestroyRef).onDestroy(() => {
      this.focusGeneration++;
      unregister();
      this.store.clear();
    });
    effect(() => {
      // Identity and active Workspace are invalidation boundaries, never fixture authority.
      this.auth.currentTenant()?.tenantId;
      this.auth.currentUser()?.userId;
      this.workspace.activeWorkspace()?.id;
      const allowed = this.canRead();
      this.focusGeneration++;
      this.store.clear(allowed ? 'idle' : 'denied');
      this.panel.set('overview');
      if (allowed) {
        void this.reload();
      }
    });
  }
  reload(profile: 'main' | 'test-edit' | 'scenario' | 'plan' | 'simulation' = 'main'): Promise<void> {
    this.focusGeneration++;
    this.panel.set('overview');
    this.severity.set('');
    this.kind.set('');
    this.search.set('');
    if (!this.canRead()) {
      this.store.clear('denied');
      return Promise.resolve();
    }
    return this.store.load((signal) =>
      this.adapter.read(
        new URL(profile === 'simulation' ? 'assets/projectide/simulation-v1.json' : profile === 'plan' ? 'assets/projectide/execution-plan-v1.json' : profile === 'scenario' ? 'assets/projectide/scenario-v1.json' : profile === 'test-edit' ? 'assets/projectide/test-edit-v1.json' : 'assets/projectide/preview-v1.json', this.document.baseURI).href,
        signal,
      ),
    );
  }
  async loadSimulation(): Promise<void> {
    this.focusGeneration++;
    if (!this.canRead()) { this.store.clear('denied'); return; }
    await this.store.loadSimulation();
    if (!this.canRead()) this.store.clear('denied');
  }
  openSimulationEvent(event: SimulationEvent): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateSimulationEvent(event)) this.open('source', true);
  }
  choose(key: string): void {
    this.focusGeneration++;
    this.store.choose(key);
    this.panel.set('overview');
    this.severity.set('');
    this.kind.set('');
    this.search.set('');
    this.diffPage.set(0);
    this.historyPage.set(0);
  }
  navigateDiff(change: SemanticChange, side: 'before' | 'after'): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateDiff(change, side)) { this.diffPage.set(0); this.open('source', true); }
  }
  openHistoryRevision(revision: HistoryRevisionView): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.openHistoryRevision(revision)) { this.historyPage.set(0); this.open('source', true); }
  }
  loadPreparedRevert(prepared: PreparedDraftRevert): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.loadPreparedRevert(prepared)) this.open('source', true);
  }
  open(panel: (typeof this.tabs)[number], focus = false): void {
    const generation = ++this.focusGeneration;
    const target = this.store.sourceLocation();
    const scoped = this.store.scopedSourceTarget();
    this.panel.set(panel);
    if (focus) {
      setTimeout(() => {
        if (generation === this.focusGeneration && this.panel() === panel && this.canRead() &&
            (panel !== 'source' || this.store.sourceLocation() === target && this.store.scopedSourceTarget() === scoped)) {
          (panel === 'source' && scoped ? this.scopedTargetElement() : panel === 'source' && this.store.sourceTarget()
            ? this.sourceTarget()
            : this.heading()
          )?.nativeElement.focus();
        }
      });
    }
  }
  inspectDiagnostic(diagnostic: Diagnostic): void {
    this.focusGeneration++;
    if (this.store.selectDiagnostic(diagnostic)) this.open('inspector', true);
  }
  inspectRecord(record: SourceRecord): void {
    this.store.select(
      record.recordType === 'entity'
        ? { kind: 'entity', id: record.identity }
        : { kind: 'relation', id: record.identity },
    );
    this.open('inspector', true);
  }
  navigate(toDiagnosticSymbol = false): void {
    this.focusGeneration++;
    const navigated = toDiagnosticSymbol ? this.store.navigateDiagnosticSymbol() : this.store.navigate();
    if (navigated) {
      this.open('source', true);
    }
  }
  findReferences(symbol: InspectedSymbol): void {
    this.focusGeneration++;
    if (this.canRead()) void this.store.findReferences(symbol);
    else this.store.clear('denied');
  }
  navigateCompletion(page: ReferenceCompletion, candidate: ReferenceCompletion['candidates'][number]): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateCompletion(page, candidate)) this.open('source', true);
  }
  navigateTaskSuggestion(page: RequirementTaskSuggestions, candidate: RequirementTaskSuggestions['candidates'][number]): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateTaskSuggestion(page, candidate)) this.open('source', true);
  }
  loadPreparedRename(prepared: PreparedDraftRename): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.loadPreparedRename(prepared)) this.open('source', true);
  }
  inspectReference(reference: BoundReference): void {
    this.focusGeneration++;
    if (this.canRead()) this.store.selectReference(reference);
    else this.store.clear('denied');
  }
  navigateReference(destination: 'origin' | 'definition'): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateReference(destination)) this.open('source', true);
  }
  readonly scenarioRows = computed(() => {
    const admitted = this.store.scenario();
    return admitted ? [admitted.baseline, admitted.scenario, admitted.draft].map((value, index) => ({ value, tests: admitted.tests[index],
      label: this.t((['ide.scenarioBaseline', 'ide.scenarioHypothesis', 'ide.scenarioDraft'] as const)[index]) })) : [];
  });
  async loadScenarioComparison(prepared: PreparedScenario): Promise<void> {
    const generation = ++this.focusGeneration;
    if (!this.canRead()) { this.store.clear('denied'); return; }
    const loaded = await this.store.loadScenarioComparison(prepared);
    if (!this.canRead()) { this.store.clear('denied'); return; }
    if (loaded && generation === this.focusGeneration) this.open('scenario', true);
  }
  async loadScenarioStage(prepared: PreparedScenario, stage: 'baseline' | 'scenario' | 'draft'): Promise<void> {
    const generation = ++this.focusGeneration;
    if (!this.canRead()) { this.store.clear('denied'); return; }
    const loaded = await this.store.loadScenarioStage(prepared, stage);
    if (!this.canRead()) { this.store.clear('denied'); return; }
    if (loaded && generation === this.focusGeneration) this.open('scenario', true);
  }
  async exportScenarioAssumptions(prepared: PreparedScenario): Promise<void> {
    const generation = ++this.focusGeneration;
    if (!this.canRead()) { this.store.clear('denied'); return; }
    const text = await this.store.exportScenarioAssumptions(prepared);
    if (!this.canRead()) { this.store.clear('denied'); return; }
    if (text === null || generation !== this.focusGeneration || this.store.preparedScenario() !== prepared) return;
    const url = URL.createObjectURL(new Blob([text], { type: 'application/json;charset=utf-8' }));
    try { const link = this.document.createElement('a'); link.href = url; link.download = 'project-scenario-assumptions.json'; link.click(); }
    finally { URL.revokeObjectURL(url); }
  }
  async exportTestDefinition(prepared: PreparedTestEdit): Promise<void> {
    if (!this.canRead()) { this.store.clear('denied'); return; }
    const definition = await this.store.exportPreparedTestDefinition(prepared);
    if (!this.canRead()) { this.store.clear('denied'); return; }
    if (definition === null || this.store.preparedTestEdit() !== prepared) return;
    const url = URL.createObjectURL(new Blob([definition], { type: 'application/json;charset=utf-8' }));
    try {
      const link = this.document.createElement('a'); link.href = url; link.download = 'project-test-' + prepared.plan.target.entityId + '.json';
      link.click();
    } finally { URL.revokeObjectURL(url); }
  }
  async loadTestEdit(prepared: PreparedTestEdit): Promise<void> {
    const generation = ++this.focusGeneration;
    if (!this.canRead()) { this.store.clear('denied'); return; }
    const loaded = await this.store.loadPreparedTestEdit(prepared);
    if (!this.canRead()) { this.store.clear('denied'); return; }
    if (loaded && generation === this.focusGeneration) this.open('source', true);
  }
  async loadLocalTests(): Promise<void> {
    this.focusGeneration++;
    if (!this.canRead()) { this.store.clear('denied'); return; }
    await this.store.loadLocalTests();
    if (!this.canRead()) this.store.clear('denied');
  }
  navigateTestAssertion(assertion: LocalTestAssertion): void {
    this.focusGeneration++;
    if (!this.canRead()) this.store.clear('denied');
    else if (this.store.navigateTestAssertion(assertion)) this.open('source', true);
  }
  testName(identity: string): string {
    const value = this.store.context()?.declarations.find(item => item.identity === identity)?.data['displayName'];
    return typeof value === 'string' && value ? value : identity;
  }
  assertionName(assertion: LocalTestAssertion): string {
    return assertion.location.propertyPath.split('/')[3]?.replaceAll('~1', '/').replaceAll('~0', '~') ?? '';
  }
  testValue(value: string): string {
    const keys: Record<string, keyof typeof PROJECTIDE_ENGLISH_MESSAGES> = {
      'coglatas.pass': 'ide.testPass', 'coglatas.fail': 'ide.testFail', 'coglatas.inconclusive': 'ide.testInconclusive', 'coglatas.error': 'ide.testError', 'coglatas.not-run': 'ide.testNotRun',
      Pass: 'ide.testPass', Fail: 'ide.testFail', Inconclusive: 'ide.testInconclusive', Error: 'ide.testError', NotRun: 'ide.testNotRun',
      Completed: 'ide.testCompleted', Cancelled: 'ide.testCancelled', TimedOut: 'ide.testTimedOut', ResourceExhausted: 'ide.testExhausted', InternalError: 'ide.testError',
      Complete: 'ide.testComplete', Partial: 'ide.testPartial', None: 'ide.testNone', Current: 'ide.testCurrent', Stale: 'ide.testStale', Undetermined: 'ide.testUndetermined',
      'coglatas.model-proof': 'ide.testModelProof', 'coglatas.missing': 'ide.testMissing'
    };
    return keys[value] ? this.t(keys[value]) : this.t('ide.testUndetermined');
  }
  testReason(value: string): string {
    const keys: Record<string, keyof typeof PROJECTIDE_ENGLISH_MESSAGES> = {
      'coglatas.local-input-defaults': 'ide.testInputDefaults', 'coglatas.incomplete-test-basis': 'ide.testIncomplete', 'coglatas.zero-tests': 'ide.testZero',
      'coglatas.cancelled': 'ide.testCancelReason', 'coglatas.stale-input': 'ide.testStaleReason', 'coglatas.evaluator-identity-mismatch': 'ide.testEvaluatorReason',
      'coglatas.assertion-satisfied': 'ide.testSatisfied', 'coglatas.assertion-counterexample': 'ide.testCounterexample',
      'coglatas.unknown-actual': 'ide.testUnknown', 'coglatas.unsupported-test': 'ide.testUnsupported'
    };
    return keys[value] ? this.t(keys[value]) : value;
  }
  cancel(): void {
    this.focusGeneration++;
    this.store.clear('cancelled');
  }
  loadResult(): void {
    this.focusGeneration++;
    this.store.loadResult();
  }
  closeInspector(): void {
    this.store.clearSelection();
    this.open('overview');
    const generation = this.focusGeneration;
    setTimeout(() => {
      if (generation === this.focusGeneration) {
        this.inspectorButton()?.nativeElement.focus();
      }
    });
  }
  stateMessage(): string {
    const state = this.store.state();
    return this.t(
      state === 'loading'
        ? 'ide.loading'
        : state === 'denied'
          ? 'ide.denied'
          : state === 'cancelled'
            ? 'ide.cancelled'
            : state === 'wrong-scope'
              ? 'ide.wrong-scope'
              : state === 'unsupported'
                ? 'ide.unsupportedState'
                : state === 'unavailable'
                  ? 'ide.unavailable'
                  : 'ide.error',
    );
  }
}
