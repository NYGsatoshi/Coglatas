import { AdmittedRecordedDebug, RecordedDebugFrame, readRecordedDebug } from './projectide-recorded-debug.reader';
import { AdmittedScopedSources, ScopedSourceRegion, readScopedSources, resolveScopedSource, scopedSourcesMatch } from './projectide-scoped-sources';
import { AdmittedSimulation, SimulationEvent, readSimulationRun } from './projectide-simulation.reader';
import { AdmittedScenario, PreparedScenario, readPreparedScenario, scenarioMatches, scenarioTriplet } from './projectide-scenario';
import { PreparedTestEdit, readPreparedTestEdit, testEditPair } from './projectide-test-edit';
import { AdmittedLocalTests, LocalTestAssertion } from './projectide-tests.contracts';
import { localTestsMatchCase, readLocalTests } from './projectide-tests.reader';
import { computed, Injectable, signal } from '@angular/core';
import {
  BoundReference,
  Diagnostic,
  InspectedSymbol,
  PreviewBundle,
  PreviewCase,
  ProjectLocation,
  PreparedDraftRename,
  PreparedDraftRevert,
  SemanticChange,
  HistoryRevisionView,
  ReferenceCompletion,
  RequirementTaskSuggestions,
  ReadState,
  Selection,
  SourceRecord,
  sameContext,
} from './projectide.contracts';
import { PreviewReadError, readPreviewBundle, resolveLocation } from './projectide-preview.adapter';

/** Semantic selection/input generation; DOM panels, focus and filters stay in the component. */
@Injectable()
export class ProjectIdeWorkbenchStore {
  private static readonly referenceScanLimit = 4096;
  private static readonly referenceResultLimit = 32;
  private static readonly referenceOriginCheckLimit = 64;
  readonly state = signal<ReadState>('idle');
  readonly bundle = signal<PreviewBundle | null>(null);
  readonly context = signal<PreviewCase | null>(null);
  readonly result = signal<PreviewCase | null>(null);
  readonly selection = signal<Selection | null>(null);
  private readonly referenceSelection = signal<{
    reference: BoundReference;
    symbol: InspectedSymbol;
    result: PreviewCase;
    sourceMap: PreviewCase['sourceMap'];
  } | null>(null);
  private readonly currentReferenceSelection = () => {
    const selected = this.referenceSelection();
    return selected && selected.result === this.context() && selected.result === this.result() &&
      selected.sourceMap === selected.result.sourceMap && (!selected.result.result.artifacts.some(a => a.version === 2) || this.hasVerifiedPublication()) ? selected : null;
  };
  private referenceQuery: AbortController | null = null;
  private readonly search = signal<{
    result: PreviewCase;
    sourceMap: PreviewCase['sourceMap'];
    status: 'searching' | 'ready';
    references: readonly BoundReference[];
    coverageComplete: boolean;
    truncated: boolean;
  } | null>(null);
  readonly referenceSearch = () => {
    const search = this.search();
    return search && search.result === this.context() && search.result === this.result() &&
      search.result.sourceMap === search.sourceMap && (!search.result.result.artifacts.some(a => a.version === 2) || this.hasVerifiedPublication()) ? search : null;
  };
  readonly selectedReference = () => this.currentReferenceSelection()?.reference ?? null;
  readonly referenceSymbol = () => this.currentReferenceSelection()?.symbol ?? null;
  private readonly target = signal<{ record: SourceRecord; location: ProjectLocation; context: PreviewCase;
    result: PreviewCase | null; sourceMap: PreviewCase['sourceMap'] } | null>(null);
  private readonly currentTarget = computed(() => {
    const target = this.target();
    return target && target.context === this.context() && target.result === this.result() &&
      target.sourceMap === target.context.sourceMap &&
      !(this.referenceSelection() && !this.currentReferenceSelection()) ? target : null;
  });
  readonly sourceTarget = () => this.currentTarget() && (!this.context()?.result.artifacts.some(a => a.version === 2) || this.hasVerifiedPublication()) ? this.currentTarget()!.record : null;
  readonly sourceLocation = () => this.sourceTarget() ? this.currentTarget()!.location : null;
  readonly navigationState = signal('');
  readonly history = computed(() => {
    const context = this.context(); const bundle = this.bundle();
    if (this.state() !== 'ready' || !bundle || !context || context !== this.result() || !context.preparedHistory) return null;
    try { readPreviewBundle(bundle); return context.preparedHistory; } catch { return null; }
  });
  openHistoryRevision(revision: HistoryRevisionView): boolean {
    const history = this.history(); const bundle = this.bundle();
    if (!history || !bundle || !history.revisions.includes(revision)) return false;
    try { readPreviewBundle(bundle); } catch { this.clear('unsupported'); return false; }
    this.choose(revision.caseKey); this.loadResult(); return true;
  }
  readonly semanticDiff = computed(() => {
    const context = this.context(); const bundle = this.bundle();
    if (this.state() !== 'ready' || !bundle || !context || context !== this.result() || !context.preparedDiff ||
        !context.semanticInspection || !bundle.cases.find(item => item.key === context.preparedDiff!.afterCaseKey)?.semanticInspection) return null;
    try { readPreviewBundle(bundle); return context.preparedDiff; } catch { return null; }
  });
  readonly preparedRevert = computed(() => {
    const current = this.context(); const bundle = this.bundle();
    if (this.state() !== 'ready' || !bundle || !current || current !== this.result() || !current.preparedRevert) return null;
    try { readPreviewBundle(bundle); return current.preparedRevert; } catch { return null; }
  });
  loadPreparedRevert(prepared: PreparedDraftRevert): boolean {
    const bundle = this.bundle();
    if (!bundle || this.preparedRevert() !== prepared) return false;
    try { readPreviewBundle(bundle); } catch { this.clear('unsupported'); return false; }
    this.choose(prepared.candidateCaseKey); this.loadResult(); return true;
  }

  async navigateDiff(change: SemanticChange, side: 'before' | 'after'): Promise<boolean> {
    const prepared = this.semanticDiff(); const before = this.context(); const bundle = this.bundle();
    if (!prepared || !before || !bundle || !prepared.comparison.changes.includes(change)) return false;
    try { readPreviewBundle(bundle); } catch { this.clear('unsupported'); return false; }
    const target = side === 'before' ? before : bundle.cases.find(item => item.key === prepared.afterCaseKey);
    const location = side === 'before' ? change.beforeLocation : change.afterLocation;
    if (!target || !location || (!location.entityId && !location.relationId) || typeof resolveLocation(target, location) === 'string') return false;
    this.choose(target.key); this.loadResult();
    if (target.result.artifacts.some(artifact => artifact.version === 2) && !await this.loadScopedSources()) return false;
    if (this.bundle() !== bundle || this.context() !== target || this.result() !== target || this.state() !== 'ready') return false;
    try { readPreviewBundle(bundle); } catch { this.clear('unsupported'); return false; }
    return this.navigateLocation(location);
  }
  private scopedGeneration = 0;
  readonly scopedReadState = signal<'idle' | 'loading' | 'ready' | 'unavailable'>('idle');
  private readonly scopedSnapshot = signal<{ context: PreviewCase; admitted: AdmittedScopedSources } | null>(null);
  private readonly scopedTarget = signal<{ context: PreviewCase; admitted: AdmittedScopedSources; region: ScopedSourceRegion; record: SourceRecord; location: ProjectLocation } | null>(null);
  private admittedScoped(): AdmittedScopedSources | null {
    const snapshot = this.scopedSnapshot(); const context = this.context();
    return snapshot && context && this.state() === 'ready' && context === this.result() && snapshot.context === context &&
      scopedSourcesMatch(context, snapshot.admitted) ? snapshot.admitted : null;
  }
  readonly hasVerifiedPublication = (): boolean => this.admittedScoped()?.publicationVerified === true;
  private resolveCurrentLocation(value: PreviewCase, location: ProjectLocation, requireSourceMap = false) {
    if (value.result.artifacts.some(artifact => artifact.version === 2)) {
      if (!sameContext(location.context, value.context)) return 'wrong-scope' as const;
      const admitted = value === this.context() && value === this.result() ? this.admittedScoped() : null;
      return admitted?.publicationVerified ? resolveScopedSource(value, admitted, location)?.record ?? 'unavailable' : 'unavailable';
    }
    return resolveLocation(value, location, requireSourceMap);
  }
  readonly planDefinition = () => this.admittedScoped()?.plan ?? null;
  readonly planNotProducedReason = () => this.admittedScoped()?.planNotProducedReason ?? null;
  private simulationGeneration = 0;
  readonly simulationReadState = signal<'idle' | 'loading' | 'ready' | 'unavailable'>('idle');
  private readonly simulationSnapshot = signal<AdmittedSimulation | null>(null);
  readonly simulation = (): AdmittedSimulation | null => {
    const simulation = this.simulationSnapshot();
    return simulation && simulation.sources === this.admittedScoped() ? simulation : null;
  };
  async loadSimulation(): Promise<boolean> {
    this.clearSimulation(); const generation = this.simulationGeneration; const context = this.context(); const sources = this.admittedScoped();
    if (!context || !sources || context !== this.result()) return false;
    this.simulationReadState.set('loading');
    try {
      const run = await readSimulationRun(context, sources);
      if (generation !== this.simulationGeneration || context !== this.context() || context !== this.result()) return false;
      if (!run || sources !== this.admittedScoped()) throw new Error('simulation-unavailable');
      this.simulationSnapshot.set(run); this.simulationReadState.set('ready'); return true;
    } catch {
      if (generation === this.simulationGeneration && context === this.context() && context === this.result()) this.simulationReadState.set('unavailable');
      return false;
    }
  }
  navigateSimulationEvent(event: SimulationEvent): boolean {
    const run = this.simulation(); const context = this.context(); this.resetSelection();
    if (!run || !context || !run.events.includes(event)) { this.navigationState.set('unavailable'); return false; }
    const target = resolveScopedSource(context, run.sources, event.location);
    if (!target) { this.navigationState.set('unavailable'); return false; }
    this.scopedTarget.set({ context, admitted: run.sources, ...target, location: event.location }); this.navigationState.set('ready'); return true;
  }
  private recordedDebugGeneration = 0;
  readonly recordedDebugReadState = signal<'idle' | 'loading' | 'ready' | 'unavailable'>('idle');
  private readonly recordedDebugSnapshot = signal<AdmittedRecordedDebug | null>(null);
  private readonly recordedDebugIndex = signal(0);
  readonly recordedDebug = (): AdmittedRecordedDebug | null => {
    const debug = this.recordedDebugSnapshot(); return debug && debug.simulation === this.simulation() ? debug : null;
  };
  readonly recordedDebugFrame = (): RecordedDebugFrame | null => this.recordedDebug()?.frames[this.recordedDebugIndex()] ?? null;
  readonly recordedDebugPosition = () => this.recordedDebugFrame() ? this.recordedDebugIndex() : -1;
  async loadRecordedDebug(): Promise<boolean> {
    this.clearRecordedDebug(); this.resetSelection();
    const generation = this.recordedDebugGeneration; const context = this.context(); const sources = this.admittedScoped(); const run = this.simulation();
    if (!context || !sources || !run || context !== this.result()) return false;
    this.recordedDebugReadState.set('loading');
    try {
      const debug = await readRecordedDebug(context, sources, run);
      if (generation !== this.recordedDebugGeneration || context !== this.context() || context !== this.result() || run !== this.simulation()) return false;
      if (!debug) throw new Error('recorded-debug-unavailable');
      this.recordedDebugSnapshot.set(debug); this.recordedDebugReadState.set('ready'); return true;
    } catch {
      if (generation === this.recordedDebugGeneration && context === this.context() && context === this.result()) this.recordedDebugReadState.set('unavailable');
      return false;
    }
  }
  selectRecordedDebugFrame(debug: AdmittedRecordedDebug, index: number): boolean {
    if (debug !== this.recordedDebug() || !Number.isSafeInteger(index) || index < 0 || index >= debug.frames.length) return false;
    this.resetSelection(); this.recordedDebugIndex.set(index); return true;
  }
  navigateRecordedDebugLocation(frame: RecordedDebugFrame, location: ProjectLocation): boolean {
    const debug = this.recordedDebug(); const context = this.context(); this.resetSelection();
    if (!debug || !context || frame !== this.recordedDebugFrame() ||
        !(frame.pending?.['location'] === location || frame.inspection?.['location'] === location || frame.events.some(event => event.location === location))) {
      this.navigationState.set('unavailable'); return false;
    }
    const target = resolveScopedSource(context, debug.simulation.sources, location);
    if (!target) { this.navigationState.set('unavailable'); return false; }
    this.scopedTarget.set({ context, admitted: debug.simulation.sources, ...target, location }); this.navigationState.set('ready'); return true;
  }
  private clearRecordedDebug(): void {
    this.scopedTarget.set(null); this.recordedDebugGeneration++; this.recordedDebugSnapshot.set(null); this.recordedDebugIndex.set(0); this.recordedDebugReadState.set('idle');
  }
  private clearSimulation(): void { this.clearRecordedDebug(); this.simulationGeneration++; this.simulationSnapshot.set(null); this.simulationReadState.set('idle'); }
  readonly scopedSources = (): readonly ScopedSourceRegion[] => this.admittedScoped()?.regions ?? [];
  readonly scopedSourceTarget = () => {
    const target = this.scopedTarget();
    return target && target.context === this.context() && target.context === this.result() && this.admittedScoped() === target.admitted ? target : null;
  };
  async loadScopedSources(): Promise<boolean> {
    this.clearScopedSources(); this.resetSelection();
    const generation = this.scopedGeneration; const context = this.context();
    if (!context || context !== this.result() || this.state() !== 'ready') return false;
    this.scopedReadState.set('loading');
    try {
      const admitted = await readScopedSources(context);
      if (generation !== this.scopedGeneration || context !== this.context() || context !== this.result()) return false;
      if (!admitted || !scopedSourcesMatch(context, admitted)) throw new Error('scoped-input-unavailable');
      this.scopedSnapshot.set({ context, admitted }); this.scopedReadState.set('ready'); return true;
    } catch {
      if (generation === this.scopedGeneration && context === this.context() && context === this.result()) {
        this.scopedSnapshot.set(null); this.scopedTarget.set(null); this.scopedReadState.set('unavailable');
      }
      return false;
    }
  }
  navigateScopedSource(record: SourceRecord): boolean {
    const context = this.context(); const admitted = this.admittedScoped(); this.resetSelection();
    if (!context || !admitted || !admitted.regions.some(region => region.records.includes(record))) { this.navigationState.set('unavailable'); return false; }
    const target = resolveScopedSource(context, admitted, record.location);
    if (!target) { this.navigationState.set('unavailable'); return false; }
    this.scopedTarget.set({ context, admitted, ...target, location: record.location }); this.navigationState.set('ready'); return true;
  }
  navigateScopedDiagnostic(region: ScopedSourceRegion, diagnostic: Diagnostic): boolean {
    const context = this.context(); const admitted = this.admittedScoped(); this.resetSelection();
    if (!context || !admitted || !admitted.regions.includes(region) || !region.diagnostics.includes(diagnostic)) { this.navigationState.set('unavailable'); return false; }
    const target = resolveScopedSource(context, admitted, diagnostic.primaryLocation);
    if (!target) { this.navigationState.set('unavailable'); return false; }
    this.scopedTarget.set({ context, admitted, ...target, location: diagnostic.primaryLocation }); this.navigationState.set('ready'); return true;
  }
  private clearScopedSources(): void {
    this.clearSimulation(); this.scopedGeneration++; this.scopedSnapshot.set(null); this.scopedTarget.set(null); this.scopedReadState.set('idle');
  }
  private scenarioGeneration = 0;
  readonly scenarioReadState = signal<'idle' | 'loading' | 'ready' | 'unavailable'>('idle');
  private readonly scenarioSnapshot = signal<{ bundle: PreviewBundle; context: PreviewCase; admitted: AdmittedScenario } | null>(null);
  readonly preparedScenario = (): PreparedScenario | null => {
    const context = this.context(); const bundle = this.bundle();
    if (!context || !bundle || this.state() !== 'ready' || context !== this.result()) return null;
    try { return scenarioTriplet(bundle, context)?.prepared ?? null; } catch { return null; }
  };
  readonly scenario = (): AdmittedScenario | null => {
    const snapshot = this.scenarioSnapshot(); const context = this.context(); const bundle = this.bundle();
    return snapshot && bundle && context && this.state() === 'ready' && snapshot.context === context && context === this.result() &&
      snapshot.bundle === bundle && scenarioMatches(bundle, context, snapshot.admitted) ? snapshot.admitted : null;
  };
  private async admitScenario(prepared: PreparedScenario) {
    const generation = this.scenarioGeneration; const bundle = this.bundle(); const context = this.context();
    if (!bundle || !context || this.preparedScenario() !== prepared) return null;
    this.scenarioReadState.set('loading');
    try {
      const admitted = await readPreparedScenario(bundle, context);
      if (generation !== this.scenarioGeneration || bundle !== this.bundle() || context !== this.context() || context !== this.result()) return null;
      if (!admitted || this.preparedScenario() !== prepared || !scenarioMatches(bundle, context, admitted)) throw new Error('stale-scenario');
      return admitted;
    } catch {
      if (generation === this.scenarioGeneration && context === this.context()) { this.scenarioSnapshot.set(null); this.scenarioReadState.set('unavailable'); }
      return null;
    }
  }
  async loadScenarioComparison(prepared: PreparedScenario): Promise<boolean> {
    const generation = this.scenarioGeneration; const bundle = this.bundle(); const context = this.context();
    const admitted = await this.admitScenario(prepared);
    if (!admitted || !bundle || !context || generation !== this.scenarioGeneration || bundle !== this.bundle() || context !== this.context() ||
        context !== this.result() || this.preparedScenario() !== prepared || !scenarioMatches(bundle, context, admitted)) return false;
    this.scenarioSnapshot.set({ bundle, context, admitted }); this.scenarioReadState.set('ready'); return true;
  }
  async exportScenarioAssumptions(prepared: PreparedScenario): Promise<string | null> {
    const generation = this.scenarioGeneration; const bundle = this.bundle(); const context = this.context();
    const admitted = await this.admitScenario(prepared);
    if (!admitted || !bundle || !context || generation !== this.scenarioGeneration || bundle !== this.bundle() || context !== this.context() ||
        context !== this.result() || this.preparedScenario() !== prepared || !scenarioMatches(bundle, context, admitted)) return null;
    this.scenarioReadState.set(this.scenario() ? 'ready' : 'idle'); return prepared.overlay.text;
  }
  async loadScenarioStage(prepared: PreparedScenario, stage: 'baseline' | 'scenario' | 'draft'): Promise<boolean> {
    const generation = this.scenarioGeneration; const bundle = this.bundle(); const context = this.context();
    const admitted = await this.admitScenario(prepared);
    if (!admitted || !bundle || !context || generation !== this.scenarioGeneration || bundle !== this.bundle() || context !== this.context() ||
        context !== this.result() || this.preparedScenario() !== prepared || !scenarioMatches(bundle, context, admitted)) return false;
    if (stage === 'scenario' && context !== admitted.baseline || stage === 'draft' && context !== admitted.scenario) { this.scenarioReadState.set('ready'); return false; }
    const target = admitted[stage]; this.choose(target.key); this.loadResult();
    if (this.context() !== target || this.result() !== target || this.bundle() !== bundle || !scenarioMatches(bundle, target, admitted)) return false;
    this.scenarioSnapshot.set({ bundle, context: target, admitted }); this.scenarioReadState.set('ready'); return true;
  }
  private clearScenario(): void { this.scenarioGeneration++; this.scenarioSnapshot.set(null); this.scenarioReadState.set('idle'); }
  readonly testEditState = signal<'idle' | 'loading' | 'unavailable'>('idle');
  readonly preparedTestEdit = (): PreparedTestEdit | null => {
    const context = this.context(); const bundle = this.bundle();
    if (!context || !bundle || this.state() !== 'ready' || context !== this.result()) return null;
    try { return testEditPair(bundle, context)?.prepared ?? null; } catch { return null; }
  };
  private async admitTestEdit(prepared: PreparedTestEdit) {
    const generation = this.testGeneration; const context = this.context(); const bundle = this.bundle();
    if (!context || !bundle || this.preparedTestEdit() !== prepared) return null;
    this.testEditState.set('loading');
    try {
      readPreviewBundle(bundle); const result = await readPreparedTestEdit(bundle, context);
      if (generation !== this.testGeneration || context !== this.context() || context !== this.result() || bundle !== this.bundle()) return null;
      readPreviewBundle(bundle);
      if (!result || result.prepared !== prepared || this.preparedTestEdit() !== prepared || !bundle.cases.includes(result.after)) throw new Error('stale-test-edit');
      this.testEditState.set('idle'); return result;
    } catch {
      if (generation === this.testGeneration && context === this.context()) this.testEditState.set('unavailable');
      return null;
    }
  }
  async exportPreparedTestDefinition(prepared: PreparedTestEdit): Promise<string | null> {
    const generation = this.testGeneration; const bundle = this.bundle(); const context = this.context();
    const result = await this.admitTestEdit(prepared);
    return result && generation === this.testGeneration && bundle === this.bundle() && context === this.context() &&
      context === this.result() && this.preparedTestEdit() === prepared ? result.definitionText : null;
  }
  async loadPreparedTestEdit(prepared: PreparedTestEdit): Promise<boolean> {
    const generation = this.testGeneration; const bundle = this.bundle(); const context = this.context();
    const result = await this.admitTestEdit(prepared);
    if (!result || generation !== this.testGeneration || bundle !== this.bundle() || context !== this.context() ||
        context !== this.result() || this.preparedTestEdit() !== prepared) return false;
    this.choose(result.after.key); this.loadResult(); return true;
  }
  private testGeneration = 0;
  readonly testReadState = signal<'idle' | 'loading' | 'ready' | 'unavailable' | 'unsupported'>('idle');
  private readonly testSnapshot = signal<{ context: PreviewCase; result: PreviewCase; admitted: AdmittedLocalTests; sourceMap: PreviewCase['sourceMap']; source: PreviewCase['source']; manifest: PreviewCase['manifest']; declarations: PreviewCase['declarations'] } | null>(null);
  readonly localTests = (): AdmittedLocalTests | null => {
    const snapshot = this.testSnapshot();
    return snapshot && this.state() === 'ready' && snapshot.context === this.context() && snapshot.result === this.result() && snapshot.sourceMap === snapshot.context.sourceMap && snapshot.source === snapshot.context.source &&
      snapshot.manifest === snapshot.context.manifest && snapshot.declarations === snapshot.context.declarations && localTestsMatchCase(snapshot.context, snapshot.admitted) ? snapshot.admitted : null;
  };
  async loadLocalTests(): Promise<boolean> {
    this.clearLocalTests(); this.resetSelection();
    const generation = this.testGeneration; const context = this.context(); const bundle = this.bundle();
    if (!context || !bundle || context !== this.result() || this.state() !== 'ready') {
      this.testReadState.set('unavailable'); return false;
    }
    const materialization = { sourceMap: context.sourceMap, source: context.source, manifest: context.manifest, declarations: context.declarations };
    this.testReadState.set('loading');
    try {
      readPreviewBundle(bundle);
      const admitted = await readLocalTests(context);
      if (generation !== this.testGeneration || context !== this.context() || context !== this.result() || bundle !== this.bundle()) return false;
      if (materialization.sourceMap !== context.sourceMap || materialization.source !== context.source || materialization.manifest !== context.manifest || materialization.declarations !== context.declarations) {
        this.clearLocalTests(); this.testReadState.set('unavailable'); return false;
      }
      readPreviewBundle(bundle);
      if (!admitted) { this.testReadState.set('unavailable'); return false; }
      if (!localTestsMatchCase(context, admitted)) throw new Error('stale-local-tests');
      this.testSnapshot.set({ context, result: context, admitted, ...materialization }); this.testReadState.set('ready'); return true;
    } catch {
      if (generation === this.testGeneration && context === this.context() && context === this.result()) {
        this.testSnapshot.set(null); this.testReadState.set('unsupported');
      }
      return false;
    }
  }
  navigateTestAssertion(assertion: LocalTestAssertion): boolean {
    const admitted = this.localTests(); this.resetSelection();
    if (!admitted || !admitted.run.tests.some(test => test.assertions.includes(assertion))) { this.navigationState.set('unavailable'); return false; }
    try { readPreviewBundle(this.bundle()); } catch { this.clear('unsupported'); return false; }
    if (!this.localTests()) { this.clearLocalTests(); this.navigationState.set('stale'); return false; }
    return this.navigateLocation(assertion.location);
  }
  private clearLocalTests(): void { this.clearScenario(); this.testEditState.set('idle'); this.testGeneration++; this.testSnapshot.set(null); this.testReadState.set('idle'); }
  private generation = 0;
  private controller: AbortController | null = null;
  readonly diagnostic = computed(() => {
    const selected = this.selection();
    if (selected?.kind !== 'diagnostic' || this.result() !== this.context()) return null;
    const matches = this.result()?.result.diagnostics.filter((item) => item.diagnosticId === selected.id);
    return matches?.length === 1 ? matches[0] : null;
  });
  readonly inspectedRecord = computed(() => {
    const context = this.context();
    const selected = this.selection();
    if (!context || !selected) return null;
    if (selected.kind === 'diagnostic') {
      const diagnostic = this.diagnostic();
      if (!diagnostic) return null;
      const target = resolveLocation(context, diagnostic.primaryLocation);
      return typeof target === 'string' ? null : target;
    }
    return [...context.declarations, ...context.relations].find(
      (record) => record.recordType === selected.kind && record.identity === selected.id,
    ) ?? null;
  });
  /** Related findings belong to the accepted result and exact Inspector source record. */
  readonly relatedDiagnostics = computed(() => {
    const context = this.context();
    const record = this.inspectedRecord();
    if (!context || this.result() !== context || !record ||
        typeof resolveLocation(context, record.location) === 'string') return [];
    return context.result.diagnostics.filter(({ primaryLocation: location }) =>
      sameContext(location.context, record.location.context) &&
      location.documentId === record.location.documentId &&
      location.entityId === record.location.entityId &&
      location.relationId === record.location.relationId,
    );
  });
  private readonly recordSymbols = computed(() => {
    const record = this.inspectedRecord();
    return record?.recordType === 'entity' && this.result() === this.context() && this.result()?.freshness === 'Current' ? (this.result()?.semanticInspection?.symbols ?? []).filter((symbol) =>
      symbol.location.entityId === record.identity && symbol.location.documentId === record.location.documentId) : [];
  });
  readonly diagnosticSymbol = () => {
    const diagnostic = this.diagnostic();
    if (!diagnostic || !(this.result()?.sourceMap || this.hasVerifiedPublication())) return null;
    const location = diagnostic.primaryLocation;
    const matches = this.recordSymbols().filter(({ location: symbol }) =>
      symbol.propertyPath === location.propertyPath &&
      symbol.textSpan?.start === location.textSpan?.start &&
      symbol.textSpan?.length === location.textSpan?.length,
    );
    return matches.length === 1 ? matches[0] : null;
  };
  readonly inspectedSymbols = () => {
    if (this.result()?.result.artifacts.some(a => a.version === 2) && !this.hasVerifiedPublication()) return [];
    if (this.selection()?.kind !== 'diagnostic') return this.recordSymbols();
    const symbol = this.diagnosticSymbol();
    return symbol ? [symbol] : [];
  };
  readonly inspectedReferences = () => {
    const keys = new Set(this.inspectedSymbols().map((symbol) => symbol.key));
    return (this.result()?.semanticInspection?.references ?? []).filter((reference) => reference.symbolKey !== null && keys.has(reference.symbolKey));
  };
  readonly referenceCompletions = () => {
    const result = this.result();
    if (!result || result !== this.context() || result.freshness !== 'Current' || !(result.sourceMap || this.hasVerifiedPublication())) return [];
    const origins = new Set(this.inspectedSymbols().map((symbol) => symbol.key));
    return (result.referenceCompletions ?? []).filter((page) => origins.has(page.originKey) && page.state === 'Ready');
  };
  readonly requirementTaskSuggestions = () => {
    const result = this.result();
    if (!result || result !== this.context() || result.freshness !== 'Current' || !(result.sourceMap || this.hasVerifiedPublication())) return [];
    const origins = new Set(this.inspectedSymbols().filter(symbol => symbol.kind === 'coglatas.requirement').map(symbol => symbol.key));
    return (result.requirementTaskSuggestions ?? []).filter(page => origins.has(page.originKey) && page.state === 'Ready' && page.candidateOnly);
  };
  readonly preparedRename = () => {
    const result = this.result(); const record = this.inspectedRecord(); const prepared = result?.preparedRename;
    if (!result || !result.semanticInspection || result !== this.context() || result.freshness !== 'Current' || !record || !prepared ||
        record.recordType !== 'entity' || record.identity !== prepared.plan.target.entityId ||
        record.location.documentId !== prepared.plan.target.documentId ||
        typeof this.resolveCurrentLocation(result, prepared.plan.target, true) === 'string') return null;
    return prepared;
  };
  loadPreparedRename(prepared: PreparedDraftRename): boolean {
    const bundle = this.bundle(); const result = this.result(); const generation = this.generation;
    if (!bundle || !result || this.preparedRename() !== prepared) return false;
    try { readPreviewBundle(bundle); } catch { return false; }
    const after = bundle.cases.find(item => item.key === prepared.afterCaseKey);
    if (!after) return false;
    this.resetSelection();
    if (this.generation !== generation || this.bundle() !== bundle || this.context() !== result || this.result() !== result) return false;
    this.clearLocalTests(); this.clearScopedSources(); this.result.set(null); this.context.set(after); this.result.set(after);
    return true;
  }
  navigateCompletion(page: ReferenceCompletion, candidate: ReferenceCompletion['candidates'][number]): boolean {
    this.target.set(null);
    const result = this.result();
    const origin = result?.semanticInspection?.symbols.find((symbol) => symbol.key === page.originKey);
    const symbol = result?.semanticInspection?.symbols.find((symbol) => symbol.key === candidate.symbolKey);
    if (!result || !this.referenceCompletions().includes(page) || !page.candidates.includes(candidate) || !origin || !symbol ||
        typeof this.resolveCurrentLocation(result, origin.location, true) === 'string') {
      this.navigationState.set('unavailable'); return false;
    }
    return this.navigateLocation(symbol.location, true);
  }
  navigateTaskSuggestion(page: RequirementTaskSuggestions, candidate: RequirementTaskSuggestions['candidates'][number]): boolean {
    this.target.set(null);
    const result = this.result();
    const origin = result?.semanticInspection?.symbols.find(symbol => symbol.key === page.originKey);
    const symbol = result?.semanticInspection?.symbols.find(symbol => symbol.key === candidate.symbolKey);
    const record = this.inspectedRecord();
    if (!result || !this.requirementTaskSuggestions().includes(page) || !page.candidates.includes(candidate) || !origin || !symbol ||
        record?.recordType !== 'entity' || record.kind !== 'coglatas.requirement' || origin.location.entityId !== record.identity ||
        origin.location.documentId !== record.location.documentId || origin.location.propertyPath !== '' ||
        typeof this.resolveCurrentLocation(result, origin.location, true) === 'string') {
      this.navigationState.set('unavailable'); return false;
    }
    return this.navigateLocation(symbol.location, true);
  }
  readonly referenceRows = () => {
    const result = this.result();
    const record = this.inspectedRecord();
    if (!record || result !== this.context() || result?.freshness !== 'Current' || result.result.artifacts.some(a => a.version === 2) && !this.hasVerifiedPublication()) return [];
    const incoming = new Set(this.inspectedReferences());
    return (result.semanticInspection?.references ?? []).filter(({ origin, state, symbolKey }, index, references) =>
      state === 'Resolved' && symbolKey !== null && (incoming.has(references[index]) ||
        (this.selection()?.kind !== 'diagnostic' && origin.documentId === record.location.documentId &&
          (record.recordType === 'entity' ? origin.entityId : origin.relationId) === record.identity)),
    );
  };
  async load(read: (signal: AbortSignal) => Promise<PreviewBundle>): Promise<void> {
    const generation = this.generation + 1;
    this.clear('loading');
    // Abort subscribers may synchronously clear this context or begin a newer read.
    if (generation !== this.generation) {
      return;
    }
    const controller = new AbortController();
    this.controller = controller;
    try {
      const bundle = await read(controller.signal);
      if (controller.signal.aborted || generation !== this.generation) {
        return;
      }
      this.bundle.set(bundle);
      this.state.set('ready');
    } catch (error) {
      if (controller.signal.aborted || generation !== this.generation) {
        return;
      }
      this.state.set(error instanceof PreviewReadError ? error.state : 'error');
    }
  }
  choose(key: string): void {
    this.clearScopedSources();
    this.clearLocalTests();
    this.resetSelection();
    this.result.set(null);
    this.context.set(this.bundle()?.cases.find((c) => c.key === key) ?? null);
  }
  loadResult(): void {
    this.clearScopedSources();
    this.clearLocalTests();
    this.resetSelection();
    this.result.set(this.context());
  }
  selectDiagnostic(diagnostic: Diagnostic): boolean {
    if (this.result()?.result.diagnostics.includes(diagnostic)) {
      return this.select({ kind: 'diagnostic', id: diagnostic.diagnosticId });
    }
    this.resetSelection();
    this.navigationState.set('unavailable');
    return false;
  }
  select(selection: Selection): boolean {
    this.resetSelection();
    const context = this.context();
    const diagnostics = this.result() === context ? context?.result.diagnostics ?? [] : [];
    const matches = selection.kind === 'diagnostic'
      ? diagnostics.filter((item) => item.diagnosticId === selection.id)
      : [...(context?.declarations ?? []), ...(context?.relations ?? [])].filter(
        (item) => item.recordType === selection.kind && item.identity === selection.id,
      );
    if (matches.length !== 1) {
      this.navigationState.set('unavailable');
      return false;
    }
    this.selection.set(selection);
    return true;
  }
  async findReferences(symbol: InspectedSymbol): Promise<boolean> {
    this.cancelReferenceSearch();
    this.referenceSelection.set(null);
    this.target.set(null);
    this.navigationState.set('');
    const result = this.result();
    const inspection = result?.semanticInspection;
    if (!result || result !== this.context() || !inspection || !this.inspectedSymbols().includes(symbol) ||
        symbol.status !== 'Decoded' || inspection.symbols.filter((item) => item.key === symbol.key).length !== 1) {
      this.navigationState.set('unavailable');
      return false;
    }
    const definition = this.resolveCurrentLocation(result, symbol.location, true);
    if (typeof definition === 'string') {
      this.navigationState.set(definition);
      return false;
    }

    const controller = new AbortController();
    this.referenceQuery = controller;
    const sourceMap = result.sourceMap;
    this.search.set({ result, sourceMap, status: 'searching', references: [], coverageComplete: false, truncated: false });
    const references: BoundReference[] = [];
    let coverageComplete = inspection.bindingComplete && result.result.coverage === 'coglatas.complete';
    const scanLimit = ProjectIdeWorkbenchStore.referenceScanLimit;
    let truncated = inspection.references.length > scanLimit;
    let originChecks = 0;
    for (let i = 0; i < Math.min(inspection.references.length, scanLimit); i++) {
      // Yield to context changes and cancellation even when no references match.
      if (i % 32 === 0) {
        await new Promise<void>((resolve) => setTimeout(resolve, 0));
        if (controller.signal.aborted) return false;
        if (!this.referenceSearch()) {
          this.cancelReferenceSearch();
          return false;
        }
      }
      const reference = inspection.references[i];
      if (reference.state !== 'Resolved' || reference.symbolKey !== symbol.key) continue;
      if (references.length === ProjectIdeWorkbenchStore.referenceResultLimit ||
          originChecks === ProjectIdeWorkbenchStore.referenceOriginCheckLimit) {
        truncated = true;
        break;
      }
      originChecks++;
      if (typeof this.resolveCurrentLocation(result, reference.origin, true) === 'string') {
        coverageComplete = false;
        continue;
      }
      references.push(reference);
    }
    this.referenceQuery = null;
    this.search.set({ result, sourceMap, status: 'ready', references, coverageComplete: coverageComplete && !truncated, truncated });
    return true;
  }
  cancelReferenceSearch(): void {
    this.referenceQuery?.abort();
    this.referenceQuery = null;
    this.search.set(null);
  }
  selectReference(reference: BoundReference): boolean {
    if (this.search()?.status === 'searching') this.cancelReferenceSearch();
    // Keep the inspector anchor when changing the reference detail.
    this.referenceSelection.set(null);
    this.target.set(null);
    const result = this.result();
    const symbol = this.resolveReference(reference);
    if (typeof symbol === 'string' || !result) {
      this.navigationState.set(typeof symbol === 'string' ? symbol : 'unavailable');
      return false;
    }
    this.referenceSelection.set({ reference, symbol, result, sourceMap: result.sourceMap });
    this.navigationState.set('');
    return true;
  }
  navigateReference(destination: 'origin' | 'definition'): boolean {
    const reference = this.selectedReference();
    const symbol = reference ? this.resolveReference(reference) : this.referenceSelection() ? 'stale' : 'unavailable';
    if (typeof symbol === 'string' || !reference) {
      this.target.set(null);
      this.referenceSelection.set(null);
      this.navigationState.set(typeof symbol === 'string' ? symbol : 'unavailable');
      return false;
    }
    return this.navigateLocation(destination === 'origin' ? reference.origin : symbol.location, true);
  }
  private resolveReference(reference: BoundReference): InspectedSymbol | string {
    const result = this.result();
    if (!result || result !== this.context() || result.freshness !== 'Current') return 'stale';
    const inspection = result.semanticInspection;
    if (!inspection || !inspection.references.includes(reference) ||
        reference.state !== 'Resolved' || reference.symbolKey === null) return 'unavailable';
    const matches = inspection.symbols.filter((symbol) => symbol.key === reference.symbolKey);
    if (matches.length !== 1 || matches[0].status !== 'Decoded') return 'unavailable';
    const origin = this.resolveCurrentLocation(result, reference.origin, true);
    if (typeof origin === 'string') return origin;
    const definition = this.resolveCurrentLocation(result, matches[0].location, true);
    return typeof definition === 'string' ? definition : matches[0];
  }
  clearSelection(): void {
    this.resetSelection();
  }
  navigate(): boolean {
    const context = this.context();
    if (this.selection()?.kind === 'diagnostic' && this.result() !== context) {
      this.resetSelection();
      this.navigationState.set('stale');
      return false;
    }
    return this.navigateLocation(this.diagnostic()?.primaryLocation ?? this.inspectedRecord()?.location);
  }
  navigateDiagnosticSymbol(): boolean {
    return this.navigateLocation(this.diagnosticSymbol()?.location);
  }
  private navigateLocation(location: ProjectLocation | undefined, requireSourceMap = false): boolean {
    this.scopedTarget.set(null);
    this.target.set(null);
    const context = this.context();
    if (!context || !location) {
      this.navigationState.set('unavailable');
      return false;
    }
    const target = this.resolveCurrentLocation(context, location, requireSourceMap);
    if (typeof target === 'string') {
      this.navigationState.set(target);
      return false;
    }
    this.target.set({ record: target, location, context, result: this.result(), sourceMap: context.sourceMap });
    this.navigationState.set('ready');
    return true;
  }
  clear(state: ReadState = 'idle'): void {
    this.clearScopedSources();
    this.clearLocalTests();
    this.generation++;
    const controller = this.controller;
    this.controller = null;
    this.bundle.set(null);
    this.context.set(null);
    this.result.set(null);
    this.resetSelection();
    this.state.set(state);
    // Publish the cleared lifecycle before invoking synchronous abort subscribers.
    controller?.abort();
  }
  private resetSelection(): void {
    this.scopedTarget.set(null);
    this.cancelReferenceSearch();
    this.selection.set(null);
    this.referenceSelection.set(null);
    this.target.set(null);
    this.navigationState.set('');
  }
}
